using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Player
{
    public enum StalkerCameraEffectSlot
    {
        MoveStep,
        SprintStep,
        TiredStep,
        Jump,
        Landing,
        Crouch,
        Stand,
        ActorMove
    }

    [DisallowMultipleComponent]
    public sealed class StalkerCameraEffects : MonoBehaviour
    {
        private const int SLOT_COUNT = 8;
        private const float EPS = 0.0000001f;
        private const float MAX_PHASE_RATE = 3.0f;
        private const float SNAP_REMINDER = 0.2f;

        private struct AnmEffect
        {
            public XRayAnmAsset Asset;
            public float Time;
            public float Factor;
            public float Scale;
            public float Fade;
        }

        [SerializeField] private StalkerCameraEffectsProfile _profile;
        [SerializeField] private Transform _pivot;

        private readonly AnmEffect[] _effects = new AnmEffect[SLOT_COUNT];
        private readonly AnmEffect[] _fading = new AnmEffect[SLOT_COUNT];
        private readonly AnmEffect[] _itemEffects = new AnmEffect[1];
        private float _bobAngle;
        private float _lastBobTarget;
        private bool _hasBobTarget;
        private float _bobAmplitude = -1.0f;
        private float _reminder;
        private float _fallPhase = 1.0f;
        private float _fallPower;
        private float _landingReadyTime;
        private float _time;
        private StalkerMoveFlags _previousFlags;

        public StalkerCameraEffectsProfile Profile => _profile;
        public Transform Pivot => _pivot;
        public float BobAmplitude { get; private set; }
        public float BobReminder => _reminder;
        public Vector3 MotionOffset { get; private set; }
        public Quaternion MotionRotation { get; private set; } = Quaternion.identity;
        public bool EffectsEnabled { get; set; } = true;
        public bool IsItemEffectPlaying => _itemEffects[0].Asset != null;

        public bool IsPlaying(StalkerCameraEffectSlot slot)
        {
            return _effects[(int)slot].Asset != null;
        }

        public void PlayItemEffect(XRayAnmAsset asset)
        {
            _itemEffects[0] = asset != null ? new AnmEffect { Asset = asset, Time = 0.0f, Factor = 1.0f, Scale = 1.0f, Fade = 1.0f } : default;
        }

        public void StopItemEffect()
        {
            _itemEffects[0] = default;
        }

        public void Tick(StalkerMovementState state, bool limping, float bodyPhase, float deltaTime)
        {
            _time += deltaTime;
            if (_profile == null || _pivot == null)
            {
                return;
            }

            DetectMoveStart(state);
            float intensity = _profile.Enabled && EffectsEnabled ? _profile.Intensity : 0.0f;
            float worldUp = 0.0f;
            Quaternion rotation = Quaternion.identity;
            Vector3 local = Vector3.zero;
            TickBobbing(state, limping, bodyPhase, deltaTime, ref worldUp, ref rotation);
            TickFall(deltaTime, ref worldUp);
            float fadeStep = _profile.AnmRestartFade > 0.0f ? deltaTime / _profile.AnmRestartFade : 1.0f;
            for (int slot = 0; slot < SLOT_COUNT; slot++)
            {
                TickAnm(_effects, slot, deltaTime, 0.0f, ref local, ref rotation);
                TickAnm(_fading, slot, deltaTime, fadeStep, ref local, ref rotation);
            }

            Transform parent = _pivot.parent;
            Vector3 up = parent != null ? parent.InverseTransformDirection(Vector3.up) : Vector3.up;
            Vector3 itemLocal = Vector3.zero;
            Quaternion itemRotation = Quaternion.identity;
            TickAnm(_itemEffects, 0, deltaTime, 0.0f, ref itemLocal, ref itemRotation);
            MotionOffset = (local + up * worldUp) * intensity + itemLocal;
            MotionRotation = Quaternion.SlerpUnclamped(Quaternion.identity, rotation, intensity) * itemRotation;
            _pivot.localPosition = MotionOffset;
            _pivot.localRotation = MotionRotation;
        }

        public void OnFootstep(StalkerMovementState state, float power)
        {
            StalkerAnmEffectValues anm = _profile != null ? _profile.Anm : null;
            if (anm == null || !_profile.StepEffectsEnabled)
            {
                return;
            }

            if (power < anm.TiredPower)
            {
                Play(StalkerCameraEffectSlot.TiredStep, anm.TiredStep, 1.0f, 1.0f);
            }
            else if (state.IsSprinting)
            {
                Play(StalkerCameraEffectSlot.SprintStep, anm.SprintStep, 1.0f, 1.0f);
            }
            else
            {
                Play(StalkerCameraEffectSlot.MoveStep, anm.MoveStep, 1.0f, 1.0f);
            }
        }

        public void OnJump()
        {
            if (_profile != null && _profile.JumpLandEffectsEnabled)
            {
                Play(StalkerCameraEffectSlot.Jump, _profile.Anm.Jump, 1.0f, _profile.JumpScale);
            }
        }

        public void OnLanded(float contactSpeed)
        {
            StalkerAnmEffectValues anm = _profile != null ? _profile.Anm : null;
            if (anm == null || !_profile.JumpLandEffectsEnabled || contactSpeed <= anm.LandingSpeed || _time <= _landingReadyTime || anm.Landing.Length == 0)
            {
                return;
            }

            Play(StalkerCameraEffectSlot.Landing, anm.Landing[Random.Range(0, anm.Landing.Length)], 1.0f, _profile.LandingScale);
            _landingReadyTime = _time + anm.LandingCooldown;
        }

        public void OnContact(float contactSpeed)
        {
            if (_profile == null || !_profile.FallEnabled)
            {
                return;
            }

            float power = contactSpeed / _profile.Fall.CrashSpeedMax;
            _fallPower = power > 1.0f ? 1.0f : power < 0.0f ? 0.0f : power * power;
            _fallPhase = 0.0f;
        }

        public void OnCrouchKey(bool pressed)
        {
            if (_profile != null && _profile.CrouchEffectsEnabled)
            {
                Play(pressed ? StalkerCameraEffectSlot.Crouch : StalkerCameraEffectSlot.Stand, pressed ? _profile.Anm.Crouch : _profile.Anm.Stand, 1.0f, 1.0f);
            }
        }

        private void Play(StalkerCameraEffectSlot slot, XRayAnmAsset asset, float factor, float scale)
        {
            if (asset == null)
            {
                return;
            }

            int index = (int)slot;
            if (_effects[index].Asset != null && _profile.AnmRestartFade > 0.0f)
            {
                _fading[index] = _effects[index];
            }

            _effects[index] = new AnmEffect { Asset = asset, Time = 0.0f, Factor = factor, Scale = scale, Fade = 1.0f };
        }

        private void TickBobbing(StalkerMovementState state, bool limping, float bodyPhase, float deltaTime, ref float worldUp, ref Quaternion rotation)
        {
            StalkerBobbingValues bob = _profile.Bobbing;
            bool moving = state.Flags.Has(StalkerMoveFlags.AnyMove) && (state.IsGrounded || state.IsClimbing || !_profile.BobRequiresGround);
            if (moving)
            {
                _reminder = Mathf.Min(1.0f, _reminder + bob.SpeedReminder * deltaTime);
            }
            else
            {
                _reminder = Mathf.Max(0.0f, _reminder - bob.SpeedReminder * deltaTime);
            }

            BobAmplitude = 0.0f;
            if (!_profile.BobbingEnabled || _reminder <= 0.0f)
            {
                _hasBobTarget = false;
                _bobAmplitude = -1.0f;
                return;
            }

            float k = state.Flags.Has(StalkerMoveFlags.Crouch) ? bob.CrouchFactor : 1.0f;
            float amplitude;
            float speed;
            if (state.Flags.IsAccelerated())
            {
                amplitude = bob.RunAmplitude * k;
                speed = bob.RunSpeed * k;
            }
            else if (limping)
            {
                amplitude = bob.LimpAmplitude * k;
                speed = bob.LimpSpeed * k;
            }
            else
            {
                amplitude = bob.WalkAmplitude * k;
                speed = bob.WalkSpeed * k;
            }

            float angle = AdvanceBobAngle(speed, bodyPhase, deltaTime);
            if (_bobAmplitude < 0.0f || bob.AmplitudeBlendTime <= 0.0f)
            {
                _bobAmplitude = amplitude;
            }
            else
            {
                _bobAmplitude = Mathf.Lerp(_bobAmplitude, amplitude, 1.0f - Mathf.Exp(-deltaTime / bob.AmplitudeBlendTime));
            }

            amplitude = _bobAmplitude;
            float scale = _reminder * bob.HeadBobFactor * _profile.BobbingScale;
            float sinA = Mathf.Abs(Mathf.Sin(angle) * amplitude) * scale;
            float cosA = Mathf.Cos(angle) * amplitude * scale;
            BobAmplitude = amplitude * scale;
            worldUp += sinA;
            rotation *= Quaternion.Euler(-sinA * Mathf.Rad2Deg, -cosA * Mathf.Rad2Deg, cosA * Mathf.Rad2Deg);
        }

        private float AdvanceBobAngle(float speed, float bodyPhase, float deltaTime)
        {
            float advance = speed * deltaTime;
            if (_profile.BobPhaseSource != StalkerBobPhaseSource.BodyAnimation || bodyPhase < 0.0f)
            {
                _hasBobTarget = false;
                _bobAngle = Mathf.Repeat(_bobAngle + advance, Mathf.PI * 2.0f);
                return _bobAngle;
            }

            float target = bodyPhase * Mathf.PI * 2.0f;
            if (!_hasBobTarget && _reminder <= SNAP_REMINDER)
            {
                _bobAngle = target;
                _lastBobTarget = target;
                _hasBobTarget = true;
                return _bobAngle;
            }

            if (_hasBobTarget)
            {
                float step = Mathf.DeltaAngle(_lastBobTarget * Mathf.Rad2Deg, target * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                if (step >= 0.0f && step <= advance * MAX_PHASE_RATE)
                {
                    advance = step;
                }
            }

            _lastBobTarget = target;
            _hasBobTarget = true;
            float error = Mathf.DeltaAngle(_bobAngle * Mathf.Rad2Deg, target * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float correction = _profile.Bobbing.PhaseCorrection * deltaTime;
            _bobAngle = Mathf.Repeat(_bobAngle + advance + Mathf.Clamp(error, -correction, correction), Mathf.PI * 2.0f);
            return _bobAngle;
        }

        private void TickFall(float deltaTime, ref float worldUp)
        {
            if (_fallPhase >= 1.0f)
            {
                return;
            }

            StalkerFallEffectValues fall = _profile.Fall;
            _fallPhase += fall.Speed * deltaTime;
            if (_fallPhase < 1.0f)
            {
                worldUp -= fall.MaxDistance * _fallPower * Mathf.Sin(Mathf.PI * _fallPhase + Mathf.PI) * _profile.LandingScale;
            }
        }

        private static void TickAnm(AnmEffect[] effects, int slot, float deltaTime, float fadeStep, ref Vector3 local, ref Quaternion rotation)
        {
            AnmEffect effect = effects[slot];
            if (effect.Asset == null)
            {
                return;
            }

            effect.Time += deltaTime;
            effect.Fade -= fadeStep;
            if (effect.Fade <= 0.0f || effect.Time > effect.Asset.Duration || !effect.Asset.Evaluate(effect.Time, out Vector3 position, out Quaternion anmRotation))
            {
                effects[slot] = default;
                return;
            }

            effects[slot] = effect;
            float weight = effect.Factor * effect.Scale * effect.Fade;
            local += rotation * (position * weight);
            rotation *= Quaternion.SlerpUnclamped(Quaternion.identity, anmRotation, weight);
        }

        private void DetectMoveStart(StalkerMovementState state)
        {
            StalkerMoveFlags flags = state.Flags;
            StalkerMoveFlags old = _previousFlags;
            _previousFlags = flags;
            if (_profile == null || !_profile.MoveEffectsEnabled || IsPlaying(StalkerCameraEffectSlot.ActorMove))
            {
                return;
            }

            float raw = RawInputMagnitude(flags);
            float factor = raw > EPS ? state.LocalMove.magnitude / raw : 0.0f;
            if (factor <= EPS || !state.IsGrounded)
            {
                return;
            }

            StalkerAnmEffectValues anm = _profile.Anm;
            XRayAnmAsset asset = null;
            if (flags.Has(StalkerMoveFlags.Sprint) && !old.Has(StalkerMoveFlags.Sprint))
            {
                asset = null;
            }
            else if (flags.Has(StalkerMoveFlags.LeftStrafe) && !old.Has(StalkerMoveFlags.LeftStrafe))
            {
                asset = anm.StrafeLeft;
            }
            else if (flags.Has(StalkerMoveFlags.RightStrafe) && !old.Has(StalkerMoveFlags.RightStrafe))
            {
                asset = anm.StrafeRight;
            }

            if (asset != null)
            {
                Play(StalkerCameraEffectSlot.ActorMove, asset, factor / anm.MoveEffectMaxScale, 1.0f);
            }
        }

        private static float RawInputMagnitude(StalkerMoveFlags flags)
        {
            float strafe = flags.Has(StalkerMoveFlags.Sprint) ? 0.5f : 1.0f;
            float z = (flags.Has(StalkerMoveFlags.Forward) ? 1.0f : 0.0f) - (flags.Has(StalkerMoveFlags.Back) ? 1.0f : 0.0f);
            float x = (flags.Has(StalkerMoveFlags.RightStrafe) ? strafe : 0.0f) - (flags.Has(StalkerMoveFlags.LeftStrafe) ? strafe : 0.0f);
            return Mathf.Sqrt(x * x + z * z);
        }
    }
}
