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
        private static bool _building;
        private static string _task;
        private static RouteReference _reference;

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
            _reference = JsonUtility.FromJson<RouteReference>(File.ReadAllText(Path.Combine(_task, "ROUTE_REFERENCE.json")));
            var scenario = AssetDatabase.LoadAssetAtPath<BenchmarkScenario>("Assets/_BENCHMARK/Scenarios/l07_military.asset");
            if (scenario == null || scenario.ScenePath != _reference.scene || scenario.WarmupSeconds != 8 || scenario.PositionSeconds != 5 || scenario.StabilizationSeconds != 0.5)
            {
                throw new InvalidOperationException("Original scenario differs from required route.");
            }
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
                PlayerSettings.enableFrameTimingStats = false;
                PlayerSettings.defaultScreenWidth = 1920;
                PlayerSettings.defaultScreenHeight = 1080;
                PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
                PlayerSettings.runInBackground = true;
                Directory.CreateDirectory(player);
                _building = true;
                Write("BUILD_PARAMETERS.json", new Parameters { unity = Application.unityVersion, backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString(), development = EditorUserBuildSettings.development, debugging = EditorUserBuildSettings.allowDebugging, autoconnectProfiler = EditorUserBuildSettings.connectProfiler, deepProfiling = EditorUserBuildSettings.buildWithDeepProfilingSupport, frameTimingStats = PlayerSettings.enableFrameTimingStats, graphicsJobs = PlayerSettings.graphicsJobs, graphicsApis = Array.ConvertAll(PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64), value => value.ToString()), scene = _reference.scene, extraScriptingDefine = "CLEAN_RELEASE_ROUTE07", buildOptions = "StrictMode | CleanBuildCache", executable = Path.Combine(player, "CleanRelease.exe") });
                var options = new BuildPlayerOptions { scenes = new[] { _reference.scene }, locationPathName = Path.Combine(player, "CleanRelease.exe"), target = BuildTarget.StandaloneWindows64, subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.StrictMode | BuildOptions.CleanBuildCache, extraScriptingDefines = new[] { "CLEAN_RELEASE_ROUTE07" } };
                var report = BuildPipeline.BuildPlayer(options);
                Write("BUILD_RESULT.json", new BuildResultData { status = report.summary.result.ToString(), buildGuid = report.summary.guid.ToString(), totalBytes = report.summary.totalSize, durationSeconds = report.summary.totalTime.TotalSeconds, errors = report.summary.totalErrors, warnings = report.summary.totalWarnings, development = report.summary.options.HasFlag(BuildOptions.Development), options = report.summary.options.ToString() });
                if (report.summary.result != BuildResult.Succeeded || report.summary.options.HasFlag(BuildOptions.Development))
                {
                    throw new BuildFailedException("Clean Release build did not succeed.");
                }
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
                    if (candidate.name == "Level Camera")
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
                    bool overlay = name == "RaidIntoDarkness.Player.StalkerPlayerDebugOverlay";
                    bool probe = type.Namespace == "XRay.Importer.Levels" && (type.Name.EndsWith("Probe", StringComparison.Ordinal) || type.Name.StartsWith("XRayPrecipitationPlayer", StringComparison.Ordinal));
                    if (benchmark || overlay || probe)
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
                positions[i] = _reference.positions[i].position;
                rotations[i] = _reference.positions[i].rotation;
            }
            var profile = AssetDatabase.LoadAssetAtPath<XRay.Importer.Levels.XRayWeatherProfile>("Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/WeatherProfiles/2e431903b176_01232f8a/w_foggy3.asset");
            if (profile == null)
            {
                throw new BuildFailedException("Exact historical w_foggy3 asset is unavailable.");
            }
            camera.gameObject.AddComponent<CleanReleaseRouteController>().Configure(camera, positions, rotations, _reference.cameraSettings, profile);
            if (Environment.GetEnvironmentVariable("ANOMALY_VISUAL08") == "1")
            {
                AnomalyVisual08Build.Prepare(scene, _task);

            }
            Write("SCENE_BUILD_EVIDENCE.json", new SceneEvidence { sourceScene = scene.path, sourceSceneWasSaved = false, removed = removed.ToArray(), retainedBehaviourTypes = behaviours.ToArray(), positions = positions, rotations = rotations, camera = camera.name, fieldOfView = camera.fieldOfView, nearClip = camera.nearClipPlane, farClip = camera.farClipPlane, cullingMask = camera.cullingMask, allowHdr = camera.allowHDR, allowMsaa = camera.allowMSAA, dynamicResolution = camera.allowDynamicResolution });
        }

        private static void ValidateSourceScene(Scene scene)
        {
            Camera camera = null;
            var positions = new Transform[4];
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<Camera>(true))
                {
                    if (candidate.name == "Level Camera")
                    {
                        if (camera != null)
                        {
                            throw new InvalidOperationException("Multiple Level Camera objects.");
                        }
                        camera = candidate;
                    }
                }
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    for (int i = 0; i < positions.Length; i++)
                    {
                        if (transform.name == _reference.positions[i].key)
                        {
                            if (positions[i] != null)
                            {
                                throw new InvalidOperationException("Duplicate source pose: " + transform.name);
                            }
                            positions[i] = transform;
                        }
                    }
                }
            }
            if (camera == null)
            {
                throw new InvalidOperationException("Original Level Camera is unavailable.");
            }
            var positionDeltas = new float[4];
            var rotationDeltas = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (positions[i] == null)
                {
                    throw new InvalidOperationException("Original source pose is unavailable: " + i);
                }
                positionDeltas[i] = Vector3.Distance(positions[i].position, _reference.positions[i].position);
                rotationDeltas[i] = Quaternion.Angle(positions[i].rotation, _reference.positions[i].rotation);
                if (positionDeltas[i] > 0.0001f || rotationDeltas[i] > 0.0001f)
                {
                    throw new InvalidOperationException("Original source pose differs from full R30 metadata: " + i);
                }
            }
            if (camera.name != "Level Camera" || camera.fieldOfView != 90 || camera.nearClipPlane != 0.1f || camera.farClipPlane != 10000 || camera.cullingMask != -1 || camera.allowHDR || camera.allowMSAA || camera.allowDynamicResolution || camera.usePhysicalProperties || camera.targetTexture != null)
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
