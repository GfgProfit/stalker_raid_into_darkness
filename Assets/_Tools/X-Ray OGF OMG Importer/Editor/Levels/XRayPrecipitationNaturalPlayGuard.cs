using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
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

        private static State _state;
        private static XRayWeatherController _controller;
        private static double _lastSave;
        private static int _pendingFatal;
        private static int _pendingGameFrames;
        private static int _pendingSceneFrames;
        private static string _threadFailure;
        private static bool _attached;

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
                Require(Shader.GetGlobalFloat("_MonolithWetnessActive") > 0.5f, "The natural owner must publish active wet globals.");
                if (!_state.naturalInitialRecorded)
                {
                    PrecipitationConfig config = _controller.EffectivePrecipitationConfig;
                    Require(_controller.Configuration == GameConfig.Load() && AssetDatabase.GetAssetPath(config) == PRECIPITATION_CONFIG && _controller.PrecipitationOverride == null, "Natural Play must inherit the canonical root without a session override.");
                    _state.naturalInitialRecorded = true;
                    _state.naturalInitialRainDensity = system.RainDensity;
                    _state.naturalWetnessEnabled = config.WetnessEnabled;
                    _state.naturalPuddlesEnabled = config.PuddlesEnabled;
                    _state.naturalConfigPath = AssetDatabase.GetAssetPath(config);
                    File.WriteAllText(Path.Combine(_state.output, "NaturalInitial.json"), JsonUtility.ToJson(_state, true));
                }
                if (_state.uniformRequested && !_state.uniformApplied)
                {
                    system.Wetness.SetUniformState(1.0f, 1.0f);
                    _state.uniformApplied = true;
                    Save();
                }
            }
            if (_state.originalGameCameras == 0 && SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Repaint();
            int frames = _state.originalGameCameras > 0 ? _state.gameFrames : _state.sceneViewFrames;
            double elapsed = (DateTime.UtcNow - DateTime.Parse(_state.playUtc).ToUniversalTime()).TotalSeconds;
            if (elapsed >= MIN_PLAY_SECONDS && frames >= REQUIRED_FRAMES && _state.resourcesObserved && (!_state.uniformRequested || _state.uniformApplied)) RequestExit();
            if (elapsed > 30.0) Fail("The natural camera/resource requirements were not met within 30 seconds.");
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
            RenderPipelineManager.endCameraRendering += CameraRendered;
            _attached = true;
        }

        private static void ThreadedLog(string message, string stack, LogType type)
        {
            if (!NativeFatal(message) && type != LogType.Exception && type != LogType.Assert) return;
            Interlocked.Increment(ref _pendingFatal);
            Interlocked.CompareExchange(ref _threadFailure, message, null);
        }

        private static void CameraRendered(ScriptableRenderContext context, Camera camera)
        {
            if (_state == null || _state.phase != "Running" || camera == null) return;
            if (camera.cameraType == CameraType.Game && camera.gameObject.scene.path == _state.scene) Interlocked.Increment(ref _pendingGameFrames);
            if (camera.cameraType == CameraType.SceneView && SceneManager.GetSceneByPath(_state.scene).isLoaded) Interlocked.Increment(ref _pendingSceneFrames);
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
            RenderPipelineManager.endCameraRendering -= CameraRendered;
            _state = null;
            _controller = null;
            _attached = false;
            _threadFailure = null;
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
            public bool uniformRequested;
            public bool uniformApplied;
            public bool resourcesObserved;
            public bool selectionRestored;
            public bool noGameCameraLimitation;
            public bool naturalInitialRecorded;
            public bool naturalWetnessEnabled;
            public bool naturalPuddlesEnabled;
            public float naturalInitialRainDensity;
            public string naturalConfigPath;
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
