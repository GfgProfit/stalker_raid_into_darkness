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
        private XRayAssetIndex _index;
        private XRayIndexCandidate _candidate;
        private string _fullPath;

        public string SourceName { get; internal set; }
        public string Origin { get; internal set; }
        public XRayAssetSourceKind Kind { get; internal set; }
        public string Priority { get; internal set; }
        public string RelativePath { get; internal set; }
        public long Size => _candidate != null ? _candidate.Size : -1;
        public bool IsArchive => Kind == XRayAssetSourceKind.Archive;

        public string FullPath
        {
            get
            {
                if (_fullPath == null && _index != null)
                {
                    _fullPath = _index.GetFullPath(_candidate);
                }

                return _fullPath;
            }
            internal set => _fullPath = value;
        }

        public string Location => IsArchive && _index != null ? _index.Describe(_candidate) : FullPath;

        public string Stamp => IsArchive ? $"{_candidate.Size}:{_candidate.ModifiedTicks}" : CreateFileStamp(FullPath);

        public byte[] ReadAllBytes()
        {
            return _index != null ? _index.ReadAllBytes(_candidate) : System.IO.File.ReadAllBytes(FullPath);
        }

        internal void Bind(XRayAssetIndex index, XRayIndexCandidate candidate)
        {
            _index = index;
            _candidate = candidate;
        }

        private static string CreateFileStamp(string file)
        {
            var info = new System.IO.FileInfo(file);
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : string.Empty;
        }
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
