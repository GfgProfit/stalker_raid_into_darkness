using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rvm.Benchmark.Editor
{
    public sealed class BenchmarkBuild : IProcessSceneWithReport
    {
        public int callbackOrder => 1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null)
            {
                return;
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var runner in root.GetComponentsInChildren<BenchmarkRunner>(true))
                {
                    BenchmarkEditorSession.FillEnvironment(runner);
                }
            }
        }

        public static string QueueDevelopmentBuild(string outputFolder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            {
                throw new InvalidOperationException("Editor must be idle before building.");
            }
            BenchmarkEditorSession.RequireCleanScenes();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                throw new InvalidOperationException("Select Windows x86_64 before building; benchmark does not change the active build target.");
            }
            var scene = SceneManager.GetActiveScene();
            var runners = UnityEngine.Object.FindObjectsByType<BenchmarkRunner>(FindObjectsSortMode.None);
            if (runners.Length != 1 || runners[0].Validate() != null || runners[0].gameObject.scene != scene)
            {
                throw new InvalidOperationException("Exactly one valid runner must be configured in the active scene.");
            }
            string folder = BenchmarkOutput.ValidateDirectory(outputFolder);
            if (Directory.Exists(folder))
            {
                throw new IOException("Build destination already exists; retained builds will not be overwritten.");
            }
            string driveRoot = Path.GetPathRoot(folder);
            if (new DriveInfo(driveRoot).AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
            {
                throw new IOException("Development build needs at least 20 GiB free (estimated output up to 12 GiB plus temporary space).");
            }
            string scenePath = scene.path;
            string scenarioId = runners[0].Scenario.ScenarioId;
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "build-status.json"), "{\"status\":\"queued\",\"estimatedOutputGiB\":12}");
            EditorApplication.delayCall += () => Build(folder, scenePath, scenarioId);
            return "Queued Development Player: " + folder;
        }

        private static void Build(string folder, string scenePath, string scenarioId)
        {
            try
            {
                BenchmarkEditorSession.RequireCleanScenes();
                var options = new BuildPlayerOptions { scenes = new[] { scenePath }, locationPathName = Path.Combine(folder, "RvmBenchmark.exe"), target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development };
                File.WriteAllText(Path.Combine(folder, "build-status.json"), "{\"status\":\"building\"}");
                var report = BuildPipeline.BuildPlayer(options);
                var status = new BuildStatus { status = report.summary.result.ToString(), scene = scenePath, executable = options.locationPathName, totalBytes = report.summary.totalSize, durationSeconds = report.summary.totalTime.TotalSeconds, errors = report.summary.totalErrors, warnings = report.summary.totalWarnings };
                BenchmarkOutput.WriteJson(Path.Combine(folder, "build-status.json"), status);
                File.WriteAllText(Path.Combine(folder, "RunBenchmark.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\n\"%~dp0RvmBenchmark.exe\" -benchmark -benchmark-scenario " + scenarioId + " -benchmark-repeat 1 -benchmark-output \"" + BenchmarkOutput.DEFAULT_ROOT.Replace('/', '\\') + "\" -logFile \"%~dp0player_%RANDOM%_%RANDOM%.log\"\r\n", System.Text.Encoding.ASCII);
                File.WriteAllText(Path.Combine(folder, "RunBenchmarkSeries.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\n\"%~dp0RvmBenchmark.exe\" -benchmark -benchmark-scenario " + scenarioId + " -benchmark-repeat 3 -benchmark-output \"" + BenchmarkOutput.DEFAULT_ROOT.Replace('/', '\\') + "\" -logFile \"%~dp0player_series_%RANDOM%_%RANDOM%.log\"\r\n", System.Text.Encoding.ASCII);
                Debug.Log("RVM Benchmark build: " + status.status + "; " + folder);
            }
            catch (Exception exception)
            {
                BenchmarkOutput.WriteJson(Path.Combine(folder, "build-status.json"), new BuildStatus { status = "Failed", error = exception.ToString(), scene = scenePath });
                Debug.LogError("RVM Benchmark build failed: " + exception);
            }
        }

        [Serializable]
        private sealed class BuildStatus
        {
            public string status;
            public string scene;
            public string executable;
            public string error;
            public ulong totalBytes;
            public double durationSeconds;
            public int errors;
            public int warnings;
        }
    }
}
