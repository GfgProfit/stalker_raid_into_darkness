using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor
{
    [FilePath("ProjectSettings/XRayAssetSources.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class XRayAssetSourceProjectSettings : ScriptableSingleton<XRayAssetSourceProjectSettings>
    {
        [SerializeField] private XRaySourceMode _sourceMode = XRaySourceMode.Mo2AndBaseAnomaly;
        [SerializeField] private bool _includeGameGamedata = true;
        [SerializeField] private bool _treatModRootAsGamedata;
        [SerializeField] private XRayImportOptions _importOptions = new XRayImportOptions();

        public XRaySourceMode SourceMode
        {
            get => _sourceMode;
            set => _sourceMode = value;
        }

        public bool IncludeGameGamedata
        {
            get => _includeGameGamedata;
            set => _includeGameGamedata = value;
        }

        public bool TreatModRootAsGamedata
        {
            get => _treatModRootAsGamedata;
            set => _treatModRootAsGamedata = value;
        }

        public XRayImportOptions ImportOptions => _importOptions;

        public void SaveSettings()
        {
            Save(true);
        }
    }
}
