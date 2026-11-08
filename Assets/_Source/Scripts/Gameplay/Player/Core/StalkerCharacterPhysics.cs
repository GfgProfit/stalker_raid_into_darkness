using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public enum StalkerEnvironment
    {
        OnGround,
        AtWall,
        InAir
    }

    public struct StalkerContacts
    {
        public bool Any;
        public bool Ground;
        public Vector3 GroundNormal;
        public bool Wall;
        public Vector3 WallNormal;
        public float ContactSpeed;

        public void Clear()
        {
            Any = false;
            Ground = false;
            GroundNormal = Vector3.up;
            Wall = false;
            WallNormal = Vector3.zero;
            ContactSpeed = 0.0f;
        }
    }

    public sealed class StalkerCharacterPhysics
    {
        private const float SQRT1_2 = 0.70710678f;
        private const float CONTROL_EPSILON = 0.1f;
        private const float DEFAULT_LINEAR_LIMIT = 150.0f;
        private const float CLAMB_SIDE_DAMPING = 200.0f;

        private readonly StalkerMovementProfile _profile;

        private Vector3 _velocity;
        private Vector3 _acceleration;
        private float _maxVelocity;
        private bool _jumpRequested;
        private bool _jumpStarted;
        private bool _meetControl;
        private float _meetSpeed;
        private bool _wasContact;
        private bool _wasControl;
        private Vector3 _departPosition;
        private Vector3 _jumpDepartPosition;
        private StalkerContacts _contacts;
        private Vector3 _externalForce;
        private float _launchAirAcceleration;
        private float _launchAirSpeed;

        public StalkerCharacterPhysics(StalkerMovementProfile profile)
        {
            _profile = profile;
            _contacts.Clear();
        }

        public Vector3 Velocity => _velocity;
        public Vector3 Acceleration => _acceleration;
        public float MaxVelocity => _maxVelocity;
        public bool LoseControl { get; private set; }
        public bool Jumping { get; private set; }
        public bool IsControl { get; private set; }
        public bool ClambJump { get; set; }
        public bool Climbing { get; set; }
        public Vector3 ClimbDirection { get; set; }
        public Vector3 LadderNormal { get; set; }
        public Vector3 LadderSide { get; set; }
        public Vector3 NearUpDirection { get; private set; }
        public float NearUpTime { get; private set; }
        public float ContactSpeed { get; private set; }
        public float FrictionFactor { get; private set; }
        public StalkerContacts Contacts => _contacts;
        public bool GoodGround => _contacts.Ground && _contacts.GroundNormal.y > SQRT1_2;
        public bool LoseGround => !(GoodGround || Climbing) || LoseControl;

        public StalkerEnvironment Environment
        {
            get
            {
                if (LoseControl)
                {
                    return StalkerEnvironment.InAir;
                }

                return Climbing ? StalkerEnvironment.AtWall : StalkerEnvironment.OnGround;
            }
        }

        public void BeginNearUp(Vector3 direction, float duration)
        {
            NearUpDirection = direction;
            NearUpTime = duration;
        }

        public void SetControl(Vector3 acceleration)
        {
            _acceleration = acceleration;
            _maxVelocity = acceleration.magnitude / _profile.Engine.MaxVelocityDivider;
        }

        public bool ConsumeContactWas(out float contactSpeed)
        {
            contactSpeed = _meetSpeed;
            bool was = _meetControl;
            _meetControl = false;
            _meetSpeed = 0.0f;
            return was;
        }

        public bool CanJump()
        {
            return !LoseControl && (_contacts.GroundNormal.y > 0.5f && _contacts.Ground || Climbing);
        }

        public void RequestJump()
        {
            if (CanJump())
            {
                _jumpRequested = true;
            }
        }

        public void SetVelocity(Vector3 velocity)
        {
            _velocity = velocity;
        }

        public void Launch(Vector3 position, float height, float airAcceleration = 0f)
        {
            if (height <= 0f || _profile.Engine.Gravity <= 0f)
            {
                return;
            }
            float gravity = _profile.Engine.Gravity;
            _velocity.y = Mathf.Sqrt(2f * gravity * height) + gravity * _profile.Engine.FixedStep * 0.5f;
            _contacts.Clear();
            _wasContact = false;
            _departPosition = position;
            _jumpRequested = false;
            _jumpStarted = false;
            Jumping = false;
            Climbing = false;
            ClambJump = false;
            NearUpTime = 0f;
            LoseControl = true;
            _launchAirAcceleration = Mathf.Max(0f, airAcceleration);
            _launchAirSpeed = _profile.Actor.WalkAccel * _profile.Actor.RunCoef * _profile.Actor.SprintKoef / _profile.Engine.MaxVelocityDivider;
        }

        public void SetExternalForce(Vector3 force)
        {
            _externalForce = force;
        }

        public void CancelLaunchControl()
        {
            _launchAirAcceleration = 0f;
        }

        public void ResetContacts()
        {
            _wasContact = _contacts.Any;
            _contacts.Clear();
        }

        public void AddContact(Vector3 normal)
        {
            _contacts.Any = true;
            float into = -Vector3.Dot(_velocity, normal);
            if (into > _contacts.ContactSpeed)
            {
                _contacts.ContactSpeed = into;
            }

            if (!_contacts.Ground || normal.y > _contacts.GroundNormal.y)
            {
                _contacts.Ground = true;
                _contacts.GroundNormal = normal;
            }

            float score = normal.x * _acceleration.x + normal.z * _acceleration.z;
            float current = _contacts.WallNormal.x * _acceleration.x + _contacts.WallNormal.z * _acceleration.z;
            if (!_contacts.Wall || score < current)
            {
                _contacts.Wall = true;
                _contacts.WallNormal = normal;
            }

            if (into > 0.0f)
            {
                _velocity += normal * into;
            }
        }

        public Vector3 Step(Vector3 position, float deltaTime)
        {
            StalkerEngineValues engine = _profile.Engine;
            if ((GoodGround && _velocity.y <= 0f) || Climbing)
            {
                _launchAirAcceleration = 0f;
            }
            ContactSpeed = _contacts.ContactSpeed;
            FrictionFactor = _contacts.Any ? engine.ActorFriction * engine.GroundFriction : 0.0f;
            if (Climbing)
            {
                FrictionFactor = 1.0f;
            }

            IsControl = _acceleration.magnitude > CONTROL_EPSILON;
            bool depart = _wasContact && !_contacts.Any;
            bool stopControl = _wasControl && !IsControl;
            _wasControl = IsControl;
            bool onGround = _contacts.Ground;
            if (Climbing && stopControl)
            {
                _velocity = Vector3.zero;
            }

            if (depart)
            {
                _departPosition = position;
            }

            if (LoseControl && (_contacts.Any || Climbing))
            {
                _meetControl = true;
                _meetSpeed = Mathf.Max(_meetSpeed, _contacts.ContactSpeed);
            }

            if (LoseControl && (onGround && _contacts.GroundNormal.y > SQRT1_2 * 0.5f || _velocity.sqrMagnitude <= 0.0f || Climbing))
            {
                LoseControl = false;
            }
            if (_launchAirAcceleration > 0f)
            {
                LoseControl = true;
            }

            if (NearUpTime > 0.0f)
            {
                NearUpTime = GoodGround ? 0.0f : NearUpTime - deltaTime;
            }

            if (Jumping && (GoodGround || Climbing && _contacts.Wall))
            {
                Jumping = false;
            }

            if (!onGround && !Climbing)
            {
                Vector3 difference = position - _departPosition;
                if (difference.sqrMagnitude > engine.LoseControlDistance * engine.LoseControlDistance && Mathf.Abs(difference.y) > 0.1f)
                {
                    LoseControl = true;
                }
            }

            if (_jumpRequested)
            {
                _jumpRequested = false;
                LoseControl = true;
                _velocity = Climbing ? ClimbJumpVelocity() : GroundJumpVelocity();
                _jumpDepartPosition = position;
                Jumping = true;
                Climbing = false;
                _jumpStarted = true;
            }

            Vector3 controlForce = ControlForce();
            Vector3 acceleration = Vector3.zero;
            float mass = _profile.Actor.Mass;
            acceleration += _externalForce / mass;
            if (IsControl)
            {
                acceleration += controlForce;
                if (!LoseControl || ClambJump)
                {
                    Vector3 side = Vector3.Cross(controlForce, Vector3.up);
                    float sideMagnitude = side.magnitude;
                    if (sideMagnitude > 0.0f)
                    {
                        side /= sideMagnitude;
                        float projection = Vector3.Dot(side, _velocity);
                        acceleration -= side * (projection * (engine.SideDamping + CLAMB_SIDE_DAMPING * (ClambJump ? 1.0f : 0.0f)) * FrictionFactor / mass);
                    }

                    if (!LoseControl && !_contacts.Any)
                    {
                        acceleration.y -= engine.GroundStickAcceleration;
                    }
                }
            }

            if (Jumping)
            {
                acceleration += JumpAirForce(position) / mass;
            }

            if (!Climbing)
            {
                acceleration.y -= engine.Gravity;
            }

            _velocity += acceleration * deltaTime;
            ApplyLaunchSteering(deltaTime);
            Vector3 contactPush = Vector3.zero;
            if (_contacts.Ground && !Climbing && !_jumpStarted)
            {
                float into = Vector3.Dot(_velocity, _contacts.GroundNormal);
                if (into < 0.0f)
                {
                    _velocity -= _contacts.GroundNormal * into;
                    contactPush = _contacts.GroundNormal * (into * deltaTime);
                }
            }

            if (!IsControl && _contacts.Any && !Climbing)
            {
                ApplyContactFriction(deltaTime);
            }

            LimitVelocity();
            return _velocity * deltaTime + contactPush;
        }

        private Vector3 GroundJumpVelocity()
        {
            StalkerEngineValues engine = _profile.Engine;
            float magnitude = Mathf.Max(_acceleration.magnitude, 1.0f);
            float rate = engine.JumpIncreaseVelocityRate;
            float directional = engine.JumpDirectionalVelocity;
            return new Vector3(_velocity.x * rate + _acceleration.x / magnitude * directional, _profile.Actor.JumpSpeed, _velocity.z * rate + _acceleration.z / magnitude * directional);
        }

        private Vector3 ClimbJumpVelocity()
        {
            Vector3 direction = LadderNormal;
            Vector3 side = LadderSide;
            float sideComponent = Vector3.Dot(_acceleration.sqrMagnitude > 0.0f ? _acceleration.normalized : Vector3.zero, side);
            if (Mathf.Abs(sideComponent) > SQRT1_2)
            {
                direction = (direction + (sideComponent < 0.0f ? -side : side)).normalized;
            }

            return direction * _profile.Engine.LadderJumpVelocity;
        }

        private Vector3 ControlForce()
        {
            StalkerEngineValues engine = _profile.Engine;
            if (LoseControl)
            {
                if (_launchAirAcceleration > 0f)
                {
                    return _externalForce * _profile.Actor.AirControlParam;
                }
                return _acceleration * _profile.Actor.AirControlParam;
            }

            if (Climbing)
            {
                return ClimbDirection * (FrictionFactor * engine.PullForce * engine.ClimbPullMultiplier);
            }

            if (NearUpTime > 0.0f)
            {
                return NearUpDirection * engine.PullForce;
            }

            var accel = new Vector3(_acceleration.x, 0.0f, _acceleration.z);
            Vector3 side = Vector3.Cross(Vector3.up, accel);
            Vector3 force;
            if (ClambJump && _contacts.Wall)
            {
                Vector3 direction = Vector3.Cross(side, _contacts.WallNormal).normalized;
                force = direction * engine.PullForce;
            }
            else if (GoodGround)
            {
                Vector3 direction = Vector3.Cross(side, _contacts.GroundNormal).normalized;
                force = direction * engine.PullForce;
            }
            else
            {
                force = accel.normalized * (engine.PullForce * 1.5f);
            }

            if (ClambJump)
            {
                force *= _profile.Unity.LedgeClimbMultiplier;
                force.y = Mathf.Abs(force.y);
                force.x = force.x * accel.x >= 0.0f ? force.x : -force.x;
                force.z = force.z * accel.z >= 0.0f ? force.z : -force.z;
            }

            return force * FrictionFactor;
        }

        private void ApplyLaunchSteering(float deltaTime)
        {
            if (_launchAirAcceleration <= 0f)
            {
                return;
            }
            Vector3 command = _acceleration - _externalForce;
            command.y = 0f;
            float input = Mathf.Min(1f, command.magnitude);
            if (input <= 0.000001f)
            {
                return;
            }
            Vector3 horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
            float speed = Mathf.Max(_launchAirSpeed, horizontal.magnitude);
            Vector3 target = command.normalized * (speed * input);
            horizontal = Vector3.MoveTowards(horizontal, target, _launchAirAcceleration * input * deltaTime);
            _velocity.x = horizontal.x;
            _velocity.z = horizontal.z;
        }

        private Vector3 JumpAirForce(Vector3 position)
        {
            StalkerEngineValues engine = _profile.Engine;
            float airFactor = LoseControl ? 10.0f * _profile.Actor.AirControlParam : 1.0f;
            float magnitude = Mathf.Sqrt(_acceleration.x * _acceleration.x + _acceleration.z * _acceleration.z);
            if (magnitude <= 0.0f)
            {
                return Vector3.zero;
            }

            Vector3 force = Vector3.zero;
            Vector3 difference = position - _jumpDepartPosition;
            if ((difference.x * _acceleration.x + difference.z * _acceleration.z) / magnitude < engine.JumpAirDistance)
            {
                force.x += _acceleration.x * (engine.JumpAirForce / magnitude * airFactor);
                force.z += _acceleration.z * (engine.JumpAirForce / magnitude * airFactor);
            }

            float projection = _acceleration.x * _velocity.x + _acceleration.z * _velocity.z;
            float squared = _velocity.x * _velocity.x + _velocity.z * _velocity.z;
            if (projection < 0.0f && squared > 0.0f)
            {
                float scale = engine.JumpAirBrakeForce * airFactor / squared / magnitude * projection;
                force.x += _velocity.x * scale;
                force.z += _velocity.z * scale;
            }

            return force;
        }

        private void ApplyContactFriction(float deltaTime)
        {
            Vector3 normal = _contacts.Ground ? _contacts.GroundNormal : Vector3.up;
            Vector3 tangent = _velocity - normal * Vector3.Dot(_velocity, normal);
            float speed = tangent.magnitude;
            if (speed <= 0.0f)
            {
                return;
            }

            float mu = _profile.Engine.ActorFriction * _profile.Engine.GroundFriction * FrictionFactor;
            float decrease = mu * _profile.Engine.Gravity * Mathf.Max(normal.y, 0.0f) * deltaTime;
            if (decrease >= speed || speed < _profile.Unity.SleepVelocity && GoodGround)
            {
                _velocity -= tangent;
            }
            else
            {
                _velocity -= tangent * (decrease / speed);
            }
        }

        private void LimitVelocity()
        {
            float limit = IsControl && !LoseControl ? _maxVelocity : DEFAULT_LINEAR_LIMIT;
            float magnitude = _velocity.magnitude;
            if (magnitude <= limit)
            {
                return;
            }

            if (limit <= 0.0f)
            {
                _velocity = Vector3.zero;
                return;
            }

            float factor = magnitude / limit;
            if (LoseGround && _velocity.y < 0.0f && _velocity.y > -DEFAULT_LINEAR_LIMIT)
            {
                _velocity.x /= factor;
                _velocity.z /= factor;
            }
            else
            {
                _velocity *= limit / magnitude;
            }
        }
    }
}
