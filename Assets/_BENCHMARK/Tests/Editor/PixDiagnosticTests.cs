using System;
using System.Reflection;
using NUnit.Framework;
using Rvm.CleanReleaseRoute07;
using UnityEngine;
using XRay.Importer.Levels;

namespace Rvm.Benchmark.Tests
{
    public sealed class PixDiagnosticTests
    {
        [Test]
        public void CleanRejectsPixAndDiagnosticAdmitsOnlyPix()
        {
            string[] pix = { @"C:\PIX\WinPixGpuCapturer.dll" };
            Assert.Throws<InvalidOperationException>(() => PixDiagnosticPolicy.Require(false, true, pix, true));
            Assert.DoesNotThrow(() => PixDiagnosticPolicy.Require(true, true, pix, true));
            foreach (string observer in new[] { "RTSSHooks64.dll", "FramePacingNative.dll", "renderdoc.dll", "bifrost.dll", "Nvda.Graphics.Interception.dll" })
            {
                Assert.Throws<InvalidOperationException>(() => PixDiagnosticPolicy.Require(true, true, new[] { pix[0], observer }, true));
            }
            Assert.Throws<InvalidOperationException>(() => PixDiagnosticPolicy.Require(true, true, new[] { "unknown.dll" }, true));
            Assert.Throws<InvalidOperationException>(() => PixDiagnosticPolicy.Require(true, true, pix, false));
        }

        [Test]
        public void DiagnosticRequiresExplicitBenchmarkSessionAndCapturePose()
        {
            Assert.That(PixDiagnosticPolicy.Requested(new string[0]), Is.False);
            Assert.That(PixDiagnosticPolicy.Requested(new[] { "-benchmark-pix-diagnostic" }), Is.False);
            Assert.That(PixDiagnosticPolicy.Requested(new[] { "-clean-route" }), Is.False);
            Assert.That(PixDiagnosticPolicy.Requested(new[] { "-clean-route", "-benchmark-pix-diagnostic" }), Is.True);
            Assert.That(PixDiagnosticSession.ParseCapturePosition(new string[0]), Is.EqualTo(-1));
            Assert.That(PixDiagnosticSession.ParseCapturePosition(new[] { "-benchmark-pix-capture-position", "3" }), Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => PixDiagnosticSession.ParseCapturePosition(new[] { "-benchmark-pix-capture-position", "0" }));
            Assert.Throws<ArgumentException>(() => PixDiagnosticSession.ParseCapturePosition(new[] { "-benchmark-pix-capture-position" }));
            Assert.That(PixDiagnosticPolicy.HasObserverArguments(new[] { "-batching-counters" }), Is.True);
        }

        [TestCase("-batching-counters")]
        [TestCase("-batching-capture")]
        [TestCase("-preparation-counters")]
        [TestCase("-preparation-capture")]
        [TestCase("-particle-counters")]
        [TestCase("-location-preparation")]
        [TestCase("-location-preparation-work")]
        [TestCase("-shadow-only-mode")]
        [TestCase("-shadow-only-technical")]
        [TestCase("-renderer-local")]
        [TestCase("-particle-frames-local")]
        [TestCase("-particle-frames-diagnostics")]
        [TestCase("-particle-frames-visual")]
        [TestCase("-anomaly-visual")]
        [TestCase("-anomaly-events")]
        public void DiagnosticRejectsOtherBenchmarkModes(string argument)
        {
            Assert.That(PixDiagnosticPolicy.HasObserverArguments(new[] { "-clean-route", "-benchmark-pix-diagnostic", argument }), Is.True);
            Assert.That(PixDiagnosticPolicy.HasObserverArguments(new[] { "-clean-route", "-benchmark-pix-diagnostic", "-location-batching", "U" }), Is.False);
        }

