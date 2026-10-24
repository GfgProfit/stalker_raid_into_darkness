using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor
{
    [FilePath("UserSettings/XRayAssetSources.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class XRayAssetSourceUserSettings : ScriptableSingleton<XRayAssetSourceUserSettings>
    {
        public const string DEFAULT_BASE_ANOMALY_FOLDER = "D:\\GRA2.5\\Anomaly 1.5.3 up\\tools\\_unpacked";
        public const string DEFAULT_MO2_FOLDER = "D:\\GRA2.5\\MO2";
        public const string DEFAULT_LEVEL_GAMEDATA_FOLDER = "E:\\True Stalker Unpacked Patch 1.5";

        [SerializeField] private string _baseAnomalyFolder = DEFAULT_BASE_ANOMALY_FOLDER;
        [SerializeField] private string _mo2Folder = DEFAULT_MO2_FOLDER;
        [SerializeField] private string _mo2Instance = string.Empty;
        [SerializeField] private string _mo2Profile = string.Empty;
        [SerializeField] private string _customFolder = string.Empty;
        [SerializeField] private bool _overrideSourceMode;
        [SerializeField] private XRaySourceMode _sourceMode = XRaySourceMode.Mo2AndBaseAnomaly;
        [SerializeField] private bool _overrideImportOptions;
        [SerializeField] private XRayImportOptions _importOptions = new XRayImportOptions();
        [SerializeField] private string _levelGamedataFolder = DEFAULT_LEVEL_GAMEDATA_FOLDER;
        [SerializeField] private bool _levelAllowFallbackSources;
        [SerializeField] private XRayLevelImportSettings _levelImport = new XRayLevelImportSettings();
        [SerializeField] private XRayLevelSourceType _levelSourceType = XRayLevelSourceType.GamedataFolder;
        [SerializeField] private string _levelMo2Folder = DEFAULT_MO2_FOLDER;
        [SerializeField] private string _levelMo2Instance = string.Empty;
        [SerializeField] private string _levelMo2Profile = string.Empty;
        [SerializeField] private string _levelGameFolder = string.Empty;
        [SerializeField] private string _levelModsFolder = string.Empty;
        [SerializeField] private string _levelProfilesFolder = string.Empty;
        [SerializeField] private string _levelOverwriteFolder = string.Empty;

        public string BaseAnomalyFolder
        {
            get => _baseAnomalyFolder;
            set => _baseAnomalyFolder = value;
        }

        public string Mo2Folder
        {
            get => _mo2Folder;
            set => _mo2Folder = value;
        }

        public string Mo2Instance
        {
            get => _mo2Instance;
            set => _mo2Instance = value;
        }

        public string Mo2Profile
        {
            get => _mo2Profile;
            set => _mo2Profile = value;
        }

        public string CustomFolder
        {
            get => _customFolder;
            set => _customFolder = value;
        }

        public bool OverrideSourceMode
        {
            get => _overrideSourceMode;
            set => _overrideSourceMode = value;
        }

        public XRaySourceMode SourceMode
        {
            get => _sourceMode;
            set => _sourceMode = value;
        }

        public bool OverrideImportOptions
        {
            get => _overrideImportOptions;
            set => _overrideImportOptions = value;
        }

        public XRayImportOptions ImportOptions => _importOptions;

        public string LevelGamedataFolder
        {
            get => _levelGamedataFolder;
            set => _levelGamedataFolder = value;
        }

        public bool LevelAllowFallbackSources
        {
            get => _levelAllowFallbackSources;
            set => _levelAllowFallbackSources = value;
        }

        public XRayLevelImportSettings LevelImport => _levelImport ?? (_levelImport = new XRayLevelImportSettings());

        public XRayLevelSourceType LevelSourceType
        {
            get => _levelSourceType;
            set => _levelSourceType = value;
        }

        public string LevelMo2Folder
        {
            get => _levelMo2Folder;
            set => _levelMo2Folder = value;
        }

        public string LevelMo2Instance
        {
            get => _levelMo2Instance;
            set => _levelMo2Instance = value;
        }

        public string LevelMo2Profile
        {
            get => _levelMo2Profile;
            set => _levelMo2Profile = value;
        }

        public string LevelGameFolder
        {
            get => _levelGameFolder;
            set => _levelGameFolder = value;
        }

        public string LevelModsFolder
        {
            get => _levelModsFolder;
            set => _levelModsFolder = value;
        }

        public string LevelProfilesFolder
        {
            get => _levelProfilesFolder;
            set => _levelProfilesFolder = value;
        }

        public string LevelOverwriteFolder
        {
            get => _levelOverwriteFolder;
            set => _levelOverwriteFolder = value;
        }

        public void SaveSettings()
        {
            Save(true);
        }
    }
}
