using System;

namespace RaidIntoDarkness.Player
{
    public sealed class AnomalyRadiationState
    {
        private double _level;

        public float Level => (float)_level;

        public void AddDosePoints(float dosePoints)
        {
            RequireNonNegative(dosePoints, nameof(dosePoints));
            _level = Math.Min(1d, _level + dosePoints / 100d);
        }

        public float Tick(float deltaTime, float decayRate, float healthRate)
        {
            RequireNonNegative(deltaTime, nameof(deltaTime));
            RequireNonNegative(decayRate, nameof(decayRate));
            RequireNonNegative(healthRate, nameof(healthRate));
            if (_level == 0d || deltaTime == 0f)
            {
                return 0f;
            }
            double activeTime = decayRate > 0f ? Math.Min(deltaTime, _level / decayRate) : deltaTime;
            double integral = _level * activeTime - 0.5d * decayRate * activeTime * activeTime;
            _level = Math.Max(0d, _level - decayRate * (double)deltaTime);
            return (float)Math.Min(float.MaxValue, Math.Max(0d, integral) * healthRate);
        }

        private static void RequireNonNegative(float value, string parameter)
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(parameter, "Radiation values must be finite and non-negative.");
            }
        }
    }
}
