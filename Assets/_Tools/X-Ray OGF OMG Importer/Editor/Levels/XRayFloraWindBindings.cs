using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayFloraWindBindings
    {
        public const string TAG = "XRayFloraWindOwnership";
        public const string VERSION = "xray-flora-wind-ownership-v1";
        public const string CONTRACT_TAG = "XRayFloraWindContractSHA256";
        public const string WIND = "_MonolithFloraWind";
        public const string PART = "_MonolithFloraWindPart";
        public const string WAVES = "_MonolithWindWaves";
        public const string WAVES_FLIP = "_MonolithWindWavesFlipV";
        public const float PART_BRANCH = 0.0f;
        public const float PART_TRUNK = 1.0f;
        public const string MOTION_VECTORS_PASS = "MotionVectors";

        public static bool Applies(Material material, XRayLevelMaterialRequest request)
        {
            if (material == null || request?.Surface == null || request.Surface.IsFallback || request.Detail || material.shader.name != MonolithMaterialConfigurator.SHADER_NAME || !material.HasProperty(WIND))
            {
                return false;
            }

            return request.Surface.RuleName == "tree_leaves" || request.Surface.RuleName == "tree_trunk";
        }

        public static XRayLevelBindingPlan Plan(Material material, XRayLevelMaterialRequest request, XRayFloraSourceProfile profile, string wavesPath, bool wavesFlipV, bool created)
        {
            var plan = new XRayLevelBindingPlan { PreviousTag = material.GetTag(TAG, false, string.Empty) };
            Dictionary<string, string> previous = XRayLevelSourceBindings.ParseTag(plan.PreviousTag.Replace(VERSION, XRayLevelSourceBindings.VERSION));
            bool accepted = profile == XRayFloraSourceProfile.AnomalySss || profile == XRayFloraSourceProfile.TrueStalker;
            if (!accepted && previous == null)
            {
                plan.Tag = plan.PreviousTag;
                return plan;
            }

            bool anomaly = profile == XRayFloraSourceProfile.AnomalySss;
            float wind = !accepted ? 0.0f : anomaly ? 1.0f : 2.0f;
            float part = request.Surface.Flora ? PART_BRANCH : PART_TRUNK;
            string waves = accepted && anomaly && !string.IsNullOrEmpty(wavesPath) ? AssetDatabase.AssetPathToGUID(wavesPath) : string.Empty;
            XRayFloraMaterialBindings.Decide(plan, previous, WIND, Number(material.GetFloat(WIND)), Number(0.0f), Number(wind), created);
            XRayFloraMaterialBindings.Decide(plan, previous, PART, Number(material.GetFloat(PART)), Number(0.0f), Number(accepted ? part : 0.0f), created);
            XRayFloraMaterialBindings.Decide(plan, previous, WAVES, XRayLevelSourceBindings.TextureValue(material, WAVES), string.Empty, waves, created);
            XRayFloraMaterialBindings.Decide(plan, previous, WAVES_FLIP, Number(material.GetFloat(WAVES_FLIP)), Number(0.0f), Number(waves.Length > 0 && wavesFlipV ? 1.0f : 0.0f), created);
            var tag = new StringBuilder(VERSION);
            foreach (XRayLevelBindingChange change in plan.Changes) XRayLevelSourceBindings.AppendOwnership(tag, change);
            plan.Tag = tag.ToString();
            return plan;
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

                if (change.Property == WAVES)
                {
                    material.SetTexture(WAVES, string.IsNullOrEmpty(change.Final) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(change.Final)));
                }
                else
                {
                    material.SetFloat(change.Property, float.Parse(change.Final, CultureInfo.InvariantCulture));
                }
            }

            if (material.GetFloat(WIND) > 0.5f && !material.GetShaderPassEnabled(MOTION_VECTORS_PASS))
            {
                material.SetShaderPassEnabled(MOTION_VECTORS_PASS, true);
            }

            material.SetOverrideTag(TAG, plan.Tag);
            material.SetOverrideTag(CONTRACT_TAG, contractHash ?? string.Empty);
            EditorUtility.SetDirty(material);
            return true;
        }

        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
