using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    [DefaultExecutionOrder(32000)]
    public sealed partial class XRayPrecipitationPlayerSafetyProbe : MonoBehaviour
    {
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";
        private const int WIDTH = 1920;
        private const int HEIGHT = 1080;

        private static XRayPrecipitationPlayerSafetyProbe _instance;

        [SerializeField] private Camera _camera;
        [SerializeField] private XRayWeatherController _controller;
        [SerializeField] private string _sourceHash;
        [SerializeField] private GameObject[] _fixtureRoots;
        [SerializeField] private bool[] _fixtureRootActive;

        private readonly List<string> _errors = new List<string>();
        private readonly object _logLock = new object();
        private readonly List<PrecipitationConfig> _ownedConfigs = new List<PrecipitationConfig>();
        private readonly List<RenderTexture> _trackedReflectionTextures = new List<RenderTexture>(20);

        private string _output;
        private string _serializedSourceTag;
        private string _buildManifestPath;
        private string _executableSha256;
        private RenderTexture _capture;
        private Color[] _lastPixels;
        private Exception _captureFailure;
        private bool _stagePassed;
        private FieldInfo _configField;
        private FieldInfo _flagsField;
        private object _originalConfig;
        private object _originalFlags;
        private string _sharedBefore;
        private XRayPrecipitationSystem _releasedSystem;
        private volatile bool _fatalNative;
        private bool _fatalHandled;
        private int _errorOverflow;
        private int _cameraRenders;

        public void Configure(Camera camera, XRayWeatherController controller, string sourceHash)
        {
            _camera = camera;
            _controller = controller;
            _sourceHash = sourceHash;
            _cameraRenders = 0;
            if (_capture != null && _camera != null) _camera.targetTexture = _capture;
        }

        public void ConfigureRoots(GameObject[] roots, bool[] active)
        {
            _fixtureRoots = roots;
            _fixtureRootActive = active;
        }

        private void Awake()
        {
            _serializedSourceTag = _sourceHash;
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < arguments.Length; index++) if (arguments[index] == "-precipitationSafety") _output = arguments[index + 1];
            if (string.IsNullOrEmpty(_output))
            {
                enabled = false;
                return;
            }
            for (int index = 0; index + 1 < arguments.Length; index++) if (arguments[index] == "-precipitationBuildManifest") _buildManifestPath = arguments[index + 1];
            Require(!string.IsNullOrEmpty(_buildManifestPath) && Path.GetFullPath(_buildManifestPath).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase), "Diagnostic source provenance requires the actual frozen Player build manifest in Archive.");
            BuildManifestRecord build = JsonUtility.FromJson<BuildManifestRecord>(File.ReadAllText(_buildManifestPath));
            Require(build != null && build.sourceSha256 != null && build.sourceSha256.Length == 64 && build.executableSha256 != null && build.executableSha256.Length == 64, "The tested build manifest must contain the actual source and executable SHA256 values.");
            _sourceHash = build.sourceSha256;
            _executableSha256 = build.executableSha256;
            if (_instance != null && _instance != this)
            {
                ActivateRoots();
                _instance.Configure(_camera, _controller, _sourceHash);
                Destroy(gameObject);
                return;
            }
            if (!Path.GetFullPath(_output).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase) || Directory.Exists(_output)) throw new InvalidOperationException("Use a new safety evidence directory in Archive.");
            Directory.CreateDirectory(_output);
            File.Copy(_buildManifestPath, Path.Combine(_output, "TestedBuildManifest.json"));
            _instance = this;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            _capture = new RenderTexture(WIDTH, HEIGHT, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _capture.name = "Persistent native framebuffer evidence";
            if (!_capture.Create()) throw new InvalidOperationException("Cannot create the persistent safety capture target.");
            _camera.targetTexture = _capture;
            DontDestroyOnLoad(gameObject);
            ActivateRoots();
        }

        private void ActivateRoots()
        {
            if (_fixtureRoots == null || _fixtureRootActive == null || _fixtureRoots.Length != _fixtureRootActive.Length) throw new InvalidOperationException("The saved fixture roots must have explicit activation states.");
            for (int index = 0; index < _fixtureRoots.Length; index++) if (_fixtureRoots[index] != null) _fixtureRoots[index].SetActive(_fixtureRootActive[index]);
        }

        private IEnumerator Start()
        {
            if (_instance != this || string.IsNullOrEmpty(_output)) yield break;
            Application.logMessageReceivedThreaded += ObserveLog;
            RenderPipelineManager.endCameraRendering += ObserveCamera;
            Write(_output, "System", new SystemRecord { unity = Application.unityVersion, api = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName, width = _capture.width, height = _capture.height, sourceHash = _sourceHash, serializedSourceTag = _serializedSourceTag, buildManifest = _buildManifestPath, executableSha256 = _executableSha256, capture = "Enabled scene camera renders naturally into a persistent1920x1080 RGBAFloat target; asynchronous readback after native end-of-frame. No Camera.Render, backbuffer copy or transient camera." });
            yield return Frames(8);
            Breadcrumb("InitialBefore");
            yield return RunStage("Initial");
            bool initial = _stagePassed;
            if (!initial || ErrorCount() != 0)
            {
                Write(_output, "Result", new ResultRecord { passed = false, initial = initial, passedScope = "NATIVE_SAFETY_AND_LIFECYCLE_ONLY", errors = ErrorSnapshot() });
                Application.Quit(1);
                yield break;
            }
            string scene = _controller.gameObject.scene.name;
            Breadcrumb("SceneReloadBefore");
            yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            yield return Frames(8);
            Breadcrumb("SceneReloadAfter");
            int trackedBeforeReload = _trackedReflectionTextures.Count;
            int releasedAfterReload = ReleasedReflectionTextureCount(_trackedReflectionTextures);
            bool oldReleased = _releasedSystem != null && _releasedSystem.EstimatedBytes == 0 && trackedBeforeReload > 0 && releasedAfterReload == trackedBeforeReload;
            yield return RunStage("Reload");
            yield return Frames(6);
            bool passed = initial && _stagePassed && oldReleased && ErrorCount() == 0;
            bool fullRequested = Array.IndexOf(Environment.GetCommandLineArgs(), "-precipitationFull") >= 0;
            if (passed && fullRequested) yield return RunGuardedFull();
            passed &= !fullRequested || _fullPassed;
            bool artRequested = Array.IndexOf(Environment.GetCommandLineArgs(), "-precipitationArt") >= 0;
            if (initial && _stagePassed && oldReleased && ErrorCount() == 0 && artRequested) yield return RunGuardedArt();
            passed &= !artRequested || _artPassed;
            if (_camera != null) _camera.targetTexture = null;
            yield return Frames(6);
            if (_capture != null) Destroy(_capture);
            yield return Frames(4);
            passed &= ErrorCount() == 0;
            Write(_output, "Result", new ResultRecord { passed = passed, initial = initial, reload = _stagePassed, oldSceneResourcesReleased = oldReleased, trackedBeforeReload = trackedBeforeReload, releasedAfterReload = releasedAfterReload, reflectionTexturesTracked = _trackedReflectionTextures.Count, reflectionTexturesReleased = ReleasedReflectionTextureCount(_trackedReflectionTextures), passedScope = artRequested ? "ART_CAPTURE_ASSERTIONS_AND_NATIVE_SAFETY" : fullRequested ? "FULL_PRECIPITATION_VISUAL_AND_LIFECYCLE" : "NATIVE_SAFETY_AND_LIFECYCLE_ONLY", fullRequested = fullRequested, fullPassed = _fullPassed, artRequested = artRequested, artCaptureAssertionsPassed = _artPassed, errors = ErrorSnapshot() });
            Application.logMessageReceivedThreaded -= ObserveLog;
            RenderPipelineManager.endCameraRendering -= ObserveCamera;
            Application.Quit(passed ? 0 : 1);
        }

        private IEnumerator RunStage(string name)
        {
            _stagePassed = false;
            string output = Path.Combine(_output, name);
            Directory.CreateDirectory(output);
            var result = new StageRecord();
            var reflectionTextures = new List<RenderTexture>(10);
            bool hasBindings = false;
            Exception failure = null;
            try
            {
                Require(_controller != null && _camera != null, "The serialized fixture controller and camera are required.");
                Require(ErrorCount() == 0, "A previous native error prevents another feature activation.");
                _configField = typeof(XRayWeatherController).GetField("_precipitationConfig", BindingFlags.Instance | BindingFlags.NonPublic);
                _flagsField = typeof(XRayWeatherController).GetField("_levelOverrides", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(_configField != null && _flagsField != null, "The owned fixture override fields must match the validated controller schema.");
                _originalConfig = _configField.GetValue(_controller);
                _originalFlags = _flagsField.GetValue(_controller);
                hasBindings = true;
                _sharedBefore = SharedJson();
                Require(_camera.pixelWidth == WIDTH && _camera.pixelHeight == HEIGHT && _camera.isActiveAndEnabled && _controller.isActiveAndEnabled && _camera.targetTexture == _capture, "Safety uses the active scene controller/camera and persistent native1920x1080 target.");
                Require(_controller.Configuration == GameConfig.Load(), "The fixture inherits the canonical root configuration.");
                _controller.SetPaused(true);
                _controller.SetTimeScale(0.0f);
                _controller.SetProfile(GameConfig.Load().DefaultWeather);
                _controller.Seek(43230.0);
                _controller.SetRainVisualOverride(false);
                Breadcrumb(name + ":BaselineFeaturesBefore");
                SetFeatures(false, false);
                Breadcrumb(name + ":BaselineFeaturesAfter");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            if (_fatalNative) yield break;
            if (failure == null)
            {
                yield return Frames(30);
                yield return Capture(output, "Baseline");
                Color[] baseline = _lastPixels;
                failure = _captureFailure;
                if (failure == null)
                {
                    yield return Frames(8);
                    yield return Capture(output, "BaselineRepeat");
                    failure = _captureFailure;
                }
                Color[] repeat = _lastPixels;
                if (failure == null)
                {
                    XRayPrecipitationValidationProbe.Difference(baseline, repeat, out result.noise, out result.noisePixels, out result.noiseMaximum);
                    try
                    {
                        Breadcrumb(name + ":WetFeaturesBefore");
                        SetFeatures(true, true);
                        JsonUtility.FromJsonOverwrite("{\"_nonCameraReflectionsEnabled\":false}", _ownedConfigs[_ownedConfigs.Count - 1]);
                        _controller.ApplySettings();
                        Breadcrumb(name + ":WetFeaturesAfter");
                        Require(_controller.Precipitation.Wetness != null, "The owned active configuration creates a real wetness field.");
                        Require(_controller.Precipitation.Geometry.GeometryCount > 0, "Real active scene geometry must be captured before wet rendering is validated.");
                        if (!_fatalNative) _controller.Precipitation.Wetness.SetUniformState(0.0f, 0.0f);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                }
                if (failure == null)
                {
                    yield return Frames(60);
                    yield return Capture(output, "WetZero");
                    failure = _captureFailure;
                    if (failure == null)
                    {
                        XRayPrecipitationValidationProbe.Difference(repeat, _lastPixels, out result.wetZeroDifference, out result.wetZeroPixels, out result.wetZeroMaximum);
                        result.zeroReflectionsHeldOff = !_controller.EffectivePrecipitationConfig.NonCameraReflectionsEnabled && (_controller.Precipitation.Reflections == null || _controller.Precipitation.Reflections.ProbeCount == 0 && _controller.Precipitation.Reflections.PlanarProbe == null);
                        result.zeroPassed = result.zeroReflectionsHeldOff && result.wetZeroDifference <= Math.Max(result.noise * 4.0, 0.00005);
                        result.materialClones = _controller.Precipitation.WetMaterials.CloneCount;
                        result.rendererBindings = _controller.Precipitation.WetMaterials.BindingCount;
                        result.estimatedBytes = _controller.Precipitation.EstimatedBytes;
                        result.instancedInstanceCount = _controller.Precipitation.Geometry.InstancedInstanceCount;
                        result.geometryEstimatedBytes = _controller.Precipitation.Geometry.EstimatedBytes;
                        result.environment = EnvironmentNames();
                        JsonUtility.FromJsonOverwrite("{\"_nonCameraReflectionsEnabled\":true}", _ownedConfigs[_ownedConfigs.Count - 1]);
                        _controller.ApplySettings();
                    }
                }
                if (failure == null)
                {
                    yield return Frames(60);
                    yield return Capture(output, "WetZeroReflectionsOn");
                    failure = _captureFailure;
                    if (failure == null)
                    {
                        XRayPrecipitationValidationProbe.Difference(repeat, _lastPixels, out result.wetZeroReflectionsDifference, out result.wetZeroReflectionsPixels, out result.wetZeroReflectionsMaximum);
                        if (!_fatalNative) _controller.Precipitation.Wetness.SetUniformState(1.0f, 0.0f);
                    }
                }
                Color[] wet = null;
                if (failure == null)
                {
                    yield return Frames(30);
                    yield return Capture(output, "WetOne");
                    failure = _captureFailure;
                    wet = _lastPixels;
                    if (failure == null)
                    {
                        XRayPrecipitationValidationProbe.Difference(repeat, wet, out result.wetOneDifference, out result.wetOnePixels, out result.wetOneMaximum);
                        if (!_fatalNative) _controller.Precipitation.Wetness.SetUniformState(1.0f, 1.0f);
                    }
                }
                if (failure == null)
                {
                    yield return Frames(30);
                    yield return Capture(output, "PuddleOne");
                    failure = _captureFailure;
                    if (failure == null) XRayPrecipitationValidationProbe.Difference(wet, _lastPixels, out result.puddleDifference, out result.puddlePixels, out result.puddleMaximum);
                    if (failure == null)
                    {
                        result.planarReleaseEvidenceRequired = _controller.EffectivePrecipitationConfig.PlanarReflectionsEnabled && _controller.EffectivePrecipitationConfig.NonCameraReflectionsEnabled;
                        for (int frame = 0; frame < 180 && !_fatalNative; frame++)
                        {
                            TrackOwnedReflectionTextures(_controller.Precipitation.Reflections, reflectionTextures, result);
                            if (result.cubeColorTexturesTracked > 0 && (!result.planarReleaseEvidenceRequired || result.planarColorTexturesTracked > 0)) break;
                            yield return null;
                        }
                        if (result.cubeColorTexturesTracked == 0 || result.planarReleaseEvidenceRequired && result.planarColorTexturesTracked == 0) failure = new InvalidOperationException("PuddleOne must allocate actual owned cubemap and enabled planar color textures before native lifetime acceptance.");
                    }
                }
                yield return Frames(120);
                TrackOwnedReflectionTextures(_controller.Precipitation.Reflections, reflectionTextures, result);
                _trackedReflectionTextures.AddRange(reflectionTextures);
            }
            if (_fatalNative) yield break;
            try
            {
                Breadcrumb(name + ":ConfigRestoreBefore");
                if (hasBindings && _controller != null)
                {
                    _configField.SetValue(_controller, _originalConfig);
                    _flagsField.SetValue(_controller, _originalFlags);
                    _controller.ApplySettings();
                    _controller.SetPaused(true);
                    _controller.SetRainVisualOverride(false);
                }
                Breadcrumb(name + ":ConfigRestoreAfter");
            }
            catch (Exception exception)
            {
                if (failure == null) failure = exception;
            }
            yield return Frames(8);
            Breadcrumb(name + ":ConfigDestructionBefore");
            foreach (PrecipitationConfig config in _ownedConfigs) if (config != null) Destroy(config);
            _ownedConfigs.Clear();
            yield return Frames(4);
            Breadcrumb(name + ":ConfigDestructionAfter");
            try
            {
                result.configurationReadOnly = _sharedBefore != null && _sharedBefore == SharedJson();
            }
            catch (Exception exception)
            {
                if (failure == null) failure = exception;
            }
            result.nativeErrors = ErrorCount();
            result.error = failure?.ToString();
            result.passed = failure == null && result.zeroPassed && result.materialClones > 0 && result.rendererBindings > 0 && result.configurationReadOnly && result.nativeErrors == 0;
            _releasedSystem = _controller != null ? _controller.Precipitation : null;
            Breadcrumb(name + ":ControllerDisableBefore");
            if (_controller != null) _controller.enabled = false;
            yield return Frames(6);
            for (int frame = 0; frame < 30 && ReleasedReflectionTextureCount(reflectionTextures) < reflectionTextures.Count; frame++) yield return null;
            result.reflectionTexturesTracked = reflectionTextures.Count;
            result.reflectionTexturesReleased = ReleasedReflectionTextureCount(reflectionTextures);
            result.reflectionTexturesReleasedAfterDisable = result.reflectionTexturesTracked > 0 && result.reflectionTexturesReleased == result.reflectionTexturesTracked && !result.reflectionSnapshotOverflow;
            if (!result.reflectionTexturesReleasedAfterDisable && string.IsNullOrEmpty(result.error)) result.error = "Actual PuddleOne reflection RenderTexture references were absent, exceeded the bounded snapshot, or remained allocated after owner disable.";
            result.disabled = _releasedSystem != null && !_releasedSystem.Enabled && (_releasedSystem.Pass == null || _releasedSystem.Pass.ViewCount == 0);
            Breadcrumb(name + ":ControllerDisableAfter");
            if (_controller != null)
            {
                _controller.enabled = true;
                _controller.SetPaused(true);
            }
            yield return Frames(6);
            result.reenabled = _controller != null && _controller.Precipitation != null && _controller.Precipitation.Enabled;
            result.nativeErrors = ErrorCount();
            result.passed &= result.disabled && result.reenabled && result.reflectionTexturesReleasedAfterDisable && result.nativeErrors == 0;
            result.visibleWetObserved = result.wetOneDifference > Math.Max(result.noise * 4.0, 0.00001);
            result.visiblePuddlesObserved = result.puddleDifference > Math.Max(result.noise * 4.0, 0.00001);
            result.passedScope = "NATIVE_SAFETY_AND_LIFECYCLE_ONLY";
            _stagePassed = result.passed;
            Write(output, "Stage", result);
        }

        private static void TrackOwnedReflectionTextures(XRayWetnessReflection reflections, List<RenderTexture> textures, StageRecord record)
        {
            if (reflections == null) return;
            for (int index = 0; index < reflections.ProbeCount; index++)
            {
                HDAdditionalReflectionData probe = reflections.GetProbe(index);
                if (probe == null) continue;
                if (TrackReflectionTexture(textures, probe.realtimeTexture, record)) record.cubeColorTexturesTracked++;
                if (TrackReflectionTexture(textures, probe.realtimeDepthTexture, record)) record.cubeDepthTexturesTracked++;
            }
            PlanarReflectionProbe planar = reflections.PlanarProbe;
            if (planar == null) return;
            if (TrackReflectionTexture(textures, planar.realtimeTexture, record)) record.planarColorTexturesTracked++;
            if (TrackReflectionTexture(textures, planar.realtimeDepthTexture, record)) record.planarDepthTexturesTracked++;
        }

        private static bool TrackReflectionTexture(List<RenderTexture> textures, RenderTexture texture, StageRecord record)
        {
            if (texture == null || !texture.IsCreated() || textures.Contains(texture)) return false;
            if (textures.Count >= 10)
            {
                record.reflectionSnapshotOverflow = true;
                return false;
            }
            textures.Add(texture);
            return true;
        }

        private static int ReleasedReflectionTextureCount(List<RenderTexture> textures)
        {
            int released = 0;
            foreach (RenderTexture texture in textures) if (texture == null || !texture.IsCreated()) released++;
            return released;
        }

        private void SetFeatures(bool wetness, bool puddles)
        {
            if (_fatalNative) return;
            PrecipitationConfig owned = Instantiate(GameConfig.Load().Precipitation);
            JsonUtility.FromJsonOverwrite("{\"_wetnessEnabled\":" + (wetness ? "true" : "false") + ",\"_puddlesEnabled\":" + (puddles ? "true" : "false") + "}", owned);
            _ownedConfigs.Add(owned);
            _configField.SetValue(_controller, owned);
            _flagsField.SetValue(_controller, (XRayWeatherLevelOverrides)_originalFlags | XRayWeatherLevelOverrides.Precipitation);
            _controller.ApplySettings();
        }

        private IEnumerator Capture(string output, string label)
        {
            _captureFailure = null;
            _lastPixels = null;
            int previousRenders = _cameraRenders;
            yield return new WaitForEndOfFrame();
            if (_fatalNative) yield break;
            Breadcrumb(label + ":NativeCameraReadbackBefore");
            AsyncGPUReadbackRequest request = default;
            try
            {
                Write(output, label + "_NativeRendering", ReadNativeRendering());
                Require(_cameraRenders > previousRenders, "The enabled scene camera must complete a fresh native HDRP frame for every captured feature state.");
                request = AsyncGPUReadback.Request(_capture, 0, TextureFormat.RGBAFloat);
                Breadcrumb(label + ":ReadbackRequested");
            }
            catch (Exception exception)
            {
                _captureFailure = exception;
            }
            if (_captureFailure != null) yield break;
            while (!request.done) yield return null;
            if (_fatalNative) yield break;
            Breadcrumb(label + ":ReadbackCompleted");
            Texture2D exr = null;
            Texture2D png = null;
            try
            {
                Require(!request.hasError, "Native asynchronous framebuffer readback succeeded.");
                _lastPixels = request.GetData<Color>().ToArray();
                int magenta = 0;
                double luminance = 0.0;
                float minimum = float.PositiveInfinity;
                float maximum = float.NegativeInfinity;
                int nonBlack = 0;
                foreach (Color pixel in _lastPixels)
                {
                    Require(float.IsFinite(pixel.r) && float.IsFinite(pixel.g) && float.IsFinite(pixel.b), "Framebuffer RGB is finite.");
                    if (pixel.r > 0.02f && pixel.b > 0.02f && pixel.g < Mathf.Min(pixel.r, pixel.b) * 0.4f && Mathf.Abs(pixel.r - pixel.b) < Mathf.Max(pixel.r, pixel.b) * 0.25f) magenta++;
                    float value = pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f;
                    luminance += value;
                    minimum = Mathf.Min(minimum, value);
                    maximum = Mathf.Max(maximum, value);
                    if (value > 0.000001f) nonBlack++;
                }
                var stats = new CaptureRecord { minimumLuminance = minimum, maximumLuminance = maximum, meanLuminance = luminance / _lastPixels.Length, nonBlackPixels = nonBlack, magentaPixels = magenta, nativeCameraRenders = _cameraRenders };
                Write(output, label + "_Capture", stats);
                Require(nonBlack > 1000 && stats.meanLuminance > 0.000001 && maximum - minimum > 0.00001f, "The real scene framebuffer must be nonblack and have a measurable luminance range.");
                Require(magenta < 1000, "Framebuffer has no widespread error-shader magenta.");
                exr = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBAFloat, false, true);
                exr.SetPixels(_lastPixels);
                File.WriteAllBytes(Path.Combine(output, label + ".exr"), exr.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                png = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false, false);
                var display = new Color[_lastPixels.Length];
                for (int index = 0; index < display.Length; index++) display[index] = _lastPixels[index].gamma;
                png.SetPixels(display);
                File.WriteAllBytes(Path.Combine(output, label + ".png"), png.EncodeToPNG());
            }
            catch (Exception exception)
            {
                _captureFailure = exception;
            }
            finally
            {
                if (exr != null) Destroy(exr);
                if (png != null) Destroy(png);
            }
            Breadcrumb(label + ":ReadbackTexturesDestroyScheduled");
            yield return Frames(4);
        }

        private static IEnumerator Frames(int count)
        {
            for (int index = 0; index < count; index++) yield return null;
        }

        private static string SharedJson()
        {
            GameConfig config = GameConfig.Load();
            return JsonUtility.ToJson(config) + JsonUtility.ToJson(config.Precipitation);
        }

        private static string[] EnvironmentNames()
        {
            var names = new string[4];
            for (int index = 0; index < names.Length; index++)
            {
                Texture texture = Shader.GetGlobalTexture("_MonolithWeatherEnvironment" + index);
                names[index] = texture != null ? texture.name : null;
            }
            return names;
        }

        private void ObserveLog(string message, string stack, LogType type)
        {
            bool fatal = type == LogType.Error || type == LogType.Exception || type == LogType.Assert || message.IndexOf("D3D12Fence", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("Texture index out of range", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("device removed", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("device lost", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!fatal) return;
            lock (_logLock)
            {
                if (_errors.Count < 128) _errors.Add(DateTime.UtcNow.ToString("O") + " " + type + " " + message + "\n" + stack);
                else _errorOverflow++;
                _fatalNative = true;
            }
        }

        private void ObserveCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera) _cameraRenders++;
        }

        private NativeRenderingRecord ReadNativeRendering()
        {
            var cameras = new List<CameraRecord>();
            foreach (Camera camera in Camera.allCameras)
            {
                cameras.Add(new CameraRecord { id = camera.GetEntityId().ToString(), name = camera.name, type = camera.cameraType.ToString(), active = camera.isActiveAndEnabled, gameScene = camera.gameObject.scene.path, renderScene = camera.scene.path, cullingMask = camera.cullingMask, targetDisplay = camera.targetDisplay, target = camera.targetTexture != null ? camera.targetTexture.name : null });
            }
            HDAdditionalCameraData data = _camera.GetComponent<HDAdditionalCameraData>();
            PropertyInfo custom = data != null ? data.GetType().GetProperty("hasCustomRender", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
            RenderPipelineAsset current = GraphicsSettings.currentRenderPipeline;
            RenderPipelineAsset defaults = GraphicsSettings.defaultRenderPipeline;
            RenderPipelineAsset quality = QualitySettings.renderPipeline;
            return new NativeRenderingRecord { frame = Time.frameCount, cameraRenders = _cameraRenders, cameraId = _camera.GetEntityId().ToString(), cameraType = _camera.cameraType.ToString(), cameraActive = _camera.isActiveAndEnabled, cameraGameScene = _camera.gameObject.scene.path, cameraRenderScene = _camera.scene.path, cameraRenderSceneLoaded = _camera.scene.isLoaded, cullingMask = _camera.cullingMask, targetDisplay = _camera.targetDisplay, targetCreated = _capture != null && _capture.IsCreated(), targetWidth = _capture != null ? _capture.width : 0, targetHeight = _capture != null ? _capture.height : 0, customRender = custom != null ? Convert.ToString(custom.GetValue(data)) : "Property unavailable", pipeline = RenderPipelineManager.currentPipeline != null ? RenderPipelineManager.currentPipeline.GetType().FullName : null, currentAsset = current != null ? current.name : null, defaultAsset = defaults != null ? defaults.name : null, qualityAsset = quality != null ? quality.name : null, renderFrameInterval = OnDemandRendering.renderFrameInterval, allCamerasCount = Camera.allCamerasCount, cameras = cameras.ToArray() };
        }

        private void LateUpdate()
        {
            if (!_fatalNative || _fatalHandled || string.IsNullOrEmpty(_output)) return;
            _fatalHandled = true;
            StopAllCoroutines();
            Write(_output, "Result", new ResultRecord { passed = false, fatalNativeError = true, errorOverflow = _errorOverflow, passedScope = "NATIVE_SAFETY_AND_LIFECYCLE_ONLY", errors = ErrorSnapshot() });
            if (_camera != null) _camera.enabled = false;
            if (_controller != null && _controller.Precipitation != null && _controller.Precipitation.Pass != null) _controller.Precipitation.Pass.enabled = false;
            Application.Quit(1);
        }

        private void Breadcrumb(string phase)
        {
            File.AppendAllText(Path.Combine(_output, "Timeline.jsonl"), JsonUtility.ToJson(new TimelineRecord { utc = DateTime.UtcNow.ToString("O"), phase = phase, frame = Time.frameCount, errors = ErrorCount() }) + "\n");
        }

        private int ErrorCount()
        {
            lock (_logLock) return _errors.Count;
        }

        private string[] ErrorSnapshot()
        {
            lock (_logLock) return _errors.ToArray();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Write(string output, string name, object value)
        {
            File.WriteAllText(Path.Combine(output, name + ".json"), JsonUtility.ToJson(value, true));
        }

        [Serializable]
        private sealed class BuildManifestRecord
        {
            public string sourceSha256;
            public string executableSha256;
        }

        [Serializable]
        private sealed class NativeRenderingRecord
        {
            public int frame;
            public int cameraRenders;
            public string cameraId;
            public string cameraType;
            public bool cameraActive;
            public string cameraGameScene;
            public string cameraRenderScene;
            public bool cameraRenderSceneLoaded;
            public int cullingMask;
            public int targetDisplay;
            public bool targetCreated;
            public int targetWidth;
            public int targetHeight;
            public string customRender;
            public string pipeline;
            public string currentAsset;
            public string defaultAsset;
            public string qualityAsset;
            public int renderFrameInterval;
            public int allCamerasCount;
            public CameraRecord[] cameras;
        }

        [Serializable]
        private sealed class CameraRecord
        {
            public string id;
            public string name;
            public string type;
            public bool active;
            public string gameScene;
            public string renderScene;
            public int cullingMask;
            public int targetDisplay;
            public string target;
        }

        [Serializable]
        private sealed class CaptureRecord
        {
            public float minimumLuminance;
            public float maximumLuminance;
            public double meanLuminance;
            public int nonBlackPixels;
            public int magentaPixels;
            public int nativeCameraRenders;
        }

        [Serializable]
        private sealed class SystemRecord
        {
            public string unity;
            public string api;
            public string gpu;
            public int width;
            public int height;
            public string sourceHash;
            public string serializedSourceTag;
            public string buildManifest;
            public string executableSha256;
            public string capture;
        }

        [Serializable]
        private sealed class TimelineRecord
        {
            public string utc;
            public string phase;
            public int frame;
            public int errors;
        }

        [Serializable]
        private sealed class StageRecord
        {
            public bool passed;
            public string passedScope;
            public bool visibleWetObserved;
            public bool visiblePuddlesObserved;
            public bool zeroPassed;
            public bool zeroReflectionsHeldOff;
            public bool configurationReadOnly;
            public bool disabled;
            public bool reenabled;
            public bool planarReleaseEvidenceRequired;
            public bool reflectionTexturesReleasedAfterDisable;
            public bool reflectionSnapshotOverflow;
            public int reflectionTexturesTracked;
            public int reflectionTexturesReleased;
            public int cubeColorTexturesTracked;
            public int cubeDepthTexturesTracked;
            public int planarColorTexturesTracked;
            public int planarDepthTexturesTracked;
            public double noise;
            public int noisePixels;
            public float noiseMaximum;
            public double wetZeroDifference;
            public int wetZeroPixels;
            public float wetZeroMaximum;
            public double wetZeroReflectionsDifference;
            public int wetZeroReflectionsPixels;
            public float wetZeroReflectionsMaximum;
            public double wetOneDifference;
            public int wetOnePixels;
            public float wetOneMaximum;
            public double puddleDifference;
            public int puddlePixels;
            public float puddleMaximum;
            public int materialClones;
            public int rendererBindings;
            public long estimatedBytes;
            public int instancedInstanceCount;
            public long geometryEstimatedBytes;
            public string[] environment;
            public int nativeErrors;
            public string error;
        }

        [Serializable]
        private sealed class ResultRecord
        {
            public bool passed;
            public string passedScope;
            public bool fatalNativeError;
            public int errorOverflow;
            public bool initial;
            public bool reload;
            public bool oldSceneResourcesReleased;
            public int trackedBeforeReload;
            public int releasedAfterReload;
            public int reflectionTexturesTracked;
            public int reflectionTexturesReleased;
            public bool fullRequested;
            public bool fullPassed;
            public bool artRequested;
            public bool artCaptureAssertionsPassed;
            public string[] errors;
        }
    }
}
