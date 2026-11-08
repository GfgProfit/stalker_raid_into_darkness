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
        [SerializeField] private int _dischargeRootIndex;
        [SerializeField] private int[] _stopEffectIndices;
        [SerializeField] private float[] _stopTimes;
        [SerializeField, Min(0f)] private float _groupDuration = 20f;
        [SerializeField] private Light _dischargeLight;
        [SerializeField, Min(0f)] private float _dischargeLightIntensity = 30000f;
        [SerializeField, Min(0f)] private float _dischargeLightRange = 9f;
        [SerializeField, Min(0f)] private float _dischargeLightDuration = 10f;
        [SerializeField, Min(0f)] private float _dischargeLightDelay;

        private readonly List<AnomalyParticlePlayback> _discharges = new List<AnomalyParticlePlayback>();

        private float _lightRemaining;
        private float _lightDelayRemaining;

        public override bool IsDischarging => _discharges.Count > 0;

        protected override bool HasDischarge => _dischargeTemplate != null;

        protected override bool StartDischarge()
        {
            GameObject instance = Instantiate(_dischargeTemplate, transform);
            instance.name = "Discharge";
            AnomalyParticlePlayback playback = new AnomalyParticlePlayback(instance, _dischargeRootIndex, _stopEffectIndices, _stopTimes, _groupDuration);
            if (playback.IsValid == false || playback.Play() == false)
            {
                Destroy(instance);
                Debug.LogError("Anomaly discharge template has no configured root ParticleSystem.", this);
                return false;
            }
            _discharges.Add(playback);
            _lightRemaining = _dischargeLightDuration;
            _lightDelayRemaining = _dischargeLightDelay;
            UpdateLight(0f);
            return true;
        }

        protected override void StopDischarge()
        {
            _lightRemaining = 0f;
            _lightDelayRemaining = 0f;
            UpdateLight(0f);
            ClearDischarges();
        }

        protected override void TickVisual(float deltaTime)
        {
            for (int index = _discharges.Count - 1; index >= 0; index--)
            {
                AnomalyParticlePlayback discharge = _discharges[index];
                if (discharge.Tick(deltaTime) == false)
                {
                    Destroy(discharge.Root);
                    _discharges.RemoveAt(index);
                }
            }
            UpdateLight(deltaTime);
        }

        private void UpdateLight(float deltaTime)
        {
            float activeDelta = Mathf.Max(0f, deltaTime - _lightDelayRemaining);
            _lightDelayRemaining = Mathf.Max(0f, _lightDelayRemaining - deltaTime);
            _lightRemaining = Mathf.Max(0f, _lightRemaining - activeDelta);
            if (_dischargeLight == null)
            {
                return;
            }

            bool active = IsVisible && _lightDelayRemaining <= 0f && _lightRemaining > 0f && _dischargeLightDuration > 0f;
            float strength = active ? Mathf.Pow(Mathf.Clamp01(_lightRemaining / _dischargeLightDuration) + LIGHT_EPSILON, LIGHT_EXPONENT) : 0f;
            _dischargeLight.intensity = _dischargeLightIntensity * strength;
            _dischargeLight.range = Mathf.Max(0.001f, _dischargeLightRange * strength);
            _dischargeLight.enabled = active;
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
    }
}
