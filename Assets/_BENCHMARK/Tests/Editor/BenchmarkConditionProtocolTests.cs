using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Rvm.Benchmark.Tests
{
    public sealed class BenchmarkConditionProtocolTests
    {
        [Test]
        public void IndependentProtocolRunsRemainDistinctFromIdentityAndUnverified()
        {
            var first = Metadata("A");
            var second = Metadata("B");
            var a = Summary("A");
            var b = Summary("B");
            var comparison = BenchmarkComparison.Compare(first, a, second, b);
            Assert.That(comparison.comparabilityStatus, Is.EqualTo("ConfirmedProtocol"));
            Assert.That(comparison.comparisonKind, Is.EqualTo("IndependentRuns"));
            Assert.That(comparison.comparable, Is.True);
            Assert.That(comparison.unverifiedConditions, Is.Not.Empty);
            Assert.That(BenchmarkComparison.Compare(first, a, first, a).comparabilityStatus, Is.EqualTo("Identity"));
            second.conditionEvidence = null;
            Assert.That(BenchmarkComparison.Compare(first, a, second, b).comparabilityStatus, Is.EqualTo("Conditional"));
        }

        [TestCase("API")]
        [TestCase("Resolution")]
        [TestCase("Mode")]
        [TestCase("Pacing")]
        [TestCase("Instrumentation")]
        [TestCase("Errors")]
        [TestCase("Abort")]
        public void IntentionalMismatchesAreIncompatible(string change)
        {
            var first = Metadata("A");
            var second = Metadata("B");
            var a = Summary("A");
            var b = Summary("B");
            switch (change)
            {
                case "API": second.graphicsApi = "Vulkan"; break;
                case "Resolution": second.width = 800; break;
                case "Mode": second.mode = "Diagnostic"; break;
                case "Pacing": second.vsync = 1; break;
                case "Instrumentation": second.instrumentationSignature = "other"; break;
                case "Errors": second.runtimeErrors = 1; break;
                case "Abort": b.status = "aborted"; break;
            }
            Assert.That(BenchmarkComparison.Compare(first, a, second, b).comparabilityStatus, Is.EqualTo("Incompatible"));
        }

        [Test]
        public void AttestationRejectsWrongBuildUnreviewedTypesAndUnboundRun()
        {
            var metadata = Metadata("A");
            metadata.buildSourceDigest = "different";
            Assert.That(BenchmarkConditionProtocol.Validate(metadata, out _), Is.False);
            metadata.buildSourceDigest = Hash('1');
            metadata.environment.Find(value => value.key == "inventory.behaviourTypes").value = "Unknown.AI";
            Assert.That(BenchmarkConditionProtocol.Validate(metadata, out _), Is.False);
            metadata.environment.Find(value => value.key == "inventory.behaviourTypes").value = "Fixture.Type";
            metadata.runId = "unbound";
            Assert.That(BenchmarkConditionProtocol.Validate(metadata, out _), Is.False);
        }

        [Test]
        public void InvalidOrInsufficientMetricSamplesNeverProduceAutomaticClassification()
        {
            var a = Summary("A");
            var b = Summary("B");
            foreach (var window in b.windows)
            {
                window.frames.frameTime.samples = 1;
                window.metrics.Add(new BenchmarkMetricStats { id = "invalid", unit = "ms", values = new BenchmarkDistribution { available = false, mean = 1180747847 } });
            }
            var result = BenchmarkComparison.Compare(Metadata("A"), a, Metadata("B"), b);
            Assert.That(result.deltas.FindAll(value => value.metric == "average_fps" || value.metric == "p95").TrueForAll(value => !value.available), Is.True);
        }

        [TestCase("", true)]
        [TestCase("none", true)]
        [TestCase(null, false)]
        [TestCase("storm", false)]
        public void ActualInactiveFxEncodingIsAcceptedWithoutTreatingMissingOrActiveFxAsInactive(string activeFx, bool expected)
        {
            var metadata = Metadata("A");
            metadata.environment.Find(value => value.key == "weather.0.ActiveFx").value = activeFx;
            Assert.That(BenchmarkConditionProtocol.Validate(metadata, out _), Is.EqualTo(expected));
        }

        [TestCase("Missing")]
        [TestCase("Mismatch")]
        [TestCase("InputHash")]
        [TestCase("Source")]
        [TestCase("Settings")]
        [TestCase("Version")]
        public void UnverifiedOriginRejectsEvenMatchingStaleMetadataAndAttestation(string change)
        {
            var first = Metadata("A");
            var second = Metadata("B");
            switch (change)
            {
                case "Missing": second.buildIdentity = null; break;
                case "Mismatch": second.buildIdentity.status = "Mismatch"; break;
                case "InputHash": second.buildIdentity.inputManifestSha256 = Hash('8'); break;
                case "Source":
                    second.buildSourceDigest = Hash('7');
                    var evidence = JsonUtility.FromJson<BenchmarkConditionEvidence>(second.conditionEvidence);
                    evidence.sourceDigest = second.buildSourceDigest;
                    second.conditionEvidence = JsonUtility.ToJson(evidence);
                    break;
                case "Settings": second.renderSettingsDigest = Hash('7'); break;
                case "Version": second.unity = "different"; break;
            }
            var result = BenchmarkComparison.Compare(first, Summary("A"), second, Summary("B"));
            Assert.That(result.comparabilityStatus, Is.EqualTo("UnverifiedOrigin"));
            Assert.That(result.originVerified, Is.False);
            Assert.That(result.comparable, Is.False);
            Assert.That(BenchmarkConditionProtocol.Validate(second, out _), Is.False);
            Assert.That(BenchmarkComparison.IsBaselineEligible(second, Summary("B"), out _), Is.False);
        }

        [TestCase("Build")]
        [TestCase("Inputs")]
        [TestCase("Artifacts")]
        [TestCase("Application")]
        [TestCase("Executable")]
        [TestCase("Legacy")]
        public void AttestationMustMatchActualBuildAndBinaryEvidence(string change)
        {
            var metadata = Metadata("A");
            var evidence = JsonUtility.FromJson<BenchmarkConditionEvidence>(metadata.conditionEvidence);
            switch (change)
            {
                case "Build": evidence.buildId = "00000000-0000-0000-0000-000000000002"; break;
                case "Inputs": evidence.inputManifestSha256 = Hash('7'); break;
                case "Artifacts": evidence.artifactsManifestSha256 = Hash('7'); break;
                case "Application": evidence.applicationBuildGuid = "other"; break;
                case "Executable": evidence.executableSha256 = Hash('7'); break;
                case "Legacy": evidence.version = 1; break;
            }
            metadata.conditionEvidence = JsonUtility.ToJson(evidence);
            Assert.That(BenchmarkConditionProtocol.ValidateOrigin(metadata, out _), Is.True);
            Assert.That(BenchmarkConditionProtocol.Validate(metadata, out _), Is.False);
            var result = BenchmarkComparison.Compare(metadata, Summary("A"), Metadata("B"), Summary("B"));
            Assert.That(result.comparabilityStatus, Is.EqualTo("Conditional"));
            Assert.That(result.comparable, Is.False);
        }

        [Test]
        public void LegacySelfIdentityNeverEstablishesActualPlayerOrigin()
        {
            var legacy = JsonUtility.FromJson<BenchmarkMetadata>("{\"schemaVersion\":1,\"runId\":\"A\",\"metadataVersion\":3,\"measurementVersion\":3}");
            legacy.positions = Metadata("A").positions;
            var result = BenchmarkComparison.Compare(legacy, Summary("A"), legacy, Summary("A"));
            Assert.That(result.comparisonKind, Is.EqualTo("IdentityValidation"));
            Assert.That(result.comparabilityStatus, Is.EqualTo("UnverifiedOrigin"));
            Assert.That(result.comparable, Is.False);
            Assert.That(result.deltas.Exists(value => value.available && value.classification.Contains("origin unverified")), Is.True);
            Assert.That(BenchmarkComparison.IsBaselineEligible(legacy, Summary("A"), out _), Is.False);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void MissingWorkingCameraRenderingCannotEstablishBaselineOrConfirmedComparison(int renderedFrames)
        {
            var first = Metadata("A");
            var second = Metadata("B");
            second.renderedMeasurementFrames = renderedFrames;
            Assert.That(BenchmarkConditionProtocol.ValidateOrigin(second, out _), Is.True);
            Assert.That(BenchmarkConditionProtocol.Validate(second, out _), Is.False);
            Assert.That(BenchmarkComparison.IsBaselineEligible(second, Summary("B"), out _), Is.False);
            var independent = BenchmarkComparison.Compare(first, Summary("A"), second, Summary("B"));
            Assert.That(independent.originVerified, Is.True);
            Assert.That(independent.renderingVerified, Is.False);
            Assert.That(independent.comparabilityStatus, Is.EqualTo("UnverifiedRendering"));
            Assert.That(independent.comparable, Is.False);
            var self = BenchmarkComparison.Compare(second, Summary("B"), second, Summary("B"));
            Assert.That(self.comparabilityStatus, Is.EqualTo("UnverifiedRendering"));
            Assert.That(self.comparable, Is.False);
        }

        [TestCase("Origin")]
        [TestCase("Binary")]
        [TestCase("Protocol")]
        [TestCase("Errors")]
        [TestCase("Duplicate")]
        [TestCase("Order")]
        [TestCase("Trace")]
        [TestCase("Rendering")]
        public void AcceptedSeriesRequiresThreeConsecutiveCleanRunsOfOneActualBuild(string change)
        {
            var metadata = new List<BenchmarkMetadata> { Metadata("A"), Metadata("B"), Metadata("C") };
            var summaries = new List<BenchmarkSummary> { Summary("A"), Summary("B"), Summary("C") };
            for (int i = 0; i < metadata.Count; i++)
            {
                metadata[i].seriesId = "series";
                metadata[i].repetition = i + 1;
            }
            Assert.That(BenchmarkComparison.ValidateSeries("series", metadata, summaries).acceptedBaseline, Is.True);
            switch (change)
            {
                case "Origin": metadata[1].buildIdentity = null; break;
                case "Binary": metadata[1].buildIdentity.executableSha256 = Hash('7'); break;
                case "Protocol": metadata[1].instrumentationSignature += "/different"; break;
                case "Errors": metadata[1].runtimeErrors = 1; break;
                case "Duplicate": metadata[1].runId = "A"; summaries[1].runId = "A"; break;
                case "Order": metadata[1].repetition = 3; break;
                case "Trace": metadata[1].shadowTrace = true; break;
                case "Rendering": metadata[1].renderedMeasurementFrames = 0; break;
            }
            var result = BenchmarkComparison.ValidateSeries("series", metadata, summaries);
            Assert.That(result.acceptedBaseline, Is.False);
            Assert.That(result.validationStatus, Is.EqualTo("Rejected"));
            Assert.That(result.rejectionReasons, Is.Not.Empty);
        }

        [TestCase("Capture")]
        [TestCase("Profiler")]
        [TestCase("Abort")]
        [TestCase("Windows")]
        [TestCase("Samples")]
        public void IndividualBaselineAcceptanceRejectsHeavyOrIncompleteRuns(string change)
        {
            var metadata = Metadata("A");
            var summary = Summary("A");
            Assert.That(BenchmarkComparison.IsBaselineEligible(metadata, summary, out _), Is.True);
            switch (change)
            {
                case "Capture": metadata.profilerCapture = true; break;
                case "Profiler": metadata.profilerWasEnabled = true; break;
                case "Abort": summary.status = "aborted"; break;
                case "Windows": summary.windows[1].position = summary.windows[3].position; break;
                case "Samples": summary.windows[0].frames.frameTime.samples = 0; break;
            }
            Assert.That(BenchmarkComparison.IsBaselineEligible(metadata, summary, out _), Is.False);
        }

        private static string Hash(char value)
        {
            return new string(value, 64);
        }

        private static BenchmarkMetadata Metadata(string id)
        {
            var stamp = new BenchmarkBuildStamp { buildId = "00000000-0000-0000-0000-000000000001", sourceDigest = Hash('1'), renderSettingsDigest = Hash('2'), inputManifestSha256 = Hash('3'), gitSha = "fixture", gitDirty = "False", unity = "6000.6.7f1", hdrp = "17.6.0" };
            var identity = new BenchmarkBuildIdentityEvidence { status = "Verified", stamp = stamp, stampSha256 = Hash('4'), inputManifestSha256 = Hash('3'), artifactsManifestSha256 = Hash('5'), applicationBuildGuid = "00000000000000000000000000000001", executableSha256 = Hash('6'), checkedArtifacts = 3 };
            var evidence = new BenchmarkConditionEvidence { protocolId = "fixture-v2", sourceDigest = stamp.sourceDigest, buildId = stamp.buildId, inputManifestSha256 = identity.inputManifestSha256, artifactsManifestSha256 = identity.artifactsManifestSha256, applicationBuildGuid = identity.applicationBuildGuid, executableSha256 = identity.executableSha256, scene = "fixture", weatherProfile = "w_foggy3", gameSeconds = 43200, reviewedBehaviourTypes = new[] { "Fixture.Type" }, reviewSummary = "Reviewed fixture participants; no NPC registry inferred.", scope = "Fixture configuration only; random/physics not deterministic.", runIds = new[] { "A", "B", "C" } };
            var entries = new List<BenchmarkEntry>
            {
                new BenchmarkEntry { key = "inventory.behaviourTypes", value = "Fixture.Type" },
                new BenchmarkEntry { key = "npc.activeNavMeshAgents", value = "0" },
                new BenchmarkEntry { key = "npc.otherAI", value = "unavailable: no registry" },
                new BenchmarkEntry { key = "weather.0.ActiveProfile", value = "w_foggy3" },
                new BenchmarkEntry { key = "weather.0.Paused", value = "True" },
                new BenchmarkEntry { key = "weather.0.IsTransitioning", value = "False" },
                new BenchmarkEntry { key = "weather.0.ActiveFx", value = "none" },
                new BenchmarkEntry { key = "weather.0.GameTimeSeconds", value = "43200" }
            };
            return new BenchmarkMetadata { runId = id, metadataVersion = 5, measurementVersion = 3, buildIdentity = identity, renderedMeasurementFrames = 40, buildSourceDigest = stamp.sourceDigest, renderSettingsDigest = stamp.renderSettingsDigest, gitSha = stamp.gitSha, gitDirty = stamp.gitDirty, unity = stamp.unity, hdrp = stamp.hdrp, execution = "Development Player", mode = "Standard", conditionProtocol = "fixture-v2", conditionEvidence = JsonUtility.ToJson(evidence), scene = "fixture", quality = "High Fidelity", finalQuality = "High Fidelity", targetFrameRate = -1, renderFrameInterval = 1, finalFramePacing = "0/-1/1", positions = new[] { new BenchmarkPose(), new BenchmarkPose(), new BenchmarkPose(), new BenchmarkPose() }, instrumentationSignature = "v21", timerSignature = "qpc", environment = entries, endEnvironment = entries };
        }

        private static BenchmarkSummary Summary(string id)
        {
            var result = new BenchmarkSummary { runId = id, status = "completed" };
            var values = new double[40];
            Array.Fill(values, 20);
            for (int i = -1; i < 4; i++)
            {
                result.windows.Add(new BenchmarkWindow { position = i, window = "Full", frames = BenchmarkStatistics.Frames(values) });
                result.windows.Add(new BenchmarkWindow { position = i, window = "Stable", frames = BenchmarkStatistics.Frames(values) });
            }
            return result;
        }
    }
}
