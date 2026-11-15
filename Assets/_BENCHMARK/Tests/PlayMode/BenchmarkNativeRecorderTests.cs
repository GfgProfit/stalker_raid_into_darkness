using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rvm.Benchmark.Tests
{
    public sealed class BenchmarkNativeRecorderTests
    {
        private const string FIXTURE = "Assets/_BENCHMARK/Tests/Fixtures/BenchmarkFixture.unity";

        private string _originalScene;
        private bool _profiler;
        private bool _cpuArea;
        private bool _binary;
        private string _profilerFile;
        private PropertyInfo _rendererSwitch;
        private bool _rendererFlag;
        private BenchmarkRunner _runner;

        [UnityTest]
        public IEnumerator NativeFreshnessGaugesAndExternalOwnershipSurviveCompleteAndAbort()
        {
            _originalScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;

            _profiler = UnityEngine.Profiling.Profiler.enabled;
            _cpuArea = UnityEngine.Profiling.Profiler.GetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU);
            _binary = UnityEngine.Profiling.Profiler.enableBinaryLog;
            _profilerFile = UnityEngine.Profiling.Profiler.logFile;
            Assert.That(_binary, Is.False, "Do not interrupt an external binary capture.");
            EditorSceneManager.LoadSceneInPlayMode(FIXTURE, new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
            yield return null;
            UnityEngine.Profiling.Profiler.enabled = true;
            UnityEngine.Profiling.Profiler.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU, true);
            var marker = new ProfilerMarker("RVM.XRay.RecorderOwnershipV21");
            var gauge = new ProfilerCounter<long>(ProfilerCategory.Memory, "RVM.GaugeV21", ProfilerMarkerDataUnit.Bytes);
            var handles = new System.Collections.Generic.List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var markerHandle = handles.First(value => ProfilerRecorderHandle.GetDescription(value).Name == "RVM.XRay.RecorderOwnershipV21");
            var gaugeHandle = handles.First(value => ProfilerRecorderHandle.GetDescription(value).Name == "RVM.GaugeV21");
            using var external = ProfilerRecorder.StartNew(marker, 256);
            using var timing = new BenchmarkRecorderSource(markerHandle, ProfilerRecorderHandle.GetDescription(markerHandle), 1, 256);
            using var memory = new BenchmarkRecorderSource(gaugeHandle, ProfilerRecorderHandle.GetDescription(gaugeHandle), 1, 256);
            using (marker.Auto())
            {
                System.Threading.Thread.SpinWait(10000);
            }
            gauge.Sample(10);
            gauge.Sample(20);
            yield return null;
            var first = timing.Read(Time.frameCount);
            var two = memory.Read(Time.frameCount);
            Assert.That(first.status, Is.EqualTo(BenchmarkSampleStatus.Valid));
            Assert.That(first.Value, Is.GreaterThan(0));
            Assert.That(two.status, Is.EqualTo(BenchmarkSampleStatus.Valid));
            Assert.That(two.Value, Is.EqualTo(20));
            Assert.That(two.publications, Is.EqualTo(2));
            yield return null;
            var absent = timing.Read(Time.frameCount);
            var noGauge = memory.Read(Time.frameCount);
            Assert.That(absent.status == BenchmarkSampleStatus.NoPublication || absent.status == BenchmarkSampleStatus.Valid && absent.calls == 0 && absent.Value == 0, Is.True);
            Assert.That(noGauge.status, Is.EqualTo(BenchmarkSampleStatus.NoPublication));
            gauge.Sample(0);
            yield return null;
            Assert.That(memory.Read(Time.frameCount).Value, Is.Zero);
            Type rendererType = AppDomain.CurrentDomain.GetAssemblies().Select(value => value.GetType("XRay.Importer.Levels.XRayLevelInstancedRenderer")).First(value => value != null);
            _rendererSwitch = rendererType.GetProperty("DiagnosticMarkersEnabled", BindingFlags.Public | BindingFlags.Static);
            _rendererFlag = _rendererSwitch != null && (bool)_rendererSwitch.GetValue(null);
            _rendererSwitch?.SetValue(null, true);
            var root = new GameObject("Recorder ownership fixture");
            root.AddComponent(rendererType);
            var camera = new GameObject("Fixture camera").AddComponent<Camera>();
            var positions = new Transform[4];
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = new GameObject("POS " + i).transform;
                positions[i].position = new Vector3(i, 0, 0);
            }
            var scenario = ScriptableObject.CreateInstance<BenchmarkScenario>();
            var settings = new SerializedObject(scenario);
            settings.FindProperty("_scenePath").stringValue = FIXTURE;
            settings.FindProperty("_positionSeconds").doubleValue = 0.2;
            settings.FindProperty("_warmupSeconds").doubleValue = 0;
            settings.FindProperty("_stabilizationSeconds").doubleValue = 0.001;
            settings.FindProperty("_frameCapacity").intValue = 65536;
            settings.FindProperty("_diagnosticGpu").boolValue = false;
            settings.FindProperty("_diagnosticRendering").boolValue = false;
            settings.FindProperty("_diagnosticMemory").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            scenario.ConfigureSession(BenchmarkMode.Diagnostic, false, 12, null, true, 4);
            var runner = root.AddComponent<BenchmarkRunner>();
            _runner = runner;
            runner.Configure(scenario, camera, positions);
            string output = Environment.GetEnvironmentVariable("RVM_BENCHMARK_VALIDATION_ROOT") ?? Path.Combine(BenchmarkOutput.DEFAULT_ROOT, "AutomatedTests", "V21");
            for (int iteration = 0; iteration < 2; iteration++)
            {
                runner.Begin(output);
                int frames = 0;
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (runner.Running && Time.realtimeSinceStartupAsDouble < deadline && frames++ < 100000)
                {
                    using (marker.Auto())
                    {
                        System.Threading.Thread.SpinWait(1000);
                    }
                    if (iteration == 1 && (bool)typeof(BenchmarkRunner).GetField("_ownsCapture", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runner))
                    {
                        runner.Abort("Recorder ownership regression fixture.");
                    }
                    yield return null;
                }
                Assert.That(runner.Running, Is.False);
                Assert.That(runner.Saved, Is.True, Convert.ToString(typeof(BenchmarkRunner).GetField("_error", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runner)));
                Assert.That(runner.LastSummary.status, Is.EqualTo(iteration == 0 ? "completed" : "aborted"));
                Assert.That(external.Valid && external.IsRunning, Is.True);
                Assert.That(UnityEngine.Profiling.Profiler.enabled, Is.True);
                Assert.That(UnityEngine.Profiling.Profiler.GetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU), Is.True);
                Assert.That(UnityEngine.Profiling.Profiler.enableBinaryLog, Is.EqualTo(_binary));
                Assert.That(UnityEngine.Profiling.Profiler.logFile, Is.EqualTo(_profilerFile));
                if (_rendererSwitch != null)
                {
                    Assert.That((bool)_rendererSwitch.GetValue(null), Is.True);
                }
            }
            timing.Dispose();
            using (marker.Auto())
            {
                System.Threading.Thread.SpinWait(1000);
            }
            yield return null;
            Assert.That(external.Valid && external.IsRunning && external.LastValue > 0, Is.True);
            UnityEngine.Object.Destroy(scenario);
        }

        [UnityTearDown]
        public IEnumerator RestoreEditor()
        {
            if (_runner != null && _runner.Running)
            {
                _runner.Abort("Native test teardown.");
            }
            if (!string.IsNullOrEmpty(_originalScene))
            {

                _rendererSwitch?.SetValue(null, _rendererFlag);
                UnityEngine.Profiling.Profiler.enabled = _profiler;
                UnityEngine.Profiling.Profiler.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU, _cpuArea);
                UnityEngine.Profiling.Profiler.enableBinaryLog = _binary;
                UnityEngine.Profiling.Profiler.logFile = _profilerFile;

                _originalScene = null;
            }
            yield return null;
        }
    }
}
