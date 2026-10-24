using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelTextureSet
    {
        public string TextureName { get; set; }
        public XRayTextureResolution Resolution { get; set; }
        public string AlbedoPath { get; set; }
        public string BumpPath { get; set; }
        public string CorrectionPath { get; set; }
        public XRayResourceStatus AlbedoStatus { get; set; }
        public XRayResourceStatus BumpStatus { get; set; }
        public XRayTextureDescription Description => Resolution?.Description;
        public bool HasAlbedo => AlbedoPath != null;
    }

    public sealed class XRayLevelTextureLibrary
    {
        private readonly XRayTextureResolver _resolver;
        private readonly XRayTextureImporter _importer;
        private readonly XRayLevelImportRecord _record;
        private readonly HashSet<string> _claimed = new HashSet<string>();
        private readonly HashSet<string> _folders = new HashSet<string>();
        private readonly Dictionary<string, XRayLevelTextureSet> _sets = new Dictionary<string, XRayLevelTextureSet>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, XRayTextureAssetResult> _packed = new Dictionary<string, XRayTextureAssetResult>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _requested = new List<string>();

        public XRayLevelTextureLibrary(XRayAssetIndex index, string levelName, XRayLevelImportRecord record, bool flipNormalGreen) : this(index, levelName, record, flipNormalGreen, false)
        {
        }

        public XRayLevelTextureLibrary(XRayAssetIndex index, string levelName, XRayLevelImportRecord record, bool flipNormalGreen, bool followWinners)
        {
            _resolver = new XRayTextureResolver(index) { LevelFolder = "levels\\" + levelName };
            _record = record;
            var options = new XRayImportOptions { FlipNormalGreen = flipNormalGreen, OverwriteExistingTextures = followWinners };
            _importer = new XRayTextureImporter(options, false, _claimed) { ContentKeys = !followWinners };
        }

        public XRayTextureResolver Resolver => _resolver;
        public IReadOnlyList<string> RequestedAssets => _requested;
        public int Imported { get; private set; }
        public int Reused { get; private set; }
        public int Missing { get; private set; }
        public int Unsupported { get; private set; }

        public XRayLevelTextureSet Request(string textureName, bool resolveBump, string purpose)
        {
            string key = (textureName ?? string.Empty) + (resolveBump ? "|bump" : string.Empty);
            if (_sets.TryGetValue(key, out XRayLevelTextureSet existing))
            {
                return existing;
            }

            var set = new XRayLevelTextureSet { TextureName = textureName };
            _sets[key] = set;
            XRayTextureResolution resolution = _resolver.Resolve(textureName);
            set.Resolution = resolution;
            XRayResolvedFile metadata = _resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(resolution.TextureName, ".thm"));
            _record.AddDependency(XRayLevelDependencyCategory.Materials, "THM metadata", metadata.DisplayPath, metadata.IsResolved ? metadata.Winner.Location : null, metadata.VirtualPath, metadata.IsResolved ? XRayLevelDependencyStatus.Found : XRayLevelDependencyStatus.Skipped, "Optional texture metadata; reimport when the effective THM changes or appears", null, resolution.DescriptionOrigin);
            XRayTextureAssetResult albedo = _importer.ImportAlbedo(resolution.Diffuse, PrepareFolder(resolution.Diffuse.VirtualPath));
            set.AlbedoPath = albedo.AssetPath;
            set.AlbedoStatus = albedo.Status;
            Track(XRayLevelDependencyCategory.Materials, "Diffuse", resolution.Diffuse, albedo, purpose);
            foreach (string note in resolution.Notes)
            {
                if (!_record.Warnings.Contains(note) && (note.StartsWith("Declared bump") || note.StartsWith("Cannot read") || note.StartsWith("THM chunk size corrected")))
                {
                    _record.Warnings.Add($"{textureName}: {note}");
                }
            }

            if (resolution.Description != null && !string.IsNullOrEmpty(resolution.Description.DetailName))
            {
                _record.AddDependency(XRayLevelDependencyCategory.Materials, "Detail texture (THM)", resolution.Description.DetailName, null, "textures\\" + resolution.Description.DetailName + ".dds", XRayLevelDependencyStatus.Described, "X-Ray detail texture; MonolithLit has no detail layer, not applied", null, $"scale {resolution.Description.DetailScale:0.###}, referenced by {textureName}");
            }

            if (resolveBump && resolution.Bump != null)
            {
                XRayTextureAssetResult bump = RequestPacked(resolution.Bump.VirtualPath, XRayLevelDependencyCategory.Materials, "Packed bump", purpose);
                set.BumpPath = bump.AssetPath;
                set.BumpStatus = bump.Status;
                if (resolution.BumpCorrection != null && resolution.BumpCorrection.IsResolved)
                {
                    XRayTextureAssetResult correction = RequestPacked(resolution.BumpCorrection.VirtualPath, XRayLevelDependencyCategory.Materials, "Bump correction (bump#)", "Reserved source data; not sampled by the active Monolith path");
                    set.CorrectionPath = correction.AssetPath;
                }
            }

            return set;
        }

        public bool Exists(string textureName)
        {
            return _resolver.ResolveTexture(textureName, ".dds").IsResolved;
        }

        public XRayTextureAssetResult RequestPacked(string virtualPath, XRayLevelDependencyCategory category, string role, string purpose)
        {
            string normalized = XRayVirtualPath.Normalize(virtualPath);
            if (_packed.TryGetValue(normalized, out XRayTextureAssetResult cached))
            {
                return cached;
            }

            XRayResolvedFile file = _resolver.ResolveFile(normalized);
            XRayTextureAssetResult result = file.IsResolved ? _importer.ImportPacked(file, PrepareFolder(normalized)) : new XRayTextureAssetResult { Status = XRayResourceStatus.Missing };
            _packed[normalized] = result;
            Track(category, role, file, result, purpose);
            return result;
        }

        public void ImportAll()
        {
            _importer.ImportPendingBatched();
            foreach (string path in _requested)
            {
                if (XRayTextureImportQueue.TryPeek(path, out XRayTextureImportRequest request))
                {
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
                    {
                        XRayAssetMarker.Write(path, request.Marker);
                    }
                    else
                    {
                        _record.Warnings.Add($"Unity could not import '{path}' as a texture.");
                    }

                    XRayTextureImportQueue.Complete(path);
                }
            }
        }

        public static Texture2D Load(string path)
        {
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private string PrepareFolder(string virtualPath)
        {
            string folder = XRayLevelPaths.TextureFolder(virtualPath);
            if (_folders.Add(folder))
            {
                XRayLevelPaths.EnsureFolder(folder);
            }

            return folder;
        }

        private void Track(XRayLevelDependencyCategory category, string role, XRayResolvedFile file, XRayTextureAssetResult result, string purpose)
        {
            XRayLevelDependencyStatus status;
            switch (result.Status)
            {
                case XRayResourceStatus.Imported:
                case XRayResourceStatus.Converted:
                case XRayResourceStatus.Updated:
                    status = XRayLevelDependencyStatus.Imported;
                    Imported++;
                    break;
                case XRayResourceStatus.Reused:
                    status = XRayLevelDependencyStatus.Reused;
                    Reused++;
                    break;
                case XRayResourceStatus.Missing:
                    status = XRayLevelDependencyStatus.Missing;
                    Missing++;
                    break;
                default:
                    status = XRayLevelDependencyStatus.Unsupported;
                    Unsupported++;
                    break;
            }

            if (result.AssetPath != null && !_requested.Contains(result.AssetPath))
            {
                _requested.Add(result.AssetPath);
            }

            string note = result.Note;
            if (file != null && file.Losers.Count > 0)
            {
                note = (note != null ? note + " " : string.Empty) + $"Conflict: also present in {string.Join(", ", file.Losers.ConvertAll(loser => loser.SourceName))}; winner {file.Winner.SourceName}.";
            }

            _record.AddDependency(category, role, file != null ? file.DisplayPath : null, file != null && file.IsResolved ? file.Winner.Location : null, file?.VirtualPath, status, purpose, result.AssetPath, note);
        }
    }
}
