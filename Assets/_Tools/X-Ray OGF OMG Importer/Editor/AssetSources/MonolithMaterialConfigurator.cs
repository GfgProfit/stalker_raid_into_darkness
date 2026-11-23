using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Shaders;
using XRay.Importer.Sources.Textures;

namespace XRay.Importer.Editor
{
    public static class MonolithMaterialConfigurator
    {
        public const string SHADER_NAME = "HDRP/MonolithLit";
        public const float FLORA_SOURCE_LEGACY = 0.0f;
        public const float FLORA_SOURCE_ANOMALY = 1.0f;
        public const float FLORA_SOURCE_TRUE_STALKER = 2.0f;

        private static readonly string[] _surfaceFloatProperties = { "_SurfaceType", "_ZWrite", "_ZTestDepthEqualForOpaque", "_SrcBlend", "_DstBlend", "_AlphaSrcBlend", "_AlphaDstBlend", "_CullMode", "_CullModeForward", "_StencilRef", "_StencilWriteMask" };
        private static readonly string[] _surfaceVectorProperties = { "_UVMappingMask", "_DoubleSidedConstants" };

        public static void Configure(Material material, XRayTextureDescription description, Texture2D albedo, Texture2D bump, Texture2D correction, bool flipGreen)
        {
            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                throw new InvalidOperationException("HDRP/MonolithLit shader is not imported.");
            }

            material.shader = shader;
            material.SetTexture("_BaseColorMap", albedo);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_MonolithBump", bump);
            material.SetTexture("_MonolithBumpCorrection", correction);
            material.SetFloat("_MonolithHasBumpCorrection", bump != null && correction != null ? 1.0f : 0.0f);
            material.SetFloat("_MonolithHasBump", bump != null ? 1.0f : 0.0f);
            material.SetFloat("_MonolithBumpFlipV", RequiresPackedTextureFlipV(bump) ? 1.0f : 0.0f);
            material.SetFloat("_MonolithMaterialId", description != null && description.HasMaterial ? description.MaterialId : 1);
            material.SetFloat("_MonolithMaterialWeight", description != null && description.HasMaterial ? (float)description.MaterialWeight : 0.0f);
            material.SetFloat("_MonolithFlipGreen", flipGreen ? 1.0f : 0.0f);
            material.SetFloat("_MonolithAlbedoSrgb", albedo != null && albedo.isDataSRGB ? 1.0f : 0.0f);
            material.SetFloat("_MaterialID", 4.0f);
            material.SetFloat("_EnergyConservingSpecularColor", 0.0f);
            material.SetFloat("_SurfaceType", 0.0f);
            material.SetFloat("_ZWrite", 1.0f);
            material.SetFloat("_ZTestDepthEqualForOpaque", 3.0f);
            material.SetFloat("_SrcBlend", 1.0f);
            material.SetFloat("_DstBlend", 0.0f);
            material.SetFloat("_AlphaSrcBlend", 1.0f);
            material.SetFloat("_AlphaDstBlend", 0.0f);
            material.SetFloat("_CullMode", 2.0f);
            material.SetFloat("_CullModeForward", 2.0f);
            material.SetVector("_UVMappingMask", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
            material.SetVector("_DoubleSidedConstants", new Vector4(1.0f, 1.0f, 1.0f, 0.0f));
            material.SetFloat("_StencilRef", 0.0f);
            material.SetFloat("_StencilWriteMask", 3.0f);
            material.EnableKeyword("_MATERIAL_FEATURE_SPECULAR_COLOR");
            material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
            material.DisableKeyword("_NORMALMAP");
            material.renderQueue = 2000;
            EditorUtility.SetDirty(material);
        }

