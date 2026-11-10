using System;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    public enum StalkerStatKind
    {
        Health,
        Stamina,
        Hunger,
        Thirst,
        Sleep,
        PsyHealth
    }

    public sealed class StalkerStat
    {
        private float _capacity;
        private float _maximum;
        private float _value;

        public float Capacity => _capacity;
        public float Maximum => _maximum;
        public float Value => _value;
        public float NormalizedValue => _value / _capacity;
        public float NormalizedMaximum => _maximum / _capacity;

        public StalkerStat(float capacity = GameConfig.DEFAULT_PLAYER_STAT_CAPACITY)
        {
            RequireCapacity(capacity);
            _capacity = capacity;
            _maximum = capacity;
            _value = capacity;
        }

        public void SetValue(float value)
        {
            RequireFinite(value, nameof(value));
            _value = Mathf.Clamp(value, 0f, _maximum);
        }

        public void ChangeValue(float delta)
        {
            RequireFinite(delta, nameof(delta));
            SetValue(_value + delta);
        }

        public void SetMaximum(float maximum)
        {
            RequireFinite(maximum, nameof(maximum));
            _maximum = Mathf.Clamp(maximum, 0f, _capacity);
            _value = Mathf.Min(_value, _maximum);
        }

        public void ChangeMaximum(float delta)
        {
            RequireFinite(delta, nameof(delta));
            SetMaximum(_maximum + delta);
        }

        public void SetCapacity(float capacity)
        {
            RequireCapacity(capacity);
            double scale = capacity / (double)_capacity;
            _maximum = Mathf.Clamp((float)(_maximum * scale), 0f, capacity);
            _value = Mathf.Clamp((float)(_value * scale), 0f, _maximum);
            _capacity = capacity;
        }

        public void Reset()
        {
            _maximum = _capacity;
            _value = _capacity;
        }

        private static void RequireCapacity(float capacity)
        {
            RequireFinite(capacity, nameof(capacity));
            if (capacity <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Stat capacity must be positive.");
            }
        }

        private static void RequireFinite(float value, string parameter)
        {
            if (float.IsFinite(value) == false)
            {
                throw new ArgumentOutOfRangeException(parameter, "Stat values must be finite.");
            }
        }
    }
}
