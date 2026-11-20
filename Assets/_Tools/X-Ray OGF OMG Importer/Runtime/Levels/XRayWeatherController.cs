using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class XRayWeatherController : MonoBehaviour
    {
        private const double DAY_SECONDS = 86400.0;
        private const float SHAFTS_DAY_GAIN = 26.0f;
        private const float SHAFTS_NIGHT_GAIN = 64.0f;
        private const double FX_BLEND_REAL_SECONDS = 5.0;
        private const float FX_FLARE_TRANSMISSION = 0.35f;
        private const float FX_DARK_SURFACE_GAIN = 128.0f;
        private const float FX_PSI_NIGHT_GAIN = 64.0f;
        private const float FX_DAY_ART_RADIANCE_LIMIT = 0.30f;
        private const float FX_NIGHT_ART_RADIANCE_LIMIT = 0.16f;
        private const float FX_DIRECT_LIGHT_ART_CUTOFF = 0.15f;

        private static readonly List<XRayWeatherController> _controllers = new List<XRayWeatherController>();

        [SerializeField] private XRayWeatherLevelOverrides _levelOverrides;
        [SerializeField] private XRayWeatherProfile _profile;
        [SerializeField] private XRayWeatherCatalog _catalog;
        [SerializeField] private XRayWeatherFxCatalog _fxCatalog;
        [SerializeField, Range(0.0f, 24.0f)] private float _timeOfDayHours = 12.0f;
        [SerializeField, Min(0.0f)] private float _timeScale = 1.0f;
        [SerializeField] private bool _paused;
        [SerializeField] private MonolithWeatherInputs _weatherInputs;
        [SerializeField] private MonolithSourceLighting _sourceLighting;
        [SerializeField] private XRayFloraWindProvider _windProvider;
        [SerializeField] private Light _sun;
        [SerializeField] private XRayWeatherSkyRenderer _skyRenderer;
        [SerializeField] private XRayWeatherFogRenderer _fogRenderer;
        [SerializeField] private XRayWeatherCelestialSettings _celestialSettings;
        [SerializeField] private PrecipitationConfig _precipitationConfig;
        [SerializeField] private bool _fogEnabled = true;
        [SerializeField] private bool _sunShaftsEnabled = true;
        [SerializeField] private bool _lensFlareEnabled = true;
        [SerializeField] private bool _expressiveDaylight = true;
        [SerializeField, Min(0.0f)] private float _lensFlareIntensity = 1.0f;
        [SerializeField] private bool _adaptLensFlareToWeather = true;
        [SerializeField, Min(0.0f)] private float _expressiveFlareIntensity = 1.0f;
        [SerializeField] private XRayWeatherFlareProfileOverride[] _flareProfileOverrides = Array.Empty<XRayWeatherFlareProfileOverride>();
        [SerializeField, Range(0.0f, GameConfig.MAX_FX_LIGHTING_ADAPTATION)] private float _fxLightingAdaptation = 1.0f;

        private GameConfig _configuration;
        private int _configurationRevision;
        private string _configurationDiagnostic;
        private XRayWeatherCatalog _effectiveCatalog;
        private XRayWeatherFxCatalog _effectiveFxCatalog;
        private XRayWeatherProfile _activeProfile;
        private XRayWeatherProfile _appliedProfile;
        private XRayWeatherProfile _transitionFrom;
        private XRayWeatherProfile _transitionTarget;
        private XRayWeatherFxPlayback _fxPlayback;
        private XRayWeatherSnapshot _snapshot;
        private Vector2 _flareVisibility = Vector2.one;
        private MonolithWeatherInputs _ownedWeather;
        private MonolithSourceLighting _ownedLighting;
        private XRayFloraWindProvider _ownedWind;
        private Light _ownedSun;
        private HDAdditionalLightData _ownedSunData;
        private XRayWeatherSkyRenderer _ownedSky;
        private XRayWeatherFogRenderer _ownedFog;
        private XRayWeatherCelestialSettings _appliedCelestialSettings;
        private XRayWeatherCelestialRenderer _celestial;
        private XRayPrecipitationSystem _precipitation;
        private Color _savedSunColor;
        private float _savedSunIntensity;
        private Quaternion _savedSunRotation;
        private float _savedVolumetricDimmer;
        private float _savedVolumetricShadowDimmer;
        private double _gameTimeSeconds;
        private float _simulationTimeScale;
        private float? _timeScaleOverride;
        private float _transitionDuration;
        private float _transitionElapsed;
        private int _lastFrame = -1;
        private bool _simulationPaused;
        private bool _appliedPaused;
        private bool _sessionWeatherSelected;
        private bool _settingsApplied;
        private bool _settingsValid;
        private bool _settingsDirty;
        private bool _previewActive;
        private bool _ownsInputs;
        private bool _rebaseWind;
        private bool _ownsSky;
        private bool _ownsFog;
        private bool _ownsShaftsLight;
        private bool _savedAffectsVolumetric;
        private bool _celestialResolved;

        public static XRayWeatherController Active { get; private set; }

        public GameConfig Configuration => _configuration;
        public XRayWeatherLevelOverrides LevelOverrides => _levelOverrides;
        public XRayWeatherProfile AuthoredProfile => Inherits(XRayWeatherLevelOverrides.WeatherProfile) ? _configuration.DefaultWeather : _profile;
        public XRayWeatherCatalog Catalog => _effectiveCatalog;
        public XRayWeatherFxCatalog FxCatalog => _effectiveFxCatalog;
        public XRayWeatherFxProfile ActiveFx => _fxPlayback?.Effect;
        public double FxTimeSeconds => _fxPlayback?.ElapsedSeconds ?? 0.0;
        public double FxDurationSeconds => _fxPlayback?.DurationSeconds ?? 0.0;
        public double FxEntrySeconds => _fxPlayback?.EntrySeconds ?? 0.0;
        public XRayWeatherFxPhase FxPhase => _fxPlayback?.Phase ?? XRayWeatherFxPhase.None;
        public XRayWeatherProfile ActiveProfile => _transitionTarget != null ? _transitionTarget : _activeProfile;
        public XRayWeatherSnapshot Snapshot => _snapshot;
        public Vector3 SourceLightColor { get; private set; }
        public string Diagnostic { get; private set; }
        public double GameTimeSeconds => _gameTimeSeconds;
        public bool Paused => _simulationPaused;
        public float TimeScale => _simulationTimeScale;
        public float? TimeScaleOverride => _timeScaleOverride;
        public float ConfiguredTimeScale => Inherits(XRayWeatherLevelOverrides.TimeScale) ? _configuration.DefaultTimeScale : _timeScale;
        public float ConfiguredTimeOfDay => Inherits(XRayWeatherLevelOverrides.TimeOfDay) ? _configuration.DefaultTimeOfDay : _timeOfDayHours;
        public bool PreviewActive => _previewActive;
        public bool IsTransitioning => _transitionTarget != null;
        public float TransitionProgress => IsTransitioning ? Mathf.Clamp01(_transitionElapsed / _transitionDuration) : 1.0f;
        public XRayWeatherCelestialRenderer Celestial => _celestial;
        public XRayPrecipitationSystem Precipitation => _precipitation;
        public PrecipitationConfig EffectivePrecipitationConfig => Inherits(XRayWeatherLevelOverrides.Precipitation) ? _configuration.Precipitation : _precipitationConfig;
        public bool? PrecipitationOverride { get; private set; }
        public bool? RainVisualOverride { get; private set; }
        public bool PrecipitationEnabled => PrecipitationOverride ?? (EffectivePrecipitationConfig != null && EffectivePrecipitationConfig.Enabled);
        public bool? FogOverride { get; private set; }
        public bool? SunShaftsOverride { get; private set; }
        public bool? LensFlareOverride { get; private set; }
        public float? LensFlareIntensityOverride { get; private set; }
        public bool? FlareAdaptationOverride { get; private set; }
        public float? ExpressiveFlareIntensityOverride { get; private set; }
        public float? FxLightingAdaptationOverride { get; private set; }
        public float FxLightingAdaptationGain => FxLightingAdaptationOverride ?? (Inherits(XRayWeatherLevelOverrides.FxLightingAdaptation) ? _configuration.FxLightingAdaptation : IsFinite(_fxLightingAdaptation) ? Mathf.Clamp(_fxLightingAdaptation, 0.0f, GameConfig.MAX_FX_LIGHTING_ADAPTATION) : 1.0f);
        public bool FogEnabled => FogOverride ?? (Inherits(XRayWeatherLevelOverrides.Fog) ? _configuration.FogEnabled : _fogEnabled);
        public bool SunShaftsEnabled => SunShaftsOverride ?? (Inherits(XRayWeatherLevelOverrides.SunShafts) ? _configuration.SunShaftsEnabled : _sunShaftsEnabled);
        public bool LensFlareEnabled => LensFlareOverride ?? XRayVideoOverrides.LensFlare ?? (Inherits(XRayWeatherLevelOverrides.LensFlare) ? _configuration.LensFlareEnabled : _lensFlareEnabled);
        public bool ExpressiveDaylight => Inherits(XRayWeatherLevelOverrides.ExpressiveDaylight) ? _configuration.ExpressiveDaylight : _expressiveDaylight;
        public float LensFlareIntensity => LensFlareIntensityOverride ?? (Inherits(XRayWeatherLevelOverrides.LensFlareIntensity) ? _configuration.LensFlareIntensity : _lensFlareIntensity);
        public bool AdaptLensFlareToWeather => FlareAdaptationOverride ?? (Inherits(XRayWeatherLevelOverrides.FlareAdaptation) ? _configuration.AdaptLensFlareToWeather : _adaptLensFlareToWeather);
        public float ExpressiveFlareIntensity => ExpressiveFlareIntensityOverride ?? (Inherits(XRayWeatherLevelOverrides.ExpressiveFlareIntensity) ? _configuration.ExpressiveFlareIntensity : _expressiveFlareIntensity);

        public void Configure(XRayWeatherProfile profile, MonolithWeatherInputs weatherInputs, MonolithSourceLighting sourceLighting, XRayFloraWindProvider windProvider, Light sun, bool overrideInitialWeather = true)
        {
            ReleaseInputs();
            _profile = profile;
            if (overrideInitialWeather) _levelOverrides |= XRayWeatherLevelOverrides.WeatherProfile;
            else _levelOverrides &= ~XRayWeatherLevelOverrides.WeatherProfile;
            _weatherInputs = weatherInputs;
            _sourceLighting = sourceLighting;
            _windProvider = windProvider;
            _sun = sun;
            _settingsApplied = false;
            ApplySettings();
        }

        public void ConfigureCatalog(XRayWeatherCatalog catalog)
        {
            if (catalog != null && !catalog.CanAttachTo(AuthoredProfile)) throw new ArgumentException("The weather catalog must match the initial profile's source context or contain an identical cycle with the same settings, sun positions and serialized resources.", nameof(catalog));
            _catalog = catalog;
            _levelOverrides |= XRayWeatherLevelOverrides.WeatherCatalog;
            _effectiveCatalog = catalog;
        }

        public void ConfigureFxCatalog(XRayWeatherFxCatalog catalog)
        {
            if (_fxPlayback != null && _effectiveFxCatalog != catalog) throw new InvalidOperationException("Stop the weather effect before replacing its catalog.");
            if (catalog != null && !catalog.CanAttachTo(AuthoredProfile)) throw new ArgumentException("The weather effect catalog must match the initial ordinary weather source.", nameof(catalog));
            _fxCatalog = catalog;
            _levelOverrides |= XRayWeatherLevelOverrides.WeatherFxCatalog;
            _effectiveFxCatalog = catalog;
        }

        public void ApplySettings()
        {
            ApplyConfiguration(GameConfig.Load());
        }

        private void ApplyConfiguration(GameConfig configuration)
        {
            _settingsDirty = false;
            _configuration = configuration;
            _configurationRevision = configuration != null ? configuration.Revision : 0;
            string diagnostic = "Missing GameConfig. Include the canonical Resources/GameConfig.asset in the project.";
            if (configuration == null || !configuration.TryValidate(out diagnostic))
            {
                _settingsValid = false;
                _effectiveCatalog = null;
                _effectiveFxCatalog = null;
                Diagnostic = diagnostic;
                if (_configurationDiagnostic != diagnostic) Debug.LogError(diagnostic, this);
                _configurationDiagnostic = diagnostic;
                ReleaseInputs();
                return;
            }
            _configurationDiagnostic = null;
            float timeOfDay = ConfiguredTimeOfDay;
            float timeScale = ConfiguredTimeScale;
            if (!IsFinite(timeOfDay) || !IsFinite(timeScale) || timeScale < 0.0f)
            {
                _settingsValid = false;
                Diagnostic = "Weather time and scale must be finite; scale must be nonnegative.";
                ReleaseInputs();
                return;
            }
            _settingsValid = true;
            XRayWeatherProfile profile = AuthoredProfile;
            bool matchesGlobalCatalog = Inherits(XRayWeatherLevelOverrides.WeatherProfile) || configuration.OrdinaryWeatherCatalog.CanAttachTo(profile);
            _effectiveCatalog = Inherits(XRayWeatherLevelOverrides.WeatherCatalog) ? (matchesGlobalCatalog ? configuration.OrdinaryWeatherCatalog : null) : _catalog;
            _effectiveFxCatalog = Inherits(XRayWeatherLevelOverrides.WeatherFxCatalog) ? (matchesGlobalCatalog ? configuration.WeatherFxCatalog : null) : _fxCatalog;
            bool discontinuity = !_settingsApplied;
            if (!_settingsApplied)
            {
                _gameTimeSeconds = WrapTime(timeOfDay * 3600.0);
                _timeScaleOverride = null;
                _sessionWeatherSelected = false;
                _fxPlayback = null;
            }
            if (!_settingsApplied || _appliedProfile != profile && !_sessionWeatherSelected && _fxPlayback == null && !IsTransitioning)
            {
                _activeProfile = profile;
                ClearTransition();
                discontinuity = true;
            }
            if (!_settingsApplied || _appliedPaused != _paused)
            {
                discontinuity |= _simulationPaused != _paused;
                _simulationPaused = _paused;
            }
            _simulationTimeScale = _timeScaleOverride ?? timeScale;
            _appliedPaused = _paused;
            _appliedProfile = profile;
            _settingsApplied = true;
            XRayWeatherCelestialSettings celestialSettings = Inherits(XRayWeatherLevelOverrides.CelestialSettings) ? configuration.CelestialSettings : _celestialSettings;
            if (_appliedCelestialSettings != celestialSettings) ReleaseCelestial();
            _appliedCelestialSettings = celestialSettings;
            if (_celestial == null) _celestialResolved = false;
            if (_ownsInputs && (_ownedWeather != _weatherInputs || _ownedLighting != _sourceLighting || _ownedWind != _windProvider || _ownedSun != _sun || _ownedSky != _skyRenderer || _ownedFog != _fogRenderer)) ReleaseInputs();
            if (Application.IsPlaying(gameObject) || _previewActive)
            {
                if (TryOwnInputs()) EvaluateAndPublish(0.0f, discontinuity);
            }
        }

        private bool Inherits(XRayWeatherLevelOverrides setting)
        {
            return _configuration != null && (_levelOverrides & setting) == 0;
        }

        public void SetProfile(XRayWeatherProfile profile)
        {
            if (_fxPlayback != null)
            {
                Diagnostic = "Stop the weather effect before selecting another ordinary cycle.";
                return;
            }
            _activeProfile = profile;
            _sessionWeatherSelected = true;
            ClearTransition();
            RefreshDiscontinuity();
        }

        public void ConfigureSky(XRayWeatherSkyRenderer skyRenderer)
        {
            ReleaseInputs();
            _skyRenderer = skyRenderer;
            RefreshDiscontinuity();
        }

        public void ConfigureFog(XRayWeatherFogRenderer fogRenderer)
        {
            ReleaseInputs();
            _fogRenderer = fogRenderer;
            RefreshDiscontinuity();
        }

        public void Seek(double gameSeconds)
        {
            if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds)) throw new ArgumentOutOfRangeException(nameof(gameSeconds));
            _gameTimeSeconds = WrapTime(gameSeconds);
            if (_fxPlayback != null) _fxPlayback.Rebase(_gameTimeSeconds - _fxPlayback.ElapsedSeconds);
            RefreshDiscontinuity();
        }

        public void SetPaused(bool paused)
        {
            if (_simulationPaused == paused) return;
            _simulationPaused = paused;
            RefreshDiscontinuity();
        }

        public void SetTimeScale(float timeScale)
        {
            if (!IsFinite(timeScale) || timeScale < 0.0f) throw new ArgumentOutOfRangeException(nameof(timeScale));
            _timeScaleOverride = timeScale;
            _simulationTimeScale = timeScale;
        }

        public void ClearTimeScaleOverride()
        {
            _timeScaleOverride = null;
            _simulationTimeScale = ConfiguredTimeScale;
        }

        public void SetFogOverride(bool? enabled)
        {
            if (FogOverride == enabled) return;
            FogOverride = enabled;
            if (_ownsInputs && Active == this) EvaluateAndPublish(0.0f, false);
        }

        public void SetPrecipitationOverride(bool? enabled)
        {
            PrecipitationOverride = enabled;
            RefreshDiscontinuity(false);
        }

        public void SetRainVisualOverride(bool? enabled)
        {
            RainVisualOverride = enabled;
            RefreshDiscontinuity(false);
        }

        public void SetCelestialEffectOverrides(bool? shafts, bool? flare)
        {
            bool previousFlare = LensFlareEnabled;
            SunShaftsOverride = shafts;
            LensFlareOverride = flare;
            if (_ownsInputs && Active == this) PublishCelestial(0.0f, previousFlare != LensFlareEnabled);
        }

        public void SetFlareAppearanceOverrides(float? intensity, bool? adaptation, float? expressiveIntensity)
        {
            if (intensity.HasValue && (!IsFinite(intensity.Value) || intensity.Value < 0.0f)) throw new ArgumentOutOfRangeException(nameof(intensity));
            if (expressiveIntensity.HasValue && (!IsFinite(expressiveIntensity.Value) || expressiveIntensity.Value < 0.0f)) throw new ArgumentOutOfRangeException(nameof(expressiveIntensity));
            LensFlareIntensityOverride = intensity;
            FlareAdaptationOverride = adaptation;
            ExpressiveFlareIntensityOverride = expressiveIntensity;
            if (_ownsInputs && Active == this) PublishCelestial(0.0f, false);
        }

        public void SetFxLightingAdaptationOverride(float? gain)
        {
            if (gain.HasValue && (!IsFinite(gain.Value) || gain.Value < 0.0f || gain.Value > GameConfig.MAX_FX_LIGHTING_ADAPTATION)) throw new ArgumentOutOfRangeException(nameof(gain));
            FxLightingAdaptationOverride = gain;
            if (_ownsInputs && Active == this && _ownedWeather != null && _fxPlayback != null)
            {
                _fxPlayback.Evaluate(_gameTimeSeconds, out float effectWeight);
                PublishEnvironment(_snapshot, effectWeight);
            }
        }

        public bool TransitionTo(XRayWeatherProfile profile, float durationRealSeconds)
        {
            if (!IsFinite(durationRealSeconds) || durationRealSeconds < 0.0f) throw new ArgumentOutOfRangeException(nameof(durationRealSeconds));
            if (_fxPlayback != null)
            {
                Diagnostic = "Stop the weather effect before starting an ordinary transition.";
                return false;
            }
            if (IsTransitioning)
            {
                Diagnostic = "Finish the current weather transition before starting another resource blend.";
                return false;
            }
            string diagnostic = null;
            if (profile == null || !profile.TryEvaluate(_gameTimeSeconds, out XRayWeatherSnapshot target, out diagnostic))
            {
                Diagnostic = profile == null ? "A weather transition requires an imported profile." : diagnostic;
                return false;
            }
            if (durationRealSeconds == 0.0f || _activeProfile == null)
            {
                SetProfile(profile);
                return true;
            }
            if (!_activeProfile.HasSameSunPositions(profile))
            {
                Diagnostic = "Weather cycle transitions require identical global sun position tables; switch profiles directly when the global environment differs.";
                return false;
            }
            _transitionFrom = _activeProfile;
            _transitionTarget = profile;
            _transitionDuration = durationRealSeconds;
            _transitionElapsed = 0.0f;
            _sessionWeatherSelected = true;
            RefreshDiscontinuity(false);
            return true;
        }

        public bool StartFx(XRayWeatherFxProfile effect, double elapsedSeconds = 0.0)
        {
            return StartFxAt(effect, elapsedSeconds, false);
        }

        public bool StartFxFromRemaining(XRayWeatherFxProfile effect, double remainingSeconds)
        {
            return StartFxAt(effect, remainingSeconds, true);
        }

        private bool StartFxAt(XRayWeatherFxProfile effect, double seconds, bool remaining)
        {
            if (!_ownsInputs || Active != this || _effectiveFxCatalog == null || !_effectiveFxCatalog.Contains(effect) || !_effectiveFxCatalog.CanAttachTo(ActiveProfile) || !effect.Validate(out _))
            {
                Diagnostic = "Weather effects require active weather ownership and a compatible prepared catalog.";
                return false;
            }
            if (_fxPlayback != null && (_fxPlayback.Effect.Kind != XRayWeatherFxKind.Blowout || effect.Kind != XRayWeatherFxKind.SuperBlowout))
            {
                Diagnostic = "Only a super blowout can interrupt an active ordinary blowout.";
                return false;
            }
            double entrySeconds = FX_BLEND_REAL_SECONDS * (_simulationTimeScale > 0.0f ? _simulationTimeScale : 1.0f);
            double duration = 2.0 * entrySeconds + effect.AuthoredDurationSeconds;
            double elapsed = remaining ? duration - seconds : seconds;
            if (!double.IsFinite(elapsed) || elapsed < 0.0 || elapsed >= duration || duration >= DAY_SECONDS)
            {
                Diagnostic = "Weather effect start must be inside a finite sequence shorter than one game day.";
                return false;
            }
            if (IsTransitioning)
            {
                _activeProfile = _transitionTarget;
                ClearTransition();
            }
            _fxPlayback = new XRayWeatherFxPlayback(effect, _activeProfile, entrySeconds, _gameTimeSeconds, elapsed);
            RefreshDiscontinuity();
            return ActiveFx == effect;
        }

        public bool SeekFx(double elapsedSeconds)
        {
            if (_fxPlayback == null || !double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0 || elapsedSeconds > _fxPlayback.DurationSeconds)
            {
                Diagnostic = "Seek must be inside an active weather effect.";
                return false;
            }
            _gameTimeSeconds = WrapTime(_gameTimeSeconds + elapsedSeconds - _fxPlayback.ElapsedSeconds);
            _fxPlayback.ElapsedSeconds = elapsedSeconds;
            if (elapsedSeconds == _fxPlayback.DurationSeconds) _fxPlayback = null;
            RefreshDiscontinuity();
            return true;
        }

        public bool StopFx()
        {
            if (_fxPlayback == null) return false;
            _fxPlayback = null;
            RefreshDiscontinuity();
            return true;
        }

        public void ResetSimulation()
        {
            _settingsApplied = false;
            ApplySettings();
            if (_ownsInputs && _ownedWind != null) _ownedWind.Reinitialize();
        }

        public void TickSimulation(float deltaSeconds)
        {
            if (!IsFinite(deltaSeconds) || deltaSeconds < 0.0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!isActiveAndEnabled || !Application.IsPlaying(gameObject) && !_previewActive) return;
            if (_settingsDirty) ApplySettings();
            else if (_configuration != null && _configurationRevision != _configuration.Revision) ApplyConfiguration(_configuration);
            if (!_settingsValid) return;
            if (!TryOwnInputs()) return;
            float elapsed = _simulationPaused ? 0.0f : deltaSeconds;
            double gameDelta = elapsed * _simulationTimeScale;
            _gameTimeSeconds = WrapTime(_gameTimeSeconds + gameDelta);
            if (IsTransitioning) _transitionElapsed = Mathf.Min(_transitionDuration, _transitionElapsed + elapsed);
            bool effectCompleted = false;
            if (_fxPlayback != null)
            {
                _fxPlayback.ElapsedSeconds += gameDelta;
                effectCompleted = _fxPlayback.ElapsedSeconds >= _fxPlayback.DurationSeconds;
                if (effectCompleted) _fxPlayback = null;
            }
            EvaluateAndPublish(elapsed, effectCompleted);
            if (IsTransitioning && _transitionElapsed >= _transitionDuration)
            {
                _activeProfile = _transitionTarget;
                ClearTransition();
            }
        }

        public bool BeginPreview()
        {
            if (Application.IsPlaying(gameObject) || !isActiveAndEnabled || Active != null && Active != this) return false;
            _previewActive = true;
            _settingsApplied = false;
            ApplySettings();
            if (!_ownsInputs) _previewActive = false;
            return _previewActive;
        }

        public void TickPreview(float deltaSeconds)
        {
            if (_previewActive && !Application.IsPlaying(gameObject)) TickSimulation(deltaSeconds);
        }

        public bool BeginPreview(double timeSeconds, XRayWeatherProfile profile)
        {
            if (double.IsNaN(timeSeconds) || double.IsInfinity(timeSeconds)) throw new ArgumentOutOfRangeException(nameof(timeSeconds));
            if (profile == null || !profile.Validate(out _) || !BeginPreview()) return false;
            _gameTimeSeconds = WrapTime(timeSeconds);
            SetProfile(profile);
            if (!_ownsInputs) EndPreview();
            return _previewActive;
        }

        public void EndPreview()
        {
            if (!_previewActive) return;
            _previewActive = false;
            ReleaseInputs();
            _settingsApplied = false;
            ApplySettings();
        }

        private void OnEnable()
        {
            if (Application.IsPlaying(gameObject) && _fogRenderer == null)
            {
                _fogRenderer = GetComponent<XRayWeatherFogRenderer>();
                if (_fogRenderer == null) _fogRenderer = gameObject.AddComponent<XRayWeatherFogRenderer>();
            }
            if (!_controllers.Contains(this)) _controllers.Add(this);
            _lastFrame = -1;
            _settingsApplied = false;
            ApplySettings();
        }

        private void OnDisable()
        {
            _previewActive = false;
            ReleaseInputs();
            _controllers.Remove(this);
            if (!Application.isPlaying || Active != null) return;
            for (int i = 0; i < _controllers.Count; i++)
            {
                XRayWeatherController controller = _controllers[i];
                if (controller == null || !controller.isActiveAndEnabled || !Application.IsPlaying(controller.gameObject)) continue;
                if (controller.TryOwnInputs())
                {
                    controller.EvaluateAndPublish(0.0f, true);
                    break;
                }
            }
        }

        private void OnValidate()
        {
            _settingsDirty = true;
        }

        private void OnDestroy()
        {
            if (_precipitation != null) _precipitation.Dispose();
            _precipitation = null;
        }

        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || Time.frameCount == _lastFrame) return;
            if (Time.timeScale == 0.0f)
            {
                if (_precipitation != null && _precipitation.HasPendingVideoQuality) PublishPrecipitation(0.0f);
                return;
            }
            _lastFrame = Time.frameCount;
            TickSimulation(Time.unscaledDeltaTime);
        }

        private bool TryOwnInputs()
        {
            if (!_settingsValid) return false;
            if (Active != null && Active != this)
            {
                Diagnostic = "Another weather controller owns the global weather inputs.";
                return false;
            }
            if (_ownsInputs) return true;
            string diagnostic = null;
            if (_activeProfile == null || !_activeProfile.TryEvaluate(_gameTimeSeconds, out XRayWeatherSnapshot snapshot, out diagnostic))
            {
                Diagnostic = _activeProfile == null ? "Select an imported weather profile." : diagnostic;
                ReleaseInputs();
                return false;
            }
            if (_weatherInputs == null || _sourceLighting == null || _windProvider == null || _sun == null || _sun.type != LightType.Directional)
            {
                Diagnostic = "Weather requires explicit material inputs, source lighting, wind provider and directional light references.";
                ReleaseInputs();
                return false;
            }
            Vector3 color = new Vector3(snapshot.SunColor.r, snapshot.SunColor.g, snapshot.SunColor.b);
            Vector3 sourceColor = _activeProfile.HasSunLightScale ? Vector3.Scale(color, _activeProfile.SunLightScale) : color;
            Vector4? trees = _activeProfile.HasSsfxWindTrees ? _activeProfile.SsfxWindTrees : (Vector4?)null;
            Vector4? lightSetup = _activeProfile.HasLightSetup ? _activeProfile.LightSetup : (Vector4?)null;
            Vector4? floraFixes = _activeProfile.HasFloraFixes ? _activeProfile.FloraFixes : (Vector4?)null;
            bool weather = _weatherInputs.TrySetWeatherInputs(this, color, lightSetup, floraFixes);
            bool lighting = _sourceLighting.TrySetWeatherLight(this, _sun, sourceColor, snapshot.SunDirection);
            bool wind = _windProvider.TryAcquireWeather(this, trees);
            bool sky = ReferenceEquals(_skyRenderer, null) || _skyRenderer != null && _skyRenderer.TrySetWeather(this, snapshot, _windProvider.State.Time);
            bool fog = !FogEnabled || ReferenceEquals(_fogRenderer, null) || _fogRenderer != null && _fogRenderer.TrySetWeather(this, snapshot);
            if (!weather || !lighting || !wind || !sky || !fog)
            {
                _weatherInputs.ReleaseWeatherInputs(this);
                _sourceLighting.ReleaseWeatherLight(this);
                _windProvider.ReleaseWeather(this);
                if (_skyRenderer != null) _skyRenderer.ReleaseWeather(this);
                if (_fogRenderer != null) _fogRenderer.ReleaseWeather(this);
                Diagnostic = "A referenced weather consumer is disabled or already owned by another provider.";
                return false;
            }
            _ownedWeather = _weatherInputs;
            _ownedLighting = _sourceLighting;
            _ownedWind = _windProvider;
            _ownedSun = _sun;
            _ownedSunData = _sun.GetComponent<HDAdditionalLightData>();
            _ownedSky = _skyRenderer;
            _ownsSky = _ownedSky != null;
            _ownedFog = _fogRenderer;
            _ownsFog = _ownedFog != null;
            _savedSunColor = _sun.color;
            _savedSunIntensity = _sun.intensity;
            _savedSunRotation = _sun.transform.rotation;
            _ownsInputs = true;
            _rebaseWind = true;
            Active = this;
            return true;
        }

        private void EvaluateAndPublish(float deltaSeconds, bool resetPrevious, bool resetCelestial = true)
        {
            if (!_ownsInputs || Active != this) return;
            if (_ownsSky && _ownedSky == null)
            {
                Diagnostic = "The owned weather sky renderer was destroyed; authored inputs were restored.";
                ReleaseInputs();
                return;
            }
            if (_ownsFog && _ownedFog == null)
            {
                Diagnostic = "The owned weather fog renderer was destroyed; authored inputs were restored.";
                ReleaseInputs();
                return;
            }
            string diagnostic = null;
            if (_activeProfile == null || !_activeProfile.TryEvaluate(_gameTimeSeconds, out XRayWeatherSnapshot snapshot, out diagnostic))
            {
                Diagnostic = _activeProfile == null ? "Select an imported weather profile." : diagnostic;
                ReleaseInputs();
                return;
            }
            Vector2 flareVisibility = ResolveFlareVisibility(_activeProfile, snapshot);
            bool effectFlareHandover = false;
            float effectWeight = 0.0f;
            if (_fxPlayback != null)
            {
                snapshot = _fxPlayback.Evaluate(_gameTimeSeconds, out effectWeight);
                Vector2 effectVisibility = Vector2.one * (FX_FLARE_TRANSMISSION * XRayWeatherFlareVisibility.FogTransmission(snapshot));
                flareVisibility = Vector2.Lerp(flareVisibility, effectVisibility, effectWeight);
                effectFlareHandover = effectWeight >= 1.0f && !_fxPlayback.AuthoredFlarePublished;
                _fxPlayback.AuthoredFlarePublished = effectWeight >= 1.0f;
            }
            else if (IsTransitioning)
            {
                if (!_transitionFrom.TryEvaluate(_gameTimeSeconds, out XRayWeatherSnapshot from, out string fromDiagnostic) || !_transitionTarget.TryEvaluate(_gameTimeSeconds, out XRayWeatherSnapshot target, out string targetDiagnostic))
                {
                    Diagnostic = "A weather transition profile is invalid.";
                    ReleaseInputs();
                    return;
                }
                snapshot = XRayWeatherSnapshot.Blend(from, target, TransitionProgress);
                flareVisibility = Vector2.Lerp(flareVisibility, ResolveFlareVisibility(_transitionTarget, target), TransitionProgress);
            }
            Vector3 color = new Vector3(snapshot.SunColor.r, snapshot.SunColor.g, snapshot.SunColor.b);
            Vector3 sourceColor = ActiveProfile.HasSunLightScale ? Vector3.Scale(color, ActiveProfile.SunLightScale) : color;
            Vector4? trees = ActiveProfile.HasSsfxWindTrees ? ActiveProfile.SsfxWindTrees : (Vector4?)null;
            Vector4? lightSetup = ActiveProfile.HasLightSetup ? ActiveProfile.LightSetup : (Vector4?)null;
            Vector4? floraFixes = ActiveProfile.HasFloraFixes ? ActiveProfile.FloraFixes : (Vector4?)null;
            if (_ownedWeather == null || _ownedLighting == null || _ownedWind == null || _ownedSun == null || !_ownedWeather.TrySetWeatherInputs(this, color, lightSetup, floraFixes) || !_ownedLighting.TrySetWeatherLight(this, _ownedSun, sourceColor, snapshot.SunDirection) || !_ownedWind.TryAcquireWeather(this, trees))
            {
                Diagnostic = "Weather consumer ownership changed; authored inputs were restored.";
                ReleaseInputs();
                return;
            }
            _snapshot = snapshot;
            _flareVisibility = flareVisibility;
            SourceLightColor = sourceColor;
            Color linearSun = new Color(sourceColor.x, sourceColor.y, sourceColor.z, 1.0f).linear;
            float intensityScale = linearSun.maxColorComponent;
            Color sunColor = intensityScale > 0.0f ? (linearSun / intensityScale).gamma : Color.black;
            sunColor.a = 1.0f;
            _ownedSun.color = sunColor;
            _ownedSun.intensity = _savedSunIntensity * intensityScale;
            _ownedSun.transform.rotation = Quaternion.LookRotation(snapshot.SunDirection);
            _ownedWind.TickWeather(this, deltaSeconds, new XRayFloraWindWeather(snapshot.WindDirectionRadians, snapshot.WindVelocity, snapshot.TreeAmplitudeIntensity), resetPrevious || _rebaseWind || deltaSeconds == 0.0f);
            if (_ownedSky != null && !_ownedSky.TrySetWeather(this, snapshot, _ownedWind.State.Time))
            {
                Diagnostic = _ownedSky.Diagnostic;
                ReleaseInputs();
                return;
            }
            if (_ownedFog != null && FogEnabled && !_ownedFog.TrySetWeather(this, snapshot))
            {
                Diagnostic = _ownedFog.Diagnostic;
                ReleaseInputs();
                return;
            }
            if (_ownedFog != null && !FogEnabled && _ownedFog.HasWeather) _ownedFog.ReleaseWeather(this);
            PublishEnvironment(snapshot, effectWeight);
            PublishPrecipitation(deltaSeconds);
            _ownedWeather.SetWeatherNightAmbient(this, _ownedSky != null ? XRayWeatherSkyPass.GetNightRadiance(_ownedSky.Settings) : Vector3.zero);
            bool transitionCompletedWithoutGameTime = IsTransitioning && _transitionElapsed >= _transitionDuration && _simulationTimeScale == 0.0f;
            PublishCelestial(deltaSeconds, resetPrevious && resetCelestial || transitionCompletedWithoutGameTime || effectFlareHandover);
            _rebaseWind = false;
            Diagnostic = ActiveProfile.HasSsfxWindTrees ? diagnostic : "INPUT BLOCKED: imported ssfx_wind_trees are unavailable; wind uses the provider's authored diagnostic tree settings.";
            if (!ActiveProfile.HasSunLightScale) Diagnostic = "INPUT BLOCKED: saved source sunlight scale is unavailable; descriptor RGB remains an explicit diagnostic light input.";
        }

        private void PublishPrecipitation(float deltaSeconds)
        {
            if (_precipitation == null) _precipitation = new XRayPrecipitationSystem(gameObject);
            float radiance = _ownedSky != null ? XRayWeatherSkyPass.GetEnvironmentRadianceScale(_ownedSky.Settings) : 1.0f;
            _precipitation.Publish(EffectivePrecipitationConfig, _snapshot, _simulationPaused ? 0.0f : deltaSeconds, radiance, PrecipitationEnabled, RainVisualOverride ?? true);
        }

        private void PublishEnvironment(XRayWeatherSnapshot snapshot, float effectWeight)
        {
            Vector2? scale = ActiveProfile.HasEnvironmentScale ? ActiveProfile.EnvironmentScale : (Vector2?)null;
            float radianceScale = _ownedSky != null ? XRayWeatherSkyPass.GetEnvironmentRadianceScale(_ownedSky.Settings) : 1.0f;
            Vector4 fxLighting = Vector4.zero;
            if (effectWeight > 0.0f)
            {
                XRayWeatherFxProfile effect = _fxPlayback.Effect;
                bool night = effect.EffectName.EndsWith("_night", StringComparison.Ordinal);
                float calibratedGain = night && effect.Kind == XRayWeatherFxKind.PsiStorm ? FX_PSI_NIGHT_GAIN : FX_DARK_SURFACE_GAIN;
                float directWeight = Mathf.SmoothStep(0.0f, 1.0f, snapshot.SunColor.maxColorComponent / FX_DIRECT_LIGHT_ART_CUTOFF);
                float gain = FxLightingAdaptationGain * calibratedGain * Mathf.Clamp01(snapshot.SkyColor.maxColorComponent) * (1.0f - directWeight);
                fxLighting = new Vector4(effectWeight, radianceScale, gain, night ? FX_NIGHT_ART_RADIANCE_LIMIT : FX_DAY_ART_RADIANCE_LIMIT);
            }
            _ownedWeather.SetWeatherEnvironment(this, snapshot, scale, fxLighting, radianceScale);
        }

        private void PublishCelestial(float deltaSeconds, bool snap)
        {
            if (!_celestialResolved)
            {
                _celestialResolved = true;
                XRayWeatherCelestialSettings settings = _appliedCelestialSettings;
                if (settings != null && settings.Shader != null && settings.Shader.isSupported) _celestial = new XRayWeatherCelestialRenderer(gameObject, settings);
                snap = true;
            }
            float sourceLux = _ownedSun != null ? _ownedSun.intensity : 0.0f;
            if (_celestial != null) _celestial.Publish(_snapshot, SunShaftsEnabled, LensFlareEnabled, deltaSeconds * _simulationTimeScale, snap || _simulationPaused, ExpressiveDaylight, sourceLux, _flareVisibility, LensFlareIntensity, AdaptLensFlareToWeather, ExpressiveFlareIntensity);
            PublishNativeShafts();
        }

        private Vector2 ResolveFlareVisibility(XRayWeatherProfile profile, XRayWeatherSnapshot snapshot)
        {
            foreach (XRayWeatherFlareProfileOverride settings in _flareProfileOverrides)
            {
                if (settings.Profile == profile) return XRayWeatherFlareVisibility.Resolve(profile, snapshot, settings.Visibility);
            }
            return XRayWeatherFlareVisibility.Resolve(profile, snapshot);
        }

        private void PublishNativeShafts()
        {
            bool mediumActive = _ownedFog != null && _ownedFog.HasWeather && _ownedSun != null && _ownedSunData != null && _celestial != null && _celestial.ShaftsIntensity > 0.001f;
            bool lightActive = mediumActive && _ownedSun.intensity > 0.0f;
            float daylight = lightActive ? Mathf.InverseLerp(64.0f, 800.0f, _ownedSun.intensity) : 0.0f;
            if (_ownedFog != null) _ownedFog.SetNativeShafts(this, mediumActive, daylight);
            if (!lightActive)
            {
                RestoreShaftsLight();
                return;
            }
            if (!_ownsShaftsLight)
            {
                _savedAffectsVolumetric = _ownedSunData.affectsVolumetric;
                if (!_savedAffectsVolumetric) _ownedSunData.affectsVolumetric = true;
                _savedVolumetricDimmer = _ownedSunData.volumetricDimmer;
                _savedVolumetricShadowDimmer = _ownedSunData.volumetricShadowDimmer;
                _ownsShaftsLight = true;
            }
            float gain = Mathf.Lerp(SHAFTS_NIGHT_GAIN, SHAFTS_DAY_GAIN, daylight);
            _ownedSunData.volumetricDimmer = _celestial.ShaftsIntensity * gain;
            _ownedSunData.volumetricShadowDimmer = 1.0f;
        }

        private void RestoreShaftsLight()
        {
            if (_ownsShaftsLight && _ownedSunData != null)
            {
                _ownedSunData.volumetricDimmer = _savedVolumetricDimmer;
                _ownedSunData.volumetricShadowDimmer = _savedVolumetricShadowDimmer;
                if (_ownedSunData.affectsVolumetric != _savedAffectsVolumetric) _ownedSunData.affectsVolumetric = _savedAffectsVolumetric;
            }
            _ownsShaftsLight = false;
        }

        private void ReleaseCelestial()
        {
            if (_celestial != null) _celestial.Release();
            _celestial = null;
            _celestialResolved = false;
        }

        private void RefreshDiscontinuity(bool resetCelestial = true)
        {
            if (!Application.IsPlaying(gameObject) && !_previewActive) return;
            if (TryOwnInputs()) EvaluateAndPublish(0.0f, true, resetCelestial);
        }

        private void ReleaseInputs()
        {
            if (_precipitation != null) _precipitation.Suspend();
            _fxPlayback = null;
            if (_ownedSun != null)
            {
                _ownedSun.color = _savedSunColor;
                _ownedSun.intensity = _savedSunIntensity;
                _ownedSun.transform.rotation = _savedSunRotation;
            }
            if (_ownedWeather != null) _ownedWeather.ReleaseWeatherInputs(this);
            if (_ownedLighting != null) _ownedLighting.ReleaseWeatherLight(this);
            if (_ownedWind != null) _ownedWind.ReleaseWeather(this);
            if (_ownedSky != null) _ownedSky.ReleaseWeather(this);
            if (_ownedFog != null) _ownedFog.ReleaseWeather(this);
            RestoreShaftsLight();
            ReleaseCelestial();
            PrecipitationOverride = null;
            RainVisualOverride = null;
            FogOverride = null;
            SunShaftsOverride = null;
            LensFlareOverride = null;
            LensFlareIntensityOverride = null;
            FlareAdaptationOverride = null;
            ExpressiveFlareIntensityOverride = null;
            _ownedWeather = null;
            _ownedLighting = null;
            _ownedWind = null;
            _ownedSun = null;
            _ownedSunData = null;
            _ownedSky = null;
            _ownsSky = false;
            _ownedFog = null;
            _ownsFog = false;
            _ownsInputs = false;
            if (Active == this) Active = null;
        }

        private void ClearTransition()
        {
            _transitionFrom = null;
            _transitionTarget = null;
            _transitionElapsed = 0.0f;
            _transitionDuration = 0.0f;
        }

        private static double WrapTime(double value)
        {
            return (value % DAY_SECONDS + DAY_SECONDS) % DAY_SECONDS;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
