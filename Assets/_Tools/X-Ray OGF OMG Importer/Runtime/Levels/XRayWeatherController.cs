using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class XRayWeatherController : MonoBehaviour
    {
        private const double DAY_SECONDS = 86400.0;

        private static readonly List<XRayWeatherController> _controllers = new List<XRayWeatherController>();

        [SerializeField] private XRayWeatherProfile _profile;
        [SerializeField, Range(0.0f, 24.0f)] private float _timeOfDayHours = 12.0f;
        [SerializeField, Min(0.0f)] private float _timeScale = 1.0f;
        [SerializeField] private bool _paused;
        [SerializeField] private MonolithWeatherInputs _weatherInputs;
        [SerializeField] private MonolithSourceLighting _sourceLighting;
        [SerializeField] private XRayFloraWindProvider _windProvider;
        [SerializeField] private Light _sun;
        [SerializeField] private XRayWeatherSkyRenderer _skyRenderer;

        private XRayWeatherProfile _activeProfile;
        private XRayWeatherProfile _appliedProfile;
        private XRayWeatherProfile _transitionFrom;
        private XRayWeatherProfile _transitionTarget;
        private XRayWeatherSnapshot _snapshot;
        private MonolithWeatherInputs _ownedWeather;
        private MonolithSourceLighting _ownedLighting;
        private XRayFloraWindProvider _ownedWind;
        private Light _ownedSun;
        private XRayWeatherSkyRenderer _ownedSky;
        private Color _savedSunColor;
        private float _savedSunIntensity;
        private Quaternion _savedSunRotation;
        private double _gameTimeSeconds;
        private float _simulationTimeScale;
        private float _appliedInitialTime;
        private float _transitionDuration;
        private float _transitionElapsed;
        private int _lastFrame = -1;
        private bool _simulationPaused;
        private bool _settingsApplied;
        private bool _settingsValid;
        private bool _settingsDirty;
        private bool _previewActive;
        private bool _ownsInputs;
        private bool _rebaseWind;
        private bool _ownsSky;

        public static XRayWeatherController Active { get; private set; }

        public XRayWeatherProfile ActiveProfile => _transitionTarget != null ? _transitionTarget : _activeProfile;
        public XRayWeatherSnapshot Snapshot => _snapshot;
        public Vector3 SourceLightColor { get; private set; }
        public string Diagnostic { get; private set; }
        public double GameTimeSeconds => _gameTimeSeconds;
        public bool Paused => _simulationPaused;
        public float TimeScale => _simulationTimeScale;
        public bool PreviewActive => _previewActive;
        public bool IsTransitioning => _transitionTarget != null;
        public float TransitionProgress => IsTransitioning ? Mathf.Clamp01(_transitionElapsed / _transitionDuration) : 1.0f;

        public void Configure(XRayWeatherProfile profile, MonolithWeatherInputs weatherInputs, MonolithSourceLighting sourceLighting, XRayFloraWindProvider windProvider, Light sun)
        {
            ReleaseInputs();
            _profile = profile;
            _weatherInputs = weatherInputs;
            _sourceLighting = sourceLighting;
            _windProvider = windProvider;
            _sun = sun;
            _settingsApplied = false;
            ApplySettings();
        }

        public void ApplySettings()
        {
            _settingsDirty = false;
            if (!IsFinite(_timeOfDayHours) || !IsFinite(_timeScale) || _timeScale < 0.0f)
            {
                _settingsValid = false;
                Diagnostic = "Weather time and scale must be finite; scale must be nonnegative.";
                ReleaseInputs();
                return;
            }
            _settingsValid = true;
            bool discontinuity = !_settingsApplied || _appliedInitialTime != _timeOfDayHours || _appliedProfile != _profile;
            if (!_settingsApplied || _appliedInitialTime != _timeOfDayHours) _gameTimeSeconds = WrapTime(_timeOfDayHours * 3600.0);
            if (!_settingsApplied || _appliedProfile != _profile)
            {
                _activeProfile = _profile;
                ClearTransition();
            }
            discontinuity |= _simulationPaused != _paused;
            _simulationPaused = _paused;
            _simulationTimeScale = _timeScale;
            _appliedInitialTime = _timeOfDayHours;
            _appliedProfile = _profile;
            _settingsApplied = true;
            if (_ownsInputs && (_ownedWeather != _weatherInputs || _ownedLighting != _sourceLighting || _ownedWind != _windProvider || _ownedSun != _sun || _ownedSky != _skyRenderer)) ReleaseInputs();
            if (Application.IsPlaying(gameObject) || _previewActive)
            {
                if (TryOwnInputs()) EvaluateAndPublish(0.0f, discontinuity);
            }
        }

        public void SetProfile(XRayWeatherProfile profile)
        {
            _activeProfile = profile;
            ClearTransition();
            RefreshDiscontinuity();
        }

        public void ConfigureSky(XRayWeatherSkyRenderer skyRenderer)
        {
            ReleaseInputs();
            _skyRenderer = skyRenderer;
            RefreshDiscontinuity();
        }

        public void Seek(double gameSeconds)
        {
            if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds)) throw new ArgumentOutOfRangeException(nameof(gameSeconds));
            _gameTimeSeconds = WrapTime(gameSeconds);
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
            _simulationTimeScale = timeScale;
        }

        public bool TransitionTo(XRayWeatherProfile profile, float durationRealSeconds)
        {
            if (!IsFinite(durationRealSeconds) || durationRealSeconds < 0.0f) throw new ArgumentOutOfRangeException(nameof(durationRealSeconds));
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
            if (!_settingsValid) return;
            if (!TryOwnInputs()) return;
            float elapsed = _simulationPaused ? 0.0f : deltaSeconds;
            _gameTimeSeconds = WrapTime(_gameTimeSeconds + elapsed * _simulationTimeScale);
            if (IsTransitioning) _transitionElapsed = Mathf.Min(_transitionDuration, _transitionElapsed + elapsed);
            EvaluateAndPublish(elapsed, false);
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

        public void EndPreview()
        {
            if (!_previewActive) return;
            _previewActive = false;
            ReleaseInputs();
        }

        private void OnEnable()
        {
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

        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            TickSimulation(Time.unscaledDeltaTime);
        }

        private bool TryOwnInputs()
        {
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
            if (!weather || !lighting || !wind || !sky)
            {
                _weatherInputs.ReleaseWeatherInputs(this);
                _sourceLighting.ReleaseWeatherLight(this);
                _windProvider.ReleaseWeather(this);
                if (_skyRenderer != null) _skyRenderer.ReleaseWeather(this);
                Diagnostic = "A referenced weather consumer is disabled or already owned by another provider.";
                return false;
            }
            _ownedWeather = _weatherInputs;
            _ownedLighting = _sourceLighting;
            _ownedWind = _windProvider;
            _ownedSun = _sun;
            _ownedSky = _skyRenderer;
            _ownsSky = _ownedSky != null;
            _savedSunColor = _sun.color;
            _savedSunIntensity = _sun.intensity;
            _savedSunRotation = _sun.transform.rotation;
            _ownsInputs = true;
            _rebaseWind = true;
            Active = this;
            return true;
        }

        private void EvaluateAndPublish(float deltaSeconds, bool resetPrevious)
        {
            if (!_ownsInputs || Active != this) return;
            if (_ownsSky && _ownedSky == null)
            {
                Diagnostic = "The owned weather sky renderer was destroyed; authored inputs were restored.";
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
            if (IsTransitioning)
            {
                if (!_transitionFrom.TryEvaluate(_gameTimeSeconds, out XRayWeatherSnapshot from, out string fromDiagnostic) || !_transitionTarget.TryEvaluate(_gameTimeSeconds, out XRayWeatherSnapshot target, out string targetDiagnostic))
                {
                    Diagnostic = "A weather transition profile is invalid.";
                    ReleaseInputs();
                    return;
                }
                snapshot = XRayWeatherSnapshot.Blend(from, target, TransitionProgress);
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
            SourceLightColor = sourceColor;
            Color linearSun = new Color(sourceColor.x, sourceColor.y, sourceColor.z, 1.0f).linear;
            float intensityScale = linearSun.maxColorComponent;
            Color sunColor = intensityScale > 0.0f ? (linearSun / intensityScale).gamma : Color.black;
            sunColor.a = 1.0f;
            _ownedSun.color = sunColor;
            _ownedSun.intensity = _savedSunIntensity * intensityScale;
            _ownedSun.transform.rotation = Quaternion.LookRotation(snapshot.SunDirection);
            _ownedWind.TickWeather(this, deltaSeconds, new XRayFloraWindWeather(snapshot.WindDirectionRadians, snapshot.WindVelocity, snapshot.TreeAmplitudeIntensity), resetPrevious || _rebaseWind || deltaSeconds == 0.0f);
            _ownedWeather.SetWeatherEnvironment(this, snapshot, ActiveProfile.HasEnvironmentScale ? ActiveProfile.EnvironmentScale : (Vector2?)null);
            if (_ownedSky != null && !_ownedSky.TrySetWeather(this, snapshot, _ownedWind.State.Time))
            {
                Diagnostic = _ownedSky.Diagnostic;
                ReleaseInputs();
                return;
            }
            _ownedWeather.SetWeatherNightAmbient(this, _ownedSky != null ? XRayWeatherSkyPass.GetNightRadiance(_ownedSky.Settings) : Vector3.zero);
            _rebaseWind = false;
            Diagnostic = ActiveProfile.HasSsfxWindTrees ? diagnostic : "INPUT BLOCKED: imported ssfx_wind_trees are unavailable; wind uses the provider's authored diagnostic tree settings.";
            if (!ActiveProfile.HasSunLightScale) Diagnostic = "INPUT BLOCKED: saved source sunlight scale is unavailable; descriptor RGB remains an explicit diagnostic light input.";
        }

        private void RefreshDiscontinuity()
        {
            if (!Application.IsPlaying(gameObject) && !_previewActive) return;
            if (TryOwnInputs()) EvaluateAndPublish(0.0f, true);
        }

        private void ReleaseInputs()
        {
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
            _ownedWeather = null;
            _ownedLighting = null;
            _ownedWind = null;
            _ownedSun = null;
            _ownedSky = null;
            _ownsSky = false;
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
