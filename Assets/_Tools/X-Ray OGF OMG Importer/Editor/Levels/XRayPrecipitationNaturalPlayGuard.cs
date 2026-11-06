using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Editor.Levels
{
    [InitializeOnLoad]
    public static class XRayPrecipitationNaturalPlayGuard
    {
        private const string PROJECT = "E:\\Projects\\stalker_raid_into_darkness";
        private const string ARCHIVE = "E:\\Projects\\stalker_raid_into_darkness_Archive\\";
        private const string SESSION_KEY = "XRay.Phase08.NaturalEditorPlayGuard";
        private const string ROOT_CONFIG = "Assets/_Source/Settings/Resources/GameConfig.asset";
        private const string PRECIPITATION_CONFIG = "Assets/_Source/Settings/Resources/PrecipitationConfig.asset";
        private const string USER_TEXTURE = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/WeatherProfiles/2e431903b176_01232f8a/Textures/blowout4_1_night_small_weather.asset";
        private const int REQUIRED_FRAMES = 120;
        private const double MIN_PLAY_SECONDS = 5.0;
        private const double POST_EXIT_SECONDS = 5.0;
        private const double WATCHDOG_SECONDS = 150.0;
        private const double NATIVE_CAPTURE_SECONDS = 90.0;
        private const float NIGHT_EXPOSURE = 9.0f;
        private const float DAY_ART_CLOCK = 8.0f;

        private static readonly FieldInfo _nightClonesField = typeof(XRayWetMaterialBindings).GetField("_clones", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<Material, float> _nightDebugValues = new Dictionary<Material, float>();

        private static State _state;
        private static XRayWeatherController _controller;
        private static double _lastSave;
        private static int _pendingFatal;
        private static int _pendingGameFrames;
        private static int _pendingSceneFrames;
        private static string _threadFailure;
        private static bool _attached;
        private static Camera _nightCamera;
        private static RenderTexture _nightTarget;
        private static RenderTexture _nightSavedTarget;
        private static Vector3 _nightSavedPosition;
        private static Quaternion _nightSavedRotation;
        private static float _nightSavedFieldOfView;
        private static GameObject _nightVolume;
        private static VolumeProfile _nightExposure;
        private static AsyncGPUReadbackRequest _nightReadback;
        private static bool _nightReadbackPending;
        private static EditorWindow _nightGameView;
        private static bool _nightSavedRunInBackground;
        private static bool _nightOwnsBackground;
        private static bool _nightOwnsCamera;
        private static bool _nightOwnsArtProfile;
        private static XRayPuddleArtProfile? _nightSavedArtProfile;
        private static bool _nightOwnsPlanarWeight;
        private static float _nightSavedPlanarWeight;
        private static bool _nightOwnsDayPause;
        private static bool _nightSavedPaused;

        static XRayPrecipitationNaturalPlayGuard()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            EditorApplication.playModeStateChanged += PlayStateChanged;
        }

        public static string EnableCanonical(string archiveFolder)
        {
            Require(Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Equals(PROJECT, StringComparison.OrdinalIgnoreCase), "The active project must be Production.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating, "Enable only the canonical asset in an idle Editor.");
            GameConfig root = GameConfig.Load();
            PrecipitationConfig config = AssetDatabase.LoadAssetAtPath<PrecipitationConfig>(PRECIPITATION_CONFIG);
            Require(root != null && AssetDatabase.GetAssetPath(root) == ROOT_CONFIG && root.Precipitation == config && config != null, "Expected the canonical root and precipitation reference.");
            Require(!EditorUtility.IsDirty(root) && !EditorUtility.IsDirty(config), "Existing authored config changes must be preserved.");
            Require(config.TryValidate(out _, true) && config.WetnessCompute != null && config.WetSurfaceTextures != null, "Prepare and validate the shared resources before enabling.");
            string archive = Path.GetFullPath(archiveFolder);
            Require(archive.StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase), "The pre-mutation evidence must stay in Archive.");
            string output = Path.Combine(archive, "CanonicalEnable_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            string childPath = Path.Combine(PROJECT, PRECIPITATION_CONFIG);
            string beforeSha256 = Hash(File.ReadAllBytes(childPath));
            string beforeBytes = File.ReadAllText(childPath);
            string beforeJson = EditorJsonUtility.ToJson(config);
            string expectedJson = beforeJson.Replace("\"_wetnessEnabled\":false", "\"_wetnessEnabled\":true").Replace("\"_puddlesEnabled\":false", "\"_puddlesEnabled\":true");
            string guid = AssetDatabase.AssetPathToGUID(PRECIPITATION_CONFIG);
            var protectedFiles = new List<FileRecord>();
            foreach (string relative in new[] { ROOT_CONFIG, ROOT_CONFIG + ".meta", USER_TEXTURE, USER_TEXTURE + ".meta", PRECIPITATION_CONFIG + ".meta" })
            {
                string path = Path.Combine(PROJECT, relative);
                protectedFiles.Add(new FileRecord { path = path, sha256 = Hash(File.ReadAllBytes(path)) });
            }
            File.WriteAllText(Path.Combine(output, "Before.asset.txt"), beforeBytes);
            File.WriteAllText(Path.Combine(output, "Before.json"), beforeJson);
            var serialized = new SerializedObject(config);
            serialized.Update();
            serialized.FindProperty("_wetnessEnabled").boolValue = true;
            serialized.FindProperty("_puddlesEnabled").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            string afterJson = EditorJsonUtility.ToJson(config);
            Require(afterJson == expectedJson, "Only the two authorized serialized booleans may change.");
            AssetDatabase.SaveAssetIfDirty(config);
            foreach (FileRecord file in protectedFiles) Require(Hash(File.ReadAllBytes(file.path)) == file.sha256, "A protected root/reference/user asset changed: " + file.path);
            Require(guid == AssetDatabase.AssetPathToGUID(PRECIPITATION_CONFIG) && root.Precipitation == config && config.WetnessEnabled && config.PuddlesEnabled, "Canonical GUID/reference or enabled flags changed unexpectedly.");
            File.WriteAllText(Path.Combine(output, "After.asset.txt"), File.ReadAllText(childPath));
            File.WriteAllText(Path.Combine(output, "After.json"), afterJson);
            var report = new EnableRecord { child = PRECIPITATION_CONFIG, guid = guid, beforeSha256 = beforeSha256, afterSha256 = Hash(File.ReadAllBytes(childPath)), onlyAuthorizedPropertiesChanged = afterJson == expectedJson, wetness = config.WetnessEnabled, puddles = config.PuddlesEnabled, protectedFiles = protectedFiles.ToArray() };
            string result = Path.Combine(output, "Result.json");
            File.WriteAllText(result, JsonUtility.ToJson(report, true));
            return result;
        }

        public static string Begin(string archiveFolder, string expectedScenePath, bool uniformWetState)
        {
            return BeginInternal(archiveFolder, expectedScenePath, uniformWetState, null, null);
        }

        public static string BeginNightPuddles(string archiveFolder, string expectedScenePath, string posesPath)
        {
            return BeginPuddles(archiveFolder, expectedScenePath, posesPath, false);
        }

        public static string BeginDayPuddles(string archiveFolder, string expectedScenePath, string posesPath)
        {
            return BeginPuddles(archiveFolder, expectedScenePath, posesPath, true);
        }

        private static string BeginPuddles(string archiveFolder, string expectedScenePath, string posesPath, bool dayCapture)
        {
            Require(Path.GetFullPath(posesPath).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase) && File.Exists(posesPath), "Use the audited Military pose manifest from Archive.");
            string posesJson = File.ReadAllText(posesPath);
            NightPoses poses = JsonUtility.FromJson<NightPoses>(posesJson);
            NightPose puddles = null;
            if (poses?.poses != null) foreach (NightPose pose in poses.poses) if (pose.name == "Puddles") puddles = pose;
            Require(puddles != null, "The audited manifest must contain the real Puddles pose.");
            Require(!dayCapture || (int)puddles.artProfile >= 0 && (int)puddles.artProfile <= 2, "Day capture art profile must be AtmosphericZone0, SubtleRealistic1 or HeavyRain2.");
            Require(!dayCapture || !puddles.overridePlanarWeight || float.IsFinite(puddles.planarWeight) && puddles.planarWeight >= 0.0f && puddles.planarWeight <= 1.0f, "The explicit native planar capture weight must lie between zero and one.");
            bool hasGameCamera = false;
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) hasGameCamera |= camera.isActiveAndEnabled && camera.cameraType == CameraType.Game && camera.gameObject.scene.path == expectedScenePath;
            Require(hasGameCamera, "The water guard requires an enabled native Game camera in the loaded Military scene.");
            return BeginInternal(archiveFolder, expectedScenePath, true, puddles, posesJson, dayCapture, posesPath);
        }

        private static string BeginInternal(string archiveFolder, string expectedScenePath, bool uniformWetState, NightPose nightPose, string posesJson, bool dayCapture = false, string posesPath = null)
        {
            Require(Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Equals(PROJECT, StringComparison.OrdinalIgnoreCase), "The active project must be Production.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating, "The clean Editor must be idle outside Play Mode.");
            Require(string.IsNullOrEmpty(SessionState.GetString(SESSION_KEY, string.Empty)), "A natural Play guard already owns this session.");
            Require(PrefabStageUtility.GetCurrentPrefabStage() == null, "Leave Prefab Mode before the natural scene guard.");
            Scene target = SceneManager.GetSceneByPath(expectedScenePath);
            Require(target.IsValid() && target.isLoaded && expectedScenePath.StartsWith("Assets/_Source/Levels/Anomaly/", StringComparison.Ordinal) && expectedScenePath.IndexOf("l07_military", StringComparison.OrdinalIgnoreCase) >= 0, "Use the already loaded actual Military scene.");
            foreach (SceneRecord scene in Scenes()) Require(!scene.loaded || !SceneManager.GetSceneByPath(scene.path).isDirty, "No loaded scene may be dirty before the guard.");
            foreach (XRayWeatherController controller in Object.FindObjectsByType<XRayWeatherController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Require(!controller.PreviewActive, "End all existing weather previews before entering natural Play.");
            XRayWeatherController owner = FindController(expectedScenePath);
            GameConfig root = GameConfig.Load();
            PrecipitationConfig config = AssetDatabase.LoadAssetAtPath<PrecipitationConfig>(PRECIPITATION_CONFIG);
            Require(root != null && AssetDatabase.GetAssetPath(root) == ROOT_CONFIG && config != null && root.Precipitation == config, "The canonical GameConfig must reference the canonical precipitation asset.");
            Require(config.Enabled && config.WetnessEnabled && config.PuddlesEnabled && config.TryValidate(out _, true), "The authorized canonical enabled/wetness/puddles edit must be completed before this read-only guard.");
            Require(owner.Configuration == root && owner.EffectivePrecipitationConfig == config && owner.PrecipitationOverride == null, "The actual scene must inherit the canonical configuration without a precipitation session override.");
            Require(Shader.GetGlobalInteger("_MonolithWetnessOwner") == 0 && Shader.GetGlobalFloat("_MonolithWetnessActive") <= 0.5f, "No existing owner may publish wet globals before Play.");
            string archive = Path.GetFullPath(archiveFolder);
            Require(archive.StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase), "Guard reports must stay in Archive.");
            string output = Path.Combine(archive, "NaturalEditorPlay_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            _state = new State { phase = "WaitingForPlay", output = output, scene = expectedScenePath, startedUtc = DateTime.UtcNow.ToString("O"), deadlineUtc = DateTime.UtcNow.AddSeconds(WATCHDOG_SECONDS).ToString("O"), uniformRequested = uniformWetState, scenes = Scenes(), selection = SelectionIds(), bindingsBefore = Bindings(expectedScenePath), resourcesBefore = ResourcesCount(), logPath = Application.consoleLogPath, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), project = PROJECT };
            _state.nightPose = nightPose;
            _state.dayCapture = dayCapture;
            _state.capturePoseSourcePath = posesPath != null ? Path.GetFullPath(posesPath) : null;
            _state.capturePoseSourceSha256 = posesPath != null ? Hash(File.ReadAllBytes(posesPath)) : null;
            _state.captureDebugWetness = dayCapture && nightPose != null && nightPose.debugWetness;
            if (nightPose != null) File.WriteAllText(Path.Combine(output, dayCapture ? "DayInputPoses.json" : "NightInputPoses.json"), posesJson);
            _state.files = ProtectedFiles(expectedScenePath);
            _state.logOffset = File.Exists(_state.logPath) ? new FileInfo(_state.logPath).Length : 0;
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (camera.isActiveAndEnabled && camera.cameraType == CameraType.Game && camera.gameObject.scene.path == expectedScenePath) _state.originalGameCameras++;
            }
            EditorWindow focused = EditorWindow.focusedWindow;
            if (focused != null)
            {
                _state.focusedWindowId = focused.GetEntityId().ToString();
                _state.focusedWindowType = focused.GetType().AssemblyQualifiedName;
            }
            _state.activeSelection = Selection.activeObject != null ? GlobalObjectId.GetGlobalObjectIdSlow(Selection.activeObject).ToString() : string.Empty;
            Attach();
            Save();
            EditorApplication.EnterPlaymode();
            return Path.Combine(output, "Status.json");
        }

        private static void Update()
        {
            try
            {
                if (_state == null)
                {
                    string saved = SessionState.GetString(SESSION_KEY, string.Empty);
                    if (string.IsNullOrEmpty(saved)) return;
                    _state = JsonUtility.FromJson<State>(saved);
                    Attach();
                }
                _state.gameFrames += Interlocked.Exchange(ref _pendingGameFrames, 0);
                _state.sceneViewFrames += Interlocked.Exchange(ref _pendingSceneFrames, 0);
                _state.threadedFatalMessages += Interlocked.Exchange(ref _pendingFatal, 0);
                if (_threadFailure != null) Fail(_threadFailure);
                ReadNativeLog();
                if (DateTime.UtcNow > DateTime.Parse(_state.deadlineUtc).ToUniversalTime())
                {
                    Fail("The bounded natural Play watchdog expired.");
                    if (EditorApplication.isPlayingOrWillChangePlaymode) RequestExit();
                    else if (_state.phase != "ObservingExit")
                    {
                        Complete();
                        return;
                    }
                }
                if (_state.phase == "Running") TickPlay();
                if (!string.IsNullOrEmpty(_state.failure) && EditorApplication.isPlaying) RequestExit();
                if (_state.phase == "WaitingForPlay" && EditorApplication.isPlaying) EnteredPlay();
                if ((_state.phase == "Running" || _state.phase == "Exiting") && !EditorApplication.isPlayingOrWillChangePlaymode) EnteredEdit();
                if (_state.phase == "ObservingExit" && (DateTime.UtcNow - DateTime.Parse(_state.exitUtc).ToUniversalTime()).TotalSeconds >= POST_EXIT_SECONDS) Complete();
                if (_state != null && EditorApplication.timeSinceStartup - _lastSave >= 0.5) Save();
            }
            catch (Exception exception)
            {
                if (_state == null) return;
                Fail(exception.ToString());
                if (EditorApplication.isPlaying) RequestExit();
                else Complete();
            }
        }

        private static void TickPlay()
        {
            if (_controller == null) _controller = FindController(_state.scene);
            XRayPrecipitationSystem system = _controller.Precipitation;
            if (system != null && system.Enabled && system.Wetness != null && system.Wetness.Buffer != null && system.WetMaterials != null)
            {
                _state.resourcesObserved = true;
                _state.maximumClones = Mathf.Max(_state.maximumClones, system.WetMaterials.CloneCount);
                _state.maximumCustomBindings = Mathf.Max(_state.maximumCustomBindings, system.WetMaterials.CustomDrawBindingCount);
                _state.maximumEstimatedBytes = Math.Max(_state.maximumEstimatedBytes, system.EstimatedBytes);
                if (system.Reflections != null && system.Reflections.Probe != null)
                {
                    _state.maximumReflectionRequests = Mathf.Max(_state.maximumReflectionRequests, system.Reflections.CaptureRequests);
                    RenderTexture texture = system.Reflections.Probe.realtimeTexture;
                    if (texture != null)
                    {
                        _state.maximumReflectionUpdates = Math.Max(_state.maximumReflectionUpdates, texture.updateCount);
                        _state.reflectionResolution = Mathf.Max(_state.reflectionResolution, texture.width);
                    }
                }
                Require(Shader.GetGlobalFloat("_MonolithWetnessActive") > 0.5f, "The natural owner must publish active wet globals.");
                if (!_state.naturalInitialRecorded)
                {
                    PrecipitationConfig config = _controller.EffectivePrecipitationConfig;
                    Require(_controller.Configuration == GameConfig.Load() && AssetDatabase.GetAssetPath(config) == PRECIPITATION_CONFIG && _controller.PrecipitationOverride == null, "Natural Play must inherit the canonical root without a session override.");
                    _state.naturalInitialRecorded = true;
                    _state.naturalInitialRainDensity = system.RainDensity;
                    _state.naturalWetnessEnabled = config.WetnessEnabled;
                    _state.naturalPuddlesEnabled = config.PuddlesEnabled;
                    _state.naturalNonCameraReflectionsEnabled = config.NonCameraReflectionsEnabled && config.ReflectionIntensity > 0.0f;
                    _state.naturalConfigPath = AssetDatabase.GetAssetPath(config);
                    File.WriteAllText(Path.Combine(_state.output, "NaturalInitial.json"), JsonUtility.ToJson(_state, true));
                }
                if (_state.nightPose != null && !_state.nightConfigured) ConfigureNight();
                if (_state.uniformRequested && !_state.uniformApplied)
                {
                    system.Wetness.SetUniformState(1.0f, _state.nightPose != null ? 0.8f : 1.0f);
                    _state.uniformApplied = true;
                    Save();
                }
            }
            if (_state.originalGameCameras == 0 && SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Repaint();
            int frames = _state.originalGameCameras > 0 ? _state.gameFrames : _state.sceneViewFrames;
            double elapsed = (DateTime.UtcNow - DateTime.Parse(_state.playUtc).ToUniversalTime()).TotalSeconds;
            bool ready = elapsed >= MIN_PLAY_SECONDS && frames >= REQUIRED_FRAMES && _state.resourcesObserved && (!_state.uniformRequested || _state.uniformApplied);
            if (_state.nightPose != null)
            {
                if (_nightGameView != null) _nightGameView.Repaint();
                EditorApplication.QueuePlayerLoopUpdate();
                if (ready && _state.maximumReflectionUpdates > 0 && (!_state.nightPartialCaptured || _state.nightRenderedFrames - _state.nightStageRenderStart >= 30)) _state.nightCaptureRequested = true;
                if (_nightReadbackPending && _nightReadback.done) CompleteNightCapture();
                ready &= _state.nightCaptured;
            }
            if (ready) RequestExit();
            if (!_state.nightConfigured && elapsed > 30.0) Fail("The natural camera/resource requirements were not met within 30 seconds.");
            if (_state.nightConfigured && (DateTime.UtcNow - DateTime.Parse(_state.captureConfiguredUtc).ToUniversalTime()).TotalSeconds > NATIVE_CAPTURE_SECONDS) Fail("The configured native partial/full water capture did not complete within its bounded shader-warmup and rendering budget.");
        }

        private static void ConfigureNight()
        {
            XRayWeatherProfile profile = null;
            string cycle = _state.dayCapture ? "w_clear1" : "w_storm2";
            foreach (XRayWeatherProfile candidate in _controller.Catalog.Profiles) if (candidate != null && candidate.CycleName == cycle) profile = candidate;
            Require(profile != null, "The inherited real weather catalog must contain " + cycle + ".");
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!camera.isActiveAndEnabled || camera.cameraType != CameraType.Game || camera.gameObject.scene.path != _state.scene) continue;
                _nightCamera = camera;
                break;
            }
            Require(_nightCamera != null, "Keep the native Military Game camera enabled during the night guard.");
            _nightSavedPosition = _nightCamera.transform.position;
            _nightSavedRotation = _nightCamera.transform.rotation;
            _nightSavedFieldOfView = _nightCamera.fieldOfView;
            _nightSavedTarget = _nightCamera.targetTexture;
            _nightOwnsCamera = true;
            _state.captureSourceCameraPosition = _nightSavedPosition;
            _state.captureSourceCameraRotation = _nightSavedRotation;
            _state.captureSourceCameraFieldOfView = _nightSavedFieldOfView;
            _state.captureSourceCameraScene = _nightCamera.gameObject.scene.path;
            _state.captureSourceCameraPath = _nightCamera.name;
            for (Transform parent = _nightCamera.transform.parent; parent != null; parent = parent.parent) _state.captureSourceCameraPath = parent.name + "/" + _state.captureSourceCameraPath;
            if (_state.dayCapture && _state.nightPose.useFieldGroundHeight) ResolveDayLowPose();
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>()) if (window.GetType().FullName == "UnityEditor.GameView") _nightGameView = window;
            Require(_nightGameView != null, "Keep an existing Game view open for the native night capture.");
            _nightGameView.Focus();
            _nightSavedRunInBackground = Application.runInBackground;
            _nightOwnsBackground = true;
            Application.runInBackground = true;
            _nightCamera.transform.SetPositionAndRotation(_state.nightPose.position, _state.nightPose.rotation);
            _nightCamera.fieldOfView = _state.dayCapture && _state.nightPose.fov >= 10.0f && _state.nightPose.fov <= 150.0f ? _state.nightPose.fov : 60.0f;
            _nightTarget = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _nightTarget.name = _state.dayCapture ? "XRayDayWaterNativeCapture" : "XRayNightWaterNativeCapture";
            _nightTarget.hideFlags = HideFlags.HideAndDontSave;
            Require(_nightTarget.Create(), "Create the bounded native night framebuffer.");
            _nightCamera.targetTexture = _nightTarget;
            _nightExposure = ScriptableObject.CreateInstance<VolumeProfile>();
            _nightExposure.hideFlags = HideFlags.HideAndDontSave;
            Exposure exposure = _nightExposure.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(NIGHT_EXPOSURE);
            exposure.compensation.Override(0.0f);
            if (_state.dayCapture)
            {
                ScreenSpaceReflection reflections = _nightExposure.Add<ScreenSpaceReflection>(true);
                reflections.enabled.Override(false);
                reflections.enabledTransparent.Override(false);
            }
            _nightVolume = new GameObject(_state.dayCapture ? "Owned day water exposure" : "Owned night water exposure");
            _nightVolume.hideFlags = HideFlags.HideAndDontSave;
            _nightVolume.transform.SetParent(_controller.transform, false);
            _nightVolume.layer = XRayWeatherFogRenderer.GetVolumeLayer(_controller.gameObject);
            Volume volume = _nightVolume.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100000.0f;
            volume.sharedProfile = _nightExposure;
            _controller.StopFx();
            if (_state.dayCapture)
            {
                _nightSavedPaused = _controller.Paused;
                _nightOwnsDayPause = true;
            }
            _controller.SetPaused(true);
            _controller.SetTimeScale(0.0f);
            _controller.SetProfile(profile);
            _controller.Seek(_state.dayCapture ? 43230.0 : 60.0);
            _controller.SetRainVisualOverride(false);
            _controller.SetPaused(_state.dayCapture);
            XRayPrecipitationSystem system = _controller.Precipitation;
            if (_state.dayCapture)
            {
                _nightSavedArtProfile = system.ArtProfileOverride;
                _nightOwnsArtProfile = true;
                system.ArtProfileOverride = _state.nightPose.artProfile;
                _state.captureArtProfile = _state.nightPose.artProfile.ToString();
                _state.captureArtClock = DAY_ART_CLOCK;
            }
            system.Wetness.SetUniformState(1.0f, 0.8f);
            if (_state.dayCapture) system.Wetness.Publish(_controller.EffectivePrecipitationConfig, DAY_ART_CLOCK, system.RainDensity, system.ArtProfileOverride, _controller.Snapshot.WindVelocity);
            Require(system.Reflections != null && system.Reflections.Probe != null, "The inherited night configuration must own a native reflection probe.");
            if (_state.dayCapture)
            {
                _nightSavedPlanarWeight = system.Reflections.PlanarWeight;
                _nightOwnsPlanarWeight = true;
                float planarWeight = _state.nightPose.overridePlanarWeight ? _state.nightPose.planarWeight : 1.0f;
                system.Reflections.PlanarWeight = planarWeight;
                _state.capturePlanarRequestedWeight = planarWeight;
            }
            system.Reflections.Dispose();
            system.Reflections.Tick(_controller.EffectivePrecipitationConfig, 0.0f);
            _state.maximumReflectionRequests = 0;
            _state.maximumReflectionUpdates = 0;
            _state.nightPuddleState = 0.8f;
            _state.nightConfigured = true;
            _state.captureConfiguredUtc = DateTime.UtcNow.ToString("O");
            _state.captureBudgetSeconds = NATIVE_CAPTURE_SECONDS;
            Save();
        }

        private static void ResolveDayLowPose()
        {
            XRayPrecipitationSystem system = _controller.Precipitation;
            Vector4 mapping = system.Geometry.WorldMapping;
            Vector3 position = _state.nightPose.position;
            int column = Mathf.FloorToInt((position.x - mapping.x) * mapping.z * system.Wetness.Width);
            int row = Mathf.FloorToInt((position.z - mapping.y) * mapping.w * system.Wetness.Height);
            bool readable = system.Wetness.TryReadCell(column, row, out XRayWetnessCell cell);
            Require(float.IsFinite(_state.nightPose.groundReferenceY) && readable && float.IsFinite(cell.Heights.x) && float.IsFinite(cell.Heights.y) && cell.Heights.x >= cell.Heights.y, "The candidate low camera requires a valid existing native height field cell at its actual XZ position.");
            float ground = Mathf.Abs(cell.Heights.x - _state.nightPose.groundReferenceY) < Mathf.Abs(cell.Heights.y - _state.nightPose.groundReferenceY) ? cell.Heights.x : cell.Heights.y;
            _state.captureGroundHeights = cell.Heights;
            _state.captureGroundColumn = column;
            _state.captureGroundRow = row;
            _state.captureGroundSelectedY = ground;
            _state.captureLowPoseScope = "Candidate low view at native field height+0.45m. Height cell is measured; visibility, clipping and surface contact still require visual review.";
            position.y = ground + 0.45f;
            _state.nightPose.position = position;
        }

        private static void CompleteNightCapture()
        {
            _nightReadbackPending = false;
            Require(!_nightReadback.hasError, "Read the actual native night Game framebuffer asynchronously.");
            Color[] pixels = _nightReadback.GetData<Color>().ToArray();
            float minimum = float.PositiveInfinity;
            float maximum = 0.0f;
            double sum = 0.0;
            int nonBlack = 0;
            foreach (Color pixel in pixels)
            {
                Require(float.IsFinite(pixel.r) && float.IsFinite(pixel.g) && float.IsFinite(pixel.b), "Native night RGB must stay finite.");
                float value = pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f;
                minimum = Mathf.Min(minimum, value);
                maximum = Mathf.Max(maximum, value);
                sum += value;
                if (value > 0.000001f) nonBlack++;
            }
            Require(nonBlack > 1000 && sum / pixels.Length > 0.000001 && maximum - minimum > 0.00001f, "The actual night water framebuffer must be nonblack with a measurable luminance range.");
            var exr = new Texture2D(_nightTarget.width, _nightTarget.height, TextureFormat.RGBAFloat, false, true);
            var png = new Texture2D(_nightTarget.width, _nightTarget.height, TextureFormat.RGBA32, false, false);
            string label = (_state.captureDebugWetness ? "DayWetnessDebug" : _state.dayCapture ? "DayPuddles" : "NightPuddles") + (_state.nightPartialCaptured ? "Full" : "Partial");
            try
            {
                exr.SetPixels(pixels);
                File.WriteAllBytes(Path.Combine(_state.output, label + ".exr"), exr.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                for (int index = 0; index < pixels.Length; index++) pixels[index] = pixels[index].gamma;
                png.SetPixels(pixels);
                string path = Path.Combine(_state.output, label + ".png");
                File.WriteAllBytes(path, png.EncodeToPNG());
                _state.nightScreenshot = path;
                _state.nightScreenshotSha256 = Hash(File.ReadAllBytes(path));
            }
            finally
            {
                Object.DestroyImmediate(exr);
                Object.DestroyImmediate(png);
            }
            _state.nightMeanLuminance = sum / pixels.Length;
            _state.nightMaximumLuminance = maximum;
            _state.nightNonBlackPixels = nonBlack;
            XRayPrecipitationSystem system = _controller.Precipitation;
            Vector4 mapping = system.Geometry.WorldMapping;
            int column = Mathf.FloorToInt((_nightCamera.transform.position.x - mapping.x) * mapping.z * system.Wetness.Width);
            int row = Mathf.FloorToInt((_nightCamera.transform.position.z - mapping.y) * mapping.w * system.Wetness.Height);
            Require(system.Wetness.TryReadCell(column, row, out XRayWetnessCell cell), "Read the actual night wetness field at the audited puddles pose.");
            _state.nightMoisture = cell.Moisture;
            bool partialCapture = !_state.nightPartialCaptured;
            if (partialCapture)
            {
                _state.nightPartialScreenshot = _state.nightScreenshot;
                _state.nightPartialCaptured = true;
            }
            else _state.nightCaptured = true;
            File.WriteAllText(Path.Combine(_state.output, label + "Evidence.json"), JsonUtility.ToJson(_state, true));
            if (partialCapture)
            {
                _state.nightCaptureRequested = false;
                _state.nightPuddleState = 1.0f;
                _state.nightStageRenderStart = _state.nightRenderedFrames;
                system.Wetness.SetUniformState(1.0f, 1.0f);
            }
            Save();
        }

        private static void ReleaseNightCapture()
        {
            if (_nightCamera != null && _nightOwnsCamera)
            {
                _nightCamera.transform.SetPositionAndRotation(_nightSavedPosition, _nightSavedRotation);
                _nightCamera.fieldOfView = _nightSavedFieldOfView;
                _nightCamera.targetTexture = _nightSavedTarget;
                if (_state != null)
                {
                    _state.captureCameraRestored = _nightCamera.transform.position == _nightSavedPosition && _nightCamera.transform.rotation == _nightSavedRotation && _nightCamera.fieldOfView == _nightSavedFieldOfView && _nightCamera.targetTexture == _nightSavedTarget;
                    if (!_state.captureCameraRestored) Fail("The owned native capture camera transform, field of view or target was not restored.");
                }
            }
            _nightOwnsCamera = false;
            bool debugRestored = true;
            foreach (KeyValuePair<Material, float> entry in _nightDebugValues)
            {
                if (entry.Key == null) continue;
                entry.Key.SetFloat("_MonolithDebug", entry.Value);
                debugRestored &= entry.Key.GetFloat("_MonolithDebug") == entry.Value;
            }
            _nightDebugValues.Clear();
            if (_state != null) _state.captureDebugRestored = debugRestored;
            if (_nightOwnsArtProfile && _controller != null && _controller.Precipitation != null)
            {
                _controller.Precipitation.ArtProfileOverride = _nightSavedArtProfile;
                if (_state != null) _state.captureArtProfileRestored = _controller.Precipitation.ArtProfileOverride == _nightSavedArtProfile;
            }
            _nightOwnsArtProfile = false;
            if (_nightOwnsPlanarWeight && _controller != null && _controller.Precipitation != null && _controller.Precipitation.Reflections != null)
            {
                _controller.Precipitation.Reflections.PlanarWeight = _nightSavedPlanarWeight;
                if (_state != null) _state.capturePlanarWeightRestored = _controller.Precipitation.Reflections.PlanarWeight == _nightSavedPlanarWeight;
            }
            _nightOwnsPlanarWeight = false;
            if (_nightOwnsDayPause && _controller != null)
            {
                _controller.SetPaused(_nightSavedPaused);
                if (_state != null) _state.captureDayPauseRestored = _controller.Paused == _nightSavedPaused;
            }
            _nightOwnsDayPause = false;
            if (_nightTarget != null) CoreUtils.Destroy(_nightTarget);
            if (_nightVolume != null) CoreUtils.Destroy(_nightVolume);
            if (_nightExposure != null)
            {
                foreach (VolumeComponent component in _nightExposure.components) CoreUtils.Destroy(component);
                CoreUtils.Destroy(_nightExposure);
            }
            _nightCamera = null;
            _nightTarget = null;
            _nightSavedTarget = null;
            _nightVolume = null;
            _nightExposure = null;
            _nightReadbackPending = false;
            _nightGameView = null;
            if (_nightOwnsBackground) Application.runInBackground = _nightSavedRunInBackground;
            _nightOwnsBackground = false;
        }

        private static void PlayStateChanged(PlayModeStateChange change)
        {
            if (_state == null)
            {
                string saved = SessionState.GetString(SESSION_KEY, string.Empty);
                if (string.IsNullOrEmpty(saved)) return;
                _state = JsonUtility.FromJson<State>(saved);
                Attach();
            }
            if (change == PlayModeStateChange.EnteredPlayMode) EnteredPlay();
            if (change == PlayModeStateChange.EnteredEditMode) EnteredEdit();
        }

        private static void EnteredPlay()
        {
            if (_state.phase == "Running") return;
            _state.phase = "Running";
            _state.playUtc = DateTime.UtcNow.ToString("O");
            if (_state.originalGameCameras == 0 && SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Focus();
            Save();
        }

        private static void RequestExit()
        {
            if (_state.phase == "Exiting") return;
            _state.phase = "Exiting";
            Save();
            ReleaseNightCapture();
            Save();
            EditorApplication.ExitPlaymode();
        }

        private static void EnteredEdit()
        {
            if (_state.phase == "ObservingExit") return;
            if (_state.phase != "Exiting") Fail("Play Mode ended before the guard requested its natural exit.");
            _state.phase = "ObservingExit";
            _state.exitUtc = DateTime.UtcNow.ToString("O");
            _controller = null;
            Save();
        }

        private static void Attach()
        {
            if (_attached) return;
            Application.logMessageReceivedThreaded += ThreadedLog;
            RenderPipelineManager.beginCameraRendering += BeforeCameraRendered;
            RenderPipelineManager.endCameraRendering += CameraRendered;
            _attached = true;
        }

        private static void ThreadedLog(string message, string stack, LogType type)
        {
            if (!NativeFatal(message) && type != LogType.Exception && type != LogType.Assert) return;
            Interlocked.Increment(ref _pendingFatal);
            Interlocked.CompareExchange(ref _threadFailure, message, null);
        }

        private static void BeforeCameraRendered(ScriptableRenderContext context, Camera camera)
        {
            if (_state == null || !_state.nightConfigured || _state.phase != "Running" || camera != _nightCamera || _state.nightCaptured) return;
            try
            {
                camera.transform.SetPositionAndRotation(_state.nightPose.position, _state.nightPose.rotation);
                XRayPrecipitationSystem system = _controller.Precipitation;
                if (_state.captureDebugWetness) ApplyDayWetnessDebug(system.WetMaterials);
                system.Wetness.SetUniformState(1.0f, _state.nightPuddleState);
                if (_state.dayCapture) system.Wetness.Publish(_controller.EffectivePrecipitationConfig, DAY_ART_CLOCK, system.RainDensity, system.ArtProfileOverride, _controller.Snapshot.WindVelocity);
                else system.Wetness.Publish(_controller.EffectivePrecipitationConfig, system.RealTime, system.RainDensity);
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
            }
        }

        private static void ApplyDayWetnessDebug(XRayWetMaterialBindings bindings)
        {
            Require(bindings != null && _nightClonesField != null, "Wetness debug requires the existing runtime-owned material clone dictionary.");
            var clones = _nightClonesField.GetValue(bindings) as Dictionary<Material, Material>;
            Require(clones != null, "The inspected runtime clone dictionary schema must match.");
            foreach (Material material in clones.Values)
            {
                if (material == null || !material.HasProperty("_MonolithDebug")) continue;
                Require(!EditorUtility.IsPersistent(material) && (material.hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave, "Wetness diagnostic may change only transient owned material clones.");
                if (!_nightDebugValues.ContainsKey(material)) _nightDebugValues.Add(material, material.GetFloat("_MonolithDebug"));
                material.SetFloat("_MonolithDebug", 89.0f);
            }
            _state.captureDebugCloneCount = _nightDebugValues.Count;
        }

        private static void CameraRendered(ScriptableRenderContext context, Camera camera)
        {
            if (_state == null || _state.phase != "Running" || camera == null) return;
            if (camera.cameraType == CameraType.Game && camera.gameObject.scene.path == _state.scene) Interlocked.Increment(ref _pendingGameFrames);
            if (camera.cameraType == CameraType.SceneView && SceneManager.GetSceneByPath(_state.scene).isLoaded) Interlocked.Increment(ref _pendingSceneFrames);
            if (_state.nightConfigured && camera == _nightCamera) _state.nightRenderedFrames++;
            if (_state.nightConfigured && camera == _nightCamera && _state.nightCaptureRequested && !_state.nightCaptured && !_nightReadbackPending)
            {
                try
                {
                    XRayPrecipitationSystem system = _controller.Precipitation;
                    Require(_controller.ActiveProfile.CycleName == (_state.dayCapture ? "w_clear1" : "w_storm2") && Math.Abs(_controller.GameTimeSeconds - (_state.dayCapture ? 43230.0 : 60.0)) < 0.001 && _controller.TimeScale == 0.0f && _controller.Paused == _state.dayCapture, "Render the selected authored snapshot; Day freezes source wind and physical time while Night retains live physical simulation.");
                    Require(!_state.dayCapture || _controller.SourceLightColor.sqrMagnitude > 0.000001f, "The authored clear daytime snapshot must retain nonzero direct sunlight.");
                    Require(_controller.EffectivePrecipitationConfig.WetnessEnabled && _controller.EffectivePrecipitationConfig.PuddlesEnabled && system.Reflections.Probe.realtimeTexture.updateCount > 0, "Night water and a fresh native cubemap capture must remain enabled.");
                    _state.nightSnapshot = _controller.Snapshot;
                    _state.nightProfile = _controller.ActiveProfile.CycleName;
                    _state.nightWetGlobals = Shader.GetGlobalVector("_MonolithWetnessSettings");
                    _state.nightProbeUpdates = system.Reflections.Probe.realtimeTexture.updateCount;
                    _state.nightProbePosition = system.Reflections.Probe.transform.position;
                    _state.nightCameraPosition = camera.transform.position;
                    _state.nightCameraRotation = camera.transform.rotation;
                    _state.captureFieldOfView = camera.fieldOfView;
                    _state.capturePuddleShape = Shader.GetGlobalVector("_MonolithPuddleShape");
                    _state.capturePuddleOptics = Shader.GetGlobalVector("_MonolithPuddleOptics");
                    _state.capturePuddleRipple = Shader.GetGlobalVector("_MonolithPuddleRipple");
                    _state.captureSimulationPaused = _controller.Paused;
                    _state.capturePrecipitationRealTime = system.RealTime;
                    _state.capturePlanarConfigured = _controller.EffectivePrecipitationConfig.PlanarReflectionsEnabled;
                    _state.capturePlanarGroundHeights = system.Reflections.PlanarGroundHeights;
                    _state.capturePlanarGroundHeight = system.Reflections.PlanarGroundHeight;
                    _state.capturePlanarGroundValid = system.Reflections.PlanarGroundValid;
                    PlanarReflectionProbe planar = system.Reflections.PlanarProbe;
                    if (planar != null)
                    {
                        _state.capturePlanarAvailable = true;
                        _state.capturePlanarPosition = planar.transform.position;
                        _state.capturePlanarInfluenceBox = planar.influenceVolume.boxSize;
                        _state.capturePlanarActualWeight = planar.weight;
                        if (planar.realtimeTexture != null)
                        {
                            _state.capturePlanarTextureUpdates = planar.realtimeTexture.updateCount;
                            _state.capturePlanarTextureWidth = planar.realtimeTexture.width;
                            _state.capturePlanarTextureHeight = planar.realtimeTexture.height;
                        }
                    }
                    _state.nightWidth = _nightTarget.width;
                    _state.nightHeight = _nightTarget.height;
                    Exposure actualExposure = HDCamera.GetOrCreate(camera).volumeStack.GetComponent<Exposure>();
                    Require(actualExposure != null && actualExposure.mode.value == ExposureMode.Fixed && Mathf.Abs(actualExposure.fixedExposure.value - NIGHT_EXPOSURE) < 0.001f, "The actual native night camera must use the controlled reference exposure.");
                    _state.nightFixedExposure = actualExposure.fixedExposure.value;
                    ScreenSpaceReflection actualReflections = HDCamera.GetOrCreate(camera).volumeStack.GetComponent<ScreenSpaceReflection>();
                    _state.captureSsrEnabled = actualReflections != null && actualReflections.enabled.value;
                    Require(!_state.dayCapture || !_state.captureSsrEnabled, "The native Day reflection comparison requires SSR disabled in the applied camera volume stack.");
                    _nightReadback = AsyncGPUReadback.Request(_nightTarget, 0, TextureFormat.RGBAFloat);
                    _nightReadbackPending = true;
                }
                catch (Exception exception)
                {
                    Fail(exception.ToString());
                }
            }
        }

        private static void ReadNativeLog()
        {
            if (!File.Exists(_state.logPath)) return;
            using (var stream = new FileStream(_state.logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < _state.logOffset) _state.logOffset = 0;
                stream.Position = _state.logOffset;
                byte[] bytes = new byte[(int)Math.Min(131072L, stream.Length - stream.Position)];
                int count = stream.Read(bytes, 0, bytes.Length);
                _state.logOffset += count;
                string text = _state.logRemainder + Encoding.UTF8.GetString(bytes, 0, count);
                int last = text.LastIndexOf('\n');
                if (last < 0)
                {
                    _state.logRemainder = text;
                    return;
                }
                foreach (string line in text.Substring(0, last).Split('\n'))
                {
                    if (!NativeFatal(line)) continue;
                    _state.nativeLogFatalLines++;
                    Fail(line);
                }
                _state.logRemainder = text.Substring(last + 1);
            }
        }

        private static bool NativeFatal(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("Texture index out of range", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("D3D12Fence", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("m_CmdState", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("DXGI_ERROR_DEVICE", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("device removed", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("device hung", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("device lost", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Assertion failed", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Crash!!!", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void Complete()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                Require(JsonUtility.ToJson(new SceneList { scenes = Scenes() }) == JsonUtility.ToJson(new SceneList { scenes = _state.scenes }), "The loaded/active scene setup changed.");
                foreach (SceneRecord scene in Scenes()) Require(!scene.loaded || !SceneManager.GetSceneByPath(scene.path).isDirty, "A scene became dirty; the guard never saves or discards it.");
                foreach (FileRecord file in _state.files) Require(File.Exists(file.path) && Hash(File.ReadAllBytes(file.path)) == file.sha256, "A protected asset or scene file changed: " + file.path);
                Require(Bindings(_state.scene) == _state.bindingsBefore, "Source material/BatchSet/Database references were not restored.");
                Require(JsonUtility.ToJson(ResourcesCount()) == JsonUtility.ToJson(_state.resourcesBefore), "Owned wet material/height-atlas resources did not return to the starting count.");
                Require(Shader.GetGlobalInteger("_MonolithWetnessOwner") == 0 && Shader.GetGlobalFloat("_MonolithWetnessActive") <= 0.5f, "Wet global ownership was not released.");
                Require(_state.resourcesObserved && _state.maximumClones > 0 && (_state.originalGameCameras > 0 ? _state.gameFrames : _state.sceneViewFrames) >= REQUIRED_FRAMES, "Required natural rendering/resource evidence is incomplete.");
                Require(!_state.naturalNonCameraReflectionsEnabled || _state.maximumReflectionRequests > 0 && _state.maximumReflectionUpdates > 0, "The inherited native wet reflection probe never completed a cubemap capture.");
                Require(_state.nightPose == null || _state.nightCaptured && _state.nightPartialCaptured && _state.nightProbeUpdates > 0 && _state.nightWetGlobals.x > 0.0f && _state.nightWetGlobals.y > 0.0f && File.Exists(_state.nightScreenshot) && File.Exists(_state.nightPartialScreenshot), "The actual night wetness, partial/full puddles and completed probe screenshot evidence is incomplete.");
                Require(_state.nightPose == null || _state.captureCameraRestored, "The capture must restore the existing native camera before leaving Play Mode.");
                Require(!_state.dayCapture || _state.captureArtProfileRestored, "Restore the inherited art profile after the owned daytime capture.");
                Require(!_state.captureDebugWetness || _state.captureDebugRestored, "Restore all surviving runtime clone diagnostic values.");
                Require(!_state.dayCapture || _state.capturePlanarWeightRestored && _state.captureDayPauseRestored, "Restore the original planar capture weight and simulation pause state.");
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
            try
            {
                RestoreSelection();
                foreach (EditorWindow focused in Resources.FindObjectsOfTypeAll<EditorWindow>())
                {
                    if (focused.GetEntityId().ToString() != _state.focusedWindowId || focused.GetType().AssemblyQualifiedName != _state.focusedWindowType) continue;
                    focused.Focus();
                    break;
                }
            }
            catch (Exception exception)
            {
                Fail("Host UI restoration failed: " + exception.Message);
            }
            _state.phase = string.IsNullOrEmpty(_state.failure) ? "Complete" : "Failed";
            _state.completedUtc = DateTime.UtcNow.ToString("O");
            _state.noGameCameraLimitation = _state.originalGameCameras == 0;
            Save();
            File.WriteAllText(Path.Combine(_state.output, "Result.json"), JsonUtility.ToJson(_state, true));
            SessionState.EraseString(SESSION_KEY);
            Application.logMessageReceivedThreaded -= ThreadedLog;
            RenderPipelineManager.beginCameraRendering -= BeforeCameraRendered;
            RenderPipelineManager.endCameraRendering -= CameraRendered;
            _state = null;
            _controller = null;
            _attached = false;
            _threadFailure = null;
            ReleaseNightCapture();
        }

        private static void RestoreSelection()
        {
            var restored = new List<Object>();
            foreach (string text in _state.selection)
            {
                if (!GlobalObjectId.TryParse(text, out GlobalObjectId id)) continue;
                Object value = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                if (value != null) restored.Add(value);
            }
            Selection.objects = restored.ToArray();
            if (GlobalObjectId.TryParse(_state.activeSelection, out GlobalObjectId active)) Selection.activeObject = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(active);
            _state.selectionRestored = JsonUtility.ToJson(new SelectionList { ids = SelectionIds() }) == JsonUtility.ToJson(new SelectionList { ids = _state.selection });
            if (!_state.selectionRestored) Fail("The original selection could not be restored exactly.");
        }

        private static XRayWeatherController FindController(string path)
        {
            XRayWeatherController found = null;
            foreach (XRayWeatherController controller in Object.FindObjectsByType<XRayWeatherController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (controller.gameObject.scene.path != path) continue;
                Require(found == null, "The actual source scene must contain one weather controller.");
                found = controller;
            }
            Require(found != null && found.isActiveAndEnabled, "The actual source weather controller is missing or disabled.");
            return found;
        }

        private static SceneRecord[] Scenes()
        {
            var scenes = new List<SceneRecord>();
            foreach (SceneSetup scene in EditorSceneManager.GetSceneManagerSetup()) scenes.Add(new SceneRecord { path = scene.path, loaded = scene.isLoaded, active = scene.isActive });
            return scenes.ToArray();
        }

        private static string[] SelectionIds()
        {
            var ids = new List<string>();
            foreach (Object value in Selection.objects) ids.Add(GlobalObjectId.GetGlobalObjectIdSlow(value).ToString());
            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        private static string Bindings(string path)
        {
            var values = new List<string>();
            foreach (GameObject root in SceneManager.GetSceneByPath(path).GetRootGameObjects())
            {
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string entry = GlobalObjectId.GetGlobalObjectIdSlow(renderer).ToString();
                    foreach (Material material in renderer.sharedMaterials) entry += "|" + (material != null ? GlobalObjectId.GetGlobalObjectIdSlow(material).ToString() : "null");
                    values.Add(entry);
                }
                foreach (XRayLevelInstancedRenderer renderer in root.GetComponentsInChildren<XRayLevelInstancedRenderer>(true))
                {
                    Require(renderer.MaterialResolver == null, "An external custom material resolver requires separate preservation evidence.");
                    values.Add(GlobalObjectId.GetGlobalObjectIdSlow(renderer) + "|" + (renderer.BatchSet != null ? GlobalObjectId.GetGlobalObjectIdSlow(renderer.BatchSet).ToString() : "null"));
                }
                foreach (XRayDetailVegetation renderer in root.GetComponentsInChildren<XRayDetailVegetation>(true))
                {
                    Require(renderer.MaterialResolver == null, "An external detail material resolver requires separate preservation evidence.");
                    values.Add(GlobalObjectId.GetGlobalObjectIdSlow(renderer) + "|" + (renderer.Database != null ? GlobalObjectId.GetGlobalObjectIdSlow(renderer.Database).ToString() : "null"));
                }
            }
            values.Sort(StringComparer.Ordinal);
            return Hash(Encoding.UTF8.GetBytes(string.Join("\n", values)));
        }

        private static ResourceRecord ResourcesCount()
        {
            var record = new ResourceRecord();
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (material != null && material.name.EndsWith(" (Precipitation)", StringComparison.Ordinal) && (material.hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave) record.materialClones++;
            }
            foreach (RenderTexture texture in Resources.FindObjectsOfTypeAll<RenderTexture>())
            {
                if (texture != null && texture.name == "XRay precipitation world heights") record.heightAtlases++;
            }
            return record;
        }

        private static FileRecord[] ProtectedFiles(string scene)
        {
            var records = new List<FileRecord>();
            foreach (string relative in new[] { ROOT_CONFIG, PRECIPITATION_CONFIG, USER_TEXTURE, scene })
            {
                string path = Path.GetFullPath(Path.Combine(PROJECT, relative));
                Require(path.StartsWith(PROJECT + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path), "Protected source file is missing: " + path);
                records.Add(new FileRecord { path = path, sha256 = Hash(File.ReadAllBytes(path)) });
                Require(File.Exists(path + ".meta"), "Protected source meta is missing: " + path);
                records.Add(new FileRecord { path = path + ".meta", sha256 = Hash(File.ReadAllBytes(path + ".meta")) });
            }
            return records.ToArray();
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void Fail(string message)
        {
            if (_state != null && string.IsNullOrEmpty(_state.failure)) _state.failure = string.IsNullOrEmpty(message) ? "Guard failure without error text." : message;
        }

        private static void Save()
        {
            string json = JsonUtility.ToJson(_state, true);
            SessionState.SetString(SESSION_KEY, json);
            File.WriteAllText(Path.Combine(_state.output, "Status.json"), json);
            _lastSave = EditorApplication.timeSinceStartup;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable]
        private sealed class State
        {
            public string phase;
            public string output;
            public string project;
            public string graphicsApi;
            public string scene;
            public string startedUtc;
            public string deadlineUtc;
            public string playUtc;
            public string exitUtc;
            public string completedUtc;
            public string failure;
            public string logPath;
            public string logRemainder;
            public long logOffset;
            public string[] selection;
            public string activeSelection;
            public string focusedWindowId;
            public string focusedWindowType;
            public string bindingsBefore;
            public SceneRecord[] scenes;
            public FileRecord[] files;
            public ResourceRecord resourcesBefore;
            public int originalGameCameras;
            public int gameFrames;
            public int sceneViewFrames;
            public int threadedFatalMessages;
            public int nativeLogFatalLines;
            public int maximumClones;
            public int maximumCustomBindings;
            public long maximumEstimatedBytes;
            public int maximumReflectionRequests;
            public uint maximumReflectionUpdates;
            public int reflectionResolution;
            public bool uniformRequested;
            public bool uniformApplied;
            public bool resourcesObserved;
            public bool selectionRestored;
            public bool noGameCameraLimitation;
            public bool naturalInitialRecorded;
            public bool naturalWetnessEnabled;
            public bool naturalPuddlesEnabled;
            public bool naturalNonCameraReflectionsEnabled;
            public float naturalInitialRainDensity;
            public string naturalConfigPath;
            public bool dayCapture;
            public string captureConfiguredUtc;
            public double captureBudgetSeconds;
            public float captureFieldOfView;
            public bool captureSsrEnabled;
            public string capturePoseSourcePath;
            public string capturePoseSourceSha256;
            public string captureSourceCameraScene;
            public string captureSourceCameraPath;
            public Vector3 captureSourceCameraPosition;
            public Quaternion captureSourceCameraRotation;
            public float captureSourceCameraFieldOfView;
            public bool captureCameraRestored;
            public string captureArtProfile;
            public float captureArtClock;
            public Vector4 capturePuddleShape;
            public Vector4 capturePuddleOptics;
            public Vector4 capturePuddleRipple;
            public bool captureArtProfileRestored;
            public Vector2 captureGroundHeights;
            public int captureGroundColumn;
            public int captureGroundRow;
            public float captureGroundSelectedY;
            public string captureLowPoseScope;
            public bool captureDebugWetness;
            public int captureDebugCloneCount;
            public bool captureDebugRestored;
            public bool capturePlanarConfigured;
            public bool capturePlanarAvailable;
            public Vector2 capturePlanarGroundHeights;
            public float capturePlanarGroundHeight;
            public bool capturePlanarGroundValid;
            public Vector3 capturePlanarPosition;
            public Vector3 capturePlanarInfluenceBox;
            public float capturePlanarActualWeight;
            public uint capturePlanarTextureUpdates;
            public int capturePlanarTextureWidth;
            public int capturePlanarTextureHeight;
            public float capturePlanarRequestedWeight;
            public bool capturePlanarWeightRestored;
            public bool captureSimulationPaused;
            public float capturePrecipitationRealTime;
            public bool captureDayPauseRestored;
            public NightPose nightPose;
            public bool nightConfigured;
            public bool nightCaptureRequested;
            public bool nightCaptured;
            public bool nightPartialCaptured;
            public string nightProfile;
            public string nightScreenshot;
            public string nightPartialScreenshot;
            public string nightScreenshotSha256;
            public XRayWeatherSnapshot nightSnapshot;
            public Vector4 nightWetGlobals;
            public Vector4 nightMoisture;
            public float nightPuddleState;
            public uint nightProbeUpdates;
            public Vector3 nightProbePosition;
            public Vector3 nightCameraPosition;
            public Quaternion nightCameraRotation;
            public int nightWidth;
            public int nightHeight;
            public float nightFixedExposure;
            public double nightMeanLuminance;
            public float nightMaximumLuminance;
            public int nightNonBlackPixels;
            public int nightRenderedFrames;
            public int nightStageRenderStart;
        }

        [Serializable]
        private sealed class NightPose
        {
            public string name;
            public Vector3 position;
            public Quaternion rotation;
            public float fov;
            public XRayPuddleArtProfile artProfile;
            public bool useFieldGroundHeight;
            public float groundReferenceY;
            public bool debugWetness;
            public float planarWeight = 1.0f;
            public bool overridePlanarWeight;
        }

        [Serializable]
        private sealed class NightPoses
        {
            public NightPose[] poses;
        }

        [Serializable]
        private sealed class EnableRecord
        {
            public string child;
            public string guid;
            public string beforeSha256;
            public string afterSha256;
            public bool onlyAuthorizedPropertiesChanged;
            public bool wetness;
            public bool puddles;
            public FileRecord[] protectedFiles;
        }

        [Serializable]
        private sealed class SceneRecord
        {
            public string path;
            public bool loaded;
            public bool active;
        }

        [Serializable]
        private sealed class SceneList
        {
            public SceneRecord[] scenes;
        }

        [Serializable]
        private sealed class SelectionList
        {
            public string[] ids;
        }

        [Serializable]
        private sealed class FileRecord
        {
            public string path;
            public string sha256;
        }

        [Serializable]
        private sealed class ResourceRecord
        {
            public int materialClones;
            public int heightAtlases;
        }
    }
}
