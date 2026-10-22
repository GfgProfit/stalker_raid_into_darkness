using System;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Textures;

namespace XRay.Importer.Editor
{
    public static class MonolithMaterialConfigurator
    {
        public const string SHADER_NAME = "HDRP/MonolithLit";

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
            material.SetFloat("_MonolithHasBump", bump != null ? 1.0f : 0.0f);
            material.SetFloat("_MonolithBumpFlipV", bump != null && (bump.format == TextureFormat.BC7 || bump.format == TextureFormat.BC6H) ? 1.0f : 0.0f);
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
    }
}
