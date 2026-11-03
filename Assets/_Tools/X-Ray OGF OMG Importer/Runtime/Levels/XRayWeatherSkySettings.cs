using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Levels
{
    [Serializable]
    public sealed class XRayWeatherSkyShaderParameter : VolumeParameter<Shader>
    {
        public XRayWeatherSkyShaderParameter(Shader value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    [VolumeComponentMenu("Sky/X-Ray Weather Sky")]
    [SupportedOnRenderPipeline(typeof(HDRenderPipelineAsset))]
    [SkyUniqueID(SKY_ID)]
    public sealed class XRayWeatherSkySettings : SkySettings
    {
        public const int SKY_ID = 71007;

        [SerializeField] private TextureParameter _sky0 = new TextureParameter(null, true);
        [SerializeField] private TextureParameter _sky1 = new TextureParameter(null, true);
        [SerializeField] private TextureParameter _sky2 = new TextureParameter(null, true);
        [SerializeField] private TextureParameter _sky3 = new TextureParameter(null, true);
        [SerializeField] private Vector4Parameter _weights = new Vector4Parameter(Vector4.zero, true);
        [SerializeField] private Vector4Parameter _sourceTint = new Vector4Parameter(Vector4.zero, true);
        [SerializeField] private FloatParameter _sourceRotation = new FloatParameter(0.0f, true);
        [SerializeField] private XRayWeatherSkyShaderParameter _shader = new XRayWeatherSkyShaderParameter(null, true);

        public Texture Sky0 => _sky0.value;
        public Texture Sky1 => _sky1.value;
        public Texture Sky2 => _sky2.value;
        public Texture Sky3 => _sky3.value;
        public Vector4 Weights => _weights.value;
        public Vector4 SourceTint => _sourceTint.value;
        public float SourceRotation => _sourceRotation.value;
        public Shader Shader => _shader.value;

        public void SetWeather(XRayWeatherSnapshot snapshot, Shader shader)
        {
            _sky0.Override(snapshot.SkyResources.Texture(0));
            _sky1.Override(snapshot.SkyResources.Texture(1));
            _sky2.Override(snapshot.SkyResources.Texture(2));
            _sky3.Override(snapshot.SkyResources.Texture(3));
            _weights.Override(snapshot.SkyResources.Weights);
            _sourceTint.Override(new Vector4(Pack(snapshot.SkyColor.r), Pack(snapshot.SkyColor.g), Pack(snapshot.SkyColor.b), 1.0f));
            _sourceRotation.Override(snapshot.SkyRotationRadians);
            _shader.Override(shader);
            updateMode.Override(EnvironmentUpdateMode.OnChanged);
        }

        public override Type GetSkyRendererType()
        {
            return typeof(XRayWeatherSkyPass);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = base.GetHashCode();
                hash = hash * 23 + _sky0.GetHashCode();
                hash = hash * 23 + _sky1.GetHashCode();
                hash = hash * 23 + _sky2.GetHashCode();
                hash = hash * 23 + _sky3.GetHashCode();
                hash = hash * 23 + _weights.GetHashCode();
                hash = hash * 23 + _sourceTint.GetHashCode();
                hash = hash * 23 + _sourceRotation.GetHashCode();
                hash = hash * 23 + _shader.GetHashCode();
                return hash;
            }
        }

        private static float Pack(float value)
        {
            return (Mathf.FloorToInt(value * 255.0f) & 255) / 255.0f;
        }
    }
}
