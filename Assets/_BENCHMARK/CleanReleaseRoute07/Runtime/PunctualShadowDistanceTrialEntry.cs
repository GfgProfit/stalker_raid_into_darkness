using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Rvm.CleanReleaseRoute07
{
    internal sealed class PunctualShadowDistanceTrialEntry
    {
        public readonly Light Light;
        public readonly Transform Transform;
        public readonly HDAdditionalLightData Data;
        public readonly LightType Type;
        public readonly LightShadows Shadows;
        public readonly bool Enabled;
        public readonly float Intensity;
        public readonly float Range;
        public readonly Color Color;
        public readonly int CullingMask;
        public readonly int RenderingLayerMask;
        public readonly float FadeDistance;
        public readonly float ShadowDimmer;
        public readonly float VolumetricShadowDimmer;
        public readonly bool Eligible;

        public bool FadeOverridden { get; set; }

        public PunctualShadowDistanceTrialEntry(Light light, HDAdditionalLightData data)
        {
            Light = light;
            Transform = light.transform;
            Data = data;
            Type = light.type;
            Shadows = light.shadows;
            Enabled = light.enabled;
            Intensity = light.intensity;
            Range = light.range;
            Color = light.color;
            CullingMask = light.cullingMask;
            RenderingLayerMask = light.renderingLayerMask;
            FadeDistance = data != null ? data.shadowFadeDistance : 0f;
            ShadowDimmer = data != null ? data.shadowDimmer : 0f;
            VolumetricShadowDimmer = data != null ? data.volumetricShadowDimmer : 0f;
            Eligible = (Type == LightType.Point || Type == LightType.Spot) && Shadows != LightShadows.None && data != null && float.IsFinite(FadeDistance) && FadeDistance > 0f && float.IsFinite(ShadowDimmer) && ShadowDimmer > 0f && float.IsFinite(VolumetricShadowDimmer);
        }
    }
}
