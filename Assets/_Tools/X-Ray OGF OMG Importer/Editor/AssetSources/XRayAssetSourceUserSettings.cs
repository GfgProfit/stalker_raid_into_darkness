using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor
{
    [FilePath("UserSettings/XRayAssetSources.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class XRayAssetSourceUserSettings : ScriptableSingleton<XRayAssetSourceUserSettings>
    {
        public const string DEFAULT_BASE_ANOMALY_FOLDER = "D:\\GRA2.5\\Anomaly 1.5.3 up\\tools\\_unpacked";
        public const string DEFAULT_MO2_FOLDER = "D:\\GRA2.5\\MO2";

        [SerializeField] private string _baseAnomalyFolder = DEFAULT_BASE_ANOMALY_FOLDER;
        [SerializeField] private string _mo2Folder = DEFAULT_MO2_FOLDER;
        [SerializeField] private string _mo2Instance = string.Empty;
        [SerializeField] private string _mo2Profile = string.Empty;
        [SerializeField] private string _customFolder = string.Empty;
        [SerializeField] private bool _overrideSourceMode;
        [SerializeField] private XRaySourceMode _sourceMode = XRaySourceMode.Mo2AndBaseAnomaly;
        [SerializeField] private bool _overrideImportOptions;
        [SerializeField] private XRayImportOptions _importOptions = new XRayImportOptions();

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

        public void SaveSettings()
        {
            Save(true);
        }
    }
}
