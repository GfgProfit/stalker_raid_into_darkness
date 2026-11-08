using System;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class ElectraVisual : MonoBehaviour
    {
        [SerializeField] private ParticleSystem _idle;
        [SerializeField] private ParticleSystem _discharge;
        [SerializeField] private Light _idleLight;
        [SerializeField] private Light _dischargeLight;
        [SerializeField] private ElectraAudio _audio;
        [SerializeField, Min(0f)] private float _idleLightIntensity = 300f;
        [SerializeField, Min(0f)] private float _dischargeLightIntensity = 30000f;
        [SerializeField] private AnimationCurve _dischargeLightEnvelope = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.08f, 0.45f), new Keyframe(0.1f, 0.9f), new Keyframe(0.25f, 0.25f), new Keyframe(0.65f, 0.07f), new Keyframe(1f, 0f));

        private bool _isDischarging;
        private bool _isVisible;
        private float _dischargeLightTime;

        public bool IsDischarging => _isDischarging;
        public bool IsVisible => _isVisible;
        public ElectraAudio Audio => _audio;

        public event Action DischargeStarted;
        public event Action Hidden;

        [ContextMenu("Visual/Show Idle")]
        public void ShowIdle()
        {
            Clear(_discharge);
            _isDischarging = false;
            _isVisible = isActiveAndEnabled;
            SetLight(_dischargeLight, 0f);
            UpdateLights(0f);

            if (_audio != null)
            {
                _audio.SetVisible(_isVisible);
            }

            if (_idle != null && isActiveAndEnabled)
            {
                _idle.Play(false);
            }
        }

        [ContextMenu("Visual/Play Discharge")]
        public void PlayDischarge()
        {
            if (isActiveAndEnabled == false || _discharge == null)
            {
                return;
            }

            Clear(_idle);
            Clear(_discharge);
            _isDischarging = true;
            _isVisible = true;
            _dischargeLightTime = 0f;
            UpdateLights(0f);
            _discharge.Play(true);

            if (_audio != null)
            {
                _audio.PlayDischarge();
            }
            DischargeStarted?.Invoke();
        }

        [ContextMenu("Visual/Hide")]
        public void Hide()
        {
            _isDischarging = false;
            _isVisible = false;
            SetLight(_dischargeLight, 0f);
            UpdateLights(0f);
            Clear(_idle);
            Clear(_discharge);

            if (_audio != null)
            {
                _audio.SetVisible(false);
            }
            Hidden?.Invoke();
        }

        private void OnEnable()
        {
            ShowIdle();
        }

        private void OnDisable()
        {
            Hide();
        }

        private void Update()
        {
            if (_isDischarging && _discharge != null && _discharge.IsAlive(true) == false)
            {
                ShowIdle();
            }

            UpdateLights(Time.deltaTime);
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

        private static void Clear(ParticleSystem system)
        {
            if (system != null)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
