using System;
using System.Reflection;
using NUnit.Framework;
using Rvm.CleanReleaseRoute07;
using UnityEngine;

namespace Rvm.Benchmark.Tests
{
    public sealed class ReleaseCpuDiagnosticTests
    {
        [Test]
        public void DiagnosticRequiresOneExplicitTimingRouteAndUngroupedSubmission()
        {
            Assert.That(ReleaseCpuDiagnosticSession.Requested(Array.Empty<string>()), Is.False);
            Assert.That(ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U" }), Is.False);
            Assert.That(ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-cpu-diagnostic" }), Is.True);
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-benchmark-cpu-diagnostic" }));
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "G1", "-benchmark-cpu-diagnostic" }));
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-cpu-diagnostic", "-benchmark-cpu-diagnostic" }));
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-clean-route", "-location-batching", "U", "-benchmark-cpu-diagnostic" }));
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-location-batching", "U", "-benchmark-cpu-diagnostic" }));
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-cpu-diagnostic", "-instance-native-submission", "C" }));
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-cpu-diagnostic", "-instance-native-submission" }));
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-cpu-diagnostic", "-instance-native-submission", "A", "-instance-native-submission", "B" }));
        }

        [TestCase("A")]
        [TestCase("B")]
        public void DiagnosticAllowsAnExplicitNativeModeWithoutTreatingItAsAnObserver(string mode)
        {
            Assert.That(ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-instance-native-submission", mode, "-benchmark-cpu-diagnostic" }), Is.True);
        }

        [TestCase("-clean-route-config-check")]
        [TestCase("-clean-route-no-timing")]
        [TestCase("-benchmark-pix-diagnostic")]
        [TestCase("-benchmark-frame-timing-diagnostic")]
        [TestCase("-batching-counters")]
        [TestCase("-batching-capture")]
        [TestCase("-preparation-counters")]
        [TestCase("-renderer-local")]
        public void DiagnosticRejectsOtherMeasurementModes(string argument)
        {
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-cpu-diagnostic", argument }));
        }

        [Test]
        public void MarkerConfigurationRequiresOneExplicitDiagnosticOwnerAndAValue()
        {
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-cpu-marker-config", "markers.json" }));
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-cpu-diagnostic", "-benchmark-cpu-marker-config" }));
            Assert.Throws<ArgumentException>(() => ReleaseCpuDiagnosticSession.Requested(new[] { "-clean-route", "-location-batching", "U", "-benchmark-cpu-diagnostic", "-benchmark-cpu-marker-config", "first.json", "-benchmark-cpu-marker-config", "second.json" }));
        }

        [Test]
        public void ThreadCpuTimesAddKernelAndUserHundredNanosecondsWithoutWallClamping()
        {
            Assert.That(ReleaseCpuDiagnosticSession.TryCpuMilliseconds(10000L, 30000L, 14000L, 36000L, 1000L, 1001L, out double value), Is.True);
            Assert.That(value, Is.EqualTo(1.0).Within(0.0000000001));
            Assert.That(ReleaseCpuDiagnosticSession.TryCpuMilliseconds(10000L, 30000L, 10000L, 30000L, 1001L, 1002L, out value), Is.True);
            Assert.That(value, Is.Zero);
        }

        [Test]
        public void InvalidThreadCountersAndTimestampsRemainUnavailable()
        {
            long[,] cases = { { -1L, 2L, 3L, 4L, 100L, 200L }, { 1L, -2L, 3L, 4L, 100L, 200L }, { 1L, 2L, -3L, 4L, 100L, 200L }, { 1L, 2L, 3L, -4L, 100L, 200L }, { 10L, 20L, 9L, 30L, 100L, 200L }, { 10L, 20L, 11L, 19L, 100L, 200L }, { 10L, 20L, 11L, 21L, 100L, 100L }, { 10L, 20L, 11L, 21L, 100L, 99L } };
            for (int index = 0; index < cases.GetLength(0); index++)
            {
                Assert.That(ReleaseCpuDiagnosticSession.TryCpuMilliseconds(cases[index, 0], cases[index, 1], cases[index, 2], cases[index, 3], cases[index, 4], cases[index, 5], out double value), Is.False, "Invalid case " + index);
                Assert.That(double.IsNaN(value), Is.True, "Invalid case " + index);
            }
        }

        [Test]
        public void LargeValidThreadCountersDoNotOverflowBeforeDoubleConversion()
        {
            Assert.That(ReleaseCpuDiagnosticSession.TryCpuMilliseconds(0L, 0L, long.MaxValue, long.MaxValue, 100L, 200L, out double value), Is.True);
            Assert.That(value, Is.EqualTo((double)long.MaxValue * 0.0002).Within(1.0));
            Assert.That(double.IsInfinity(value), Is.False);
            Assert.That(ReleaseCpuDiagnosticSession.TryCpuMilliseconds(long.MaxValue - 10L, long.MaxValue - 10L, long.MaxValue, long.MaxValue, 100L, 200L, out value), Is.True);
            Assert.That(value, Is.EqualTo(0.002).Within(0.000000000001));
        }

        [Test]
        public void ClockAlignmentAllowsIndependentEpochsFrequenciesAndZeroDurationBrackets()
        {
            Assert.That(ReleaseCpuDiagnosticSession.TryClockOffsetBounds(100L, 1000000L, 120L, 1000L, 10L, out double minimum, out double maximum), Is.True);
            Assert.That(minimum, Is.EqualTo(988.0));
            Assert.That(maximum, Is.EqualTo(990.0));
            Assert.That(ReleaseCpuDiagnosticSession.TryClockOffsetBounds(1000000L, 100L, 1000000L, 10L, 1000L, out minimum, out maximum), Is.True);
            Assert.That(minimum, Is.EqualTo(-990.0));
            Assert.That(maximum, Is.EqualTo(-990.0));
        }

        [Test]
        public void InvalidClockAlignmentInputsRemainUnavailable()
        {
            long[,] cases = { { 120L, 1000000L, 100L, 1000L, 10L }, { -1L, 1000000L, 120L, 1000L, 10L }, { 100L, 0L, 120L, 1000L, 10L }, { 100L, -1L, 120L, 1000L, 10L }, { 100L, 1000000L, 120L, 0L, 10L }, { 100L, 1000000L, 120L, -1L, 10L }, { 100L, 1000000L, 120L, 1000L, 0L }, { 100L, 1000000L, 120L, 1000L, -1L } };
            for (int index = 0; index < cases.GetLength(0); index++)
            {
                Assert.That(ReleaseCpuDiagnosticSession.TryClockOffsetBounds(cases[index, 0], cases[index, 1], cases[index, 2], cases[index, 3], cases[index, 4], out double minimum, out double maximum), Is.False, "Invalid clock case " + index);
                Assert.That(double.IsNaN(minimum), Is.True, "Invalid clock case " + index);
                Assert.That(double.IsNaN(maximum), Is.True, "Invalid clock case " + index);
            }
        }

        [Test]
        public void MarkerConversionUsesDeclaredNanosecondsAndPreservesUnavailableValues()
        {
            Assert.That(ReleaseCpuDiagnosticSession.MarkerMilliseconds(1500000L, "TimeNanoseconds"), Is.EqualTo(1.5));
            Assert.That(ReleaseCpuDiagnosticSession.MarkerMilliseconds(0L, "TimeNanoseconds"), Is.Zero);
            Assert.That(double.IsNaN(ReleaseCpuDiagnosticSession.MarkerMilliseconds(-1L, "TimeNanoseconds")), Is.True);
            Assert.That(double.IsNaN(ReleaseCpuDiagnosticSession.MarkerMilliseconds(1500000L, "Count")), Is.True);
            Assert.That(double.IsNaN(ReleaseCpuDiagnosticSession.MarkerMilliseconds(1500000L, "Bytes")), Is.True);
            Assert.That(double.IsNaN(ReleaseCpuDiagnosticSession.MarkerMilliseconds(1500000L, null)), Is.True);
        }

        [Test]
        public void CpuDiagnosticClassificationCannotEnterCleanFpsComparison()
        {
            var controllerType = typeof(CleanReleaseRouteController);
            Type resultType = controllerType.GetNestedType("Result", BindingFlags.NonPublic);
            object result = Activator.CreateInstance(resultType);
            MethodInfo classify = controllerType.GetMethod("ClassifyMeasurement", BindingFlags.Static | BindingFlags.NonPublic);
            classify.Invoke(null, new[] { result, (object)false, false, false, new[] { "-clean-route", "-location-batching", "U", "-benchmark-cpu-diagnostic" } });
            Assert.That(resultType.GetField("measurementClass").GetValue(result), Is.EqualTo("release-cpu-diagnostic"));
            Assert.That(resultType.GetField("instrumented").GetValue(result), Is.True);
            Assert.That(resultType.GetField("eligibleForFpsComparison").GetValue(result), Is.False);
        }

        [Test]
        public void AbortBeforeCpuSessionCreationIsIdempotentAndLeavesNoSession()
        {
            var root = new GameObject("Release CPU diagnostic lifecycle fixture");
            try
            {
                var controller = root.AddComponent<CleanReleaseRouteController>();
                controller.ConfigureProfiles(root.AddComponent<Camera>(), Array.Empty<BenchmarkScenario>(), "test", null);
                FieldInfo session = typeof(CleanReleaseRouteController).GetField("_cpuSession", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(session, Is.Not.Null);
                Assert.That(session.GetValue(controller), Is.Null);
                controller.Abort();
                controller.Abort();
                Assert.That(session.GetValue(controller), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void StartupFocusRequiresTheFullContinuousSettleInterval()
        {
            long since = -1;
            Assert.That(FocusReady(true, 1000, 1000, ref since), Is.False);
            Assert.That(FocusReady(true, 1499, 1000, ref since), Is.False);
            Assert.That(FocusReady(true, 1500, 1000, ref since), Is.True);
            Assert.That(since, Is.EqualTo(1000));
            Assert.That(FocusReady(false, 1600, 1000, ref since), Is.False);
            Assert.That(since, Is.EqualTo(-1));
            Assert.That(FocusReady(true, 2000, 1000, ref since), Is.False);
            Assert.That(FocusReady(true, 2499, 1000, ref since), Is.False);
            Assert.That(FocusReady(true, 2500, 1000, ref since), Is.True);
        }

        [Test]
        public void InvalidOrRegressingStartupClocksRestartTheFocusInterval()
        {
            long since = 1000;
            Assert.That(FocusReady(true, -1, 1000, ref since), Is.False);
            Assert.That(since, Is.EqualTo(-1));
            Assert.That(FocusReady(true, 1000, 0, ref since), Is.False);
            Assert.That(since, Is.EqualTo(-1));
            Assert.That(FocusReady(true, 1000, 1000, ref since), Is.False);
            Assert.That(FocusReady(true, 500, 1000, ref since), Is.False);
            Assert.That(since, Is.EqualTo(500));
            Assert.That(FocusReady(true, 999, 1000, ref since), Is.False);
            Assert.That(FocusReady(true, 1000, 1000, ref since), Is.True);
        }

        [Test]
        public void StartupFocusCallbackResetsSettlingAndStartedRouteStillRecordsViolation()
        {
            var root = new GameObject("Startup focus lifecycle fixture");
            try
            {
                var controller = root.AddComponent<CleanReleaseRouteController>();
                controller.ConfigureProfiles(root.AddComponent<Camera>(), Array.Empty<BenchmarkScenario>(), "test", null);
                Type type = typeof(CleanReleaseRouteController);
                BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetField("_running", fields).SetValue(controller, true);
                type.GetField("_startupFocusSince", fields).SetValue(controller, 1000L);
                type.GetMethod("OnApplicationFocus", fields).Invoke(controller, new object[] { false });
                Assert.That(type.GetField("_startupFocusSince", fields).GetValue(controller), Is.EqualTo(-1L));
                Assert.That(type.GetField("_focusViolation", fields).GetValue(controller), Is.False);
                Assert.That(type.GetField("_focusLostFrame", fields).GetValue(controller), Is.EqualTo(-1));
                type.GetField("_started", fields).SetValue(controller, true);
                type.GetMethod("OnApplicationFocus", fields).Invoke(controller, new object[] { false });
                Assert.That(type.GetField("_focusViolation", fields).GetValue(controller), Is.True);
                Assert.That((int)type.GetField("_focusLostFrame", fields).GetValue(controller), Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static bool FocusReady(bool focused, long timestamp, long frequency, ref long since)
        {
            MethodInfo method = typeof(CleanReleaseRouteController).GetMethod("StartupFocusSettled", BindingFlags.Static | BindingFlags.NonPublic);
            object[] arguments = { focused, timestamp, frequency, since };
            bool result = (bool)method.Invoke(null, arguments);
            since = (long)arguments[3];
            return result;
        }
    }
}
