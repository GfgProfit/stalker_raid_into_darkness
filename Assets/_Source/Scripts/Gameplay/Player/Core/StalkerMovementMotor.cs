using System;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [ExecuteAlways]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(StalkerStanceController))]
    [RequireComponent(typeof(StalkerActorCondition))]
    [DisallowMultipleComponent]
    public sealed class StalkerMovementMotor : MonoBehaviour
    {
        private const float EPS = 0.0000001f;
        private const float LADDER_SEARCH_RADIUS = 1.5f;
        private const float LADDER_LOOK_DOWN = -Mathf.PI / 20.0f;
        private const float LADDER_STOP_DISTANCE = 0.1f;
        private const float WALL_PROBE_DISTANCE = 0.01f;
        private const float NEAR_UP_TIME = 0.75f;
        private const float NEAR_UP_CLEARANCE = 0.02f;
        private const float LEFT_GROUND_DELAY = 0.05f;

        private static readonly Collider[] QUERY_BUFFER = new Collider[16];

        [SerializeField] private StalkerMovementProfile _profile;

        private CharacterController _controller;
        private StalkerStanceController _stance;
        private StalkerActorCondition _condition;
        private StalkerCharacterPhysics _physics;
        private StalkerMoveFlags _wish;
        private StalkerMoveFlags _real;
        private StalkerMoveFlags _old;
        private Vector3 _controlAccel;
        private Vector3 _localMove;
        private float _jump;
        private float _fallTime;
        private float _jumpTime;
        private float _landingTime;
        private bool _jumpKeyPressed;
        private float _yaw;
        private Vector3 _cameraDirection = Vector3.forward;
        private float _actualVelocity;
        private float _airTime;
        private bool _leftGroundRaised;
        private float _lastContactSpeed;
        private StalkerLadder _ladder;
        private bool _climbingUp;

        public event Action JumpStarted;
        public event Action LeftGround;
        public event Action StartedFalling;
        public event Action<float> Landed;
        public event Action<float> ContactRestored;

        public StalkerMovementProfile Profile => _profile;
        public StalkerMoveFlags RealFlags => _real;
        public StalkerMoveFlags Wish
        {
            get => _wish;
            set => _wish = value;
        }

        public Vector3 Velocity => _physics != null ? _physics.Velocity : Vector3.zero;
        public float ActualVelocity => _actualVelocity;
        public float LastContactSpeed => _lastContactSpeed;
        public StalkerEnvironment Environment => _physics != null ? _physics.Environment : StalkerEnvironment.OnGround;
        public StalkerCharacterPhysics Physics => _physics;
        public StalkerActorCondition Condition => _condition;
        public StalkerStanceController Stance => _stance;
        public StalkerLadder Ladder => _ladder;
        public Vector3 ControlAcceleration => _controlAccel;

        public bool IsSlowedDown => _condition.RunCoef <= _profile.VisibleBody.SlowedRunCoef || _condition.SprintKoef <= _profile.VisibleBody.SlowedSprintKoef;

        public StalkerMovementState State
        {
            get
            {
                return new StalkerMovementState(_real, Environment, Velocity, _localMove, _stance.CurrentBox, IsSlowedDown, _actualVelocity, _physics != null ? _physics.MaxVelocity : 0.0f);
            }
        }

        public void Initialize(StalkerMovementProfile profile)
        {
            _profile = profile;
            _controller = GetComponent<CharacterController>();
            _stance = GetComponent<StalkerStanceController>();
            _condition = GetComponent<StalkerActorCondition>();
            _controller.skinWidth = profile.Unity.SkinWidth;
            _controller.slopeLimit = profile.Unity.SlopeLimit;
            _controller.minMoveDistance = 0.0f;
            _stance.Initialize(profile);
            _condition.Initialize(profile);
            _physics = new StalkerCharacterPhysics(profile);
            _fallTime = profile.Engine.FallTime;
            _jumpTime = 0.0f;
            _real = StalkerMoveFlags.None;
        }

        public void SetView(float yaw, Vector3 cameraDirection)
        {
            _yaw = yaw;
            _cameraDirection = cameraDirection;
        }

        public void Teleport(Vector3 position)
        {
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = true;
            _physics.SetVelocity(Vector3.zero);
        }

        public void Tick(float deltaTime)
        {
            if (_physics == null || deltaTime <= 0.0f)
            {
                return;
            }

            CheckControls(_wish, deltaTime);
            _physics.SetControl(_controlAccel);
            if (_jump > 0.0f)
            {
                _physics.RequestJump();
                _jump = 0.0f;
            }

            float fixedStep = _profile.Engine.FixedStep;
            float remaining = deltaTime;
            while (remaining > EPS)
            {
                float step = Mathf.Min(fixedStep, remaining);
                PhysicsStep(step);
                remaining -= step;
            }

            _actualVelocity = _physics.Velocity.magnitude;
            if (_physics.Contacts.Ground || _physics.Climbing)
            {
                _airTime = 0.0f;
                _leftGroundRaised = false;
            }
            else
            {
                _airTime += deltaTime;
                if (!_leftGroundRaised && _airTime >= LEFT_GROUND_DELAY)
                {
                    _leftGroundRaised = true;
                    LeftGround?.Invoke();
                }
            }

            ValidateState(_wish, deltaTime);
            _condition.Tick(_real, _physics.Environment == StalkerEnvironment.InAir, transform.position.y, deltaTime);
            _stance.UpdateCameraHeight(deltaTime);
        }

        public void ClearHeldCommands()
        {
            StalkerInputSettings input = _profile.Input;
            _wish &= ~StalkerMoveFlags.AnyMove;
            if (!input.CrouchToggle)
            {
                _wish &= ~StalkerMoveFlags.Crouch;
            }

            if (!input.WalkToggle)
            {
                _wish &= ~StalkerMoveFlags.Accel;
            }

            if (!input.SprintToggle)
            {
                _wish &= ~StalkerMoveFlags.Sprint;
            }
        }

        private void PhysicsStep(float step)
        {
            Vector3 feet = transform.position;
            UpdateLadder(feet);
            _physics.ClambJump = _profile.Unity.EnableLedgeClimb && _physics.IsControl && !_physics.LoseControl && !_physics.Climbing && _physics.Contacts.Wall && ValidateWalkOnMesh(feet);
            Vector3 displacement = _physics.Step(feet, step);
            _physics.ResetContacts();
            _controller.Move(displacement);
            ProbeWall();
        }

        private void ProbeWall()
        {
            if (!_physics.IsControl || _physics.Climbing)
            {
                return;
            }

            var direction = new Vector3(_physics.Acceleration.x, 0.0f, _physics.Acceleration.z);
            if (direction.sqrMagnitude <= 0.0f)
            {
                return;
            }

            float radius = _controller.radius;
            Vector3 center = transform.TransformPoint(_controller.center);
            Vector3 offset = Vector3.up * Mathf.Max(_controller.height * 0.5f - radius, 0.0f);
            if (UnityEngine.Physics.CapsuleCast(center + offset, center - offset, radius, direction.normalized, out RaycastHit hit, _controller.skinWidth + WALL_PROBE_DISTANCE, _profile.Unity.CollisionMask, QueryTriggerInteraction.Ignore) && hit.collider != _controller && !hit.collider.transform.IsChildOf(transform))
            {
                _physics.AddContact(hit.normal);
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (_physics != null)
            {
                _physics.AddContact(hit.normal);
            }
        }

        private bool CanMove()
        {
            return !_condition.IsCantWalk() && !_condition.IsCantWalkWeight();
        }

        private bool CanAccelerate()
        {
            return !_condition.IsLimping();
        }

        private bool CanSprint()
        {
            bool canSprint = CanAccelerate() && !_condition.IsCantSprint() && _condition.SprintAllowed;
            if (_real.Has(StalkerMoveFlags.LeftStrafe | StalkerMoveFlags.RightStrafe) && !_real.Has(StalkerMoveFlags.Forward))
            {
                canSprint = false;
            }

            return canSprint;
        }

        private bool CanJump()
        {
            return !_condition.IsCantSprint() && !_real.Has(StalkerMoveFlags.Jump) && _jumpTime <= 0.0f && !_jumpKeyPressed;
        }

        private void CheckControls(StalkerMoveFlags wish, float deltaTime)
        {
            StalkerEngineValues engine = _profile.Engine;
            StalkerActorValues actor = _profile.Actor;
            _old = _real;
            Vector3 accel = Vector3.zero;
            StalkerEnvironment environment = _physics.Environment;
            if (!_real.Has(StalkerMoveFlags.Fall) && environment == StalkerEnvironment.InAir)
            {
                _fallTime -= deltaTime;
                if (_fallTime <= 0.0f)
                {
                    _fallTime = engine.FallTime;
                    _real |= StalkerMoveFlags.Fall;
                    _real &= ~StalkerMoveFlags.Jump;
                    StartedFalling?.Invoke();
                }
            }

            if (!CanMove() && wish.Has(StalkerMoveFlags.AnyMove))
            {
                _wish &= ~StalkerMoveFlags.AnyMove;
                _real &= ~StalkerMoveFlags.AnyMove;
                wish &= ~(StalkerMoveFlags.AnyMove | StalkerMoveFlags.Jump);
            }

            float strafe = wish.Has(StalkerMoveFlags.Sprint) ? engine.SprintAirStrafeInput : 1.0f;
            if (wish.Has(StalkerMoveFlags.Forward))
            {
                accel.z += 1.0f;
            }

            if (wish.Has(StalkerMoveFlags.Back))
            {
                accel.z -= 1.0f;
            }

            if (wish.Has(StalkerMoveFlags.LeftStrafe))
            {
                accel.x -= strafe;
            }

            if (wish.Has(StalkerMoveFlags.RightStrafe))
            {
                accel.x += strafe;
            }

            if (environment == StalkerEnvironment.OnGround || environment == StalkerEnvironment.AtWall)
            {
                if (!_real.Has(StalkerMoveFlags.Crouch) && wish.Has(StalkerMoveFlags.Crouch))
                {
                    if (_real.Has(StalkerMoveFlags.Climb))
                    {
                        wish &= ~StalkerMoveFlags.Crouch;
                    }
                    else if (_stance.TryActivateBox(wish.IsAccelerated() ? StalkerStanceController.CROUCH_BOX : StalkerStanceController.LOW_CROUCH_BOX))
                    {
                        _real |= StalkerMoveFlags.Crouch;
                    }
                }

                _jumpTime -= deltaTime;
                if (CanJump() && wish.Has(StalkerMoveFlags.Jump))
                {
                    _real |= StalkerMoveFlags.Jump;
                    _jumpKeyPressed = true;
                    _jump = actor.JumpSpeed;
                    _jumpTime = engine.JumpTime;
                    _condition.ConditionJump();
                    JumpStarted?.Invoke();
                }

                StalkerMoveFlags move = StalkerMoveFlags.AnyMove | StalkerMoveFlags.Accel;
                if (_real.Has(StalkerMoveFlags.Crouch))
                {
                    if (!_real.IsAccelerated() && wish.IsAccelerated() && !_stance.TryActivateBox(StalkerStanceController.CROUCH_BOX))
                    {
                        move &= ~StalkerMoveFlags.Accel;
                    }

                    if (_real.IsAccelerated() && !wish.IsAccelerated() && _stance.TryActivateBox(StalkerStanceController.LOW_CROUCH_BOX))
                    {
                        _real &= ~StalkerMoveFlags.Accel;
                    }
                }

                if (wish.Has(StalkerMoveFlags.Sprint) && !CanSprint())
                {
                    wish &= ~StalkerMoveFlags.Sprint;
                }

                _real &= ~move;
                _real |= wish & move;
                if (wish.Has(StalkerMoveFlags.Sprint))
                {
                    _real |= StalkerMoveFlags.Sprint;
                }
                else
                {
                    _real &= ~StalkerMoveFlags.Sprint;
                }

                if (!_real.Has(StalkerMoveFlags.Forward) || _real.Has(StalkerMoveFlags.Crouch | StalkerMoveFlags.Climb) || !wish.IsAccelerated())
                {
                    _real &= ~StalkerMoveFlags.Sprint;
                    if (!(_real.Has(StalkerMoveFlags.Crouch) && !wish.Has(StalkerMoveFlags.Crouch)))
                    {
                        _wish &= ~StalkerMoveFlags.Sprint;
                    }
                }

                if (_real.Has(StalkerMoveFlags.AnyMove))
                {
                    accel = ScaleGroundAcceleration(accel);
                }
            }

            _localMove = accel;
            _controlAccel = Quaternion.Euler(0.0f, _yaw, 0.0f) * accel;
        }

        private Vector3 ScaleGroundAcceleration(Vector3 accel)
        {
            StalkerActorValues actor = _profile.Actor;
            bool accelerated = _real.IsAccelerated() && CanAccelerate();
            if (Mathf.Abs(accel.z) < EPS)
            {
                _real &= ~(StalkerMoveFlags.Forward | StalkerMoveFlags.Back);
            }

            if (Mathf.Abs(accel.x) < EPS)
            {
                _real &= ~(StalkerMoveFlags.LeftStrafe | StalkerMoveFlags.RightStrafe);
            }

            float scale = accel.magnitude;
            if (scale <= EPS)
            {
                return accel;
            }

            scale = actor.WalkAccel / scale;
            if (accelerated)
            {
                scale *= _real.Has(StalkerMoveFlags.Back) ? _condition.RunBackCoef : _condition.RunCoef;
            }
            else if (_real.Has(StalkerMoveFlags.Back))
            {
                scale *= actor.WalkBackCoef;
            }

            if (_real.Has(StalkerMoveFlags.Crouch))
            {
                scale *= actor.CrouchCoef;
            }

            if (_real.Has(StalkerMoveFlags.Climb))
            {
                scale *= actor.ClimbCoef;
            }

            if (_real.Has(StalkerMoveFlags.Sprint))
            {
                scale *= _condition.SprintKoef;
            }

            if (_real.Has(StalkerMoveFlags.LeftStrafe | StalkerMoveFlags.RightStrafe) && !_real.Has(StalkerMoveFlags.Crouch))
            {
                if (_real.Has(StalkerMoveFlags.Sprint))
                {
                    scale *= actor.SprintStrafeCoef;
                }
                else
                {
                    scale *= accelerated ? actor.RunStrafeCoef : actor.WalkStrafeCoef;
                }
            }

            return accel * scale;
        }

        private void ValidateState(StalkerMoveFlags wish, float deltaTime)
        {
            StalkerEngineValues engine = _profile.Engine;
            StalkerEnvironment environment = _physics.Environment;
            if (_real.Has(StalkerMoveFlags.Landing | StalkerMoveFlags.Landing2))
            {
                _landingTime -= deltaTime;
                if (_landingTime <= 0.0f)
                {
                    _real &= ~(StalkerMoveFlags.Landing | StalkerMoveFlags.Landing2 | StalkerMoveFlags.Fall | StalkerMoveFlags.Jump);
                }
            }

            if (_physics.ConsumeContactWas(out float contactSpeed))
            {
                _lastContactSpeed = contactSpeed;
                bool wasFalling = _real.Has(StalkerMoveFlags.Fall);
                if (_real.Has(StalkerMoveFlags.Fall) && contactSpeed > engine.LandingContactSpeed)
                {
                    bool healthLost = contactSpeed > _profile.Actor.CrashSpeedMin;
                    _landingTime = healthLost ? engine.LandingTime2 : engine.LandingTime1;
                    _real |= healthLost ? StalkerMoveFlags.Landing2 : StalkerMoveFlags.Landing;
                }

                _jumpKeyPressed = true;
                _jumpTime = engine.JumpTime;
                _real &= ~(StalkerMoveFlags.Fall | StalkerMoveFlags.Jump);
                ContactRestored?.Invoke(contactSpeed);
                if (wasFalling)
                {
                    Landed?.Invoke(contactSpeed);
                }
            }

            if (!wish.Has(StalkerMoveFlags.Jump))
            {
                _jumpKeyPressed = false;
            }

            bool airborneFlags = _real.Has(StalkerMoveFlags.Fall | StalkerMoveFlags.Jump | StalkerMoveFlags.Landing | StalkerMoveFlags.Landing2);
            if (_actualVelocity < engine.StuckVelocity && !airborneFlags || !_real.Has(StalkerMoveFlags.Climb) && environment == StalkerEnvironment.AtWall)
            {
                _real &= ~StalkerMoveFlags.AnyMove;
            }

            if ((environment == StalkerEnvironment.OnGround || environment == StalkerEnvironment.AtWall) && engine.JumpTime - _jumpTime > engine.JumpGroundTime && _real.Has(StalkerMoveFlags.Jump))
            {
                _real &= ~StalkerMoveFlags.Jump;
                _jumpTime = engine.JumpTime;
            }

            if (environment == StalkerEnvironment.AtWall)
            {
                if (!_real.Has(StalkerMoveFlags.Climb))
                {
                    _real |= StalkerMoveFlags.Climb;
                    _real &= ~StalkerMoveFlags.Sprint;
                }
            }
            else
            {
                _real &= ~StalkerMoveFlags.Climb;
            }

            if (wish != _real && _real.Has(StalkerMoveFlags.Crouch) && (!wish.Has(StalkerMoveFlags.Crouch) || _real.Has(StalkerMoveFlags.Climb)) && _stance.TryActivateBox(StalkerStanceController.STAND_BOX))
            {
                _real &= ~StalkerMoveFlags.Crouch;
            }

            if (!CanAccelerate() && _real.IsAccelerated())
            {
                _real ^= StalkerMoveFlags.Accel;
            }
        }

        private bool ValidateWalkOnMesh(Vector3 feet)
        {
            Vector3 accel = _physics.Acceleration;
            accel.y = 0.0f;
            float magnitude = accel.magnitude;
            if (magnitude <= 0.0f)
            {
                return false;
            }

            StalkerUnityAdaptation unity = _profile.Unity;
            float radius = _controller.radius;
            Vector3 forward = accel / magnitude;
            Quaternion orientation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 center = feet + forward * unity.LedgeProbeForward;
            Vector3 forbidCenter = center + Vector3.up * unity.LedgeForbidHeight;
            Vector3 lowCenter = center + Vector3.up * (radius + unity.LedgeProbeShift);
            if (Overlaps(forbidCenter, new Vector3(radius * 0.5f, radius * 2.0f, radius * 0.7f), orientation))
            {
                return false;
            }

            return Overlaps(lowCenter, new Vector3(radius * 0.5f, radius, radius * 0.7f), orientation);
        }

        private bool Overlaps(Vector3 center, Vector3 halfExtents, Quaternion orientation)
        {
            int count = UnityEngine.Physics.OverlapBoxNonAlloc(center, halfExtents, QUERY_BUFFER, orientation, _profile.Unity.CollisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (QUERY_BUFFER[i] != _controller && !QUERY_BUFFER[i].transform.IsChildOf(transform))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateLadder(Vector3 feet)
        {
            StalkerEngineValues engine = _profile.Engine;
            float footRadius = _controller.radius;
            if (_ladder == null)
            {
                _ladder = FindLadder(feet);
            }

            if (_ladder == null)
            {
                _physics.Climbing = false;
                return;
            }

            Vector3 accel = _physics.Acceleration;
            Vector3 toPlane = _ladder.DirectionToPlane(feet);
            Vector3 camera = new Vector3(_cameraDirection.x, 0.0f, _cameraDirection.z).normalized;
            float climbDirection = Vector3.Dot(accel, toPlane);
            if (climbDirection > EPS)
            {
                climbDirection *= _cameraDirection.y + engine.LadderLookUpSine;
            }

            bool touch = _ladder.InTouch(feet, footRadius);
            if (!_physics.Climbing)
            {
                bool facing = Vector3.Dot(toPlane, camera) > engine.LadderLookAngleCosine;
                bool atTop = _ladder.AxisDistanceToTop(feet) < footRadius;
                bool startDown = atTop && _cameraDirection.y < LADDER_LOOK_DOWN && climbDirection < 0.0f;
                if (touch && _ladder.BeforeLadder(feet, footRadius) && facing && (climbDirection > 0.0f && !atTop || startDown))
                {
                    _physics.Climbing = true;
                    _physics.SetVelocity(Vector3.zero);
                }
                else if (!touch && Vector3.Distance(feet, _ladder.Bottom) > _ladder.Height + LADDER_SEARCH_RADIUS)
                {
                    _ladder = null;
                }

                return;
            }

            _physics.LadderNormal = _ladder.Normal;
            _physics.LadderSide = _ladder.transform.right;
            _climbingUp = climbDirection > 0.0f || climbDirection >= 0.0f && _climbingUp;
            Vector3 toAxis = _ladder.ToAxis(feet, footRadius);
            Vector3 axis = _ladder.Axis;
            _physics.ClimbDirection = ((_climbingUp ? axis : -axis) + toAxis).normalized * (accel.sqrMagnitude > 0.0f ? 1.0f : 0.0f);
            float controlMagnitude = accel.magnitude;
            bool sideways = controlMagnitude > EPS && Mathf.Abs(Vector3.Dot(accel / controlMagnitude, _ladder.Normal)) < 0.70710678f;
            bool nearUp = _climbingUp && _ladder.AxisDistanceToTop(feet) < -NEAR_UP_CLEARANCE;
            bool aboveTop = _ladder.AxisDistanceToTop(feet) < -footRadius;
            bool atBottom = !_climbingUp && _ladder.AxisDistanceToBottom(feet) - footRadius < LADDER_STOP_DISTANCE;
            if (nearUp)
            {
                Vector3 over = -_ladder.Normal;
                over.y = 0.0f;
                _physics.Climbing = false;
                _physics.BeginNearUp(over.normalized, NEAR_UP_TIME);
            }
            else if (sideways || aboveTop || atBottom || !touch)
            {
                _physics.Climbing = false;
            }
        }

        private StalkerLadder FindLadder(Vector3 feet)
        {
            int count = UnityEngine.Physics.OverlapSphereNonAlloc(feet + Vector3.up * _controller.height * 0.5f, LADDER_SEARCH_RADIUS, QUERY_BUFFER, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                StalkerLadder ladder = QUERY_BUFFER[i].GetComponentInParent<StalkerLadder>();
                if (ladder != null)
                {
                    return ladder;
                }
            }

            return null;
        }
    }
}
