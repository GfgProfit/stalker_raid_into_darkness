using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Core.Level;
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
        public XRayTextureResolution DetailResolution { get; set; }
        public string DetailPath { get; set; }
        public string DetailBumpPath { get; set; }
        public string DetailCorrectionPath { get; set; }
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
        private readonly bool _sourceDetails;
        private readonly HashSet<string> _claimed = new HashSet<string>();
        private readonly HashSet<string> _folders = new HashSet<string>();
        private readonly Dictionary<string, XRayLevelTextureSet> _sets = new Dictionary<string, XRayLevelTextureSet>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, XRayTextureAssetResult> _packed = new Dictionary<string, XRayTextureAssetResult>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, XRayTextureAssetResult> _authored = new Dictionary<string, XRayTextureAssetResult>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _requested = new List<string>();
        private readonly Dictionary<string, Vector2> _terrainSamplerAssets = new Dictionary<string, Vector2>();

        private XRayTerrainSourceParameters _terrainParameters;
        private bool _sourceTerrainSampler;
        private Vector2? _floraSampler;

        public XRayLevelTextureLibrary(XRayAssetIndex index, string levelName, XRayLevelImportRecord record, bool flipNormalGreen) : this(index, levelName, record, flipNormalGreen, false)
        {
        }

        public XRayLevelTextureLibrary(XRayAssetIndex index, string levelName, XRayLevelImportRecord record, bool flipNormalGreen, bool followWinners)
        {
            _resolver = new XRayTextureResolver(index) { LevelFolder = "levels\\" + levelName };
            _record = record;
            _sourceDetails = followWinners;
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

            if (resolution.Description != null && !string.IsNullOrEmpty(resolution.Description.DetailName) && !(resolveBump && _sourceDetails && XRayLevelSourceBindings.DeclaresDetail(resolution.Description)))
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
                    XRayTextureAssetResult correction = RequestPacked(resolution.BumpCorrection.VirtualPath, XRayLevelDependencyCategory.Materials, "Bump correction (bump#)", _sourceDetails ? "s_bumpX: A height for USE_PARALLAX / USE_STEEPPARALLAX, R for steep AO" : "Reserved source data; not sampled by the active Monolith path");
                    set.CorrectionPath = correction.AssetPath;
                }
            }

            if (resolveBump && _sourceDetails && XRayLevelSourceBindings.DeclaresDetail(resolution.Description))
            {
                RequestDetail(set);
            }

            return set;
        }

        public XRayLevelFloraTextures RequestFlora(XRayLevelTextureSet set, XRayFloraSourceBinding source)
        {
            var flora = new XRayLevelFloraTextures { Profile = source?.Profile ?? XRayFloraSourceProfile.Unknown };
            if (set?.Resolution?.Diffuse == null || flora.Profile == XRayFloraSourceProfile.Unknown)
            {
                return flora;
            }

            if (flora.Profile == XRayFloraSourceProfile.TrueStalker)
            {
                flora.CorrectionPath = set.CorrectionPath;
                return flora;
            }

            if (_floraSampler == null)
            {
                XRayTerrainSourceParameters parameters = XRayTerrainSourceParameters.FromSource(_record.Source);
                _floraSampler = new Vector2(parameters?.Anisotropy ?? 16.0f, parameters?.MipBias ?? -0.5f);
            }

            XRayResolvedFile diffuse = set.Resolution.Diffuse;
            string authoredKey = XRayVirtualPath.Normalize(diffuse.VirtualPath);
            if (_authored.TryGetValue(authoredKey, out XRayTextureAssetResult cached))
            {
                flora.AuthoredAlbedoPath = cached.Status == XRayResourceStatus.Skipped || cached.Status == XRayResourceStatus.Unsupported ? null : cached.AssetPath;
                flora.AuthoredNote = cached.Note;
                return flora;
            }

            XRayTextureAssetResult result = _importer.ImportAuthoredMips(diffuse, PrepareFolder(diffuse.VirtualPath), (int)_floraSampler.Value.x, _floraSampler.Value.y);
            _authored[authoredKey] = result;
            if (result.Status == XRayResourceStatus.Skipped || result.Status == XRayResourceStatus.Unsupported)
            {
                _record.AddDependency(XRayLevelDependencyCategory.Materials, "Flora authored albedo mips", diffuse.DisplayPath, diffuse.IsResolved ? diffuse.Winner.Location : null, diffuse.VirtualPath, XRayLevelDependencyStatus.Described, "Legacy albedo keeps the source mip chain or the format has no authored-mip path", null, result.Note);
                flora.AuthoredNote = result.Note;
                return flora;
            }

            Track(XRayLevelDependencyCategory.Materials, "Flora authored albedo mips", diffuse, result, "s_base with the authored DDS mip chain, encoded UNorm sampling, smp_base anisotropy and mip bias");
            flora.AuthoredAlbedoPath = result.AssetPath;
            flora.AuthoredNote = result.Note;
            return flora;
        }

        public string FindPacked(XRayResolvedFile file, out bool upToDate)
        {
            upToDate = false;
            return file != null && file.IsResolved ? _importer.FindPacked(file, XRayLevelPaths.TextureFolder(XRayVirtualPath.Normalize(file.VirtualPath)), out upToDate) : null;
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

        public XRayLevelTerrainTextures RequestTerrain(XRayLevelShader shader, XRayBlender blender, bool trueStalkerSource = false)
        {
            var terrain = new XRayLevelTerrainTextures { Source = XRayTerrainSourceBinding.IsTerrainBlender(blender) ? XRayTerrainSourceBinding.Resolve(_resolver) : new XRayTerrainSourceBinding() };
            trueStalkerSource &= terrain.Source.VerifiedTrueStalker;
            bool sourceColors = terrain.Source.UsesHeight || trueStalkerSource;
            if (trueStalkerSource)
            {
                foreach (string path in XRayTerrainSourceBinding.TrueStalkerFiles) RecordTerrainFile(_resolver.ResolveFile(path), "TS terrain source contract", null);
            }
            _sourceTerrainSampler = terrain.Source.UsesHeight;
            terrain.Parameters = terrain.Source.UsesHeight ? XRayTerrainSourceParameters.FromSource(_record.Source) : null;
            _terrainParameters = terrain.Parameters;
            if (terrain.Parameters != null) _record.AddDependency(XRayLevelDependencyCategory.Materials, "Terrain user settings", terrain.Parameters.Path, terrain.Parameters.Path, null, XRayLevelDependencyStatus.Found, "Source terrain POM, layer offsets and source sampler settings", null, "SHA256=" + terrain.Parameters.Hash);
            else if (terrain.Source.UsesHeight && !string.IsNullOrEmpty(_record.Source.GameFolder)) _record.Warnings.Add("Terrain user settings are unavailable or use an external app_data_root; shader defaults remain active and source parameter parity is unverified.");
            XRayTextureResolution terrainBase = _resolver.Resolve(shader.Texture);
            RecordTerrainFile(terrainBase.Diffuse, "Terrain base", Request(shader.Texture, false, "Terrain base").AlbedoPath);
            RecordTerrainFile(_resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(terrainBase.TextureName, ".thm")), "Terrain THM", null);
            foreach (XRayResolvedFile source in new[] { terrain.Source.Shader, terrain.Source.Brdf, terrain.Source.MidShader, terrain.Source.LowShader }) RecordTerrainFile(source, "Terrain shader contract", null);
            if (sourceColors) terrain.SourceBasePath = RequestTerrainPacked(terrainBase.Diffuse.VirtualPath, XRayLevelDependencyCategory.Materials, "Terrain source base UNORM", "s_base encoded color and alpha hemisphere; independent of legacy hardware sRGB albedo").AssetPath;
            _record.Source.TerrainProfile = terrain.Source.Profile.ToString();
            _record.Source.TerrainShaderPath = terrain.Source.Shader?.VirtualPath;
            _record.Source.TerrainShaderHash = terrain.Source.ShaderHash;
            _record.Source.TerrainBrdfHash = terrain.Source.BrdfHash;
            _record.AddDependency(XRayLevelDependencyCategory.Materials, "Terrain source shader", terrain.Source.Shader?.VirtualPath, terrain.Source.Shader?.Winner?.Location, terrain.Source.Shader?.VirtualPath, terrain.Source.Shader?.IsResolved == true ? XRayLevelDependencyStatus.Found : XRayLevelDependencyStatus.Missing, "Source-specific terrain data contract; activation remains separate", null, terrain.Source.ShaderHash);
            _record.AddDependency(XRayLevelDependencyCategory.Materials, "Terrain source BRDF", terrain.Source.Brdf?.VirtualPath, terrain.Source.Brdf?.Winner?.Location, terrain.Source.Brdf?.VirtualPath, terrain.Source.Brdf?.IsResolved == true ? XRayLevelDependencyStatus.Found : XRayLevelDependencyStatus.Missing, "Source-specific terrain BRDF identity; activation remains separate", null, terrain.Source.BrdfHash);
            foreach (var variant in new[] { terrain.Source.MidShader, terrain.Source.LowShader })
            {
                _record.AddDependency(XRayLevelDependencyCategory.Materials, "Terrain quality shader", variant?.VirtualPath, variant?.Winner?.Location, variant?.VirtualPath, variant?.IsResolved == true ? XRayLevelDependencyStatus.Found : XRayLevelDependencyStatus.Missing, "Explicit quality variant; original visual sphere selector is not inferred from mesh chunks", null, variant == terrain.Source.MidShader ? terrain.Source.MidHash : terrain.Source.LowHash);
            }
            if (terrain.Source.VerifiedMid)
            {
                var atlas = _resolver.ResolveTexture(shader.Texture + "_lod_textures", ".dds");
                if (!atlas.IsResolved)
                {
                    _record.AddDependency(XRayLevelDependencyCategory.Materials, "Terrain level LOD atlas", atlas.DisplayPath, null, atlas.VirtualPath, XRayLevelDependencyStatus.Missing, "Blender_BmmD selects terrain/default_lod_textures when this atlas is absent");
                    atlas = _resolver.ResolveTexture("terrain/default_lod_textures", ".dds");
                }
                terrain.LodAtlasPath = RequestTerrainPacked(atlas.VirtualPath, XRayLevelDependencyCategory.Materials, "Terrain MID atlas", "s_lod_texture: encoded UNORM; smp_base for selected UV, smp_linear for previous UV").AssetPath;
            }
            XRayTextureAssetResult mask = RequestTerrainPacked(XRayVirtualPath.TextureToVirtualPath(shader.Texture + "_mask", ".dds"), XRayLevelDependencyCategory.Materials, "Terrain mask", "s_mask: per-channel weights of the 4 detail layers");
            terrain.MaskPath = mask.AssetPath;
            string[] channels = { "R2-R", "R2-G", "R2-B", "R2-A" };
            for (int i = 0; i < channels.Length; i++)
            {
                string detail = blender.GetString("Detail map/" + channels[i]);
                if (string.IsNullOrEmpty(detail))
                {
                    continue;
                }

                XRayLevelTextureSet set = Request(detail, false, "Terrain detail " + channels[i] + " (s_dt_*)");
                terrain.DetailPaths[i] = set.AlbedoPath;
                RecordTerrainFile(set.Resolution.Diffuse, "Terrain layer color " + channels[i], set.AlbedoPath);
                RecordTerrainFile(_resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(set.Resolution.TextureName, ".thm")), "Terrain layer THM " + channels[i], null);
                XRayTextureAssetResult bump = RequestTerrainPacked(XRayVirtualPath.TextureToVirtualPath(detail + "_bump", ".dds"), XRayLevelDependencyCategory.Materials, "Terrain detail bump " + channels[i], "s_dn_*: .wzyx normal + gloss");
                terrain.BumpPaths[i] = bump.AssetPath;
                if (sourceColors)
                {
                    terrain.SourceColorPaths[i] = RequestTerrainPacked(set.Resolution.Diffuse.VirtualPath, XRayLevelDependencyCategory.Materials, "Terrain source color " + channels[i], "s_dt_* encoded UNORM filtering; legacy albedo references remain separate").AssetPath;
                    if (terrain.Source.UsesHeight) terrain.HeightPaths[i] = RequestTerrainPacked(XRayVirtualPath.TextureToVirtualPath(detail + "_height", ".dds"), XRayLevelDependencyCategory.Materials, "Terrain source height " + channels[i], "s_height_*: R height, independent of bump#; source terrain POM and initial-height AO").AssetPath;
                }
            }

            XRayTextureDescription description = terrainBase.Description;
            terrain.DetailScale = description != null && !double.IsNaN(description.DetailScale) && !double.IsInfinity(description.DetailScale) && description.DetailScale > 0.0 && description.DetailScale <= float.MaxValue ? (float)description.DetailScale : 1.0f;
            if (description == null || double.IsNaN(description.DetailScale) || double.IsInfinity(description.DetailScale) || description.DetailScale <= 0.0 || description.DetailScale > float.MaxValue)
            {
                _record.Warnings.Add($"Terrain '{shader.Texture}': no THM detail scale; dt_params = 1 is used.");
            }

            return terrain;
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
            foreach (KeyValuePair<string, Vector2> sampler in _terrainSamplerAssets)
            {
                string path = sampler.Key;
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer == null) continue;
                var settings = new SerializedObject(importer);
                string[] names = { "m_TextureSettings.m_FilterMode", "m_TextureSettings.m_Aniso", "m_TextureSettings.m_MipBias", "m_TextureSettings.m_WrapU", "m_TextureSettings.m_WrapV" };
                float[] values = { (float)FilterMode.Trilinear, sampler.Value.x, sampler.Value.y, (float)TextureWrapMode.Repeat, (float)TextureWrapMode.Repeat };
                bool changed = false;
                for (int i = 0; i < names.Length; i++)
                {
                    SerializedProperty property = settings.FindProperty(names[i]);
                    if (property == null)
                    {
                        _record.Warnings.Add($"Terrain sampler setting '{names[i]}' is unavailable for '{path}'; source sampling parity is unverified.");
                        continue;
                    }
                    if (i == 2)
                    {
                        changed |= property.floatValue != values[i];
                        property.floatValue = values[i];
                    }
                    else
                    {
                        changed |= property.intValue != (int)values[i];
                        property.intValue = (int)values[i];
                    }
                }
                if (!changed) continue;
                settings.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.ReleaseCachedFileHandles();
                importer.SaveAndReimport();
            }
            _terrainSamplerAssets.Clear();
            foreach (XRayLevelSourceFile file in _record.Source.Files)
            {
                if (!string.IsNullOrEmpty(file.AssetPath)) file.AssetGuid = AssetDatabase.AssetPathToGUID(file.AssetPath);
            }
        }

        private XRayTextureAssetResult RequestTerrainPacked(string virtualPath, XRayLevelDependencyCategory category, string role, string purpose)
        {
            XRayTextureAssetResult result = RequestPacked(virtualPath, category, role, purpose);
            RecordTerrainFile(_resolver.ResolveFile(virtualPath), role, result.AssetPath);
            if (_sourceTerrainSampler && result.AssetPath != null && (result.Status == XRayResourceStatus.Imported || result.Status == XRayResourceStatus.Converted)) _terrainSamplerAssets[result.AssetPath] = new Vector2(_terrainParameters?.Anisotropy ?? 16.0f, _terrainParameters?.MipBias ?? -0.5f);
            return result;
        }

        private void RecordTerrainFile(XRayResolvedFile resolved, string role, string assetPath)
        {
            if (resolved == null || !resolved.IsResolved) return;
            XRayLevelSourceFile existing = _record.Source.Files.Find(file => file.VirtualPath == resolved.VirtualPath && file.AssetPath == assetPath);
            if (existing != null) return;
            XRayResolvedSource winner = resolved.Winner;
            var source = new XRayLevelSourceFile { VirtualPath = resolved.VirtualPath, Role = role, Source = winner.SourceName, Origin = winner.Origin, Kind = winner.Kind.ToString(), Priority = winner.Priority, Size = winner.Size, Location = winner.Location, ContentHash = "sha256:" + XRayTerrainSourceBinding.Hash(resolved), AssetPath = assetPath };
            foreach (XRayResolvedSource loser in resolved.Losers) source.Losers.Add(loser.Location);
            _record.Source.Files.Add(source);
        }

        private void RequestDetail(XRayLevelTextureSet set)
        {
            XRayTextureResolution detail = _resolver.Resolve(set.Description.DetailName);
            set.DetailResolution = detail;
            XRayResolvedFile metadata = _resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(detail.TextureName, ".thm"));
            _record.AddDependency(XRayLevelDependencyCategory.Materials, "Detail THM metadata", metadata.DisplayPath, metadata.IsResolved ? metadata.Winner.Location : null, metadata.VirtualPath, metadata.IsResolved ? XRayLevelDependencyStatus.Found : XRayLevelDependencyStatus.Skipped, "Optional detail bump declaration; reimport when the effective detail THM changes or appears", null, detail.DescriptionOrigin);
            set.DetailPath = RequestPacked(detail.Diffuse.VirtualPath, XRayLevelDependencyCategory.Materials, "Detail texture (THM detail_name)", "s_detail: rgb x2 colour, a x2 gloss (USE_TDETAIL)").AssetPath;
            if (detail.Bump == null)
            {
                return;
            }

            set.DetailBumpPath = RequestPacked(detail.Bump.VirtualPath, XRayLevelDependencyCategory.Materials, "Detail bump", "s_detailBump: ABG normal, R gloss x2 (USE_TDETAIL_BUMP)").AssetPath;
            if (detail.BumpCorrection != null)
            {
                set.DetailCorrectionPath = RequestPacked(detail.BumpCorrection.VirtualPath, XRayLevelDependencyCategory.Materials, "Detail bump correction (detail bump#)", "s_detailBumpX: rgb added to the detail normal").AssetPath;
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
