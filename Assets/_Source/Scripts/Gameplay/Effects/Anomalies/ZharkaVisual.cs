using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class ZharkaVisual : AnomalyVisual
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

        private readonly List<DischargeInstance> _discharges = new List<DischargeInstance>();

        private float _lightRemaining;

        public override bool IsDischarging => _discharges.Count > 0;

        protected override bool HasDischarge => _dischargeTemplate != null;

        protected override bool StartDischarge()
        {
            GameObject instance = Instantiate(_dischargeTemplate, transform);
            instance.name = "Discharge";
            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            if (_dischargeRootIndex < 0 || _dischargeRootIndex >= systems.Length)
            {
                Destroy(instance);
                Debug.LogError("Zharka discharge template has no configured root ParticleSystem.", this);
                return false;
            }
            instance.SetActive(true);
            _discharges.Add(new DischargeInstance(instance, systems));
            _lightRemaining = _dischargeLightDuration;
            UpdateLight(0f);
            systems[_dischargeRootIndex].Play(true);
            return true;
        }

        protected override void StopDischarge()
        {
            _lightRemaining = 0f;
            UpdateLight(0f);
            ClearDischarges();
        }

        protected override void TickVisual(float deltaTime)
        {
            for (int index = _discharges.Count - 1; index >= 0; index--)
            {
                DischargeInstance discharge = _discharges[index];
                float previous = discharge.Elapsed;
                discharge.Elapsed += deltaTime;
                if (_stopEffectIndices != null && _stopTimes != null)
                {
                    int count = Mathf.Min(_stopEffectIndices.Length, _stopTimes.Length);
                    for (int stopIndex = 0; stopIndex < count; stopIndex++)
                    {
                        if (previous < _stopTimes[stopIndex] && discharge.Elapsed >= _stopTimes[stopIndex])
                        {
                            int systemIndex = _stopEffectIndices[stopIndex];
                            if (systemIndex >= 0 && systemIndex < discharge.Systems.Length)
                            {
                                Clear(discharge.Systems[systemIndex]);
                            }
                        }
                    }
                }
                if (_groupDuration > 0f && previous < _groupDuration && discharge.Elapsed >= _groupDuration)
                {
                    foreach (ParticleSystem system in discharge.Systems)
                    {
                        system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
                bool alive = false;
                foreach (ParticleSystem system in discharge.Systems)
                {
                    alive |= system != null && system.IsAlive(false);
                }
                if (alive == false)
                {
                    Destroy(discharge.Root);
                    _discharges.RemoveAt(index);
                }
            }
            UpdateLight(deltaTime);
        }

        private void UpdateLight(float deltaTime)
        {
            _lightRemaining = Mathf.Max(0f, _lightRemaining - deltaTime);
            if (_dischargeLight == null)
            {
                return;
            }

            bool active = IsVisible && _lightRemaining > 0f && _dischargeLightDuration > 0f;
            float strength = active ? Mathf.Pow(Mathf.Clamp01(_lightRemaining / _dischargeLightDuration) + LIGHT_EPSILON, LIGHT_EXPONENT) : 0f;
            _dischargeLight.intensity = _dischargeLightIntensity * strength;
            _dischargeLight.range = Mathf.Max(0.001f, _dischargeLightRange * strength);
            _dischargeLight.enabled = active;
        }

        private void ClearDischarges()
        {
            foreach (DischargeInstance discharge in _discharges)
            {
                foreach (ParticleSystem system in discharge.Systems)
                {
                    Clear(system);
                }
                Destroy(discharge.Root);
            }
            _discharges.Clear();
        }

        private sealed class DischargeInstance
        {
            public GameObject Root { get; }
            public ParticleSystem[] Systems { get; }
            public float Elapsed { get; set; }

            public DischargeInstance(GameObject root, ParticleSystem[] systems)
            {
                Root = root;
                Systems = systems;
            }
        }
    }
}
