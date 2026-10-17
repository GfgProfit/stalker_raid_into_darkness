using System;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor
{
    public sealed class XRayAssetSourceOptions
    {
        public XRaySourceConfiguration Sources { get; set; } = new XRaySourceConfiguration();
        public XRayImportOptions Import { get; set; } = new XRayImportOptions();
        public bool PersistIndexCache { get; set; } = true;
    }

    public static class XRayAssetSourceSettings
    {
        internal static XRayAssetSourceOptions Override { get; set; }

        public static XRayAssetSourceOptions GetEffective()
        {
            if (Override != null)
            {
                return Override;
            }

            XRayAssetSourceProjectSettings project = XRayAssetSourceProjectSettings.instance;
            XRayAssetSourceUserSettings user = XRayAssetSourceUserSettings.instance;
            var sources = new XRaySourceConfiguration
            {
                Mode = user.OverrideSourceMode ? user.SourceMode : project.SourceMode,
                BaseAnomalyFolder = user.BaseAnomalyFolder,
                Mo2Folder = user.Mo2Folder,
                Mo2Instance = user.Mo2Instance,
                Mo2Profile = user.Mo2Profile,
                CustomFolder = user.CustomFolder,
                LocalAppDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                IncludeGameGamedata = project.IncludeGameGamedata,
                TreatModRootAsGamedata = project.TreatModRootAsGamedata
            };

            XRayImportOptions import = user.OverrideImportOptions ? user.ImportOptions : project.ImportOptions;
            return new XRayAssetSourceOptions { Sources = sources, Import = import.Clone() };
        }
    }
}
