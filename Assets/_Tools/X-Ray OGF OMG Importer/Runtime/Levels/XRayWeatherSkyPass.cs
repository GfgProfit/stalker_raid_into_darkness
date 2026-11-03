using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Levels
{
    public sealed class XRayWeatherSkyPass : SkyRenderer
    {
        private static readonly int _sky0Id = Shader.PropertyToID("_XRaySky0");
        private static readonly int _sky1Id = Shader.PropertyToID("_XRaySky1");
        private static readonly int _sky2Id = Shader.PropertyToID("_XRaySky2");
        private static readonly int _sky3Id = Shader.PropertyToID("_XRaySky3");
        private static readonly int _weightsId = Shader.PropertyToID("_XRaySkyWeights");
        private static readonly int _tintId = Shader.PropertyToID("_XRaySkyTint");
        private static readonly int _rotationId = Shader.PropertyToID("_XRaySkyRotation");
        private static readonly int _intensityId = Shader.PropertyToID("_XRaySkyIntensity");
        private static readonly int _nightAmbientId = Shader.PropertyToID("_XRayNightAmbient");
        private static readonly int _nightWeightId = Shader.PropertyToID("_XRayNightWeight");
        private static readonly int _viewId = Shader.PropertyToID("_PixelCoordToViewDirWS");

        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
        private Material _material;
        private Cubemap _black;

        public XRayWeatherSkyPass()
        {
            SupportDynamicSunLight = false;
        }

        public static Vector3 GetNightRadiance(XRayWeatherSkySettings settings)
        {
            if (settings == null) return Vector3.zero;
            Color ambient = settings.NightAmbient;
            float intensity = settings.NightWeight * settings.NightDiffuseMultiplier * (settings.UseHdrpIntensity ? GetSkyIntensity(settings, null) : 1.0f);
            return new Vector3(ambient.r * intensity, ambient.g * intensity, ambient.b * intensity);
        }

        public override void Build()
        {
            _black = new Cubemap(1, TextureFormat.RGBA32, false);
            _black.name = "XRayWeatherEmptySky";
            _black.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < 6; i++) _black.SetPixel((CubemapFace)i, 0, 0, Color.black);
            _black.Apply(false, true);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(_material);
            CoreUtils.Destroy(_black);
            _material = null;
            _black = null;
        }

        public override void RenderSky(BuiltinSkyParameters builtinParams, bool renderForCubemap, bool renderSunDisk)
        {
            var settings = builtinParams.skySettings as XRayWeatherSkySettings;
            if (settings == null || settings.Shader == null) return;
            if (_material == null || _material.shader != settings.Shader)
            {
                CoreUtils.Destroy(_material);
                _material = CoreUtils.CreateEngineMaterial(settings.Shader);
            }
            _properties.SetTexture(_sky0Id, settings.Sky0 != null ? settings.Sky0 : _black);
            _properties.SetTexture(_sky1Id, settings.Sky1 != null ? settings.Sky1 : _black);
            _properties.SetTexture(_sky2Id, settings.Sky2 != null ? settings.Sky2 : _black);
            _properties.SetTexture(_sky3Id, settings.Sky3 != null ? settings.Sky3 : _black);
            _properties.SetVector(_weightsId, settings.Weights);
            _properties.SetVector(_tintId, settings.SourceTint);
            _properties.SetVector(_rotationId, new Vector4(Mathf.Cos(settings.SourceRotation), Mathf.Sin(settings.SourceRotation), 0.0f, 0.0f));
            _properties.SetFloat(_intensityId, settings.UseHdrpIntensity ? GetSkyIntensity(settings, builtinParams.debugSettings) : 1.0f);
            _properties.SetVector(_nightAmbientId, settings.NightAmbient);
            _properties.SetFloat(_nightWeightId, settings.NightWeight);
            _properties.SetMatrix(_viewId, builtinParams.pixelCoordToViewDirMatrix);
            CoreUtils.DrawFullScreen(builtinParams.commandBuffer, _material, _properties, renderForCubemap ? 0 : 1);
        }
    }
}