        [TestCase("debugBuild", true)]
        [TestCase("frameTimingEnabled", true)]
        [TestCase("profilerEnabled", true)]
        [TestCase("eventJournalConfigured", true)]
        [TestCase("graphicsApi", "Direct3D11")]
        [TestCase("quality", "Low")]
        [TestCase("width", 1280)]
        [TestCase("cameraPixelHeight", 720)]
        [TestCase("renderWidthScale", 0.5f)]
        [TestCase("fullScreenMode", "Windowed")]
        [TestCase("vSyncCount", 1)]
        [TestCase("targetFrameRate", 60)]
        [TestCase("renderFrameInterval", 2)]
        [TestCase("runInBackground", false)]
        [TestCase("cameraEnabled", false)]
        [TestCase("cameraTargetIsScreen", false)]
        [TestCase("weatherPaused", false)]
        [TestCase("weatherSeconds", 1.0)]
        public void DiagnosticControllerRetainsConfigurationGuard(string field, object invalid)
        {
            var root = new GameObject("PIX guard test");
            var weather = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            try
            {
                weather.name = "w_foggy3";
                var controller = root.AddComponent<CleanReleaseRouteController>();
                controller.ConfigureProfiles(root.AddComponent<Camera>(), new BenchmarkScenario[0], "test", weather);
                var type = typeof(CleanReleaseRouteController);
                type.GetField("_pixDiagnostic", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
                var conditionsType = type.GetNestedType("Conditions", BindingFlags.NonPublic);
                object conditions = Activator.CreateInstance(conditionsType);
                object[,] defaults = { { "externalDiagnostics", true }, { "nativeModules", new[] { "WinPixGpuCapturer.dll" } }, { "graphicsApi", "Direct3D12" }, { "quality", "High Fidelity" }, { "width", 1920 }, { "height", 1080 }, { "cameraPixelWidth", 1920 }, { "cameraPixelHeight", 1080 }, { "renderWidthScale", 1.0f }, { "renderHeightScale", 1.0f }, { "fullScreenMode", "FullScreenWindow" }, { "targetFrameRate", -1 }, { "renderFrameInterval", 1 }, { "runInBackground", true }, { "cameraEnabled", true }, { "cameraTargetIsScreen", true }, { "weather", "w_foggy3" }, { "weatherPaused", true }, { "weatherSeconds", 43200.0 } };
                for (int i = 0; i < defaults.GetLength(0); i++) conditionsType.GetField((string)defaults[i, 0]).SetValue(conditions, defaults[i, 1]);
                var require = type.GetMethod("RequireConditions", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.DoesNotThrow(() => require.Invoke(controller, new[] { conditions }));
                conditionsType.GetField(field).SetValue(conditions, invalid);
                var exception = Assert.Throws<TargetInvocationException>(() => require.Invoke(controller, new[] { conditions }));
                Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(weather);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DiagnosticCompletionAndAbortDisposeNativeSession(bool abort)
        {
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D12) Assert.Ignore("This native diagnostic contract requires D3D12.");
            var root = new GameObject("PIX lifecycle test");
            string outputRoot = Environment.GetEnvironmentVariable("BENCHMARK_ROUTE_TEST_OUTPUT") ?? System.IO.Path.Combine(BenchmarkOutput.DEFAULT_ROOT, "PixDiagnosticTests");
            string output = System.IO.Path.Combine(BenchmarkOutput.ValidateDirectory(outputRoot), Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(output);
            try
            {
                var camera = root.AddComponent<Camera>();
                var controller = root.AddComponent<CleanReleaseRouteController>();
                controller.ConfigureProfiles(camera, new BenchmarkScenario[0], "test", null);
                var session = new PixDiagnosticSession(output, "lifecycle-test", camera, new string[0]);
                var type = typeof(CleanReleaseRouteController);
                type.GetField("_pixSession", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, session);
                if (abort) controller.Abort();
                else type.GetMethod("RestoreState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                string first = System.IO.File.ReadAllText(System.IO.Path.Combine(output, "pix-diagnostic.json"));
                controller.Abort();
                session.Dispose();
                Assert.That(type.GetField("_pixSession", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller), Is.Null);
                Assert.That(System.IO.File.ReadAllText(System.IO.Path.Combine(output, "pix-diagnostic.json")), Is.EqualTo(first));
                Assert.That(System.IO.File.ReadAllLines(System.IO.Path.Combine(output, "clock-sync.csv")).Length, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
