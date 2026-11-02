using System;
using System.IO;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [Serializable]
    public struct XRayGrassValues
    {
        [SerializeField] private float _density;
        [SerializeField] private int _radius;
        [SerializeField] private float _height;
        [SerializeField] private bool _shadows;

        public XRayGrassValues(float density, int radius, float height, bool shadows)
        {
            _density = density;
            _radius = radius;
            _height = height;
            _shadows = shadows;
        }

        public float Density => _density;
        public int Radius => _radius;
        public float Height => _height;
        public bool Shadows => _shadows;
        public float FadeDistance => Mathf.Floor(_radius / 4.0f) * 4.0f - 0.5f;
        public bool Valid => !float.IsNaN(_density) && !float.IsInfinity(_density) && _density >= 0.04f && _density <= 1.0f && _radius >= 0 && _radius <= 250 && !float.IsNaN(_height) && !float.IsInfinity(_height) && _height >= 0.5f && _height <= 2.0f;
    }

    [Serializable]
    public sealed class XRayGrassLevelDefaults
    {
        [SerializeField] private string _level;
        [SerializeField] private XRayGrassValues _values;

        public string Level => _level;
        public XRayGrassValues Values => _values;
    }

    [Serializable]
    public sealed class XRayGrassDefaults
    {
        [SerializeField] private string _provenance;
        [SerializeField] private string _engineRevision;
        [SerializeField] private XRayGrassValues _values;
        [SerializeField] private XRayGrassLevelDefaults[] _levels;
        [SerializeField] private float _terrainSlope;
        [SerializeField] private bool _terrainAlignment;
        [SerializeField] private Vector4 _windGrass;
        [SerializeField] private Vector4 _windTrees;
        [SerializeField] private Vector4 _grassShadows;
        [SerializeField] private Vector3 _shadowCascades;

        public string Provenance => _provenance;
        public string EngineRevision => _engineRevision;
        public float TerrainSlope => _terrainSlope;
        public bool TerrainAlignment => _terrainAlignment;
        public Vector4 WindGrass => _windGrass;
        public Vector4 WindTrees => _windTrees;
        public Vector4 GrassShadows => _grassShadows;
        public Vector3 ShadowCascades => _shadowCascades;

        public XRayGrassValues ForLevel(string level)
        {
            if (_levels != null)
            {
                foreach (XRayGrassLevelDefaults entry in _levels)
                {
                    if (entry.Level == level) return entry.Values;
                }
            }

            return _values;
        }

        public static XRayGrassDefaults Load(string json)
        {
            var result = JsonUtility.FromJson<XRayGrassDefaults>(json);
            if (result == null || !result._values.Valid || string.IsNullOrEmpty(result._provenance) || string.IsNullOrEmpty(result._engineRevision))
            {
                throw new InvalidDataException("DEFAULT INPUT BLOCKED: a verified RVM grass profile is required.");
            }

            if (result._levels != null)
            {
                foreach (XRayGrassLevelDefaults entry in result._levels)
                {
                    if (!entry.Values.Valid) throw new InvalidDataException("DEFAULT INPUT BLOCKED: invalid per-level RVM grass profile.");
                }
            }

            return result;
        }

        public XRayGrassDefaults WithValues(XRayGrassValues values, string provenance)
        {
            if (!values.Valid || string.IsNullOrEmpty(provenance)) throw new ArgumentException(nameof(values));
            XRayGrassDefaults result = Load(JsonUtility.ToJson(this));
            result._values = values;
            result._levels = Array.Empty<XRayGrassLevelDefaults>();
            result._provenance = provenance;
            return result;
        }
    }

    public sealed class XRayGrassSettings
    {
        private XRayGrassDefaults _defaults;
        private readonly string _path;
        private XRayGrassValues _overrides;
        private bool _hasOverrides;
        private string _level;

        public XRayGrassSettings(XRayGrassDefaults defaults, string path)
        {
            _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
            _path = path;
            if (File.Exists(path))
            {
                try
                {
                    _overrides = JsonUtility.FromJson<XRayGrassValues>(File.ReadAllText(path));
                    _hasOverrides = _overrides.Valid;
                    if (!_hasOverrides) Debug.LogWarning("Invalid saved grass settings; using verified RVM defaults.");
                }
                catch (Exception exception) when (exception is IOException || exception is ArgumentException)
                {
                    Debug.LogWarning("Grass settings could not be loaded: " + exception.Message);
                }
            }
        }

        public event Action Changed;

        public XRayGrassDefaults Defaults => _defaults;
        public XRayGrassValues Values => _hasOverrides ? _overrides : _defaults.ForLevel(_level);
        public bool HasOverrides => _hasOverrides;

        public void SetLevel(string level)
        {
            if (_level == level) return;
            _level = level;
            Changed?.Invoke();
        }

        public void RefreshDefaults(XRayGrassDefaults defaults)
        {
            _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
            Changed?.Invoke();
        }

        public void Set(XRayGrassValues values, bool persist = true)
        {
            if (!values.Valid) throw new ArgumentOutOfRangeException(nameof(values));
            if (persist)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path + ".tmp", JsonUtility.ToJson(values, true));
                if (File.Exists(_path)) File.Replace(_path + ".tmp", _path, null);
                else File.Move(_path + ".tmp", _path);
            }
            _overrides = values;
            _hasOverrides = true;
            Changed?.Invoke();
        }

        public void ResetToRvm()
        {
            if (File.Exists(_path)) File.Delete(_path);
            _hasOverrides = false;
            Changed?.Invoke();
        }
    }
}
