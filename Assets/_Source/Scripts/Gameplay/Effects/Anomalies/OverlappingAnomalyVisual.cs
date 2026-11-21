using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class OverlappingAnomalyVisual : AnomalyVisual
    {
        private const float LIGHT_EPSILON = 0.001f;
        private const float LIGHT_EXPONENT = 0.15f;

        [SerializeField] private GameObject _dischargeTemplate;
        [SerializeField] private bool _freezeDischargeAtStart;
        [SerializeField] private int _dischargeRootIndex;
        [SerializeField] private int[] _stopEffectIndices;
        [SerializeField] private float[] _stopTimes;
        [SerializeField, Min(0f)] private float _groupDuration = 20f;
        [SerializeField, Min(0f)] private float _dischargeEffectDelay;
        [SerializeField] private Light _dischargeLight;
        [SerializeField, Min(0f)] private float _dischargeLightIntensity = 30000f;
        [SerializeField, Min(0f)] private float _dischargeLightRange = 9f;
        [SerializeField, Min(0f)] private float _dischargeLightDuration = 10f;
        [SerializeField, Min(0f)] private float _dischargeLightDelay;
        [SerializeField, ColorUsage(false, true)] private Color _dischargeLightColor = Color.white;
        [SerializeField] private Vector3 _dischargeLightLocalPosition;

        private readonly List<AnomalyParticlePlayback> _discharges = new List<AnomalyParticlePlayback>();

        private float _lightRemaining;
        private float _lightDelayRemaining;
        private bool _idleLightRequested;
        private Color _previousIdleLightColor;
        private Vector3 _previousIdleLightLocalPosition;
        private float _previousIdleLightIntensity;
        private float _previousIdleLightRange;

        public override bool IsDischarging => _discharges.Count > 0 || (ParticlePrototype != null && ParticlePrototype.IsDischarging);

        protected override bool HasDischarge => _dischargeTemplate != null;

        private bool SharesIdleLight => _dischargeLight != null && _dischargeLight == IdleIllumination;

        protected override bool StartDischarge()
        {
            if (ParticlePrototype != null)
            {
                if (!ParticlePrototype.StartDischarge(_dischargeEffectDelay)) return false;
            }
            else if (!StartParticleDischarge())
            {
                return false;
            }

            _lightRemaining = _dischargeLightDuration;
            _lightDelayRemaining = _dischargeLightDelay;
            UpdateLight(0f);
            return true;
        }

        private bool StartParticleDischarge()
        {
            GameObject instance = Instantiate(_dischargeTemplate, transform);
            instance.name = "Discharge";
            if (_freezeDischargeAtStart)
            {
                instance.transform.SetParent(null, true);
            }
            AnomalyParticlePlayback playback = new AnomalyParticlePlayback(instance, _dischargeRootIndex, _stopEffectIndices, _stopTimes, _groupDuration);
            if (playback.IsValid == false || playback.Play(_dischargeEffectDelay) == false)
            {
                Destroy(instance);
                Debug.LogError("Anomaly discharge template has no configured root ParticleSystem.", this);
                return false;
            }
            _discharges.Add(playback);
            TrimDischarges();
            return true;
        }

        protected override void StopDischarge()
        {
            _lightRemaining = 0f;
            _lightDelayRemaining = 0f;
            UpdateLight(0f);
            ClearDischarges();
            if (ParticlePrototype != null) ParticlePrototype.StopDischarges();
        }

        protected override void TickVisual(float deltaTime)
        {
            if (ParticlePrototype != null) ParticlePrototype.TickDischarges(deltaTime);
            TrimDischarges();
            for (int index = _discharges.Count - 1; index >= 0; index--)
            {
                AnomalyParticlePlayback discharge = _discharges[index];
                if (discharge.Tick(deltaTime) == false)
                {
                    Destroy(discharge.Root);
                    _discharges.RemoveAt(index);
                }
            }
            if (_lightRemaining > 0f)
            {
                UpdateLight(deltaTime);
            }
        }

        protected override void SetIdleLightEnabled(bool enabled)
        {
            if (SharesIdleLight)
            {
                _idleLightRequested = enabled;
                if (_lightRemaining > 0f)
                {
                    return;
                }
            }
            base.SetIdleLightEnabled(enabled);
        }

        private void UpdateLight(float deltaTime)
        {
            bool sharedLightNeedsUpdate = _lightRemaining > 0f || IdleLightOverridden;
            float activeDelta = Mathf.Max(0f, deltaTime - _lightDelayRemaining);
            _lightDelayRemaining = Mathf.Max(0f, _lightDelayRemaining - deltaTime);
            _lightRemaining = Mathf.Max(0f, _lightRemaining - activeDelta);
            if (_dischargeLight == null)
            {
                return;
            }

            bool active = _lightDelayRemaining <= 0f && _lightRemaining > 0f && _dischargeLightDuration > 0f;
            float strength = active ? Mathf.Pow(Mathf.Clamp01(_lightRemaining / _dischargeLightDuration) + LIGHT_EPSILON, LIGHT_EXPONENT) : 0f;
            if (SharesIdleLight)
            {
                if (sharedLightNeedsUpdate)
                {
                    UpdateSharedLight(active, strength);
                }
                return;
            }
            _dischargeLight.intensity = _dischargeLightIntensity * strength;
            _dischargeLight.range = Mathf.Max(0.001f, _dischargeLightRange * strength);
            _dischargeLight.enabled = active;
        }

        private void UpdateSharedLight(bool active, float strength)
        {
            if (active)
            {
                if (!IdleLightOverridden)
                {
                    IdleLightOverridden = true;
                    _previousIdleLightColor = _dischargeLight.color;
                    _previousIdleLightLocalPosition = _dischargeLight.transform.localPosition;
                    _previousIdleLightIntensity = _dischargeLight.intensity;
                    _previousIdleLightRange = _dischargeLight.range;
                    _dischargeLight.color = _dischargeLightColor;
                    _dischargeLight.transform.localPosition = _dischargeLightLocalPosition;
                }
                _dischargeLight.intensity = _dischargeLightIntensity * strength;
                _dischargeLight.range = Mathf.Max(0.001f, _dischargeLightRange * strength);
                base.SetIdleLightEnabled(true);
                return;
            }
            if (IdleLightOverridden)
            {
                IdleLightOverridden = false;
                _dischargeLight.color = _previousIdleLightColor;
                _dischargeLight.transform.localPosition = _previousIdleLightLocalPosition;
                _dischargeLight.intensity = _previousIdleLightIntensity;
                _dischargeLight.range = _previousIdleLightRange;
            }
            base.SetIdleLightEnabled(_lightRemaining <= 0f && _idleLightRequested);
        }

        private void ClearDischarges()
        {
            foreach (AnomalyParticlePlayback discharge in _discharges)
            {
                discharge.Stop();
                Destroy(discharge.Root);
            }
            _discharges.Clear();
        }

        private void TrimDischarges()
        {
            while (_discharges.Count > AnomalyVisualQuality.DischargeLimit)
            {
                AnomalyParticlePlayback oldest = _discharges[0];
                oldest.Stop();
                Destroy(oldest.Root);
                _discharges.RemoveAt(0);
            }
        }
    }
}
