using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace XRay.Importer.Editor
{
    public enum XRayResourceStatus
    {
        Imported,
        Reused,
        Updated,
        Converted,
        Missing,
        Unsupported,
        Pending,
        Skipped,
        Preserved,
        Created
    }

    [Serializable]
    public sealed class XRayReportSource
    {
        [SerializeField] private string _name;
        [SerializeField] private string _kind;
        [SerializeField] private string _priority;
        [SerializeField] private string _fullPath;

        public XRayReportSource(string name, string kind, string priority, string fullPath)
        {
            _name = name;
            _kind = kind;
            _priority = priority;
            _fullPath = fullPath;
        }

        public string Name => _name;
        public string Kind => _kind;
        public string Priority => _priority;
        public string FullPath => _fullPath;
    }

    [Serializable]
    public sealed class XRayReportTexture
    {
        [SerializeField] private string _role;
        [SerializeField] private string _requested;
        [SerializeField] private XRayResourceStatus _status;
        [SerializeField] private string _assetPath;
        [SerializeField] private string _format;
        [SerializeField] private string _note;
        [SerializeField] private bool _hasWinner;
        [SerializeField] private XRayReportSource _winner;
        [SerializeField] private List<XRayReportSource> _losers = new List<XRayReportSource>();

        public string Role
        {
            get => _role;
            set => _role = value;
        }

        public string Requested
        {
            get => _requested;
            set => _requested = value;
        }

        public XRayResourceStatus Status
        {
            get => _status;
            set => _status = value;
        }

        public string AssetPath
        {
            get => _assetPath;
            set => _assetPath = value;
        }

        public string Format
        {
            get => _format;
            set => _format = value;
        }

        public string Note
        {
            get => _note;
            set => _note = value;
        }

        public XRayReportSource Winner
        {
            get => _hasWinner ? _winner : null;
            set
            {
                _winner = value;
                _hasWinner = value != null;
            }
        }

        public List<XRayReportSource> Losers => _losers;
    }

    [Serializable]
    public sealed class XRayReportMaterial
    {
        [SerializeField] private int _index;
        [SerializeField] private string _key;
        [SerializeField] private string _texture;
        [SerializeField] private string _shader;
        [SerializeField] private string _surfaceRule;
        [SerializeField] private string _surfaceKind;
        [SerializeField] private string _materialPath;
        [SerializeField] private XRayResourceStatus _materialStatus;
        [SerializeField] private string _descriptionOrigin;
        [SerializeField] private List<string> _meshes = new List<string>();
        [SerializeField] private List<XRayReportTexture> _textures = new List<XRayReportTexture>();
        [SerializeField] private List<string> _notes = new List<string>();

        public int Index
        {
            get => _index;
            set => _index = value;
        }

        public string Key
        {
            get => _key;
            set => _key = value;
        }

        public string Texture
        {
            get => _texture;
            set => _texture = value;
        }

        public string Shader
        {
            get => _shader;
            set => _shader = value;
        }

        public string SurfaceRule
        {
            get => _surfaceRule;
            set => _surfaceRule = value;
        }

        public string SurfaceKind
        {
            get => _surfaceKind;
            set => _surfaceKind = value;
        }

        public string MaterialPath
        {
            get => _materialPath;
            set => _materialPath = value;
        }

        public XRayResourceStatus MaterialStatus
        {
            get => _materialStatus;
            set => _materialStatus = value;
        }

        public string DescriptionOrigin
        {
            get => _descriptionOrigin;
            set => _descriptionOrigin = value;
        }

        public List<string> Meshes => _meshes;
        public List<XRayReportTexture> Textures => _textures;
        public List<string> Notes => _notes;
    }

    [Serializable]
    public sealed class XRayResourceReport
    {
        [SerializeField] private string _modelPath;
        [SerializeField] private string _profile;
        [SerializeField] private string _sourceMode;
        [SerializeField] private string _indexFingerprint;
        [SerializeField] private long _createdUtcTicks;
        [SerializeField] private bool _isComplete;
        [SerializeField] private List<XRayReportMaterial> _materials = new List<XRayReportMaterial>();
        [SerializeField] private List<string> _errors = new List<string>();
        [SerializeField] private List<string> _warnings = new List<string>();

        public string ModelPath
        {
            get => _modelPath;
            set => _modelPath = value;
        }

        public string Profile
        {
            get => _profile;
            set => _profile = value;
        }

        public string SourceMode
        {
            get => _sourceMode;
            set => _sourceMode = value;
        }

        public string IndexFingerprint
        {
            get => _indexFingerprint;
            set => _indexFingerprint = value;
        }

        public long CreatedUtcTicks
        {
            get => _createdUtcTicks;
            set => _createdUtcTicks = value;
        }

        public bool IsComplete
        {
            get => _isComplete;
            set => _isComplete = value;
        }

        public List<XRayReportMaterial> Materials => _materials;
        public List<string> Errors => _errors;
        public List<string> Warnings => _warnings;

        public int CountTextures(XRayResourceStatus status)
        {
            int count = 0;
            foreach (XRayReportMaterial material in _materials)
            {
                foreach (XRayReportTexture texture in material.Textures)
                {
                    if (texture.Status == status)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        public int CountResolvedTextures()
        {
            int count = 0;
            foreach (XRayReportMaterial material in _materials)
            {
                foreach (XRayReportTexture texture in material.Textures)
                {
                    if (texture.Winner != null)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }

    public static class XRayResourceReportStore
    {
        public const string REPORT_FOLDER = XRayAssetIndexCache.CACHE_FOLDER + "/Reports";

        public static string GetPath(string modelGuid)
        {
            return REPORT_FOLDER + "/" + modelGuid + ".json";
        }

        public static void Save(string modelGuid, XRayResourceReport report)
        {
            Directory.CreateDirectory(REPORT_FOLDER);
            File.WriteAllText(GetPath(modelGuid), JsonUtility.ToJson(report, true));
        }

        public static XRayResourceReport Load(string modelGuid)
        {
            if (string.IsNullOrEmpty(modelGuid))
            {
                return null;
            }

            string path = GetPath(modelGuid);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<XRayResourceReport>(File.ReadAllText(path));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static List<XRayResourceReport> LoadAll()
        {
            var reports = new List<XRayResourceReport>();
            if (!Directory.Exists(REPORT_FOLDER))
            {
                return reports;
            }

            foreach (string file in Directory.GetFiles(REPORT_FOLDER, "*.json"))
            {
                XRayResourceReport report = Load(Path.GetFileNameWithoutExtension(file));
                if (report != null)
                {
                    reports.Add(report);
                }
            }

            return reports;
        }

        public static void Clear()
        {
            if (Directory.Exists(REPORT_FOLDER))
            {
                Directory.Delete(REPORT_FOLDER, true);
            }
        }
    }
}
