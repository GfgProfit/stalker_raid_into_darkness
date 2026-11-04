using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public sealed class XRayWeatherFogValidationProbe : MonoBehaviour
    {
        private const int WIDTH = 640;
        private const int HEIGHT = 360;
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\WeatherCompletion_20261104_024544\";

        private static XRayWeatherFogValidationProbe _instance;

        [SerializeField] private Camera _camera;
        [SerializeField] private XRayWeatherController _controller;
        [SerializeField] private XRayWeatherProfile _clear;
        [SerializeField] private XRayWeatherProfile _rain;
        [SerializeField] private Volume _volume;
        [SerializeField] private string _sourceSha256;
        [SerializeField] private Light _sun;
        [SerializeField] private MonolithSourceLighting _sourceLighting;
        [SerializeField] private Volume _skyVolume;
        [SerializeField] private Color _authoredSunColor;
        [SerializeField] private float _authoredSunIntensity;
        [SerializeField] private Quaternion _authoredSunRotation;
        [SerializeField] private float _authoredLightingScale;
        [SerializeField] private int _cameraVolumeMask;

        private string _output;
        private bool _failed;

        public void Configure(Camera camera, XRayWeatherController controller, XRayWeatherProfile clear, XRayWeatherProfile rain, Volume volume, string sourceSha256)
        {
            _camera = camera;
            _controller = controller;
            _clear = clear;
            _rain = rain;
            _volume = volume;
            _sourceSha256 = sourceSha256;
            _cameraVolumeMask = camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value;
        }

        public void ConfigureEnvironment(Light sun, MonolithSourceLighting sourceLighting, Volume skyVolume)
        {
            _sun = sun;
            _sourceLighting = sourceLighting;
            _skyVolume = skyVolume;
            _authoredSunColor = sun.color;
            _authoredSunIntensity = sun.intensity;
            _authoredSunRotation = sun.transform.rotation;
            _authoredLightingScale = sourceLighting.BoundRadianceScale;
        }

        private void Awake()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++) if (arguments[i] == "-weatherFogEvidence") _output = arguments[i + 1];
            if (string.IsNullOrEmpty(_output))
            {
                enabled = false;
                return;
            }
            if (_instance != null && _instance != this)
            {
                _instance.Configure(_camera, _controller, _clear, _rain, _volume, _sourceSha256);
                _instance._sun = _sun;
                _instance._sourceLighting = _sourceLighting;
                _instance._skyVolume = _skyVolume;
                _instance._authoredSunColor = _authoredSunColor;
                _instance._authoredSunIntensity = _authoredSunIntensity;
                _instance._authoredSunRotation = _authoredSunRotation;
                _instance._authoredLightingScale = _authoredLightingScale;
                _instance._cameraVolumeMask = _cameraVolumeMask;
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (!Path.GetFullPath(_output).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Native fog evidence must use a new directory under the task Archive.");
            if (File.Exists(Path.Combine(_output, "System.json")) || File.Exists(Path.Combine(_output, "Result.json"))) throw new IOException("Native fog evidence must not overwrite a previous run.");
            Directory.CreateDirectory(_output);
            DontDestroyOnLoad(gameObject);
        }

        private IEnumerator Start()
        {
            if (_instance != this || string.IsNullOrEmpty(_output)) yield break;
            Application.logMessageReceived += ObserveLog;
            File.WriteAllText(Path.Combine(_output, "System.json"), JsonUtility.ToJson(new SystemRecord { unity = Application.unityVersion, api = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName, sourceSha256 = _sourceSha256 }, true));
            for (int i = 0; i < 8; i++) yield return null;
            XRayWeatherFogRenderer serializedFog = _controller.GetComponent<XRayWeatherFogRenderer>();
            bool hadSerializedConsumer = serializedFog != null && serializedFog.Volume != null && serializedFog.Volume.gameObject.hideFlags != HideFlags.HideAndDontSave;
            _controller.enabled = false;
            Destroy(serializedFog);
            yield return null;
            try
            {
                Require(hadSerializedConsumer && serializedFog == null, "The serialized Fog consumer was removed before testing legacy auto-connection");
                _controller.enabled = true;
                XRayWeatherFogRenderer automaticFog = _controller.GetComponent<XRayWeatherFogRenderer>();
                Require(automaticFog != null && automaticFog != serializedFog && automaticFog.HasWeather && automaticFog.Volume != null, "Legacy weather controller automatically acquires a new native fog consumer");
                Require(automaticFog.Volume.gameObject.hideFlags == HideFlags.HideAndDontSave && automaticFog.Volume.profile.TryGet(out Fog nativeFog) && nativeFog.enabled.value, "Automatic legacy connection uses native HDRP Fog in a transient Volume");
                HDAdditionalCameraData cameraData = _camera.GetComponent<HDAdditionalCameraData>();
                Require(cameraData.volumeLayerMask.value == _cameraVolumeMask && (_cameraVolumeMask & (1 << automaticFog.Volume.gameObject.layer)) != 0, "Automatic fog uses the camera's original Volume mask");
                Require((_cameraVolumeMask & (1 << _controller.gameObject.layer)) == 0, "Legacy owner layer is excluded from the camera Volume mask");
                File.WriteAllText(Path.Combine(_output, "LegacyAutoConnection.json"), JsonUtility.ToJson(new ConnectionRecord { passed = true, serializedConsumerRemoved = true, runtimeNativeFog = true, cameraVolumeMask = cameraData.volumeLayerMask.value, controllerLayer = _controller.gameObject.layer, consumerLayer = automaticFog.Volume.gameObject.layer }, true));
            }
            catch (Exception exception)
            {
                _failed = true;
                File.WriteAllText(Path.Combine(_output, "LegacyAutoConnection.json"), "{\"passed\":false}");
                File.WriteAllText(Path.Combine(_output, "LegacyAutoConnection_Failure.txt"), exception.ToString());
            }
            bool initial = !_failed && RunPlayerStage("Initial", true);
            string scene = _controller.gameObject.scene.name;
            yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            for (int i = 0; i < 8; i++) yield return null;
            bool reload = RunPlayerStage("Reload", false);
            bool passed = initial && reload && !_failed;
            File.WriteAllText(Path.Combine(_output, "Result.json"), JsonUtility.ToJson(new ResultRecord { passed = passed, initial = initial, reload = reload, sourceSha256 = _sourceSha256, api = SystemInfo.graphicsDeviceType.ToString() }, true));
            Application.logMessageReceived -= ObserveLog;
            Application.Quit(passed ? 0 : 1);
        }

        private bool RunPlayerStage(string stage, bool fallback)
        {
            string directory = Path.Combine(_output, stage);
            Directory.CreateDirectory(directory);
            try
            {
                Require(_controller != null && _camera != null && _volume != null && _sun != null && _sourceLighting != null && _skyVolume != null, "Serialized fixture references survive " + stage);
                Require(_camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value == _cameraVolumeMask, "The original camera Volume mask survives " + stage);
                _controller.SetPaused(true);
                _controller.SetTimeScale(60.0f);
                _controller.Seek(86390.0);
                _controller.TickSimulation(1.0f);
                Require(Math.Abs(_controller.GameTimeSeconds - 86390.0) < 0.001, "Paused clock remains fixed");
                _controller.SetPaused(false);
                _controller.TickSimulation(1.0f);
                _controller.SetPaused(true);
                Require(Math.Abs(_controller.GameTimeSeconds - 50.0) < 0.001, "Live clock wraps midnight");
                XRayWeatherFogRenderer fog = _controller.GetComponent<XRayWeatherFogRenderer>();
                Require(fog != null && fog.HasWeather && fog.Volume != null, "The stage has an owned native fog consumer");
                Require((fog.Volume.gameObject.hideFlags == HideFlags.HideAndDontSave) == fallback, "Initial uses the runtime fallback and reload uses the serialized consumer");
                Volume serializedVolume = fallback ? null : fog.Volume;
                VolumeProfile serializedProfile = serializedVolume != null ? serializedVolume.sharedProfile : null;
                string authoredFog = serializedProfile != null ? ProfileState(serializedProfile) : null;
                VolumeProfile skyProfile = _skyVolume.sharedProfile;
                string authoredSky = ProfileState(skyProfile);
                LightShadows authoredShadows = _sun.shadows;
                _controller.SetProfile(_clear);
                _controller.Seek(43200.0);
                RunSweep(_camera, fog, _controller, _volume, directory, _skyVolume);
                foreach (XRayWeatherProfile profile in new[] { _clear, _rain })
                {
                    _controller.SetProfile(profile);
                    foreach (double hour in new[] { 0.0, 6.0, 12.0, 20.0 })
                    {
                        _controller.Seek(hour * 3600.0);
                        Require(fog.HasWeather, "Weather controller owns fog for " + profile.CycleName);
                        string stem = Path.Combine(directory, profile.CycleName + "_" + hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                        WriteWeatherSample(stem, fog);
                        Capture(_camera, stem);
                    }
                }
                _controller.SetProfile(_clear);
                _controller.SetTimeScale(60.0f);
                _controller.SetPaused(false);
                Require(_controller.TransitionTo(_rain, 1.0f), "Clear/rain transition starts");
                for (int i = 0; i < 5; i++)
                {
                    _controller.TickSimulation(0.25f);
                    Require(fog.HasWeather, "Moving weather transition retains fog ownership");
                    string stem = Path.Combine(directory, "Transition_" + i);
                    WriteWeatherSample(stem, fog);
                    Capture(_camera, stem);
                }
                _controller.SetPaused(true);
                _controller.enabled = false;
                Require(!fog.HasWeather, "Controller disable releases fog");
                Require(fog.Volume == null || !fog.Volume.HasInstantiatedProfile(), "Controller release restores the authored Fog profile");
                Require(!_skyVolume.HasInstantiatedProfile(), "Controller release restores the authored Sky profile");
                Require(_skyVolume.sharedProfile == skyProfile && ProfileState(skyProfile) == authoredSky, "The authored Sky profile and serialized parameters remain unchanged");
                Require(serializedVolume == null || serializedVolume.sharedProfile == serializedProfile && ProfileState(serializedProfile) == authoredFog, "The authored Fog profile and serialized parameters remain unchanged");
                Require(_sun.color == _authoredSunColor && _sun.intensity == _authoredSunIntensity && _sun.transform.rotation == _authoredSunRotation, "Controller release restores the authored physical Sun");
                Require(_sun.shadows == authoredShadows, "Weather validation preserves Sun shadow settings");
                Require(_sourceLighting.BoundRadianceScale == _authoredLightingScale && _sourceLighting.EffectiveBoundRadianceScale == _authoredLightingScale, "Controller release restores authored Bound lighting units");
                _controller.enabled = true;
                Require(fog.HasWeather, "Controller enable reacquires fog");
                WriteWeatherSample(Path.Combine(directory, "Reacquired"), fog);
                File.WriteAllText(Path.Combine(directory, "Result.json"), JsonUtility.ToJson(new StageRecord { passed = true, runtimeFallback = fallback, serializedConsumer = !fallback, ownershipReleased = true, authoredStateRestored = true, ownershipReacquired = true, cameraVolumeMaskUnchanged = _camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value == _cameraVolumeMask }, true));
                return true;
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(directory, "Failure.txt"), exception.ToString());
                return false;
            }
        }

        public static string RunSweep(Camera camera, XRayWeatherFogRenderer fog, UnityEngine.Object owner, Volume volume, string directory, Volume weatherSky = null)
        {
            Require(Path.GetFullPath(directory).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase), "Fog sweep evidence must remain inside the task Archive");
            Directory.CreateDirectory(directory);
            Fog hdrpFog = null;
            Require(volume != null && ActiveProfile(volume) != null && ActiveProfile(volume).TryGet(out hdrpFog), "The independent environment has an authored Fog baseline");
            string authored = JsonUtility.ToJson(hdrpFog);
            float far = camera.farClipPlane;
            int mask = camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value;
            Fog diagnosticFog = null;
            float savedSkyMip = 0.0f;
            bool savedSkyMipOverride = false;
            VisualEnvironment diagnosticSky = null;
            string savedSky = null;
            XRayWeatherSnapshot snapshot = Snapshot(0.5f, 400.0f, new Color(0.25f, 0.35f, 0.45f, 1.0f));
            try
            {
                if (weatherSky != null)
                {
                    Require(weatherSky.HasInstantiatedProfile() && weatherSky.profile.TryGet(out diagnosticSky), "The owned weather Sky has a runtime VisualEnvironment");
                    savedSky = JsonUtility.ToJson(diagnosticSky);
                    diagnosticSky.skyType.overrideState = false;
                }
                Require(XRayWeatherFogMath.TryGetParameters(snapshot, out Vector4 parameters), "Source fog parameters are finite");
                Require(XRayWeatherFogMath.TryGetMeanFreePath(snapshot, out float meanFreePath), "Native fog attenuation distance is finite");
                fog.ReleaseWeather(owner);
                hdrpFog.enabled.Override(false);
                Color[] baseline = Capture(camera, Path.Combine(directory, "KnownDistances_Off"));
                Require(HDCamera.GetOrCreate(camera).volumeStack.GetComponent<VisualEnvironment>().skyType.value == SkySettings.GetUniqueID<GradientSky>(), "The attenuation shape control uses the independent uniform Gradient Sky");
                Require(fog.TrySetWeather(owner, snapshot), "Synthetic fog owner acquires renderer");
                Volume consumer = fog.Volume;
                Fog controlledFog = null;
                Require(consumer != null && consumer.HasInstantiatedProfile() && consumer.profile.TryGet(out controlledFog) && controlledFog.enabled.value && controlledFog.colorMode.value == FogColorMode.SkyColor, "The actual weather consumer uses the native sky radiance source");
                Require((mask & (1 << consumer.gameObject.layer)) != 0, "The actual Fog consumer is visible through the original camera Volume mask");
                Require(consumer == volume || consumer.profile != ActiveProfile(volume), "Dedicated Fog and environment profiles remain distinct");
                diagnosticFog = controlledFog;
                savedSkyMip = diagnosticFog.mipFogMaxMip.value;
                savedSkyMipOverride = diagnosticFog.mipFogMaxMip.overrideState;
                diagnosticFog.mipFogMaxMip.Override(0.0f);
                Color[] enabled = Capture(camera, Path.Combine(directory, "KnownDistances_On"));
                Require(Mathf.Abs(baseline[0].r - enabled[0].r) + Mathf.Abs(baseline[0].g - enabled[0].g) + Mathf.Abs(baseline[0].b - enabled[0].b) < 0.0001f, "Fog preserves pixels without geometry depth");
                hdrpFog.enabled.Override(true);
                Color[] stockEnabled = Capture(camera, Path.Combine(directory, "KnownDistances_StockEnabled"));
                double stockDifference = MeanDifference(enabled, stockEnabled);
                Require(stockDifference < 0.0005, "The controlled native Fog retains one effective Volume stack contribution");
                hdrpFog.enabled.Override(false);
                Vector3[] points = { new Vector3(-5.2f, 1.7f, 30.0f), new Vector3(-1.7f, 1.7f, 180.0f), new Vector3(1.7f, 1.7f, 280.0f), new Vector3(5.2f, 1.7f, 500.0f) };
                var samples = new List<DistanceRecord>();
                Color farColor = Pixel(camera, enabled, points[3]);
                Color farBaseline = Pixel(camera, baseline, points[3]);
                float farDistance = camera.orthographic ? Vector3.Dot(points[3] - camera.transform.position, camera.transform.forward) : Vector3.Distance(camera.transform.position, points[3]);
                float farFactor = 1.0f - Mathf.Exp(-farDistance / meanFreePath);
                Color fittedFog = (farColor - farBaseline * (1.0f - farFactor)) / farFactor;
                Color effectiveFog = fittedFog;
                for (int i = 0; i < points.Length; i++)
                {
                    float radialDistance = Vector3.Distance(camera.transform.position, points[i]);
                    float distance = camera.orthographic ? Vector3.Dot(points[i] - camera.transform.position, camera.transform.forward) : radialDistance;
                    float factor = 1.0f - Mathf.Exp(-distance / meanFreePath);
                    Color off = Pixel(camera, baseline, points[i]);
                    Color on = Pixel(camera, enabled, points[i]);
                    Color expected = Color.LerpUnclamped(off, effectiveFog, factor);
                    float error = Mathf.Max(Mathf.Abs(on.r - expected.r), Mathf.Abs(on.g - expected.g), Mathf.Abs(on.b - expected.b));
                    samples.Add(new DistanceRecord { distance = distance, radialDistance = radialDistance, factor = factor, sourceFactor = XRayWeatherFogMath.EvaluateFactor(radialDistance, parameters), off = off, on = on, expected = expected, maxError = error });
                }
                File.WriteAllText(Path.Combine(directory, "KnownDistances_Samples.json"), JsonUtility.ToJson(new SweepRecord { passed = false, stockDifference = stockDifference, samples = samples.ToArray(), near = snapshot.FogNear, far = snapshot.FogFar, cameraFar = far, meanFreePath = meanFreePath, fittedFog = fittedFog, attenuationShapeUsesFittedFarRadiance = true, absoluteSkyRadianceValidated = false, diagnosticConstantSkyMip = true, diagnosticUniformSky = true, authoredSkyMipMax = savedSkyMip, authoredSkyMipOverride = savedSkyMipOverride, consumerLayer = consumer.gameObject.layer, cameraVolumeMask = mask }, true));
                Require(Finite(fittedFog.r) && Finite(fittedFog.g) && Finite(fittedFog.b) && fittedFog.r > 0.0f && fittedFog.g > 0.0f && fittedFog.b > 0.0f, "Native sky fog has positive finite in-scattering");
                Require(ColorDifference(Pixel(camera, baseline, points[3]), farColor) > 0.02f, "Far opaque target receives native exponential fog independently of transparent surfaces");
                Require(ColorDifference(Pixel(camera, baseline, points[2]), Pixel(camera, enabled, points[2])) > 0.01f, "Mid-distance opaque target receives partial fog");
                foreach (DistanceRecord sample in samples) Require(sample.maxError < 0.015f, "Native exponential attenuation shape with fitted far in-scattering matches framebuffer at " + sample.distance);
                Vector3 cutoutSolidPoint = new Vector3(-4.1f, -2.475f, 280.0f);
                Vector3 cutoutHolePoint = new Vector3(-4.1f, -1.125f, 280.0f);
                Vector3 transparentPoint = new Vector3(3.4f, -1.8f, 280.0f);
                Color cutoutOff = Pixel(camera, baseline, cutoutSolidPoint);
                Color cutoutOn = Pixel(camera, enabled, cutoutSolidPoint);
                Color holeOff = Pixel(camera, baseline, cutoutHolePoint);
                Color holeOn = Pixel(camera, enabled, cutoutHolePoint);
                Color transparentOff = Pixel(camera, baseline, transparentPoint);
                Color transparentOn = Pixel(camera, enabled, transparentPoint);
                var surfaces = new SurfaceRecord { cutoutOff = cutoutOff, cutoutOn = cutoutOn, holeOff = holeOff, holeOn = holeOn, transparentOff = transparentOff, transparentOn = transparentOn };
                File.WriteAllText(Path.Combine(directory, "SurfaceSamples.json"), JsonUtility.ToJson(surfaces, true));
                Require(ColorDifference(cutoutOff, cutoutOn) > 0.001f, "Alpha-tested solid pixels receive fog");
                Require(ColorDifference(holeOff, holeOn) < 0.0001f, "Alpha-tested holes preserve background");
                Require(ColorDifference(transparentOff, transparentOn) > 0.001f, "Applicable transparent Monolith surface receives fog");
                Require(MeanDifference(baseline, enabled) > 0.001, "Fog changes the rendered image");
                Require(camera.farClipPlane == far, "Fog preserves camera far clip");
                Require(camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value == mask, "Fog validation preserves the original camera Volume mask");
                diagnosticFog.mipFogMaxMip.value = savedSkyMip;
                diagnosticFog.mipFogMaxMip.overrideState = savedSkyMipOverride;
                fog.ReleaseWeather(owner);
                Color[] released = Capture(camera, Path.Combine(directory, "KnownDistances_Released"));
                Require(MeanDifference(baseline, released) < 0.0005, "Release restores baseline rendering");
                JsonUtility.FromJsonOverwrite(authored, hdrpFog);
                Require(fog.TrySetWeather(owner, snapshot), "Fog can reacquire after release");
                Require(fog.Volume.profile.TryGet(out Fog reacquiredFog) && reacquiredFog.mipFogMaxMip.value == savedSkyMip && reacquiredFog.mipFogMaxMip.overrideState == savedSkyMipOverride, "Fog reacquires its authored sky mip value and override state");
                string json = JsonUtility.ToJson(new SweepRecord { passed = true, stockDifference = stockDifference, samples = samples.ToArray(), near = snapshot.FogNear, far = snapshot.FogFar, cameraFar = far, meanFreePath = meanFreePath, fittedFog = fittedFog, attenuationShapeUsesFittedFarRadiance = true, absoluteSkyRadianceValidated = false, diagnosticConstantSkyMip = true, diagnosticUniformSky = true, authoredSkyMipMax = savedSkyMip, authoredSkyMipOverride = savedSkyMipOverride, consumerLayer = fog.Volume.gameObject.layer, cameraVolumeMask = mask }, true);
                File.WriteAllText(Path.Combine(directory, "KnownDistances.json"), json);
                return json;
            }
            finally
            {
                if (diagnosticSky != null && savedSky != null) JsonUtility.FromJsonOverwrite(savedSky, diagnosticSky);
                if (diagnosticFog != null)
                {
                    diagnosticFog.mipFogMaxMip.value = savedSkyMip;
                    diagnosticFog.mipFogMaxMip.overrideState = savedSkyMipOverride;
                }
                JsonUtility.FromJsonOverwrite(authored, hdrpFog);
                File.WriteAllText(Path.Combine(directory, "KnownDistances_Restoration.json"), JsonUtility.ToJson(new RestorationRecord { environmentFogRestored = JsonUtility.ToJson(hdrpFog) == authored, diagnosticSkyRestored = diagnosticSky == null || JsonUtility.ToJson(diagnosticSky) == savedSky, diagnosticSkyMipRestored = diagnosticFog == null || diagnosticFog.mipFogMaxMip.value == savedSkyMip && diagnosticFog.mipFogMaxMip.overrideState == savedSkyMipOverride, cameraFarRestored = camera.farClipPlane == far, cameraVolumeMaskRestored = camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value == mask }, true));
            }
        }

        public static XRayWeatherSnapshot Snapshot(float density, float distance, Color color)
        {
            return new XRayWeatherSnapshot("fog_fixture", 43200.0, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.0f, 0.0f, Color.gray, color, density, distance, 1000.0f, Color.gray, Color.clear, 0.0f, default, default);
        }

        public static Color[] Capture(Camera camera, string stem, int width = WIDTH, int height = HEIGHT, int warmup = 8)
        {
            Require(Path.GetFullPath(stem).StartsWith(@"E:\Projects\stalker_raid_into_darkness_Archive\", StringComparison.OrdinalIgnoreCase), "Framebuffer evidence must remain inside the project Archive");
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            Texture2D png = null;
            bool captureWritten = false;
            bool rgbFinite = false;
            Color[] colors = null;
            try
            {
                target.Create();
                camera.targetTexture = target;
                for (int i = 0; i < warmup; i++) camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply(false, false);
                colors = pixels.GetPixels();
                foreach (Color color in colors) Require(Finite(color.r) && Finite(color.g) && Finite(color.b), "Framebuffer RGB is finite");
                rgbFinite = true;
                File.WriteAllBytes(stem + ".exr", pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                png = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                var display = new Color[colors.Length];
                for (int i = 0; i < colors.Length; i++) display[i] = colors[i].gamma;
                png.SetPixels(display);
                png.Apply(false, false);
                File.WriteAllBytes(stem + ".png", png.EncodeToPNG());
                captureWritten = true;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                CoreUtils.Destroy(target);
                CoreUtils.Destroy(pixels);
                if (png != null) CoreUtils.Destroy(png);
                File.WriteAllText(stem + "_Capture.json", JsonUtility.ToJson(new CaptureRecord { captureWritten = captureWritten, rgbFinite = rgbFinite, renderTargetRestored = camera.targetTexture == previousTarget, activeTargetRestored = RenderTexture.active == previousActive, width = width, height = height }, true));
            }
            Require(captureWritten && rgbFinite && colors != null, "Capture files contain finite framebuffer RGB");
            Require(camera.targetTexture == previousTarget && RenderTexture.active == previousActive, "Capture restores both render targets");
            return colors;
        }

        private static Color Pixel(Camera camera, Color[] colors, Vector3 point)
        {
            Vector3 viewport = camera.WorldToViewportPoint(point);
            return colors[Mathf.Clamp((int)(viewport.y * HEIGHT), 0, HEIGHT - 1) * WIDTH + Mathf.Clamp((int)(viewport.x * WIDTH), 0, WIDTH - 1)];
        }

        private void WriteWeatherSample(string stem, XRayWeatherFogRenderer fog)
        {
            XRayWeatherSnapshot snapshot = _controller.Snapshot;
            VolumeStack stack = VolumeManager.instance.CreateStack();
            try
            {
                int mask = _camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value;
                VolumeManager.instance.Update(stack, _camera.transform, mask);
                Fog effectiveFog = stack.GetComponent<Fog>();
                VisualEnvironment environment = stack.GetComponent<VisualEnvironment>();
                XRayWeatherSkySettings effectiveSky = stack.GetComponent<XRayWeatherSkySettings>();
                Exposure exposure = stack.GetComponent<Exposure>();
                Fog consumerFog = null;
                Require(fog.Volume != null && fog.Volume.HasInstantiatedProfile() && fog.Volume.profile.TryGet(out consumerFog), "Weather owns the actual native Fog consumer profile");
                XRayWeatherSkyRenderer sky = _controller.GetComponent<XRayWeatherSkyRenderer>();
                XRayWeatherSkySettings skySettings = null;
                Require(sky != null && sky.HasWeather && _skyVolume.HasInstantiatedProfile() && _skyVolume.profile.TryGet(out skySettings) && sky.Settings == skySettings, "Weather owns the serialized native Sky consumer profile");
                Vector3 source = new Vector3(snapshot.SunColor.r, snapshot.SunColor.g, snapshot.SunColor.b);
                if (_controller.ActiveProfile.HasSunLightScale) source = Vector3.Scale(source, _controller.ActiveProfile.SunLightScale);
                Color expectedRadiance = new Color(source.x, source.y, source.z, 1.0f).linear * _authoredSunIntensity;
                Color physicalRadiance = _sun.color.linear * _sun.intensity;
                var record = new WeatherSampleRecord { seconds = snapshot.GameSeconds, cycle = _controller.ActiveProfile.CycleName, key0 = snapshot.Key0, key1 = snapshot.Key1, fogColor = snapshot.FogColor, fogDensity = snapshot.FogDensity, fogDistance = snapshot.FogDistance, farPlane = snapshot.FarPlane, fogNear = snapshot.FogNear, fogFar = snapshot.FogFar, parameters = fog.Parameters, meanFreePath = fog.MeanFreePath, transitionProgress = _controller.TransitionProgress, consumerFog = JsonUtility.ToJson(consumerFog), effectiveFog = JsonUtility.ToJson(effectiveFog), consumerLayer = fog.Volume.gameObject.layer, cameraVolumeMask = mask, effectiveSkyType = environment.skyType.value, sky = JsonUtility.ToJson(skySettings), effectiveSky = JsonUtility.ToJson(effectiveSky), exposureMode = exposure.mode.value.ToString(), fixedExposure = exposure.fixedExposure.value, sourceSun = source, physicalSunRadiance = physicalRadiance, expectedSunRadiance = expectedRadiance, sunUnit = _sun.lightUnit.ToString(), sunIntensity = _sun.intensity, authoredBoundScale = _sourceLighting.BoundRadianceScale, effectiveBoundScale = _sourceLighting.EffectiveBoundRadianceScale, publishedBoundScale = Shader.GetGlobalFloat("_MonolithBoundRadianceScale"), nightRadiance = XRayWeatherSkyPass.GetNightRadiance(skySettings) };
                File.WriteAllText(stem + ".json", JsonUtility.ToJson(record, true));
                Require(mask == _cameraVolumeMask && (mask & (1 << fog.Volume.gameObject.layer)) != 0 && (mask & (1 << _skyVolume.gameObject.layer)) != 0, "Weather consumers use the unchanged camera Volume mask");
                Require(effectiveFog.enabled.value && effectiveFog.colorMode.value == FogColorMode.SkyColor, "The camera Volume stack uses native Fog SkyColor");
                Require(Mathf.Abs(effectiveFog.meanFreePath.value - fog.MeanFreePath) < 0.001f && ColorDifference(effectiveFog.tint.value, XRayWeatherFogMath.ToSkyTint(snapshot.FogColor)) < 0.00001f, "The effective camera Fog matches weather attenuation and bounded sky tint");
                Require(environment.skyType.value == XRayWeatherSkySettings.SKY_ID && effectiveSky.active && effectiveSky.Weights == snapshot.SkyResources.Weights && effectiveSky.SourceTint == skySettings.SourceTint, "The effective camera Sky uses the serialized weather cubemap blend");
                Require(effectiveSky.Shader != null && effectiveSky.Shader == skySettings.Shader && effectiveSky.Shader.isSupported, "The native Sky uses its serialized supported runtime shader");
                Require(effectiveSky.UseHdrpIntensity == skySettings.UseHdrpIntensity && effectiveSky.skyIntensityMode.value == skySettings.skyIntensityMode.value && effectiveSky.multiplier.value == skySettings.multiplier.value && effectiveSky.exposure.value == skySettings.exposure.value && effectiveSky.NightAmbient == skySettings.NightAmbient && effectiveSky.NightDiffuseMultiplier == skySettings.NightDiffuseMultiplier && effectiveSky.NightTransitionHours == skySettings.NightTransitionHours, "The effective Sky retains authored physical intensity and night controls");
                for (int i = 0; i < 3; i++) Require(Mathf.Abs(effectiveSky.SourceTint[i] - ((Mathf.FloorToInt(snapshot.SkyColor[i] * 255.0f) & 255) / 255.0f)) < 0.00001f, "The native Sky retains source packed RGB tint");
                Texture[] textures = { effectiveSky.Sky0, effectiveSky.Sky1, effectiveSky.Sky2, effectiveSky.Sky3 };
                for (int i = 0; i < textures.Length; i++) if (snapshot.SkyResources.Weights[i] > 0.0f) Require(textures[i] != null && textures[i] == snapshot.SkyResources.Texture(i) && textures[i].dimension == TextureDimension.Cube, "Weighted Sky resources are serialized native cubemaps");
                record.runtimeResourcesSerialized = true;
                Require(_sourceLighting.Mode == MonolithSourceLighting.LightingMode.Bound && _sourceLighting.BoundRadianceScale == _authoredLightingScale && _sourceLighting.EffectiveBoundRadianceScale == 512.0f && record.publishedBoundScale == 512.0f, "Weather retains the existing Bound512 unit conversion");
                Require(Vector3.Distance(source, _controller.SourceLightColor) < 0.00001f, "The source Sun RGB preserves the imported sunlight scale");
                Require(_sun.lightUnit == LightUnit.Lux && Vector3.Distance(_sun.transform.forward, snapshot.SunDirection) < 0.0001f, "The physical Sun retains Lux units and the source direction");
                for (int i = 0; i < 3; i++) Require(Mathf.Abs(physicalRadiance[i] - expectedRadiance[i]) < 0.0001f * Mathf.Max(1.0f, expectedRadiance[i]), "The physical Sun preserves the source linear color and authored intensity formula");
                record.behaviorPassed = true;
                File.WriteAllText(stem + ".json", JsonUtility.ToJson(record, true));
            }
            finally
            {
                VolumeManager.instance.DestroyStack(stack);
            }
        }

        private static VolumeProfile ActiveProfile(Volume volume)
        {
            return volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
        }

        private static string ProfileState(VolumeProfile profile)
        {
            string result = JsonUtility.ToJson(profile);
            foreach (VolumeComponent component in profile.components) result += JsonUtility.ToJson(component);
            return result;
        }

        private static double MeanDifference(Color[] a, Color[] b)
        {
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return sum / (a.Length * 3.0);
        }

        private static float ColorDifference(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private void ObserveLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                _failed = true;
                File.AppendAllText(Path.Combine(_output, "RuntimeErrors.txt"), condition + "\n" + stack + "\n");
            }
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
        private sealed class ResultRecord
        {
            public bool passed;
            public bool initial;
            public bool reload;
            public string sourceSha256;
            public string api;
        }

        [Serializable]
        private sealed class ConnectionRecord
        {
            public bool passed;
            public bool serializedConsumerRemoved;
            public bool runtimeNativeFog;
            public int cameraVolumeMask;
            public int controllerLayer;
            public int consumerLayer;
        }

        [Serializable]
        private sealed class StageRecord
        {
            public bool passed;
            public bool runtimeFallback;
            public bool serializedConsumer;
            public bool ownershipReleased;
            public bool authoredStateRestored;
            public bool ownershipReacquired;
            public bool cameraVolumeMaskUnchanged;
        }

        [Serializable]
        private sealed class CaptureRecord
        {
            public bool captureWritten;
            public bool rgbFinite;
            public bool renderTargetRestored;
            public bool activeTargetRestored;
            public int width;
            public int height;
        }

        [Serializable]
        private sealed class RestorationRecord
        {
            public bool environmentFogRestored;
            public bool diagnosticSkyRestored;
            public bool diagnosticSkyMipRestored;
            public bool cameraFarRestored;
            public bool cameraVolumeMaskRestored;
        }

        [Serializable]
        private sealed class SweepRecord
        {
            public bool passed;
            public double stockDifference;
            public float near;
            public float far;
            public float cameraFar;
            public float meanFreePath;
            public Color fittedFog;
            public bool attenuationShapeUsesFittedFarRadiance;
            public bool absoluteSkyRadianceValidated;
            public bool diagnosticConstantSkyMip;
            public bool diagnosticUniformSky;
            public float authoredSkyMipMax;
            public bool authoredSkyMipOverride;
            public int consumerLayer;
            public int cameraVolumeMask;
            public DistanceRecord[] samples;
        }

        [Serializable]
        private sealed class DistanceRecord
        {
            public float distance;
            public float radialDistance;
            public float factor;
            public float sourceFactor;
            public Color off;
            public Color on;
            public Color expected;
            public float maxError;
        }

        [Serializable]
        private sealed class SurfaceRecord
        {
            public Color cutoutOff;
            public Color cutoutOn;
            public Color holeOff;
            public Color holeOn;
            public Color transparentOff;
            public Color transparentOn;
        }

        [Serializable]
        private sealed class WeatherSampleRecord
        {
            public bool behaviorPassed;
            public double seconds;
            public string cycle;
            public string key0;
            public string key1;
            public Color fogColor;
            public float fogDensity;
            public float fogDistance;
            public float farPlane;
            public float fogNear;
            public float fogFar;
            public Vector4 parameters;
            public float meanFreePath;
            public float transitionProgress;
            public string consumerFog;
            public string effectiveFog;
            public int consumerLayer;
            public int cameraVolumeMask;
            public int effectiveSkyType;
            public string sky;
            public string effectiveSky;
            public string exposureMode;
            public float fixedExposure;
            public Vector3 sourceSun;
            public Color physicalSunRadiance;
            public Color expectedSunRadiance;
            public string sunUnit;
            public float sunIntensity;
            public float authoredBoundScale;
            public float effectiveBoundScale;
            public float publishedBoundScale;
            public Vector3 nightRadiance;
            public bool runtimeResourcesSerialized;
        }
    }
}
