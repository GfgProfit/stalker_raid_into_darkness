using System;
using System.IO;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRayDetailVegetation))]
    [AddComponentMenu("X-Ray/Detail Vegetation Controller")]
    public sealed class XRayDetailVegetationController : MonoBehaviour
    {
        [SerializeField, Range(0.04f, 1.0f), InspectorName("Плотность травы")] private float _density;
        [SerializeField, Range(50, 250), InspectorName("Дистанция травы")] private int _radius;
        [SerializeField, Range(XRayGrassValues.MIN_HEIGHT, XRayGrassValues.MAX_HEIGHT), InspectorName("Высота травы")] private float _height;
        [SerializeField, InspectorName("Тени от травы")] private bool _shadows;
        [SerializeField, HideInInspector] private bool _initialized;
        [SerializeField, HideInInspector] private string _importRvmSnapshot;
        [SerializeField, HideInInspector] private string _rvmSnapshot;
        [SerializeField, HideInInspector] private int _overrideMask;
        private XRayDetailVegetation _vegetation;
        private XRayGrassSettings _settings;
        private bool _applyPending;
        private bool _playing;

        public static event Action PreviewRequested;

        public XRayGrassValues Values => new XRayGrassValues(_density, _radius, _height, _shadows);
        public string ImportRvmSnapshot => _importRvmSnapshot;
        public string RvmSnapshot => _rvmSnapshot;
        public int OverrideMask => _overrideMask;
        public XRayGrassValues RvmValues => LoadDefaults().ForLevel(_vegetation != null && _vegetation.Database != null ? _vegetation.Database.LevelName : null);

        private bool IsRuntime => Application.IsPlaying(gameObject);

        public void Initialize(XRayDetailVegetation vegetation)
        {
            _vegetation = vegetation;
            if (_vegetation == null || _vegetation.Database == null || !_vegetation.Database.HasRuntimeSource) return;
            if (_settings == null || _playing != IsRuntime)
            {
                if (_settings != null) _settings.Changed -= OnSettingsChanged;
                XRayGrassDefaults defaults = LoadDefaults();
                if (!_initialized)
                {
                    var initial = new XRayGrassSettings(defaults, Path.Combine(Application.persistentDataPath, "XRayGrassSettings.json"));
                    initial.SetLevel(_vegetation.Database.LevelName);
                    CopyValues(initial.Values);
                    _overrideMask = initial.HasOverrides ? 15 : 0;
                    _importRvmSnapshot = JsonUtility.ToJson(defaults);
                    _rvmSnapshot = _importRvmSnapshot;
                    _initialized = true;
                }

                XRayGrassValues values = Values;
                _settings = IsRuntime ? XRayGrassSettingsProvider.Ensure().Settings : new XRayGrassSettings(defaults, null);
                _playing = IsRuntime;
                if (_settings == null) return;
                if (IsRuntime)
                {
                    _settings.RefreshDefaults(defaults);
                    if (_settings.HasOverrides)
                    {
                        values = _settings.Values;
                        _overrideMask = 15;
                        CopyValues(values);
                    }
                }
                _settings.SetLevel(_vegetation.Database.LevelName);
                _settings.Set(values, false);
                _settings.Changed += OnSettingsChanged;
            }

            _vegetation.SetSettings(_settings);
            PreviewRequested?.Invoke();
        }

        public void SetValues(XRayGrassValues values)
        {
            if (!values.Valid) throw new ArgumentOutOfRangeException(nameof(values));
            _overrideMask |= DifferenceMask(Values, values);
            CopyValues(values);
            _initialized = true;
            Apply();
        }

        public void Apply()
        {
            _applyPending = false;
            _density = Mathf.Clamp(_density, 0.04f, 1.0f);
            _radius = Mathf.Clamp(_radius, 50, 250);
            _height = Mathf.Clamp(_height, XRayGrassValues.MIN_HEIGHT, XRayGrassValues.MAX_HEIGHT);
            if (_vegetation == null) _vegetation = GetComponent<XRayDetailVegetation>();
            Initialize(_vegetation);
            if (_settings == null) return;
            _settings.Set(Values, IsRuntime);
            PreviewRequested?.Invoke();
        }

        public void ApplyUserValues()
        {
            if (_settings != null) _overrideMask |= DifferenceMask(_settings.Values, Values);
            Apply();
        }

        public void ResetToRvm()
        {
            if (_vegetation == null) _vegetation = GetComponent<XRayDetailVegetation>();
            Initialize(_vegetation);
            if (_settings == null) return;
            _settings.RefreshDefaults(LoadDefaults());
            _settings.ResetToRvm();
            _overrideMask = 0;
            CopyValues(_settings.Values);
            PreviewRequested?.Invoke();
        }

        public void RefreshFromRvm(string json)
        {
            XRayGrassDefaults defaults = XRayGrassDefaults.Load(json);
            XRayGrassValues old = RvmValues;
            XRayGrassValues current = _initialized && Values.Valid ? Values : old;
            XRayGrassValues next = defaults.ForLevel(_vegetation != null && _vegetation.Database != null ? _vegetation.Database.LevelName : null);
            _overrideMask |= DifferenceMask(old, current);
            int overrides = _overrideMask;
            _rvmSnapshot = json;
            if (string.IsNullOrEmpty(_importRvmSnapshot)) _importRvmSnapshot = JsonUtility.ToJson(LoadFrozenDefaults());
            var merged = new XRayGrassValues((_overrideMask & 1) != 0 ? current.Density : next.Density, (_overrideMask & 2) != 0 ? current.Radius : next.Radius, (_overrideMask & 4) != 0 ? current.Height : next.Height, (_overrideMask & 8) != 0 ? current.Shadows : next.Shadows);
            if (_settings != null)
            {
                _settings.Changed -= OnSettingsChanged;
                _settings = null;
            }
            CopyValues(merged);
            _initialized = true;
            Apply();
            if (IsRuntime && _settings != null)
            {
                CopyValues(merged);
                _settings.Set(merged);
            }
            _overrideMask = overrides;
        }

        public void InitializeRvmSnapshot(string json)
        {
            XRayGrassDefaults.Load(json);
            _importRvmSnapshot = json;
            RefreshFromRvm(json);
        }

        private XRayGrassDefaults LoadDefaults()
        {
            return string.IsNullOrEmpty(_rvmSnapshot) ? LoadFrozenDefaults() : XRayGrassDefaults.Load(_rvmSnapshot);
        }

        private static XRayGrassDefaults LoadFrozenDefaults()
        {
            TextAsset profile = Resources.Load<TextAsset>("XRayRvmGrassDefaults");
            if (profile == null) throw new InvalidOperationException("DEFAULT INPUT BLOCKED: XRayRvmGrassDefaults is missing.");
            return XRayGrassDefaults.Load(profile.text);
        }

        private static int DifferenceMask(XRayGrassValues first, XRayGrassValues second)
        {
            return (first.Density != second.Density ? 1 : 0) | (first.Radius != second.Radius ? 2 : 0) | (first.Height != second.Height ? 4 : 0) | (first.Shadows != second.Shadows ? 8 : 0);
        }

        private void OnEnable()
        {
            Initialize(GetComponent<XRayDetailVegetation>());
        }

        private void OnDisable()
        {
            if (_settings != null) _settings.Changed -= OnSettingsChanged;
            _settings = null;
        }

        private void OnValidate()
        {
            _applyPending = true;
        }

        private void Update()
        {
            if (_playing != IsRuntime) Initialize(GetComponent<XRayDetailVegetation>());
            if (_applyPending) Apply();
            if (!Application.isPlaying && _vegetation != null && _vegetation.NeedsEditorUpdate) PreviewRequested?.Invoke();
        }

        private void OnSettingsChanged()
        {
            CopyValues(_settings.Values);
            PreviewRequested?.Invoke();
        }

        private void CopyValues(XRayGrassValues values)
        {
            _density = values.Density;
            _radius = values.Radius;
            _height = values.Height;
            _shadows = values.Shadows;
        }
    }
}
