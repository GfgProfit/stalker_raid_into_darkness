#if UNITY_EDITOR || CLEAN_RELEASE_ROUTE07
using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;
using RaidIntoDarkness.Effects;
using Rvm.Benchmark;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Rvm.CleanReleaseRoute07
{
    [DefaultExecutionOrder(-9000)]
    public sealed class CleanReleaseRouteController : MonoBehaviour
    {
        private const int CAPACITY = 262144;
        private const double WARMUP_SECONDS = 8.0;
        private const double POSITION_SECONDS = 5.0;
        private const int BOUNDARY_FLAG = 1;
        private const int END_FLAG = 2;

        [SerializeField] private Camera _camera;
        [SerializeField] private Vector3[] _positions;
        [SerializeField] private Quaternion[] _rotations;
        [SerializeField] private string _referenceCameraSettings;
        [SerializeField] private XRayWeatherProfile _weatherProfile;
        [SerializeField] private GameObject _technicalKiselPrefab;
        [SerializeField] private BenchmarkScenario[] _profiles;
        [SerializeField] private string _sceneGuid;

        private Sample[] _samples;
        private readonly long[] _phaseStarts = new long[5];
        private readonly int[] _renderedFrames = new int[5];
        private XRayWeatherController _weather;
        private string _output;
        private bool _timing;
        private bool _configCheck;
        private bool _running;
        private bool _started;
        private bool _saved;
        private bool _focusViolation;
        private bool _overflow;
        private int _focusLostFrame = -1;
        private int _runtimeErrors;
        private int _count;
        private int _position = -1;
        private int _lastRenderedFrame = -1;
        private int _configurationRenderFrames;
        private long _phaseStart;
        private long _endTimestamp;
        private Conditions _before;
        private AnomalyVisual08Check _visualCheck;
        private AnomalyLight10Check _lightCheck;
        private AnomalyShadow11Check _shadowCheck;
        private BenchmarkScenario _scenario;
        private BenchmarkCameraState _cameraState;
        private XRayWeatherProfile _savedWeather;
        private double _savedWeatherSeconds;
        private bool _savedWeatherPaused;
        private bool _weatherCaptured;
        private string _originalCameraSettings;
        private Vector3 _originalCameraPosition;
        private Quaternion _originalCameraRotation;
        private static BenchmarkPacingState _pacing;

        public void ConfigureProfiles(Camera camera, BenchmarkScenario[] profiles, string sceneGuid, XRayWeatherProfile weatherProfile)
        {
            _camera = camera;
            _profiles = profiles;
            _sceneGuid = sceneGuid;
            _weatherProfile = weatherProfile;
            enabled = false;
        }

        public void Configure(Camera camera, Vector3[] positions, Quaternion[] rotations, string referenceCameraSettings, XRayWeatherProfile weatherProfile)
        {
            _camera = camera;
            _positions = positions;
            _rotations = rotations;
            _referenceCameraSettings = referenceCameraSettings;
            _weatherProfile = weatherProfile;
            enabled = false;
        }

        public void ConfigureTechnicalPrefab(GameObject prefab)
        {
            _technicalKiselPrefab = prefab;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyDefaults()
        {
            if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "-clean-route") < 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-clean-route-no-timing") < 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-clean-route-config-check") < 0)
            {
                return;
            }
            int quality = Array.IndexOf(QualitySettings.names, "High Fidelity");
            if (quality < 0)
            {
                throw new InvalidOperationException("High Fidelity quality is unavailable.");
            }
            _pacing = new BenchmarkPacingState();
            QualitySettings.SetQualityLevel(quality, true);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            OnDemandRendering.renderFrameInterval = 1;
            Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartRequestedRoute()
        {
            if (Application.isEditor)
            {
                return;
            }
            string[] args = Environment.GetCommandLineArgs();
            bool route = Array.IndexOf(args, "-clean-route") >= 0;
            bool noTiming = Array.IndexOf(args, "-clean-route-no-timing") >= 0;
            bool check = Array.IndexOf(args, "-clean-route-config-check") >= 0;
            if (!route && !noTiming && !check)
            {
                return;
            }
            var controllers = UnityEngine.Object.FindObjectsByType<CleanReleaseRouteController>(FindObjectsInactive.Include);
            if (controllers.Length != 1)
            {
                throw new InvalidOperationException("Exactly one clean route controller is required.");
            }
            var controller = controllers[0];
            controller._output = GetArgument(args, "-clean-route-output");
            try
            {
                if (controller._profiles != null && controller._profiles.Length > 0)
                {
                    int index = Array.IndexOf(args, "-benchmark-route");
                    if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Select an explicit -benchmark-route profile ID.");
                    controller._scenario = BenchmarkScenario.SelectRoute(controller._profiles, args[index + 1], controller._sceneGuid);
                    if (controller._scenario.WarmupSeconds != WARMUP_SECONDS || controller._scenario.PositionSeconds != POSITION_SECONDS || controller._scenario.StabilizationSeconds != 0.5) throw new InvalidOperationException("Release route requires the established 8 / 5 / 0.5 protocol.");
                    controller._positions = new Vector3[4];
                    controller._rotations = new Quaternion[4];
                    for (int i = 0; i < 4; i++)
                    {
                        controller._positions[i] = controller._scenario.Position(i);
                        controller._rotations[i] = controller._scenario.Rotation(i);
                    }
                    controller._referenceCameraSettings = controller._scenario.CameraProvenance;
                }
            }
            catch (Exception exception)
            {
                controller.Fail(exception);
                return;
            }
            controller._timing = route && !noTiming && !check;
            controller._configCheck = check;
            controller.enabled = true;
        }

        private static string GetArgument(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException("Missing " + key);
            }
            return Path.GetFullPath(args[index + 1]);
        }

        private void Start()
        {
            try
            {
                if (_camera == null || _positions == null || _rotations == null || _positions.Length != 4 || _rotations.Length != 4)
                {
                    throw new InvalidOperationException("Clean route camera and four exact poses are required.");
                }
                string archive = Path.GetFullPath(@"E:\Projects\stalker_raid_into_darkness_Archive").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!_output.StartsWith(archive, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Route output must be inside Archive.");
                }
                Directory.CreateDirectory(_output);
                _cameraState = new BenchmarkCameraState(_camera);
                _originalCameraSettings = JsonUtility.ToJson(BenchmarkCameraSettings.Capture(_camera));
                _originalCameraPosition = _camera.transform.position;
                _originalCameraRotation = _camera.transform.rotation;
                if (_scenario != null)
                {
                    _scenario.CameraSettings.Apply(_camera);
                    File.WriteAllText(Path.Combine(_output, "route-profile.json"), JsonUtility.ToJson(_scenario, true));
                }
                if (_timing)
                {
                    _samples = new Sample[CAPACITY];
                }
                var weatherControllers = UnityEngine.Object.FindObjectsByType<XRayWeatherController>();
                if (weatherControllers.Length != 1 || _weatherProfile == null)
                {
                    throw new InvalidOperationException("Exactly one weather controller and the exact w_foggy3 profile are required.");
                }
                _weather = weatherControllers[0];
                if (_weather.IsTransitioning || _weather.ActiveFx != null)
                {
                    throw new InvalidOperationException("Weather transition or FX is active before the route.");
                }
                _savedWeather = _weather.ActiveProfile;
                _savedWeatherSeconds = _weather.GameTimeSeconds;
                _savedWeatherPaused = _weather.Paused;
                _weatherCaptured = true;
                if (_weather.ActiveProfile != _weatherProfile)
                {
                    _weather.SetProfile(_weatherProfile);
                }
                _weather.SetPaused(true);
                _weather.Seek(_scenario != null ? _scenario.FixedTimeHours * 3600 : 43200.0);
                if (XRayWeatherController.Active != _weather)
                {
                    throw new InvalidOperationException("Weather ownership is unavailable: " + _weather.Diagnostic);
                }
                MoveCamera(0);
                _before = CaptureConditions();
                File.WriteAllText(Path.Combine(_output, "conditions-before.json"), JsonUtility.ToJson(_before, true));
                RequireConditions(_before);
                if (AnomalyVisual08Target.Active)
                {
                    File.WriteAllText(Path.Combine(_output, "visual-state-before.json"), AnomalyVisual08Target.Snapshot());
                    if (AnomalyVisual08Target.Mode.StartsWith("Light10_", StringComparison.Ordinal) || AnomalyVisual08Target.Mode.StartsWith("Shadow11_", StringComparison.Ordinal))
                    {
                        File.WriteAllText(Path.Combine(_output, "light-state-before.json"), AnomalyLight10Inventory.Capture(AnomalyLight10Inventory.Targets(), _camera, false));
                    }
                }
                Application.logMessageReceived += OnLog;
                RenderPipelineManager.endCameraRendering += OnCameraRendered;
                _running = true;
                if (_configCheck && AnomalyVisual08Target.Active)
                {
                    if (AnomalyVisual08Target.Mode.StartsWith("Shadow11_", StringComparison.Ordinal))
                    {
                        _shadowCheck = gameObject.AddComponent<AnomalyShadow11Check>();
                        StartCoroutine(_shadowCheck.Run(_output, _camera));
                    }
                    else if (AnomalyVisual08Target.Mode.StartsWith("Light10_", StringComparison.Ordinal))
                    {
                        _lightCheck = gameObject.AddComponent<AnomalyLight10Check>();
                        StartCoroutine(_lightCheck.Run(_output, _camera));
                    }
                    else
                    {
                        _visualCheck = gameObject.AddComponent<AnomalyVisual08Check>();
                        StartCoroutine(_visualCheck.Run(_output, _camera, _technicalKiselPrefab));
                    }
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private void Update()
        {
            long timestamp = Stopwatch.GetTimestamp();
            if (AnomalyVisual09Events.Enabled)
            {
                AnomalyVisual09Events.Frame(timestamp, Time.frameCount);
            }
            if (!_running)
            {
                return;
            }
            if (_configCheck)
            {
                if (_configurationRenderFrames >= 3 && (_visualCheck == null || _visualCheck.Completed) && (_lightCheck == null || _lightCheck.Completed) && (_shadowCheck == null || _shadowCheck.Completed))
                {
                    _endTimestamp = timestamp;
                    Complete();
                }
                return;
            }
            int flags = 0;
            if (!_started)
            {
                if (!Application.isFocused)
                {
                    return;
                }
                _started = true;
                _phaseStart = timestamp;
                _phaseStarts[0] = timestamp;
                flags = BOUNDARY_FLAG;
                if (!Application.isFocused)
                {
                    _focusViolation = true;
                    _focusLostFrame = Time.frameCount;
                }
            }
            else if ((timestamp - _phaseStart) / (double)Stopwatch.Frequency >= (_position < 0 ? WARMUP_SECONDS : POSITION_SECONDS))
            {
                _position++;
                if (_position == 4)
                {
                    Store(timestamp, BOUNDARY_FLAG | END_FLAG);
                    _endTimestamp = timestamp;
                    Complete();
                    return;
                }
                MoveCamera(_position);
                _phaseStart = timestamp;
                _phaseStarts[_position + 1] = timestamp;
                flags = BOUNDARY_FLAG;
            }
            Store(timestamp, flags);
        }

        private void Store(long timestamp, int flags)
        {
            if (!_timing)
            {
                return;
            }
            if (_count >= CAPACITY)
            {
                _overflow = true;
                return;
            }
            _samples[_count++] = new Sample { Timestamp = timestamp, UnityFrame = Time.frameCount, Position = _position, Flags = flags };
        }

        private void MoveCamera(int position)
        {
            _camera.transform.SetPositionAndRotation(_positions[position], _rotations[position]);
        }

        private void OnCameraRendered(ScriptableRenderContext context, Camera camera)
        {
            if (!_running || camera != _camera || _lastRenderedFrame == Time.frameCount)
            {
                return;
            }
            _lastRenderedFrame = Time.frameCount;
            _configurationRenderFrames++;
            if (_started && _position < 4)
            {
                _renderedFrames[_position + 1]++;
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (_running && _started && !focused)
            {
                _focusViolation = true;
                if (_focusLostFrame < 0)
                {
                    _focusLostFrame = Time.frameCount;
                }
            }
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _runtimeErrors++;
            }
        }

        private Conditions CaptureConditions()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            bool oldBenchmark = false;
            for (int i = 0; i < assemblies.Length; i++)
            {
                oldBenchmark |= assemblies[i].GetName().Name == "Rvm.Benchmark.Runtime";
            }
            string[] modules;
            bool externalDiagnostics = false;
            bool nvidiaCaptureModuleLoaded = false;
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                modules = new string[process.Modules.Count];
                for (int i = 0; i < modules.Length; i++)
                {
                    modules[i] = process.Modules[i].FileName;
                    string name = Path.GetFileName(modules[i]);
                    nvidiaCaptureModuleLoaded |= name.IndexOf("nvspcap", StringComparison.OrdinalIgnoreCase) >= 0;
                    externalDiagnostics |= name.IndexOf("RTSSHooks", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("FramePacingNative", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("renderdoc", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("WinPixGpuCapturer", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            return new Conditions { unity = Application.unityVersion, buildGuid = Application.buildGUID, debugBuild = UnityEngine.Debug.isDebugBuild, frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), profilerEnabled = UnityEngine.Profiling.Profiler.enabled, eventJournalConfigured = AnomalyVisual09Events.Configured, oldBenchmarkAssemblyLoaded = oldBenchmark, nativeModules = modules, externalDiagnostics = externalDiagnostics, nvidiaCaptureModuleLoaded = nvidiaCaptureModuleLoaded, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), quality = QualitySettings.names[QualitySettings.GetQualityLevel()], width = Screen.width, height = Screen.height, fullScreenMode = Screen.fullScreenMode.ToString(), vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate, renderFrameInterval = OnDemandRendering.renderFrameInterval, runInBackground = Application.runInBackground, focused = Application.isFocused, camera = _camera.name, cameraEnabled = _camera.isActiveAndEnabled, fieldOfView = _camera.fieldOfView, nearClip = _camera.nearClipPlane, farClip = _camera.farClipPlane, cameraAspect = _camera.aspect, cameraRect = _camera.rect, cameraPixelWidth = _camera.pixelWidth, cameraPixelHeight = _camera.pixelHeight, renderWidthScale = ScalableBufferManager.widthScaleFactor, renderHeightScale = ScalableBufferManager.heightScaleFactor, orthographic = _camera.orthographic, orthographicSize = _camera.orthographicSize, focalLength = _camera.focalLength, sensorSize = _camera.sensorSize, lensShift = _camera.lensShift, cullingMask = _camera.cullingMask, allowHdr = _camera.allowHDR, allowMsaa = _camera.allowMSAA, dynamicResolution = _camera.allowDynamicResolution, physicalCamera = _camera.usePhysicalProperties, cameraTargetIsScreen = _camera.targetTexture == null, referenceCameraSettings = _referenceCameraSettings, timeScale = Time.timeScale, fixedDeltaTime = Time.fixedDeltaTime, weather = _weather.ActiveProfile.name, weatherSeconds = _weather.GameTimeSeconds, weatherPaused = _weather.Paused, particleDiagnostics = false, rendererDiagnostics = false };
        }

        private void RequireConditions(Conditions value)
        {
            if (_scenario != null && (value.fieldOfView != _scenario.CameraSettings.FieldOfView || value.nearClip != _scenario.CameraSettings.Near || value.farClip != _scenario.CameraSettings.Far)) throw new InvalidOperationException("Actual camera lens differs from the selected route profile.");
            if (value.debugBuild || value.frameTimingEnabled || value.profilerEnabled || value.externalDiagnostics || value.graphicsApi != "Direct3D12" || value.quality != "High Fidelity" || value.width != 1920 || value.height != 1080 || value.cameraPixelWidth != 1920 || value.cameraPixelHeight != 1080 || value.renderWidthScale != 1.0f || value.renderHeightScale != 1.0f || value.fullScreenMode != "FullScreenWindow" || value.vSyncCount != 0 || value.targetFrameRate != -1 || value.renderFrameInterval != 1 || !value.runInBackground || !value.cameraEnabled || !value.cameraTargetIsScreen || value.particleDiagnostics || value.rendererDiagnostics || value.weather != _weatherProfile.name || !value.weatherPaused || value.weatherSeconds != (_scenario != null ? _scenario.FixedTimeHours * 3600 : 43200.0))
            {
                throw new InvalidOperationException("Clean Release route conditions do not match the required configuration.");
            }
        }

        private void Complete()
        {
            try
            {
                SaveResult();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private void SaveResult()
        {
            _running = false;
            _saved = true;
            AnomalyVisual09Events.Save(_output);
            if (AnomalyVisual08Target.Active)
            {
                File.WriteAllText(Path.Combine(_output, "visual-state-after.json"), AnomalyVisual08Target.Snapshot());
                if (AnomalyVisual08Target.Mode.StartsWith("Light10_", StringComparison.Ordinal) || AnomalyVisual08Target.Mode.StartsWith("Shadow11_", StringComparison.Ordinal))
                {
                    File.WriteAllText(Path.Combine(_output, "light-state-after.json"), AnomalyLight10Inventory.Capture(AnomalyLight10Inventory.Targets(), _camera, true));
                }
            }
            Application.logMessageReceived -= OnLog;
            RenderPipelineManager.endCameraRendering -= OnCameraRendered;
            var after = CaptureConditions();
            RequireConditions(after);
            File.WriteAllText(Path.Combine(_output, "conditions-after.json"), JsonUtility.ToJson(after, true));
            if (_timing)
            {
                using (var writer = new StreamWriter(Path.Combine(_output, "timestamps.csv")))
                {
                    writer.WriteLine("sample,timestamp,unity_frame,position,flags");
                    for (int i = 0; i < _count; i++)
                    {
                        Sample sample = _samples[i];
                        writer.WriteLine(i.ToString(CultureInfo.InvariantCulture) + "," + sample.Timestamp.ToString(CultureInfo.InvariantCulture) + "," + sample.UnityFrame.ToString(CultureInfo.InvariantCulture) + "," + sample.Position.ToString(CultureInfo.InvariantCulture) + "," + sample.Flags.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            bool rendered = _configCheck ? _configurationRenderFrames >= 3 : _renderedFrames[1] > 0 && _renderedFrames[2] > 0 && _renderedFrames[3] > 0 && _renderedFrames[4] > 0;
            bool valid = !_overflow && !_focusViolation && _runtimeErrors == 0 && rendered;
            var result = new Result { status = valid ? "completed" : "conditions-violated", configurationCheckOnly = _configCheck, timing = _timing, stopwatchFrequency = Stopwatch.Frequency, capacity = CAPACITY, samples = _count, overflow = _overflow, foregroundViolation = _focusViolation, firstFocusLostUnityFrame = _focusLostFrame, runtimeErrors = _runtimeErrors, phaseStarts = _phaseStarts, endTimestamp = _endTimestamp, renderedFrames = _renderedFrames, configurationRenderFrames = _configurationRenderFrames, positions = _positions, rotations = _rotations, warmupSeconds = WARMUP_SECONDS, positionSeconds = POSITION_SECONDS, updateExecutionOrder = -9000 };
            File.WriteAllText(Path.Combine(_output, "result.json"), JsonUtility.ToJson(result, true));
            RestoreState();
            Application.Quit(valid ? 0 : 2);
        }

        private void Fail(Exception exception)
        {
            _running = false;
            RestoreState();
            Application.logMessageReceived -= OnLog;
            RenderPipelineManager.endCameraRendering -= OnCameraRendered;
            if (!string.IsNullOrEmpty(_output))
            {
                Directory.CreateDirectory(_output);
                File.WriteAllText(Path.Combine(_output, "failure.txt"), exception.ToString());
            }
            UnityEngine.Debug.LogError(exception);
            Application.Quit(1);
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            RenderPipelineManager.endCameraRendering -= OnCameraRendered;
            if (_running && !_saved)
            {
                _running = false;
            }
            RestoreState();
        }

        public void Abort()
        {
            _running = false;
            Application.logMessageReceived -= OnLog;
            RenderPipelineManager.endCameraRendering -= OnCameraRendered;
            RestoreState();
            enabled = false;
        }

        private void RestoreState()
        {
            try
            {
                _cameraState?.Dispose();
                _cameraState = null;
                if (_weatherCaptured && _weather != null)
                {
                    _weatherCaptured = false;
                    if (_weather.ActiveProfile != _savedWeather) _weather.SetProfile(_savedWeather);
                    _weather.Seek(_savedWeatherSeconds);
                    _weather.SetPaused(_savedWeatherPaused);
                }
            }
            finally
            {
                LocationBatchingBenchmark.RestoreMode();
                _pacing?.Dispose();
                _pacing = null;
            }
            if (_camera != null && !string.IsNullOrEmpty(_originalCameraSettings) && !string.IsNullOrEmpty(_output))
            {
                bool cameraRestored = JsonUtility.ToJson(BenchmarkCameraSettings.Capture(_camera)) == _originalCameraSettings && _camera.transform.position == _originalCameraPosition && Quaternion.Angle(_camera.transform.rotation, _originalCameraRotation) < 0.001f;
                bool weatherRestored = _weather == null || _weather.ActiveProfile == _savedWeather && Math.Abs(_weather.GameTimeSeconds - _savedWeatherSeconds) < 0.001 && _weather.Paused == _savedWeatherPaused;
                File.WriteAllText(Path.Combine(_output, "restoration.json"), JsonUtility.ToJson(new Restoration { cameraRestored = cameraRestored, weatherRestored = weatherRestored }, true));
                if (!cameraRestored || !weatherRestored) throw new InvalidOperationException("Route state restoration did not match the original state.");
            }
        }

        [Serializable]
        private sealed class Restoration
        {
            public bool cameraRestored;
            public bool weatherRestored;
        }

        private struct Sample
        {
            public long Timestamp;
            public int UnityFrame;
            public int Position;
            public int Flags;
        }

        [Serializable]
        private sealed class Result
        {
            public string status;
            public bool configurationCheckOnly;
            public bool timing;
            public long stopwatchFrequency;
            public int capacity;
            public int samples;
            public bool overflow;
            public bool foregroundViolation;
            public int firstFocusLostUnityFrame;
            public int runtimeErrors;
            public long[] phaseStarts;
            public long endTimestamp;
            public int[] renderedFrames;
            public int configurationRenderFrames;
            public Vector3[] positions;
            public Quaternion[] rotations;
            public double warmupSeconds;
            public double positionSeconds;
            public int updateExecutionOrder;
        }

        [Serializable]
        private sealed class Conditions
        {
            public string unity;
            public string buildGuid;
            public bool debugBuild;
            public bool frameTimingEnabled;
            public bool profilerEnabled;
            public bool eventJournalConfigured;
            public bool oldBenchmarkAssemblyLoaded;
            public string[] nativeModules;
            public bool externalDiagnostics;
            public bool nvidiaCaptureModuleLoaded;
            public string graphicsApi;
            public string quality;
            public int width;
            public int height;
            public string fullScreenMode;
            public int vSyncCount;
            public int targetFrameRate;
            public int renderFrameInterval;
            public bool runInBackground;
            public bool focused;
            public string camera;
            public bool cameraEnabled;
            public float fieldOfView;
            public float nearClip;
            public float farClip;
            public float cameraAspect;
            public Rect cameraRect;
            public int cameraPixelWidth;
            public int cameraPixelHeight;
            public float renderWidthScale;
            public float renderHeightScale;
            public bool orthographic;
            public float orthographicSize;
            public float focalLength;
            public Vector2 sensorSize;
            public Vector2 lensShift;
            public int cullingMask;
            public bool allowHdr;
            public bool allowMsaa;
            public bool dynamicResolution;
            public bool physicalCamera;
            public bool cameraTargetIsScreen;
            public string referenceCameraSettings;
            public float timeScale;
            public float fixedDeltaTime;
            public string weather;
            public double weatherSeconds;
            public bool weatherPaused;
            public bool particleDiagnostics;
            public bool rendererDiagnostics;
        }
    }
}
#endif
