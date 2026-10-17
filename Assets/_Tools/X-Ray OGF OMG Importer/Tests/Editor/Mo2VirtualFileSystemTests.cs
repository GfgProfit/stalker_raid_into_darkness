using System.IO;
using System.Text;
using NUnit.Framework;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class Mo2VirtualFileSystemTests
    {
        private const string TEXTURE = "textures/wpn/aks74/body_diff.dds";

        [Test]
        public void HigherPriorityModWinsAndLosersAreListed()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                XRayAssetIndex index = Build(fixture, "Main");
                Assert.IsTrue(index.TryGet(TEXTURE, out XRayIndexedFile file));
                Assert.AreEqual("HighMod", index.GetSource(file.Winner).Name);
                Assert.AreEqual(3, file.Candidates.Count);
                Assert.AreEqual("LowMod", index.GetSource(file.Candidates[1]).Name);
                Assert.AreEqual(XRayAssetSourceKind.BaseAnomaly, index.GetSource(file.Candidates[2]).Kind);
                Assert.AreEqual("winner", File.ReadAllText(index.GetFullPath(file.Winner)));
                Assert.GreaterOrEqual(index.Statistics.ConflictCount, 1);
                Assert.AreEqual(2, index.Statistics.ActiveModCount);
            }
        }

        [Test]
        public void DisabledModsAreIgnored()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                XRayAssetIndex index = Build(fixture, "Main");
                Assert.IsFalse(index.TryGet("textures/disabled_only.dds", out _));
                index.TryGet(TEXTURE, out XRayIndexedFile file);
                foreach (XRayIndexCandidate candidate in file.Candidates)
                {
                    Assert.AreNotEqual("DisabledMod", index.GetSource(candidate).Name);
                }
            }
        }

        [Test]
        public void OverwriteBeatsEveryMod()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                fixture.AddOverwriteFile(TEXTURE, Encoding.ASCII.GetBytes("overwrite"));
                XRayAssetIndex index = Build(fixture, "Main");
                index.TryGet(TEXTURE, out XRayIndexedFile file);
                Assert.AreEqual(XRayAssetSourceKind.Overwrite, index.GetSource(file.Winner).Kind);
                Assert.AreEqual(4, file.Candidates.Count);
            }
        }

        [Test]
        public void BaseAnomalyIsUsedWhenNoModProvidesTheFile()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                fixture.AddBaseFile("textures/wpn/base_only.dds", Encoding.ASCII.GetBytes("base"));
                XRayAssetIndex index = Build(fixture, "Main");
                Assert.IsTrue(index.TryGet("textures/wpn/base_only.dds", out XRayIndexedFile file));
                Assert.AreEqual(XRayAssetSourceKind.BaseAnomaly, index.GetSource(file.Winner).Kind);
                Assert.IsFalse(file.HasConflict);
            }
        }

        [Test]
        public void LookupIsCaseAndSeparatorInsensitive()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                fixture.AddModFile("LowMod", "Textures/WPN/MixedCase_Diff.DDS", Encoding.ASCII.GetBytes("mixed"));
                XRayAssetIndex index = Build(fixture, "Main");
                Assert.IsTrue(index.TryGet("textures\\wpn\\mixedcase_diff.dds", out XRayIndexedFile file));
                StringAssert.EndsWith("MixedCase_Diff.DDS", file.Winner.RelativePath);
                var resolver = new XRayTextureResolver(index);
                Assert.IsTrue(resolver.Resolve("WPN\\mixedcase_diff").Diffuse.IsResolved);
            }
        }

        [Test]
        public void ModRootFoldersOutsideGamedataAreIgnoredForAnomalyLayout()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                fixture.AddModRootFile("LowMod", "textures/root_layout.dds", Encoding.ASCII.GetBytes("root"));
                fixture.AddModRootFile("RootOnlyMod", "textures/root_only.dds", Encoding.ASCII.GetBytes("root"));
                fixture.AddProfile("Root", "+RootOnlyMod", "+HighMod");
                XRayAssetIndex index = Build(fixture, "Root");
                Assert.IsFalse(index.TryGet("textures/root_only.dds", out _));
                XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(fixture.CreateConfiguration("Root"));
                Assert.AreEqual(1, plan.ModsWithoutGamedataCount);
                XRaySourceConfiguration configuration = fixture.CreateConfiguration("Root");
                configuration.TreatModRootAsGamedata = true;
                Assert.IsTrue(XRayAssetIndex.Build(Mo2VirtualFileSystem.Plan(configuration)).TryGet("textures/root_only.dds", out _));
            }
        }

        [Test]
        public void HiddenFilesAreSkipped()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                fixture.AddModFile("HighMod", "textures/hidden.dds.mohidden", Encoding.ASCII.GetBytes("hidden"));
                XRayAssetIndex index = Build(fixture, "Main");
                Assert.IsFalse(index.TryGet("textures/hidden.dds.mohidden", out _));
                Assert.IsFalse(index.TryGet("textures/hidden.dds", out _));
            }
        }

        [Test]
        public void ProfileChangeChangesWinnerAndFingerprint()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                fixture.AddProfile("Reversed", "+LowMod", "+HighMod");
                XRayAssetIndex main = Build(fixture, "Main");
                XRayAssetIndex reversed = Build(fixture, "Reversed");
                Assert.AreNotEqual(main.Fingerprint, reversed.Fingerprint);
                reversed.TryGet(TEXTURE, out XRayIndexedFile file);
                Assert.AreEqual("LowMod", reversed.GetSource(file.Winner).Name);
            }
        }

        [Test]
        public void ModListEditChangesFingerprint()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                string before = Mo2VirtualFileSystem.Plan(fixture.CreateConfiguration("Main")).Fingerprint;
                fixture.AddProfile("Main", "+HighMod", "+LowMod", "+DisabledMod");
                File.SetLastWriteTimeUtc(Path.Combine(fixture.Mo2Root, "profiles/Main/modlist.txt"), System.DateTime.UtcNow.AddMinutes(1));
                Assert.AreNotEqual(before, Mo2VirtualFileSystem.Plan(fixture.CreateConfiguration("Main")).Fingerprint);
            }
        }

        [Test]
        public void ActiveProfileFromIniIsUsedWhenNoneSelected()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(fixture.CreateConfiguration(null));
                Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
                Assert.AreEqual("Main", plan.ProfileName);
            }
        }

        [Test]
        public void MissingProfileIsReported()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(fixture.CreateConfiguration("DoesNotExist"));
                Assert.IsFalse(plan.IsValid);
                StringAssert.Contains("DoesNotExist", plan.Errors[0]);
            }
        }

        [Test]
        public void IndexRoundTripsThroughCacheFile()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                XRayAssetIndex index = Build(fixture, "Main");
                string cache = directory.Combine("cache/index.bin");
                index.Save(cache);
                XRayAssetIndex loaded = XRayAssetIndex.Load(cache);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(index.Fingerprint, loaded.Fingerprint);
                Assert.AreEqual(index.Count, loaded.Count);
                loaded.TryGet(TEXTURE, out XRayIndexedFile file);
                Assert.AreEqual("HighMod", loaded.GetSource(file.Winner).Name);
                Assert.AreEqual(index.Statistics.ConflictCount, loaded.Statistics.ConflictCount);
            }
        }

        [Test]
        public void ArchivesInModsAreReported()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateConflictFixture(directory);
                fixture.AddModRootFile("LowMod", "db/mods/textures_pack.db", new byte[] { 1 });
                XRayAssetIndex index = Build(fixture, "Main");
                Assert.AreEqual(1, index.ArchiveSources.Count);
                XRayTextureResolution resolution = new XRayTextureResolver(index).Resolve("wpn\\not_there");
                Assert.IsFalse(resolution.Diffuse.IsResolved);
                Assert.IsTrue(resolution.Notes.Exists(note => note.Contains("archives")));
            }
        }

        public static Mo2Fixture CreateConflictFixture(TestDirectory directory)
        {
            var fixture = new Mo2Fixture(directory);
            fixture.WriteIni("Main");
            fixture.AddProfile("Main", "+HighMod", "-DisabledMod", "+---Fixes---_separator", "+LowMod");
            fixture.AddModFile("HighMod", TEXTURE, Encoding.ASCII.GetBytes("winner"));
            fixture.AddModFile("LowMod", TEXTURE, Encoding.ASCII.GetBytes("loser"));
            fixture.AddModFile("DisabledMod", TEXTURE, Encoding.ASCII.GetBytes("disabled"));
            fixture.AddModFile("DisabledMod", "textures/disabled_only.dds", Encoding.ASCII.GetBytes("disabled"));
            fixture.AddBaseFile(TEXTURE, Encoding.ASCII.GetBytes("base"));
            return fixture;
        }

        private static XRayAssetIndex Build(Mo2Fixture fixture, string profile)
        {
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(fixture.CreateConfiguration(profile));
            Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
            return XRayAssetIndex.Build(plan);
        }
    }
}
