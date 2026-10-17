using System.Collections.Generic;

namespace XRay.Importer.Sources.Vfs
{
    public enum XRaySourceMode
    {
        BaseAnomalyOnly,
        Mo2AndBaseAnomaly,
        CustomFolder
    }

    public sealed class XRaySourceConfiguration
    {
        public static readonly string[] DEFAULT_INDEX_SCOPE = { "textures", "textures.ltx" };

        public XRaySourceMode Mode { get; set; } = XRaySourceMode.Mo2AndBaseAnomaly;
        public string BaseAnomalyFolder { get; set; }
        public string Mo2Folder { get; set; }
        public string Mo2Instance { get; set; }
        public string Mo2Profile { get; set; }
        public string CustomFolder { get; set; }
        public string LocalAppDataFolder { get; set; }
        public bool IncludeGameGamedata { get; set; } = true;
        public bool TreatModRootAsGamedata { get; set; }
        public List<string> IndexScope { get; } = new List<string>(DEFAULT_INDEX_SCOPE);

        public string Describe()
        {
            return string.Join("|", Mode.ToString(), BaseAnomalyFolder ?? string.Empty, Mo2Folder ?? string.Empty, Mo2Instance ?? string.Empty, Mo2Profile ?? string.Empty, CustomFolder ?? string.Empty, IncludeGameGamedata.ToString(), TreatModRootAsGamedata.ToString(), string.Join(",", IndexScope));
        }
    }
}
