using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using NUnit.Framework;
using Rvm.Benchmark.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rvm.Benchmark.Tests
{
    public sealed class BenchmarkTests
    {
        private const string FIXTURE = "Assets/_BENCHMARK/Tests/Fixtures/BenchmarkFixture.unity";

        [Test]
        public void PercentilesUseLinearInterpolation()
        {
            var distribution = BenchmarkStatistics.Distribution(new double[] { 40, 10, 30, 20 });
            Assert.That(distribution.p50, Is.EqualTo(25));
            Assert.That(distribution.p90, Is.EqualTo(37));
            Assert.That(distribution.p95, Is.EqualTo(38.5));
            Assert.That(distribution.p99, Is.EqualTo(39.7).Within(0.000001));
        }

        [Test]
        public void FpsLowsAverageTheSlowestFrameTimes()
        {
            var frames = new double[1000];
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = 10;
            }
            frames[998] = 50;
            frames[999] = 100;
            var result = BenchmarkStatistics.Frames(frames);
            Assert.That(result.averageFps, Is.EqualTo(1000000.0 / 10130).Within(0.000001));
            Assert.That(result.low1Fps, Is.EqualTo(10000.0 / 230).Within(0.000001));
            Assert.That(result.low01Fps, Is.EqualTo(10));
            Assert.That(result.minFps, Is.EqualTo(10));
            Assert.That(result.maxFps, Is.EqualTo(100));
        }

        [TestCase(0, false, false)]
        [TestCase(99, false, false)]
        [TestCase(100, true, false)]
        [TestCase(999, true, false)]
        [TestCase(1000, true, true)]
        public void LowAvailabilityRequiresEnoughFrames(int count, bool low1, bool low01)
        {
            var frames = new double[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = 20;
            }
            var result = BenchmarkStatistics.Frames(frames);
            Assert.That(result.low1Available, Is.EqualTo(low1));
            Assert.That(result.low01Available, Is.EqualTo(low01));
        }

        [Test]
        public void InvalidSamplesRemainUnavailable()
        {
            var result = BenchmarkStatistics.Distribution(new[] { double.NaN, double.PositiveInfinity, -1.0 });
            Assert.That(result.available, Is.False);
            Assert.That(result.samples, Is.Zero);
            Assert.That(BenchmarkStatistics.Frames(new double[] { 0, -1 }).frameTime.available, Is.False);
        }

        [TestCase(100, 110, true, "Improvement", 10)]
        [TestCase(20, 25, false, "Regression", 25)]
        [TestCase(20, 18, false, "Improvement", -10)]
        [TestCase(20, 20, false, "No meaningful difference", 0)]
        public void ComparisonsRespectMetricDirection(double baseline, double candidate, bool higher, string classification, double percent)
        {
            var result = BenchmarkComparison.Delta(baseline, candidate, true, higher, true, 0);
            Assert.That(result.classification, Is.EqualTo(classification));
            Assert.That(result.percent, Is.EqualTo(percent));
        }

        [Test]
        public void ComparisonRejectsZeroBaselineMissingAndMismatchedConditions()
        {
            Assert.That(BenchmarkComparison.Delta(0, 10, true, false, true, 0).available, Is.False);
            Assert.That(BenchmarkComparison.Delta(10, 0, false, false, true, 0).available, Is.False);
            Assert.That(BenchmarkComparison.Delta(10, 5, true, false, false, 0).classification, Does.StartWith("Incomparable"));
            var baseline = new BenchmarkMetadata { graphicsApi = "Direct3D11", positions = Poses() };
            var candidate = new BenchmarkMetadata { graphicsApi = "Direct3D12", positions = Poses() };
            var result = BenchmarkComparison.Compare(baseline, new BenchmarkSummary(), candidate, new BenchmarkSummary());
            Assert.That(result.comparable, Is.False);
            Assert.That(result.warnings.Exists(warning => warning.Contains("Graphics API")), Is.True);
        }

        [Test]
        public void PracticalToleranceIsExplicit()
        {
            Assert.That(BenchmarkComparison.Delta(100, 101, true, true, true, 2).classification, Is.EqualTo("No meaningful difference"));
            Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkComparison.Compare(new BenchmarkMetadata(), new BenchmarkSummary(), new BenchmarkMetadata(), new BenchmarkSummary(), -1));
        }

        [Test]
        public void SequenceVisitsFourPositionsWithoutHiddenGaps()
        {
            var sequence = new BenchmarkSequence(5, 2);
            sequence.Start(100);
            Assert.That(sequence.Measuring, Is.False);
            Assert.That(sequence.Advance(101.9), Is.False);
            Assert.That(sequence.Advance(102), Is.True);
            for (int position = 0; position < 4; position++)
            {
                Assert.That(sequence.Position, Is.EqualTo(position));
                Assert.That(sequence.Advance(102 + position * 5 + 4.999), Is.False);
                Assert.That(sequence.Advance(107 + position * 5), Is.True);
            }
            Assert.That(sequence.Complete, Is.True);
            Assert.That(sequence.Measuring, Is.False);
        }

        [Test]
        public void SequenceDoesNotSkipPositionsOnSlowFrames()
        {
            var sequence = new BenchmarkSequence(5, 0);
            sequence.Start(0);
            sequence.Advance(6);
            Assert.That(sequence.Position, Is.EqualTo(1));
            Assert.That(sequence.PhaseStart, Is.EqualTo(6));
            Assert.That(sequence.Advance(10), Is.False);
            Assert.Throws<InvalidOperationException>(() => sequence.Advance(9));
        }

        [Test]
        public void ScenarioRejectsMissingDuplicateAndInvalidDurations()
        {
            var scenario = ScriptableObject.CreateInstance<BenchmarkScenario>();
            try
            {
                Set(scenario, "_scenePath", FIXTURE);
                Assert.That(scenario.Validate(), Is.Null);
                Set(scenario, "_positionKeys", new[] { "A", "B", "C", "A" });
                Assert.That(scenario.Validate(), Does.Contain("unique"));
                Set(scenario, "_positionKeys", new[] { "A", "B", "C", "D" });
                Set(scenario, "_positionSeconds", double.NaN);
                Assert.That(scenario.Validate(), Is.Not.Null);
                Set(scenario, "_positionSeconds", 5.0);
                Set(scenario, "_stabilizationSeconds", 5.0);
                Assert.That(scenario.Validate(), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scenario);
            }
        }

        [Test]
        public void SourceTimingIsMatchedByTimestampRatherThanReceiveFrame()
        {
            var frames = new[] { new BenchmarkFrame { startTicks = 100 }, new BenchmarkFrame { startTicks = 200 }, new BenchmarkFrame { startTicks = 300 } };
            Assert.That(BenchmarkCollector.SourceRow(frames, frames.Length, 180), Is.EqualTo(1));
            Assert.That(BenchmarkCollector.SourceRow(frames, frames.Length, 280), Is.EqualTo(2));
            Assert.That(BenchmarkCollector.SourceRow(frames, frames.Length, 301), Is.EqualTo(3));
        }

        [Test]
        public void OutOfOrderDelayedTimingsAreRetainedAndDeduplicated()
        {
            using (var collector = new BenchmarkCollector(1024, BenchmarkMode.Standard))
            {
                var append = typeof(BenchmarkCollector).GetMethod("AppendTiming", BindingFlags.NonPublic | BindingFlags.Instance);
                append.Invoke(collector, new object[] { new FrameTiming { frameStartTimestamp = 200 } });
                append.Invoke(collector, new object[] { new FrameTiming { frameStartTimestamp = 100 } });
                append.Invoke(collector, new object[] { new FrameTiming { frameStartTimestamp = 200 } });
                Assert.That(collector.TimingCount, Is.EqualTo(2));
                Assert.That(collector.Timings[1].frameStartTimestamp, Is.EqualTo(100));
            }
        }

        [TestCase(1, true)]
        [TestCase(2, false)]
        public void DelayedTimingsRejectAmbiguousMatches(int copies, bool expected)
        {
            if (!BenchmarkClock.WindowsQpc || FrameTimingManager.GetCpuTimerFrequency() != (ulong)BenchmarkClock.Frequency)
            {
                Assert.Ignore("Validated Windows QPC frame timing timebase is required.");
            }
            using (var collector = new BenchmarkCollector(1024, BenchmarkMode.Standard))
            {
                var timing = new FrameTiming { frameStartTimestamp = 150, cpuTimePresentCalled = 250, cpuFrameTime = 20, cpuMainThreadFrameTime = 10, cpuRenderThreadFrameTime = 5, gpuFrameTime = 6 };
                for (int i = 0; i < copies; i++)
                {
                    collector.Timings[i] = timing;
                }
                typeof(BenchmarkCollector).GetProperty("TimingCount").SetValue(collector, copies);
                var frames = new[] { new BenchmarkFrame { startTicks = 100 }, new BenchmarkFrame { startTicks = 200 }, new BenchmarkFrame { startTicks = 300 } };
                collector.AlignTimings(frames, frames.Length, 400, true);
                Assert.That(collector.Metrics[BenchmarkCollector.GPU].available, Is.EqualTo(expected));
                Assert.That(double.IsNaN(collector.Values[BenchmarkCollector.METRIC_COUNT + BenchmarkCollector.CPU_RENDER]), Is.EqualTo(!expected));
            }
        }

        [Test]
        public void StableWindowRetainsTransitionOnlyInFullAndSerializesUnavailable()
        {
            var metadata = new BenchmarkMetadata { runId = "test", positionSeconds = 5 };
            var frames = new[] { new BenchmarkFrame { position = 0, transition = true, frameMs = 30 }, new BenchmarkFrame { position = 0, transition = false, frameMs = 10 } };
            using (var collector = new BenchmarkCollector(1024, BenchmarkMode.Standard))
            {
                var summary = BenchmarkOutput.Summarize(metadata, frames, frames.Length, collector, null);
                Assert.That(summary.windows.Count, Is.EqualTo(10));
                Assert.That(summary.windows[0].frames.frameTime.samples, Is.EqualTo(2));
                Assert.That(summary.windows[1].frames.frameTime.samples, Is.EqualTo(1));
                Assert.That(summary.windows[1].frames.frameTime.mean, Is.EqualTo(10));
                Assert.That(summary.windows[0].metrics[BenchmarkCollector.GPU].values.available, Is.False);
                string json = JsonUtility.ToJson(summary);
                var restored = JsonUtility.FromJson<BenchmarkSummary>(json);
                Assert.That(restored.windows[1].frames.frameTime.mean, Is.EqualTo(10));
                Assert.That(restored.windows[0].metrics[BenchmarkCollector.GPU].values.available, Is.False);
            }
        }

        [Test]
        public void OutputRejectsProductionAndCleanTestsPaths()
        {
            Assert.Throws<IOException>(() => BenchmarkOutput.ValidateDirectory("E:/Projects/stalker_raid_into_darkness/BenchmarkResults/run"));
            Assert.Throws<IOException>(() => BenchmarkOutput.ValidateDirectory("E:/Projects/stalker_raid_into_darkness_clean_tests/BenchmarkResults/run"));
            Assert.Throws<IOException>(() => BenchmarkOutput.ValidateDirectory(BenchmarkOutput.ARCHIVE_ROOT + "_Other/run"));
        }

        [Test]
        public void RunnerAbortRestoresCameraDisposesAndPreservesPartialFiles()
        {
            var originalActive = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(FIXTURE, OpenSceneMode.Additive);
            var scenario = ScriptableObject.CreateInstance<BenchmarkScenario>();
            GameObject host = null;
            try
            {
                Set(scenario, "_scenePath", FIXTURE);
                host = new GameObject("BenchmarkLifecycleFixture");
                SceneManager.MoveGameObjectToScene(host, scene);
                var cameraObject = new GameObject("Fixture Camera");
                cameraObject.transform.SetParent(host.transform);
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.localPosition = new Vector3(3, 4, 5);
                camera.transform.localRotation = Quaternion.Euler(1, 2, 3);
                Vector3 savedPosition = camera.transform.localPosition;
                Quaternion savedRotation = camera.transform.localRotation;
                var positions = new Transform[4];
                for (int i = 0; i < 4; i++)
                {
                    var position = new GameObject("Position " + i).transform;
                    position.SetParent(host.transform);
                    position.position = new Vector3(i + 100, 10, 20);
                    positions[i] = position;
                }
                var runner = host.AddComponent<BenchmarkRunner>();
                bool previousRunInBackground = Application.runInBackground;
                runner.Configure(scenario, camera, positions);
                Assert.That(runner.Validate(), Is.Null);
                runner.Begin(BenchmarkOutput.DEFAULT_ROOT + "/AutomatedTests");
                Assert.That(runner.Running, Is.True);
                Assert.That(camera.transform.position, Is.EqualTo(positions[0].position));
                runner.Abort("Automated lifecycle test interruption");
                Assert.That(runner.Running, Is.False);
                Assert.That(Application.runInBackground, Is.EqualTo(previousRunInBackground));
                Assert.That(runner.Saved, Is.True);
                Assert.That(camera.transform.localPosition, Is.EqualTo(savedPosition));
                Assert.That(Quaternion.Angle(camera.transform.localRotation, savedRotation), Is.LessThan(0.001));
                Assert.That(runner.LastSummary.status, Is.EqualTo("aborted"));
                var collector = typeof(BenchmarkRunner).GetField("_collector", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(runner);
                Assert.That(typeof(BenchmarkCollector).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(collector), Is.True);
                Assert.That(File.Exists(Path.Combine(runner.LastDirectory, "frames.csv")), Is.True);
                Assert.That(File.Exists(Path.Combine(runner.LastDirectory, "report.md")), Is.True);
                runner.Abort("Repeated abort");
                BenchmarkComparison.ReadRun(runner.LastDirectory, out var restoredMetadata, out var restoredSummary);
                Assert.That(restoredSummary.runId, Is.EqualTo(restoredMetadata.runId));
            }
            finally
            {
                if (host != null)
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
                UnityEngine.Object.DestroyImmediate(scenario);
                EditorSceneManager.CloseScene(scene, true);
                SceneManager.SetActiveScene(originalActive);
            }
        }

        [Test]
        public void SamplerHotPathDoesNotAllocateManagedMemory()
        {
            using (var collector = new BenchmarkCollector(1024, BenchmarkMode.Standard))
            {
                for (int i = 0; i < 32; i++)
                {
                    collector.ReadPreviousFrame(0);
                    collector.PollTimings();
                }
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                for (int i = 0; i < 1000; i++)
                {
                    collector.ReadPreviousFrame(0);
                    collector.PollTimings();
                }
                long elapsed = Stopwatch.GetTimestamp() - started;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                string directory = BenchmarkOutput.ValidateDirectory(Path.Combine(BenchmarkOutput.DEFAULT_ROOT, "AutomatedTests", "Sampler_" + Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "overhead.txt"), "1000 warmed in-process sampler iterations; not total profiler/GPU overhead. Managed allocated bytes=" + allocated + "; microseconds/iteration=" + BenchmarkOutput.Number((double)elapsed / Stopwatch.Frequency * 1000));
                Assert.That(allocated, Is.Zero);
            }
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private static BenchmarkPose[] Poses()
        {
            return new[] { new BenchmarkPose(), new BenchmarkPose(), new BenchmarkPose(), new BenchmarkPose() };
        }
    }
}
