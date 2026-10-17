using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
#if XRAY_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace XRay.Importer.Editor
{
    public sealed class XRayMaterialBuilder
    {
        private static readonly string[] TEXTURE_EXTENSIONS = { ".dds", ".png", ".tga", ".tif", ".tiff", ".psd", ".jpg", ".jpeg", ".exr" };

        private readonly AssetImportContext _context;
        private readonly bool _createMaterials;
        private readonly string _texturesFolder;
        private readonly bool _searchByName;
        private readonly List<string> _warnings;
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Material> _externalMaterials = new Dictionary<string, Material>();
        private Material _fallback;
        private bool _reportedMissingShader;

        public XRayMaterialBuilder(AssetImportContext context, bool createMaterials, string texturesFolder, bool searchByName, List<string> warnings, Dictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> externalObjects)
        {
            _context = context;
            _createMaterials = createMaterials;
            _texturesFolder = string.IsNullOrEmpty(texturesFolder) ? null : texturesFolder.Replace('\\', '/').TrimEnd('/');
            _searchByName = searchByName;
            _warnings = warnings;
            if (externalObjects == null)
            {
                return;
            }

            foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> pair in externalObjects)
            {
                if (pair.Key.type == typeof(Material) && pair.Value is Material external)
                {
                    _externalMaterials[pair.Key.name] = external;
                }
            }
        }

        public IEnumerable<Material> Materials => _materials.Values;

        public Material Get(string texture, string shader)
        {
            string key = XRayMaterialSlot.MakeKey(texture, shader);
            if (_externalMaterials.TryGetValue(key, out Material external))
            {
                return external;
            }

            if (!_createMaterials)
            {
                return GetFallback();
            }

            if (_materials.TryGetValue(key, out Material material))
            {
                return material;
            }

            Shader lit = Shader.Find("HDRP/Lit");
            if (lit == null)
            {
                if (!_reportedMissingShader)
                {
                    _warnings.Add("Shader 'HDRP/Lit' not found; materials use the render pipeline default shader.");
                    _reportedMissingShader = true;
                }

                Material pipelineDefault = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
                lit = pipelineDefault != null ? pipelineDefault.shader : Shader.Find("Standard");
            }

            string relative = (texture ?? string.Empty).Replace('\\', '/');
            material = new Material(lit) { name = string.IsNullOrEmpty(relative) ? "xray_default" : relative.Replace('/', '_') };
            Texture2D albedo = FindTexture(relative);
            if (albedo != null)
            {
                material.SetTexture("_BaseColorMap", albedo);
                material.SetTexture("_MainTex", albedo);
            }
            else if (!string.IsNullOrEmpty(relative))
            {
                _warnings.Add($"Texture '{texture}' (shader '{shader}') was not found in the project.");
            }

            ConfigureSurface(material, shader ?? string.Empty);
            _materials.Add(key, material);
            _context.AddObjectToAsset("material:" + key, material);
            return material;
        }

        private static void ConfigureSurface(Material material, string shader)
        {
            string lower = shader.ToLowerInvariant();
            bool alphaTest = lower.Contains("aref");
            bool transparent = lower.Contains("transparent") || lower.Contains("glass") || lower.Contains("collimator");
#if XRAY_HDRP
            if (material.shader.name != "HDRP/Lit")
            {
                return;
            }

            if (alphaTest)
            {
                HDMaterial.SetAlphaClipping(material, true);
                HDMaterial.SetAlphaCutoff(material, 0.5f);
            }

            if (transparent)
            {
                HDMaterial.SetSurfaceType(material, true);
            }

            HDMaterial.ValidateMaterial(material);
#endif
        }

        private Material GetFallback()
        {
            if (_fallback == null)
            {
                _fallback = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
                if (_fallback == null)
                {
                    _fallback = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                }
            }

            return _fallback;
        }

        private Texture2D FindTexture(string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }

            if (_texturesFolder != null)
            {
                foreach (string extension in TEXTURE_EXTENSIONS)
                {
                    string path = _texturesFolder + "/" + relative + extension;
                    if (File.Exists(path))
                    {
                        return Load(path);
                    }
                }
            }

            if (!_searchByName)
            {
                return null;
            }

            string fileName = Path.GetFileName(relative);
            string best = null;
            int bestScore = -1;
            foreach (string guid in AssetDatabase.FindAssets(fileName + " t:Texture2D"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string withoutExtension = Path.ChangeExtension(path, null).Replace('\\', '/');
                if (!string.Equals(Path.GetFileName(withoutExtension), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int score = withoutExtension.EndsWith(relative, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                if (score > bestScore || (score == bestScore && string.CompareOrdinal(path, best) < 0))
                {
                    best = path;
                    bestScore = score;
                }
            }

            return best != null ? Load(best) : null;
        }

        private Texture2D Load(string path)
        {
            _context.DependsOnArtifact(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
