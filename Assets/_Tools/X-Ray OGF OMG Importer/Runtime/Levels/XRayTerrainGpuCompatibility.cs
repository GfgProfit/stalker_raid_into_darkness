using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public static class XRayTerrainGpuCompatibility
    {
        private const string TERRAIN_KEYWORD = "_MONOLITH_LEVEL_TERRAIN";
        private const string TERRAIN_PROPERTY = "_MonolithLevelTerrain";

        private static readonly Type DISALLOW_GPU_DRAWING = Type.GetType("UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime");
        private static readonly PropertyInfo ALLOW_GPU_DRAWING = typeof(Renderer).GetProperty("allowGPUDrivenRendering", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static void ConfigureImportedRenderer(MeshRenderer renderer, Material[] materials)
        {
            if (DISALLOW_GPU_DRAWING != null && UsesTerrain(materials) && renderer.GetComponent(DISALLOW_GPU_DRAWING) == null)
            {
                renderer.gameObject.AddComponent(DISALLOW_GPU_DRAWING);
            }
        }

        internal static void ApplyToExistingGeometry(Transform geometry)
        {
            if (geometry == null || ALLOW_GPU_DRAWING?.CanWrite != true)
            {
                return;
            }
            var materials = new List<Material>();
            foreach (MeshRenderer renderer in geometry.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.GetSharedMaterials(materials);
                if (UsesTerrain(materials))
                {
                    ALLOW_GPU_DRAWING.SetValue(renderer, false);
                }
            }
        }

        private static bool UsesTerrain(IReadOnlyList<Material> materials)
        {
            for (int i = 0; i < materials.Count; i++)
            {
                Material material = materials[i];
                if (material != null && (material.IsKeywordEnabled(TERRAIN_KEYWORD) || (material.HasProperty(TERRAIN_PROPERTY) && material.GetFloat(TERRAIN_PROPERTY) > 0.5f)))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
