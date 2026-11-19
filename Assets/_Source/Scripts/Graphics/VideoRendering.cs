using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.GraphicsOptions
{
    internal sealed class VideoRendering : IDisposable
    {
        public const string VOLUME_LAYER = "Video Options";

        private readonly VolumeProfile _profile;
        private readonly ColorAdjustments _color;
        private readonly HDShadowSettings _shadows;
        private readonly ContactShadows _contactShadows;
        private readonly ScreenSpaceAmbientOcclusion _ao;
        private readonly Fog _fog;
        private readonly MotionBlur _motionBlur;
        private readonly Bloom _bloom;
        private readonly Dictionary<Camera, CameraState> _cameras = new();
        private readonly List<Camera> _removedCameras = new();
        private readonly int _volumeLayerMask;

        private VideoQuality _quality;
        private int _revision;

        public static bool HasHdrp => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset;
        public static bool SupportsRenderScale => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp && hdrp.currentPlatformRenderPipelineSettings.dynamicResolutionSettings.enabled;
        public static bool SupportsContactShadows => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp && hdrp.currentPlatformRenderPipelineSettings.hdShadowInitParams.supportContactShadows;
        public static bool SupportsAmbientOcclusion => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp && hdrp.currentPlatformRenderPipelineSettings.supportSSAO;
        public static bool SupportsFog => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp && hdrp.currentPlatformRenderPipelineSettings.supportVolumetrics;
        public static bool SupportsMotion => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset hdrp && hdrp.currentPlatformRenderPipelineSettings.supportMotionVectors;

        public VideoRendering(GameObject root)
        {
            int layer = LayerMask.NameToLayer(VOLUME_LAYER);
            layer = layer >= 0 ? layer : root.layer;
            _volumeLayerMask = 1 << layer;
            var volumeObject = new GameObject("Video Settings Volume") { layer = layer };
            volumeObject.transform.SetParent(root.transform, false);
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _color = _profile.Add<ColorAdjustments>();
            _shadows = _profile.Add<HDShadowSettings>();
            _contactShadows = _profile.Add<ContactShadows>();
            _contactShadows.directionalOnly.Override(false);
            _ao = _profile.Add<ScreenSpaceAmbientOcclusion>();
            _fog = _profile.Add<Fog>();
            _motionBlur = _profile.Add<MotionBlur>();
            _bloom = _profile.Add<Bloom>();
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10000.0f;
            volume.sharedProfile = _profile;
            RenderPipelineManager.beginContextRendering += PrepareCameras;
            SceneManager.sceneUnloaded += PruneCameras;
            DynamicResolutionHandler.SetDynamicResScaler(GetRenderScale, DynamicResScalePolicyType.ReturnsPercentage);
        }

        public void Apply(VideoQuality quality)
        {
            if (_revision != 0 && _quality.Equals(quality))
            {
                return;
            }
            _quality = quality;
            _revision++;
            _shadows.maxShadowDistance.Override(quality.sunShadowDistance);
            _shadows.cascadeShadowSplitCount.Override(quality.shadowQuality + 2);
            _contactShadows.enable.Override(quality.contactShadows);
            _contactShadows.quality.Override(quality.contactShadowQuality);
            XRayVideoLightShadows.Apply(quality.shadowQuality, SupportsContactShadows ? quality.contactShadows : null);
            _ao.rayTracing.Override(false);
            _ao.intensity.Override(quality.ambientOcclusion > 0 ? 0.7f : 0.0f);
            _ao.quality.Override(Math.Max(0, quality.ambientOcclusion - 1));
            _fog.quality.Override(quality.fogQuality);
            _motionBlur.intensity.Override(quality.motionBlur ? 0.35f : 0.0f);
            _bloom.intensity.Override(quality.bloom ? 0.2f : 0.0f);
        }

        public void SetBrightness(int brightness)
        {
            _color.postExposure.value = Mathf.Log(brightness * 0.01f, 2.0f);
            _color.postExposure.overrideState = brightness != 100;
        }

        public void Dispose()
        {
            SceneManager.sceneUnloaded -= PruneCameras;
            RenderPipelineManager.beginContextRendering -= PrepareCameras;
            XRayVideoLightShadows.Apply(null, null);
            foreach (CameraState state in _cameras.Values)
            {
                state.Restore();
            }
            _cameras.Clear();
            DynamicResolutionHandler.SetDynamicResScaler(FullRenderScale, DynamicResScalePolicyType.ReturnsPercentage);
            foreach (VolumeComponent component in _profile.components)
            {
                UnityEngine.Object.Destroy(component);
            }
            UnityEngine.Object.Destroy(_profile);
        }

        private float GetRenderScale()
        {
            return _quality.renderScale;
        }

        private static float FullRenderScale()
        {
            return 100.0f;
        }

        private void PrepareCameras(ScriptableRenderContext context, List<Camera> cameras)
        {
            foreach (Camera camera in cameras)
            {
                if (camera == null || camera.cameraType != CameraType.Game)
                {
                    continue;
                }
                if (camera.targetTexture != null)
                {
                    if (_cameras.Remove(camera, out CameraState previous))
                    {
                        previous.Restore();
                    }
                    continue;
                }
                if (!_cameras.TryGetValue(camera, out CameraState state) || !state.Valid)
                {
                    if (!camera.TryGetComponent(out HDAdditionalCameraData data))
                    {
                        continue;
                    }
                    state = new CameraState(data);
                    _cameras[camera] = state;
                }
                if (state.Revision != _revision)
                {
                    state.Apply(_quality, _volumeLayerMask, _revision);
                }
            }
        }

        private void PruneCameras(Scene scene)
        {
            _removedCameras.Clear();
            foreach (var entry in _cameras)
            {
                if (entry.Key == null || !entry.Value.Valid)
                {
                    _removedCameras.Add(entry.Key);
                }
            }
            foreach (Camera camera in _removedCameras)
            {
                _cameras.Remove(camera);
            }
            _removedCameras.Clear();
        }

        private sealed class CameraState
        {
            private readonly HDAdditionalCameraData _camera;
            private readonly FrameSettings _settings;
            private readonly FrameSettingsOverrideMask _mask;
            private readonly bool _customSettings;
            private readonly bool _dynamicResolution;
            private readonly LayerMask _volumeMask;
            private readonly HDAdditionalCameraData.AntialiasingMode _antialiasing;
            private readonly HDAdditionalCameraData.SMAAQualityLevel _smaa;
            private readonly HDAdditionalCameraData.TAAQualityLevel _taa;

            public int Revision { get; private set; }
            public bool Valid => _camera != null;

            public CameraState(HDAdditionalCameraData camera)
            {
                _camera = camera;
                _settings = camera.renderingPathCustomFrameSettings;
                _mask = camera.renderingPathCustomFrameSettingsOverrideMask;
                _customSettings = camera.customRenderingSettings;
                _dynamicResolution = camera.allowDynamicResolution;
                _volumeMask = camera.volumeLayerMask;
                _antialiasing = camera.antialiasing;
                _smaa = camera.SMAAQuality;
                _taa = camera.TAAQuality;
            }

            public void Apply(VideoQuality quality, int volumeLayerMask, int revision)
            {
                var settings = _settings;
                FrameSettingsOverrideMask mask = _customSettings ? _mask : default;
                bool allowPost = !mask.mask[(uint)FrameSettingsField.Postprocess] || settings.IsEnabled(FrameSettingsField.Postprocess);
                bool temporal = quality.antialiasing == VideoAntialiasing.Taa;
                _camera.volumeLayerMask = _volumeMask.value | volumeLayerMask;
                _camera.customRenderingSettings = true;
                _camera.allowDynamicResolution = quality.renderScale < 100;
                _camera.antialiasing = quality.antialiasing switch
                {
                    VideoAntialiasing.Fxaa => HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing,
                    VideoAntialiasing.Smaa => HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing,
                    VideoAntialiasing.Taa => HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing,
                    _ => HDAdditionalCameraData.AntialiasingMode.None
                };
                _camera.SMAAQuality = (HDAdditionalCameraData.SMAAQualityLevel)quality.antialiasingQuality;
                _camera.TAAQuality = (HDAdditionalCameraData.TAAQualityLevel)quality.antialiasingQuality;
                Set(ref settings, ref mask, FrameSettingsField.Antialiasing, allowPost && quality.antialiasing != VideoAntialiasing.Off);
                Set(ref settings, ref mask, FrameSettingsField.ContactShadows, quality.contactShadows);
                Set(ref settings, ref mask, FrameSettingsField.SSAO, quality.ambientOcclusion > 0);
                Set(ref settings, ref mask, FrameSettingsField.DepthOfField, false);
                Set(ref settings, ref mask, FrameSettingsField.MotionBlur, allowPost && quality.motionBlur);
                Set(ref settings, ref mask, FrameSettingsField.Bloom, allowPost && quality.bloom);
                Set(ref settings, ref mask, FrameSettingsField.LensFlareDataDriven, allowPost && quality.lensFlare);
                if (!quality.lensFlare)
                {
                    Set(ref settings, ref mask, FrameSettingsField.LensFlareScreenSpace, false);
                }
                if (allowPost && (temporal || quality.motionBlur))
                {
                    Set(ref settings, ref mask, FrameSettingsField.MotionVectors, true);
                }
                settings.lodBias = VideoPresets.LodBias(quality.lodQuality);
                settings.lodBiasMode = LODBiasMode.ScaleQualitySettings;
                mask.mask[(uint)FrameSettingsField.LODBias] = true;
                mask.mask[(uint)FrameSettingsField.LODBiasMode] = true;
                _camera.renderingPathCustomFrameSettings = settings;
                _camera.renderingPathCustomFrameSettingsOverrideMask = mask;
                Revision = revision;
            }

            public void Restore()
            {
                if (!Valid)
                {
                    return;
                }
                _camera.renderingPathCustomFrameSettings = _settings;
                _camera.renderingPathCustomFrameSettingsOverrideMask = _mask;
                _camera.customRenderingSettings = _customSettings;
                _camera.allowDynamicResolution = _dynamicResolution;
                _camera.volumeLayerMask = _volumeMask;
                _camera.antialiasing = _antialiasing;
                _camera.SMAAQuality = _smaa;
                _camera.TAAQuality = _taa;
            }

            private static void Set(ref FrameSettings settings, ref FrameSettingsOverrideMask mask, FrameSettingsField field, bool enabled)
            {
                settings.SetEnabled(field, enabled);
                mask.mask[(uint)field] = true;
            }
        }
    }
}
