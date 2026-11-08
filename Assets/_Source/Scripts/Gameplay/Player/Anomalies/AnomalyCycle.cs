using System;

namespace RaidIntoDarkness.Player
{
    public enum AnomalyPhase
    {
        Idle,
        Awaking,
        Blowout,
        Accumulate
    }

    public sealed class AnomalyCycle
    {
        private readonly float _awakingDuration;
        private readonly float _blowoutDuration;
        private readonly float _accumulateDuration;

        private float _stateStartedAt;

        public AnomalyPhase Phase { get; private set; }

        public AnomalyCycle(float awakingDuration, float blowoutDuration, float accumulateDuration)
        {
            _awakingDuration = Math.Max(0f, awakingDuration);
            _blowoutDuration = Math.Max(0f, blowoutDuration);
            _accumulateDuration = Math.Max(0f, accumulateDuration);
        }

        public bool Tick(float now, bool hasContact)
        {
            switch (Phase)
            {
                case AnomalyPhase.Idle:
                    if (hasContact)
                    {
                        Phase = AnomalyPhase.Awaking;
                        _stateStartedAt = now;
                    }
                    break;
                case AnomalyPhase.Awaking:
                    if (now - _stateStartedAt >= _awakingDuration)
                    {
                        RecordDischarge(now);
                        return true;
                    }
                    break;
                case AnomalyPhase.Blowout:
                    if (now - _stateStartedAt >= _blowoutDuration)
                    {
                        Phase = AnomalyPhase.Accumulate;
                        _stateStartedAt = now;
                    }
                    break;
                case AnomalyPhase.Accumulate:
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
            Phase = AnomalyPhase.Blowout;
            _stateStartedAt = now;
        }

        public float GetCooldownRemaining(float now)
        {
            float elapsed = Math.Max(0f, now - _stateStartedAt);
            switch (Phase)
            {
                case AnomalyPhase.Awaking:
                    return Math.Max(0f, _awakingDuration - elapsed);
                case AnomalyPhase.Blowout:
                    return Math.Max(0f, _blowoutDuration - elapsed) + _accumulateDuration;
                case AnomalyPhase.Accumulate:
                    return Math.Max(0f, _accumulateDuration - elapsed);
                default:
                    return 0f;
            }
        }

        public void Reset()
        {
            Phase = AnomalyPhase.Idle;
            _stateStartedAt = 0f;
        }
    }
}
