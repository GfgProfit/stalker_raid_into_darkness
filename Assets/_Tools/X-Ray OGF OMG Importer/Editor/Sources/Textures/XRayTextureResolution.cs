using System.Collections.Generic;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Sources.Textures
{
    public enum XRayResolveStatus
    {
        Resolved,
        Missing
    }

    public sealed class XRayResolvedSource
    {
        public string SourceName { get; internal set; }
        public XRayAssetSourceKind Kind { get; internal set; }
        public string Priority { get; internal set; }
        public string FullPath { get; internal set; }
        public string RelativePath { get; internal set; }
    }

    public sealed class XRayResolvedFile
    {
        public string VirtualPath { get; internal set; }
        public XRayResolveStatus Status { get; internal set; }
        public XRayResolvedSource Winner { get; internal set; }
        public List<XRayResolvedSource> Losers { get; } = new List<XRayResolvedSource>();
        public bool IsAmbiguous { get; internal set; }
        public bool IsResolved => Status == XRayResolveStatus.Resolved;
        public string DisplayPath => XRayVirtualPath.ToDisplay(VirtualPath);
    }

    public sealed class XRayTextureResolution
    {
        public string TextureName { get; internal set; }
        public XRayResolvedFile Diffuse { get; internal set; }
        public XRayResolvedFile Bump { get; internal set; }
        public XRayResolvedFile BumpCorrection { get; internal set; }
        public XRayTextureDescription Description { get; internal set; }
        public string DescriptionOrigin { get; internal set; }
        public List<string> Notes { get; } = new List<string>();
    }
}
