using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Rvm.Benchmark;
using Rvm.Benchmark.Editor;

namespace Rvm.CleanReleaseRoute07.Editor
{
    public sealed class CleanReleaseBuild : IProcessSceneWithReport
    {
        private const string ROUTE_REFERENCE_PATH = "Assets/_BENCHMARK/Scenarios/l07_military.route.json";

        private static bool _building;
        private static string _task;
        private static RouteReference _reference;
        private static BenchmarkScenario[] _profiles;
        private static string _sceneGuid;

        public int callbackOrder => -20000;

        public static void BuildFromCommandLine()
        {
            string task = Environment.GetEnvironmentVariable("CLEAN_RELEASE_ROUTE07_OUTPUT");
            if (string.IsNullOrEmpty(task))
            {
                throw new InvalidOperationException("CLEAN_RELEASE_ROUTE07_OUTPUT is required.");
            }
            _task = Path.GetFullPath(task);
            string expectedProject = @"E:\Projects\stalker_raid_into_darkness";
            if (!string.Equals(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), expectedProject, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Build must use canonical Production.");
            }
            foreach (var scene in EnumerableScenes())
            {
                if (scene.isDirty)
                {
                    throw new InvalidOperationException("Unsaved scene blocks clean release build: " + scene.path);
                }
            }
            if (Application.unityVersion != "6000.6.7f1" || EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 || PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.Mono2x)
            {
                throw new InvalidOperationException("Exact Unity6000.6.7f1, Windows64 and Mono backend are required.");
            }
            HdrpShadowPackagePatch.RequireApplied();
            string referencePath = Path.Combine(_task, "ROUTE_REFERENCE.json");
            if (!File.Exists(referencePath))
            {
                Directory.CreateDirectory(_task);
                File.Copy(ROUTE_REFERENCE_PATH, referencePath);
            }
            _sceneGuid = Environment.GetEnvironmentVariable("BENCHMARK_SCENE_GUID");
            if (string.IsNullOrEmpty(_sceneGuid)) _sceneGuid = AssetDatabase.AssetPathToGUID(SceneManager.GetActiveScene().path);
            string scenePath = AssetDatabase.GUIDToAssetPath(_sceneGuid);
            var profiles = new List<BenchmarkScenario>();
            foreach (string guid in AssetDatabase.FindAssets("t:BenchmarkScenario", new[] { "Assets/_BENCHMARK" }))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<BenchmarkScenario>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate.SceneGuid != _sceneGuid) continue;
                string error = candidate.ValidateRoute(_sceneGuid);
                if (error != null || candidate.WarmupSeconds != 8 || candidate.PositionSeconds != 5 || candidate.StabilizationSeconds != 0.5 || !candidate.FixedWeatherTime) throw new InvalidOperationException(error ?? "Route does not match the fixed Release protocol.");
                profiles.Add(candidate);
            }
            if (profiles.Count == 0) throw new InvalidOperationException("No verified benchmark route is available for the selected scene GUID: " + _sceneGuid);
            profiles.Sort((a, b) => string.CompareOrdinal(a.ScenarioId, b.ScenarioId));
            _profiles = profiles.ToArray();
            foreach (var profile in _profiles) BenchmarkScenario.SelectRoute(_profiles, profile.ScenarioId, _sceneGuid);
            _reference = new RouteReference { scene = scenePath };
            var loadedScene = EditorSceneManager.OpenScene(_reference.scene, OpenSceneMode.Single);
            ValidateSourceScene(loadedScene);
            if (Environment.GetEnvironmentVariable("ANOMALY_VISUAL08") == "1")
            {
                AnomalyVisual08Build.CaptureSource(loadedScene);
            }
            string player = Path.Combine(_task, "Player");
            if (Directory.Exists(player))
            {
                throw new IOException("Release output already exists; never overwrite a retained build.");
            }
            bool development = EditorUserBuildSettings.development;
            bool debugging = EditorUserBuildSettings.allowDebugging;
            bool autoconnect = EditorUserBuildSettings.connectProfiler;
            bool deep = EditorUserBuildSettings.buildWithDeepProfilingSupport;
            bool timing = PlayerSettings.enableFrameTimingStats;
            bool frameTimingDiagnostic = Environment.GetEnvironmentVariable("BENCHMARK_FRAME_TIMING_DIAGNOSTIC") == "1";
            int width = PlayerSettings.defaultScreenWidth;
            int height = PlayerSettings.defaultScreenHeight;
            FullScreenMode fullscreen = PlayerSettings.fullScreenMode;
            bool background = PlayerSettings.runInBackground;
            try
            {
                EditorUserBuildSettings.development = false;
                EditorUserBuildSettings.allowDebugging = false;
                EditorUserBuildSettings.connectProfiler = false;
                EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
                PlayerSettings.enableFrameTimingStats = frameTimingDiagnostic;
                PlayerSettings.defaultScreenWidth = 1920;
                PlayerSettings.defaultScreenHeight = 1080;
                PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
                PlayerSettings.runInBackground = true;
                Directory.CreateDirectory(player);
                _building = true;
                Write("BUILD_PARAMETERS.json", new Parameters { unity = Application.unityVersion, backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString(), development = EditorUserBuildSettings.development, debugging = EditorUserBuildSettings.allowDebugging, autoconnectProfiler = EditorUserBuildSettings.connectProfiler, deepProfiling = EditorUserBuildSettings.buildWithDeepProfilingSupport, frameTimingStats = PlayerSettings.enableFrameTimingStats, frameTimingDiagnostic = frameTimingDiagnostic, graphicsJobs = PlayerSettings.graphicsJobs, graphicsApis = Array.ConvertAll(PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64), value => value.ToString()), scene = _reference.scene, extraScriptingDefine = "CLEAN_RELEASE_ROUTE07", buildOptions = "StrictMode | CleanBuildCache", executable = Path.Combine(player, "CleanRelease.exe") });
                var options = new BuildPlayerOptions { scenes = new[] { _reference.scene }, locationPathName = Path.Combine(player, "CleanRelease.exe"), target = BuildTarget.StandaloneWindows64, subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.StrictMode | BuildOptions.CleanBuildCache, extraScriptingDefines = new[] { "CLEAN_RELEASE_ROUTE07" } };
                var report = BuildPipeline.BuildPlayer(options);
                Write("BUILD_RESULT.json", new BuildResultData { status = report.summary.result.ToString(), buildGuid = report.summary.guid.ToString(), totalBytes = report.summary.totalSize, durationSeconds = report.summary.totalTime.TotalSeconds, errors = report.summary.totalErrors, warnings = report.summary.totalWarnings, development = report.summary.options.HasFlag(BuildOptions.Development), options = report.summary.options.ToString() });
                if (report.summary.result != BuildResult.Succeeded || report.summary.options.HasFlag(BuildOptions.Development))
                {
                    throw new BuildFailedException("Clean Release build did not succeed.");
                }
                BenchmarkReleaseLaunchers.Write(_task, _profiles);
                EditorPrefs.SetString("Rvm.Benchmark.ReleaseDirectory", _task);
                var payload = JsonUtility.FromJson<PatchPayload>(File.ReadAllText("Assets/_Tools/X-Ray OGF OMG Importer/Editor/Rendering/HdrpShadowBindingPatch.json"));
                string package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.render-pipelines.high-definition").resolvedPath;
                var files = new List<PatchEvidence>();
                foreach (var file in payload.files)
                {
                    string actual = HashFile(Path.Combine(package, file.path));
                    if (actual != file.patchedSha256)
                    {
                        throw new BuildFailedException("HDRP shadow patch changed during build: " + file.path);
                    }
                    files.Add(new PatchEvidence { path = file.path, expectedSha256 = file.patchedSha256, actualSha256 = actual });
                }
                Write("HDRP_PATCH_VERIFICATION.json", new PatchResult { packagePath = package, files = files.ToArray(), applied = true });
            }
            finally
            {
                _building = false;
                EditorUserBuildSettings.development = development;
                EditorUserBuildSettings.allowDebugging = debugging;
                EditorUserBuildSettings.connectProfiler = autoconnect;
                EditorUserBuildSettings.buildWithDeepProfilingSupport = deep;
                PlayerSettings.enableFrameTimingStats = timing;
                PlayerSettings.defaultScreenWidth = width;
                PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.fullScreenMode = fullscreen;
                PlayerSettings.runInBackground = background;
                Write("BUILD_SETTINGS_RESTORED.json", new Restored { restored = true, development = development, frameTimingStats = timing, width = width, height = height, fullScreenMode = fullscreen.ToString() });
            }
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!_building || report == null || scene.path != _reference.scene)
            {
                return;
            }
            Camera camera = null;
            var removed = new List<string>();
            var behaviours = new List<string>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "BENCHMARK_POSITION")
                {
                    if (root.GetComponentsInChildren<Camera>(true).Length != 0)
                    {
                        throw new BuildFailedException("Legacy benchmark root unexpectedly owns a camera.");
                    }
                    removed.Add("Build-only legacy benchmark root: " + root.name);
                    UnityEngine.Object.DestroyImmediate(root);
                    continue;
                }
                foreach (var candidate in root.GetComponentsInChildren<Camera>(true))
                {
                    if (candidate.isActiveAndEnabled && candidate.cameraType == CameraType.Game)
                    {
                        if (camera != null)
                        {
                            throw new BuildFailedException("Multiple Level Camera objects.");
                        }
                        camera = candidate;
                    }
                }
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null)
                    {
                        continue;
                    }
                    Type type = component.GetType();
                    string name = type.FullName;
                    bool benchmark = type.Namespace != null && type.Namespace.StartsWith("Rvm.Benchmark", StringComparison.Ordinal);
                    bool probe = type.Namespace == "XRay.Importer.Levels" && (type.Name.EndsWith("Probe", StringComparison.Ordinal) || type.Name.StartsWith("XRayPrecipitationPlayer", StringComparison.Ordinal));
                    if (benchmark || probe)
                    {
                        removed.Add(name + "|" + component.gameObject.name);
                        UnityEngine.Object.DestroyImmediate(component);
                    }
                    else
                    {
                        behaviours.Add(name);
                    }
                }
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
                }
            }
            if (camera == null)
            {
                throw new BuildFailedException("Level Camera is missing.");
            }
            var positions = new Vector3[4];
            var rotations = new Quaternion[4];
            for (int i = 0; i < 4; i++)
            {
                positions[i] = _profiles[0].Position(i);
                rotations[i] = _profiles[0].Rotation(i);
            }
            XRay.Importer.Levels.XRayWeatherProfile profile = null;
            foreach (string guid in AssetDatabase.FindAssets("t:XRayWeatherProfile"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<XRay.Importer.Levels.XRayWeatherProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate.name != _profiles[0].ExpectedWeatherProfile) continue;
                if (profile != null) throw new BuildFailedException("Ambiguous benchmark weather profile.");
                profile = candidate;
            }
            if (profile == null)
            {
                throw new BuildFailedException("Exact historical w_foggy3 asset is unavailable.");
            }
            foreach (var route in _profiles)
            {
                if (route.ExpectedWeatherProfile != profile.name) throw new BuildFailedException("Routes in one Player must use the same weather profile.");
            }
            camera.gameObject.AddComponent<CleanReleaseRouteController>().ConfigureProfiles(camera, _profiles, _sceneGuid, profile);
            if (Environment.GetEnvironmentVariable("ANOMALY_VISUAL08") == "1")
            {
                AnomalyVisual08Build.Prepare(scene, _task);

            }
            Write("SCENE_BUILD_EVIDENCE.json", new SceneEvidence { sourceScene = scene.path, sourceSceneWasSaved = false, removed = removed.ToArray(), retainedBehaviourTypes = behaviours.ToArray(), positions = positions, rotations = rotations, camera = camera.name, fieldOfView = camera.fieldOfView, nearClip = camera.nearClipPlane, farClip = camera.farClipPlane, cullingMask = camera.cullingMask, allowHdr = camera.allowHDR, allowMsaa = camera.allowMSAA, dynamicResolution = camera.allowDynamicResolution });
        }

        private static void ValidateSourceScene(Scene scene)
        {
            Camera camera = null;
            var recipe = JsonUtility.FromJson<RouteReference>(File.ReadAllText(ROUTE_REFERENCE_PATH));
            if (_profiles == null || _profiles.Length == 0 || AssetDatabase.AssetPathToGUID(scene.path) != _sceneGuid)
            {
                throw new InvalidOperationException("Module-owned route recipe does not match the location.");
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<Camera>(true))
                {
                    if (candidate.isActiveAndEnabled && candidate.cameraType == CameraType.Game)
                    {
                        if (camera != null)
                        {
                            throw new InvalidOperationException("Multiple Level Camera objects.");
                        }
                        camera = candidate;
                    }
                }
            }
            if (camera == null)
            {
                throw new InvalidOperationException("Original Level Camera is unavailable.");
            }
            var positionDeltas = new float[4];
            var rotationDeltas = new float[4];
            BenchmarkScenario overview = Array.Find(_profiles, value => value.ScenarioId == "military-overview");
            if (overview == null) return;
            if (recipe.scene != scene.path || recipe.positions == null || recipe.positions.Length != 4) throw new InvalidOperationException("Historical Overview reference belongs to a different scene.");
            for (int i = 0; i < 4; i++)
            {
                if (recipe.positions[i].key != overview.PositionKeys[i])
                {
                    throw new InvalidOperationException("Route pose key differs from the module recipe: " + i);
                }
                positionDeltas[i] = Vector3.Distance(recipe.positions[i].position, overview.Position(i));
                rotationDeltas[i] = Quaternion.Angle(recipe.positions[i].rotation, overview.Rotation(i));
                if (positionDeltas[i] > 0.0001f || rotationDeltas[i] > 0.0001f)
                {
                    throw new InvalidOperationException("Original source pose differs from full R30 metadata: " + i);
                }
            }
            if (overview.CameraSettings.FieldOfView != 90 || overview.CameraSettings.Near != 0.1f || overview.CameraSettings.Far != 10000)
            {
                throw new InvalidOperationException("Source camera settings differ from R30.");
            }
            Write("SOURCE_ROUTE_COMPARISON.json", new SourceComparison { positions = positionDeltas, rotationDegrees = rotationDeltas, camera = camera.name, matches = true });
        }

        private static IEnumerable<Scene> EnumerableScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                yield return SceneManager.GetSceneAt(i);
            }
        }

        private static void Write(string name, object value)
        {
            File.WriteAllText(Path.Combine(_task, name), JsonUtility.ToJson(value, true));
        }

        private static string HashFile(string path)
        {
            using var algorithm = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        [Serializable]
        private sealed class RouteReference
        {
            public string scene;
            public PositionReference[] positions;
            public string cameraSettings;
        }

        [Serializable]
        private sealed class PositionReference
        {
            public string key;
            public Vector3 position;
            public Quaternion rotation;
        }

        [Serializable]
        private sealed class SourceComparison
        {
            public float[] positions;
            public float[] rotationDegrees;
            public string camera;
            public bool matches;
        }

        [Serializable]
        private sealed class Parameters
        {
            public string unity;
            public string backend;
            public bool development;
            public bool debugging;
            public bool autoconnectProfiler;
            public bool deepProfiling;
            public bool frameTimingStats;
            public bool frameTimingDiagnostic;
            public bool graphicsJobs;
            public string[] graphicsApis;
            public string scene;
            public string extraScriptingDefine;
            public string buildOptions;
            public string executable;
        }

        [Serializable]
        private sealed class BuildResultData
        {
            public string status;
            public string buildGuid;
            public ulong totalBytes;
            public double durationSeconds;
            public int errors;
            public int warnings;
            public bool development;
            public string options;
        }

        [Serializable]
        private sealed class Restored
        {
            public bool restored;
            public bool development;
            public bool frameTimingStats;
            public int width;
            public int height;
            public string fullScreenMode;
        }

        [Serializable]
        private sealed class SceneEvidence
        {
            public string sourceScene;
            public bool sourceSceneWasSaved;
            public string[] removed;
            public string[] retainedBehaviourTypes;
            public Vector3[] positions;
            public Quaternion[] rotations;
            public string camera;
            public float fieldOfView;
            public float nearClip;
            public float farClip;
            public int cullingMask;
            public bool allowHdr;
            public bool allowMsaa;
            public bool dynamicResolution;
        }

        [Serializable]
        private sealed class PatchPayload
        {
            public PatchFile[] files;
        }

        [Serializable]
        private sealed class PatchFile
        {
            public string path;
            public string patchedSha256;
        }

        [Serializable]
        private sealed class PatchEvidence
        {
            public string path;
            public string expectedSha256;
            public string actualSha256;
        }

        [Serializable]
        private sealed class PatchResult
        {
            public string packagePath;
            public PatchEvidence[] files;
            public bool applied;
        }
    }
}
