using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayTerrainDiagnosticBinding : IDisposable
    {
        private readonly XRayTextureResolver _resolver;
        private readonly XRayShaderLibrary _shaders;
        private readonly string _root;
        private readonly string _level;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly List<XRayResolvedFile> _sources = new List<XRayResolvedFile>();

        private XRayTerrainSourceParameters _parameters;

        public XRayTerrainDiagnosticBinding(XRayAssetIndex index, string root, string level)
        {
            _root = root;
            _level = level;
            _resolver = new XRayTextureResolver(index) { LevelFolder = "levels/" + level };
            Source = XRayTerrainSourceBinding.Resolve(_resolver);
            XRayResolvedFile file = _resolver.ResolveFile("shaders.xr");
            if (!file.IsResolved)
            {
                throw new FileNotFoundException("The effective shaders.xr is required for terrain diagnostic bindings.");
            }
            _shaders = XRayShaderLibrary.Read(file.Winner.ReadAllBytes(), file.Winner.Location);
        }

        public IReadOnlyList<XRayResolvedFile> Sources => _sources;
        public XRayTerrainSourceBinding Source { get; }

        public static XRayTerrainDiagnosticBinding Open(string root, string level)
        {
            XRayLevelImportManifest manifest = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(XRayLevelPaths.ManifestPath(root, level));
            if (manifest == null) throw new InvalidOperationException("Imported level provenance is required for terrain diagnostics.");
            XRayLevelSourceProvenance provenance = manifest.Record.Source;
            XRaySourcePlan plan;
            if (provenance.SourceType == XRayLevelSourceManifest.SOURCE_TYPE_MO2)
            {
                XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
                configuration.Mo2Instance = provenance.Mo2Instance;
                configuration.Mo2Profile = provenance.Mo2Profile;
                configuration.GameFolder = provenance.GameFolder;
                configuration.IndexScope.Clear();
                configuration.IndexScope.AddRange(new[] { "textures", "textures.ltx", "shaders.xr", "shaders", "levels/" + level });
                plan = Mo2VirtualFileSystem.Plan(configuration);
                if (plan.IsValid && provenance.ProfileId != XRayLevelSourceCache.CreateProfileId(plan.Installation.InstanceFolder, plan.ProfileName)) throw new InvalidOperationException("Resolved MO2 identity differs from the imported manifest.");
            }
            else
            {
                string folder = provenance.SourceFolder;
                if (string.IsNullOrEmpty(folder)) folder = Directory.GetParent(Directory.GetParent(manifest.Record.SourceFolder).FullName).FullName;
                plan = XRayLevelGamedata.CreatePlan(XRayLevelGamedata.Inspect(folder), null, false);
            }
            if (!plan.IsValid)
            {
                throw new InvalidOperationException(string.Join("; ", plan.Errors));
            }
            return new XRayTerrainDiagnosticBinding(XRayAssetIndex.Build(plan), root, level) { _parameters = XRayTerrainSourceParameters.FromSource(provenance) };
        }

        public void Bind(Material source, Material preview)
        {
            if (Source.Profile != XRayTerrainSourceProfile.AnomalyAtmospherics) throw new NotSupportedException("This terrain preview implements only the verified Atmospherics contract; the source-specific terrain path remains unchanged: " + Source.Profile);
            if (source == null || preview == null || source == preview || EditorUtility.IsPersistent(preview) || preview.shader.name != MonolithMaterialConfigurator.SHADER_NAME || !source.IsKeywordEnabled("_MONOLITH_LEVEL_TERRAIN"))
            {
                throw new InvalidOperationException("Terrain diagnostics require a transient copy of an imported terrain material.");
            }
            string path = AssetDatabase.GetAssetPath(source);
            if (!path.StartsWith(_root + "/Materials/", StringComparison.Ordinal) && !path.StartsWith(_root + "/" + _level + "/LightingPreview/Materials/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Terrain diagnostics must use materials belonging to the imported source root.");
            }
            string original = path.Replace(_root + "/" + _level + "/LightingPreview/Materials/", _root + "/Materials/");
            string[] key = XRayAssetMarker.Read(original)?.Key.Split('|');
            if (key == null || key.Length < XRayLevelMaterialLibrary.METADATA_KEY_PARTS || !_shaders.TryGet(key[11], out XRayBlender blender) || !XRayTerrainSourceBinding.IsTerrainBlender(blender) || XRayLevelSurfaceRules.Classify(new XRayLevelShader(0, key[11]), blender, false).Kind != XRayLevelSurfaceKind.Terrain)
            {
                throw new InvalidOperationException("Effective terrain blender provenance is missing for " + original);
            }
            string texture = key[3].Split('#')[0];
            if (texture.StartsWith("textures/", StringComparison.Ordinal)) texture = texture.Substring(9);
            string prefix = "levels/" + _level + "/";
            if (texture.StartsWith(prefix, StringComparison.Ordinal)) texture = texture.Substring(prefix.Length);
            XRayTextureResolution resolution = _resolver.Resolve(texture);
            preview.SetTexture("_MonolithTerrainSourceBase", Load(resolution.Diffuse));
            preview.SetTexture("_MonolithTerrainMask", Load(_resolver.ResolveTexture(resolution.TextureName + "_mask", ".dds")));
            string[] channels = { "R", "G", "B", "A" };
            foreach (string channel in channels)
            {
                string detail = blender.GetString("Detail map/R2-" + channel);
                if (string.IsNullOrEmpty(detail)) throw new InvalidOperationException("Terrain layer name is missing: " + channel);
                preview.SetTexture("_MonolithTerrainSourceColor" + channel, Load(_resolver.ResolveTexture(detail, ".dds")));
                preview.SetTexture("_MonolithTerrainBump" + channel, Load(_resolver.ResolveTexture(detail + "_bump", ".dds")));
                preview.SetTexture("_MonolithTerrainHeight" + channel, Load(_resolver.ResolveTexture(detail + "_height", ".dds")));
            }
            if (resolution.Description == null || resolution.Description.DetailScale <= 0.0) throw new InvalidOperationException("Terrain THM detail scale is missing.");
            preview.SetFloat("_MonolithTerrainDetailScale", (float)resolution.Description.DetailScale);
            preview.SetFloat("_MonolithTerrainBaseFlipV", 1.0f);
            preview.SetFloat("_MonolithTerrainMaskFlipV", 1.0f);
            preview.SetVector("_MonolithTerrainColorFlipV", Vector4.one);
            preview.SetVector("_MonolithTerrainBumpFlipV", Vector4.one);
            preview.SetVector("_MonolithTerrainHeightFlipV", Vector4.one);
            preview.SetVector("_MonolithTerrainColorSrgb", Vector4.zero);
            preview.SetFloat("_MonolithHasTerrainHeight", 1.0f);
            preview.SetFloat("_MonolithHasTerrainSourceColor", 1.0f);
            preview.SetFloat("_MonolithTerrainSource", (float)Source.Profile);
            if (_parameters?.Pom != null) preview.SetVector("_MonolithTerrainPom", _parameters.Pom.Value);
            if (_parameters?.Offset != null) preview.SetVector("_MonolithTerrainOffset", _parameters.Offset.Value);
            if (Source.VerifiedMid)
            {
                XRayResolvedFile atlas = _resolver.ResolveTexture(resolution.TextureName + "_lod_textures", ".dds");
                if (!atlas.IsResolved) atlas = _resolver.ResolveTexture("terrain/default_lod_textures", ".dds");
                preview.SetTexture("_MonolithTerrainLodAtlas", atlas.IsResolved ? Load(atlas) : null);
                preview.SetFloat("_MonolithTerrainLodFlipV", 1.0f);
                preview.SetFloat("_MonolithTerrainMidAvailable", atlas.IsResolved ? 1.0f : 0.0f);
            }
            preview.SetFloat("_MonolithTerrainLowAvailable", Source.VerifiedLow ? 1.0f : 0.0f);
            preview.SetOverrideTag("XRayTerrainMidSHA256", Source.MidHash);
            preview.SetOverrideTag("XRayTerrainLowSHA256", Source.LowHash);
        }

        public void Dispose()
        {
            foreach (Texture2D texture in _textures.Values)
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
            _textures.Clear();
            _sources.Clear();
        }

        private Texture2D Load(XRayResolvedFile file)
        {
            if (!file.IsResolved) throw new FileNotFoundException("Terrain diagnostic dependency is missing: " + file.VirtualPath);
            if (_textures.TryGetValue(file.VirtualPath, out Texture2D cached)) return cached;
            byte[] data = file.Winner.ReadAllBytes();
            DdsHeader header = DdsHeader.Parse(data);
            if (header.IsCubemap || header.IsVolume || header.ArraySize != 1) throw new NotSupportedException("Terrain diagnostic texture must be 2D: " + file.VirtualPath);
            TextureFormat format;
            switch (header.Format)
            {
                case DdsFormat.BC1: format = TextureFormat.DXT1; break;
                case DdsFormat.BC3: format = TextureFormat.DXT5; break;
                case DdsFormat.BC4: format = TextureFormat.BC4; break;
                case DdsFormat.BC5: format = TextureFormat.BC5; break;
                case DdsFormat.BC6H: format = TextureFormat.BC6H; break;
                case DdsFormat.BC7: format = TextureFormat.BC7; break;
                default: format = TextureFormat.RGBA32; break;
            }
            bool native = format != TextureFormat.RGBA32;
            var texture = new Texture2D(header.Width, header.Height, format, native ? header.MipCount : 1, true) { name = file.VirtualPath + " (Diagnostic)", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = (int)(_parameters?.Anisotropy ?? 16.0f), mipMapBias = _parameters?.MipBias ?? -0.5f };
            try
            {
                if (native)
                {
                    var payload = new byte[data.Length - header.DataOffset];
                    Buffer.BlockCopy(data, header.DataOffset, payload, 0, payload.Length);
                    texture.LoadRawTextureData(payload);
                }
                else
                {
                    texture.LoadRawTextureData(DdsDecoder.DecodeTopLevel(data, header));
                }
                texture.Apply(false, false);
                _textures.Add(file.VirtualPath, texture);
                _sources.Add(file);
                return texture;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }
    }
}
