using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer
{
    [ExecuteAlways]
    public sealed class MonolithSourceLighting : MonoBehaviour
    {
        public enum LightingMode
        {
            Native,
            Calibrated,
            Bound
        }

        private const int MAX_LIGHTS = 64;

        private static readonly int _modeId = Shader.PropertyToID("_MonolithSourceLightingMode");
        private static readonly int _countId = Shader.PropertyToID("_MonolithBoundLightCount");
        private static readonly int _positionId = Shader.PropertyToID("_MonolithBoundPositionType64");
        private static readonly int _directionId = Shader.PropertyToID("_MonolithBoundDirectionRange64");
        private static readonly int _colorId = Shader.PropertyToID("_MonolithBoundColor64");
        private static readonly int _calibrationId = Shader.PropertyToID("_MonolithLightCalibration");
        private static readonly int _radianceScaleId = Shader.PropertyToID("_MonolithBoundRadianceScale");
        private static MonolithSourceLighting _owner;

        [SerializeField] private LightingMode _mode;
        [SerializeField] private Vector2 _calibration = Vector2.one;
        [SerializeField] private Light[] _lights = Array.Empty<Light>();
        [SerializeField] private Vector3[] _sourceColors = Array.Empty<Vector3>();
        [SerializeField, Min(0)] private float _boundRadianceScale = 1.0f;

        private readonly Vector4[] _positions = new Vector4[MAX_LIGHTS];
        private readonly Vector4[] _directions = new Vector4[MAX_LIGHTS];
        private readonly Vector4[] _colors = new Vector4[MAX_LIGHTS];
        private UnityEngine.Object _weatherOwner;
        private Light _weatherLight;
        private Vector3 _weatherColor;
        private Vector3 _weatherDirection;

        public bool TrySetWeatherLight(UnityEngine.Object owner, Light light, Vector3 sourceColor, Vector3 direction)
        {
            if (owner == null || light == null || !light.isActiveAndEnabled || light.type != LightType.Directional || !isActiveAndEnabled || _owner != null && _owner != this || _weatherOwner != null && _weatherOwner != owner) return false;
            for (int i = 0; i < 3; i++)
            {
                if (float.IsNaN(sourceColor[i]) || float.IsInfinity(sourceColor[i]) || sourceColor[i] < 0.0f || float.IsNaN(direction[i]) || float.IsInfinity(direction[i])) throw new ArgumentOutOfRangeException(nameof(sourceColor));
            }
            if (direction.sqrMagnitude < 0.000001f) throw new ArgumentOutOfRangeException(nameof(direction));
            int count = 0;
            bool bound = false;
            int length = _lights != null && _sourceColors != null ? Mathf.Min(MAX_LIGHTS, Mathf.Min(_lights.Length, _sourceColors.Length)) : 0;
            for (int i = 0; i < length; i++)
            {
                Light candidate = _lights[i];
                if (candidate == light) bound = true;
                if (candidate != null && candidate.isActiveAndEnabled && (candidate.type == LightType.Directional || candidate.type == LightType.Point || candidate.type == LightType.Spot)) count++;
            }
            if (!bound && count >= MAX_LIGHTS) return false;
            _owner = this;
            _weatherOwner = owner;
            _weatherLight = light;
            _weatherColor = sourceColor;
            _weatherDirection = direction.normalized;
            Publish();
            return true;
        }

        public void ReleaseWeatherLight(UnityEngine.Object owner)
        {
            if (_weatherOwner != owner) return;
            _weatherOwner = null;
            _weatherLight = null;
            Publish();
        }

        public LightingMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                Publish();
            }
        }

        public float BoundRadianceScale
        {
            get => _boundRadianceScale;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _boundRadianceScale = value;
                Publish();
            }
        }

        public void SetBindings(Light[] lights, Vector3[] sourceColors)
        {
            if (lights == null || sourceColors == null || lights.Length != sourceColors.Length || lights.Length > MAX_LIGHTS) throw new ArgumentException("Supply up to 64 lights with one encoded X-Ray RGB value per light.");
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null || lights[i].type != LightType.Directional && lights[i].type != LightType.Point && lights[i].type != LightType.Spot) throw new ArgumentException("Only directional, point and spot lights can be bound.");
                for (int c = 0; c < 3; c++)
                {
                    float value = sourceColors[i][c];
                    if (float.IsNaN(value) || float.IsInfinity(value) || value < 0.0f) throw new ArgumentOutOfRangeException(nameof(sourceColors));
                }
            }
            _lights = (Light[])lights.Clone();
            _sourceColors = (Vector3[])sourceColors.Clone();
            Publish();
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            if (_owner != null && _owner != this)
            {
                Debug.LogError("Only one MonolithSourceLighting controller can be active.", this);
                return;
            }
            _owner = this;
            Publish();
        }

        private void OnDisable()
        {
            _weatherOwner = null;
            _weatherLight = null;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            if (_owner != this) return;
            Shader.SetGlobalFloat(_modeId, 0.0f);
            Shader.SetGlobalInt(_countId, 0);
            _owner = null;
        }

        private void LateUpdate()
        {
            Publish();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            Publish();
        }

        public void Publish()
        {
            if (_owner == null && isActiveAndEnabled) _owner = this;
            if (_owner != this || !isActiveAndEnabled) return;
            int count = 0;
            bool weatherBound = false;
            int length = _lights != null && _sourceColors != null ? Mathf.Min(MAX_LIGHTS, Mathf.Min(_lights.Length, _sourceColors.Length)) : 0;
            for (int i = 0; i < length; i++)
            {
                Light light = _lights[i];
                if (light == null || !light.isActiveAndEnabled) continue;
                float type = light.type == LightType.Directional ? 0.0f : light.type == LightType.Point ? 1.0f : light.type == LightType.Spot ? 2.0f : -1.0f;
                if (type < 0.0f) continue;
                Vector3 position = light.transform.position;
                Vector3 direction = light.transform.forward;
                bool weather = _weatherOwner != null && light == _weatherLight;
                if (weather) direction = _weatherDirection;
                _positions[count] = new Vector4(position.x, position.y, position.z, type);
                _directions[count] = new Vector4(direction.x, direction.y, direction.z, light.range);
                Vector3 color = weather ? _weatherColor : _sourceColors[i];
                _colors[count] = new Vector4(color.x, color.y, color.z, light.type == LightType.Point && light.shadows == LightShadows.None ? 2.0f : 1.0f);
                weatherBound |= weather;
                count++;
            }
            if (!weatherBound && count < MAX_LIGHTS && _weatherOwner != null && _weatherLight != null && _weatherLight.isActiveAndEnabled)
            {
                Vector3 position = _weatherLight.transform.position;
                _positions[count] = new Vector4(position.x, position.y, position.z, 0.0f);
                _directions[count] = new Vector4(_weatherDirection.x, _weatherDirection.y, _weatherDirection.z, _weatherLight.range);
                _colors[count] = new Vector4(_weatherColor.x, _weatherColor.y, _weatherColor.z, 1.0f);
                count++;
            }
            Shader.SetGlobalVectorArray(_positionId, _positions);
            Shader.SetGlobalVectorArray(_directionId, _directions);
            Shader.SetGlobalVectorArray(_colorId, _colors);
            Shader.SetGlobalVector(_calibrationId, new Vector4(Mathf.Max(0.0f, _calibration.x), Mathf.Max(0.0f, _calibration.y), 0.0f, 0.0f));
            Shader.SetGlobalFloat(_radianceScaleId, _boundRadianceScale);
            Shader.SetGlobalInt(_countId, count);
            Shader.SetGlobalFloat(_modeId, (float)_mode);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGlobals()
        {
            _owner = null;
            Shader.SetGlobalFloat(_modeId, 0.0f);
            Shader.SetGlobalInt(_countId, 0);
        }
    }
}
