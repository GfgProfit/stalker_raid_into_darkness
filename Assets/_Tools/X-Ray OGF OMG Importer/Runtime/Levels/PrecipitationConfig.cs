using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public enum XRayPrecipitationQuality
    {
        Low,
        Medium,
        High
    }

    [Serializable]
    public sealed class XRayPuddleMaskBinding
    {
        [SerializeField] private string _baseTextureName;
        [SerializeField] private Texture2D _mask;

        public string BaseTextureName => _baseTextureName;
        public Texture2D Mask => _mask;
    }

    public sealed class PrecipitationConfig : ScriptableObject
    {
        [Header("Дождь")]
        [SerializeField] private bool _enabled = true;
        [SerializeField, Min(0.0f)] private float _intensityMultiplier = 1.0f;
        [SerializeField] private XRayPrecipitationQuality _quality = XRayPrecipitationQuality.High;
        [SerializeField, Range(1, 20000)] private int _maxDrops = 2500;
        [SerializeField, Min(1.0f)] private float _drawRadius = 15.0f;
        [SerializeField, Min(0.01f)] private float _dropLength = 2.0f;
        [SerializeField, Min(0.001f)] private float _dropHalfWidth = 0.1f;
        [SerializeField, Min(0.0f)] private float _dropSpeed = 0.6f;
        [SerializeField, Min(0.0f)] private float _windResponse = 1.0f;
        [SerializeField, Range(0.0f, 1.0f)] private float _rainOpacity = 0.65f;
        [SerializeField] private bool _splashesEnabled = true;
        [SerializeField, Range(0, 4000)] private int _maxSplashes = 1000;
        [SerializeField, Min(0.01f)] private float _splashSize = 0.2f;
        [SerializeField, Range(1, 8)] private int _maxCameraViews = 4;
        [SerializeField, Min(0.1f)] private float _geometryRefreshSeconds = 2.0f;
        [Header("Ресурсы дождя")]
        [SerializeField] private Shader _rainShader;
        [SerializeField] private Shader _heightShader;
        [SerializeField] private ComputeShader _rainCompute;
        [SerializeField] private Texture2D _rainTexture;
        [SerializeField] private Texture2D _splashTexture;
        [Header("Намокание")]
        [SerializeField] private bool _wetnessEnabled;
        [SerializeField, Min(0.0f)] private float _wettingRate = 0.0156f;
        [SerializeField, Min(0.0f)] private float _dryingRate = 0.0072f;
        [SerializeField, Range(0.0f, 1.0f)] private float _maxWetness = 1.0f;
        [SerializeField, Min(0.0f)] private float _wetnessStrength = 1.0f;
        [Header("Лужи")]
        [SerializeField] private bool _puddlesEnabled;
        [SerializeField, Min(0.0f)] private float _puddleFormationRate = 0.0039f;
        [SerializeField, Min(0.0f)] private float _puddleDryingRate = 0.0018f;
        [SerializeField, Range(0.0f, 1.0f)] private float _maxPuddles = 1.0f;
        [SerializeField, Range(0.0f, 1.0f)] private float _puddleWetnessThreshold = 0.65f;
        [SerializeField, Min(0.0f)] private float _puddleStrength = 1.0f;
        [SerializeField, Min(0.0f)] private float _rippleStrength = 0.025f;
        [SerializeField, Min(0.01f)] private float _puddleMaskScale = 1.1f;
        [SerializeField, Min(0.01f)] private float _shelterTolerance = 0.15f;
        [Header("Ресурсы поверхностей")]
        [SerializeField] private ComputeShader _wetnessCompute;
        [SerializeField] private Texture2D _puddleNoise;
        [SerializeField] private Texture2D _rippleNormal;
        [SerializeField] private XRayPuddleMaskBinding[] _puddleMasks = Array.Empty<XRayPuddleMaskBinding>();

        public bool Enabled => _enabled;
        public float IntensityMultiplier => _intensityMultiplier;
        public XRayPrecipitationQuality Quality => _quality;
        public int MaxDrops => _maxDrops;
        public float DrawRadius => _drawRadius;
        public float DropLength => _dropLength;
        public float DropHalfWidth => _dropHalfWidth;
        public float DropSpeed => _dropSpeed;
        public float WindResponse => _windResponse;
        public float RainOpacity => _rainOpacity;
        public bool SplashesEnabled => _splashesEnabled;
        public int MaxSplashes => _maxSplashes;
        public float SplashSize => _splashSize;
        public int MaxCameraViews => _maxCameraViews;
        public float GeometryRefreshSeconds => _geometryRefreshSeconds;
        public Shader RainShader => _rainShader;
        public Shader HeightShader => _heightShader;
        public ComputeShader RainCompute => _rainCompute;
        public Texture2D RainTexture => _rainTexture;
        public Texture2D SplashTexture => _splashTexture;
        public bool WetnessEnabled => _wetnessEnabled;
        public float WettingRate => _wettingRate;
        public float DryingRate => _dryingRate;
        public float MaxWetness => _maxWetness;
        public float WetnessStrength => _wetnessStrength;
        public bool PuddlesEnabled => _puddlesEnabled;
        public float PuddleFormationRate => _puddleFormationRate;
        public float PuddleDryingRate => _puddleDryingRate;
        public float MaxPuddles => _maxPuddles;
        public float PuddleWetnessThreshold => _puddleWetnessThreshold;
        public float PuddleStrength => _puddleStrength;
        public float RippleStrength => _rippleStrength;
        public float PuddleMaskScale => _puddleMaskScale;
        public float ShelterTolerance => _shelterTolerance;
        public ComputeShader WetnessCompute => _wetnessCompute;
        public Texture2D PuddleNoise => _puddleNoise;
        public Texture2D RippleNormal => _rippleNormal;
        public int AtlasResolution => 256 << (int)_quality;
        public int DropBudget => Mathf.Max(1, _maxDrops >> (2 - (int)_quality));
        public int Revision { get; private set; }

        public Texture2D FindPuddleMask(string baseTextureName)
        {
            if (string.IsNullOrEmpty(baseTextureName)) return null;
            foreach (XRayPuddleMaskBinding binding in _puddleMasks)
            {
                if (binding != null && !string.IsNullOrEmpty(binding.BaseTextureName) && baseTextureName.IndexOf(binding.BaseTextureName, StringComparison.OrdinalIgnoreCase) >= 0) return binding.Mask;
            }
            return null;
        }

        public bool TryValidate(out string diagnostic, bool requireResources = false)
        {
            diagnostic = null;
            if ((int)_quality < 0 || (int)_quality > 2 || _maxDrops < 1 || _maxDrops > 20000 || _maxCameraViews < 1 || _maxCameraViews > 8) diagnostic = "Precipitation quality and resource budgets are invalid.";
            else if (!FiniteNonnegative(_intensityMultiplier) || !FiniteNonnegative(_dropSpeed) || !FiniteNonnegative(_windResponse) || !float.IsFinite(_rainOpacity) || _rainOpacity < 0.0f || _rainOpacity > 1.0f) diagnostic = "Precipitation intensity, speed, wind response and opacity must be finite and nonnegative.";
            else if (!Positive(_drawRadius) || !Positive(_dropLength) || !Positive(_dropHalfWidth) || !Positive(_splashSize) || !Positive(_geometryRefreshSeconds) || !Positive(_shelterTolerance) || _maxSplashes < 0 || _maxSplashes > 4000) diagnostic = "Precipitation geometry and splash settings are invalid.";
            else if ((_enabled || requireResources) && (_rainShader == null || _heightShader == null || _rainCompute == null || _rainTexture == null || _splashTexture == null)) diagnostic = "Precipitation requires prepared rain, splash, height and compute shader resources.";
            else if (!FiniteNonnegative(_wettingRate) || !FiniteNonnegative(_dryingRate) || !FiniteNonnegative(_wetnessStrength) || !FiniteNonnegative(_puddleFormationRate) || !FiniteNonnegative(_puddleDryingRate) || !FiniteNonnegative(_puddleStrength) || !FiniteNonnegative(_rippleStrength) || !Positive(_puddleMaskScale)) diagnostic = "Wetness and puddle rates must be finite and nonnegative.";
            else if (!Unit(_maxWetness) || !Unit(_maxPuddles) || !Unit(_puddleWetnessThreshold)) diagnostic = "Wetness and puddle limits must be between zero and one.";
            else if ((_wetnessEnabled || _puddlesEnabled) && _wetnessCompute == null) diagnostic = "Wetness and puddles require the prepared wetness compute shader.";
            return diagnostic == null;
        }

        private void OnValidate()
        {
            Revision++;
        }

        private static bool FiniteNonnegative(float value)
        {
            return float.IsFinite(value) && value >= 0.0f;
        }

        private static bool Positive(float value)
        {
            return float.IsFinite(value) && value > 0.0f;
        }

        private static bool Unit(float value)
        {
            return float.IsFinite(value) && value >= 0.0f && value <= 1.0f;
        }
    }
}
