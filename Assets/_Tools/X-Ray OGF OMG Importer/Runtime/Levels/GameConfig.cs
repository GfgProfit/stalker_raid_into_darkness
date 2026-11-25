using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public sealed class GameConfig : ScriptableObject
    {
        public const float MAX_FX_LIGHTING_ADAPTATION = 4.0f;
        public const float DEFAULT_PLAYER_STAT_CAPACITY = 100.0f;
        public const float DEFAULT_GRASS_SHADOW_DISTANCE = 25.0f;
        public const float DEFAULT_GRASS_SHADOW_FADE_DISTANCE = 7.0f;
        public const float DEFAULT_GRASS_THINNING_START_DISTANCE = 36.0f;
        public const float DEFAULT_HIP_FIRE_DISPERSION_MULTIPLIER = 3f;

        private const string RESOURCE_PATH = "GameConfig";

        [SerializeField, Min(0.0f)] private float _defaultTimeScale = 7.0f;
        [SerializeField, Range(0.0f, 24.0f)] private float _defaultTimeOfDay = 12.0f;
        [SerializeField] private XRayWeatherProfile _defaultWeather;
        [SerializeField] private XRayWeatherCatalog _ordinaryWeatherCatalog;
        [SerializeField] private XRayWeatherFxCatalog _weatherFxCatalog;
        [SerializeField] private XRayWeatherCelestialSettings _celestialSettings;
        [SerializeField] private PrecipitationConfig _precipitation;
        [SerializeField] private bool _instanceSubmissionGroupingEnabled;
        [SerializeField] private bool _instanceNativeSubmissionEnabled;
        [SerializeField] private bool _vegetationOcclusionEnabled = true;
        [SerializeField] private bool _vegetationTreeImpostorsEnabled = true;
        [SerializeField, Min(0.0f)] private float _grassShadowDistance = DEFAULT_GRASS_SHADOW_DISTANCE;
        [SerializeField, Min(0.0f)] private float _grassShadowFadeDistance = DEFAULT_GRASS_SHADOW_FADE_DISTANCE;
        [SerializeField, Min(0.0f)] private float _grassThinningStartDistance = DEFAULT_GRASS_THINNING_START_DISTANCE;
        [SerializeField] private bool _punctualShadowDistanceEnabled = true;
        [SerializeField, Min(0.01f)] private float _punctualShadowDistance = 75.0f;
        [SerializeField] private bool _fogEnabled = true;
        [SerializeField] private bool _sunShaftsEnabled = true;
        [SerializeField] private bool _lensFlareEnabled = true;
        [SerializeField] private bool _expressiveDaylight = true;
        [SerializeField, Min(0.0f)] private float _lensFlareIntensity = 1.0f;
        [SerializeField] private bool _adaptLensFlareToWeather = true;
        [SerializeField, Min(0.0f)] private float _expressiveFlareIntensity = 1.0f;
        [SerializeField, Range(0.0f, MAX_FX_LIGHTING_ADAPTATION)] private float _fxLightingAdaptation = 1.0f;
        [SerializeField, Min(0.0001f)] private float _defaultPlayerStatCapacity = DEFAULT_PLAYER_STAT_CAPACITY;
        [SerializeField] private AnomalyBalanceConfig _anomalyBalance;
        [SerializeField] private WeaponCrosshairSettings _weaponCrosshair = new WeaponCrosshairSettings();
        [SerializeField, Min(1f)] private float _hipFireDispersionMultiplier = DEFAULT_HIP_FIRE_DISPERSION_MULTIPLIER;

        public float DefaultTimeScale => _defaultTimeScale;
        public float DefaultTimeOfDay => _defaultTimeOfDay;
        public XRayWeatherProfile DefaultWeather => _defaultWeather;
        public XRayWeatherCatalog OrdinaryWeatherCatalog => _ordinaryWeatherCatalog;
        public XRayWeatherFxCatalog WeatherFxCatalog => _weatherFxCatalog;
        public XRayWeatherCelestialSettings CelestialSettings => _celestialSettings;
        public PrecipitationConfig Precipitation => _precipitation;
        public bool InstanceSubmissionGroupingEnabled => _instanceSubmissionGroupingEnabled;
        public bool InstanceNativeSubmissionEnabled => _instanceNativeSubmissionEnabled;
        public bool VegetationOcclusionEnabled => _vegetationOcclusionEnabled;
        public bool VegetationTreeImpostorsEnabled => _vegetationTreeImpostorsEnabled;
        public float GrassShadowDistance => _grassShadowDistance;
        public float GrassShadowFadeDistance => _grassShadowFadeDistance;
        public float GrassThinningStartDistance => _grassThinningStartDistance;
        public bool PunctualShadowDistanceEnabled => _punctualShadowDistanceEnabled;
        public float PunctualShadowDistance => _punctualShadowDistance;
        public bool FogEnabled => _fogEnabled;
        public bool SunShaftsEnabled => _sunShaftsEnabled;
        public bool LensFlareEnabled => _lensFlareEnabled;
        public bool ExpressiveDaylight => _expressiveDaylight;
        public float LensFlareIntensity => _lensFlareIntensity;
        public bool AdaptLensFlareToWeather => _adaptLensFlareToWeather;
        public float ExpressiveFlareIntensity => _expressiveFlareIntensity;
        public float FxLightingAdaptation => _fxLightingAdaptation;
        public float DefaultPlayerStatCapacity => _defaultPlayerStatCapacity;
        public AnomalyBalanceConfig AnomalyBalance => _anomalyBalance;
        public WeaponCrosshairSettings WeaponCrosshair => _weaponCrosshair;
        public float HipFireDispersionMultiplier => _hipFireDispersionMultiplier;
        public int Revision { get; private set; }

        public event Action Changed;

        public static GameConfig Load()
        {
            return Resources.Load<GameConfig>(RESOURCE_PATH);
        }

        public bool TryValidate(out string diagnostic)
        {
            diagnostic = null;
            if (!float.IsFinite(_defaultTimeScale) || _defaultTimeScale < 0.0f) diagnostic = "GameConfig DefaultTimeScale must be finite and non-negative.";
            else if (!float.IsFinite(_defaultTimeOfDay) || _defaultTimeOfDay < 0.0f || _defaultTimeOfDay > 24.0f) diagnostic = "GameConfig DefaultTimeOfDay must be between 0 and 24 hours.";
            else if (_ordinaryWeatherCatalog == null || _ordinaryWeatherCatalog.Profiles == null || _ordinaryWeatherCatalog.Profiles.Count == 0) diagnostic = "GameConfig requires an ordinary weather catalog with profiles.";
            else if (!_ordinaryWeatherCatalog.Contains(_defaultWeather)) diagnostic = "GameConfig DefaultWeather must belong to its ordinary weather catalog.";
            else if (!_defaultWeather.Validate(out string weatherDiagnostic)) diagnostic = "GameConfig DefaultWeather is invalid. " + weatherDiagnostic;
            else if (_weatherFxCatalog == null || _weatherFxCatalog.Effects == null || _weatherFxCatalog.Effects.Count == 0) diagnostic = "GameConfig requires a WeatherFX catalog with effects.";
            else if (_weatherFxCatalog.OrdinaryCatalog != _ordinaryWeatherCatalog) diagnostic = "GameConfig WeatherFX and ordinary weather catalogs must reference the same ordinary catalog.";
            else if (_celestialSettings == null || _celestialSettings.Shader == null) diagnostic = "GameConfig requires celestial settings with a shader.";
            else if (_precipitation != null && !_precipitation.TryValidate(out string precipitationDiagnostic)) diagnostic = precipitationDiagnostic;
            else if (!float.IsFinite(_grassShadowDistance) || _grassShadowDistance < 0.0f) diagnostic = "GameConfig GrassShadowDistance must be finite and non-negative.";
            else if (!float.IsFinite(_grassShadowFadeDistance) || _grassShadowFadeDistance < 0.0f) diagnostic = "GameConfig GrassShadowFadeDistance must be finite and non-negative.";
            else if (!float.IsFinite(_grassThinningStartDistance) || _grassThinningStartDistance < 0.0f) diagnostic = "GameConfig GrassThinningStartDistance must be finite and non-negative.";
            else if (!float.IsFinite(_punctualShadowDistance) || _punctualShadowDistance <= 0.0f) diagnostic = "GameConfig PunctualShadowDistance must be finite and positive.";
            else if (!float.IsFinite(_lensFlareIntensity) || _lensFlareIntensity < 0.0f) diagnostic = "GameConfig LensFlareIntensity must be finite and non-negative.";
            else if (!float.IsFinite(_expressiveFlareIntensity) || _expressiveFlareIntensity < 0.0f) diagnostic = "GameConfig ExpressiveFlareIntensity must be finite and non-negative.";
            else if (!float.IsFinite(_fxLightingAdaptation) || _fxLightingAdaptation < 0.0f || _fxLightingAdaptation > MAX_FX_LIGHTING_ADAPTATION) diagnostic = "GameConfig FxLightingAdaptation must be within its supported range.";
            else if (!float.IsFinite(_defaultPlayerStatCapacity) || _defaultPlayerStatCapacity <= 0.0f) diagnostic = "GameConfig DefaultPlayerStatCapacity must be finite and positive.";
            else if (!float.IsFinite(_hipFireDispersionMultiplier) || _hipFireDispersionMultiplier < 1f) diagnostic = "GameConfig HipFireDispersionMultiplier must be finite and at least 1.";
            else if (_anomalyBalance != null && !_anomalyBalance.TryValidate(out string anomalyDiagnostic)) diagnostic = anomalyDiagnostic;
            return diagnostic == null;
        }

        private void OnValidate()
        {
            Revision++;
            Changed?.Invoke();
        }
    }
}
