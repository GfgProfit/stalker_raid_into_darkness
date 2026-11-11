using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Rvm.Benchmark.Tests
{
    public sealed class BenchmarkV2Tests
    {
        [Test]
        public void DiagnosticRecordersAndProfilerOwnershipAreReleasedWithoutCapture()
        {
            var originalScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene("Assets/_BENCHMARK/Tests/Fixtures/BenchmarkFixture.unity", OpenSceneMode.Additive);
            bool profiler = UnityEngine.Profiling.Profiler.enabled;
            bool cpuArea = UnityEngine.Profiling.Profiler.GetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU);
            var scenario = ScriptableObject.CreateInstance<BenchmarkScenario>();
            BenchmarkDiagnostics diagnostics = null;
            try
            {
                var cameraObject = new GameObject("V2 fixture camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                diagnostics = new BenchmarkDiagnostics(scenario, camera, new BenchmarkMetadata { instrumentationSignature = "fixture" });
                diagnostics.BeginSampling();
                Assert.That(diagnostics.RecorderCount, Is.GreaterThan(0));
                diagnostics.Dispose();
                diagnostics.Dispose();
                Assert.That(diagnostics.RecorderCount, Is.Zero);
                Assert.That(UnityEngine.Profiling.Profiler.enabled, Is.EqualTo(profiler));
                Assert.That(UnityEngine.Profiling.Profiler.GetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU), Is.EqualTo(cpuArea));
            }
            finally
            {
                diagnostics?.Dispose();
                UnityEngine.Object.DestroyImmediate(scenario);
                EditorSceneManager.CloseScene(scene, true);
                SceneManager.SetActiveScene(originalScene);
            }
        }

        [Test]
        public void DiagnosticAggregationKeepsMarkersIndependentAndFiltersStablePos()
        {
            var frames = new[] { new BenchmarkFrame { position = 0, transition = true }, new BenchmarkFrame { position = 0 }, new BenchmarkFrame { position = 1 } };
            var parent = BenchmarkDiagnostics.Aggregate(new[] { 10.0, 20.0, 30.0 }, new long[] { 1, 2, 3 }, frames, 3, 0, true, true);
            var child = BenchmarkDiagnostics.Aggregate(new[] { 5.0, 8.0, 10.0 }, new long[] { 2, 3, 4 }, frames, 3, 0, true, true);
            Assert.That(parent.inclusive.mean, Is.EqualTo(20));
            Assert.That(parent.callCount, Is.EqualTo(2));
            Assert.That(child.inclusive.mean, Is.EqualTo(8));
            Assert.That(parent.inclusive.mean, Is.Not.EqualTo(28));
            Assert.That(BenchmarkDiagnostics.Aggregate(new[] { 0.0, 0.0, 0.0 }, new long[3], frames, 3, -1, false, false).inclusive.available, Is.False);
        }

        [Test]
        public void DelayedGpuObservationsNeverClaimSourceFrameOrPose()
        {
            var observation = new BenchmarkGpuObservation { observedUnityFrame = 100, observedPosition = 2, milliseconds = 5 };
            var restored = JsonUtility.FromJson<BenchmarkGpuObservation>(JsonUtility.ToJson(observation));
            Assert.That(restored.sourceUnityFrame, Is.EqualTo(-1));
            Assert.That(restored.sourcePosition, Is.EqualTo(-1));
        }

        [Test]
        public void FixedWeatherLeaseResetsEachRepeatAndRestoresOnDispose()
        {
            var weather = new Weather { TimeSeconds = 123, Paused = false };
            for (int i = 0; i < 3; i++)
            {
                using (new BenchmarkWorldState(new IBenchmarkWeatherState[] { weather }, 12, "w_foggy3"))
                {
                    Assert.That(weather.TimeSeconds, Is.EqualTo(43200));
                    Assert.That(weather.Paused, Is.True);
                }
                Assert.That(weather.TimeSeconds, Is.EqualTo(123));
                Assert.That(weather.Paused, Is.False);
            }
        }

        [Test]
        public void WeatherLeaseValidatesAllSourcesBeforeMutation()
        {
            var first = new Weather { TimeSeconds = 234 };
            var busy = new Weather { Busy = true };
            Assert.Throws<InvalidOperationException>(() => new BenchmarkWorldState(new IBenchmarkWeatherState[] { first, busy }, 12, "w_foggy3"));
            Assert.That(first.TimeSeconds, Is.EqualTo(234));
            Assert.That(first.Paused, Is.False);
            Assert.Throws<InvalidOperationException>(() => new BenchmarkWorldState(new IBenchmarkWeatherState[] { first }, 12, "different"));
            Assert.Throws<InvalidOperationException>(() => new BenchmarkWorldState(Array.Empty<IBenchmarkWeatherState>(), 12, null));
        }

        [Test]
        public void WeatherLeaseRollsBackPartialAcquisitionAndDisposesOnce()
        {
            var first = new Weather { TimeSeconds = 111 };
            var second = new Weather { TimeSeconds = 222, FailFixedSeek = true };
            Assert.Throws<InvalidOperationException>(() => new BenchmarkWorldState(new IBenchmarkWeatherState[] { first, second }, 12, null));
            Assert.That(first.TimeSeconds, Is.EqualTo(111));
            Assert.That(first.Paused, Is.False);
            Assert.That(second.TimeSeconds, Is.EqualTo(222));
            Assert.That(second.Paused, Is.False);
            var lease = new BenchmarkWorldState(new IBenchmarkWeatherState[] { first }, 12, null);
            lease.Dispose();
            first.TimeSeconds = 999;
            lease.Dispose();
            Assert.That(first.TimeSeconds, Is.EqualTo(999));
        }

        [Test]
        public void LegacyMetadataLoadsAndNewFieldsRoundTrip()
        {
            var legacy = JsonUtility.FromJson<BenchmarkMetadata>("{\"schemaVersion\":1,\"runId\":\"legacy\",\"vsync\":1}");
            Assert.That(legacy.schemaVersion, Is.EqualTo(1));
            Assert.That(legacy.vsync, Is.EqualTo(1));
            Assert.That(legacy.metadataVersion, Is.EqualTo(0));
            var current = new BenchmarkMetadata { metadataVersion = 2, refreshRateHz = 144, renderFrameInterval = 1, instrumentationSignature = "diag", timerSignature = "gpu:yes", fixedWorldState = "12" };
            var restored = JsonUtility.FromJson<BenchmarkMetadata>(JsonUtility.ToJson(current));
            Assert.That(restored.refreshRateHz, Is.EqualTo(144));
            Assert.That(restored.timerSignature, Is.EqualTo("gpu:yes"));
            Assert.That(restored.instrumentationSignature, Is.EqualTo("diag"));
        }

        [Test]
        public void ComparisonFlagsInstrumentationTimersRefreshAndLegacy()
        {
            var a = new BenchmarkMetadata { metadataVersion = 2, instrumentationSignature = "one", timerSignature = "gpu", refreshRateHz = 60 };
            var b = new BenchmarkMetadata { metadataVersion = 2, instrumentationSignature = "two", timerSignature = "none", refreshRateHz = 120 };
            var result = BenchmarkComparison.Compare(a, new BenchmarkSummary(), b, new BenchmarkSummary());
            Assert.That(result.warnings.Any(value => value.Contains("Instrumentation")), Is.True);
            Assert.That(result.warnings.Any(value => value.Contains("Timer availability")), Is.True);
            Assert.That(result.warnings.Any(value => value.Contains("Refresh")), Is.True);
            b.metadataVersion = 0;
            Assert.That(BenchmarkComparison.Compare(a, new BenchmarkSummary(), b, new BenchmarkSummary()).warnings.Any(value => value.Contains("Legacy")), Is.True);
        }

        [Test]
        public void ComparisonAllowsDifferentGitAndObservationFramesButRejectsChangedClock()
        {
            var poses = new[] { new BenchmarkPose(), new BenchmarkPose(), new BenchmarkPose(), new BenchmarkPose() };
            var a = new BenchmarkMetadata { metadataVersion = 2, positions = poses, gitSha = "before", quality = "High Fidelity", finalQuality = "High Fidelity", timerSignature = "QPC", instrumentationSignature = "Standard", targetFrameRate = -1, renderFrameInterval = 1, finalFramePacing = "0/-1/1", environment = new List<BenchmarkEntry> { new BenchmarkEntry { key = "observation.unityFrame", value = "1" }, new BenchmarkEntry { key = "weather.0.GameTimeSeconds", value = "43200" } } };
            var b = JsonUtility.FromJson<BenchmarkMetadata>(JsonUtility.ToJson(a));
            b.gitSha = "after";
            b.environment[0].value = "999";
            var summary = new BenchmarkSummary { status = "completed" };
            for (int i = -1; i < 4; i++)
            {
                summary.windows.Add(new BenchmarkWindow { position = i, window = "Full", frames = new BenchmarkFrameStats() });
                summary.windows.Add(new BenchmarkWindow { position = i, window = "Stable", frames = new BenchmarkFrameStats() });
            }
            Assert.That(BenchmarkComparison.Compare(a, summary, b, summary).comparable, Is.True);
            b.environment[1].value = "45000";
            Assert.That(BenchmarkComparison.Compare(a, summary, b, summary).comparable, Is.False);
            b.environment[1].value = "43200";
            b.finalQuality = "changed";
            Assert.That(BenchmarkComparison.Compare(a, summary, b, summary).warnings.Any(value => value.Contains("within run")), Is.True);
        }

        [Test]
        public void AllQualityLevelsKeepVsyncDisabledWhenSwitched()
        {
            int original = QualitySettings.GetQualityLevel();
            int vsync = QualitySettings.vSyncCount;
            try
            {
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    Assert.That(QualitySettings.vSyncCount, Is.Zero, QualitySettings.names[i]);
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(original, false);
                QualitySettings.vSyncCount = vsync;
            }
        }

        [Test]
        public void WindowsStartupDefaultIsUncappedAndDoesNotOverrideLaterUserChoice()
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(value => value.GetType("WindowsFramePacingDefaults")).FirstOrDefault(value => value != null);
            Assert.That(type, Is.Not.Null);
            MethodInfo method = type.GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Static);
            int oldTarget = Application.targetFrameRate;
            try
            {
                Application.targetFrameRate = 60;
                method.Invoke(null, null);
                Assert.That(Application.targetFrameRate, Is.EqualTo(-1));
                Application.targetFrameRate = 90;
                Assert.That(Application.targetFrameRate, Is.EqualTo(90));
            }
            finally
            {
                Application.targetFrameRate = oldTarget;
            }
        }

        private sealed class Weather : IBenchmarkWeatherState
        {
            public double TimeSeconds { get; set; }
            public bool Paused { get; set; }
            public bool Busy { get; set; }
            public string Profile => "w_foggy3";
            public bool FailFixedSeek { get; set; }

            public void Seek(double seconds)
            {
                if (FailFixedSeek && seconds == 43200)
                {
                    throw new InvalidOperationException("Fixture seek failure");
                }
                TimeSeconds = seconds;
            }

            public void SetPaused(bool value)
            {
                Paused = value;
            }
        }
    }
}
