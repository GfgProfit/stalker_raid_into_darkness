using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayFloraMaterialBindings
    {
        public const string TAG = "XRayFloraOwnership";
        public const string VERSION = "xray-flora-ownership-v1";
        public const string ALBEDO = "_BaseColorMap";
        public const string FLORA_SOURCE = "_MonolithFloraSource";

        private static readonly string[] CUTOFFS = { "_AlphaCutoff", "_AlphaCutoffShadow", "_AlphaCutoffPrepass", "_AlphaCutoffPostpass" };
        private static readonly string[] TEXTURES = { ALBEDO, XRayLevelSourceBindings.CORRECTION, XRayFloraWindBindings.WAVES };

        public static bool Applies(Material material, XRayLevelMaterialRequest request)
        {
            if (material == null || request?.Surface == null || request.Surface.IsFallback || material.shader.name != MonolithMaterialConfigurator.SHADER_NAME)
            {
                return false;
            }

            return request.Detail || request.Surface.Flora && material.IsKeywordEnabled("_MONOLITH_LEVEL_FLORA");
        }

        public static XRayLevelBindingPlan Plan(Material material, XRayLevelMaterialRequest request, XRayFloraImportMode mode, XRayFloraSourceProfile profile, bool created)
        {
            var plan = new XRayLevelBindingPlan { PreviousTag = material.GetTag(TAG, false, string.Empty) };
            Dictionary<string, string> previous = XRayLevelSourceBindings.ParseTag(plan.PreviousTag.Replace(VERSION, XRayLevelSourceBindings.VERSION));
            bool accepted = XRayFloraSourceBinding.Accepts(mode, profile);
            if (!accepted && previous == null)
            {
                plan.Tag = plan.PreviousTag;
                return plan;
            }

            bool trueStalker = profile == XRayFloraSourceProfile.TrueStalker;
            float legacyCutoff = request.Surface.AlphaCutoff;
            if (request.Detail)
            {
                float cutoff = !accepted ? legacyCutoff : trueStalker && request.Textures?.BumpPath != null ? XRayFloraSourceBinding.GRASS_AREF : XRayFloraSourceBinding.AREF;
                foreach (string property in CUTOFFS) Decide(plan, previous, property, Number(material.GetFloat(property)), Number(legacyCutoff), Number(cutoff), created);
                if (request.RuntimeDetail)
                {
                    string detailLegacyAlbedo = Guid(request.Textures?.AlbedoPath);
                    string detailAuthoredAlbedo = accepted ? Guid(request.Flora?.AuthoredAlbedoPath) : string.Empty;
                    XRayLevelBindingChange detailAlbedo = Decide(plan, previous, ALBEDO, XRayLevelSourceBindings.TextureValue(material, ALBEDO), detailLegacyAlbedo, detailAuthoredAlbedo.Length > 0 ? detailAuthoredAlbedo : detailLegacyAlbedo, created);
                    Texture2D detailTexture = Load(detailAlbedo.Final);
                    Decide(plan, previous, "_MonolithAlbedoSrgb", Number(material.GetFloat("_MonolithAlbedoSrgb")), Number(material.GetFloat("_MonolithAlbedoSrgb")), Number(detailTexture != null && detailTexture.isDataSRGB ? 1.0f : 0.0f), true);
                    string detailCorrection = accepted && trueStalker ? Guid(request.Flora?.CorrectionPath) : string.Empty;
                    Decide(plan, previous, XRayLevelSourceBindings.CORRECTION, XRayLevelSourceBindings.TextureValue(material, XRayLevelSourceBindings.CORRECTION), string.Empty, detailCorrection, created);
                    Decide(plan, previous, XRayLevelSourceBindings.HAS_CORRECTION, Number(material.GetFloat(XRayLevelSourceBindings.HAS_CORRECTION)), Number(0.0f), Number(detailCorrection.Length > 0 ? 1.0f : 0.0f), created);
                    Decide(plan, previous, "_MonolithGrassSource", Number(material.GetFloat("_MonolithGrassSource")), Number(0.0f), Number(accepted ? trueStalker ? 2.0f : 1.0f : 0.0f), created);
                    Decide(plan, previous, "_MonolithGrassRuntime", Number(material.GetFloat("_MonolithGrassRuntime")), Number(0.0f), Number(accepted ? 1.0f : 0.0f), created);
                    Decide(plan, previous, "_MonolithSourceProfile", Number(material.GetFloat("_MonolithSourceProfile")), Number(0.0f), Number(accepted && !trueStalker ? 1.0f : 0.0f), created);
                    string grassWaves = Guid(request.Flora?.GrassWavesPath);
                    Decide(plan, previous, XRayFloraWindBindings.WAVES, XRayLevelSourceBindings.TextureValue(material, XRayFloraWindBindings.WAVES), string.Empty, grassWaves, created);
                    Decide(plan, previous, XRayFloraWindBindings.WAVES_FLIP, Number(material.GetFloat(XRayFloraWindBindings.WAVES_FLIP)), Number(0.0f), Number(grassWaves.Length > 0 && XRayLevelSourceBindings.PackedFlipV(request.Flora.GrassWavesPath) ? 1.0f : 0.0f), created);
                }
                return Finish(plan);
            }

            string legacyAlbedo = Guid(request.Textures?.AlbedoPath);
            string authored = accepted ? Guid(request.Flora?.AuthoredAlbedoPath) : string.Empty;
            XRayLevelBindingChange albedo = Decide(plan, previous, ALBEDO, XRayLevelSourceBindings.TextureValue(material, ALBEDO), legacyAlbedo, authored.Length > 0 ? authored : legacyAlbedo, created);
            string legacyCorrection = string.Empty;
            string correctionDesired = accepted && trueStalker ? Guid(request.Flora?.CorrectionPath) : legacyCorrection;
            XRayLevelBindingChange correction = Decide(plan, previous, XRayLevelSourceBindings.CORRECTION, XRayLevelSourceBindings.TextureValue(material, XRayLevelSourceBindings.CORRECTION), legacyCorrection, correctionDesired, created);
            Texture2D albedoTexture = Load(albedo.Final);
            Texture2D bump = material.GetTexture("_MonolithBump") as Texture2D;
            Texture2D correctionTexture = Load(correction.Final);
            bool hasBump = bump != null && !bump.isDataSRGB && material.GetFloat("_MonolithHasBump") > 0.5f;
            bool hasCorrection = hasBump && correctionTexture != null && !correctionTexture.isDataSRGB && (XRayLevelSourceBindings.PackedFlipV(AssetDatabase.GetAssetPath(correctionTexture)) ? 1.0f : 0.0f) == material.GetFloat("_MonolithBumpFlipV");
            float source = !accepted || !hasBump ? MonolithMaterialConfigurator.FLORA_SOURCE_LEGACY : trueStalker ? hasCorrection ? MonolithMaterialConfigurator.FLORA_SOURCE_TRUE_STALKER : MonolithMaterialConfigurator.FLORA_SOURCE_LEGACY : MonolithMaterialConfigurator.FLORA_SOURCE_ANOMALY;
            bool active = source > 0.5f;
            Decide(plan, previous, "_MonolithAlbedoSrgb", Number(material.GetFloat("_MonolithAlbedoSrgb")), Number(material.GetFloat("_MonolithAlbedoSrgb")), Number(albedoTexture != null && albedoTexture.isDataSRGB ? 1.0f : 0.0f), true);
            Decide(plan, previous, XRayLevelSourceBindings.HAS_CORRECTION, Number(material.GetFloat(XRayLevelSourceBindings.HAS_CORRECTION)), Number(material.GetFloat(XRayLevelSourceBindings.HAS_CORRECTION)), Number(hasCorrection ? 1.0f : 0.0f), true);
            Decide(plan, previous, FLORA_SOURCE, Number(material.GetFloat(FLORA_SOURCE)), Number(MonolithMaterialConfigurator.FLORA_SOURCE_LEGACY), Number(source), created);
            Decide(plan, previous, "_MonolithSourceProfile", Number(material.GetFloat("_MonolithSourceProfile")), Number(0.0f), Number(active && !trueStalker ? 1.0f : 0.0f), created || trueStalker);
            Decide(plan, previous, "_DoubleSidedEnable", Number(material.GetFloat("_DoubleSidedEnable")), Number(1.0f), Number(active ? 0.0f : 1.0f), created);
            Decide(plan, previous, "_CullMode", Number(material.GetFloat("_CullMode")), Number(0.0f), Number(active ? (float)UnityEngine.Rendering.CullMode.Back : 0.0f), created);
            Decide(plan, previous, "_CullModeForward", Number(material.GetFloat("_CullModeForward")), Number(0.0f), Number(active ? (float)UnityEngine.Rendering.CullMode.Back : 0.0f), created);
            foreach (string property in CUTOFFS) Decide(plan, previous, property, Number(material.GetFloat(property)), Number(legacyCutoff), Number(accepted ? XRayFloraSourceBinding.AREF : legacyCutoff), created);
            return Finish(plan);
        }

        public static bool Apply(Material material, XRayLevelBindingPlan plan, string contractHash)
        {
            if (!plan.Modifies)
            {
                return false;
            }

            foreach (XRayLevelBindingChange change in plan.Changes)
            {
                if (!change.Modifies)
                {
                    continue;
                }

                if (System.Array.IndexOf(TEXTURES, change.Property) >= 0)
                {
                    material.SetTexture(change.Property, Load(change.Final));
                }
                else
                {
                    material.SetFloat(change.Property, float.Parse(change.Final, CultureInfo.InvariantCulture));
                }
            }

            if (material.GetFloat("_DoubleSidedEnable") > 0.5f)
            {
                material.EnableKeyword("_DOUBLESIDED_ON");
            }
            else
            {
                material.DisableKeyword("_DOUBLESIDED_ON");
            }

            material.SetOverrideTag(TAG, plan.Tag);
            if (material.GetFloat("_MonolithGrassRuntime") > 0.5f) material.SetShaderPassEnabled(XRayFloraWindBindings.MOTION_VECTORS_PASS, true);
            material.SetOverrideTag("XRayFloraBinding", XRayFloraSourceBinding.VERSION);
            material.SetOverrideTag("XRayFloraContractSHA256", contractHash ?? string.Empty);
            EditorUtility.SetDirty(material);
            return true;
        }

        private static XRayLevelBindingPlan Finish(XRayLevelBindingPlan plan)
        {
            var tag = new StringBuilder(VERSION);
            foreach (XRayLevelBindingChange change in plan.Changes) XRayLevelSourceBindings.AppendOwnership(tag, change);
            plan.Tag = tag.ToString();
            return plan;
        }

        internal static XRayLevelBindingChange Decide(XRayLevelBindingPlan plan, Dictionary<string, string> previous, string property, string current, string legacy, string desired, bool generated)
        {
            XRayLevelBindingChange change;
            if (previous == null && !generated)
            {
                change = new XRayLevelBindingChange { Property = property, Current = current, Desired = desired };
                change.Ownership = current == legacy || current == desired ? XRayLevelBindingOwnership.Generated : XRayLevelBindingOwnership.UserOverride;
                change.Previous = change.Ownership == XRayLevelBindingOwnership.UserOverride ? legacy : null;
                change.Final = change.Ownership == XRayLevelBindingOwnership.UserOverride ? current : desired;
            }
            else
            {
                change = XRayLevelSourceBindings.Decide(property, current, legacy, previous, desired);
                if (generated)
                {
                    change.Ownership = XRayLevelBindingOwnership.Generated;
                    change.Final = desired;
                }
            }

            plan.Changes.Add(change);
            return change;
        }

        private static Texture2D Load(string guid)
        {
            return string.IsNullOrEmpty(guid) || guid.StartsWith("instance:", System.StringComparison.Ordinal) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static string Guid(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
        }

        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
