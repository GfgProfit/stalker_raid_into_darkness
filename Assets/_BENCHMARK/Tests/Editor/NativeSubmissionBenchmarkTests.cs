using System;
using System.Reflection;
using NUnit.Framework;
using Rvm.CleanReleaseRoute07;
using UnityEngine;
using XRay.Importer.Levels;

namespace Rvm.Benchmark.Tests
{
    public sealed class NativeSubmissionBenchmarkTests
    {
        [Test]
        public void OrdinaryArgumentsDoNotRequestNativeOverride()
        {
            Assert.That(BenchmarkNativeSubmissionState.RequestedMode(new string[0]), Is.Null);
            Assert.That(BenchmarkNativeSubmissionState.RequestedMode(new[] { "-clean-route", "-location-batching", "U" }), Is.Null);
            Assert.That(BenchmarkNativeSubmissionState.Enabled("A"), Is.False);
            Assert.That(BenchmarkNativeSubmissionState.Enabled("B"), Is.True);
            Assert.Throws<ArgumentException>(() => BenchmarkNativeSubmissionState.Enabled("G1"));
        }

        [TestCase("-clean-route")]
        [TestCase("-clean-route-no-timing")]
        [TestCase("-clean-route-config-check")]
        public void NativeOverrideRequiresExplicitRouteAndValidMode(string route)
        {
            Assert.That(BenchmarkNativeSubmissionState.RequestedMode(new[] { route, "-instance-native-submission", "A" }), Is.EqualTo("A"));
            Assert.That(BenchmarkNativeSubmissionState.RequestedMode(new[] { route, "-instance-native-submission", "B" }), Is.EqualTo("B"));
            Assert.Throws<ArgumentException>(() => BenchmarkNativeSubmissionState.RequestedMode(new[] { "-instance-native-submission", "B" }));
            Assert.Throws<ArgumentException>(() => BenchmarkNativeSubmissionState.RequestedMode(new[] { route, "-instance-native-submission" }));
            Assert.Throws<ArgumentException>(() => BenchmarkNativeSubmissionState.RequestedMode(new[] { route, "-instance-native-submission", "U" }));
            Assert.Throws<ArgumentException>(() => BenchmarkNativeSubmissionState.RequestedMode(new[] { route, "-instance-native-submission", "A", "-instance-native-submission", "B" }));
            Assert.That(PixDiagnosticPolicy.HasObserverArguments(new[] { route, "-instance-native-submission", "A" }), Is.True);
        }

        [TestCase("A", -1)]
        [TestCase("A", 0)]
        [TestCase("A", 1)]
        [TestCase("B", -1)]
        [TestCase("B", 0)]
        [TestCase("B", 1)]
        public void SessionOverrideRestoresEachOriginalValueWithoutWritingConfiguration(string mode, int previous)
        {
            var root = new GameObject("Native submission session");
            var configuration = ScriptableObject.CreateInstance<GameConfig>();
            try
            {
                var renderer = root.AddComponent<XRayLevelInstancedRenderer>();
                renderer.ApplyConfiguration(configuration);
                bool? original = previous < 0 ? (bool?)null : previous != 0;
                renderer.NativeSubmissionOverride = original;
                string serialized = JsonUtility.ToJson(configuration);
                var renderers = new[] { renderer };
                var state = new BenchmarkNativeSubmissionState(renderers, mode);
                try
                {
                    Assert.That(renderer.NativeSubmissionOverride, Is.EqualTo(mode == "B"));
                    Assert.That(renderer.Configuration, Is.SameAs(configuration));
                    Assert.That(JsonUtility.ToJson(configuration), Is.EqualTo(serialized));
                    renderers[0] = null;
                }
                finally
                {
                    state.Dispose();
                }
                Assert.That(renderer.NativeSubmissionOverride, Is.EqualTo(original));
                renderer.NativeSubmissionOverride = !BenchmarkNativeSubmissionState.Enabled(mode);
                state.Dispose();
                Assert.That(renderer.NativeSubmissionOverride, Is.EqualTo(mode != "B"));
                Assert.That(JsonUtility.ToJson(configuration), Is.EqualTo(serialized));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompletionAndAbortRestoreNativeOverride(bool abort)
        {
            var root = new GameObject("Native submission route lifecycle");
            try
            {
                var camera = root.AddComponent<Camera>();
                var renderer = root.AddComponent<XRayLevelInstancedRenderer>();
                renderer.NativeSubmissionOverride = false;
                using (var state = new BenchmarkNativeSubmissionState(new[] { renderer }, "B"))
                {
                    typeof(LocationBatchingBenchmark).GetField("_nativeState", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, state);
                    var controller = root.AddComponent<CleanReleaseRouteController>();
                    controller.ConfigureProfiles(camera, new BenchmarkScenario[0], "native-test", null);
                    if (abort) controller.Abort();
                    else typeof(CleanReleaseRouteController).GetMethod("RestoreState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
                    Assert.That(renderer.NativeSubmissionOverride, Is.False);
                    Assert.That(typeof(LocationBatchingBenchmark).GetField("_nativeState", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), Is.Null);
                    controller.Abort();
                    Assert.That(renderer.NativeSubmissionOverride, Is.False);
                }
            }
            finally
            {
                LocationBatchingBenchmark.RestoreMode();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
