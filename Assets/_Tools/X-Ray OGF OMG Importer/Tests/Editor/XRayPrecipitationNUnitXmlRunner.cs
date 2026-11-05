using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Tests
{
    [InitializeOnLoad]
    public static class XRayPrecipitationNUnitXmlRunner
    {
        private const string PRODUCTION = @"E:\Projects\stalker_raid_into_darkness";
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";
        private const string SESSION_KEY = "Phase08.NUnitXmlRunner.Active";
        private const string IMPORT_OUTPUT = "GAME_CONFIG_VALIDATION_OUTPUT";
        private const string SHORT_ROOT = @"E:\Projects\stalker_raid_into_darkness_Archive\P08_992480";

        private static State _state;
        private static TestRunnerApi _api;
        private static Callbacks _callbacks;

        static XRayPrecipitationNUnitXmlRunner()
        {
            string json = SessionState.GetString(SESSION_KEY, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            _state = JsonUtility.FromJson<State>(json);
            if (_state.status == "queued" || _state.status == "discovering") ScheduleExecute();
            else if (_state.status == "running") RegisterCallbacks();
            else ScheduleRelease();
        }

        public static string Queue(string manifestPath, string archive)
        {
            GuardEditor();
            if (_state != null && (_state.status == "queued" || _state.status == "discovering" || _state.status == "running")) throw new InvalidOperationException("This NUnit XML runner already owns an active run.");
            string manifestFile = RequireArchivePath(manifestPath);
            string outputRoot = RequireArchivePath(archive);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestFile));
            if (manifest == null || manifest.names == null || manifest.names.Length == 0 || string.IsNullOrEmpty(manifest.category)) throw new InvalidDataException("The manifest must select exact named cases.");
            if (manifest.testMode != "EditMode" && manifest.testMode != "PlayMode") throw new InvalidDataException("Select one test mode per run.");
            string assembly = manifest.testMode == "EditMode" ? "XRay.Importer.Tests.Editor" : "XRay.Importer.Tests.PlayMode";
            if (manifest.assembly != assembly) throw new InvalidDataException("The test assembly does not match the requested mode.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in manifest.names)
            {
                if (string.IsNullOrEmpty(name) || !unique.Add(name) || name.IndexOf("FullRealEscape", StringComparison.Ordinal) >= 0) throw new InvalidDataException("Duplicate, empty or forbidden Explicit case in the selection.");
            }
            string directory = Path.Combine(outputRoot, "NUnit_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            if (Directory.Exists(directory)) throw new IOException("NUnit output must use a unique Archive directory.");
            Directory.CreateDirectory(directory);
            File.Copy(manifestFile, Path.Combine(directory, "Selection.json"));
            _state = new State { status = "queued", output = directory, manifestPath = manifestFile, project = PRODUCTION, unity = Application.unityVersion, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), queuedUtc = DateTime.UtcNow.ToString("O"), manifest = manifest };
            Persist();
            ScheduleExecute();
            return Status();
        }

        public static string Discover(string className, string archive)
        {
            GuardEditor();
            if (_state != null && (_state.status == "queued" || _state.status == "discovering" || _state.status == "running")) throw new InvalidOperationException("This NUnit XML runner already owns an active run.");
            if (string.IsNullOrEmpty(className) || !className.StartsWith("XRay.Importer.Tests.", StringComparison.Ordinal) || className.EndsWith(".", StringComparison.Ordinal)) throw new InvalidDataException("Discovery requires one exact XRay.Importer.Tests class name.");
            string outputRoot = RequireArchivePath(archive);
            string directory = Path.Combine(outputRoot, "NUnitDiscovery_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            if (Directory.Exists(directory)) throw new IOException("Discovery output must use a unique Archive directory.");
            Directory.CreateDirectory(directory);
            var manifest = new Manifest { category = "Discovery_" + className.Substring(className.LastIndexOf('.') + 1), testMode = "EditMode", assembly = "XRay.Importer.Tests.Editor", names = Array.Empty<string>() };
            _state = new State { status = "queued", discoveryClass = className, output = directory, project = PRODUCTION, unity = Application.unityVersion, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), queuedUtc = DateTime.UtcNow.ToString("O"), manifest = manifest };
            Persist();
            ScheduleExecute();
            return Status();
        }
        public static string Status()
        {
            return _state == null ? "{\"status\":\"idle\"}" : JsonUtility.ToJson(_state, true);
        }

        private static string RequireArchivePath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("All inputs and outputs must remain under the canonical Archive.");
            return fullPath;
        }

        private static void GuardEditor()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(project, PRODUCTION, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("This validation runner requires the canonical Production project.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Queue tests only in an idle Editor in Edit Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.isDirty) throw new InvalidOperationException("NUnit validation cannot replace or save dirty scenes: " + scene.path);
            }
        }

        private static void ScheduleExecute()
        {
            EditorApplication.update -= ExecuteQueued;
            EditorApplication.update += ExecuteQueued;
        }

        private static void ScheduleRelease()
        {
            EditorApplication.update -= Release;
            EditorApplication.update += Release;
        }

        private static void ExecuteQueued()
        {
            EditorApplication.update -= ExecuteQueued;
            try
            {
                GuardEditor();
                if (_api == null) _api = ScriptableObject.CreateInstance<TestRunnerApi>();
                _state.status = "discovering";
                Persist();
                TestMode mode = _state.manifest.testMode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;
                _api.RetrieveTestList(mode, VerifyAndExecute);
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
            }
        }

        private static void VerifyAndExecute(ITestAdaptor root)
        {
            try
            {
                if (!string.IsNullOrEmpty(_state.discoveryClass))
                {
                    CompleteDiscovery(root);
                    return;
                }
                var requested = new HashSet<string>(_state.manifest.names, StringComparer.Ordinal);
                var found = new HashSet<string>(StringComparer.Ordinal);
                VerifyTree(root, requested, found);
                if (!found.SetEquals(requested)) throw new InvalidDataException("Requested test names were not discovered as runnable leaves: " + string.Join(" | ", Missing(requested, found)));
                if (_state.manifest.useShortImportOutput)
                {
                    if (!Directory.Exists(SHORT_ROOT) || !File.Exists(Path.Combine(SHORT_ROOT, "ShortFixtureAlias.json"))) throw new DirectoryNotFoundException("The accepted task-owned short Archive junction is required for import tests.");
                    _state.previousImportOutput = Environment.GetEnvironmentVariable(IMPORT_OUTPUT);
                    _state.hadPreviousImportOutput = _state.previousImportOutput != null;
                    _state.restoreImportOutput = true;
                    Environment.SetEnvironmentVariable(IMPORT_OUTPUT, Path.Combine(SHORT_ROOT, "I"));
                }
                RegisterCallbacks();
                _state.status = "running";
                Persist();
                var filter = new Filter { testMode = _state.manifest.testMode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode, assemblyNames = new[] { _state.manifest.assembly }, testNames = _state.manifest.names };
                _state.runId = _api.Execute(new ExecutionSettings(filter) { runSynchronously = false });
                Persist();
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
            }
        }

        private static void CompleteDiscovery(ITestAdaptor root)
        {
            var cases = new List<CaseRecord>();
            CollectDiscovery(root, _state.discoveryClass + ".", cases);
            if (cases.Count == 0) throw new InvalidDataException("No leaves were discovered for the exact class: " + _state.discoveryClass);
            cases.Sort((a, b) => string.CompareOrdinal(a.fullName, b.fullName));
            var names = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (CaseRecord item in cases)
            {
                if (!unique.Add(item.fullName)) throw new InvalidDataException("Ambiguous discovered leaf: " + item.fullName);
                if (item.status == RunState.Runnable.ToString()) names.Add(item.fullName);
            }
            if (names.Count == 0) throw new InvalidDataException("No runnable leaves were discovered for the exact class: " + _state.discoveryClass);
            _state.cases = cases.ToArray();
            _state.manifest.names = names.ToArray();
            _state.status = "discovered";
            _state.finishedUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(Path.Combine(_state.output, "Selection.json"), JsonUtility.ToJson(_state.manifest, true));
            Persist();
            ScheduleRelease();
        }

        private static void CollectDiscovery(ITestAdaptor node, string prefix, List<CaseRecord> cases)
        {
            if (!node.IsSuite && node.FullName.StartsWith(prefix, StringComparison.Ordinal)) cases.Add(new CaseRecord { fullName = node.FullName, status = node.RunState.ToString(), message = string.Empty, stackTrace = string.Empty });
            if (node.HasChildren) foreach (ITestAdaptor child in node.Children) CollectDiscovery(child, prefix, cases);
        }
        private static string[] Missing(HashSet<string> requested, HashSet<string> found)
        {
            var missing = new List<string>();
            foreach (string name in requested) if (!found.Contains(name)) missing.Add(name);
            return missing.ToArray();
        }

        private static void VerifyTree(ITestAdaptor node, HashSet<string> requested, HashSet<string> found)
        {
            if (!node.IsSuite && requested.Contains(node.FullName))
            {
                if (node.RunState != RunState.Runnable) throw new InvalidDataException("The requested case is not runnable and will not be forced: " + node.FullName + " " + node.RunState);
                if (!found.Add(node.FullName)) throw new InvalidDataException("The discovered test name is ambiguous: " + node.FullName);
            }
            if (node.HasChildren) foreach (ITestAdaptor child in node.Children) VerifyTree(child, requested, found);
        }

        private static void RegisterCallbacks()
        {
            if (_callbacks != null) return;
            _callbacks = new Callbacks();
            TestRunnerApi.RegisterTestCallback(_callbacks);
        }

        private static void Persist()
        {
            string json = JsonUtility.ToJson(_state, true);
            SessionState.SetString(SESSION_KEY, json);
            File.WriteAllText(Path.Combine(_state.output, "Summary.json"), json);
        }

        private static void Release()
        {
            EditorApplication.update -= Release;
            if (_callbacks != null) TestRunnerApi.UnregisterTestCallback(_callbacks);
            _callbacks = null;
            if (_api != null) UnityEngine.Object.DestroyImmediate(_api);
            _api = null;
            if (_state != null && _state.restoreImportOutput)
            {
                Environment.SetEnvironmentVariable(IMPORT_OUTPUT, _state.hadPreviousImportOutput ? _state.previousImportOutput : null);
                _state.restoreImportOutput = false;
                Persist();
            }
            SessionState.EraseString(SESSION_KEY);
        }

        private static void Fail(string error)
        {
            if (_state == null) throw new InvalidOperationException(error);
            _state.status = "error";
            _state.passed = false;
            _state.error = error;
            _state.finishedUtc = DateTime.UtcNow.ToString("O");
            Persist();
            ScheduleRelease();
        }

        private static void Collect(ITestResultAdaptor result, List<CaseRecord> cases)
        {
            if (!result.Test.IsSuite) cases.Add(new CaseRecord { fullName = result.FullName, status = result.TestStatus.ToString(), message = result.Message, stackTrace = result.StackTrace, duration = result.Duration });
            if (result.HasChildren) foreach (ITestResultAdaptor child in result.Children) Collect(child, cases);
        }

        private sealed class Callbacks : IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                _state.startedUtc = DateTime.UtcNow.ToString("O");
                Persist();
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                try
                {
                    _state.xml = Path.Combine(_state.output, "Results.xml");
                    if (File.Exists(_state.xml)) throw new IOException("Refusing to overwrite an existing NUnit result.");
                    TestRunnerApi.SaveResultToFile(result, _state.xml);
                    if (!File.Exists(_state.xml) || new FileInfo(_state.xml).Length == 0) throw new IOException("The Unity Test Runner did not write NUnit XML.");
                    var cases = new List<CaseRecord>();
                    Collect(result, cases);
                    _state.cases = cases.ToArray();
                    var actual = new HashSet<string>(StringComparer.Ordinal);
                    bool allPassed = true;
                    foreach (CaseRecord item in cases)
                    {
                        if (!actual.Add(item.fullName) || item.status != "Passed") allPassed = false;
                    }
                    _state.passed = allPassed && actual.SetEquals(_state.manifest.names) && cases.Count == _state.manifest.names.Length && result.FailCount == 0 && result.SkipCount == 0 && result.InconclusiveCount == 0;
                    _state.passCount = result.PassCount;
                    _state.failCount = result.FailCount;
                    _state.skipCount = result.SkipCount;
                    _state.status = "completed";
                    _state.finishedUtc = DateTime.UtcNow.ToString("O");
                    Persist();
                    ScheduleRelease();
                }
                catch (Exception exception)
                {
                    Fail(exception.ToString());
                }
            }

            public void TestStarted(ITestAdaptor test)
            {
                if (test.IsSuite) return;
                _state.currentTest = test.FullName;
                Persist();
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }

            public void OnError(string message)
            {
                Fail(message);
            }
        }

        [Serializable]
        private sealed class Manifest
        {
            public string category;
            public string testMode;
            public string assembly;
            public string[] names;
            public bool useShortImportOutput;
        }

        [Serializable]
        private sealed class State
        {
            public string status;
            public string discoveryClass;
            public string output;
            public string manifestPath;
            public string project;
            public string unity;
            public string graphicsApi;
            public string queuedUtc;
            public string startedUtc;
            public string finishedUtc;
            public string runId;
            public string currentTest;
            public string xml;
            public string error;
            public bool passed;
            public int passCount;
            public int failCount;
            public int skipCount;
            public bool restoreImportOutput;
            public bool hadPreviousImportOutput;
            public string previousImportOutput;
            public Manifest manifest;
            public CaseRecord[] cases;
        }

        [Serializable]
        private sealed class CaseRecord
        {
            public string fullName;
            public string status;
            public string discoveryClass;
            public string message;
            public string stackTrace;
            public double duration;
        }
    }
}
