using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [DisallowMultipleComponent]
    public sealed class XRayAnomalySchedule : MonoBehaviour
    {
        [SerializeField] private uint _enabledTime;
        [SerializeField] private uint _disabledTime;
        [SerializeField] private uint _startTimeShift;

        private IXRayAnomalyActivitySource[] _sources;
        private uint _startedAt;
        private bool _initialized;
        private bool _hasEpoch;
        private bool _started;
        private bool _active = true;

        public uint EnabledTime => _enabledTime;
        public uint DisabledTime => _disabledTime;
        public uint StartTimeShift => _startTimeShift;
        public bool IsActive => _active;

        public void Configure(uint enabledTime, uint disabledTime, uint startTimeShift)
        {
            if (Application.isPlaying && _initialized && _active == false)
            {
                ApplyActivity(true);
            }
            _enabledTime = enabledTime;
            _disabledTime = disabledTime;
            _startTimeShift = startTimeShift;
            _sources = null;
            _initialized = false;
            _hasEpoch = false;
            _active = true;
            if (Application.isPlaying)
            {
                _startedAt = CurrentMilliseconds();
                _hasEpoch = true;
                if (_started)
                {
                    Tick(_startedAt);
                }
            }
        }

        public void Tick(uint nowMilliseconds)
        {
            if (_hasEpoch == false)
            {
                _startedAt = nowMilliseconds;
                _hasEpoch = true;
            }
            if (_initialized == false)
            {
                var sources = new List<IXRayAnomalyActivitySource>();
                foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
                {
                    if (behaviour is IXRayAnomalyActivitySource source)
                    {
                        sources.Add(source);
                    }
                }
                _sources = sources.ToArray();
                _initialized = true;
            }
            bool desired = Evaluate(_enabledTime, _disabledTime, _startTimeShift, _startedAt, nowMilliseconds);
            if (desired == _active)
            {
                return;
            }
            bool canDeactivate = true;
            if (desired == false)
            {
                foreach (IXRayAnomalyActivitySource source in _sources)
                {
                    if (IsAvailable(source) && source.CanDeactivate == false)
                    {
                        canDeactivate = false;
                        break;
                    }
                }
            }
            bool next = ResolveActivity(_active, desired, canDeactivate);
            if (next != _active)
            {
                ApplyActivity(next);
            }
        }

        public static bool Evaluate(uint enabledTime, uint disabledTime, uint startTimeShift, uint startedAtMilliseconds, uint nowMilliseconds)
        {
            uint enabled = unchecked(enabledTime * 1000U);
            uint disabled = unchecked(disabledTime * 1000U);
            uint cycle = unchecked(enabled + disabled);
            if (enabled == 0 || disabled == 0 || cycle == 0)
            {
                return true;
            }
            uint shift = unchecked(startTimeShift * 1000U);
            uint phase = unchecked(nowMilliseconds - startedAtMilliseconds + shift) % cycle;
            return phase < enabled;
        }

        public static bool ResolveActivity(bool currentActive, bool desiredActive, bool canDeactivate)
        {
            return desiredActive || currentActive && canDeactivate == false;
        }

        private void Awake()
        {
            if (Application.isPlaying)
            {
                _startedAt = CurrentMilliseconds();
                _hasEpoch = true;
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying && _started)
            {
                Tick(CurrentMilliseconds());
            }
        }

        private void Start()
        {
            _started = true;
            Tick(CurrentMilliseconds());
        }

        private void Update()
        {
            Tick(CurrentMilliseconds());
        }

        private void ApplyActivity(bool active)
        {
            if (_sources != null)
            {
                foreach (IXRayAnomalyActivitySource source in _sources)
                {
                    if (IsAvailable(source))
                    {
                        source.SetSourceActive(active);
                    }
                }
            }
            _active = active;
        }

        private static bool IsAvailable(IXRayAnomalyActivitySource source)
        {
            if (source is UnityEngine.Object owner)
            {
                return owner != null;
            }
            return source != null;
        }

        private static uint CurrentMilliseconds()
        {
            return unchecked((uint)(ulong)Math.Max(0d, Time.timeAsDouble * 1000d));
        }
    }
}
