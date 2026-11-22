using System;
using UnityEngine;

namespace RaidIntoDarkness.Weapons.Optics
{
    [Serializable]
    public sealed class ScopeOpticsState
    {
        [SerializeField] private float _magnification = 1f;
        [SerializeField] private float _targetMagnification = 1f;
        [SerializeField] private bool _active;
        [SerializeField] private float _windageMilliradians;
        [SerializeField] private float _elevationMilliradians;

        public float Magnification => _magnification;
        public float TargetMagnification => _targetMagnification;
        public bool Active => _active;
        public float WindageMilliradians => _windageMilliradians;
        public float ElevationMilliradians => _elevationMilliradians;

        public ScopeOpticsState()
        {
        }

        public ScopeOpticsState(ScopeOpticsConfig config)
        {
            if (config == null)
            {
                return;
            }

            _magnification = config.MinMagnification;
            _targetMagnification = _magnification;
            Sanitize(config);
        }

        public void SetActive(bool active)
        {
            _active = active;
        }

        public void SetMagnification(float value, ScopeOpticsConfig config, bool immediate = false)
        {
            if (config == null)
            {
                return;
            }

            _targetMagnification = config.ResolveMagnification(value);
            if (immediate || config.HasDiscreteMagnification)
            {
                _magnification = _targetMagnification;
            }
        }

        public void Tick(float deltaTime, float response, ScopeOpticsConfig config)
        {
            if (config == null)
            {
                return;
            }

            Sanitize(config);
            float duration = ScopeOpticsMath.FiniteClamp(deltaTime, 0f, 60f, 0f);
            float speed = ScopeOpticsMath.FiniteClamp(response, 0f, 1000f, 0f);
            float blend = speed <= 0f ? 1f : 1f - Mathf.Exp(-speed * duration);
            _magnification = Mathf.Lerp(_magnification, _targetMagnification, blend);
            if (Mathf.Abs(_magnification - _targetMagnification) < 0.00001f)
            {
                _magnification = _targetMagnification;
            }
        }

        public void SetCorrections(float windage, float elevation, ScopeOpticsConfig config)
        {
            if (config == null)
            {
                return;
            }

            float limit = config.MaximumCorrectionMilliradians;
            _windageMilliradians = ScopeOpticsMath.FiniteClamp(windage, -limit, limit, 0f);
            _elevationMilliradians = ScopeOpticsMath.FiniteClamp(elevation, -limit, limit, 0f);
        }

        public void Sanitize(ScopeOpticsConfig config)
        {
            if (config == null)
            {
                return;
            }

            _magnification = ScopeOpticsMath.ClampMagnification(_magnification, config.MinMagnification, config.MaxMagnification);
            _targetMagnification = ScopeOpticsMath.ClampMagnification(_targetMagnification, config.MinMagnification, config.MaxMagnification);
            SetCorrections(_windageMilliradians, _elevationMilliradians, config);
        }
    }
}
