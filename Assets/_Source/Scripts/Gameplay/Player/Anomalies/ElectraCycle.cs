using System;

namespace RaidIntoDarkness.Player
{
    public enum ElectraPhase
    {
        Idle,
        Awaking,
        Blowout,
        Accumulate
    }

    public sealed class ElectraCycle
    {
        private readonly float _awakingDuration;
        private readonly float _blowoutDuration;
        private readonly float _accumulateDuration;

        private float _stateStartedAt;

        public ElectraPhase Phase { get; private set; }

        public ElectraCycle(float awakingDuration = 0.1f, float blowoutDuration = 0.1f, float accumulateDuration = 1.5f)
        {
            _awakingDuration = Math.Max(0f, awakingDuration);
            _blowoutDuration = Math.Max(0f, blowoutDuration);
            _accumulateDuration = Math.Max(0f, accumulateDuration);
        }

        public bool Tick(float now, bool hasContact)
        {
            switch (Phase)
            {
                case ElectraPhase.Idle:
                    if (hasContact)
                    {
                        Phase = ElectraPhase.Awaking;
                        _stateStartedAt = now;
                    }
                    break;
                case ElectraPhase.Awaking:
                    if (now - _stateStartedAt >= _awakingDuration)
                    {
                        RecordDischarge(now);
                        return true;
                    }
                    break;
                case ElectraPhase.Blowout:
                    if (now - _stateStartedAt >= _blowoutDuration)
                    {
                        Phase = ElectraPhase.Accumulate;
                        _stateStartedAt = now;
                    }
                    break;
                case ElectraPhase.Accumulate:
                    if (now - _stateStartedAt >= _accumulateDuration)
                    {
                        if (hasContact)
                        {
                            RecordDischarge(now);
                            return true;
                        }
                        Reset();
                    }
                    break;
            }

            return false;
        }

        public void RecordDischarge(float now)
        {
            Phase = ElectraPhase.Blowout;
            _stateStartedAt = now;
        }

        public float GetCooldownRemaining(float now)
        {
            float elapsed = Math.Max(0f, now - _stateStartedAt);
            switch (Phase)
            {
                case ElectraPhase.Awaking:
                    return Math.Max(0f, _awakingDuration - elapsed);
                case ElectraPhase.Blowout:
                    return Math.Max(0f, _blowoutDuration - elapsed) + _accumulateDuration;
                case ElectraPhase.Accumulate:
                    return Math.Max(0f, _accumulateDuration - elapsed);
                default:
                    return 0f;
            }
        }

        public void Reset()
        {
            Phase = ElectraPhase.Idle;
            _stateStartedAt = 0f;
        }
    }
}
