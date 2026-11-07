using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Shaders;
using XRay.Importer.Sources.Textures;
#if XRAY_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace XRay.Importer.Editor
{
    public sealed class XRayMaterialResult
    {
        public Material Material { get; set; }
        public string AssetPath { get; set; }
        public XRayResourceStatus Status { get; set; }
    }

    public sealed class XRayMaterialGenerator
    {
        public const string MATERIAL_ROLE = "Material";
        public const string HDRP_LIT_SHADER = "HDRP/Lit";

        private readonly bool _preserveExisting;
        private readonly HashSet<string> _claimed;
        private readonly List<string> _warnings;

        public XRayMaterialGenerator(bool preserveExisting, HashSet<string> claimed, List<string> warnings)
        {
            _preserveExisting = preserveExisting;
            _claimed = claimed;
            _warnings = warnings;
        }

        public XRayMaterialResult GetOrCreate(string key, string nameSource, string shaderName, XRaySurfaceDescription surface, Texture2D albedo, Texture2D normal, string folder, XRayMaterialGenerationMode mode = XRayMaterialGenerationMode.MonolithHdrp, XRayTextureDescription description = null, Texture2D packedBump = null, Texture2D packedCorrection = null, bool flipGreen = true)
        {
            bool monolith = mode == XRayMaterialGenerationMode.MonolithHdrp;
            string suffix = IsDefaultSurface(surface) ? string.Empty : "_" + GetShaderLeaf(shaderName);
            string migratedPath = monolith ? FindMigratedMaterial(key, nameSource, suffix, folder) : null;

            if (monolith)
            {
                key += "#monolith";
            }

            suffix += monolith ? "_monolith" : string.Empty;
            List<string> candidates = XRayAssetNaming.CreateCandidates(nameSource, suffix);
            string path = migratedPath ?? XRayAssetNaming.Place(folder, candidates, ".mat", key, XRayAssetMarker.ReadKey, _claimed);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (_preserveExisting)
                {
                    return new XRayMaterialResult { Material = existing, AssetPath = path, Status = XRayResourceStatus.Preserved };
                }

                if (monolith && packedBump == null && normal == null && existing.HasProperty("_NormalMap"))
                {
                    normal = existing.GetTexture("_NormalMap") as Texture2D;
                }

                string[] monolithKeywords = null;
                if (monolith && existing.shader.name == MonolithMaterialConfigurator.SHADER_NAME)
                {
                    monolithKeywords = existing.shaderKeywords;
                    existing.shader = Shader.Find(HDRP_LIT_SHADER) ?? throw new System.InvalidOperationException("HDRP/Lit is required to validate imported material surface settings.");
                }

                Configure(existing, surface, albedo, normal);
                if (monolith)
                {
                    MonolithMaterialConfigurator.ConfigurePreservingSurface(existing, description, albedo, packedBump, packedCorrection, flipGreen);
                    if (monolithKeywords != null)
                    {
                        foreach (string keyword in monolithKeywords)
                        {
                            if (keyword.StartsWith("_MONOLITH_", System.StringComparison.Ordinal))
                            {
                                existing.EnableKeyword(keyword);
                            }
                        }
                    }
                }
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                return new XRayMaterialResult { Material = existing, AssetPath = path, Status = XRayResourceStatus.Updated };
            }

            Shader shader = Shader.Find(HDRP_LIT_SHADER);
            if (shader == null)
            {
                _warnings.Add($"Shader '{HDRP_LIT_SHADER}' not found; '{path}' uses the render pipeline default shader.");
                Material pipelineDefault = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
                shader = pipelineDefault != null ? pipelineDefault.shader : Shader.Find("Standard");
            }

            var material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            Configure(material, surface, albedo, normal);
            if (monolith)
            {
                MonolithMaterialConfigurator.ConfigurePreservingSurface(material, description, albedo, packedBump, packedCorrection, flipGreen);
            }
            Directory.CreateDirectory(folder);
            AssetDatabase.CreateAsset(material, path);
            XRayAssetMarker.Write(path, new XRayAssetMarker(key, MATERIAL_ROLE, surface.RuleName, shaderName, string.Empty));
            return new XRayMaterialResult { Material = material, AssetPath = path, Status = XRayResourceStatus.Created };
        }

        public static void Configure(Material material, XRaySurfaceDescription surface, Texture2D albedo, Texture2D normal)
        {
            SetTexture(material, "_BaseColorMap", albedo);
            SetTexture(material, "_MainTex", albedo);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            SetTexture(material, "_NormalMap", normal);
            if (normal != null && material.HasProperty("_NormalScale"))
            {
                material.SetFloat("_NormalScale", 1.0f);
            }

            if (material.HasProperty("_DoubleSidedEnable"))
            {
                material.SetFloat("_DoubleSidedEnable", surface.DoubleSided ? 1.0f : 0.0f);
            }

#if XRAY_HDRP
            if (material.shader.name != HDRP_LIT_SHADER)
            {
                return;
            }

            bool transparent = surface.Kind == XRaySurfaceKind.Transparent || surface.Kind == XRaySurfaceKind.Additive || surface.Kind == XRaySurfaceKind.Collimator || surface.Kind == XRaySurfaceKind.Distortion;
            HDMaterial.SetSurfaceType(material, transparent);
            HDMaterial.SetAlphaClipping(material, surface.Kind == XRaySurfaceKind.AlphaTest);
            HDMaterial.SetAlphaCutoff(material, surface.AlphaCutoff);
            if (material.HasProperty("_BlendMode"))
            {
                material.SetFloat("_BlendMode", surface.Kind == XRaySurfaceKind.Additive || surface.Kind == XRaySurfaceKind.Collimator ? 1.0f : 0.0f);
            }

            bool emissive = surface.Kind == XRaySurfaceKind.Collimator || surface.Kind == XRaySurfaceKind.Emissive;
            SetTexture(material, "_EmissiveColorMap", emissive ? albedo : null);
            if (emissive)
            {
                HDMaterial.SetUseEmissiveIntensity(material, true);
                HDMaterial.SetEmissiveColor(material, Color.white);
                HDMaterial.SetEmissiveIntensity(material, surface.EmissiveEv100, EmissiveIntensityUnit.EV100);
            }
            else if (material.HasProperty("_EmissiveColor"))
            {
                material.SetColor("_EmissiveColor", Color.black);
            }

            HDMaterial.ValidateMaterial(material);
#endif
        }

        public static bool IsDefaultSurface(XRaySurfaceDescription surface)
        {
            return surface.Kind == XRaySurfaceKind.Opaque && !surface.DoubleSided;
        }

        private string FindMigratedMaterial(string key, string nameSource, string suffix, string folder)
        {
            foreach (string candidate in XRayAssetNaming.CreateCandidates(nameSource, suffix))
            {
                string path = folder + "/" + candidate + ".mat";
                if ((_claimed != null && _claimed.Contains(path)) || XRayAssetMarker.ReadKey(path) != key)
                {
                    continue;
                }

                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null && material.shader != null && material.shader.name == MonolithMaterialConfigurator.SHADER_NAME)
                {
                    _claimed?.Add(path);
                    return path;
                }
            }

            return null;
        }

        private static string GetShaderLeaf(string shaderName)
        {
            string normalized = (shaderName ?? string.Empty).Replace('/', '\\');
            int separator = normalized.LastIndexOf('\\');
            string leaf = separator >= 0 ? normalized.Substring(separator + 1) : normalized;
            return leaf.Length > 0 ? leaf : "shader";
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }
    }
}