        internal static bool RequiresPackedTextureFlipV(Texture2D texture)
        {
            return texture != null && (texture.format == TextureFormat.BC7 || texture.format == TextureFormat.BC6H) && !AssetDatabase.GetAssetPath(texture).EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        public static void ConfigurePreservingSurface(Material material, XRayTextureDescription description, Texture2D albedo, Texture2D bump, Texture2D correction, bool flipGreen)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            var floats = new Dictionary<string, float>();
            var vectors = new Dictionary<string, Vector4>();
            foreach (string property in _surfaceFloatProperties)
            {
                if (material.HasProperty(property))
                {
                    floats[property] = material.GetFloat(property);
                }
            }

            foreach (string property in _surfaceVectorProperties)
            {
                if (material.HasProperty(property))
                {
                    vectors[property] = material.GetVector(property);
                }
            }

            Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            Color? savedColor;
            using (var serialized = new SerializedObject(material))
            {
                SerializedProperty property = FindSavedBaseColor(serialized);
                savedColor = property != null ? property.colorValue : (Color?)null;
            }

            Vector2 scale = material.HasProperty("_BaseColorMap") ? material.GetTextureScale("_BaseColorMap") : Vector2.one;
            Vector2 offset = material.HasProperty("_BaseColorMap") ? material.GetTextureOffset("_BaseColorMap") : Vector2.zero;
            int renderQueue = material.renderQueue;
            bool normalMap = material.IsKeywordEnabled("_NORMALMAP");
            bool tangentSpace = material.IsKeywordEnabled("_NORMALMAP_TANGENT_SPACE");
            bool preserveSmoothness = material.shader != null && material.shader.name == XRayMaterialGenerator.HDRP_LIT_SHADER;
            float smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0.5f;
            Configure(material, description, albedo, bump, correction, flipGreen);
            foreach (KeyValuePair<string, float> property in floats)
            {
                material.SetFloat(property.Key, property.Value);
            }

            foreach (KeyValuePair<string, Vector4> property in vectors)
            {
                material.SetVector(property.Key, property.Value);
            }

            material.SetColor("_BaseColor", color);
            if (savedColor.HasValue)
            {
                using (var serialized = new SerializedObject(material))
                {
                    SerializedProperty property = FindSavedBaseColor(serialized);
                    if (property != null)
                    {
                        property.colorValue = savedColor.Value;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }

            material.SetTextureScale("_BaseColorMap", scale);
            material.SetTextureOffset("_BaseColorMap", offset);
            material.renderQueue = renderQueue;
            foreach (string keyword in new[] { "_MATERIAL_FEATURE_SUBSURFACE_SCATTERING", "_MATERIAL_FEATURE_TRANSMISSION", "_MATERIAL_FEATURE_ANISOTROPY", "_MATERIAL_FEATURE_IRIDESCENCE", "_MATERIAL_FEATURE_CLEAR_COAT" })
            {
                material.DisableKeyword(keyword);
            }

            if (bump == null)
            {
                if (normalMap)
                {
                    material.EnableKeyword("_NORMALMAP");
                }

                if (!tangentSpace)
                {
                    material.DisableKeyword("_NORMALMAP_TANGENT_SPACE");
                }

                if (preserveSmoothness)
                {
                    double decoded = MonolithMaterialMath.DecodeMaterial((uint)material.GetFloat("_MonolithMaterialId"), material.GetFloat("_MonolithMaterialWeight"));
                    material.SetFloat("_MonolithFallbackGloss", GlossFromSmoothness(smoothness, decoded, material.GetFloat("_MonolithDynamicGloss")));
                }
            }

            EditorUtility.SetDirty(material);
        }

        private static SerializedProperty FindSavedBaseColor(SerializedObject material)
        {
            SerializedProperty colors = material.FindProperty("m_SavedProperties.m_Colors");
            for (int i = 0; i < colors.arraySize; i++)
            {
                SerializedProperty entry = colors.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == "_BaseColor")
                {
                    return entry.FindPropertyRelative("second");
                }
            }

            return null;
        }

        private static float GlossFromSmoothness(float smoothness, double material, double dynamicGloss)
        {
            double roughness = 1.0 - Mathf.Clamp01(smoothness);
            double minimum = 0.0;
            double maximum = 1.0;
            for (int i = 0; i < 32; i++)
            {
                double middle = (minimum + maximum) * 0.5;
                if (MonolithMaterialMath.Roughness(middle, material, dynamicGloss) > roughness)
                {
                    minimum = middle;
                }
                else
                {
                    maximum = middle;
                }
            }

            return (float)((minimum + maximum) * 0.5);
        }

        public static bool ConfigureFloraSource(Material material, float source, Texture2D correction)
        {
            bool flora = material.IsKeywordEnabled("_MONOLITH_LEVEL_FLORA") && material.GetTexture("_MonolithBump") != null && material.GetFloat("_MonolithHasBump") > 0.5f;
            bool enabled = flora && (source == FLORA_SOURCE_ANOMALY || (source == FLORA_SOURCE_TRUE_STALKER && correction != null));
            if (enabled && source == FLORA_SOURCE_TRUE_STALKER)
            {
                material.SetTexture("_MonolithBumpCorrection", correction);
                material.SetFloat("_MonolithHasBumpCorrection", 1.0f);
            }

            material.SetFloat("_MonolithFloraSource", enabled ? source : FLORA_SOURCE_LEGACY);
            EditorUtility.SetDirty(material);
            return enabled;
        }

        public static Levels.XRayLevelBindingPlan ConfigureSourceBindings(Material material, Levels.XRayLevelSourceBinding binding)
        {
            Levels.XRayLevelBindingPlan plan = Levels.XRayLevelSourceBindings.Plan(material, binding);
            Levels.XRayLevelSourceBindings.Apply(material, plan);
            return plan;
        }
    }
}
