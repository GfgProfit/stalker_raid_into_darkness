using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace XRay.Importer.Editor.Levels
{
    public enum XRayLevelDependencyCategory
    {
        Geometry,
        Materials,
        Lighting,
        VisualObjects,
        GameplayData,
        Unsupported
    }

    public enum XRayLevelDependencyStatus
    {
        Imported,
        Reused,
        Found,
        Missing,
        Unsupported,
        Skipped,
        Described
    }

    [Serializable]
    public sealed class XRayLevelDependency
    {
        [SerializeField] private string _category;
        [SerializeField] private string _role;
        [SerializeField] private string _reference;
        [SerializeField] private string _sourcePath;
        [SerializeField] private string _searchPath;
        [SerializeField] private string _status;
        [SerializeField] private string _purpose;
        [SerializeField] private string _assetPath;
        [SerializeField] private string _note;
        [SerializeField] private long _size;
        [SerializeField] private string _winner;
        [SerializeField] private string _origin;
        [SerializeField] private string _priority;
        [SerializeField] private string _losers;

        public XRayLevelDependency(XRayLevelDependencyCategory category, string role, string reference, string sourcePath, string searchPath, XRayLevelDependencyStatus status, string purpose, string assetPath, string note)
        {
            _category = category.ToString();
            _role = role;
            _reference = reference;
            _sourcePath = sourcePath;
            _searchPath = searchPath;
            _status = status.ToString();
            _purpose = purpose;
            _assetPath = assetPath;
            _note = note;
            _size = sourcePath != null && File.Exists(sourcePath) ? new FileInfo(sourcePath).Length : 0;
        }

        public string Category => _category;
        public string Role => _role;
        public string Reference => _reference;
        public string SourcePath => _sourcePath;
        public string Status => _status;
        public string AssetPath => _assetPath;
        public string Note => _note;
        public long Size => _size;
        public string SearchPath => _searchPath;
        public string Purpose => _purpose;
        public string Winner => _winner;
        public string Origin => _origin;
        public string Priority => _priority;
        public string Losers => _losers;

        public void SetSource(string winner, string origin, string priority, long size, string losers)
        {
            _winner = winner;
            _origin = origin;
            _priority = priority;
            _size = size;
            _losers = losers;
        }
    }

    [Serializable]
    public sealed class XRayLevelMetric
    {
        [SerializeField] private string _name;
        [SerializeField] private double _value;

        public XRayLevelMetric(string name, double value)
        {
            _name = name;
            _value = value;
        }

        public string Name => _name;

        public double Value
        {
            get => _value;
            set => _value = value;
        }
    }

    [Serializable]
    public sealed class XRayLevelImportRecord
    {
        [SerializeField] private string _levelName;
        [SerializeField] private string _sourceFolder;
        [SerializeField] private string _sourceLevelPath;
        [SerializeField] private string _sourceLevelSha256;
        [SerializeField] private string _identification;
        [SerializeField] private int _xrlcVersion;
        [SerializeField] private int _xrlcQuality;
        [SerializeField] private string _importedUtc;
        [SerializeField] private string _scenePath;
        [SerializeField] private string _prefabPath;
        [SerializeField] private string _settings;
        [SerializeField] private bool _completed;
        [SerializeField] private bool _cancelled;
        [SerializeField] private List<XRayLevelMetric> _metrics = new List<XRayLevelMetric>();
        [SerializeField] private List<XRayLevelDependency> _dependencies = new List<XRayLevelDependency>();
        [SerializeField] private List<string> _vertexLayouts = new List<string>();
        [SerializeField] private List<string> _shaderClasses = new List<string>();
        [SerializeField] private List<string> _generatedAssets = new List<string>();
        [SerializeField] private List<string> _warnings = new List<string>();
        [SerializeField] private List<string> _errors = new List<string>();
        [SerializeField] private List<string> _limitations = new List<string>();
        [SerializeField] private XRayLevelSourceProvenance _source = new XRayLevelSourceProvenance();

        public string LevelName
        {
            get => _levelName;
            set => _levelName = value;
        }

        public string SourceFolder
        {
            get => _sourceFolder;
            set => _sourceFolder = value;
        }

        public string SourceLevelPath
        {
            get => _sourceLevelPath;
            set => _sourceLevelPath = value;
        }

        public string SourceLevelSha256
        {
            get => _sourceLevelSha256;
            set => _sourceLevelSha256 = value;
        }

        public string Identification
        {
            get => _identification;
            set => _identification = value;
        }

        public int XrlcVersion
        {
            get => _xrlcVersion;
            set => _xrlcVersion = value;
        }

        public int XrlcQuality
        {
            get => _xrlcQuality;
            set => _xrlcQuality = value;
        }

        public string ImportedUtc
        {
            get => _importedUtc;
            set => _importedUtc = value;
        }

        public string ScenePath
        {
            get => _scenePath;
            set => _scenePath = value;
        }

        public string PrefabPath
        {
            get => _prefabPath;
            set => _prefabPath = value;
        }

        public string Settings
        {
            get => _settings;
            set => _settings = value;
        }

        public bool Completed
        {
            get => _completed;
            set => _completed = value;
        }

        public bool Cancelled
        {
            get => _cancelled;
            set => _cancelled = value;
        }

        public List<XRayLevelMetric> Metrics => _metrics;
        public List<XRayLevelDependency> Dependencies => _dependencies;
        public List<string> VertexLayouts => _vertexLayouts;
        public List<string> ShaderClasses => _shaderClasses;
        public List<string> GeneratedAssets => _generatedAssets;
        public List<string> Warnings => _warnings;
        public List<string> Errors => _errors;
        public List<string> Limitations => _limitations;
        public XRayLevelSourceProvenance Source => _source ?? (_source = new XRayLevelSourceProvenance());

        public void SetMetric(string name, double value)
        {
            XRayLevelMetric metric = _metrics.Find(item => item.Name == name);
            if (metric == null)
            {
                _metrics.Add(new XRayLevelMetric(name, value));
                return;
            }

            metric.Value = value;
        }

        public void AddMetric(string name, double value)
        {
            SetMetric(name, GetMetric(name) + value);
        }

        public double GetMetric(string name)
        {
            XRayLevelMetric metric = _metrics.Find(item => item.Name == name);
            return metric != null ? metric.Value : 0.0;
        }

        public XRayLevelDependency AddDependency(XRayLevelDependencyCategory category, string role, string reference, string sourcePath, string searchPath, XRayLevelDependencyStatus status, string purpose, string assetPath = null, string note = null)
        {
            var dependency = new XRayLevelDependency(category, role, reference, sourcePath, searchPath, status, purpose, assetPath, note);
            _dependencies.Add(dependency);
            return dependency;
        }

        public void AddLimitation(string text)
        {
            if (!_limitations.Contains(text))
            {
                _limitations.Add(text);
            }
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }
    }
}
