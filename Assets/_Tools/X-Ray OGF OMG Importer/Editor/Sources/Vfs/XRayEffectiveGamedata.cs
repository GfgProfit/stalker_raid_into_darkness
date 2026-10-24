using System;
using System.Collections.Generic;
using System.IO;

namespace XRay.Importer.Sources.Vfs
{
    public sealed class XRayEffectiveFile
    {
        public string VirtualPath { get; internal set; }
        public string RelativePath { get; internal set; }
        public string SourceName { get; internal set; }
        public string Origin { get; internal set; }
        public XRayAssetSourceKind Kind { get; internal set; }
        public int Priority { get; internal set; }
        public string DisplayPriority { get; internal set; }
        public string PhysicalPath { get; internal set; }
        public string Location { get; internal set; }
        public long Size { get; internal set; }
        public long ModifiedTicks { get; internal set; }
        public bool IsArchive => Kind == XRayAssetSourceKind.Archive;
        public XRayIndexCandidate Candidate { get; internal set; }
    }

    public sealed class XRayEffectiveGamedata
    {
        private readonly XRayAssetIndex _index;

        public XRayEffectiveGamedata(XRayAssetIndex index)
        {
            _index = index ?? throw new ArgumentNullException(nameof(index));
        }

        public XRayAssetIndex Index => _index;

        public bool Exists(string virtualPath)
        {
            return _index.TryGet(Safe(virtualPath), out XRayIndexedFile _);
        }

        public XRayEffectiveFile Resolve(string virtualPath)
        {
            return _index.TryGet(Safe(virtualPath), out XRayIndexedFile file) ? Describe(file.VirtualPath, file.Winner) : null;
        }

        public IReadOnlyList<XRayEffectiveFile> GetCandidates(string virtualPath)
        {
            var result = new List<XRayEffectiveFile>();
            if (_index.TryGet(Safe(virtualPath), out XRayIndexedFile file))
            {
                foreach (XRayIndexCandidate candidate in file.Candidates)
                {
                    result.Add(Describe(file.VirtualPath, candidate));
                }
            }

            return result;
        }

        public string GetOrigin(string virtualPath)
        {
            return Resolve(virtualPath)?.Origin;
        }

        public Stream OpenRead(string virtualPath)
        {
            return _index.OpenRead(Require(virtualPath).Winner);
        }

        public byte[] ReadAllBytes(string virtualPath)
        {
            return _index.ReadAllBytes(Require(virtualPath).Winner);
        }

        public string Materialize(string virtualPath)
        {
            return _index.Materialize(Require(virtualPath).Winner);
        }

        public IEnumerable<XRayEffectiveFile> Enumerate(string virtualDirectory, bool recursive)
        {
            string prefix = Safe(virtualDirectory);
            prefix = prefix.Length == 0 ? string.Empty : prefix + "/";
            var result = new List<XRayEffectiveFile>();
            foreach (XRayIndexedFile file in _index.Files)
            {
                if (!file.VirtualPath.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!recursive && file.VirtualPath.IndexOf('/', prefix.Length) >= 0)
                {
                    continue;
                }

                result.Add(Describe(file.VirtualPath, file.Winner));
            }

            result.Sort((a, b) => string.CompareOrdinal(a.VirtualPath, b.VirtualPath));
            return result;
        }

        public XRayEffectiveFile Describe(string virtualPath, XRayIndexCandidate candidate)
        {
            XRayAssetSource source = _index.GetSource(candidate);
            return new XRayEffectiveFile
            {
                VirtualPath = virtualPath,
                RelativePath = candidate.RelativePath,
                SourceName = source.Name,
                Origin = source.Origin,
                Kind = source.Kind,
                Priority = source.Priority,
                DisplayPriority = source.DisplayPriority,
                PhysicalPath = source.IsArchive ? source.GamedataRoot : source.GetFullPath(candidate.RelativePath),
                Location = source.Describe(candidate.RelativePath),
                Size = candidate.Size,
                ModifiedTicks = candidate.ModifiedTicks,
                Candidate = candidate
            };
        }

        private XRayIndexedFile Require(string virtualPath)
        {
            if (!_index.TryGet(Safe(virtualPath), out XRayIndexedFile file))
            {
                throw new XRaySourceException($"'{virtualPath}' is not present in the effective gamedata.");
            }

            return file;
        }

        private static string Safe(string virtualPath)
        {
            if (!XRayVirtualPath.TryNormalizeSafe(virtualPath, out string normalized))
            {
                throw new XRaySourceException($"Virtual path '{virtualPath}' leaves the gamedata root or is not a valid game path.");
            }

            return normalized;
        }
    }
}
