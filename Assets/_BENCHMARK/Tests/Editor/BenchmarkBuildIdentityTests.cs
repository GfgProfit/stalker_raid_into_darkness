using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rvm.Benchmark.Editor;

namespace Rvm.Benchmark.Tests
{
    public sealed class BenchmarkBuildIdentityTests
    {
        public const int VALIDATION_INPUT = 3;

        private const string APPLICATION_GUID = "2392b13e16834a55b1aee309d94dc3b7";
        private const string EXECUTABLE = "RvmBenchmark.exe";

        [Test]
        public void MatchingEmbeddedInputAndActualBinariesProduceVerifiedProof()
        {
            var stamp = Stamp();
            var artifacts = Artifacts();
            var result = Verify(stamp, Input(stamp), Manifest(stamp, artifacts), artifacts);
            result.artifactsManifestSha256 = Hash('a');
            Assert.That(result.status, Is.EqualTo("Verified"));
            Assert.That(result.checkedArtifacts, Is.EqualTo(3));
            Assert.That(BenchmarkBuildIdentity.IsVerified(result, stamp.sourceDigest, stamp.renderSettingsDigest), Is.True);
            Assert.That(BenchmarkBuildIdentity.IsVerified(result, Hash('f'), stamp.renderSettingsDigest), Is.False);
        }

        [TestCase("buildId")]
        [TestCase("sourceDigest")]
        [TestCase("inputHash")]
        [TestCase("stampHash")]
        [TestCase("applicationGuid")]
        [TestCase("inputFields")]
        [TestCase("binary")]
        [TestCase("missingBinary")]
        [TestCase("extraBinary")]
        public void StaleOrModifiedIdentityCannotBeAccepted(string change)
        {
            var stamp = Stamp();
            var input = Input(stamp);
            var expected = Artifacts();
            var actual = Artifacts();
            var manifest = Manifest(stamp, expected);
            switch (change)
            {
                case "buildId": manifest.buildId = Guid.NewGuid().ToString("N"); break;
                case "sourceDigest": manifest.sourceDigest = Hash('f'); break;
                case "inputHash": manifest.inputManifestSha256 = Hash('f'); break;
                case "stampHash": manifest.stampSha256 = Hash('f'); break;
                case "applicationGuid": manifest.applicationBuildGuid = Guid.NewGuid().ToString("N"); break;
                case "inputFields": input.sourceDigest = Hash('f'); break;
                case "binary": actual[0].sha256 = Hash('f'); break;
                case "missingBinary": actual.RemoveAt(0); break;
                case "extraBinary": actual.Add(new BenchmarkBuildArtifact { path = "RvmBenchmark_Data/extra.dll", sha256 = Hash('e') }); break;
            }
            var result = Verify(stamp, input, manifest, actual);
            Assert.That(result.status, Is.EqualTo("Mismatch"));
            Assert.That(result.mismatches, Is.Not.Empty);
            Assert.That(BenchmarkBuildIdentity.IsVerified(result), Is.False);
        }

        [Test]
        public void AnOldEmbeddedStampIsNotReplacedByExpectedManifestIdentity()
        {
            var oldStamp = Stamp();
            var newStamp = Stamp();
            newStamp.buildId = Guid.NewGuid().ToString("N");
            newStamp.sourceDigest = Hash('e');
            var result = Verify(oldStamp, Input(newStamp), Manifest(newStamp, Artifacts()), Artifacts());
            Assert.That(result.stamp.buildId, Is.EqualTo(oldStamp.buildId));
            Assert.That(result.stamp.sourceDigest, Is.EqualTo(oldStamp.sourceDigest));
            Assert.That(result.status, Is.EqualTo("Mismatch"));
        }

        [Test]
        public void AClaimedVerifiedStatusWithoutIndependentHashesIsUnverified()
        {
            Assert.That(BenchmarkBuildIdentity.IsVerified(null), Is.False);
            Assert.That(BenchmarkBuildIdentity.IsVerified(new BenchmarkBuildIdentityEvidence { status = "Verified", stamp = Stamp() }), Is.False);
        }

        [TestCase("../outside.exe")]
        [TestCase("RvmBenchmark_Data/../../outside.exe")]
        [TestCase("C:/outside.exe")]
        [TestCase("folder\\outside.exe")]
        public void ArtifactPathsCannotEscapeThePlayerDirectory(string relative)
        {
            Assert.Throws<ArgumentException>(() => BenchmarkBuildIdentity.ResolveArtifact(Path.GetTempPath(), relative));
        }

