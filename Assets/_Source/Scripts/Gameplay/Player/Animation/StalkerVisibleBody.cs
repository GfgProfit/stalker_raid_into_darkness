using RaidIntoDarkness.FirstPerson;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerVisibleBody : MonoBehaviour
    {
        private const int IDLE_FLAGS = 0;
        private const int IDLE_WALK_FLAGS = (int)StalkerMoveFlags.Accel;
        private const float POSITION_HOLD_TIME = 1.0f;
        private const StalkerMoveFlags AIRBORNE_FLAGS = StalkerMoveFlags.Jump | StalkerMoveFlags.Fall;
        private const StalkerMoveFlags LANDING_FLAGS = StalkerMoveFlags.Landing | StalkerMoveFlags.Landing2;

        [SerializeField] private Transform _bodyRoot;
        [SerializeField] private StalkerBodyAnimationPlayer _player;
        [SerializeField] private bool _combat;
        [SerializeField] private float _landingFadeIn = 0.08f;
        [SerializeField] private float _landingHold = 0.15f;
        [SerializeField] private float _landingBlend = 0.3f;

        private StalkerMovementProfile _profile;
        private BodyAnimationLibrary _library;
        private BodyAnimationBinding _active;
        private float _time;
        private float _turnUntil = -1.0f;
        private float _lastTurnTime = -10.0f;
        private float _turnReferenceYaw;
        private BodyAnimationBinding _turn;
        private float _idleCooldown;
        private float _idleTimer;
        private float _idleUntil = -1.0f;
        private BodyAnimationBinding _idle;
        private float _positionTimer;
        private bool _feetPlacement = true;
        private bool _wasAirborne;
        private float _landingUntil = -1.0f;
        private BodyAnimationBinding _landing;
        private bool _blendAfterLanding;
        private bool _landingStarted;

        public bool Combat
        {
            get => _combat;
            set => _combat = value;
        }

        public BodyAnimationLibrary Library => _library;
        public BodyAnimationBinding Active => _active;
        public BodyAnimationTable ActiveTable { get; private set; }
        public Transform BodyRoot => _bodyRoot;
        public StalkerBodyAnimationPlayer Player => _player;
        public float ForwardOffset { get; private set; }

        public void Initialize(StalkerMovementProfile profile)
        {
            _profile = profile;
            _idleCooldown = Random.Range(profile.VisibleBody.IdleCooldownMin, profile.VisibleBody.IdleCooldownMax);
        }

        public void SetLibrary(BodyAnimationLibrary library)
        {
            _library = library;
            _active = null;
        }

        public void Tick(StalkerMovementState state, StalkerCameraController view, float deltaTime)
        {
            _time += deltaTime;
            if (_library != null && _player != null && _player.IsBound)
            {
                BodyAnimationBinding binding = SelectAnimation(state, view, deltaTime);
                if (binding != null)
                {
                    float fade = 0.0f;
                    if (binding == _landing && !_landingStarted)
                    {
                        fade = _landingFadeIn;
                        _landingStarted = true;
                    }
                    else if (_blendAfterLanding && binding != _landing && !state.Flags.Has(LANDING_FLAGS))
                    {
                        fade = _landingBlend;
                        _blendAfterLanding = false;
                    }

                    _active = binding;
                    _player.Play(binding, fade);
                }

                _player.Tick(deltaTime);
            }

            Place(state, view, deltaTime);
        }

        private BodyAnimationBinding SelectAnimation(StalkerMovementState state, StalkerCameraController view, float deltaTime)
        {
            StalkerVisibleBodyValues values = _profile.VisibleBody;
            int flags = (int)state.Flags & ~_library.DisabledFlags;
            bool calm = !_combat;
            BodyAnimationBinding binding = _library.Resolve((int)state.Flags, calm, state.IsSlowedDown);
            ActiveTable = ResolveTable(flags, calm, state.IsSlowedDown);
            bool airborne = state.Flags.Has(AIRBORNE_FLAGS) || state.Environment == StalkerEnvironment.InAir;
            if (_wasAirborne && !airborne)
            {
                _landingUntil = _time + _landingHold;
                _landing = state.Flags.Has(LANDING_FLAGS) ? binding : _library.Resolve((int)StalkerMoveFlags.Landing, calm, state.IsSlowedDown);
                _blendAfterLanding = true;
                _landingStarted = false;
            }

            _wasAirborne = airborne;
            if (airborne)
            {
                _blendAfterLanding = false;
            }
            else if (_landing != null && _time < _landingUntil)
            {
                return _landing;
            }

            float angleDown = view.Direction.y;
            bool idleAllowed = values.EnableIdleAnimations && _library.EnableIdleAnimations && (flags == IDLE_FLAGS || flags == IDLE_WALK_FLAGS) && calm && angleDown <= values.LookDownThreshold;
            BodyAnimationBinding idle = UpdateIdle(idleAllowed, deltaTime);
            if (idle != null)
            {
                binding = idle;
            }

            BodyAnimationBinding turn = UpdateTurn(state, view, angleDown);
            return turn ?? binding;
        }

        private BodyAnimationTable ResolveTable(int flags, bool calm, bool slowed)
        {
            if (_library.UseCombatState && calm && _library.TryGetBinding(BodyAnimationTable.NonCombat, flags, out BodyAnimationBinding _))
            {
                return BodyAnimationTable.NonCombat;
            }

            if (_library.UseSlowDownState && slowed && _library.TryGetBinding(BodyAnimationTable.SlowedDown, flags, out BodyAnimationBinding _))
            {
                return BodyAnimationTable.SlowedDown;
            }

            return BodyAnimationTable.Basic;
        }

        private BodyAnimationBinding UpdateIdle(bool allowed, float deltaTime)
        {
            if (!allowed || _library.Idles.Count == 0)
            {
                _idleTimer = 0.0f;
                _idleUntil = -1.0f;
                return null;
            }

            if (_idleUntil >= _time)
            {
                return _idle;
            }

            _idleTimer += deltaTime;
            if (_idleTimer < _idleCooldown)
            {
                return null;
            }

            _idleTimer = 0.0f;
            BodyIdleAnimation idle = _library.Idles[Random.Range(0, _library.Idles.Count)];
            _idle = idle.Binding;
            _idleUntil = _time + idle.DurationMilliseconds / 1000.0f;
            return _idle;
        }

        private BodyAnimationBinding UpdateTurn(StalkerMovementState state, StalkerCameraController view, float angleDown)
        {
            StalkerVisibleBodyValues values = _profile.VisibleBody;
            float yaw = view.YawDegrees * Mathf.Deg2Rad;
            if (state.IsMoving || state.IsClimbing || state.IsJumping)
            {
                _turnReferenceYaw = yaw;
                _turnUntil = -1.0f;
                return null;
            }

            if (_turnUntil >= _time)
            {
                return _turn;
            }

            float threshold = angleDown < values.LookDownThreshold ? values.TurnThresholdLookDown : values.TurnThreshold;
            float angle = Mathf.DeltaAngle(_turnReferenceYaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            if (Mathf.Abs(angle) > threshold && _time - _lastTurnTime >= values.TurnDuration)
            {
                _turn = angle > 0.0f ? _library.TurnRight : _library.TurnLeft;
                _turnUntil = _time + values.TurnDuration;
                _lastTurnTime = _time;
                _turnReferenceYaw = yaw;
                return _turn;
            }

            return null;
        }

        private void Place(StalkerMovementState state, StalkerCameraController view, float deltaTime)
        {
            if (_bodyRoot == null || view.CameraRoot == null)
            {
                return;
            }

            StalkerVisibleBodyValues values = _profile.VisibleBody;
            Vector3 camera = view.CenteredPosition;
            Vector3 feet = transform.position;
            Vector3 position = camera;
            _positionTimer = Mathf.Max(0.0f, _positionTimer - deltaTime);
            if (_positionTimer <= 0.0f && Vector3.Distance(camera, feet) < values.HeadToBodyDistance)
            {
                position.y = feet.y;
                _feetPlacement = true;
            }
            else
            {
                float shift = values.FallShiftStand;
                if (state.IsCrouching)
                {
                    shift = state.IsLowCrouching ? values.FallShiftLowCrouch : values.FallShiftCrouch;
                }

                position.y -= shift;
                if (_feetPlacement)
                {
                    _positionTimer = POSITION_HOLD_TIME;
                    _feetPlacement = false;
                }
            }

            position.y += state.IsClimbing ? values.HeightLadder : values.Height;
            float forward = values.UseSpecialNonCombatShift && !_combat ? values.LegsOffsetNonCombat : values.LegsOffset;
            float right = values.LegsOffsetSide;
            string motion = _active != null ? _active.Motion : null;
            if (motion != null && values.TryGetShift(values.MotionShifts, motion, out StalkerBodyShift shiftValue))
            {
                forward = shiftValue.Forward;
                right = shiftValue.Right;
            }

            if (motion != null && values.HideLegsLowCrouch && state.IsLowCrouching && values.TryGetShift(values.LowCrouchShifts, motion, out StalkerBodyShift prone))
            {
                forward = prone.Forward;
                right = prone.Right;
            }

            Quaternion heading = Quaternion.Euler(0.0f, view.YawDegrees, 0.0f);
            position += heading * new Vector3(right, 0.0f, forward);
            ForwardOffset = forward;
            _bodyRoot.SetPositionAndRotation(position, heading);
        }
    }
}
