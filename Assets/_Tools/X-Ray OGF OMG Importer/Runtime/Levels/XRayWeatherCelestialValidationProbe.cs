using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace XRay.Importer.Levels
{
    public sealed class XRayWeatherCelestialValidationProbe : MonoBehaviour
    {
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";
        private const int WIDTH = 640;
        private const int HEIGHT = 360;
        private const int NATIVE_WARMUP = 14;
        private const float TOLERANCE = 0.00001f;
        private const double VISIBLE_DELTA = 0.0000001;

        private static XRayWeatherCelestialValidationProbe _instance;

        [SerializeField] private Camera _camera;
        [SerializeField] private XRayWeatherController _controller;
        [SerializeField] private XRayWeatherProfile _clear;
        [SerializeField] private XRayWeatherProfile _rain;
        [SerializeField] private GameObject _opaque;
        [SerializeField] private GameObject _alpha;
        [SerializeField] private Light _sun;
        [SerializeField] private string _sourceHash;
        [SerializeField] private int _cameraMask;
        [SerializeField] private bool _catalogExpected;
        [SerializeField] private bool _fxExpected;
        [SerializeField] private Material _fxLightingSurface;

        private string _output;
        private string _configurationState;
        private GameConfig _sharedConfiguration;
        private bool _stagePassed;
        private readonly List<string> _runtimeErrors = new List<string>();

        public void Configure(Camera camera, XRayWeatherController controller, XRayWeatherProfile clear, XRayWeatherProfile rain, GameObject opaque, GameObject alpha, string sourceHash)
        {
            _camera = camera;
            _controller = controller;
            _clear = clear;
            _rain = rain;
            _opaque = opaque;
            _alpha = alpha;
            _sourceHash = sourceHash;
            _cameraMask = camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value;
        }

        public void ConfigureEnvironment(Light sun)
        {
            _sun = sun;
        }

        public void RequireCatalog(bool expected)
        {
            _catalogExpected = expected;
        }

        public void RequireFx(bool expected)
        {
            _fxExpected = expected;
        }

        public void ConfigureFxLightingSurface(Material material)
        {
            _fxLightingSurface = material;
        }

        public string RunEditorCases(string evidenceDir)
        {
            bool started = !_controller.PreviewActive;
            if (started && !_controller.BeginPreview()) throw new InvalidOperationException("The validation fixture could not own weather in Editor preview.");
            try
            {
                return RunCases(_camera, _controller, _clear, _rain, _opaque, _alpha, evidenceDir, _sun);
            }
            finally
            {
                if (started) _controller.EndPreview();
            }
        }

        private void Awake()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++) if (arguments[i] == "-celestialEvidence") _output = arguments[i + 1];
            if (string.IsNullOrEmpty(_output))
            {
                enabled = false;
                return;
            }
            if (_instance != null && _instance != this)
            {
                _instance.Configure(_camera, _controller, _clear, _rain, _opaque, _alpha, _sourceHash);
                _instance.ConfigureEnvironment(_sun);
                _instance._cameraMask = _cameraMask;
                _instance._catalogExpected = _catalogExpected;
                _instance._fxExpected = _fxExpected;
                _instance._fxLightingSurface = _fxLightingSurface;
                Destroy(gameObject);
                return;
            }
            _instance = this;
            PrepareOutput(_output);
            DontDestroyOnLoad(gameObject);
        }

        private IEnumerator Start()
        {
            if (_instance != this || string.IsNullOrEmpty(_output)) yield break;
            Application.logMessageReceived += ObserveLog;
            var system = new SystemRecord { unity = Application.unityVersion, api = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName, sourceSha256 = _sourceHash };
            Write(_output, "System", system);
            for (int i = 0; i < 8; i++) yield return null;
            XRayWeatherFogRenderer serialized = _controller.GetComponent<XRayWeatherFogRenderer>();
            bool hadSerialized = serialized != null && serialized.Volume != null && serialized.Volume.gameObject.hideFlags != HideFlags.HideAndDontSave;
            _controller.enabled = false;
            if (serialized != null) Destroy(serialized);
            yield return null;
            _controller.enabled = true;
            yield return null;
            yield return RunPlayerStage("Initial", true, hadSerialized && serialized == null);
            bool initial = _stagePassed;
            string scene = _controller.gameObject.scene.name;
            yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            for (int i = 0; i < 8; i++) yield return null;
            yield return RunPlayerStage("Reload", false, true);
            bool passed = initial && _stagePassed && _runtimeErrors.Count == 0;
            Write(_output, "Result", new PlayerRecord { passed = passed, initial = initial, reload = _stagePassed, sourceSha256 = _sourceHash, api = system.api, runtimeErrors = _runtimeErrors.ToArray() });
            Application.logMessageReceived -= ObserveLog;
            Application.Quit(passed ? 0 : 1);
        }

        private IEnumerator RunPlayerStage(string stage, bool fallback, bool setupValid)
        {
            _stagePassed = false;
            string directory = Path.Combine(_output, stage);
            Directory.CreateDirectory(directory);
            var record = new StageRecord { expectedFallback = fallback };
            XRayWeatherFogRenderer fog = _controller.GetComponent<XRayWeatherFogRenderer>();
            try
            {
                GameConfig configuration = GameConfig.Load();
                Require(configuration != null && configuration.TryValidate(out _), "The Player requires a valid canonical Resources/GameConfig.asset.");
                if (_sharedConfiguration == null)
                {
                    _sharedConfiguration = configuration;
                    _configurationState = ConfigurationJson(configuration);
                }
                record.sharedConfiguration = configuration == _sharedConfiguration && _controller.Configuration == configuration;
                record.inheritedTimeScale = _controller.TimeScale;
                record.configurationInherited = (_controller.LevelOverrides & XRayWeatherLevelOverrides.TimeScale) == 0 && !_controller.TimeScaleOverride.HasValue && record.inheritedTimeScale == configuration.DefaultTimeScale;
                record.sharedCatalogs = _controller.Catalog == configuration.OrdinaryWeatherCatalog && _controller.FxCatalog == configuration.WeatherFxCatalog;
                record.ordinaryCycles = _controller.Catalog == null ? 0 : _controller.Catalog.Profiles.Count;
                record.weatherFxEffects = _controller.FxCatalog == null ? 0 : _controller.FxCatalog.Effects.Count;
                record.sharedGraphics = _controller.FogEnabled == configuration.FogEnabled && _controller.SunShaftsEnabled == configuration.SunShaftsEnabled && _controller.LensFlareEnabled == configuration.LensFlareEnabled && _controller.FxLightingAdaptationGain == configuration.FxLightingAdaptation;
                Require(record.sharedConfiguration && record.configurationInherited, "The Player must retain one canonical configuration and inherit the configured default TimeScale after initialization and reload.");
                Require(record.sharedCatalogs && record.ordinaryCycles == 21 && record.weatherFxEffects == 7 && record.sharedGraphics, "The Player must resolve the shared catalogs and supported graphics defaults.");
                Require(setupValid && fog != null && fog.HasWeather && fog.Volume != null, "The current scene owns its native Fog consumer.");
                record.runtimeFallback = fog.Volume.gameObject.hideFlags == HideFlags.HideAndDontSave;
                Require(record.runtimeFallback == fallback && (_cameraMask & (1 << fog.Volume.gameObject.layer)) != 0, "The native Fog consumer uses the original camera layer and expected serialized/fallback binding.");
                Require(_camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value == _cameraMask, "The fixture's original Volume Mask is unchanged.");
                if (_catalogExpected) RunCatalogStage(directory, fog);
                if (_fxExpected)
                {
                    RunAdaptationStage(directory);
                    RunFxStage(directory, fog);
                    if (_fxLightingSurface != null) RunFxLightingPixelStage(directory);
                }
                string resultPath = RunCases(_camera, _controller, _clear, _rain, _opaque, _alpha, Path.Combine(directory, "Cases"), _sun);
                ResultRecord cases = JsonUtility.FromJson<ResultRecord>(File.ReadAllText(resultPath));
                record.captureWritten = cases.captureWritten;
                record.assertionsPassed = cases.assertionsPassed;
                record.restorationPassed = cases.restorationPassed;
                record.error = cases.error;
                record.configurationReadOnly = _configurationState == ConfigurationJson(configuration);
                Require(record.configurationReadOnly, "Runtime weather and FX must preserve all shared configuration assets.");
            }
            catch (Exception exception)
            {
                record.error = exception.ToString();
            }
            Volume boundFog = fallback || fog == null ? null : fog.Volume;
            VolumeProfile authoredFog = boundFog == null ? null : boundFog.sharedProfile;
            XRayWeatherCelestialRenderer celestial = _controller.Celestial;
            CustomPassVolume volume = celestial?.Volume;
            _controller.enabled = false;
            yield return null;
            record.released = XRayWeatherController.Active != _controller && _controller.Celestial == null && (volume == null || !volume.isActiveAndEnabled) && (fog == null || !fog.HasWeather) && (boundFog == null || boundFog.sharedProfile == authoredFog && !boundFog.HasInstantiatedProfile());
            _controller.enabled = true;
            yield return null;
            record.reacquired = XRayWeatherController.Active == _controller && _controller.Celestial != null && _controller.Celestial != celestial && fog != null && fog.HasWeather && (boundFog == null || fog.Volume == boundFog);
            record.configurationReadOnly &= _sharedConfiguration != null && _configurationState == ConfigurationJson(_sharedConfiguration);
            record.passed = record.captureWritten && record.assertionsPassed && record.restorationPassed && record.released && record.reacquired && record.sharedConfiguration && record.configurationInherited && record.sharedCatalogs && record.sharedGraphics && record.configurationReadOnly && string.IsNullOrEmpty(record.error);
            Write(directory, "Result", record);
            _stagePassed = record.passed;
        }

        private void RunFxLightingPixelStage(string directory)
        {
            const int PIXEL_WIDTH = 256;
            const int PIXEL_HEIGHT = 144;
            const float MILITARY_SKY_EXPOSURE = 10.5f;
            var context = new CaptureContext(_camera, _controller, Path.Combine(directory, "FxLightingPixels"), new List<FrameRecord>(), _sun);
            PrepareOutput(context.Output);
            var original = new SavedState(context, _opaque, _alpha);
            float? overrideBefore = _controller.FxLightingAdaptationOverride;
            XRayWeatherSkySettings sky = _controller.GetComponent<XRayWeatherSkyRenderer>().Settings;
            bool useHdrpIntensity = sky.UseHdrpIntensity;
            SkyIntensityMode skyIntensityMode = sky.skyIntensityMode.value;
            float skyExposure = sky.exposure.value;
            var surface = _opaque.GetComponent<Renderer>();
            Material previous = surface.sharedMaterial;
            var exposureObject = new GameObject("FX Lighting Validation Exposure");
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                exposureObject.layer = _opaque.layer;
                SceneManager.MoveGameObjectToScene(exposureObject, _camera.gameObject.scene);
                var volume = exposureObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10001.0f;
                var exposure = profile.Add<Exposure>(true);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(9.0f);
                profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
                profile.Add<Bloom>(true).intensity.Override(0.0f);
                volume.sharedProfile = profile;
                sky.SetHdrpIntensity(true);
                sky.skyIntensityMode.value = SkyIntensityMode.Exposure;
                sky.exposure.value = MILITARY_SKY_EXPOSURE;

                XRayWeatherFxProfile effect = _controller.FxCatalog.FindVariant(XRayWeatherFxKind.PsiStorm, 43200.0);
                Require(effect != null, "The source psi storm is available for the pixel fixture.");
                _controller.SetProfile(_clear);
                _controller.SetPaused(true);
                _controller.SetTimeScale(10.0f);
                _controller.Seek(43200.0);
                _controller.SetCelestialEffectOverrides(false, false);
                _controller.SetFxLightingAdaptationOverride(0.0f);
                Require(_controller.StartFx(effect) && _controller.SeekFx(_controller.FxEntrySeconds + 480.0), "The pixel fixture reaches the zero-direct psi storm key.");
                Require(_sun.intensity == 0.0f && _controller.Snapshot.SunColor.maxColorComponent == 0.0f, "The pixel fixture uses the true zero direct source.");
                float radianceScale = Shader.GetGlobalVector("_MonolithWeatherFxAmbient").y;
                Require(Mathf.Abs(radianceScale - sky.GetIntensityFromSettings()) < TOLERANCE && radianceScale > 1.0f, "The pixel fixture uses the military Sky radiance at fixed camera exposure.");

                var cameraData = _camera.GetComponent<HDAdditionalCameraData>();
                cameraData.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.LitShaderMode, false);
                cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.LitShaderMode] = true;
                _camera.transform.rotation = Quaternion.identity;
                surface.sharedMaterial = _fxLightingSurface;
                _opaque.transform.SetPositionAndRotation(_camera.transform.position + new Vector3(0.0f, -2.25f, 12.0f), Quaternion.identity);
                _opaque.transform.localScale = new Vector3(10.0f, 6.0f, 1.0f);
                _opaque.SetActive(true);
                _alpha.SetActive(false);
                Color[] before = CaptureFrame(context, "Unadapted", PIXEL_WIDTH, PIXEL_HEIGHT, 8);
                _controller.SetFxLightingAdaptationOverride(null);
                Color[] after = CaptureFrame(context, "Adapted", PIXEL_WIDTH, PIXEL_HEIGHT, 8);
                double geometryBefore = MeanLuminance(before, 96, 30, 160, 63);
                double geometryAfter = MeanLuminance(after, 96, 30, 160, 63);
                double skyDifference = Math.Abs(MeanLuminance(after, 8, 112, 100, 136) - MeanLuminance(before, 8, 112, 100, 136));
                bool passed = geometryAfter > geometryBefore + VISIBLE_DELTA && skyDifference < Math.Max(TOLERANCE, (geometryAfter - geometryBefore) * 0.25) && _sun.intensity == 0.0f;
                Write(context.Output, "Result", new FxLightingPixelRecord { passed = passed, geometryMeanBefore = geometryBefore, geometryMeanAfter = geometryAfter, skyMeanDifference = skyDifference, sunLux = _sun.intensity, radianceScale = radianceScale, gain = _controller.FxLightingAdaptationGain });
                Require(passed, "The Player adaptation brightens the terrain surface with 0 lux and without raising the sky.");
            }
            finally
            {
                sky.SetHdrpIntensity(useHdrpIntensity);
                sky.skyIntensityMode.value = skyIntensityMode;
                sky.exposure.value = skyExposure;
                if (_controller.ActiveFx != null) _controller.StopFx();
                _controller.SetFxLightingAdaptationOverride(overrideBefore);
                surface.sharedMaterial = previous;
                exposureObject.SetActive(false);
                Destroy(exposureObject);
                Destroy(profile);
                original.Restore(context);
            }

            double MeanLuminance(Color[] pixels, int minX, int minY, int maxX, int maxY)
            {
                double total = 0.0;
                for (int y = minY; y < maxY; y++)
                {
                    for (int x = minX; x < maxX; x++)
                    {
                        Color pixel = pixels[y * PIXEL_WIDTH + x];
                        total += pixel.r * 0.2126 + pixel.g * 0.7152 + pixel.b * 0.0722;
                    }
                }
                return total / ((maxX - minX) * (maxY - minY));
            }
        }

        [Serializable]
        private sealed class FxLightingPixelRecord
        {
            public bool passed;
            public double geometryMeanBefore;
            public double geometryMeanAfter;
            public double skyMeanDifference;
            public float sunLux;
            public float radianceScale;
            public float gain;
        }

        private void RunAdaptationStage(string directory)
        {
            var context = new CaptureContext(_camera, _controller, Path.Combine(directory, "Adaptation"), new List<FrameRecord>(), _sun);
            var original = new SavedState(context, _opaque, _alpha);
            float? intensity = _controller.LensFlareIntensityOverride;
            bool? adaptation = _controller.FlareAdaptationOverride;
            float? expressive = _controller.ExpressiveFlareIntensityOverride;
            var samples = new List<PlayerFlareSample>();
            PrepareOutput(context.Output);
            try
            {
                _controller.SetPaused(true);
                _controller.SetFlareAppearanceOverrides(1.0f, true, 1.0f);
                _controller.SetCelestialEffectOverrides(true, true);
                foreach (XRayWeatherProfile profile in _controller.Catalog.Profiles)
                {
                    _controller.SetProfile(profile);
                    foreach (double time in new[] { 0.0, 43200.0 })
                    {
                        _controller.Seek(time);
                        XRayWeatherCelestialRenderer renderer = _controller.Celestial;
                        float visibility = XRayWeatherFlareVisibility.AtTime(XRayWeatherFlareVisibility.Resolve(profile, _controller.Snapshot), time);
                        Require(Mathf.Abs(renderer.WeatherVisibility - visibility) < TOLERANCE && renderer.SourceBrightness == _sun.intensity, "The Player changed flare weather visibility or source Lux for " + profile.CycleName);
                        samples.Add(new PlayerFlareSample { profile = profile.CycleName, time = time, brightness = renderer.SourceBrightness, visibility = renderer.WeatherVisibility, intensity = renderer.EffectiveIntensity });
                    }
                }
                double clear = MeasureFlare(_clear, 43200.0, "Clear");
                double dense = MeasureFlare(FindCatalogProfile(_controller.Catalog, "w_cloudy2_dark"), 43200.0, "Dense");
                double moon = MeasureFlare(_clear, 0.0, "Moon");
                Require(clear > VISIBLE_DELTA && moon > VISIBLE_DELTA && dense < TOLERANCE, "The Player must show real clear solar/lunar contribution and suppress dense daylight flare.");
                Write(context.Output, "Result", new PlayerFlareAdaptationRecord { samples = samples.ToArray(), clearPixelDelta = clear, densePixelDelta = dense, clearMoonPixelDelta = moon });
            }
            finally
            {
                original.Restore(context);
                _controller.SetFlareAppearanceOverrides(intensity, adaptation, expressive);
            }

            double MeasureFlare(XRayWeatherProfile profile, double time, string label)
            {
                _controller.SetProfile(profile);
                _controller.Seek(time);
                _camera.transform.rotation = Quaternion.LookRotation(-_controller.Snapshot.SunDirection);
                Color[] off = Capture(context, label + "_Off", true, false);
                Color[] on = Capture(context, label + "_On", true, true);
                for (int i = 0; i < off.Length; i++) Require(Mathf.Abs(off[i].a - on[i].a) < TOLERANCE, "The Player flare changed frame alpha in " + label);
                return Difference(off, on);
            }
        }

        private void RunFxStage(string directory, XRayWeatherFogRenderer fog)
        {
            XRayWeatherFxCatalog catalog = _controller.FxCatalog;
            Require(catalog != null && catalog.Effects.Count == 7, "The Player must serialize all seven Weather FX profiles.");
            Require(fog.Volume.profile.TryGet(out Fog fogSettings), "The Player's weather Fog settings are active during FX.");
            XRayWeatherProfile ordinary = _controller.ActiveProfile;
            double time = _controller.GameTimeSeconds;
            bool paused = _controller.Paused;
            float scale = _controller.TimeScale;
            bool? shafts = _controller.SunShaftsOverride;
            bool? flare = _controller.LensFlareOverride;
            XRayWeatherSkyRenderer sky = _controller.GetComponent<XRayWeatherSkyRenderer>();
            Require(sky != null && sky.HasWeather && sky.Settings != null, "The Player requires an active weather Sky for the FX radiance conversion.");
            float expectedRadianceScale = sky.Settings.UseHdrpIntensity ? sky.Settings.GetIntensityFromSettings() : 1.0f;
            float? adaptation = _controller.FxLightingAdaptationOverride;
            int fxLightingId = Shader.PropertyToID("_MonolithWeatherFxAmbient");
            int weatherActiveId = Shader.PropertyToID("_MonolithWeatherEnvironmentActive");
            var names = new List<string>();
            var samples = new List<PlayerFxLightingSample>();
            int keys = 0;
            int zeroDirectKeys = 0;
            int artKeys = 0;
            bool zero = false;
            bool zeroSky = false;
            try
            {
                _controller.SetPaused(false);
                _controller.SetTimeScale(2.0f);
                _controller.SetCelestialEffectOverrides(true, true);
                foreach (XRayWeatherFxProfile effect in catalog.Effects)
                {
                    names.Add(effect.EffectName);
                    _controller.Seek(86399.0);
                    Require(_controller.StartFx(effect) && _controller.FxPhase == XRayWeatherFxPhase.Entry, "The Player cannot start " + effect.EffectName + ". " + _controller.Diagnostic);
                    Require(Shader.GetGlobalVector(fxLightingId) == Vector4.zero, "FX indirect lighting must be absent during the ordinary entry blend.");
                    _controller.TickSimulation(1.0f);
                    Require(_controller.FxTimeSeconds == 2.0 && _controller.GameTimeSeconds == 1.0, "The Player did not use one wrapped clock while entering " + effect.EffectName);
                    double entry = _controller.FxEntrySeconds;
                    double end = entry + effect.AuthoredDurationSeconds;
                    double duration = _controller.FxDurationSeconds;
                    Require(entry == 10.0 && duration == end + entry, "The Player computed unexpected entry and return times for " + effect.EffectName);
                    for (int i = 1; i < effect.KeyframeCount; i++)
                    {
                        XRayWeatherSnapshot key = effect.GetKeyframe(i);
                        _controller.SeekFx(entry + key.GameSeconds);
                        XRayWeatherSnapshot snapshot = _controller.Snapshot;
                        Require(_controller.ActiveFx == effect && snapshot.SunDefinition == key.SunDefinition && Vector4.Distance(snapshot.SunColor, key.SunColor) < TOLERANCE && snapshot.SkyResources.Texture(0) is Cubemap && snapshot.EnvironmentResources.Texture(0) is Cubemap && fog.HasWeather, "The Player changed the FX frame " + effect.EffectName + "/" + key.Key0);
                        XRayWeatherFlareDefinition definition = _controller.Celestial.FlareDefinition;
                        Require(definition?.Name == (string.IsNullOrEmpty(key.SunDefinition) ? null : key.SunDefinition), "The Player retained the wrong flare at " + effect.EffectName + "/" + key.Key0);
                        if (key.SunDefinition == "none") Require(definition.Source.Texture == null && definition.Gradient.Texture != null && definition.Elements.Count > 0, "The Player lost the source named none flare data.");
                        Vector4 fxLighting = Shader.GetGlobalVector(fxLightingId);
                        bool blackSky = key.SkyColor.maxColorComponent == 0.0f;
                        Require(Shader.GetGlobalFloat(weatherActiveId) == 1.0f && fxLighting.x == 1.0f && fxLighting.y > 0.0f && Mathf.Abs(fxLighting.y - expectedRadianceScale) < TOLERANCE, "The Player did not publish source FX lighting in the current HDRP Sky units for " + effect.EffectName + "/" + key.Key0);
                        Require(float.IsFinite(fxLighting.z) && fxLighting.z >= 0.0f && (!blackSky || fxLighting.z == 0.0f), "The Player applied artistic FX gain to a source blackout.");
                        Require(Vector4.Distance(snapshot.AmbientColor, key.AmbientColor) < TOLERANCE && Vector4.Distance(snapshot.HemisphereColor, key.HemisphereColor) < TOLERANCE && Vector4.Distance(snapshot.SkyColor, key.SkyColor) < TOLERANCE, "The Player changed the authored FX lighting for " + effect.EffectName + "/" + key.Key0);
                        if (key.SunColor.maxColorComponent == 0.0f)
                        {
                            Require(_sun.intensity == 0.0f && _controller.Celestial.EffectiveIntensity == 0.0f, "FX indirect lighting changed the zero direct source or flare for " + effect.EffectName + "/" + key.Key0);
                            zeroDirectKeys++;
                        }
                        else
                        {
                            Require(_sun.intensity > 0.0f && fxLighting.z == 0.0f, "The Player changed an authored direct flash or added artistic fill during it for " + effect.EffectName + "/" + key.Key0);
                        }
                        if (blackSky) zeroSky = true;
                        if (fxLighting.z > 0.0f) artKeys++;
                        samples.Add(new PlayerFxLightingSample { effect = effect.EffectName, key = key.Key0, input = fxLighting, sourceSun = snapshot.SunColor, sourceAmbient = snapshot.AmbientColor, sourceHemisphere = snapshot.HemisphereColor, sourceSky = snapshot.SkyColor, sunLux = _sun.intensity });
                        keys++;
                        if (effect.EffectName == "fx_super_blowout_night" && key.GameSeconds == 432.0)
                        {
                            Require(_sun.intensity == 0.0f && _controller.SourceLightColor == Vector3.zero && _controller.Celestial.EffectiveIntensity == 0.0f && fogSettings.enableVolumetricFog.value, "The source zero must suppress flare and light while preserving native Fog.");
                            _controller.SetFxLightingAdaptationOverride(0.0f);
                            Vector4 sourceOnly = Shader.GetGlobalVector(fxLightingId);
                            Require(sourceOnly.x == fxLighting.x && sourceOnly.y == fxLighting.y && sourceOnly.z == 0.0f && _sun.intensity == 0.0f, "Disabling FX art adaptation must preserve source lighting and zero direct Lux.");
                            _controller.SetFxLightingAdaptationOverride(adaptation);
                            _controller.SetCelestialEffectOverrides(false, true);
                            Require(!fogSettings.enableVolumetricFog.value && _sun.intensity == 0.0f, "Explicit shafts-off must restore analytic Fog at the FX zero key.");
                            _controller.SetCelestialEffectOverrides(true, true);
                            zero = true;
                        }
                    }
                    _controller.SeekFx(end + entry * 0.5);
                    Require(_controller.FxPhase == XRayWeatherFxPhase.Return, "The Player skipped the return phase of " + effect.EffectName);
                    Require(Mathf.Abs(Shader.GetGlobalVector(fxLightingId).x - 0.5f) < TOLERANCE, "The Player did not fade FX indirect lighting during return.");
                    _controller.SetPaused(true);
                    double returnClock = _controller.GameTimeSeconds;
                    _controller.TickSimulation(5.0f);
                    Require(_controller.GameTimeSeconds == returnClock && _controller.FxTimeSeconds == end + entry * 0.5, "The Player did not freeze FX and world clock on Pause.");
                    _controller.SetPaused(false);
                    _controller.TickSimulation(2.5f);
                    Require(_controller.ActiveFx == null && _controller.FxPhase == XRayWeatherFxPhase.None && _controller.ActiveProfile == ordinary, "The Player did not return to its ordinary profile after " + effect.EffectName);
                    Require(Shader.GetGlobalVector(fxLightingId) == Vector4.zero, "The Player retained FX lighting after the ordinary weather return.");
                    Require(ordinary.TryEvaluate(_controller.GameTimeSeconds, out XRayWeatherSnapshot expected, out _) && expected.SunDefinition == _controller.Snapshot.SunDefinition && expected.SunColor == _controller.Snapshot.SunColor, "The Player did not sample current ordinary weather on return.");
                    Require(_controller.Celestial.FlareDefinition?.Name == (string.IsNullOrEmpty(expected.SunDefinition) ? null : expected.SunDefinition), "The Player retained a stale FX flare on return.");
                }
                XRayWeatherFxProfile cancelled = catalog.Effects[0];
                Require(_controller.StartFx(cancelled, 10.0 + cancelled.GetKeyframe(1).GameSeconds) && Shader.GetGlobalVector(fxLightingId).x == 1.0f, "The Player cannot reach an authored FX state before cancellation.");
                _controller.StopFx();
                Require(_controller.ActiveFx == null && Shader.GetGlobalVector(fxLightingId) == Vector4.zero, "The Player retained FX lighting after cancellation.");
                Require(zero && zeroSky && keys == 105 && zeroDirectKeys == 99 && artKeys > 0, "The Player must cover all authored playable FX keys, source blackouts and direct flashes.");
                Write(directory, "WeatherFx", new PlayerFxRecord { effects = names.ToArray(), playableKeys = keys, exactZero = zero, zeroDirectKeys = zeroDirectKeys, artKeys = artKeys, radianceScale = expectedRadianceScale, lighting = samples.ToArray() });
            }
            finally
            {
                if (_controller.ActiveFx != null) _controller.StopFx();
                _controller.SetProfile(ordinary);
                _controller.Seek(time);
                _controller.SetTimeScale(scale);
                _controller.SetPaused(paused);
                _controller.SetCelestialEffectOverrides(shafts, flare);
                _controller.SetFxLightingAdaptationOverride(adaptation);
            }
        }

        [Serializable]
        private sealed class PlayerFlareSample
        {
            public string profile;
            public double time;
            public float brightness;
            public float visibility;
            public float intensity;
        }

        [Serializable]
        private sealed class PlayerFlareAdaptationRecord
        {
            public PlayerFlareSample[] samples;
            public double clearPixelDelta;
            public double densePixelDelta;
            public double clearMoonPixelDelta;
        }

        [Serializable]
        private sealed class PlayerFxRecord
        {
            public string[] effects;
            public int playableKeys;
            public bool exactZero;
            public int zeroDirectKeys;
            public int artKeys;
            public float radianceScale;
            public PlayerFxLightingSample[] lighting;
        }

        [Serializable]
        private sealed class PlayerFxLightingSample
        {
            public string effect;
            public string key;
            public Vector4 input;
            public Color sourceSun;
            public Color sourceAmbient;
            public Color sourceHemisphere;
            public Color sourceSky;
            public float sunLux;
        }

        private void RunCatalogStage(string directory, XRayWeatherFogRenderer fog)
        {
            XRayWeatherCatalog catalog = _controller.Catalog;
            Require(catalog != null && catalog.Profiles.Count == 21, "The Player must serialize all 21 ordinary profiles.");
            Require(fog.Volume.profile.TryGet(out Fog fogSettings), "The Player's weather Fog settings are active.");
            XRayWeatherProfile original = _controller.ActiveProfile;
            double time = _controller.GameTimeSeconds;
            bool paused = _controller.Paused;
            float scale = _controller.TimeScale;
            bool? shafts = _controller.SunShaftsOverride;
            bool? flare = _controller.LensFlareOverride;
            var profiles = new List<string>();
            var transitions = new List<string>();
            int keys = 0;
            int zeroSun = 0;
            try
            {
                _controller.SetPaused(true);
                _controller.SetCelestialEffectOverrides(true, true);
                foreach (XRayWeatherProfile profile in catalog.Profiles)
                {
                    Require(catalog.Contains(profile) && catalog.IsCompatible(profile) && !profiles.Contains(profile.CycleName), "The Player catalog contains an incompatible or repeated profile.");
                    profiles.Add(profile.CycleName);
                    _controller.SetProfile(profile);
                    Require(_controller.ActiveProfile == profile && XRayWeatherController.Active == _controller, "The Player could not select " + profile.CycleName + ". " + _controller.Diagnostic);
                    for (int i = 0; i < profile.KeyframeCount; i++)
                    {
                        XRayWeatherSnapshot key = profile.GetKeyframe(i);
                        _controller.Seek(key.GameSeconds);
                        XRayWeatherSnapshot snapshot = _controller.Snapshot;
                        string label = profile.CycleName + "/" + key.Key0;
                        Require(snapshot.GameSeconds == key.GameSeconds && snapshot.SunColor == key.SunColor && snapshot.FogDensity == key.FogDensity && snapshot.RainDensity == key.RainDensity, "The Player changed the serialized key " + label);
                        Require(snapshot.SkyResources.Texture(0) is Cubemap && snapshot.EnvironmentResources.Texture(0) is Cubemap && fog.HasWeather, "The Player is missing serialized Sky/Environment/Fog at " + label);
                        XRayWeatherFlareDefinition flareDefinition = _controller.Celestial.FlareDefinition;
                        string definitionName = string.IsNullOrEmpty(snapshot.SunDefinition) ? null : snapshot.SunDefinition;
                        Require(flareDefinition?.Name == definitionName, "The Player could not apply the flare definition at " + label);
                        if (definitionName == "none") Require(flareDefinition.Source.Texture == null && flareDefinition.Gradient.Texture != null && flareDefinition.Elements.Count > 0, "The none definition must retain the source gradient and flare elements at " + label);
                        keys++;
                        if (key.SunColor.maxColorComponent != 0.0f) continue;
                        Require(_sun.intensity == 0.0f && fogSettings.enableVolumetricFog.value, "The native Fog or exact zero Lux was lost at " + label);
                        _controller.SetCelestialEffectOverrides(false, true);
                        Require(!fogSettings.enableVolumetricFog.value && _sun.intensity == 0.0f, "Disabling shafts must restore analytic Fog and preserve zero Lux at " + label);
                        _controller.SetCelestialEffectOverrides(true, true);
                        CheckCatalogSunBoundary(key.GameSeconds - 1.0, fogSettings, label);
                        CheckCatalogSunBoundary(key.GameSeconds + 1.0, fogSettings, label);
                        zeroSun++;
                    }
                    _controller.Seek(-1.0);
                    XRayWeatherSnapshot beforeMidnight = _controller.Snapshot;
                    _controller.Seek(86399.0);
                    Require(beforeMidnight.GameSeconds == _controller.GameTimeSeconds && beforeMidnight.SunColor == _controller.Snapshot.SunColor, "The Player could not wrap time for " + profile.CycleName);
                }
                _controller.SetPaused(false);
                foreach (string[] pair in new[] { new[] { "w_clear1", "w_cloudy1" }, new[] { "w_clear1", "w_foggy1" }, new[] { "w_partly1", "w_rain1" }, new[] { "w_rain1", "w_storm1" } })
                {
                    RunCatalogTransition(catalog, pair[0], pair[1]);
                    transitions.Add(pair[0] + " -> " + pair[1]);
                }
                Require(zeroSun > 0, "The catalog must contain a checked zero-Sun key.");
                Write(directory, "Catalog", new CatalogRecord { profiles = profiles.ToArray(), keys = keys, zeroSun = zeroSun, transitions = transitions.ToArray() });
            }
            finally
            {
                _controller.SetProfile(original);
                _controller.Seek(time);
                _controller.SetTimeScale(scale);
                _controller.SetPaused(paused);
                _controller.SetCelestialEffectOverrides(shafts, flare);
            }
        }

        private void CheckCatalogSunBoundary(double time, Fog fog, string label)
        {
            _controller.Seek(time);
            bool sourcePositive = _controller.Snapshot.SunColor.maxColorComponent > 0.0f;
            Require(fog.enableVolumetricFog.value && (_sun.intensity > 0.0f) == sourcePositive, "The Player changed Fog mode or Sun intensity beside " + label);
        }

        private void RunCatalogTransition(XRayWeatherCatalog catalog, string from, string to)
        {
            XRayWeatherProfile first = FindCatalogProfile(catalog, from);
            XRayWeatherProfile second = FindCatalogProfile(catalog, to);
            _controller.SetProfile(first);
            _controller.Seek(86398.0);
            _controller.SetTimeScale(4.0f);
            Require(_controller.TransitionTo(second, 4.0f), "The Player could not transition from " + from + " to " + to + ". " + _controller.Diagnostic);
            Require(!_controller.TransitionTo(first, 1.0f), "The Player accepted overlapping weather transitions.");
            _controller.TickSimulation(1.0f);
            Require(_controller.GameTimeSeconds == 2.0 && _controller.TransitionProgress == 0.25f, "The Player transition did not cross midnight with a single game clock.");
            _controller.SetPaused(true);
            _controller.TickSimulation(2.0f);
            Require(_controller.GameTimeSeconds == 2.0 && _controller.TransitionProgress == 0.25f, "Pause did not freeze the Player transition.");
            _controller.Seek(43200.0);
            _controller.SetPaused(false);
            _controller.SetTimeScale(0.0f);
            _controller.TickSimulation(1.0f);
            Require(_controller.GameTimeSeconds == 43200.0 && _controller.TransitionProgress == 0.5f, "Zero TimeScale stopped the Player transition or advanced its game clock.");
            Require(second.TryEvaluate(43200.0, out XRayWeatherSnapshot target, out _) && _controller.Snapshot.SunDefinition == target.SunDefinition, "The Player did not switch the flare definition at the transition midpoint.");
            _controller.TickSimulation(2.0f);
            Require(!_controller.IsTransitioning && _controller.ActiveProfile == second && _controller.TransitionTo(first, 0.0f) && _controller.ActiveProfile == first, "The Player did not complete the timed and immediate transitions.");
        }

        private static XRayWeatherProfile FindCatalogProfile(XRayWeatherCatalog catalog, string cycle)
        {
            foreach (XRayWeatherProfile profile in catalog.Profiles)
            {
                if (profile.CycleName == cycle) return profile;
            }
            throw new InvalidOperationException("The Player catalog is missing " + cycle);
        }

        public static string RunCases(Camera camera, XRayWeatherController controller, XRayWeatherProfile clear, XRayWeatherProfile rain, GameObject opaque, GameObject alpha, string evidenceDir, Light sun = null)
        {
            Require(camera != null && controller != null && clear != null && rain != null && opaque != null && alpha != null, "The validation fixture has all serialized references.");
            Require(XRayWeatherController.Active == controller && controller.Celestial != null && !controller.IsTransitioning, "The fixture owns weather and starts outside a transition.");
            Require(controller.Celestial.FlareFade == 0.0f || controller.Celestial.FlareFade == 1.0f && controller.Celestial.FlareDefinition?.Name == controller.Snapshot.SunDefinition, "Finish the current flare fade before taking a reversible capture.");
            PrepareOutput(evidenceDir);
            var result = new ResultRecord();
            var frames = new List<FrameRecord>();
            var checks = new List<string>();
            var context = new CaptureContext(camera, controller, evidenceDir, frames, sun);
            var original = new SavedState(context, opaque, alpha);
            try
            {
                Require(context.SunData != null && context.Settings != null, "The validation fixture has an HDRP directional light and the shared celestial settings.");
                HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Volumetrics, true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Volumetrics] = true;
                RenderTimed(camera, 1);
                Write(evidenceDir, "Before", ReadFrame(context, "Before", WIDTH, HEIGHT, 1));
                controller.SetPaused(true);
                opaque.SetActive(false);
                alpha.SetActive(false);
                SetWeather(context, clear, 43200.0);
                ExpectSource(context, 0.045f, "sunset_sunrise_small");
                Color[] clearOff = Capture(context, "ClearDay_Off", false, false);
                Color[] clearShafts = Capture(context, "ClearDay_Shafts", true, false);
                RequireNative(context.Frames[context.Frames.Count - 1]);
                Color[] clearFlare = Capture(context, "ClearDay_Flare", false, true);
                Color[] clearBoth = Capture(context, "ClearDay_Both", true, true);
                var day = new EffectDeltaRecord { shafts = Difference(clearOff, clearShafts), flare = Difference(clearOff, clearFlare), both = Difference(clearOff, clearBoth), addFlare = Difference(clearShafts, clearBoth), addShafts = Difference(clearFlare, clearBoth) };
                Write(context.Output, "DayABMetrics", day);
                Require(day.flare > VISIBLE_DELTA && day.addFlare > VISIBLE_DELTA, "Daytime flare contributes independently with the native volume enabled or disabled.");
                checks.Add("Daytime independent native shafts and flare controls with Fog, Sky and Sun");
                CheckOccluders(context, opaque, alpha, clearOff, day.flare, checks);
                CheckNativeShafts(context, opaque, "Sun", checks);
                CheckMotion(context, checks);
                CheckCameraVariants(context, checks);
                SetWeather(context, rain, 43200.0);
                ExpectSource(context, 0.09f, "storm_sunrise1");
                Color[] rainOff = Capture(context, "RainDay_Off", false, false);
                Color[] rainBoth = Capture(context, "RainDay_Both", true, true);
                RequireNative(context.Frames[context.Frames.Count - 1]);
                Require(Difference(rainOff, rainBoth) > VISIBLE_DELTA, "Rain day retains its nonzero celestial source.");
                CheckMoon(context, clear, "ClearMoon", "moon_full_clear", 0.32f, checks);
                CheckNativeShafts(context, opaque, "Moon", checks);
                CheckMoon(context, rain, "RainMoon", "moon_full_verycloudydark", 0.18f, checks);
                SetWeather(context, clear, 16200.0);
                ExpectSource(context, 0.0f, "none");
                Capture(context, "BlackSource_Off", false, false);
                Require(!context.Frames[context.Frames.Count - 1].volumetricFog, "Disabling shafts selects analytic fog at zero light.");
                Color[] blackShafts = Capture(context, "BlackSource_Shafts", true, false);
                Color[] blackBoth = Capture(context, "BlackSource_Both", true, true);
                FrameRecord blackNative = context.Frames[context.Frames.Count - 1];
                Require(blackNative.volumetricFog && context.Sun.intensity == 0.0f && blackNative.flareDraws == 0, "The native fog stays active at zero physical light, with no visible flare source.");
                Require(Difference(blackShafts, blackBoth) < TOLERANCE, "A zero source RGB adds no flare over the same native fog.");
                SetWeather(context, rain, 16200.0);
                ExpectSource(context, 0.03f, "");
                Require(controller.Celestial.FlareDefinition == null, "Empty flare definition removes the active target.");
                checks.Add("Zero RGB, positive floor, literal none and empty definition");
                CheckTransition(context, clear, rain, "ClearToRain", 0.045f, 0.09f, "sunset_sunrise_small", "storm_sunrise1");
                CheckTransition(context, rain, clear, "RainToClear", 0.09f, 0.045f, "storm_sunrise1", "sunset_sunrise_small");
                checks.Add("Clear/rain and rain/clear select definitions independently from raw intensity");
                CheckMidnight(context, clear);
                checks.Add("Pause, TimeScale and midnight through TickSimulation");
                SetWeather(context, clear, 43200.0);
                Measure(context);
                result.assertionsPassed = true;
            }
            catch (Exception exception)
            {
                result.error = exception.ToString();
            }
            finally
            {
                try
                {
                    original.Restore(context);
                    RenderTimed(context.Camera, 1);
                    result.restorationPassed = original.Matches(context);
                    Write(evidenceDir, "After", ReadFrame(context, "After", WIDTH, HEIGHT, 1));
                }
                catch (Exception exception)
                {
                    result.restorationError = exception.ToString();
                }
                result.captureWritten = frames.Count > 0;
                result.captures = frames.Count;
                result.frames = frames.ToArray();
                result.checks = checks.ToArray();
                result.passed = result.captureWritten && result.assertionsPassed && result.restorationPassed;
                Write(evidenceDir, "Result", result);
            }
            return Path.Combine(evidenceDir, "Result.json");
        }

        private static void CheckOccluders(CaptureContext context, GameObject opaque, GameObject alpha, Color[] open, double openFlare, List<string> checks)
        {
            PlaceBlocker(context.Camera, opaque, false);
            Color[] partialOff = Capture(context, "OpaquePartial_Off", false, false);
            Color[] partialFlare = Capture(context, "OpaquePartial_Flare", false, true);
            Require(Difference(partialOff, open) > VISIBLE_DELTA && Difference(partialOff, partialFlare) > VISIBLE_DELTA, "Partial opaque overlap leaves a visible flare.");
            PlaceBlocker(context.Camera, opaque, true);
            Color[] fullOff = Capture(context, "OpaqueFull_Off", false, false);
            Color[] fullFlare = Capture(context, "OpaqueFull_Flare", false, true);
            Require(Difference(fullOff, fullFlare) < Math.Max(TOLERANCE, openFlare * 0.01), "Full opaque coverage suppresses the flare.");
            opaque.SetActive(false);
            PlaceBlocker(context.Camera, alpha, true);
            Color[] alphaOff = Capture(context, "AlphaClip_Off", false, false);
            Color[] alphaFlare = Capture(context, "AlphaClip_Flare", false, true);
            double geometryCoverage = ChangedFraction(open, alphaOff);
            Require(geometryCoverage > 0.01 && geometryCoverage < 0.99 && Difference(alphaOff, alphaFlare) > VISIBLE_DELTA, "The alpha-clipped surface leaves holes for the flare.");
            alpha.SetActive(false);
            Write(context.Output, "OccluderMetrics", new OccluderRecord { openFlareDelta = openFlare, partialFlareDelta = Difference(partialOff, partialFlare), fullFlareDelta = Difference(fullOff, fullFlare), alphaFlareDelta = Difference(alphaOff, alphaFlare), alphaCoverage = geometryCoverage });
            checks.Add("Flare occlusion through opaque and alpha-clipped depth");
        }

        private static void CheckNativeShafts(CaptureContext context, GameObject opaque, string label, List<string> checks)
        {
            Camera camera = context.Camera;
            XRayWeatherController controller = context.Controller;
            HDAdditionalLightData sun = context.SunData;
            Quaternion rotation = camera.transform.rotation;
            var blocker = new BlockerState(opaque);
            bool? shafts = controller.SunShaftsOverride;
            bool? flare = controller.LensFlareOverride;
            controller.SetCelestialEffectOverrides(true, false);
            float dimmer = sun.volumetricDimmer;
            try
            {
                camera.transform.rotation = Quaternion.LookRotation(controller.Snapshot.SunDirection, camera.transform.up);
                PlaceBlocker(camera, opaque, true);
                sun.volumetricDimmer = 0.0f;
                Color[] off = CaptureFrame(context, label + "_NativeAway_Zero", WIDTH, HEIGHT, NATIVE_WARMUP);
                Color[] repeat = CaptureFrame(context, label + "_NativeAway_ZeroRepeat", WIDTH, HEIGHT, NATIVE_WARMUP);
                sun.volumetricDimmer = dimmer;
                Color[] on = CaptureFrame(context, label + "_NativeAway_Lit", WIDTH, HEIGHT, NATIVE_WARMUP);
                FrameRecord lit = context.Frames[context.Frames.Count - 1];
                double meanLightIncrease = 0.0;
                for (int i = 0; i < on.Length; i++) meanLightIncrease += on[i].grayscale - repeat[i].grayscale;
                meanLightIncrease /= on.Length;
                float sourceForward = Vector3.Dot(camera.transform.forward, -controller.Snapshot.SunDirection);
                double noise = Difference(off, repeat);
                Write(context.Output, label + "_NativeAwayMetrics", new NativeShaftsRecord { sourceForward = sourceForward, volumetricDimmer = lit.volumetricDimmer, volumetricShadowDimmer = lit.volumetricShadowDimmer, zeroRepeatNoise = noise, meanLightIncrease = meanLightIncrease });
                RequireNative(lit);
                Require(sourceForward < -0.99f && meanLightIncrease > Math.Max(VISIBLE_DELTA, noise * 2.0), "Native directional scattering adds light in the depth-tested view facing away from " + label + ".");
            }
            finally
            {
                sun.volumetricDimmer = dimmer;
                blocker.Restore();
                camera.transform.rotation = rotation;
                controller.SetCelestialEffectOverrides(shafts, flare);
            }
            checks.Add(label + " native scattering with identical Fog and opaque depth, facing away from the source");
        }

        private static void CheckMotion(CaptureContext context, List<string> checks)
        {
            Camera camera = context.Camera;
            Quaternion front = camera.transform.rotation;
            Vector3 up = camera.transform.up;
            float edge = Mathf.Atan(Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * WIDTH / HEIGHT) * Mathf.Rad2Deg;
            float[] angles = { 0.0f, edge * 0.5f, edge * 0.98f, edge * 1.12f, 110.0f };
            Color[][] off = CaptureMotion(context, front, up, angles, false);
            int firstFrame = context.Frames.Count;
            Color[][] on = CaptureMotion(context, front, up, angles, true);
            FrameRecord edgeFrame = context.Frames[firstFrame + 2];
            FrameRecord behindFrame = context.Frames[firstFrame + angles.Length - 1];
            Require(Mathf.Abs(edgeFrame.sourceUv.x - 0.5f) > 0.35f && behindFrame.flareDraws == 0, "Flare projection crosses the visible edge and rejects the rear hemisphere.");
            double behindDelta = Difference(on[0], off[0]);
            double settledDelta = Difference(on[1], off[1]);
            double returnDelta = Difference(on[2], off[2]);
            HDCamera hdCamera = HDCamera.GetOrCreate(camera);
            MotionBlur motionBlur = hdCamera.volumeStack.GetComponent<MotionBlur>();
            Write(context.Output, "MotionMetrics", new MotionRecord { angles = angles, nearEdgeUv = edgeFrame.sourceUv, behindFirstFrameDelta = behindDelta, behindSettledDelta = settledDelta, offFirstVsSettledDelta = Difference(off[0], off[1]), returnFirstFrameDelta = returnDelta, motionBlurFrameSetting = hdCamera.frameSettings.IsEnabled(FrameSettingsField.MotionBlur), motionBlurIntensity = motionBlur.intensity.value, cameraMotionBlur = motionBlur.cameraMotionBlur.value });
            Require(behindDelta < TOLERANCE, "The first frame after a rear-facing cut contains no flare trail.");
            Require(settledDelta < TOLERANCE, "The settled rear-facing image contains no flare contribution.");
            Require(context.Frames[context.Frames.Count - 1].flareDraws > 0 && returnDelta > VISIBLE_DELTA, "The first frame after return projects the flare from the current camera.");
            checks.Add("Matched flare OFF/ON motion, edge, first rear-facing cut, settled rear and immediate return");
        }

        private static Color[][] CaptureMotion(CaptureContext context, Quaternion front, Vector3 up, float[] angles, bool enabled)
        {
            Camera camera = context.Camera;
            context.Controller.SetCelestialEffectOverrides(false, enabled);
            camera.transform.rotation = front;
            RenderTimed(camera, 1);
            Color[] rear = null;
            for (int i = 0; i < angles.Length; i++)
            {
                camera.transform.rotation = Quaternion.AngleAxis(angles[i], up) * front;
                string label = enabled ? "Motion_" + i : i == angles.Length - 1 ? "Motion_BehindOff" : "Motion_Off_" + i;
                rear = Capture(context, label, false, enabled, warmup: 1);
            }
            Color[] settled = Capture(context, enabled ? "Motion_BehindSettled" : "Motion_BehindSettledOff", false, enabled, warmup: 1);
            camera.transform.rotation = front;
            Color[] returned = Capture(context, enabled ? "Motion_ReturnFirst" : "Motion_ReturnFirstOff", false, enabled, warmup: 1);
            return new[] { rear, settled, returned };
        }

        private static void CheckCameraVariants(CaptureContext context, List<string> checks)
        {
            HDAdditionalCameraData data = context.Camera.GetComponent<HDAdditionalCameraData>();
            HDAdditionalCameraData.AntialiasingMode old = data.antialiasing;
            try
            {
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                Capture(context, "Resized_TAA", true, true, 512, 288);
                FrameRecord resized = context.Frames[context.Frames.Count - 1];
                RequireNative(resized);
                Require(resized.flareDraws > 0, "The resized TAA camera projects the flare.");
            }
            finally
            {
                data.antialiasing = old;
            }
            var secondObject = new GameObject("Celestial secondary validation camera");
            secondObject.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                SceneManager.MoveGameObjectToScene(secondObject, context.Camera.gameObject.scene);
                Camera second = secondObject.AddComponent<Camera>();
                second.CopyFrom(context.Camera);
                second.enabled = false;
                second.scene = context.Camera.scene;
                data.CopyTo(secondObject.AddComponent<HDAdditionalCameraData>());
                second.transform.SetPositionAndRotation(context.Camera.transform.position, Quaternion.AngleAxis(135.0f, context.Camera.transform.up) * context.Camera.transform.rotation);
                var secondContext = new CaptureContext(second, context.Controller, context.Output, context.Frames, context.Sun);
                Capture(secondContext, "SecondCamera_Behind", true, true, 480, 270, 1);
                FrameRecord secondState = context.Frames[context.Frames.Count - 1];
                RequireNative(secondState);
                Require(secondState.cameraObserved && secondState.flareDraws == 0, "The second camera receives native Fog and independently rejects the flare behind it.");
                Capture(context, "PrimaryCamera_AfterSecond", true, true, warmup: 1);
                FrameRecord primary = context.Frames[context.Frames.Count - 1];
                RequireNative(primary);
                Require(primary.cameraObserved && primary.flareDraws > 0 && (primary.sourceUv - Vector2.one * 0.5f).sqrMagnitude < 0.01f, "The primary camera flare projection is independent of the second camera.");
            }
            finally
            {
                CoreUtils.Destroy(secondObject);
            }
            checks.Add("Native Fog in resized TAA and interleaved cameras, independent flare projection");
        }

        private static void CheckMoon(CaptureContext context, XRayWeatherProfile profile, string label, string definition, float raw, List<string> checks)
        {
            SetWeather(context, profile, 0.0);
            ExpectSource(context, raw, definition);
            Color[] off = Capture(context, label + "_Off", false, false);
            Color[] shafts = Capture(context, label + "_Shafts", true, false);
            RequireNative(context.Frames[context.Frames.Count - 1]);
            Color[] flare = Capture(context, label + "_Disk", false, true);
            Color[] both = Capture(context, label + "_Both", true, true);
            XRayWeatherFlareDefinition active = context.Controller.Celestial.FlareDefinition;
            Require(active != null && active.Source.Texture != null && active.Source.IgnoreColor && active.Gradient.Texture == null && active.Elements.Count == 0, "The source moon definition has one white disk and no ghosts or gradient.");
            var deltas = new EffectDeltaRecord { shafts = Difference(off, shafts), flare = Difference(off, flare), both = Difference(off, both), addFlare = Difference(shafts, both), addShafts = Difference(flare, both) };
            Write(context.Output, label + "_ABMetrics", deltas);
            Require(deltas.flare > VISIBLE_DELTA && deltas.addFlare > VISIBLE_DELTA, "The source moon projects its disk independently of native Fog.");
            checks.Add(label + " native shafts and one disk");
        }

        private static void CheckTransition(CaptureContext context, XRayWeatherProfile from, XRayWeatherProfile to, string label, float first, float last, string firstDefinition, string lastDefinition)
        {
            SetWeather(context, from, 43200.0);
            XRayWeatherController controller = context.Controller;
            controller.SetTimeScale(0.0f);
            controller.SetPaused(false);
            Require(controller.TransitionTo(to, 1.0f), "The source weather cycles share one sun position table.");
            for (int i = 0; i <= 4; i++)
            {
                if (i > 0) controller.TickSimulation(0.25f);
                float weight = i * 0.25f;
                float raw = first * (1.0f - weight) + last * weight;
                ExpectSource(context, raw, i < 2 ? firstDefinition : lastDefinition);
                Capture(context, label + "_" + i, true, true, warmup: 1);
            }
            Require(controller.ActiveProfile == to && !controller.IsTransitioning, "The transition commits the target profile and retains ownership.");
            controller.SetPaused(true);
        }

        private static void CheckMidnight(CaptureContext context, XRayWeatherProfile clear)
        {
            SetWeather(context, clear, 86399.0);
            XRayWeatherController controller = context.Controller;
            controller.SetTimeScale(1.0f);
            controller.TickSimulation(1.0f);
            Require(controller.GameTimeSeconds == 86399.0, "Pause stops the weather clock.");
            Capture(context, "Midnight_86399", true, true, warmup: 1);
            controller.SetPaused(false);
            for (int i = 0; i <= 1; i++)
            {
                controller.TickSimulation(1.0f);
                Require(controller.GameTimeSeconds == i, "The single weather clock wraps midnight.");
                ExpectSource(context, 0.32f, "moon_full_clear");
                Capture(context, "Midnight_" + i, true, true, warmup: 1);
            }
            controller.SetPaused(true);
        }

        private static void SetWeather(CaptureContext context, XRayWeatherProfile profile, double seconds)
        {
            context.Controller.SetPaused(true);
            context.Controller.SetProfile(profile);
            context.Controller.Seek(seconds);
            context.Controller.SetCelestialEffectOverrides(true, true);
            Vector3 direction = -context.Controller.Snapshot.SunDirection;
            Vector3 up = Mathf.Abs(direction.y) > 0.99f ? Vector3.forward : Vector3.up;
            context.Camera.transform.rotation = Quaternion.LookRotation(direction, up);
        }

        private static void ExpectSource(CaptureContext context, float raw, string definition)
        {
            XRayWeatherController controller = context.Controller;
            float effective = Mathf.Clamp01((raw * (1.0f - context.Settings.ShaftsMin) + context.Settings.ShaftsMin) * context.Settings.ShaftsValue);
            Require(Mathf.Abs(controller.Snapshot.SunShaftsIntensity - raw) < TOLERANCE && Mathf.Abs(controller.Celestial.ShaftsIntensity - effective) < TOLERANCE && controller.Snapshot.SunDefinition == definition, "Raw intensity, one effective transform and selected definition match the source at " + controller.GameTimeSeconds + ".");
        }

        private static Color[] Capture(CaptureContext context, string name, bool shafts, bool flare, int width = WIDTH, int height = HEIGHT, int warmup = 8)
        {
            context.Controller.SetCelestialEffectOverrides(shafts, flare);
            return CaptureFrame(context, name, width, height, warmup);
        }

        private static Color[] CaptureFrame(CaptureContext context, string name, int width, int height, int warmup)
        {
            Color[] pixels = XRayWeatherFogValidationProbe.Capture(context.Camera, Path.Combine(context.Output, name), width, height, warmup);
            FrameRecord frame = ReadFrame(context, name, width, height, warmup);
            context.Frames.Add(frame);
            Write(context.Output, name + "_State", frame);
            Require(frame.originalMask && frame.weatherOwned && frame.fogEnabled && frame.skyType == SkySettings.GetUniqueID<XRayWeatherSkySettings>() && frame.sunDirectionMatches, "Camera mask, ownership, native Fog, source Sky and Sun direction persist in " + name + ".");
            Require(frame.celestialLayer < 0 || (frame.cameraMask & (1 << frame.celestialLayer)) != 0, "The celestial pass uses an original camera Volume Mask layer in " + name + ".");
            Require(!frame.shaftsDrawn && (frame.shaftsEnabled || !frame.volumetricFog) && (frame.flareEnabled || frame.flareDraws == 0), "The screen shafts remain inactive and each disabled switch suppresses its own effect in " + name + ".");
            return pixels;
        }

        private static void RequireNative(FrameRecord frame)
        {
            Require(frame.volumetrics && frame.volumetricFog && frame.volumetricDimmer > 0.0f && frame.volumetricShadowDimmer > 0.0f, "HDRP consumes directional volumetric lighting and shadows in " + frame.label + ".");
        }

        private static FrameRecord ReadFrame(CaptureContext context, string name, int width, int height, int warmup)
        {
            XRayWeatherController controller = context.Controller;
            XRayWeatherCelestialRenderer celestial = controller.Celestial;
            CustomPassVolume volume = celestial?.Volume;
            XRayWeatherCelestialPass pass = volume != null && volume.customPasses.Count > 0 ? volume.customPasses[0] as XRayWeatherCelestialPass : null;
            HDCamera camera = HDCamera.GetOrCreate(context.Camera);
            VolumeStack stack = camera.volumeStack;
            Fog fog = stack?.GetComponent<Fog>();
            VisualEnvironment sky = stack?.GetComponent<VisualEnvironment>();
            Exposure exposure = stack?.GetComponent<Exposure>();
            bool observed = pass != null && pass.LastCamera == context.Camera;
            return new FrameRecord { label = name, width = width, height = height, warmup = warmup, time = controller.GameTimeSeconds, camera = context.Camera.name, pose = context.Camera.transform.rotation, aa = context.Camera.GetComponent<HDAdditionalCameraData>().antialiasing.ToString(), profile = controller.ActiveProfile.CycleName, definition = controller.Snapshot.SunDefinition, renderedDefinition = celestial?.FlareDefinition?.Name, flareFade = celestial?.FlareFade ?? 0.0f, sourceUv = observed ? pass.LastSourceUv : default, sourceRgb = controller.Snapshot.SunColor, raw = controller.Snapshot.SunShaftsIntensity, effective = celestial?.ShaftsIntensity ?? 0.0f, shaftsDrawn = observed && pass.LastShaftsDrawn, flareDraws = observed ? pass.LastFlareDraws : 0, cameraObserved = observed, shaftsEnabled = controller.SunShaftsEnabled, flareEnabled = controller.LensFlareEnabled, originalMask = context.Camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value == context.Mask, cameraMask = context.Mask, celestialLayer = volume == null ? -1 : volume.gameObject.layer, fogEnabled = fog != null && fog.enabled.value && fog.colorMode.value == FogColorMode.SkyColor, fogMeanFreePath = fog?.meanFreePath.value ?? 0.0f, volumetricFog = fog != null && fog.enableVolumetricFog.value, volumetrics = camera.frameSettings.IsEnabled(FrameSettingsField.Volumetrics), volumetricDimmer = context.SunData != null ? context.SunData.volumetricDimmer : 0.0f, volumetricShadowDimmer = context.SunData != null ? context.SunData.volumetricShadowDimmer : 0.0f, skyType = sky?.skyType.value ?? -1, fixedExposure = exposure?.fixedExposure.value ?? 0.0f, exposureMode = exposure?.mode.value.ToString(), weatherOwned = XRayWeatherController.Active == controller, sunDirectionMatches = context.Sun == null || Vector3.Dot(context.Sun.transform.forward, controller.Snapshot.SunDirection) > 0.99999f, boundScale = Shader.GetGlobalFloat("_MonolithBoundRadianceScale") };
        }

        private static void PlaceBlocker(Camera camera, GameObject blocker, bool full)
        {
            const float DISTANCE = 8.0f;
            float halfHeight = DISTANCE * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * WIDTH / HEIGHT;
            Vector3 center = camera.transform.position + camera.transform.forward * DISTANCE;
            if (!full) center += camera.transform.right * (0.6f * halfWidth);
            blocker.transform.SetPositionAndRotation(center, camera.transform.rotation);
            blocker.transform.localScale = new Vector3((full ? 2.4f : 1.2f) * halfWidth, 2.4f * halfHeight, 1.0f);
            blocker.SetActive(true);
        }

        private static void Measure(CaptureContext context)
        {
            context.Controller.SetCelestialEffectOverrides(false, false);
            double off = RenderTimed(context.Camera, 12);
            context.Controller.SetCelestialEffectOverrides(true, true);
            double on = RenderTimed(context.Camera, 12);
            var timings = new FrameTiming[4];
            FrameTimingManager.CaptureFrameTimings();
            uint available = FrameTimingManager.GetLatestTimings(4, timings);
            Write(context.Output, "Performance", new PerformanceRecord { width = WIDTH, height = HEIGHT, rendersPerMode = 12, offCpuWallMilliseconds = off, onCpuWallMilliseconds = on, differenceCpuWallMilliseconds = on - off, frameTimingSamplesAvailable = available, latestUnattributedGpuMilliseconds = available > 0 ? timings[0].gpuFrameTime : 0.0, gpuAttributableToManualRender = false });
        }

        private static double RenderTimed(Camera camera, int count)
        {
            RenderTexture original = camera.targetTexture;
            RenderTexture active = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try
            {
                camera.targetTexture = target;
                for (int i = 0; i < 3; i++) camera.Render();
                var timer = Stopwatch.StartNew();
                for (int i = 0; i < count; i++) camera.Render();
                return timer.Elapsed.TotalMilliseconds / count;
            }
            finally
            {
                camera.targetTexture = original;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static double Difference(Color[] a, Color[] b)
        {
            Require(a.Length == b.Length && a.Length > 0, "A/B images must have identical dimensions.");
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return sum / (a.Length * 3.0);
        }

        private static double ChangedFraction(Color[] a, Color[] b)
        {
            int changed = 0;
            for (int i = 0; i < a.Length; i++) if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 0.001f) changed++;
            return (double)changed / a.Length;
        }

        private static void PrepareOutput(string directory)
        {
            Require(Path.GetFullPath(directory).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase), "Celestial validation evidence must remain in the project Archive.");
            Require(!File.Exists(Path.Combine(directory, "Result.json")), "Existing celestial evidence cannot be overwritten.");
            Directory.CreateDirectory(directory);
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
        }

        private static void Write(string directory, string name, object value)
        {
            File.WriteAllText(Path.Combine(directory, name + ".json"), JsonUtility.ToJson(value, true));
        }

        private static string ProfileJson(VolumeProfile profile)
        {
            if (profile == null) return null;
            var result = new System.Text.StringBuilder(JsonUtility.ToJson(profile));
            foreach (VolumeComponent component in profile.components) if (component != null) result.Append(JsonUtility.ToJson(component));
            return result.ToString();
        }

        private static string ConfigurationJson(GameConfig configuration)
        {
            var result = new System.Text.StringBuilder(JsonUtility.ToJson(configuration));
            result.Append(JsonUtility.ToJson(configuration.CelestialSettings));
            result.Append(JsonUtility.ToJson(configuration.OrdinaryWeatherCatalog));
            result.Append(JsonUtility.ToJson(configuration.WeatherFxCatalog));
            foreach (XRayWeatherProfile profile in configuration.OrdinaryWeatherCatalog.Profiles) result.Append(JsonUtility.ToJson(profile));
            foreach (XRayWeatherFxProfile effect in configuration.WeatherFxCatalog.Effects)
            {
                result.Append(JsonUtility.ToJson(effect));
                result.Append(JsonUtility.ToJson(effect.SourceProfile));
            }
            return result.ToString();
        }

        private void ObserveLog(string text, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) _runtimeErrors.Add(text + "\n" + trace);
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            Application.logMessageReceived -= ObserveLog;
            _instance = null;
        }

        private sealed class CaptureContext
        {
            public Camera Camera { get; }
            public XRayWeatherController Controller { get; }
            public string Output { get; }
            public List<FrameRecord> Frames { get; }
            public Light Sun { get; }
            public HDAdditionalLightData SunData { get; }
            public XRayWeatherCelestialSettings Settings { get; }
            public int Mask { get; }

            public CaptureContext(Camera camera, XRayWeatherController controller, string output, List<FrameRecord> frames, Light sun)
            {
                Camera = camera;
                Controller = controller;
                Output = output;
                Frames = frames;
                Sun = sun;
                SunData = sun != null ? sun.GetComponent<HDAdditionalLightData>() : null;
                Settings = Resources.Load<XRayWeatherCelestialSettings>(XRayWeatherCelestialSettings.RESOURCE_PATH);
                Mask = camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value;
            }
        }

        private readonly struct SavedState
        {
            private readonly XRayWeatherProfile _profile;
            private readonly double _time;
            private readonly bool _paused;
            private readonly float _timeScale;
            private readonly bool? _shafts;
            private readonly bool? _flare;
            private readonly Quaternion _rotation;
            private readonly RenderTexture _target;
            private readonly RenderTexture _active;
            private readonly HDAdditionalCameraData.AntialiasingMode _aa;
            private readonly FrameSettings _frameSettings;
            private readonly FrameSettingsOverrideMask _frameMask;
            private readonly XRayWeatherFogRenderer _fog;
            private readonly Volume _fogVolume;
            private readonly VolumeProfile _authoredFog;
            private readonly string _authoredFogJson;
            private readonly Color _sunColor;
            private readonly float _sunIntensity;
            private readonly Quaternion _sunRotation;
            private readonly LightShadows _sunShadows;
            private readonly float _volumetricDimmer;
            private readonly float _volumetricShadowDimmer;
            private readonly BlockerState _opaque;
            private readonly BlockerState _alpha;

            public SavedState(CaptureContext context, GameObject opaque, GameObject alpha)
            {
                XRayWeatherController controller = context.Controller;
                _profile = controller.ActiveProfile;
                _time = controller.GameTimeSeconds;
                _paused = controller.Paused;
                _timeScale = controller.TimeScale;
                _shafts = controller.SunShaftsOverride;
                _flare = controller.LensFlareOverride;
                _rotation = context.Camera.transform.rotation;
                _target = context.Camera.targetTexture;
                _active = RenderTexture.active;
                HDAdditionalCameraData data = context.Camera.GetComponent<HDAdditionalCameraData>();
                _aa = data.antialiasing;
                _frameSettings = data.renderingPathCustomFrameSettings;
                _frameMask = data.renderingPathCustomFrameSettingsOverrideMask;
                _fog = controller.GetComponent<XRayWeatherFogRenderer>();
                _fogVolume = _fog != null ? _fog.Volume : null;
                _authoredFog = _fogVolume != null ? _fogVolume.sharedProfile : null;
                _authoredFogJson = ProfileJson(_authoredFog);
                _sunColor = context.Sun != null ? context.Sun.color : default;
                _sunIntensity = context.Sun != null ? context.Sun.intensity : 0.0f;
                _sunRotation = context.Sun != null ? context.Sun.transform.rotation : default;
                _sunShadows = context.Sun != null ? context.Sun.shadows : default;
                _volumetricDimmer = context.SunData != null ? context.SunData.volumetricDimmer : 0.0f;
                _volumetricShadowDimmer = context.SunData != null ? context.SunData.volumetricShadowDimmer : 0.0f;
                _opaque = new BlockerState(opaque);
                _alpha = new BlockerState(alpha);
            }

            public void Restore(CaptureContext context)
            {
                _opaque.Restore();
                _alpha.Restore();
                context.Camera.transform.rotation = _rotation;
                HDAdditionalCameraData data = context.Camera.GetComponent<HDAdditionalCameraData>();
                data.antialiasing = _aa;
                data.renderingPathCustomFrameSettings = _frameSettings;
                data.renderingPathCustomFrameSettingsOverrideMask = _frameMask;
                context.Camera.targetTexture = _target;
                RenderTexture.active = _active;
                XRayWeatherController controller = context.Controller;
                controller.SetProfile(_profile);
                controller.SetTimeScale(_timeScale);
                controller.SetPaused(_paused);
                controller.Seek(_time);
                controller.SetCelestialEffectOverrides(_shafts, _flare);
            }

            public bool Matches(CaptureContext context)
            {
                XRayWeatherController controller = context.Controller;
                bool weather = XRayWeatherController.Active == controller && controller.ActiveProfile == _profile && controller.GameTimeSeconds == _time && controller.Paused == _paused && controller.TimeScale == _timeScale && controller.SunShaftsOverride == _shafts && controller.LensFlareOverride == _flare;
                HDAdditionalCameraData data = context.Camera.GetComponent<HDAdditionalCameraData>();
                bool camera = context.Camera.transform.rotation == _rotation && context.Camera.targetTexture == _target && RenderTexture.active == _active && data.antialiasing == _aa && data.volumeLayerMask.value == context.Mask && data.renderingPathCustomFrameSettings.Equals(_frameSettings) && data.renderingPathCustomFrameSettingsOverrideMask.Equals(_frameMask);
                bool fog = controller.GetComponent<XRayWeatherFogRenderer>() == _fog && (_fogVolume == null || _fog.Volume == _fogVolume && _fogVolume.sharedProfile == _authoredFog && ProfileJson(_authoredFog) == _authoredFogJson);
                bool sun = context.Sun == null || context.Sun.color == _sunColor && context.Sun.intensity == _sunIntensity && context.Sun.transform.rotation == _sunRotation && context.Sun.shadows == _sunShadows;
                bool sunVolumetrics = context.SunData == null || context.SunData.volumetricDimmer == _volumetricDimmer && context.SunData.volumetricShadowDimmer == _volumetricShadowDimmer;
                return weather && camera && fog && sun && sunVolumetrics && _opaque.Matches() && _alpha.Matches();
            }
        }

        private readonly struct BlockerState
        {
            private readonly GameObject _blocker;
            private readonly bool _active;
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly Vector3 _scale;

            public BlockerState(GameObject blocker)
            {
                _blocker = blocker;
                _active = blocker.activeSelf;
                _position = blocker.transform.localPosition;
                _rotation = blocker.transform.localRotation;
                _scale = blocker.transform.localScale;
            }

            public void Restore()
            {
                _blocker.transform.SetLocalPositionAndRotation(_position, _rotation);
                _blocker.transform.localScale = _scale;
                _blocker.SetActive(_active);
            }

            public bool Matches()
            {
                return _blocker.activeSelf == _active && _blocker.transform.localPosition == _position && _blocker.transform.localRotation == _rotation && _blocker.transform.localScale == _scale;
            }
        }

        [Serializable]
        private sealed class FrameRecord
        {
            public string label;
            public int width;
            public int height;
            public int warmup;
            public double time;
            public string camera;
            public Quaternion pose;
            public string aa;
            public string profile;
            public string definition;
            public string renderedDefinition;
            public float flareFade;
            public Vector2 sourceUv;
            public Color sourceRgb;
            public float raw;
            public float effective;
            public bool shaftsDrawn;
            public int flareDraws;
            public bool cameraObserved;
            public bool shaftsEnabled;
            public bool flareEnabled;
            public bool originalMask;
            public int cameraMask;
            public int celestialLayer;
            public bool fogEnabled;
            public float fogMeanFreePath;
            public bool volumetricFog;
            public bool volumetrics;
            public float volumetricDimmer;
            public float volumetricShadowDimmer;
            public int skyType;
            public float fixedExposure;
            public string exposureMode;
            public bool weatherOwned;
            public bool sunDirectionMatches;
            public float boundScale;
        }

        [Serializable]
        private sealed class ResultRecord
        {
            public bool passed;
            public bool captureWritten;
            public bool assertionsPassed;
            public bool restorationPassed;
            public int captures;
            public string error;
            public string restorationError;
            public FrameRecord[] frames;
            public string[] checks;
        }

        [Serializable]
        private sealed class StageRecord
        {
            public bool passed;
            public bool expectedFallback;
            public bool runtimeFallback;
            public bool captureWritten;
            public bool assertionsPassed;
            public bool restorationPassed;
            public bool released;
            public bool reacquired;
            public bool sharedConfiguration;
            public bool configurationInherited;
            public float inheritedTimeScale;
            public bool sharedCatalogs;
            public int ordinaryCycles;
            public int weatherFxEffects;
            public bool sharedGraphics;
            public bool configurationReadOnly;
            public string error;
        }

        [Serializable]
        private sealed class CatalogRecord
        {
            public string[] profiles;
            public int keys;
            public int zeroSun;
            public string[] transitions;
        }

        [Serializable]
        private sealed class PlayerRecord
        {
            public bool passed;
            public bool initial;
            public bool reload;
            public string sourceSha256;
            public string api;
            public string[] runtimeErrors;
        }

        [Serializable]
        private sealed class SystemRecord
        {
            public string unity;
            public string api;
            public string gpu;
            public string sourceSha256;
        }

        [Serializable]
        private sealed class OccluderRecord
        {
            public double openFlareDelta;
            public double partialFlareDelta;
            public double fullFlareDelta;
            public double alphaFlareDelta;
            public double alphaCoverage;
        }

        [Serializable]
        private sealed class NativeShaftsRecord
        {
            public float sourceForward;
            public float volumetricDimmer;
            public float volumetricShadowDimmer;
            public double zeroRepeatNoise;
            public double meanLightIncrease;
        }

        [Serializable]
        private sealed class EffectDeltaRecord
        {
            public double shafts;
            public double flare;
            public double both;
            public double addFlare;
            public double addShafts;
        }

        [Serializable]
        private sealed class MotionRecord
        {
            public float[] angles;
            public Vector2 nearEdgeUv;
            public double behindFirstFrameDelta;
            public double behindSettledDelta;
            public double offFirstVsSettledDelta;
            public double returnFirstFrameDelta;
            public bool motionBlurFrameSetting;
            public float motionBlurIntensity;
            public bool cameraMotionBlur;
        }

        [Serializable]
        private sealed class PerformanceRecord
        {
            public int width;
            public int height;
            public int rendersPerMode;
            public double offCpuWallMilliseconds;
            public double onCpuWallMilliseconds;
            public double differenceCpuWallMilliseconds;
            public uint frameTimingSamplesAvailable;
            public double latestUnattributedGpuMilliseconds;
            public bool gpuAttributableToManualRender;
        }
    }
}