        [Test]
        public void GeneratedStampAndMetaAreExcludedWithoutExcludingProductionSources()
        {
            Assert.That(BenchmarkBuildProvenance.IsGeneratedPath(BenchmarkBuildProvenance.STAMP_PATH), Is.True);
            Assert.That(BenchmarkBuildProvenance.IsGeneratedPath(BenchmarkBuildProvenance.STAMP_PATH + ".meta"), Is.True);
            Assert.That(BenchmarkBuildProvenance.IsGeneratedPath(BenchmarkBuildProvenance.STAMP_FOLDER), Is.True);
            Assert.That(BenchmarkBuildProvenance.IsGeneratedPath("Assets/_BENCHMARK/Runtime/BenchmarkBuildIdentity.cs"), Is.False);
        }

        [Test]
        public void RegisteredUpmSourceIsHashedThroughItsActualPathAndRetainsItsVirtualKey()
        {
            const string VIRTUAL_PATH = "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowAtlas.cs";
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(VIRTUAL_PATH);
            Assert.That(package, Is.Not.Null);
            string actual = Path.GetFullPath(Path.Combine(package.resolvedPath, "Runtime/Lighting/Shadow/HDShadowAtlas.cs"));
            Assert.That(File.Exists(actual), Is.True);
            var entry = BenchmarkBuildProvenance.ReadInputFile(VIRTUAL_PATH);
            Assert.That(entry.key, Is.EqualTo(VIRTUAL_PATH));
            Assert.That(entry.value, Is.EqualTo(BenchmarkBuildIdentity.HashBytes(File.ReadAllBytes(actual))));
            Assert.That(BenchmarkBuildProvenance.ResolveInputPath(VIRTUAL_PATH), Is.EqualTo(actual));
        }

        [TestCase("Packages/manifest.json")]
        [TestCase("Packages/packages-lock.json")]
        public void ProjectPackageManifestsAreHashedAsLocalInputs(string path)
        {
            string actual = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", path));
            var entry = BenchmarkBuildProvenance.ReadInputFile(path);
            Assert.That(entry.key, Is.EqualTo(path));
            Assert.That(entry.value, Is.EqualTo(BenchmarkBuildIdentity.HashBytes(File.ReadAllBytes(actual))));
            Assert.That(BenchmarkBuildProvenance.ResolveInputPath(path), Is.EqualTo(actual));
        }

        [Test]
        public void MissingRequiredInputFailsRatherThanSilentlyReducingProvenanceCoverage()
        {
            Assert.Throws<FileNotFoundException>(() => BenchmarkBuildProvenance.ReadInputFile("Assets/_BENCHMARK/NoSuchInputForProvenance.cs"));
        }

        private static BenchmarkBuildIdentityEvidence Verify(BenchmarkBuildStamp stamp, BenchmarkBuildInputIdentity input, BenchmarkBuildArtifactManifest manifest, List<BenchmarkBuildArtifact> actual)
        {
            return BenchmarkBuildIdentity.Verify(stamp, Hash('b'), input, Hash('c'), manifest, actual, APPLICATION_GUID, EXECUTABLE);
        }

        private static BenchmarkBuildStamp Stamp()
        {
            return new BenchmarkBuildStamp { buildId = "a330f0c694a84b358fa20e3fe708c856", sourceDigest = Hash('1'), renderSettingsDigest = Hash('2'), inputManifestSha256 = Hash('c'), gitSha = "revision", gitDirty = "True", unity = "6000.6.7f1", hdrp = "17.6.0" };
        }

        private static BenchmarkBuildInputIdentity Input(BenchmarkBuildStamp stamp)
        {
            return new BenchmarkBuildInputIdentity { buildId = stamp.buildId, sourceDigest = stamp.sourceDigest, renderSettingsDigest = stamp.renderSettingsDigest, gitSha = stamp.gitSha, gitDirty = stamp.gitDirty, unity = stamp.unity, hdrp = stamp.hdrp };
        }

        private static BenchmarkBuildArtifactManifest Manifest(BenchmarkBuildStamp stamp, List<BenchmarkBuildArtifact> artifacts)
        {
            return new BenchmarkBuildArtifactManifest { buildId = stamp.buildId, sourceDigest = stamp.sourceDigest, inputManifestSha256 = stamp.inputManifestSha256, stampSha256 = Hash('b'), applicationBuildGuid = APPLICATION_GUID, executable = EXECUTABLE, artifacts = artifacts };
        }

        private static List<BenchmarkBuildArtifact> Artifacts()
        {
            return new List<BenchmarkBuildArtifact> { new BenchmarkBuildArtifact { path = EXECUTABLE, sha256 = Hash('a') }, new BenchmarkBuildArtifact { path = "UnityPlayer.dll", sha256 = Hash('b') }, new BenchmarkBuildArtifact { path = "RvmBenchmark_Data/resources.assets", sha256 = Hash('d') } };
        }

        private static string Hash(char value)
        {
            return new string(value, 64);
        }
    }
}
