using System;
using System.Reflection;
using NUnit.Framework;
using Rvm.CleanReleaseRoute07;
using UnityEngine;
using XRay.Importer.Levels;

namespace Rvm.Benchmark.Tests
{
    public sealed class FrameTimingDiagnosticTests
    {
        [Test]
        public void DiagnosticRequiresExplicitTimingRouteAndOriginalSubmission()
        {
            Assert.That(FrameTimingDiagnosticSession.Requested(new string[0]), Is.False);
            Assert.That(FrameTimingDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U" }), Is.False);
            Assert.That(FrameTimingDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-frame-timing-diagnostic" }), Is.True);
            Assert.That(FrameTimingDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-instance-native-submission", "A", "-benchmark-frame-timing-diagnostic" }), Is.True);
            Assert.Throws<ArgumentException>(() => FrameTimingDiagnosticSession.Requested(new[] { "-benchmark-frame-timing-diagnostic" }));
            Assert.Throws<ArgumentException>(() => FrameTimingDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "G1", "-benchmark-frame-timing-diagnostic" }));
            Assert.Throws<ArgumentException>(() => FrameTimingDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-instance-native-submission", "B", "-benchmark-frame-timing-diagnostic" }));
            Assert.Throws<ArgumentException>(() => FrameTimingDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-frame-timing-diagnostic", "-benchmark-frame-timing-diagnostic" }));
        }

        [TestCase("-clean-route-config-check")]
        [TestCase("-clean-route-no-timing")]
        [TestCase("-benchmark-pix-diagnostic")]
        [TestCase("-punctual-shadow-mode")]
        [TestCase("-batching-counters")]
        [TestCase("-batching-capture")]
        [TestCase("-preparation-counters")]
        [TestCase("-particle-counters")]
        [TestCase("-location-preparation")]
        [TestCase("-location-preparation-work")]
        [TestCase("-shadow-only-mode")]
        [TestCase("-renderer-local")]
        [TestCase("-particle-frames-diagnostics")]
        [TestCase("-anomaly-visual")]
        [TestCase("-anomaly-events")]
        public void DiagnosticRejectsOtherInstrumentation(string argument)
        {
            Assert.Throws<ArgumentException>(() => FrameTimingDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-frame-timing-diagnostic", argument }));
        }

        [TestCase(false, false, false, true)]
        [TestCase(false, false, true, false)]
        [TestCase(false, true, false, true)]
        [TestCase(false, true, true, false)]
        [TestCase(true, false, true, true)]
        [TestCase(true, false, false, false)]
        [TestCase(true, true, true, false)]
        public void ControllerSeparatesCleanPixAndFrameTiming(bool frameTimingDiagnostic, bool pixDiagnostic, bool frameTimingEnabled, bool accepted)
        {
            var root = new GameObject("Frame timing guard test");
            var weather = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            try
            {
                weather.name = "w_foggy3";
                var controller = root.AddComponent<CleanReleaseRouteController>();
                controller.ConfigureProfiles(root.AddComponent<Camera>(), new BenchmarkScenario[0], "test", weather);
                var type = typeof(CleanReleaseRouteController);
                type.GetField("_frameTimingDiagnostic", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, frameTimingDiagnostic);
                type.GetField("_pixDiagnostic", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, pixDiagnostic);
                var conditionsType = type.GetNestedType("Conditions", BindingFlags.NonPublic);
                object conditions = Activator.CreateInstance(conditionsType);
                object[,] defaults = { { "frameTimingEnabled", frameTimingEnabled }, { "externalDiagnostics", pixDiagnostic }, { "nativeModules", pixDiagnostic ? new[] { "WinPixGpuCapturer.dll" } : new string[0] }, { "graphicsApi", "Direct3D12" }, { "quality", "High Fidelity" }, { "width", 1920 }, { "height", 1080 }, { "cameraPixelWidth", 1920 }, { "cameraPixelHeight", 1080 }, { "renderWidthScale", 1.0f }, { "renderHeightScale", 1.0f }, { "fullScreenMode", "FullScreenWindow" }, { "targetFrameRate", -1 }, { "renderFrameInterval", 1 }, { "runInBackground", true }, { "cameraEnabled", true }, { "cameraTargetIsScreen", true }, { "weather", "w_foggy3" }, { "weatherPaused", true }, { "weatherSeconds", 43200.0 } };
                for (int i = 0; i < defaults.GetLength(0); i++) conditionsType.GetField((string)defaults[i, 0]).SetValue(conditions, defaults[i, 1]);
                var require = type.GetMethod("RequireConditions", BindingFlags.Instance | BindingFlags.NonPublic);
                if (accepted)
                {
                    Assert.DoesNotThrow(() => require.Invoke(controller, new[] { conditions }));
                }
                else
                {
                    var exception = Assert.Throws<TargetInvocationException>(() => require.Invoke(controller, new[] { conditions }));
                    Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(weather);
            }
        }

        [TestCase("", null, false, false, false, "clean")]
        [TestCase("-instance-native-submission", "A", false, false, false, "clean")]
        [TestCase("-instance-native-submission", "B", false, false, false, "clean")]
        [TestCase("-batching-counters", null, false, false, false, "renderer-technical")]
        [TestCase("-batching-capture", null, false, false, false, "renderer-technical")]
        [TestCase("-batching-counters", null, true, false, false, "pix-diagnostic")]
        [TestCase("-batching-counters", null, false, true, false, "punctual-shadow-technical")]
        [TestCase("-batching-counters", null, false, false, true, "frame-timing-diagnostic")]
        [TestCase("-batching-capture", null, true, true, true, "pix-diagnostic")]
        [TestCase("", null, false, true, true, "punctual-shadow-technical")]
        public void MeasurementClassificationExcludesRendererObserversAndPreservesNativeSeries(string argument, string value, bool pixDiagnostic, bool punctualShadowTechnical, bool frameTimingDiagnostic, string expectedClass)
        {
            var type = typeof(CleanReleaseRouteController);
            var resultType = type.GetNestedType("Result", BindingFlags.NonPublic);
            object result = Activator.CreateInstance(resultType);
            string[] args = argument.Length == 0 ? new string[0] : value == null ? new[] { argument } : new[] { argument, value };
            type.GetMethod("ClassifyMeasurement", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { result, (object)pixDiagnostic, punctualShadowTechnical, frameTimingDiagnostic, args });
            Assert.That(resultType.GetField("measurementClass").GetValue(result), Is.EqualTo(expectedClass));
            Assert.That(resultType.GetField("instrumented").GetValue(result), Is.EqualTo(expectedClass != "clean"));
            Assert.That(resultType.GetField("eligibleForFpsComparison").GetValue(result), Is.EqualTo(expectedClass == "clean"));
        }

        [Test]
        public void PublicationsDeduplicateOutOfOrderAndRemainBounded()
        {
            var session = new FrameTimingDiagnosticSession(2);
            var append = typeof(FrameTimingDiagnosticSession).GetMethod("Append", BindingFlags.Instance | BindingFlags.NonPublic);
            append.Invoke(session, new object[] { new FrameTiming { frameStartTimestamp = 300, gpuFrameTime = 0 }, 1000L, 10 });
            append.Invoke(session, new object[] { new FrameTiming { frameStartTimestamp = 200, gpuFrameTime = 2 }, 1100L, 11 });
            append.Invoke(session, new object[] { new FrameTiming { frameStartTimestamp = 300, gpuFrameTime = 3 }, 1200L, 12 });
            Assert.That(session.Count, Is.EqualTo(2));
            Assert.That(session.Overflow, Is.False);
            var samples = (Array)typeof(FrameTimingDiagnosticSession).GetField("_samples", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
            object first = samples.GetValue(0);
            var timing = (FrameTiming)first.GetType().GetField("Timing").GetValue(first);
            Assert.That(timing.gpuFrameTime, Is.EqualTo(3));
            Assert.That(first.GetType().GetField("FirstPollTimestamp").GetValue(first), Is.EqualTo(1000L));
            Assert.That(first.GetType().GetField("LastPollTimestamp").GetValue(first), Is.EqualTo(1200L));
            append.Invoke(session, new object[] { new FrameTiming(), 1300L, 13 });
            Assert.That(session.Count, Is.EqualTo(2));
            Assert.That(session.Overflow, Is.False);
            append.Invoke(session, new object[] { new FrameTiming { frameStartTimestamp = 400 }, 1400L, 14 });
            Assert.That(session.Count, Is.EqualTo(2));
            Assert.That(session.Overflow, Is.True);
        }

        [Test]
        public void AbortReleasesDiagnosticBufferAndRemainsIdempotent()
        {
            var root = new GameObject("Frame timing lifecycle test");
            try
            {
                var controller = root.AddComponent<CleanReleaseRouteController>();
                controller.ConfigureProfiles(root.AddComponent<Camera>(), new BenchmarkScenario[0], "test", null);
                var session = typeof(CleanReleaseRouteController).GetField("_frameTimingSession", BindingFlags.Instance | BindingFlags.NonPublic);
                session.SetValue(controller, new FrameTimingDiagnosticSession(2));
                controller.Abort();
                controller.Abort();
                Assert.That(session.GetValue(controller), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
