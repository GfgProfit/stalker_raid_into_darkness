using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class ElectraVisual : AnomalyVisual
    {
        [SerializeField] private ParticleSystem _discharge;
        [SerializeField] private Light _idleLight;
        [SerializeField] private Light _dischargeLight;
        [SerializeField, Min(0f)] private float _idleLightIntensity = 300f;
        [SerializeField, Min(0f)] private float _dischargeLightIntensity = 30000f;
        [SerializeField] private AnimationCurve _dischargeLightEnvelope = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.08f, 0.45f), new Keyframe(0.1f, 0.9f), new Keyframe(0.25f, 0.25f), new Keyframe(0.65f, 0.07f), new Keyframe(1f, 0f));

        private bool _isDischarging;
        private float _dischargeLightTime;

        public override bool IsDischarging => _isDischarging;

        protected override bool HasDischarge => _discharge != null;

        protected override bool StartDischarge()
        {
            Clear(_discharge);
            _isDischarging = true;
            _dischargeLightTime = 0f;
            UpdateLights(0f);
            _discharge.Play(true);
            return true;
        }

        protected override void StopDischarge()
        {
            _isDischarging = false;
            SetLight(_dischargeLight, 0f);
            Clear(_discharge);
        }

        protected override void TickVisual(float deltaTime)
        {
            if (_isDischarging && _discharge != null && _discharge.IsAlive(true) == false)
            {
                ShowIdle();
            }

            UpdateLights(deltaTime);
        }

        private void UpdateLights(float deltaTime)
        {
            if (_idleLight != null)
            {
                _idleLight.enabled = false;
                _idleLight.intensity = _idleLightIntensity;
            }

            if (_isDischarging)
            {
                _dischargeLightTime += deltaTime;
                float strength = _dischargeLightEnvelope != null ? Mathf.Max(0f, _dischargeLightEnvelope.Evaluate(_dischargeLightTime)) : Mathf.Clamp01(1f - _dischargeLightTime);
                SetLight(_dischargeLight, _dischargeLightIntensity * strength);
            }
        }

        private static void SetLight(Light light, float intensity)
        {
            if (light != null)
            {
                light.intensity = intensity;
                light.enabled = intensity > 0f;
            }
        }
    }
}
