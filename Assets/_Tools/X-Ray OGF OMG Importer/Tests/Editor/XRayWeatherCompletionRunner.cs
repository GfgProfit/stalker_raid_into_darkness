using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Tests
{
    public static class XRayWeatherCompletionRunner
    {
        private const string PRODUCTION = @"E:\Projects\stalker_raid_into_darkness";
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\WeatherCompletion_20261104_024544";
        private const string ASSEMBLY = "XRay.Importer.Tests.Editor";

        private static readonly List<CaseRecord> _cases = new List<CaseRecord>();
        private static TestRunnerApi _api;
        private static Callbacks _callbacks;
        private static Filter _filter;
        private static SummaryRecord _summary;
        private static string _previousResourceTestDirectory;
        private static bool _restoreResourceTestDirectory;

        public static string RunLayerRegression()
        {
            return Queue("LayerRegression", new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { ASSEMBLY }, testNames = new[] { "XRay.Importer.Tests.XRayWeatherFogIntegrationTests.FreshBuilderFogAndSkyUseTheSameCameraVolumeLayerWithoutChangingItsMask" } });
        }

        public static string RunCore()
        {
            return Queue("Core", new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { ASSEMBLY }, groupNames = new[] { @"^XRay\.Importer\.Tests\.XRayWeatherFogMathTests\.", @"^XRay\.Importer\.Tests\.NativeFogRadianceTests\.", @"^XRay\.Importer\.Tests\.WeatherSunRadianceTests\.", @"^XRay\.Importer\.Tests\.XRayWeatherFogIntegrationTests\.", @"^XRay\.Importer\.Tests\.XRayWeatherControllerTests\." } });
        }

        public static string RunHostIntegration()
        {
            return Queue("HostIntegration", new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { ASSEMBLY }, testNames = new[] { "XRay.Importer.Tests.NativeFogPreviewSelectionTests.SwitchingFromControllerInspectorToFogVolumeKeepsWeatherPreviewRunning", "XRay.Importer.Tests.XRayWeatherFogIntegrationTests.SavingAndReloadingSceneBPreservesSceneAFogOwnershipAndSerializedBindings" } });
        }

        public static string RunCelestial(string archive)
        {
            return Queue("Celestial", new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { ASSEMBLY }, groupNames = new[] { @"^XRay\.Importer\.Tests\.XRayWeatherCelestialTests\.", @"^XRay\.Importer\.Tests\.XRayWeatherResourceTests\.FlareBgr24AndBgra32PreserveAuthoredMipsAndAlpha$", @"^XRay\.Importer\.Tests\.XRayWeatherFogMathTests\.", @"^XRay\.Importer\.Tests\.NativeFogRadianceTests\.", @"^XRay\.Importer\.Tests\.WeatherSunRadianceTests\.", @"^XRay\.Importer\.Tests\.XRayWeatherFogIntegrationTests\.", @"^XRay\.Importer\.Tests\.XRayWeatherControllerTests\.", @"^XRay\.Importer\.Tests\.NativeFogPreviewSelectionTests\." } }, archive);
        }

        public static string RunCatalog(string archive)
        {
            return Queue("Catalog", new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { ASSEMBLY }, groupNames = new[] { @"^XRay\.Importer\.Tests\.XRayWeatherCatalogTests\." } }, archive);
        }

        public static string RunFlareAdaptation(string archive)
        {
            return Queue("FlareAdaptation", new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { ASSEMBLY }, groupNames = new[] { @"^XRay\.Importer\.Tests\.XRayWeatherFlareAdaptationTests\." } }, archive);
        }

        public static string RunFx(string archive)
        {
            return Queue("Fx", new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { ASSEMBLY }, groupNames = new[] { @"^XRay\.Importer\.Tests\.XRayWeatherFxTests\." } }, archive);
        }

        public static string RunGameConfig(string archive)
        {
            return Queue("GameConfig", new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { ASSEMBLY }, groupNames = new[] { @"^XRay\.Importer\.Tests\.GameConfigTests\.", @"^XRay\.Importer\.Tests\.XRayWeatherEditorTests\.PreviewClockRebasesAndRestoresWithoutSerializingTicks$" } }, archive);
        }

        public static string Status()
        {
            return _summary == null ? "{\"status\":\"idle\"}" : JsonUtility.ToJson(_summary, true);
        }

        private static string Queue(string category, Filter filter, string archive = ARCHIVE)
        {
            if (_summary != null && (_summary.status == "queued" || _summary.status == "running")) throw new InvalidOperationException("A weather completion test run is already active.");
            GuardEditor();
            if (!Path.GetFullPath(archive).StartsWith(@"E:\Projects\stalker_raid_into_darkness_Archive\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test output must stay inside the project Archive.");
            EditorApplication.delayCall -= ReleaseApi;
            ReleaseApi();
            string directory = Path.Combine(archive, "TestRunner_" + category + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            if (Directory.Exists(directory)) throw new IOException("Test results must use a new Archive directory.");
            Directory.CreateDirectory(directory);
            _cases.Clear();
            _filter = filter;
            _summary = new SummaryRecord { status = "queued", output = directory, project = PRODUCTION, unity = Application.unityVersion, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), category = category, testNames = filter.testNames, groupNames = filter.groupNames, queuedUtc = DateTime.UtcNow.ToString("O"), cases = Array.Empty<CaseRecord>() };
            WriteSummary();
            EditorApplication.update += ExecuteQueued;
            return Status();
        }

        private static void ExecuteQueued()
        {
            EditorApplication.update -= ExecuteQueued;
            try
            {
                GuardEditor();
                if (_summary.category == "Celestial")
                {
                    _previousResourceTestDirectory = Environment.GetEnvironmentVariable("XRAY_WEATHER_RESOURCE_TEST_DIR");
                    _restoreResourceTestDirectory = true;
                    Environment.SetEnvironmentVariable("XRAY_WEATHER_RESOURCE_TEST_DIR", Path.Combine(_summary.output, "Resources"));
                }
                _api = ScriptableObject.CreateInstance<TestRunnerApi>();
                _api.hideFlags = HideFlags.HideAndDontSave;
                _callbacks = new Callbacks();
                _api.RegisterCallbacks(_callbacks);
                _summary.status = "running";
                WriteSummary();
                _summary.runId = _api.Execute(new ExecutionSettings(_filter) { runSynchronously = false });
                WriteSummary();
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
            }
        }

        private static void GuardEditor()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(project, PRODUCTION, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Weather completion tests require the canonical Production project.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Weather completion tests require an idle Editor in Edit Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.isDirty) throw new InvalidOperationException("Save or discard the dirty scene before running weather completion tests: " + scene.path);
            }
        }

        private static void WriteSummary()
        {
            _summary.cases = _cases.ToArray();
            File.WriteAllText(Path.Combine(_summary.output, "Summary.json"), JsonUtility.ToJson(_summary, true));
        }

        private static void Fail(string message)
        {
            _summary.status = "error";
            _summary.passed = false;
            _summary.error = message;
            _summary.finishedUtc = DateTime.UtcNow.ToString("O");
            WriteSummary();
            EditorApplication.delayCall += ReleaseApi;
        }

        private static void ReleaseApi()
        {
            if (_api != null && _callbacks != null) _api.UnregisterCallbacks(_callbacks);
            if (_api != null) UnityEngine.Object.DestroyImmediate(_api);
            if (_restoreResourceTestDirectory)
            {
                Environment.SetEnvironmentVariable("XRAY_WEATHER_RESOURCE_TEST_DIR", _previousResourceTestDirectory);
                _previousResourceTestDirectory = null;
                _restoreResourceTestDirectory = false;
            }
            _api = null;
            _callbacks = null;
        }

        private static CaseRecord Case(ITestResultAdaptor result)
        {
            return new CaseRecord { fullName = result.FullName, status = result.TestStatus.ToString(), resultState = result.ResultState, message = result.Message, stackTrace = result.StackTrace, output = result.Output, duration = result.Duration, assertCount = result.AssertCount };
        }

        private static void Collect(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
            {
                _cases.Add(Case(result));
                return;
            }
            if (result.HasChildren) foreach (ITestResultAdaptor child in result.Children) Collect(child);
        }

        private sealed class Callbacks : IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                _summary.status = "running";
                _summary.startedUtc = DateTime.UtcNow.ToString("O");
                WriteSummary();
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                try
                {
                    _cases.Clear();
                    Collect(result);
                    _summary.result = result.TestStatus.ToString();
                    _summary.resultState = result.ResultState;
                    _summary.passCount = result.PassCount;
                    _summary.failCount = result.FailCount;
                    _summary.skipCount = result.SkipCount;
                    _summary.inconclusiveCount = result.InconclusiveCount;
                    _summary.duration = result.Duration;
                    _summary.passed = result.TestStatus == TestStatus.Passed && result.PassCount > 0 && result.FailCount == 0 && result.InconclusiveCount == 0;
                    _summary.finishedUtc = DateTime.UtcNow.ToString("O");
                    _summary.xml = Path.Combine(_summary.output, "Results.xml");
                    TestRunnerApi.SaveResultToFile(result, _summary.xml);
                    if (!File.Exists(_summary.xml) || new FileInfo(_summary.xml).Length == 0) throw new IOException("Unity Test Runner did not write its XML result.");
                    _summary.status = "completed";
                    WriteSummary();
                    EditorApplication.delayCall += ReleaseApi;
                }
                catch (Exception exception)
                {
                    Fail(exception.ToString());
                }
            }

            public void TestStarted(ITestAdaptor test)
            {
                if (!test.IsSuite) _summary.currentTest = test.FullName;
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.Test.IsSuite) return;
                _cases.Add(Case(result));
                WriteSummary();
            }

            public void OnError(string message)
            {
                Fail(message);
            }
        }

        [Serializable]
        private sealed class SummaryRecord
        {
            public string status;
            public string output;
            public string project;
            public string unity;
            public string graphicsApi;
            public string category;
            public string[] testNames;
            public string[] groupNames;
            public string queuedUtc;
            public string startedUtc;
            public string finishedUtc;
            public string runId;
            public string currentTest;
            public string result;
            public string resultState;
            public bool passed;
            public int passCount;
            public int failCount;
            public int skipCount;
            public int inconclusiveCount;
            public double duration;
            public string xml;
            public string error;
            public CaseRecord[] cases;
        }

        [Serializable]
        private sealed class CaseRecord
        {
            public string fullName;
            public string status;
            public string resultState;
            public string message;
            public string stackTrace;
            public string output;
            public double duration;
            public int assertCount;
        }
    }
}
