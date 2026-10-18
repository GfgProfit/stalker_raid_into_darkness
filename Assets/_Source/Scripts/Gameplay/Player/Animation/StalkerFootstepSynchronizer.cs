using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public enum StalkerFootstepSource
    {
        BodyAnimation,
        XRayLegsTiming
    }

    public readonly struct StalkerFootstep
    {
        public StalkerFootstep(StalkerFootSide side, float power, string legsMotion, string bodyMotion, float phase, StalkerFootstepSource source, StalkerMovingMode mode)
        {
            Side = side;
            Power = power;
            LegsMotion = legsMotion;
            BodyMotion = bodyMotion;
            Phase = phase;
            Source = source;
            Mode = mode;
        }

        public StalkerFootSide Side { get; }
        public float Power { get; }
        public string LegsMotion { get; }
        public string BodyMotion { get; }
        public float Phase { get; }
        public StalkerFootstepSource Source { get; }
        public StalkerMovingMode Mode { get; }
    }

    [DisallowMultipleComponent]
    public sealed class StalkerFootstepSynchronizer : MonoBehaviour
    {
        private const string TURN = "turn";
        private const string TURN_LEFT = "left";
        private const int LEGS = 2;

        [SerializeField] private StalkerFootstepTimingLibrary _library;
        [Tooltip("Unity adaptation: body contacts closer than this to the previous step are dropped, e.g. when the dominant Swm clip changes mid-crossfade.")]
        [SerializeField] private float _minBodyStepInterval = 0.15f;

        private readonly bool[] _handled = new bool[LEGS];
        private float _lastStepTime = float.NegativeInfinity;
        private readonly int[] _handledCycle = new int[LEGS];
        private AnimationClip _lastClip;
        private float _lastPhase;
        private string _legsMotion;
        private float _legsStart;
        private int _currentCycle = 1;
        private float _time;

        public event Action<StalkerFootstep> Footstep;

        public StalkerFootstepTimingLibrary Library => _library;
        public string LegsMotion => _legsMotion;
        public float Phase { get; private set; }
        public float CyclePhase { get; private set; } = -1.0f;
        public StalkerFootstepSource ActiveSource { get; private set; }
        public bool HasLastStep { get; private set; }
        public StalkerFootstep LastStep { get; private set; }

        public void Tick(StalkerMovementState state, StalkerBodyAnimationPlayer player, float deltaTime)
        {
            _time += deltaTime;
            CyclePhase = -1.0f;
            if (_library == null)
            {
                return;
            }

            bool airborne = state.Flags.Has(StalkerMoveFlags.Jump | StalkerMoveFlags.Fall) || state.Environment == StalkerEnvironment.InAir;
            string legs = GetLegsMotion(state.Flags);
            StalkerMovingMode mode = GetMovingMode(state.Flags);
            string bodyMotion = player != null ? player.CurrentMotion : null;
            bool turning = bodyMotion != null && bodyMotion.IndexOf(TURN, StringComparison.Ordinal) >= 0;
            if (airborne)
            {
                _lastClip = null;
                RestartLegs(null);
                return;
            }

            int dominant = FindDominant(player, out StalkerBodyMotionSteps steps);
            if (dominant >= 0 && (state.IsMoving || turning))
            {
                ActiveSource = StalkerFootstepSource.BodyAnimation;
                RestartLegs(null);
                TickBody(player.GetSlotClip(dominant), steps, Mathf.Repeat(player.GetSlotNormalizedTime(dominant), 1.0f), turning ? GetTurnMotion(state.Flags, bodyMotion) : legs, player.GetSlotMotion(dominant), mode);
                return;
            }

            _lastClip = null;
            ActiveSource = StalkerFootstepSource.XRayLegsTiming;
            TickLegs(legs, bodyMotion, mode);
        }

        public static string GetLegsMotion(StalkerMoveFlags flags)
        {
            string prefix = flags.Has(StalkerMoveFlags.Crouch) ? "cr" : flags.Has(StalkerMoveFlags.Climb) ? "cl" : "norm";
            if (flags.Has(StalkerMoveFlags.Landing))
            {
                return prefix + "_jump_end";
            }

            if (flags.Has(StalkerMoveFlags.Landing2))
            {
                return prefix + "_jump_end_1";
            }

            if (flags.Has(StalkerMoveFlags.Fall))
            {
                return prefix + "_jump_idle";
            }

            if (flags.Has(StalkerMoveFlags.Jump))
            {
                return prefix + "_jump_begin";
            }

            if (flags.Has(StalkerMoveFlags.Sprint))
            {
                if (flags.Has(StalkerMoveFlags.Forward))
                {
                    return "norm_escape_00";
                }

                if (flags.Has(StalkerMoveFlags.LeftStrafe))
                {
                    return "norm_escape_ls_00";
                }

                if (flags.Has(StalkerMoveFlags.RightStrafe))
                {
                    return "norm_escape_rs_00";
                }
            }

            string gait = flags.IsAccelerated() ? "_run" : "_walk";
            if (flags.Has(StalkerMoveFlags.Forward))
            {
                return prefix + gait + "_fwd_0";
            }

            if (flags.Has(StalkerMoveFlags.Back))
            {
                return prefix + gait + "_back_0";
            }

            if (flags.Has(StalkerMoveFlags.LeftStrafe))
            {
                return prefix + gait + "_ls_0";
            }

            if (flags.Has(StalkerMoveFlags.RightStrafe))
            {
                return prefix + gait + "_rs_0";
            }

            return null;
        }

        public static StalkerMovingMode GetMovingMode(StalkerMoveFlags flags)
        {
            if (flags.Has(StalkerMoveFlags.Sprint))
            {
                return StalkerMovingMode.Run;
            }

            return flags.Has(StalkerMoveFlags.Crouch) ? StalkerMovingMode.Crouch : StalkerMovingMode.Walk;
        }

        private static string GetTurnMotion(StalkerMoveFlags flags, string bodyMotion)
        {
            string prefix = flags.Has(StalkerMoveFlags.Crouch) ? "cr" : "norm";
            return prefix + (bodyMotion.IndexOf(TURN_LEFT, StringComparison.Ordinal) >= 0 ? "_turn_left_0" : "_turn_right_0");
        }

        private int FindDominant(StalkerBodyAnimationPlayer player, out StalkerBodyMotionSteps steps)
        {
            steps = null;
            if (player == null)
            {
                return -1;
            }

            int best = -1;
            float bestWeight = 0.0f;
            for (int i = 0; i < player.ActiveSlots; i++)
            {
                float weight = player.GetSlotWeight(i);
                if (weight > bestWeight && _library.TryGetBody(player.GetSlotClip(i), out StalkerBodyMotionSteps candidate))
                {
                    best = i;
                    bestWeight = weight;
                    steps = candidate;
                }
            }

            return best;
        }

        private void TickBody(AnimationClip clip, StalkerBodyMotionSteps steps, float phase, string legs, string bodyMotion, StalkerMovingMode mode)
        {
            Phase = phase;
            CyclePhase = GetStepCycle(phase, steps.Contacts);
            _legsMotion = legs;
            if (clip != _lastClip)
            {
                _lastClip = clip;
                _lastPhase = phase;
                return;
            }

            if (!_library.TryGetLegs(legs, out StalkerLegsMotionSteps legsSteps))
            {
                _lastPhase = phase;
                return;
            }

            for (int i = 0; i < steps.Contacts.Count; i++)
            {
                StalkerFootContact contact = steps.Contacts[i];
                if (Crossed(_lastPhase, phase, contact.Phase) && _time - _lastStepTime >= _minBodyStepInterval)
                {
                    Raise(new StalkerFootstep(contact.Side, legsSteps.GetPower(contact.Side), legs, bodyMotion, contact.Phase, StalkerFootstepSource.BodyAnimation, mode));
                }
            }

            _lastPhase = phase;
        }

        public static float GetStepCycle(float phase, IReadOnlyList<StalkerFootContact> contacts)
        {
            int count = contacts.Count;
            if (count == 0)
            {
                return -1.0f;
            }

            int origin = 0;
            for (int i = 0; i < count; i++)
            {
                if (contacts[i].Side == StalkerFootSide.Left)
                {
                    origin = i;
                    break;
                }
            }

            int segment = count - 1;
            for (int i = 0; i < count; i++)
            {
                if (contacts[i].Phase > phase)
                {
                    segment = i - 1;
                    break;
                }
            }

            if (segment < 0)
            {
                segment = count - 1;
            }

            float start = contacts[segment].Phase;
            float end = segment + 1 < count ? contacts[segment + 1].Phase : contacts[0].Phase + 1.0f;
            float local = phase < start ? phase + 1.0f : phase;
            float fraction = end > start ? Mathf.Clamp01((local - start) / (end - start)) : 0.0f;
            int step = (segment - origin + count) % count;
            return Mathf.Repeat((step + fraction) * 0.5f, 1.0f);
        }

        public static float GetStepCycle(float phase, float leftTime, float rightTime)
        {
            float span = Mathf.Repeat(rightTime - leftTime, 1.0f);
            float local = Mathf.Repeat(phase - leftTime, 1.0f);
            if (span <= 0.0f)
            {
                return local;
            }

            return local < span ? local / span * 0.5f : 0.5f + (local - span) / (1.0f - span) * 0.5f;
        }

        private static bool Crossed(float from, float to, float point)
        {
            if (Mathf.Approximately(from, to))
            {
                return false;
            }

            if (to > from)
            {
                return point > from && point <= to;
            }

            return point > from || point <= to;
        }

        private void TickLegs(string legs, string bodyMotion, StalkerMovingMode mode)
        {
            if (legs != _legsMotion)
            {
                RestartLegs(legs);
            }

            if (!_library.TryGetLegs(legs, out StalkerLegsMotionSteps steps) || steps.CycleTime <= 0.0f)
            {
                Phase = 0.0f;
                return;
            }

            float cycleTime = steps.CycleTime / Mathf.Max(steps.Cycles, 1);
            float elapsed = _time - _legsStart;
            Phase = Mathf.Repeat(elapsed / steps.CycleTime, 1.0f);
            CyclePhase = GetStepCycle(Mathf.Repeat(elapsed / cycleTime, 1.0f), steps.LeftTime, steps.RightTime);
            for (int leg = 0; leg < LEGS; leg++)
            {
                if (_handled[leg] && _handledCycle[leg] == _currentCycle)
                {
                    continue;
                }

                var side = (StalkerFootSide)leg;
                float offset = cycleTime * (_currentCycle - 1) + cycleTime * steps.GetTime(side);
                if (offset <= elapsed)
                {
                    Raise(new StalkerFootstep(side, steps.GetPower(side), legs, bodyMotion, steps.GetTime(side), StalkerFootstepSource.XRayLegsTiming, mode));
                    _handled[leg] = true;
                    _handledCycle[leg] = _currentCycle;
                }
            }

            if (_currentCycle < steps.Cycles)
            {
                _currentCycle = 1 + (int)(elapsed / cycleTime);
            }

            if (!steps.StopAtEnd && steps.CycleTime < elapsed)
            {
                _legsStart += steps.CycleTime;
                _currentCycle = 1;
                ResetHandled();
            }
        }

        private void RestartLegs(string legs)
        {
            _legsMotion = legs;
            _legsStart = _time;
            _currentCycle = 1;
            ResetHandled();
        }

        private void ResetHandled()
        {
            for (int leg = 0; leg < LEGS; leg++)
            {
                _handled[leg] = false;
                _handledCycle[leg] = _currentCycle;
            }
        }

        private void Raise(StalkerFootstep step)
        {
            LastStep = step;
            HasLastStep = true;
            _lastStepTime = _time;
            Footstep?.Invoke(step);
        }
    }
}
