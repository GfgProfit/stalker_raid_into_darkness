using System.Collections.Generic;
using XRay.Importer.Sources.Mo2;

namespace XRay.Importer.Sources.Vfs
{
    public sealed class XRaySourcePlan
    {
        public XRaySourceConfiguration Configuration { get; internal set; }
        public Mo2Installation Installation { get; internal set; }
        public Mo2ModList ModList { get; internal set; }
        public string ProfileName { get; internal set; }
        public string Fingerprint { get; internal set; }
        public int ActiveModCount { get; internal set; }
        public int ModsWithoutGamedataCount { get; internal set; }
        public List<XRayAssetSource> Sources { get; } = new List<XRayAssetSource>();
        public List<string> SkipFileSuffixes { get; } = new List<string>();
        public List<string> SkipDirectories { get; } = new List<string>();
        public List<string> MissingModFolders { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0 && Sources.Count > 0;
    }
}
