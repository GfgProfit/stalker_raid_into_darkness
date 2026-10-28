using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayTerrainMaterialBindings
    {
        public const string TAG = "XRayTerrainOwnership";
        public const string VERSION = "xray-terrain-ownership-v1";

        private static readonly string[] CHANNELS = { "R", "G", "B", "A" };
        private static readonly string[] VECTOR_PROPERTIES = { "_MonolithTerrainColorFlipV", "_MonolithTerrainBumpFlipV", "_MonolithTerrainHeightFlipV", "_MonolithTerrainColorSrgb", "_MonolithTerrainPom", "_MonolithTerrainOffset" };

        public static XRayLevelBindingPlan Plan(Material material, XRayLevelTerrainTextures terrain, XRayTerrainImportMode mode, bool created = false, XRayTerrainQuality quality = XRayTerrainQuality.High)
        {
            var plan = new XRayLevelBindingPlan { PreviousTag = material.GetTag(TAG, false, string.Empty) };
            Dictionary<string, string> previous = XRayLevelSourceBindings.ParseTag(plan.PreviousTag.Replace(VERSION, XRayLevelSourceBindings.VERSION));
            var textures = new Dictionary<string, string>();
            textures["_MonolithTerrainMask"] = terrain?.MaskPath;
            textures["_MonolithTerrainSourceBase"] = terrain?.SourceBasePath;
            textures["_MonolithTerrainLodAtlas"] = terrain?.LodAtlasPath;
            for (int i = 0; i < CHANNELS.Length; i++)
            {
                textures["_MonolithTerrainDetail" + CHANNELS[i]] = terrain?.DetailPaths[i];
                textures["_MonolithTerrainBump" + CHANNELS[i]] = terrain?.BumpPaths[i];
                textures["_MonolithTerrainSourceColor" + CHANNELS[i]] = terrain?.SourceColorPaths[i];
                textures["_MonolithTerrainHeight" + CHANNELS[i]] = terrain?.HeightPaths[i];
            }
            var bound = new Dictionary<string, Texture2D>();
            foreach (KeyValuePair<string, string> pair in textures)
            {
                string desired = string.IsNullOrEmpty(pair.Value) ? string.Empty : AssetDatabase.AssetPathToGUID(pair.Value);
                XRayLevelBindingChange change = Decide(material, plan, previous, pair.Key, XRayLevelSourceBindings.TextureValue(material, pair.Key), string.Empty, desired, created);
                bound[pair.Key] = change.Final == change.Current ? material.GetTexture(pair.Key) as Texture2D : XRayLevelTextureLibrary.Load(AssetDatabase.GUIDToAssetPath(change.Final));
            }
            bool anomaly = terrain?.Source?.Profile == XRayTerrainSourceProfile.AnomalyAtmospherics;
            bool colors = anomaly && Linear(bound["_MonolithTerrainSourceBase"]);
            bool height = anomaly;
            bool complete = Linear(bound["_MonolithTerrainMask"]);
            Vector4 colorFlip = Vector4.zero;
            Vector4 bumpFlip = Vector4.zero;
            Vector4 heightFlip = Vector4.zero;
            for (int i = 0; i < CHANNELS.Length; i++)
            {
                Texture2D color = bound["_MonolithTerrainSourceColor" + CHANNELS[i]];
                Texture2D bump = bound["_MonolithTerrainBump" + CHANNELS[i]];
                Texture2D layerHeight = bound["_MonolithTerrainHeight" + CHANNELS[i]];
                colors &= Linear(color);
                height &= Linear(layerHeight);
                complete &= Linear(bump) && bound["_MonolithTerrainDetail" + CHANNELS[i]] != null;
                colorFlip[i] = Flip(color);
                bumpFlip[i] = Flip(bump);
                heightFlip[i] = Flip(layerHeight);
            }
            var values = new Dictionary<string, float>
            {
                ["_MonolithTerrainSource"] = (float)(terrain?.Source?.Profile ?? XRayTerrainSourceProfile.Unknown),
                ["_MonolithHasTerrainSourceColor"] = colors && complete ? 1.0f : 0.0f,
                ["_MonolithHasTerrainHeight"] = height ? 1.0f : 0.0f,
                ["_MonolithTerrainDetailScale"] = terrain?.DetailScale ?? 1.0f,
                ["_MonolithTerrainBumpScale"] = 0.6f,
                ["_MonolithTerrainMaskFlipV"] = Flip(bound["_MonolithTerrainMask"]),
                ["_MonolithTerrainBaseFlipV"] = Flip(bound["_MonolithTerrainSourceBase"])
            };
            values["_MonolithTerrainMidAvailable"] = terrain?.Source?.VerifiedMid == true && Linear(bound["_MonolithTerrainLodAtlas"]) ? 1.0f : 0.0f;
            values["_MonolithTerrainLowAvailable"] = terrain?.Source?.VerifiedLow == true ? 1.0f : 0.0f;
            values["_MonolithTerrainLodFlipV"] = Flip(bound["_MonolithTerrainLodAtlas"]);
            bool enable = mode == XRayTerrainImportMode.EnableVerifiedAnomaly && anomaly && colors && complete;
            values["_MonolithTerrainQuality"] = created || enable ? (float)quality : material.GetFloat("_MonolithTerrainQuality");
            foreach (string property in new[] { "_MonolithSourceProfile", "_MonolithSourceTerrain", "_MonolithTerrainParallax", "_MonolithTerrainOcclusion", "_MonolithTerrainVirtualHeight" })
            {
                float desired = material.GetFloat(property);
                if (!anomaly) desired = 0.0f;
                else if (enable) desired = property == "_MonolithTerrainVirtualHeight" ? desired : property == "_MonolithTerrainParallax" || property == "_MonolithTerrainOcclusion" ? (height ? 1.0f : 0.0f) : 1.0f;
                values[property] = desired;
            }
            foreach (KeyValuePair<string, float> pair in values)
            {
                int index = material.shader.FindPropertyIndex(pair.Key);
                float defaultValue = index >= 0 ? material.shader.GetPropertyDefaultFloatValue(index) : 0.0f;
                bool safety = pair.Key == "_MonolithTerrainSource" || pair.Key == "_MonolithTerrainMidAvailable" || pair.Key == "_MonolithTerrainLowAvailable" || pair.Key.StartsWith("_MonolithHasTerrain", StringComparison.Ordinal) || !anomaly && (pair.Key == "_MonolithSourceProfile" || pair.Key == "_MonolithSourceTerrain" || pair.Key == "_MonolithTerrainParallax" || pair.Key == "_MonolithTerrainOcclusion" || pair.Key == "_MonolithTerrainVirtualHeight");
                Decide(material, plan, previous, pair.Key, Number(material.GetFloat(pair.Key)), Number(defaultValue), Number(pair.Value), created || safety);
            }
            Vector4[] vectors = { colorFlip, bumpFlip, heightFlip, Vector4.zero, terrain?.Parameters?.Pom ?? material.GetVector("_MonolithTerrainPom"), terrain?.Parameters?.Offset ?? material.GetVector("_MonolithTerrainOffset") };
            for (int i = 0; i < VECTOR_PROPERTIES.Length; i++)
            {
                string property = VECTOR_PROPERTIES[i];
                int index = material.shader.FindPropertyIndex(property);
                Vector4 defaultValue = index >= 0 ? material.shader.GetPropertyDefaultVectorValue(index) : Vector4.zero;
                Decide(material, plan, previous, property, Vector(material.GetVector(property)), Vector(defaultValue), Vector(vectors[i]), created);
            }
            var tag = new StringBuilder(VERSION);
            foreach (XRayLevelBindingChange change in plan.Changes) XRayLevelSourceBindings.AppendOwnership(tag, change);
            plan.Tag = tag.ToString();
            return plan;
        }

        public static bool Apply(Material material, XRayLevelTerrainTextures terrain, XRayLevelBindingPlan plan)
        {
            bool modifies = plan.Modifies;
            foreach (XRayLevelBindingChange change in plan.Changes)
            {
                if (!change.Modifies) continue;
                if (Array.IndexOf(VECTOR_PROPERTIES, change.Property) >= 0)
                {
                    string[] parts = change.Final.Split(',');
                    material.SetVector(change.Property, new Vector4(Parse(parts[0]), Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                }
                else if (change.Property == "_MonolithTerrainMask" || change.Property == "_MonolithTerrainSourceBase" || change.Property == "_MonolithTerrainLodAtlas" || change.Property.StartsWith("_MonolithTerrainDetail", StringComparison.Ordinal) && change.Property != "_MonolithTerrainDetailScale" || change.Property.StartsWith("_MonolithTerrainBump", StringComparison.Ordinal) && change.Property != "_MonolithTerrainBumpScale" || change.Property.StartsWith("_MonolithTerrainSourceColor", StringComparison.Ordinal) || change.Property.StartsWith("_MonolithTerrainHeight", StringComparison.Ordinal))
                {
                    material.SetTexture(change.Property, string.IsNullOrEmpty(change.Final) ? null : AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(change.Final)));
                }
                else material.SetFloat(change.Property, Parse(change.Final));
            }
            if (plan.Modifies) material.SetOverrideTag(TAG, plan.Tag);
            modifies |= SetTag(material, "XRayTerrainBinding", XRayTerrainSourceBinding.VERSION);
            modifies |= SetTag(material, "XRayTerrainShaderSHA256", terrain?.Source?.ShaderHash ?? string.Empty);
            modifies |= SetTag(material, "XRayTerrainBrdfSHA256", terrain?.Source?.BrdfHash ?? string.Empty);
            modifies |= SetTag(material, "XRayTerrainMidSHA256", terrain?.Source?.MidHash ?? string.Empty);
            modifies |= SetTag(material, "XRayTerrainLowSHA256", terrain?.Source?.LowHash ?? string.Empty);
            modifies |= SetTag(material, "XRayTerrainSettingsSHA256", terrain?.Parameters?.Hash ?? string.Empty);
            if (modifies) EditorUtility.SetDirty(material);
            return modifies;
        }

        private static XRayLevelBindingChange Decide(Material material, XRayLevelBindingPlan plan, Dictionary<string, string> previous, string property, string current, string defaultValue, string desired, bool generated)
        {
            XRayLevelBindingChange change = XRayLevelSourceBindings.Decide(property, current, defaultValue, previous, desired);
            if (generated)
            {
                change.Ownership = XRayLevelBindingOwnership.Generated;
                change.Final = desired;
            }
            plan.Changes.Add(change);
            return change;
        }

        private static bool SetTag(Material material, string name, string value)
        {
            if (material.GetTag(name, false, string.Empty) == value) return false;
            material.SetOverrideTag(name, value);
            return true;
        }

        private static bool Linear(Texture2D texture)
        {
            return texture != null && !texture.isDataSRGB;
        }

        private static float Flip(Texture2D texture)
        {
            return texture != null && (texture.format == TextureFormat.BC7 || texture.format == TextureFormat.BC6H) ? 1.0f : 0.0f;
        }

        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Vector(Vector4 value)
        {
            return Number(value.x) + "," + Number(value.y) + "," + Number(value.z) + "," + Number(value.w);
        }

        private static float Parse(string value)
        {
            return float.Parse(value, CultureInfo.InvariantCulture);
        }
    }
}
