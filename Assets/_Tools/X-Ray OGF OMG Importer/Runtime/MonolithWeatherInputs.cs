using System;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer
{
    [ExecuteAlways]
    public sealed class MonolithWeatherInputs : MonoBehaviour
    {
        private static readonly int _activeId = Shader.PropertyToID("_MonolithWeatherActive");
        private static readonly int _stateId = Shader.PropertyToID("_MonolithWeatherState");
        private static readonly int _sunId = Shader.PropertyToID("_MonolithWeatherSun");
        private static readonly int _lightSetupId = Shader.PropertyToID("_MonolithWeatherLightSetup");
        private static readonly int _floraId = Shader.PropertyToID("_MonolithWeatherFloraFixes");
        private static readonly int _environmentActiveId = Shader.PropertyToID("_MonolithWeatherEnvironmentActive");
        private static readonly int _environmentWeightsId = Shader.PropertyToID("_MonolithWeatherEnvironmentWeights");
        private static readonly int _environmentColorId = Shader.PropertyToID("_MonolithWeatherEnvironmentColor");
        private static readonly int _ambientId = Shader.PropertyToID("_MonolithWeatherAmbient");
        private static readonly int[] _environmentIds = { Shader.PropertyToID("_MonolithWeatherEnvironment0"), Shader.PropertyToID("_MonolithWeatherEnvironment1"), Shader.PropertyToID("_MonolithWeatherEnvironment2"), Shader.PropertyToID("_MonolithWeatherEnvironment3") };
        private static MonolithWeatherInputs _owner;

        [SerializeField, Range(0.0f, 1.0f)] private float _wetness;
        [SerializeField] private Vector3 _sunColor = Vector3.one;
        [SerializeField] private Vector4 _lightSetup = new Vector4(0.5f, 1.0f, 0.0f, 0.0f);
        [SerializeField] private Vector4 _floraFixes = new Vector4(1.5f, 1.0f, 0.0f, 0.0f);

        private float _dynamicGloss = 0.71864f;
        private bool _pendingValidation;
        private UnityEngine.Object _weatherOwner;
        private Vector3 _weatherSunColor;
        private Vector4? _weatherLightSetup;
        private Vector4? _weatherFloraFixes;
        private XRayWeatherResources _weatherEnvironment;
        private Vector4 _weatherEnvironmentColor;
        private Vector4 _weatherAmbient;
        private bool _environmentReady;

        public float DynamicGloss => _dynamicGloss;
        public float Wetness => _wetness;
        public Vector3 SunColor => _weatherOwner != null ? _weatherSunColor : _sunColor;
        public Vector4 LightSetup => _weatherOwner != null && _weatherLightSetup.HasValue ? _weatherLightSetup.Value : _lightSetup;
        public Vector4 FloraFixes => _weatherOwner != null && _weatherFloraFixes.HasValue ? _weatherFloraFixes.Value : _floraFixes;
        public bool EnvironmentActive => _weatherOwner != null && _environmentReady;

        public void SetWeatherEnvironment(UnityEngine.Object owner, XRayWeatherSnapshot snapshot, Vector2? scale)
        {
            if (_weatherOwner != owner || owner == null) return;
            _weatherEnvironment = snapshot.EnvironmentResources;
            _environmentReady = scale.HasValue && Mathf.Abs(Vector4.Dot(_weatherEnvironment.Weights, Vector4.one) - 1.0f) < 0.0001f;
            for (int i = 0; i < 4; i++)
            {
                if (_weatherEnvironment.Weights[i] > 0.0f && !(_weatherEnvironment.Texture(i) is Cubemap)) _environmentReady = false;
            }
            if (_environmentReady)
            {
                Vector2 value = scale.Value;
                _weatherAmbient = new Vector4(Mathf.Max(snapshot.AmbientColor.r * 2.0f, 0.001f) * value.x, Mathf.Max(snapshot.AmbientColor.g * 2.0f, 0.001f) * value.x, Mathf.Max(snapshot.AmbientColor.b * 2.0f, 0.001f) * value.x, 0.0f);
                _weatherEnvironmentColor = new Vector4((snapshot.HemisphereColor.r * 2.0f + 0.00001f) * 2.0f * value.y, (snapshot.HemisphereColor.g * 2.0f + 0.00001f) * 2.0f * value.y, (snapshot.HemisphereColor.b * 2.0f + 0.00001f) * 2.0f * value.y, 0.0f);
            }
            Publish();
        }

        public bool TrySetWeatherInputs(UnityEngine.Object owner, Vector3 sunColor, Vector4? lightSetup = null, Vector4? floraFixes = null)
        {
            if (owner == null || !isActiveAndEnabled || _weatherOwner != null && _weatherOwner != owner) return false;
            for (int i = 0; i < 3; i++)
            {
                Validate(sunColor[i]);
                if (sunColor[i] < 0.0f) throw new ArgumentOutOfRangeException(nameof(sunColor));
            }
            for (int i = 0; i < 4; i++)
            {
                if (lightSetup.HasValue) Validate(lightSetup.Value[i]);
                if (floraFixes.HasValue) Validate(floraFixes.Value[i]);
            }
            if (_weatherOwner != owner) _environmentReady = false;
            _weatherOwner = owner;
            _weatherSunColor = sunColor;
            _weatherLightSetup = lightSetup;
            _weatherFloraFixes = floraFixes;
            Publish();
            return true;
        }

        public void ReleaseWeatherInputs(UnityEngine.Object owner)
        {
            if (_weatherOwner != owner) return;
            _weatherOwner = null;
            _weatherLightSetup = null;
            _weatherFloraFixes = null;
            _environmentReady = false;
            Publish();
        }

        public static float EvaluateGloss(float minimum, float maximum, float autoMaximum, float wetness)
        {
            float factor = (minimum + Mathf.Max(autoMaximum - minimum, 0.0f) * Mathf.Clamp01(wetness)) * 0.96f;
            return minimum + Mathf.Clamp01(maximum - minimum) * factor;
        }

        public void SetWetness(float wetness)
        {
            Validate(wetness);
            _wetness = Mathf.Clamp01(wetness);
            _dynamicGloss = EvaluateGloss(0.65f, 0.76f, 1.0f, _wetness);
            Publish();
        }

        public void SetInputs(float dynamicGloss, Vector3 sunColor, Vector4 lightSetup, Vector4 floraFixes, float wetness)
        {
            Validate(dynamicGloss);
            Validate(wetness);
            for (int i = 0; i < 3; i++) Validate(sunColor[i]);
            for (int i = 0; i < 4; i++)
            {
                Validate(lightSetup[i]);
                Validate(floraFixes[i]);
            }
            if (dynamicGloss < 0.0f || dynamicGloss >= 0.99f || sunColor.x < 0.0f || sunColor.y < 0.0f || sunColor.z < 0.0f) throw new ArgumentOutOfRangeException(nameof(dynamicGloss));
            _dynamicGloss = dynamicGloss;
            _sunColor = sunColor;
            _lightSetup = lightSetup;
            _floraFixes = floraFixes;
            _wetness = Mathf.Clamp01(wetness);
            Publish();
        }

        private static void Validate(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        }

        private void OnEnable()
        {
            if (_owner != null && _owner != this)
            {
                Debug.LogError("Only one MonolithWeatherInputs provider can be active.", this);
                enabled = false;
                return;
            }
            _owner = this;
            _pendingValidation = false;
            SetWetness(_wetness);
        }

        private void OnDisable()
        {
            _weatherOwner = null;
            _environmentReady = false;
            if (_owner != this) return;
            Shader.SetGlobalFloat(_activeId, 0.0f);
            Shader.SetGlobalFloat(_environmentActiveId, 0.0f);
            _owner = null;
        }

        private void OnValidate()
        {
            _pendingValidation = true;
        }

        private void LateUpdate()
        {
            if (_owner == null)
            {
                _owner = this;
                Publish();
            }
            if (_owner == this && _pendingValidation)
            {
                _pendingValidation = false;
                SetWetness(_wetness);
            }
        }

        private void Publish()
        {
            if (_owner != this || !isActiveAndEnabled) return;
            Shader.SetGlobalVector(_stateId, new Vector4(_dynamicGloss, _wetness, 0.0f, 0.0f));
            Shader.SetGlobalVector(_sunId, SunColor);
            Shader.SetGlobalVector(_lightSetupId, LightSetup);
            Shader.SetGlobalVector(_floraId, FloraFixes);
            Shader.SetGlobalFloat(_environmentActiveId, EnvironmentActive ? 1.0f : 0.0f);
            if (EnvironmentActive)
            {
                for (int i = 0; i < 4; i++) Shader.SetGlobalTexture(_environmentIds[i], _weatherEnvironment.Texture(i));
                Shader.SetGlobalVector(_environmentWeightsId, new Vector4(_weatherEnvironment.Weights.x, _weatherEnvironment.Weights.y, _weatherEnvironment.Weights.z, _weatherEnvironment.Weights.w));
                Shader.SetGlobalVector(_environmentColorId, _weatherEnvironmentColor);
                Shader.SetGlobalVector(_ambientId, _weatherAmbient);
            }
            Shader.SetGlobalFloat(_activeId, 1.0f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGlobals()
        {
            _owner = null;
            Shader.SetGlobalFloat(_activeId, 0.0f);
            Shader.SetGlobalFloat(_environmentActiveId, 0.0f);
        }
    }
}
