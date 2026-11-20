using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.GraphicsOptions
{
    internal sealed class VideoHdEffects : IDisposable
    {
        private readonly GlobalIllumination _gi;
        private readonly ScreenSpaceReflection _ssr;
        private readonly VideoSharpening _sharpening;

        private int _reflections;
        private bool _ssgiEnabled;
        private bool _ssrEnabled;
        private bool _transparentSsrEnabled;
        private bool _decalsEnabled;

        public static bool SupportsReflections => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset;
        public static bool SupportsSsgi => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp && hdrp.currentPlatformRenderPipelineSettings.supportSSGI;
        public static bool SupportsSsr => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp && hdrp.currentPlatformRenderPipelineSettings.supportSSR;
        public static bool SupportsDecals => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp && hdrp.currentPlatformRenderPipelineSettings.supportDecals;
        public static bool SupportsSharpening => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset;

        public VideoHdEffects(VolumeProfile profile)
        {
            _gi = profile.Add<GlobalIllumination>();
            _ssr = profile.Add<ScreenSpaceReflection>();
            _sharpening = profile.Add<VideoSharpening>();
        }

        public void Apply(int reflections, int ssgi, int ssr, int decals, int sharpening)
        {
            _reflections = reflections;
            _ssgiEnabled = ssgi > 0 && SupportsSsgi;
            _ssrEnabled = ssr > 0 && SupportsSsr;
            _transparentSsrEnabled = _ssrEnabled && GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp && hdrp.currentPlatformRenderPipelineSettings.supportSSRTransparent;
            _decalsEnabled = decals > 0 && SupportsDecals;
            XRayWetnessReflection.SetVideoQuality(reflections);
            _gi.enable.Override(_ssgiEnabled);
            _gi.tracing.Override(RayCastingMode.RayMarching);
            _gi.quality.Override(Mathf.Max(0, ssgi - 1));
            _ssr.enabled.Override(_ssrEnabled);
            _ssr.enabledTransparent.Override(_transparentSsrEnabled);
            _ssr.tracing.Override(RayCastingMode.RayMarching);
            _ssr.quality.Override(Mathf.Max(0, ssr - 1));
            _sharpening.Strength.Override(Mathf.Clamp01(sharpening / 100.0f));
        }

        public void PrepareCamera(HDAdditionalCameraData camera)
        {
            FrameSettings settings = camera.renderingPathCustomFrameSettings;
            FrameSettingsOverrideMask mask = camera.renderingPathCustomFrameSettingsOverrideMask;
            Set(ref settings, ref mask, FrameSettingsField.ReflectionProbe, _reflections > 0);
            Set(ref settings, ref mask, FrameSettingsField.PlanarProbe, _reflections > 1);
            Set(ref settings, ref mask, FrameSettingsField.SSGI, _ssgiEnabled);
            Set(ref settings, ref mask, FrameSettingsField.SSR, _ssrEnabled);
            Set(ref settings, ref mask, FrameSettingsField.TransparentSSR, _transparentSsrEnabled);
            Set(ref settings, ref mask, FrameSettingsField.Decals, _decalsEnabled);
            if (_sharpening.Strength.value > 0.0f) Set(ref settings, ref mask, FrameSettingsField.CustomPostProcess, true);
            camera.renderingPathCustomFrameSettings = settings;
            camera.renderingPathCustomFrameSettingsOverrideMask = mask;
        }

        public void Dispose()
        {
            XRayWetnessReflection.SetVideoQuality(null);
        }

        private static void Set(ref FrameSettings settings, ref FrameSettingsOverrideMask mask, FrameSettingsField field, bool enabled)
        {
            settings.SetEnabled(field, enabled);
            mask.mask[(uint)field] = true;
        }
    }
}
