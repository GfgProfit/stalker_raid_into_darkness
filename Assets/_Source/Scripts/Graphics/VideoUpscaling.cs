using UnityEngine;
using UnityEngine.AMD;
using UnityEngine.NVIDIA;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace RaidIntoDarkness.GraphicsOptions
{
    internal static class VideoUpscaling
    {
        private const int NATIVE = 0;
        private const int TAAU = 1;
        private const int FSR2 = 2;
        private const int DLSS = 3;
        private const int BALANCED = 1;
        private const int PERFORMANCE = 2;
        private const int ULTRA_PERFORMANCE = 3;

        public static bool IsSupported(int method)
        {
            if (method == NATIVE) return true;
            if (GraphicsSettings.currentRenderPipeline is not HDRenderPipelineAsset hdrp) return false;
            RenderPipelineSettings settings = hdrp.currentPlatformRenderPipelineSettings;
            if (!settings.dynamicResolutionSettings.enabled || !settings.supportMotionVectors) return false;
            return method switch
            {
                TAAU => true,
                FSR2 => HDDynamicResolutionPlatformCapabilities.FSR2Detected && settings.dynamicResolutionSettings.advancedUpscalerNames?.Contains("FSR2") == true,
                DLSS => HDDynamicResolutionPlatformCapabilities.DLSSDetected && settings.dynamicResolutionSettings.advancedUpscalerNames?.Contains("DLSS") == true,
                _ => false
            };
        }

        public static int ResolveMethod(int method)
        {
            if (IsSupported(method)) return method;
            return IsSupported(TAAU) ? TAAU : NATIVE;
        }

        public static bool UsesQuality(int method)
        {
            return ResolveMethod(method) != NATIVE;
        }

        public static bool SupportsUltraPerformance(int method)
        {
            return ResolveMethod(method) is FSR2 or DLSS;
        }

        public static float GetRenderScale(int method, int quality, int existingScale)
        {
            method = ResolveMethod(method);
            if (method == NATIVE) return Mathf.Clamp(existingScale, 50.0f, 100.0f);
            return quality switch
            {
                ULTRA_PERFORMANCE when method is FSR2 or DLSS => 100.0f / 3.0f,
                PERFORMANCE or ULTRA_PERFORMANCE => 50.0f,
                BALANCED => 100.0f / 1.7f,
                _ => 100.0f / 1.5f
            };
        }

        private static uint Fsr2Quality(int quality)
        {
            return (uint)(quality switch
            {
                ULTRA_PERFORMANCE => FSR2Quality.UltraPerformance,
                PERFORMANCE => FSR2Quality.Performance,
                BALANCED => FSR2Quality.Balanced,
                _ => FSR2Quality.Quality
            });
        }

        private static uint DlssQuality(int quality)
        {
            return (uint)(quality switch
            {
                ULTRA_PERFORMANCE => DLSSQuality.UltraPerformance,
                PERFORMANCE => DLSSQuality.MaximumPerformance,
                BALANCED => DLSSQuality.Balanced,
                _ => DLSSQuality.MaximumQuality
            });
        }

        internal sealed class CameraState
        {
            private readonly HDAdditionalCameraData _data;
            private readonly Camera _camera;
            private readonly DynamicResUpscaleFilter _filter;
            private readonly bool _allowDynamicResolution;
            private readonly (bool Allow, bool CustomQuality, uint Quality, bool CustomAttributes, bool Optimal) _dlss;
            private readonly (bool Allow, bool CustomQuality, uint Quality, bool CustomAttributes, bool Optimal) _fsr2;

            public CameraState(HDAdditionalCameraData data)
            {
                _data = data;
                _camera = data.GetComponent<Camera>();
                _filter = GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp ? hdrp.currentPlatformRenderPipelineSettings.dynamicResolutionSettings.upsampleFilter : DynamicResUpscaleFilter.CatmullRom;
                _allowDynamicResolution = data.allowDynamicResolution;
                _dlss = (data.allowDeepLearningSuperSampling, data.deepLearningSuperSamplingUseCustomQualitySettings, data.deepLearningSuperSamplingQuality, data.deepLearningSuperSamplingUseCustomAttributes, data.deepLearningSuperSamplingUseOptimalSettings);
                _fsr2 = (data.allowFidelityFX2SuperResolution, data.fidelityFX2SuperResolutionUseCustomQualitySettings, data.fidelityFX2SuperResolutionQuality, data.fidelityFX2SuperResolutionUseCustomAttributes, data.fidelityFX2SuperResolutionUseOptimalSettings);
            }

            public bool Apply(int method, int quality, int existingScale, bool allowPost)
            {
                method = ResolveMethod(method);
                bool temporal = allowPost && method != NATIVE;
                bool taaUpscale = temporal && method == TAAU;
                bool dlss = temporal && method == DLSS;
                bool fsr2 = temporal && method == FSR2;
                _data.allowDynamicResolution = temporal || (method == NATIVE && existingScale < 100);
                _data.allowDeepLearningSuperSampling = dlss;
                _data.deepLearningSuperSamplingUseCustomQualitySettings = dlss || _dlss.CustomQuality;
                _data.deepLearningSuperSamplingQuality = dlss ? DlssQuality(quality) : _dlss.Quality;
                _data.deepLearningSuperSamplingUseCustomAttributes = dlss || _dlss.CustomAttributes;
                _data.deepLearningSuperSamplingUseOptimalSettings = dlss || _dlss.Optimal;
                _data.allowFidelityFX2SuperResolution = fsr2;
                _data.fidelityFX2SuperResolutionUseCustomQualitySettings = fsr2 || _fsr2.CustomQuality;
                _data.fidelityFX2SuperResolutionQuality = fsr2 ? Fsr2Quality(quality) : _fsr2.Quality;
                _data.fidelityFX2SuperResolutionUseCustomAttributes = fsr2 || _fsr2.CustomAttributes;
                _data.fidelityFX2SuperResolutionUseOptimalSettings = fsr2 || _fsr2.Optimal;
                DynamicResUpscaleFilter spatial = _filter == DynamicResUpscaleFilter.TAAU ? DynamicResUpscaleFilter.CatmullRom : _filter;
                if (_camera != null) DynamicResolutionHandler.SetUpscaleFilter(_camera, taaUpscale ? DynamicResUpscaleFilter.TAAU : spatial);
                if (taaUpscale) _data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                else if (temporal) _data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                return temporal;
            }

            public void Restore()
            {
                if (_data == null) return;
                _data.allowDynamicResolution = _allowDynamicResolution;
                _data.allowDeepLearningSuperSampling = _dlss.Allow;
                _data.deepLearningSuperSamplingUseCustomQualitySettings = _dlss.CustomQuality;
                _data.deepLearningSuperSamplingQuality = _dlss.Quality;
                _data.deepLearningSuperSamplingUseCustomAttributes = _dlss.CustomAttributes;
                _data.deepLearningSuperSamplingUseOptimalSettings = _dlss.Optimal;
                _data.allowFidelityFX2SuperResolution = _fsr2.Allow;
                _data.fidelityFX2SuperResolutionUseCustomQualitySettings = _fsr2.CustomQuality;
                _data.fidelityFX2SuperResolutionQuality = _fsr2.Quality;
                _data.fidelityFX2SuperResolutionUseCustomAttributes = _fsr2.CustomAttributes;
                _data.fidelityFX2SuperResolutionUseOptimalSettings = _fsr2.Optimal;
                if (_camera != null) DynamicResolutionHandler.SetUpscaleFilter(_camera, _filter);
            }
        }
    }
}
