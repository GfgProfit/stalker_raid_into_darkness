using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor
{
    public sealed class MonolithMaterialInspector : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUI.BeginChangeCheck();
            base.OnGUI(materialEditor, properties);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            foreach (UnityEngine.Object target in materialEditor.targets)
            {
                UpdateTexturePresence((Material)target);
            }
        }

        public static void UpdateTexturePresence(Material material)
        {
            Texture2D bump = material.GetTexture("_MonolithBump") as Texture2D;
            Texture2D correction = material.GetTexture(Levels.XRayLevelSourceBindings.CORRECTION) as Texture2D;
            Texture2D detail = material.GetTexture(Levels.XRayLevelSourceBindings.DETAIL) as Texture2D;
            Texture2D detailBump = material.GetTexture(Levels.XRayLevelSourceBindings.DETAIL_BUMP) as Texture2D;
            Texture2D detailCorrection = material.GetTexture(Levels.XRayLevelSourceBindings.DETAIL_CORRECTION) as Texture2D;
            bool hasBump = bump != null && !bump.isDataSRGB && material.GetFloat("_MonolithHasBump") > 0.5f;
            bool hasDetail = detail != null && !float.IsNaN(material.GetFloat(Levels.XRayLevelSourceBindings.DETAIL_SCALE)) && !float.IsInfinity(material.GetFloat(Levels.XRayLevelSourceBindings.DETAIL_SCALE));
            SetPresence(material, Levels.XRayLevelSourceBindings.HAS_CORRECTION, hasBump && correction != null && !correction.isDataSRGB && FlipV(correction) == (material.GetFloat("_MonolithBumpFlipV") > 0.5f));
            SetPresence(material, Levels.XRayLevelSourceBindings.HAS_DETAIL, hasDetail);
            SetPresence(material, Levels.XRayLevelSourceBindings.HAS_DETAIL_BUMP, hasBump && hasDetail && detailBump != null && detailCorrection != null && !detailBump.isDataSRGB && !detailCorrection.isDataSRGB && FlipV(detailBump) == FlipV(detail) && FlipV(detailCorrection) == FlipV(detail));
            bool hasTerrainHeight = Mathf.RoundToInt(material.GetFloat("_MonolithTerrainSource")) == (int)Levels.XRayTerrainSourceProfile.AnomalyAtmospherics;
            Texture2D terrainBase = material.GetTexture("_MonolithTerrainSourceBase") as Texture2D;
            Texture2D terrainMask = material.GetTexture("_MonolithTerrainMask") as Texture2D;
            bool hasTerrainColor = hasTerrainHeight && terrainBase != null && !terrainBase.isDataSRGB && terrainMask != null && !terrainMask.isDataSRGB;
            foreach (string channel in new[] { "R", "G", "B", "A" })
            {
                Texture2D height = material.GetTexture("_MonolithTerrainHeight" + channel) as Texture2D;
                hasTerrainHeight &= height != null && !height.isDataSRGB;
                Texture2D color = material.GetTexture("_MonolithTerrainSourceColor" + channel) as Texture2D;
                hasTerrainColor &= color != null && !color.isDataSRGB;
                Texture2D terrainBump = material.GetTexture("_MonolithTerrainBump" + channel) as Texture2D;
                hasTerrainColor &= terrainBump != null && !terrainBump.isDataSRGB;
            }
            SetPresence(material, "_MonolithHasTerrainHeight", hasTerrainHeight);
            SetPresence(material, "_MonolithHasTerrainSourceColor", hasTerrainColor);
            Texture2D atlas = material.GetTexture("_MonolithTerrainLodAtlas") as Texture2D;
            bool anomalyTerrain = Mathf.RoundToInt(material.GetFloat("_MonolithTerrainSource")) == (int)Levels.XRayTerrainSourceProfile.AnomalyAtmospherics;
            SetPresence(material, "_MonolithTerrainMidAvailable", anomalyTerrain && atlas != null && !atlas.isDataSRGB && material.GetTag("XRayTerrainMidSHA256", false, string.Empty) == Levels.XRayTerrainSourceBinding.ANOMALY_MID_HASH);
            SetPresence(material, "_MonolithTerrainLowAvailable", anomalyTerrain && material.GetTag("XRayTerrainLowSHA256", false, string.Empty) == Levels.XRayTerrainSourceBinding.ANOMALY_LOW_HASH);
        }

        private static bool FlipV(Texture2D texture)
        {
            return texture.format == TextureFormat.BC7 || texture.format == TextureFormat.BC6H;
        }

        private static void SetPresence(Material material, string property, bool present)
        {
            float value = present ? 1.0f : 0.0f;
            if (material.GetFloat(property) != value)
            {
                material.SetFloat(property, value);
                EditorUtility.SetDirty(material);
            }
        }
    }
}
