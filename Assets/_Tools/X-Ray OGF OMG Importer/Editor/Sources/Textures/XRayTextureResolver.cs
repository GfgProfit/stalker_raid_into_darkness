using System;
using System.IO;
using XRay.Importer.Core.IO;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Sources.Textures
{
    public sealed class XRayTextureResolver
    {
        public const string TEXTURES_LTX_PATH = "textures.ltx";
        public const string BUMP_CORRECTION_SUFFIX = "#";

        private readonly XRayAssetIndex _index;
        private XRayTexturesLtx _ltx;
        private bool _ltxLoaded;
        private string _ltxError;

        public XRayTextureResolver(XRayAssetIndex index)
        {
            _index = index;
        }

        public string LevelFolder { get; set; }

        public XRayTexturesLtx TexturesLtx
        {
            get
            {
                EnsureLtx();
                return _ltx;
            }
        }

        public XRayTextureResolution Resolve(string textureName)
        {
            string name = StripExtension(textureName);
            var resolution = new XRayTextureResolution { TextureName = name };
            resolution.Diffuse = ResolveTexture(name, ".dds");
            ResolveDescription(name, resolution);
            if (resolution.Description != null && resolution.Description.HasBump)
            {
                string bumpName = StripExtension(resolution.Description.BumpName);
                resolution.Bump = ResolveTexture(bumpName, ".dds");
                resolution.BumpCorrection = ResolveTexture(bumpName + BUMP_CORRECTION_SUFFIX, ".dds");
                if (!resolution.Bump.IsResolved)
                {
                    resolution.Notes.Add($"Declared bump map '{bumpName}' ({resolution.DescriptionOrigin}) was not found.");
                }
            }

            if (resolution.Description != null && !string.IsNullOrEmpty(resolution.Description.DetailName))
            {
                resolution.Notes.Add($"Detail texture '{resolution.Description.DetailName}' (scale {resolution.Description.DetailScale:0.###}) is declared; renderer-specific source binding determines its use.");
            }

            if (!resolution.Diffuse.IsResolved && _index.ArchiveSources.Count > 0)
            {
                resolution.Notes.Add($"Not found in loose files. {_index.ArchiveSources.Count} sources contain game archives (.db/.xdb) that are not indexed; the texture may be stored in one of them: {string.Join(", ", _index.ArchiveSources)}");
            }

            return resolution;
        }

        public XRayResolvedFile ResolveTexture(string name, string extension)
        {
            if (!string.IsNullOrEmpty(LevelFolder))
            {
                XRayResolvedFile level = ResolveFile(XRayVirtualPath.Normalize(LevelFolder + "\\" + StripExtension(name) + extension));
                if (level.IsResolved)
                {
                    return level;
                }
            }

            return ResolveFile(XRayVirtualPath.TextureToVirtualPath(name, extension));
        }

        public XRayResolvedFile ResolveFile(string virtualPath)
        {
            var file = new XRayResolvedFile { VirtualPath = XRayVirtualPath.Normalize(virtualPath), Status = XRayResolveStatus.Missing };
            if (!_index.TryGet(file.VirtualPath, out XRayIndexedFile indexed))
            {
                return file;
            }

            file.Status = XRayResolveStatus.Resolved;
            file.IsAmbiguous = indexed.IsAmbiguous;
            file.Winner = CreateSource(indexed.Winner);
            for (int i = 1; i < indexed.Candidates.Count; i++)
            {
                file.Losers.Add(CreateSource(indexed.Candidates[i]));
            }

            return file;
        }

        private void ResolveDescription(string name, XRayTextureResolution resolution)
        {
            XRayResolvedFile thm = ResolveFile(XRayVirtualPath.TextureToVirtualPath(name, ".thm"));
            if (thm.IsResolved)
            {
                try
                {
                    XRayTextureDescription description = XRayThmReader.Read(thm.Winner.ReadAllBytes(), thm.Winner.Location);
                    foreach (string correction in description.CorrectedChunks)
                    {
                        resolution.Notes.Add($"THM chunk size corrected in '{thm.Winner.Location}': {correction}.");
                    }

                    if (XRayThmReader.IsUsedByEngine(description))
                    {
                        resolution.Description = description;
                        resolution.DescriptionOrigin = $"thm ({thm.Winner.SourceName})";
                        return;
                    }
                }
                catch (Exception exception) when (exception is XRayFormatException || exception is IOException || exception is XRaySourceException)
                {
                    resolution.Notes.Add($"Cannot read '{thm.Winner.Location}': {exception.Message}");
                }
            }

            EnsureLtx();
            if (_ltx != null && _ltx.TryGet(name, out XRayTextureDescription ltxDescription))
            {
                resolution.Description = ltxDescription;
                resolution.DescriptionOrigin = $"textures.ltx ({_ltx.Origin})";
            }
            else if (_ltxError != null)
            {
                resolution.Notes.Add(_ltxError);
            }
        }

        private void EnsureLtx()
        {
            if (_ltxLoaded)
            {
                return;
            }

            _ltxLoaded = true;
            XRayResolvedFile file = ResolveFile(TEXTURES_LTX_PATH);
            if (!file.IsResolved)
            {
                return;
            }

            try
            {
                _ltx = XRayTexturesLtx.Parse(file.Winner.ReadAllBytes(), file.Winner.SourceName);
            }
            catch (Exception exception) when (exception is IOException || exception is XRaySourceException)
            {
                _ltxError = $"Cannot read '{file.Winner.Location}': {exception.Message}";
            }
        }

        private XRayResolvedSource CreateSource(XRayIndexCandidate candidate)
        {
            XRayAssetSource source = _index.GetSource(candidate);
            var resolved = new XRayResolvedSource { SourceName = source.Name, Origin = source.Origin, Kind = source.Kind, Priority = source.DisplayPriority, RelativePath = candidate.RelativePath };
            resolved.Bind(_index, candidate);
            return resolved;
        }

        private static string StripExtension(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            return trimmed.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring(0, trimmed.Length - 4) : trimmed;
        }
    }
}
