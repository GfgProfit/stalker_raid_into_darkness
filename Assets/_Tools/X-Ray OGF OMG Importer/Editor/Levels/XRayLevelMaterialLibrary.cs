using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Vfs;
#if XRAY_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelMaterialRequest
    {
        public string ShaderName { get; set; }
        public XRayLevelSurface Surface { get; set; }
        public XRayLevelTextureSet Textures { get; set; }
        public string LightmapPath { get; set; }
        public string LightmapKey { get; set; }
        public string LightmapLabel { get; set; }
        public bool Detail { get; set; }
        public XRayLevelTerrainTextures Terrain { get; set; }
    }

    public sealed class XRayLevelTerrainTextures
    {
        public string MaskPath { get; set; }
        public string[] DetailPaths { get; } = new string[4];
        public string[] BumpPaths { get; } = new string[4];
        public string[] HeightPaths { get; } = new string[4];
        public string[] SourceColorPaths { get; } = new string[4];
        public string SourceBasePath { get; set; }
        public XRayTerrainSourceBinding Source { get; set; }
        public float DetailScale { get; set; } = 1.0f;

        public string Key()
        {
            return $"{XRayAssetMarker.ReadKey(MaskPath ?? string.Empty)}|{string.Join(",", Array.ConvertAll(DetailPaths, path => path ?? "-"))}|{string.Join(",", Array.ConvertAll(BumpPaths, path => path ?? "-"))}|{DetailScale:0.###}";
        }
    }

    public sealed class XRayLevelMaterialLibrary
    {
        public const string MATERIAL_ROLE = "LevelMaterial";
        public const string FALLBACK_SUFFIX = "_fallback";
        public const float DETAIL_AREF = 170.0f / 255.0f;
        public const float DEF_GLOSS = 2.0f / 255.0f;
        public const string DECAL_SHADER_NAME = "HDRP/MonolithDecal";
        public const float WMARK_NORMAL_SHIFT = 0.007f;
        public const float WMARK_MIN_SHIFT = 0.003f;
        public const float WMARK_MAX_SHIFT = 0.011f;
        public const float WALLMARK_SHIFT_PP = 0.0001f;
        public const float WALLMARK_SHIFT_V = 0.0001f;
        public const float VIEWPORT_NEAR = 0.2f;
        public const int DECAL_STENCIL = 16;
        public const float FLORA_MATERIAL_WEIGHT = 0.1f;
        public const int METADATA_KEY_PARTS = 13;

        private readonly XRayLevelImportRecord _record;
        private readonly XRayLevelMaterialUpdateMode _updateMode;
        private readonly bool _sourceBindings;
        private readonly HashSet<string> _claimed = new HashSet<string>();
        private readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

        public XRayLevelMaterialLibrary(XRayLevelImportRecord record, XRayLevelMaterialUpdateMode updateMode) : this(record, updateMode, false)
        {
        }

        public XRayLevelMaterialLibrary(XRayLevelImportRecord record, XRayLevelMaterialUpdateMode updateMode, bool sourceBindings)
        {
            _record = record;
            _updateMode = updateMode;
            _sourceBindings = sourceBindings;
        }

        public int Created { get; private set; }
        public int Reused { get; private set; }
        public int Fallbacks { get; private set; }
        public int MigratedDecals { get; private set; }
        public int FloraMaterials { get; private set; }
        public List<string> Paths { get; } = new List<string>();
        public List<string> Regenerated { get; } = new List<string>();

        public static string MetadataIdentity(string key)
        {
            string[] parts = (key ?? string.Empty).Split('|');
            if (parts.Length < METADATA_KEY_PARTS || parts[0] != "level" || parts[1] == "decal" || parts[1] == "lod")
            {
                return null;
            }

            parts[4] = "*";
            parts[5] = "*";
            return string.Join("|", parts);
        }

        public Material Get(XRayLevelMaterialRequest request)
        {
            string key = BuildKey(request);
            if (_cache.TryGetValue(key, out Material cached))
            {
                return cached;
            }

            XRayLevelSurface surface = request.Surface;
            string virtualPath = request.Textures?.Resolution?.Diffuse?.VirtualPath ?? XRayVirtualPath.TextureToVirtualPath(string.IsNullOrEmpty(request.Textures?.TextureName) ? "xray_default" : request.Textures.TextureName, ".dds");
            string folder = XRayLevelPaths.MaterialFolder(virtualPath);
            XRayLevelPaths.EnsureFolder(folder);
            List<string> candidates = XRayAssetNaming.CreateCandidates(virtualPath, BuildSuffix(request));
            string path = FindRegenerated(folder, candidates, key) ?? XRayAssetNaming.Place(folder, candidates, ".mat", key, XRayAssetMarker.ReadKey, _claimed);
            string previousKey = XRayAssetMarker.ReadKey(path);
            bool regenerated = !string.IsNullOrEmpty(previousKey) && previousKey != key;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool migrated = false;
            if (material == null && surface.IsDecal)
            {
                material = MigrateLegacyDecal(request, folder, virtualPath, path, key);
                migrated = material != null;
            }

            if (material != null)
            {
                Reused++;
                if (_updateMode == XRayLevelMaterialUpdateMode.Update || migrated || regenerated)
                {
                    Configure(material, request);
                    EditorUtility.SetDirty(material);
                }
                else if (_sourceBindings && !surface.IsFallback && !surface.IsDecal && !request.Detail)
                {
                    ConfigureSourceBindings(material, request);
                }

                if (regenerated)
                {
                    XRayAssetMarker.Write(path, new XRayAssetMarker(key, MATERIAL_ROLE, surface.RuleName, request.ShaderName, string.Empty));
                    Regenerated.Add(path);
                    _record.Warnings.Add($"Material '{path}' was regenerated in place (GUID kept): texture metadata changed ({Describe(previousKey)} -> {Describe(key)}).");
                }
            }
            else
            {
                string shaderName = surface.IsDecal ? DECAL_SHADER_NAME : surface.UsesMonolith ? MonolithMaterialConfigurator.SHADER_NAME : XRayMaterialGenerator.HDRP_LIT_SHADER;
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    _record.Errors.Add($"Shader '{shaderName}' is not available; material '{path}' was not created.");
                    return null;
                }

                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                Configure(material, request);
                AssetDatabase.CreateAsset(material, path);
                XRayAssetMarker.Write(path, new XRayAssetMarker(key, MATERIAL_ROLE, surface.RuleName, request.ShaderName, string.Empty));
                Created++;
            }

            if (surface.IsFallback)
            {
                Fallbacks++;
            }

            _cache[key] = material;
            Paths.Add(path);
            return material;
        }

        private string FindRegenerated(string folder, List<string> candidates, string key)
        {
            string identity = MetadataIdentity(key);
            if (identity == null)
            {
                return null;
            }

            string match = null;
            foreach (string candidate in candidates)
            {
                string path = folder + "/" + candidate + ".mat";
                string existing = XRayAssetMarker.ReadKey(path);
                if (existing == key)
                {
                    return null;
                }

                if (match == null && existing != null && !_claimed.Contains(path) && MetadataIdentity(existing) == identity)
                {
                    match = path;
                }
            }

            if (match != null)
            {
                _claimed.Add(match);
            }

            return match;
        }

        private static string Describe(string key)
        {
            string[] parts = key.Split('|');
            return parts.Length >= METADATA_KEY_PARTS ? $"bump {parts[4]}, THM {parts[5]}" : key;
        }

        public Material GetLod(string shaderName, XRayLevelTextureSet textures, string normalPath)
        {
            string albedoKey = textures?.AlbedoPath != null ? XRayAssetMarker.ReadKey(textures.AlbedoPath) ?? textures.AlbedoPath : "none:" + textures?.TextureName;
            string normalKey = normalPath != null ? XRayAssetMarker.ReadKey(normalPath) ?? normalPath : "none";
            string key = $"level|lod|{albedoKey}|{normalKey}|{shaderName}";
            if (_cache.TryGetValue(key, out Material cached))
            {
                return cached;
            }

            Texture2D albedo = XRayLevelTextureLibrary.Load(textures?.AlbedoPath);
            Texture2D normal = XRayLevelTextureLibrary.Load(normalPath);
            if (albedo == null || normal == null)
            {
                _record.Warnings.Add($"LOD impostor atlas '{textures?.TextureName}' is incomplete (albedo {(albedo != null ? "found" : "missing")}, normal/hemi {(normal != null ? "found" : "missing")}); MT_LOD impostors are not available.");
                return null;
            }

            string virtualPath = textures.Resolution?.Diffuse?.VirtualPath ?? XRayVirtualPath.TextureToVirtualPath(textures.TextureName, ".dds");
            string folder = XRayLevelPaths.MaterialFolder(virtualPath);
            XRayLevelPaths.EnsureFolder(folder);
            string path = XRayAssetNaming.Place(folder, XRayAssetNaming.CreateCandidates(virtualPath, "_lod"), ".mat", key, XRayAssetMarker.ReadKey, _claimed);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                Shader shader = Shader.Find(MonolithMaterialConfigurator.SHADER_NAME);
                if (shader == null)
                {
                    _record.Errors.Add($"Shader '{MonolithMaterialConfigurator.SHADER_NAME}' is not available; material '{path}' was not created.");
                    return null;
                }

                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            }

            if (created || _updateMode == XRayLevelMaterialUpdateMode.Update)
            {
                ConfigureLod(material, albedo, normal);
            }

            if (created)
            {
                AssetDatabase.CreateAsset(material, path);
                XRayAssetMarker.Write(path, new XRayAssetMarker(key, MATERIAL_ROLE, "lod", shaderName, string.Empty));
                Created++;
            }
            else
            {
                EditorUtility.SetDirty(material);
                Reused++;
            }

            _cache[key] = material;
            Paths.Add(path);
            return material;
        }

        private static void ConfigureLod(Material material, Texture2D albedo, Texture2D normal)
        {
            MonolithMaterialConfigurator.Configure(material, null, albedo, normal, null, false);
            material.enableInstancing = false;
            material.SetFloat("_MonolithHasBump", 0.0f);
            material.SetFloat("_MonolithMaterialId", 0.0f);
            material.SetFloat("_MonolithMaterialWeight", -0.5f);
            material.SetFloat("_MonolithFallbackGloss", DEF_GLOSS);
            material.SetFloat("_MonolithLevel", 0.0f);
            material.DisableKeyword("_MONOLITH_LEVEL");
            material.SetFloat("_MonolithLevelDetail", 0.0f);
            material.DisableKeyword("_MONOLITH_LEVEL_DETAIL");
            material.SetFloat("_MonolithLevelTerrain", 0.0f);
            material.DisableKeyword("_MONOLITH_LEVEL_TERRAIN");
            material.SetFloat("_MonolithLevelLod", 1.0f);
            material.EnableKeyword("_MONOLITH_LEVEL_LOD");
            material.SetFloat("_MonolithLevelAmbientHemi", 1.0f);
            SetAlphaTest(material, 0.0f);
            SetDoubleSided(material);
        }

        private Material MigrateLegacyDecal(XRayLevelMaterialRequest request, string folder, string virtualPath, string path, string key)
        {
            XRayLevelSurface surface = request.Surface;
            var legacySurface = new XRayLevelSurface { Kind = surface.Kind, HemiMode = surface.HemiMode, AlphaCutoff = surface.AlphaCutoff, DoubleSided = surface.DoubleSided, CastShadows = false, IsFallback = true, IsApproximation = true, RuleName = surface.RuleName, BlenderClass = surface.BlenderClass };
            var legacyRequest = new XRayLevelMaterialRequest { ShaderName = request.ShaderName, Surface = legacySurface, Textures = request.Textures, LightmapPath = request.LightmapPath, LightmapKey = request.LightmapKey, LightmapLabel = request.LightmapLabel, Detail = request.Detail, Terrain = request.Terrain };
            string legacyKey = BuildKey(legacyRequest);
            foreach (string candidate in XRayAssetNaming.CreateCandidates(virtualPath, BuildSuffix(legacyRequest)))
            {
                string legacyPath = folder + "/" + candidate + ".mat";
                if (XRayAssetMarker.ReadKey(legacyPath) != legacyKey)
                {
                    continue;
                }

                string error = AssetDatabase.MoveAsset(legacyPath, path);
                if (!string.IsNullOrEmpty(error))
                {
                    _record.Warnings.Add($"Wallmark material '{legacyPath}' could not be moved to '{path}': {error}. A new material is created instead.");
                    return null;
                }

                XRayAssetMarker.Write(path, new XRayAssetMarker(key, MATERIAL_ROLE, surface.RuleName, request.ShaderName, string.Empty));
                MigratedDecals++;
                return AssetDatabase.LoadAssetAtPath<Material>(path);
            }

            return null;
        }

        private static string BuildKey(XRayLevelMaterialRequest request)
        {
            XRayLevelSurface surface = request.Surface;
            string albedo = request.Textures != null && request.Textures.AlbedoPath != null ? XRayAssetMarker.ReadKey(request.Textures.AlbedoPath) ?? request.Textures.AlbedoPath : "none:" + request.Textures?.TextureName;
            if (surface.IsDecal)
            {
                return $"level|decal|{surface.RuleName}|{albedo}|{request.ShaderName}";
            }

            string bump = request.Textures != null && request.Textures.BumpPath != null ? XRayAssetMarker.ReadKey(request.Textures.BumpPath) ?? request.Textures.BumpPath : "none";
            XRayTextureDescriptionKey description = new XRayTextureDescriptionKey(request.Textures?.Description);
            return $"level|{surface.RuleName}|{(request.Detail ? "detail" : "static")}|{albedo}|{bump}|{description}|hemi{(int)surface.HemiMode}|{request.LightmapKey ?? "-"}|cut{surface.AlphaCutoff:0.####}|ds{(surface.DoubleSided ? 1 : 0)}|{(surface.IsFallback ? "lit" : "monolith")}|{request.ShaderName}|{(request.Terrain != null ? request.Terrain.Key() : "-")}";
        }

        private static string BuildSuffix(XRayLevelMaterialRequest request)
        {
            XRayLevelSurface surface = request.Surface;
            if (surface.IsDecal)
            {
                return "_" + surface.RuleName;
            }

            string suffix = surface.RuleName == "opaque" ? string.Empty : "_" + surface.RuleName;
            if (request.Detail)
            {
                suffix += "_detail";
            }
            else if (surface.HemiMode == XRayLevelHemiMode.Lightmap)
            {
                suffix += "_" + XRayLevelPaths.SanitizeName(request.LightmapLabel ?? "lmap");
            }
            else if (surface.HemiMode == XRayLevelHemiMode.Vertex)
            {
                suffix += "_v";
            }

            return surface.IsFallback ? suffix + FALLBACK_SUFFIX : suffix;
        }

        private void Configure(Material material, XRayLevelMaterialRequest request)
        {
            XRayLevelSurface surface = request.Surface;
            XRayLevelTextureSet textures = request.Textures;
            Texture2D albedo = XRayLevelTextureLibrary.Load(textures?.AlbedoPath);
            Texture2D bump = XRayLevelTextureLibrary.Load(textures?.BumpPath);
            if (surface.IsDecal)
            {
                ConfigureDecal(material, albedo);
                return;
            }

            if (surface.IsFallback)
            {
                if (material.shader.name != XRayMaterialGenerator.HDRP_LIT_SHADER)
                {
                    material.shader = Shader.Find(XRayMaterialGenerator.HDRP_LIT_SHADER);
                }

                XRayMaterialGenerator.Configure(material, surface.ToFallbackDescription(), albedo, null);
#if XRAY_HDRP
                if (surface.Kind == XRayLevelSurfaceKind.Transparent || surface.Kind == XRayLevelSurfaceKind.Decal || surface.Kind == XRayLevelSurfaceKind.Additive)
                {
                    material.SetFloat("_EnableBlendModePreserveSpecularLighting", 0.0f);
                    HDMaterial.ValidateMaterial(material);
                }
#endif
                material.enableInstancing = true;
                return;
            }

            MonolithMaterialConfigurator.Configure(material, textures?.Description, albedo, bump, _sourceBindings ? material.GetTexture(XRayLevelSourceBindings.CORRECTION) as Texture2D : null, !request.Detail);
            material.enableInstancing = true;
            if (request.Detail)
            {
                material.SetFloat("_MonolithLevel", 0.0f);
                material.DisableKeyword("_MONOLITH_LEVEL");
                material.SetFloat("_MonolithLevelDetail", 1.0f);
                material.EnableKeyword("_MONOLITH_LEVEL_DETAIL");
                material.SetFloat("_MonolithMaterialId", 0.0f);
                material.SetFloat("_MonolithMaterialWeight", 0.1f);
                material.SetFloat("_MonolithFallbackGloss", DEF_GLOSS);
                material.SetFloat("_MonolithLevelWind", 1.0f);
                SetAlphaTest(material, DETAIL_AREF);
                SetDoubleSided(material);
                return;
            }

            material.SetFloat("_MonolithLevelDetail", 0.0f);
            material.DisableKeyword("_MONOLITH_LEVEL_DETAIL");
            material.SetFloat("_MonolithLevel", 1.0f);
            material.EnableKeyword("_MONOLITH_LEVEL");
            material.SetFloat("_MonolithLevelHemiMode", (float)surface.HemiMode);
            material.SetFloat("_MonolithLevelAmbientHemi", 1.0f);
            Texture2D lightmap = XRayLevelTextureLibrary.Load(request.LightmapPath);
            material.SetTexture("_MonolithLevelHemiMap", lightmap);
            material.SetFloat("_MonolithLevelHemiFlipV", lightmap != null && (lightmap.format == TextureFormat.BC7 || lightmap.format == TextureFormat.BC6H) ? 0.0f : 1.0f);
            if (surface.HemiMode == XRayLevelHemiMode.Lightmap && lightmap == null)
            {
                material.SetFloat("_MonolithLevelHemiMode", (float)XRayLevelHemiMode.Constant);
            }

            if (surface.Kind == XRayLevelSurfaceKind.Terrain)
            {
                material.SetFloat("_MonolithMaterialId", 3.0f);
                material.SetFloat("_MonolithMaterialWeight", 0.3f);
                material.SetFloat("_MonolithHasBump", 0.0f);
                material.SetFloat("_MonolithFallbackGloss", 0.001f);
                ConfigureTerrain(material, request.Terrain);
            }
            else
            {
                material.SetFloat("_MonolithLevelTerrain", 0.0f);
                material.DisableKeyword("_MONOLITH_LEVEL_TERRAIN");
            }

            bool flora = surface.Flora && bump != null && UsesFloraNormal(textures?.Description);
            material.SetFloat("_MonolithFloraSubsurface", surface.Flora || request.Detail ? 1.0f : 0.0f);
            material.SetFloat("_MonolithLevelFlora", flora ? 1.0f : 0.0f);
            if (flora)
            {
                material.EnableKeyword("_MONOLITH_LEVEL_FLORA");
                material.SetFloat("_MonolithMaterialId", 0.0f);
                material.SetFloat("_MonolithMaterialWeight", FLORA_MATERIAL_WEIGHT);
                FloraMaterials++;
            }
            else
            {
                material.DisableKeyword("_MONOLITH_LEVEL_FLORA");
            }

            if (surface.Kind == XRayLevelSurfaceKind.AlphaTest)
            {
                SetAlphaTest(material, surface.AlphaCutoff);
            }

            if (surface.DoubleSided)
            {
                SetDoubleSided(material);
            }

            if (_sourceBindings)
            {
                ConfigureSourceBindings(material, request);
            }

#if XRAY_HDRP
            if (surface.Kind == XRayLevelSurfaceKind.Emissive && albedo != null)
            {
                material.SetTexture("_EmissiveColorMap", albedo);
                material.EnableKeyword("_EMISSIVE_COLOR_MAP");
                HDMaterial.SetUseEmissiveIntensity(material, true);
                HDMaterial.SetEmissiveColor(material, Color.white);
                HDMaterial.SetEmissiveIntensity(material, 6.0f, EmissiveIntensityUnit.EV100);
            }
#endif
        }

        private void ConfigureSourceBindings(Material material, XRayLevelMaterialRequest request)
        {
            XRayLevelTextureSet textures = request.Textures;
            XRayLevelSurface surface = request.Surface;
            XRayLevelSourceBinding binding = XRayLevelSourceBindings.Resolve(textures?.Description, surface.BlenderClass, surface.Kind, surface.Flora, request.Detail, material.GetTexture("_MonolithBump") != null && material.GetFloat("_MonolithHasBump") > 0.5f, material.GetFloat("_MonolithBumpFlipV"), textures?.CorrectionPath, textures?.DetailPath, textures?.DetailBumpPath, textures?.DetailCorrectionPath);
            XRayLevelBindingPlan plan = MonolithMaterialConfigurator.ConfigureSourceBindings(material, binding);
            if (plan.HasUserOverrides)
            {
                _record.Warnings.Add($"Material '{AssetDatabase.GetAssetPath(material)}' keeps user-authored X-Ray source binding values: {string.Join(", ", plan.Changes.FindAll(change => change.Ownership == XRayLevelBindingOwnership.UserOverride).ConvertAll(change => change.Property))}.");
            }
        }

        public static bool UsesFloraNormal(XRay.Importer.Sources.Textures.XRayTextureDescription description)
        {
            if (description == null || !description.HasBump || description.BumpMode == XRay.Importer.Sources.Textures.XRayBumpMode.UseParallax)
            {
                return false;
            }

            bool detail = !string.IsNullOrEmpty(description.DetailName) && (description.Flags & (XRay.Importer.Sources.Textures.XRayTextureDescription.FLAG_DIFFUSE_DETAIL | XRay.Importer.Sources.Textures.XRayTextureDescription.FLAG_BUMP_DETAIL)) != 0;
            return !detail;
        }

        private static void ConfigureDecal(Material material, Texture2D albedo)
        {
            Shader shader = Shader.Find(DECAL_SHADER_NAME);
            if (shader == null)
            {
                throw new InvalidOperationException(DECAL_SHADER_NAME + " shader is not imported.");
            }

            if (material.shader != shader)
            {
                string name = material.name;
                var template = new Material(shader);
                EditorUtility.CopySerialized(template, material);
                UnityEngine.Object.DestroyImmediate(template);
                material.name = name;
            }

            material.SetTexture("_BaseColorMap", albedo);
            material.SetFloat("_MonolithAlbedoSrgb", albedo != null && albedo.isDataSRGB ? 1.0f : 0.0f);
            material.SetFloat("_MonolithDecalNormalShift", WMARK_NORMAL_SHIFT);
            material.SetFloat("_MonolithDecalMinShift", WMARK_MIN_SHIFT);
            material.SetFloat("_MonolithDecalMaxShift", WMARK_MAX_SHIFT);
            material.SetFloat("_MonolithDecalProjectionShift", WALLMARK_SHIFT_PP);
            material.SetFloat("_MonolithDecalViewShift", WALLMARK_SHIFT_V);
            material.SetFloat("_MonolithDecalXRayNear", VIEWPORT_NEAR);
            material.SetFloat("_MonolithDecalAlphaRef", 0.0f);
            material.SetInt("_DecalStencilRef", DECAL_STENCIL);
            material.SetInt("_DecalStencilWriteMask", DECAL_STENCIL);
            material.renderQueue = (int)RenderQueue.Geometry;
            material.enableInstancing = true;
        }

        private static void ConfigureTerrain(Material material, XRayLevelTerrainTextures terrain)
        {
            bool enabled = terrain != null && terrain.MaskPath != null && Array.TrueForAll(terrain.DetailPaths, path => path != null);
            material.SetFloat("_MonolithLevelTerrain", enabled ? 1.0f : 0.0f);
            if (!enabled)
            {
                material.DisableKeyword("_MONOLITH_LEVEL_TERRAIN");
                return;
            }

            material.EnableKeyword("_MONOLITH_LEVEL_TERRAIN");
            material.SetTexture("_MonolithTerrainMask", XRayLevelTextureLibrary.Load(terrain.MaskPath));
            string[] channels = { "R", "G", "B", "A" };
            for (int i = 0; i < 4; i++)
            {
                material.SetTexture("_MonolithTerrainDetail" + channels[i], XRayLevelTextureLibrary.Load(terrain.DetailPaths[i]));
                material.SetTexture("_MonolithTerrainBump" + channels[i], XRayLevelTextureLibrary.Load(terrain.BumpPaths[i]));
            }

            material.SetFloat("_MonolithTerrainDetailScale", terrain.DetailScale);
            material.SetFloat("_MonolithTerrainBumpScale", 0.6f);
            ConfigureTerrainSource(material, terrain);
        }

        private static void ConfigureTerrainSource(Material material, XRayLevelTerrainTextures terrain)
        {
            if (terrain.Source == null) return;
            string[] channels = { "R", "G", "B", "A" };
            var colorFlip = Vector4.zero;
            var bumpFlip = Vector4.zero;
            var heightFlip = Vector4.zero;
            bool hasHeight = terrain.Source.UsesHeight;
            Texture2D sourceBase = XRayLevelTextureLibrary.Load(terrain.SourceBasePath);
            bool hasColor = sourceBase != null && !sourceBase.isDataSRGB;
            material.SetTexture("_MonolithTerrainSourceBase", sourceBase);
            for (int i = 0; i < channels.Length; i++)
            {
                Texture2D detail = XRayLevelTextureLibrary.Load(terrain.SourceColorPaths[i]);
                Texture2D bump = XRayLevelTextureLibrary.Load(terrain.BumpPaths[i]);
                Texture2D height = XRayLevelTextureLibrary.Load(terrain.HeightPaths[i]);
                material.SetTexture("_MonolithTerrainHeight" + channels[i], height);
                material.SetTexture("_MonolithTerrainSourceColor" + channels[i], detail);
                colorFlip[i] = detail != null && (detail.format == TextureFormat.BC7 || detail.format == TextureFormat.BC6H) ? 1.0f : 0.0f;
                bumpFlip[i] = bump != null && (bump.format == TextureFormat.BC7 || bump.format == TextureFormat.BC6H) ? 1.0f : 0.0f;
                heightFlip[i] = height != null && (height.format == TextureFormat.BC7 || height.format == TextureFormat.BC6H) ? 1.0f : 0.0f;
                hasHeight &= height != null;
                hasColor &= detail != null && !detail.isDataSRGB;
            }
            Texture2D mask = XRayLevelTextureLibrary.Load(terrain.MaskPath);
            material.SetFloat("_MonolithTerrainMaskFlipV", mask != null && (mask.format == TextureFormat.BC7 || mask.format == TextureFormat.BC6H) ? 1.0f : 0.0f);
            material.SetVector("_MonolithTerrainColorFlipV", colorFlip);
            material.SetVector("_MonolithTerrainBumpFlipV", bumpFlip);
            material.SetVector("_MonolithTerrainHeightFlipV", heightFlip);
            material.SetFloat("_MonolithHasTerrainHeight", hasHeight ? 1.0f : 0.0f);
            material.SetFloat("_MonolithHasTerrainSourceColor", hasColor ? 1.0f : 0.0f);
            material.SetVector("_MonolithTerrainColorSrgb", Vector4.zero);
            material.SetFloat("_MonolithTerrainSource", (float)terrain.Source.Profile);
            material.SetFloat("_MonolithTerrainBaseFlipV", sourceBase != null && (sourceBase.format == TextureFormat.BC7 || sourceBase.format == TextureFormat.BC6H) ? 1.0f : 0.0f);
            material.SetOverrideTag("XRayTerrainBinding", XRayTerrainSourceBinding.VERSION);
            material.SetOverrideTag("XRayTerrainShaderSHA256", terrain.Source.ShaderHash);
            material.SetOverrideTag("XRayTerrainBrdfSHA256", terrain.Source.BrdfHash);
        }

        private static void SetAlphaTest(Material material, float cutoff)
        {
            material.SetFloat("_SurfaceType", 0.0f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetFloat("_ZWrite", 1.0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_AlphaCutoffEnable", 1.0f);
            material.SetFloat("_AlphaCutoff", cutoff);
            material.SetFloat("_AlphaCutoffShadow", cutoff);
            material.SetFloat("_AlphaCutoffPrepass", cutoff);
            material.SetFloat("_AlphaCutoffPostpass", cutoff);
            material.SetFloat("_UseShadowThreshold", 1.0f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }

        private static void SetDoubleSided(Material material)
        {
            material.SetFloat("_DoubleSidedEnable", 1.0f);
            material.SetFloat("_CullMode", 0.0f);
            material.SetFloat("_CullModeForward", 0.0f);
            material.EnableKeyword("_DOUBLESIDED_ON");
        }

        private readonly struct XRayTextureDescriptionKey
        {
            private readonly string _value;

            public XRayTextureDescriptionKey(XRay.Importer.Sources.Textures.XRayTextureDescription description)
            {
                _value = description == null ? "nodesc" : $"m{description.MaterialId}w{description.MaterialWeight:0.####}b{(int)description.BumpMode}";
            }

            public override string ToString()
            {
                return _value;
            }
        }
    }
}
