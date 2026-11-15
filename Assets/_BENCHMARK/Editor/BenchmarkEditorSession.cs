using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Rvm.Benchmark.Editor
{
    [InitializeOnLoad]
    public static class BenchmarkEditorSession
    {
        private const string PENDING = "Rvm.Benchmark.Pending";
        private const string SCENARIO = "Rvm.Benchmark.Scenario";
        private const string REMAINING = "Rvm.Benchmark.Remaining";
        private const string SERIES = "Rvm.Benchmark.Series";
        private const string RUNS = "Rvm.Benchmark.Runs";
        private const string OUTPUT = "Rvm.Benchmark.Output";
        private const string LAST = "Rvm.Benchmark.Last";
        private const string STOP_AFTER_RELOAD = "Rvm.Benchmark.StopAfterReload";

        public static string LastReport => EditorPrefs.GetString(LAST, "");

        static BenchmarkEditorSession()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            BenchmarkRunner.Completed -= OnCompleted;
            BenchmarkRunner.Completed += OnCompleted;
            if (SessionState.GetBool(STOP_AFTER_RELOAD, false))
            {
                SessionState.SetBool(STOP_AFTER_RELOAD, false);
                EditorApplication.delayCall += StopPlayMode;
            }
        }

        public static void Launch(BenchmarkScenario scenario, int repetitions, string outputRoot = BenchmarkOutput.DEFAULT_ROOT)
        {
            RequireEditMode();
            RequireCleanScenes();
            if (scenario == null || (repetitions != 1 && repetitions != 3))
            {
                throw new InvalidOperationException("Select a scenario and one or three repetitions.");
            }
            var runner = FindRunner(scenario);
            if (runner == null || runner.Validate() != null)
            {
                throw new InvalidOperationException(runner == null ? "Connect the selected scenario to the open scene first." : runner.Validate());
            }
            if (UnityEditorInternal.ProfilerDriver.deepProfiling)
            {
                throw new InvalidOperationException("Disable Deep Profiling before starting a benchmark.");
            }
            AssetDatabase.SaveAssetIfDirty(scenario);
            SessionState.SetBool(PENDING, true);
            SessionState.SetString(SCENARIO, AssetDatabase.GetAssetPath(scenario));
            SessionState.SetInt(REMAINING, repetitions);
            SessionState.SetString(SERIES, Guid.NewGuid().ToString("N"));
            SessionState.SetString(RUNS, "");
            SessionState.SetString(OUTPUT, BenchmarkOutput.ValidateDirectory(outputRoot));
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.isPlaying = true;
        }

        public static void Connect(BenchmarkScenario scenario, Camera camera, Transform[] positions, bool save)
        {
            RequireEditMode();
            if (save)
            {
                RequireCleanScenes();
            }
            if (scenario == null || positions == null || positions.Length != 4 || camera == null)
            {
                throw new InvalidOperationException("Scenario, camera and four positions are required.");
            }
            var scene = camera.gameObject.scene;
            if (!scene.path.StartsWith("Assets/_BENCHMARK/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Persistent benchmark runners belong in module-owned scenes. Use the build-only Release route for ordinary locations.");
            }
            if (scene.path != scenario.ScenePath)
            {
                throw new InvalidOperationException("Open the scene specified by the scenario.");
            }
            GameObject host = positions[0] == null ? null : positions[0].parent == null ? positions[0].gameObject : positions[0].parent.gameObject;
            if (host == null)
            {
                throw new InvalidOperationException("Position references are missing.");
            }
            var runner = FindRunner(scenario);
            if (runner == null)
            {
                runner = Undo.AddComponent<BenchmarkRunner>(host);
            }
            Undo.RecordObject(runner, "Connect RVM Benchmark");
            runner.Configure(scenario, camera, positions);
            string error = runner.Validate();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }
            EditorUtility.SetDirty(runner);
            EditorSceneManager.MarkSceneDirty(scene);
            if (save)
            {
                Undo.FlushUndoRecordObjects();
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new IOException("Could not save benchmark scene integration.");
                }
            }
        }

        public static BenchmarkScenario PrepareCurrentScene()
        {
            RequireEditMode();
            RequireCleanScenes();
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                throw new InvalidOperationException("Save the scene before creating a scenario.");
            }
            string path = "Assets/_BENCHMARK/Scenarios/" + scene.name + ".asset";
            var scenario = AssetDatabase.LoadAssetAtPath<BenchmarkScenario>(path);
            if (scenario == null)
            {
                scenario = ScriptableObject.CreateInstance<BenchmarkScenario>();
                var serialized = new SerializedObject(scenario);
                serialized.FindProperty("_scenarioId").stringValue = scene.name;
                serialized.FindProperty("_scenePath").stringValue = scene.path;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (!AssetDatabase.IsValidFolder("Assets/_BENCHMARK/Scenarios"))
                {
                    AssetDatabase.CreateFolder("Assets/_BENCHMARK", "Scenarios");
                }
                AssetDatabase.CreateAsset(scenario, path);
            }
            AutoReferences(scenario, out var camera, out var positions);
            Connect(scenario, camera, positions, true);
            AssetDatabase.SaveAssetIfDirty(scenario);
            return scenario;
        }

        public static void AutoReferences(BenchmarkScenario scenario, out Camera camera, out Transform[] positions)
        {
            positions = new Transform[4];
            camera = null;
            var scene = SceneManager.GetActiveScene();
            var keys = scenario.PositionKeys;
            int cameraCount = 0;
            Camera soleCamera = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (transform.name != keys[i])
                        {
                            continue;
                        }
                        if (positions[i] != null)
                        {
                            throw new InvalidOperationException("Ambiguous position key: " + keys[i]);
                        }
                        positions[i] = transform;
                    }
                }
                foreach (var candidate in root.GetComponentsInChildren<Camera>(true))
                {
                    if (!candidate.isActiveAndEnabled || candidate.cameraType != CameraType.Game)
                    {
                        continue;
                    }
                    soleCamera = candidate;
                    cameraCount++;
                }
            }
            if (cameraCount == 1)
            {
                camera = soleCamera;
            }
            var runner = FindRunner(scenario);
            if (runner != null)
            {
                camera = runner.WorkingCamera;
                positions = runner.Positions;
            }
            if (camera == null)
            {
                throw new InvalidOperationException("Select a working camera explicitly; the scene does not have exactly one active Game camera.");
            }
        }

        public static BenchmarkRunner FindRunner(BenchmarkScenario scenario)
        {
            BenchmarkRunner result = null;
            foreach (var runner in UnityEngine.Object.FindObjectsByType<BenchmarkRunner>())
            {
                if (runner.Scenario != scenario)
                {
                    continue;
                }
                if (result != null)
                {
                    throw new InvalidOperationException("Multiple runners reference the scenario.");
                }
                result = runner;
            }
            return result;
        }

        public static void FillEnvironment(BenchmarkRunner runner)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.render-pipelines.high-definition");
            runner.SetBuildEnvironment(Git("rev-parse HEAD"), GitDirty(), info == null ? "unavailable" : info.version);
        }

        public static void RequireCleanScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    throw new InvalidOperationException("Unsaved scene changes: " + scene.path + ". Save them manually before benchmarking.");
                }
            }
        }

        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                throw new InvalidOperationException("Wait until the Editor is idle in Edit Mode.");
            }
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PENDING, false))
            {
                try
                {
                    var scenario = AssetDatabase.LoadAssetAtPath<BenchmarkScenario>(SessionState.GetString(SCENARIO, ""));
                    var runner = FindRunner(scenario);
                    if (runner == null)
                    {
                        throw new InvalidOperationException("Configured runner was not found after entering Play Mode.");
                    }
                    FillEnvironment(runner);
                    runner.Begin(SessionState.GetString(OUTPUT, BenchmarkOutput.DEFAULT_ROOT), SessionState.GetString(SERIES, ""), 1);
                }
                catch (Exception exception)
                {
                    StopWithError(exception);
                }
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                SessionState.SetBool(PENDING, false);
                foreach (var runner in UnityEngine.Object.FindObjectsByType<BenchmarkRunner>())
                {
                    runner.Abort("Play Mode stopped manually.");
                }
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(PENDING, false);
            }
        }

        private static void OnCompleted(BenchmarkRunner runner)
        {
            if (!SessionState.GetBool(PENDING, false))
            {
                return;
            }
            try
            {
                if (!runner.Saved || runner.LastSummary == null || runner.LastSummary.status != "completed")
                {
                    throw new IOException("Benchmark aborted or save failed; partial output: " + runner.LastDirectory);
                }
                EditorPrefs.SetString(LAST, Path.Combine(runner.LastDirectory, "report.md"));
                string previous = SessionState.GetString(RUNS, "");
                SessionState.SetString(RUNS, string.IsNullOrEmpty(previous) ? runner.LastDirectory : previous + "\n" + runner.LastDirectory);
                int remaining = SessionState.GetInt(REMAINING, 1) - 1;
                SessionState.SetInt(REMAINING, remaining);
                if (remaining > 0)
                {
                    EditorApplication.delayCall += BeginNext;
                    return;
                }
                var directories = new List<string>(SessionState.GetString(RUNS, "").Split('\n'));
                if (directories.Count > 1)
                {
                    EditorPrefs.SetString(LAST, BenchmarkComparison.SaveSeries(SessionState.GetString(SERIES, ""), directories));
                }
                SessionState.SetBool(PENDING, false);
                EditorApplication.delayCall += StopPlayMode;
            }
            catch (Exception exception)
            {
                StopWithError(exception);
            }
        }

        private static void BeginNext()
        {
            if (!SessionState.GetBool(PENDING, false) || !EditorApplication.isPlaying)
            {
                return;
            }
            try
            {
                var scenario = AssetDatabase.LoadAssetAtPath<BenchmarkScenario>(SessionState.GetString(SCENARIO, ""));
                var runner = FindRunner(scenario);
                var directories = SessionState.GetString(RUNS, "").Split('\n');
                runner.Begin(SessionState.GetString(OUTPUT, BenchmarkOutput.DEFAULT_ROOT), SessionState.GetString(SERIES, ""), directories.Length + 1);
            }
            catch (Exception exception)
            {
                StopWithError(exception);
            }
        }

        private static void BeforeReload()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(PENDING, false))
            {
                return;
            }
            SessionState.SetBool(PENDING, false);
            SessionState.SetBool(STOP_AFTER_RELOAD, true);
            foreach (var runner in UnityEngine.Object.FindObjectsByType<BenchmarkRunner>())
            {
                runner.Abort("Assembly reload interrupted benchmark.");
            }
        }

        private static void StopWithError(Exception exception)
        {
            SessionState.SetBool(PENDING, false);
            Debug.LogError("RVM Benchmark: " + exception);
            EditorApplication.delayCall += StopPlayMode;
        }

        private static void StopPlayMode()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }

        private static string GitDirty()
        {
            string value = Git("status --porcelain=v1 --untracked-files=normal");
            return value == "unavailable" ? value : (!string.IsNullOrWhiteSpace(value)).ToString();
        }

        private static string Git(string arguments)
        {
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var start = new ProcessStartInfo("git", arguments) { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var process = Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill();
                        return "unavailable";
                    }
                    return process.ExitCode == 0 ? output.GetAwaiter().GetResult().Trim() : "unavailable";
                }
            }
            catch
            {
                return "unavailable";
            }
        }
    }
}
