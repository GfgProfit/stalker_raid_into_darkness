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

        [SerializeField] private BoolParameter _useHdrpIntensity = new BoolParameter(false, true);
        [SerializeField] private ColorParameter _nightAmbient = new ColorParameter(Color.black, true, true, false);
        [SerializeField] private FloatParameter _nightWeight = new FloatParameter(0.0f, true);
        [SerializeField] private MinFloatParameter _nightDiffuseMultiplier = new MinFloatParameter(1.0f, 0.0f, true);
        [SerializeField] private Vector4Parameter _nightTransitionHours = new Vector4Parameter(new Vector4(4.0f, 7.0f, 19.0f, 22.0f), true);
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
        public bool UseHdrpIntensity => _useHdrpIntensity.value;
        public Color NightAmbient => _nightAmbient.value;
        public float NightWeight => _nightWeight.value;
        public float NightDiffuseMultiplier => _nightDiffuseMultiplier.value;
        public Vector4 NightTransitionHours => _nightTransitionHours.value;

        public void SetHdrpIntensity(bool value)
        {
            _useHdrpIntensity.Override(value);
        }

        public void SetNightAmbient(Color linearRadiance)
        {
            _nightAmbient.Override(linearRadiance);
        }

        public void SetNightDiffuseMultiplier(float value)
        {
            _nightDiffuseMultiplier.Override(value);
        }

        public void SetNightTransitionHours(Vector4 value)
        {
            for (int i = 0; i < 4; i++)
            {
                if (float.IsNaN(value[i]) || float.IsInfinity(value[i])) throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (value.x < 0.0f || value.x >= value.y || value.y > value.z || value.z >= value.w || value.w > 24.0f) throw new ArgumentOutOfRangeException(nameof(value));
            _nightTransitionHours.Override(value);
        }

        public void SetWeather(XRayWeatherSnapshot snapshot, Shader shader)
        {
            float hour = (float)(((snapshot.GameSeconds % 86400.0 + 86400.0) % 86400.0) / 3600.0);
            Vector4 hours = _nightTransitionHours.value;
            float dawn = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(hours.x, hours.y, hour));
            float dusk = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(hours.z, hours.w, hour));
            _nightWeight.Override(1.0f - dawn + dusk);
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
                hash = hash * 23 + _useHdrpIntensity.GetHashCode();
                hash = hash * 23 + _nightAmbient.GetHashCode();
                hash = hash * 23 + _nightWeight.GetHashCode();
                hash = hash * 23 + _nightDiffuseMultiplier.GetHashCode();
                hash = hash * 23 + _nightTransitionHours.GetHashCode();
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
