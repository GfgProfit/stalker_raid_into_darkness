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
    public sealed class BenchmarkRecorderSemanticsTests
    {
        [TestCase(BenchmarkSampleKind.InclusiveTime, 1, 2000000000L, 20, BenchmarkSampleStatus.Valid)]
        [TestCase(BenchmarkSampleKind.InclusiveTime, 2, 100L, 2, BenchmarkSampleStatus.AmbiguousInterval)]
        [TestCase(BenchmarkSampleKind.InstantaneousGauge, 0, 100L, 1, BenchmarkSampleStatus.NoPublication)]
        [TestCase(BenchmarkSampleKind.InstantaneousGauge, 2, 200L, 1, BenchmarkSampleStatus.Valid)]
        [TestCase(BenchmarkSampleKind.InstantaneousGauge, 1, 0L, 1, BenchmarkSampleStatus.Valid)]
        [TestCase(BenchmarkSampleKind.PublishedFrameTotal, 2, 100L, 50, BenchmarkSampleStatus.Valid)]
        [TestCase(BenchmarkSampleKind.EventSum, 1, 300L, 2, BenchmarkSampleStatus.Valid)]
        public void SemanticsDoNotDivideByCallsOrApplyFrameDurationCap(BenchmarkSampleKind kind, int publications, long value, long calls, BenchmarkSampleStatus expected)
        {
            Assert.That(BenchmarkRecorderSource.Evaluate(kind, true, false, false, publications, value, calls), Is.EqualTo(expected));
        }

        [Test]
        public void UnsupportedAmbiguousAndQuarantinedValuesRemainInvalid()
        {
            Assert.That(BenchmarkRecorderSource.Evaluate(BenchmarkSampleKind.InclusiveTime, true, false, true, 1, 1180747847000000, 10), Is.EqualTo(BenchmarkSampleStatus.Quarantined));
            Assert.That(BenchmarkRecorderSource.Evaluate(BenchmarkSampleKind.InclusiveTime, true, true, false, 1, 100, 1), Is.EqualTo(BenchmarkSampleStatus.AmbiguousIdentity));
            Assert.That(BenchmarkRecorderSource.Evaluate(BenchmarkSampleKind.InstantaneousGauge, false, false, false, 1, 100, 1), Is.EqualTo(BenchmarkSampleStatus.UnsupportedType));
        }

        [Test]
        public void RawEvidenceSurvivesJsonWithoutSerializingInvalidFloatingPointValues()
        {
            var source = new BenchmarkCollectorEvidenceSource { id = "raw", identity = new BenchmarkSourceIdentity { key = "Jobs/WaitForCompleted" }, rawEvidence = new System.Collections.Generic.List<BenchmarkSampleRead> { new BenchmarkSampleRead { status = BenchmarkSampleStatus.Quarantined, firstRaw = 1180747847000000, lastRaw = 1180747847000000, calls = 10, publications = 1, sourceUnityFrame = -1, Value = double.NaN } } };
            string json = JsonUtility.ToJson(source);
            var restored = JsonUtility.FromJson<BenchmarkCollectorEvidenceSource>(json);
            Assert.That(restored.rawEvidence.Count, Is.EqualTo(1));
            Assert.That(restored.rawEvidence[0].lastRaw, Is.EqualTo(1180747847000000));
            Assert.That(restored.rawEvidence[0].status, Is.EqualTo(BenchmarkSampleStatus.Quarantined));
            Assert.That(restored.rawEvidence[0].sourceUnityFrame, Is.EqualTo(-1));
            Assert.That(json.Contains("NaN"), Is.False);
        }

    }
}
