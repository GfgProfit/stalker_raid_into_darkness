using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class Mo2EffectiveGamedataTests
    {
        private const string PROFILE = "Main";
        private const string GAME = "game";
        private const string MOD_A = "Mod A";
        private const string MOD_B = "Mod B";
        private const string MOD_DISABLED = "Disabled Mod";
        private const string MOD_LEVELS = "Location Mod";
        private const string SHARED = "textures/terrain/shared.dds";

        [Test]
        public void Crc32AndLiteralLzoMatchReferenceVectors()
        {
            byte[] check = Encoding.ASCII.GetBytes("123456789");
            Assert.AreEqual(0xCBF43926u, XRayCompression.Crc32(check, 0, check.Length));
            byte[] data = Encoding.ASCII.GetBytes("x-ray archive payload");
            byte[] packed = XRayArchiveTestWriter.Lzo(data);
            CollectionAssert.AreEqual(data, XRayCompression.DecompressLzo1x(packed, 0, packed.Length, data.Length));
        }

        [Test]
        public void ArchiveEntriesAreIndexedReadAndMaterialized()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                byte[] packed = Encoding.ASCII.GetBytes("compressed entry");
                WriteGameArchive(directory, "db/textures/textures.db0", Entry("textures/base/a.dds", "base-archive"), new XRayArchiveTestEntry("textures\\base\\packed.dds", packed, true));
                XRayAssetIndex index = Build(fixture, directory);
                var gamedata = new XRayEffectiveGamedata(index);
                Assert.AreEqual("base-archive", Encoding.ASCII.GetString(gamedata.ReadAllBytes("textures/base/a.dds")));
                CollectionAssert.AreEqual(packed, gamedata.ReadAllBytes("textures/base/packed.dds"));
                index.ExtractionFolder = directory.Combine("cache");
                string extracted = gamedata.Materialize("textures/base/a.dds");
                Assert.IsTrue(extracted.StartsWith(directory.Combine("cache"), StringComparison.OrdinalIgnoreCase));
                Assert.AreEqual("base-archive", File.ReadAllText(extracted));
                Assert.AreEqual(Mo2ArchiveLayout.GAME_FOLDER_ORIGIN, gamedata.GetOrigin("textures/base/a.dds"));
                Assert.AreEqual(1, index.Statistics.LoadedArchiveCount);
            }
        }

        [Test]
        public void DamagedExtractionCacheIsRebuiltFromTheWinningArchive()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                WriteGameArchive(directory, "db/textures/textures.db0", Entry(SHARED, "valid"));
                XRayAssetIndex index = Build(fixture, directory);
                index.ExtractionFolder = directory.Combine("cache");
                var gamedata = new XRayEffectiveGamedata(index);
                string cached = gamedata.Materialize(SHARED);
                File.WriteAllText(cached, "wrong");
                Assert.AreEqual(cached, gamedata.Materialize(SHARED));
                Assert.AreEqual("valid", File.ReadAllText(cached));
            }
        }

        [Test]
        public void LooseFilesBeatArchivesAndModsFollowPriority()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A, "+" + MOD_B, "-" + MOD_DISABLED);
                WriteGameArchive(directory, "db/textures/textures.db0", Entry(SHARED, "archive"));
                directory.WriteText(GAME + "/gamedata/" + SHARED, "game-loose");
                fixture.AddModFile(MOD_A, SHARED, Encoding.ASCII.GetBytes("mod-a"));
                fixture.AddModFile(MOD_B, SHARED, Encoding.ASCII.GetBytes("mod-b"));
                fixture.AddModFile(MOD_DISABLED, SHARED, Encoding.ASCII.GetBytes("disabled"));
                fixture.AddModFile(MOD_DISABLED, "textures/disabled_only.dds", Encoding.ASCII.GetBytes("disabled"));
                var gamedata = new XRayEffectiveGamedata(Build(fixture, directory));
                IReadOnlyList<XRayEffectiveFile> candidates = gamedata.GetCandidates(SHARED);
                Assert.AreEqual(4, candidates.Count);
                Assert.AreEqual(MOD_A, candidates[0].Origin);
                Assert.AreEqual(MOD_B, candidates[1].Origin);
                Assert.AreEqual(XRayAssetSourceKind.GameGamedata, candidates[2].Kind);
                Assert.AreEqual(XRayAssetSourceKind.Archive, candidates[3].Kind);
                Assert.IsFalse(gamedata.Exists("textures/disabled_only.dds"));
                fixture.AddProfile(PROFILE, "+" + MOD_B, "+" + MOD_A, "-" + MOD_DISABLED);
                gamedata = new XRayEffectiveGamedata(Build(fixture, directory));
                Assert.AreEqual("mod-b", Encoding.ASCII.GetString(gamedata.ReadAllBytes(SHARED)));
                fixture.AddOverwriteFile(SHARED, Encoding.ASCII.GetBytes("overwrite"));
                gamedata = new XRayEffectiveGamedata(Build(fixture, directory));
                Assert.AreEqual(XRayAssetSourceKind.Overwrite, gamedata.Resolve(SHARED).Kind);
                Assert.AreEqual(5, gamedata.GetCandidates(SHARED).Count);
            }
        }

        [Test]
        public void ArchiveLoadOrderFollowsFsGameAndFileNames()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                WriteGameArchive(directory, "db/files.db0", Entry(SHARED, "files"));
                WriteGameArchive(directory, "db/textures/textures.db0", Entry(SHARED, "textures0"), Entry("textures/only0.dds", "only0"));
                WriteGameArchive(directory, "db/textures/textures.db1", Entry(SHARED, "textures1"));
                var gamedata = new XRayEffectiveGamedata(Build(fixture, directory));
                Assert.AreEqual("textures1", Encoding.ASCII.GetString(gamedata.ReadAllBytes(SHARED)));
                Assert.AreEqual(3, gamedata.GetCandidates(SHARED).Count);
                fixture.AddModRootFile(MOD_A, "db/mods/zz_retexture.db0", XRayArchiveTestWriter.Write(Entry(SHARED, "mod-archive")));
                gamedata = new XRayEffectiveGamedata(Build(fixture, directory));
                Assert.AreEqual("mod-archive", Encoding.ASCII.GetString(gamedata.ReadAllBytes(SHARED)));
                Assert.AreEqual(MOD_A, gamedata.GetOrigin(SHARED));
                fixture.AddModFile(MOD_A, SHARED, Encoding.ASCII.GetBytes("loose"));
                gamedata = new XRayEffectiveGamedata(Build(fixture, directory));
                Assert.AreEqual("loose", Encoding.ASCII.GetString(gamedata.ReadAllBytes(SHARED)));
                Assert.IsFalse(gamedata.Resolve(SHARED).IsArchive);
            }
        }

        [Test]
        public void ModArchiveWithTheSameNameHidesTheGameArchive()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                WriteGameArchive(directory, "db/textures/textures.db0", Entry("textures/game_only.dds", "game"));
                fixture.AddModRootFile(MOD_A, "db/textures/textures.db0", XRayArchiveTestWriter.Write(Entry("textures/mod_only.dds", "mod")));
                XRayAssetIndex index = Build(fixture, directory);
                Assert.IsTrue(index.TryGet("textures/mod_only.dds", out _));
                Assert.IsFalse(index.TryGet("textures/game_only.dds", out _));
                Assert.AreEqual(1, index.ShadowedArchives.Count);
            }
        }

        [Test]
        public void UnsupportedAndDisabledArchivesAreReportedNotIndexed()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                directory.WriteFile(GAME + "/db/textures/broken.db0", XRayArchiveTestWriter.Write(XRayArchiveTestWriter.DEFAULT_ENTRY_POINT, true, false, Entry("textures/broken.dds", "x")));
                directory.WriteFile(GAME + "/db/textures/manual.db0", XRayArchiveTestWriter.Write(XRayArchiveTestWriter.DEFAULT_ENTRY_POINT, false, true, Entry("textures/manual.dds", "x")));
                XRayAssetIndex index = Build(fixture, directory);
                Assert.AreEqual(1, index.UnreadableArchives.Count);
                StringAssert.Contains("broken.db0", index.UnreadableArchives[0]);
                Assert.IsFalse(index.TryGet("textures/broken.dds", out _));
                Assert.IsFalse(index.TryGet("textures/manual.dds", out _));
                Assert.IsTrue(index.Warnings.Exists(warning => warning.Contains("auto_load = false")));
            }
        }

        [Test]
        public void CorruptedArchiveEntryFailsInsteadOfFallingBack()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                byte[] archive = XRayArchiveTestWriter.Write(Entry(SHARED, "good-archive"));
                WriteGameArchive(directory, "db/files.db0", Entry(SHARED, "older-copy"));
                string path = fixture.AddModRootFile(MOD_A, "db/mods/new.db0", archive);
                XRayAssetIndex index = Build(fixture, directory);
                long indexed = Winner(index, SHARED).ModifiedTicks;
                int payload = Encoding.ASCII.GetString(archive).IndexOf("good-archive", StringComparison.Ordinal);
                archive[payload] = (byte)'X';
                File.WriteAllBytes(path, archive);
                File.SetLastWriteTimeUtc(path, new DateTime(indexed, DateTimeKind.Utc));
                var gamedata = new XRayEffectiveGamedata(index);
                Assert.AreEqual(MOD_A, gamedata.GetOrigin(SHARED));
                var exception = Assert.Throws<XRaySourceException>(() => gamedata.ReadAllBytes(SHARED));
                StringAssert.Contains("CRC", exception.Message);
            }
        }

        [Test]
        public void LookupIsCaseInsensitiveAndUnsafePathsAreRejected()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                fixture.AddModFile(MOD_A, "Textures/Terrain/Shared.DDS", Encoding.ASCII.GetBytes("mixed"));
                WriteGameArchive(directory, "db/files.db0", Entry("..\\..\\evil.dds", "evil"), Entry(SHARED, "archive"));
                XRayAssetIndex index = Build(fixture, directory);
                var gamedata = new XRayEffectiveGamedata(index);
                Assert.AreEqual("mixed", Encoding.ASCII.GetString(gamedata.ReadAllBytes("TEXTURES\\terrain\\shared.dds")));
                Assert.AreEqual("Textures/Terrain/Shared.DDS".Replace('/', Path.DirectorySeparatorChar), gamedata.Resolve(SHARED).RelativePath);
                Assert.IsFalse(XRayVirtualPath.TryNormalizeSafe("textures/../../evil.dds", out _));
                Assert.Throws<XRaySourceException>(() => gamedata.Exists("../evil.dds"));
                Assert.IsTrue(index.Warnings.Exists(warning => warning.Contains("escapes the game folder")));
            }
        }

        [Test]
        public void MissingModFolderIsReported()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A, "+Ghost Mod");
                XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(Configuration(fixture, directory));
                CollectionAssert.Contains(plan.MissingModFolders, "Ghost Mod");
                Assert.IsTrue(plan.Warnings.Exists(warning => warning.Contains("Ghost Mod")));
            }
        }

        [Test]
        public void LevelsComeFromTheEffectiveGamedataIncludingModLevels()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A, "+" + MOD_LEVELS, "-" + MOD_DISABLED);
                WriteGameArchive(directory, "db/levels/level_base.db0", Entry("levels/base_loc/level", "base level"), Entry("levels/base_loc/level.geom", "base geom"));
                WriteGameArchive(directory, "db/configs/configs.db0", Entry("configs/game_levels.ltx", "[levels]\r\nlevel01\r\n[level01]\r\nname = base_loc\r\n"));
                fixture.AddModRootFile(MOD_LEVELS, "db/mods/new_loc.db0", XRayArchiveTestWriter.Write(Entry("levels/new_loc/level", "new level"), Entry("levels/new_loc/level.geom", "new geom")));
                fixture.AddModFile(MOD_LEVELS, "configs/mod_game_new.ltx", Encoding.ASCII.GetBytes("![levels]\r\nlevel40\r\n[level40]\r\nname = new_loc\r\n"));
                fixture.AddModFile(MOD_A, "levels/base_loc/level", Encoding.ASCII.GetBytes("replaced level"));
                fixture.AddModFile(MOD_A, "levels/unlisted/level", Encoding.ASCII.GetBytes("unlisted"));
                fixture.AddModFile(MOD_DISABLED, "levels/hidden_loc/level", Encoding.ASCII.GetBytes("hidden"));
                XRayLevelGamedataInfo info = XRayLevelEffectiveCatalog.Inspect(Build(fixture, directory), PROFILE);
                Assert.AreEqual(3, info.Levels.Count);
                XRayLevelEntry baseLevel = info.Find("base_loc");
                Assert.IsTrue(baseLevel.IsReplaced);
                Assert.AreEqual(MOD_A, baseLevel.Origin);
                Assert.IsTrue(baseLevel.IsRegistered);
                CollectionAssert.Contains(baseLevel.Files, "level.geom");
                XRayLevelEntry modLevel = info.Find("new_loc");
                Assert.IsTrue(modLevel.IsModded);
                Assert.AreEqual(MOD_LEVELS, modLevel.Origin);
                Assert.IsTrue(modLevel.IsRegistered);
                Assert.IsFalse(info.Find("unlisted").IsRegistered);
                Assert.IsNull(info.Find("hidden_loc"));
                Assert.AreEqual(XRayLevelProbeStatus.Unreadable, XRayLevelProbe.Run(info.Effective, modLevel).Status);
            }
        }

        [Test]
        public void ThmAndBumpAreResolvedIndependently()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A, "+" + MOD_B);
                WriteGameArchive(directory, "db/textures/textures.db0", new XRayArchiveTestEntry("textures\\trees\\pine.dds", XRayTestBinaries.Bc1Solid(16, 16, 10, 20, 30), false));
                fixture.AddModFile(MOD_A, "textures/trees/pine.thm", XRayTestBinaries.Thm("trees\\pine_bump", 0));
                fixture.AddModFile(MOD_B, "textures/trees/pine_bump.dds", XRayTestBinaries.Bc1Solid(16, 16, 128, 128, 255));
                var resolver = new XRayTextureResolver(Build(fixture, directory));
                XRayTextureResolution resolution = resolver.Resolve("trees\\pine");
                Assert.AreEqual(XRayAssetSourceKind.Archive, resolution.Diffuse.Winner.Kind);
                StringAssert.Contains(MOD_A, resolution.DescriptionOrigin);
                Assert.AreEqual(MOD_B, resolution.Bump.Winner.Origin);
                Assert.IsFalse(resolver.Resolve("trees\\missing").Diffuse.IsResolved);
            }
        }

        [Test]
        public void CancellationStopsIndexing()
        {
            using (var directory = new TestDirectory())
            using (var cancellation = new CancellationTokenSource())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                fixture.AddModFile(MOD_A, SHARED, Encoding.ASCII.GetBytes("a"));
                XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(Configuration(fixture, directory));
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => XRayAssetIndex.Build(plan, cancellation.Token, null));
            }
        }

        [Test]
        public void FingerprintFollowsProfileChangesAndProfileIdIsStable()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A, "+" + MOD_B);
                fixture.AddModFile(MOD_A, SHARED, Encoding.ASCII.GetBytes("a"));
                fixture.AddModFile(MOD_B, SHARED, Encoding.ASCII.GetBytes("b"));
                XRaySourcePlan first = Mo2VirtualFileSystem.Plan(Configuration(fixture, directory));
                Assert.AreEqual(first.Fingerprint, Mo2VirtualFileSystem.Plan(Configuration(fixture, directory)).Fingerprint);
                string id = XRayLevelSourceCache.CreateProfileId(first.Installation.InstanceFolder, first.ProfileName);
                fixture.AddProfile(PROFILE, "+" + MOD_B, "+" + MOD_A);
                File.SetLastWriteTimeUtc(Mo2ProfilePath(fixture), DateTime.UtcNow.AddMinutes(1));
                XRaySourcePlan second = Mo2VirtualFileSystem.Plan(Configuration(fixture, directory));
                Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
                Assert.AreNotEqual(first.ModOrderFingerprint, second.ModOrderFingerprint);
                Assert.AreEqual(id, XRayLevelSourceCache.CreateProfileId(second.Installation.InstanceFolder, second.ProfileName));
                Assert.AreNotEqual(id, XRayLevelSourceCache.CreateProfileId(second.Installation.InstanceFolder, "Other"));
                StringAssert.StartsWith("1v2_DX11_v_12gb_2.5_168_", XRayLevelSourceCache.CreateProfileId("D:\\GRA2.5\\MO2", "1v2(DX11) (v_12gb) 2.5, Все включено (168)"));
            }
        }

        [Test]
        public void FingerprintFollowsNestedLooseFileEdits()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                fixture.AddModFile(MOD_A, SHARED, Encoding.ASCII.GetBytes("old"));
                string path = Path.Combine(fixture.Mo2Root, "mods", MOD_A, "gamedata", SHARED);
                string parent = Path.GetDirectoryName(path);
                DateTime folderStamp = Directory.GetLastWriteTimeUtc(parent);
                XRaySourcePlan first = Mo2VirtualFileSystem.Plan(Configuration(fixture, directory));
                File.WriteAllText(path, "new");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
                Directory.SetLastWriteTimeUtc(parent, folderStamp);
                XRaySourcePlan second = Mo2VirtualFileSystem.Plan(Configuration(fixture, directory));
                Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
            }
        }

        [Test]
        public void IndexRoundTripKeepsArchiveCandidatesReadable()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                WriteGameArchive(directory, "db/textures/textures.db0", Entry(SHARED, "archive"));
                XRayAssetIndex index = Build(fixture, directory);
                string path = directory.Combine("index.bin");
                index.Save(path);
                XRayAssetIndex loaded = XRayAssetIndex.Load(path);
                Assert.AreEqual(index.Fingerprint, loaded.Fingerprint);
                Assert.AreEqual(index.Statistics.FileCount, loaded.Statistics.FileCount);
                Assert.AreEqual("archive", Encoding.ASCII.GetString(new XRayEffectiveGamedata(loaded).ReadAllBytes(SHARED)));
            }
        }

        [Test]
        public void ChangedWinnerIsDetectedAgainstThePreviousManifest()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A, "+" + MOD_B);
                WriteGameArchive(directory, "db/levels/level_x.db0", Entry("levels/x/level", "level bytes"));
                fixture.AddModFile(MOD_A, SHARED, Encoding.ASCII.GetBytes("retexture a"));
                fixture.AddModFile(MOD_B, SHARED, Encoding.ASCII.GetBytes("retexture b"));
                XRayLevelGamedataInfo info = XRayLevelEffectiveCatalog.Inspect(Build(fixture, directory), PROFILE);
                var record = new XRayLevelImportRecord { LevelName = "x" };
                record.Source.SourceType = XRayLevelSourceManifest.SOURCE_TYPE_MO2;
                record.AddDependency(XRayLevelDependencyCategory.Materials, "Diffuse", SHARED, null, SHARED, XRayLevelDependencyStatus.Imported, "test");
                XRayLevelSourceManifest.Collect(record, info.Effective, info.Find("x"));
                Assert.AreEqual(MOD_A, record.Source.Find(SHARED).Origin);
                Assert.IsNotEmpty(record.Source.Find(SHARED).ContentHash);
                Assert.AreEqual(MOD_A, record.Dependencies[0].Origin);
                Assert.IsFalse(XRayLevelSourceManifest.Compare(record, info.Effective, info.Find("x"), null).ReimportRequired);
                fixture.AddProfile(PROFILE, "+" + MOD_B, "+" + MOD_A);
                XRayLevelGamedataInfo changed = XRayLevelEffectiveCatalog.Inspect(Build(fixture, directory), PROFILE);
                XRayLevelSourceDiff diff = XRayLevelSourceManifest.Compare(record, changed.Effective, changed.Find("x"), null);
                Assert.IsTrue(diff.ReimportRequired);
                XRayLevelSourceChange change = diff.Changes.Find(item => item.VirtualPath == SHARED);
                Assert.AreEqual(XRayLevelSourceChangeKind.WinnerChanged, change.Kind);
                Assert.AreEqual(XRayLevelSourceManifest.CATEGORY_TEXTURE, change.Category);
                Assert.AreEqual(MOD_B, change.Current);
            }
        }

        [Test]
        public void TextureAssetFollowsTheWinnerAndKeepsItsGuid()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                Mo2Fixture fixture = CreateProfile(scope.Directory, "+" + MOD_A, "+" + MOD_B);
                fixture.AddModFile(MOD_A, "textures/trees/pine.dds", XRayTestBinaries.Bc1Solid(16, 16, 200, 10, 10));
                fixture.AddModFile(MOD_B, "textures/trees/pine.dds", XRayTestBinaries.Bc1Solid(16, 16, 10, 200, 10));
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    XRayLevelTextureSet first = Import(fixture, scope);
                    string guid = AssetDatabase.AssetPathToGUID(first.AlbedoPath);
                    byte[] firstBytes = File.ReadAllBytes(first.AlbedoPath);
                    string key = XRayAssetMarker.ReadKey(first.AlbedoPath);
                    StringAssert.StartsWith(scope.Folder + "/Textures/", first.AlbedoPath);
                    XRayLevelTextureSet repeated = Import(fixture, scope);
                    Assert.AreEqual(XRayResourceStatus.Reused, repeated.AlbedoStatus);
                    fixture.AddProfile(PROFILE, "+" + MOD_B, "+" + MOD_A);
                    XRayLevelTextureSet second = Import(fixture, scope);
                    Assert.AreEqual(first.AlbedoPath, second.AlbedoPath);
                    Assert.AreEqual(XRayResourceStatus.Updated, second.AlbedoStatus);
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(second.AlbedoPath));
                    Assert.AreEqual(key, XRayAssetMarker.ReadKey(second.AlbedoPath));
                    CollectionAssert.AreNotEqual(firstBytes, File.ReadAllBytes(second.AlbedoPath));
                    Assert.AreEqual(MOD_B, XRayAssetMarker.Read(second.AlbedoPath).Source);
                }
                finally
                {
                    XRayLevelPaths.UseRoot(null);
                }
            }
        }

        [Test]
        public void ManifestTracksChangedThmAndPreviouslyMissingDependencies()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                fixture.AddModFile(MOD_A, "levels/x/level", Encoding.ASCII.GetBytes("level"));
                const string THM = "textures/trees/pine.thm";
                const string MISSING = "textures/trees/missing.thm";
                string path = fixture.AddModFile(MOD_A, THM, Encoding.ASCII.GetBytes("old metadata"));
                XRayLevelGamedataInfo info = XRayLevelEffectiveCatalog.Inspect(Build(fixture, directory), PROFILE);
                var record = new XRayLevelImportRecord { LevelName = "x" };
                record.Source.SourceType = XRayLevelSourceManifest.SOURCE_TYPE_MO2;
                record.AddDependency(XRayLevelDependencyCategory.Materials, "THM", THM, null, THM, XRayLevelDependencyStatus.Found, "metadata");
                record.AddDependency(XRayLevelDependencyCategory.Materials, "THM", MISSING, null, MISSING, XRayLevelDependencyStatus.Skipped, "optional metadata");
                XRayLevelSourceManifest.Collect(record, info.Effective, info.Find("x"));
                Assert.IsNotEmpty(record.Source.Find(THM).ContentHash);
                File.WriteAllText(path, "new metadata");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
                fixture.AddModFile(MOD_A, MISSING, Encoding.ASCII.GetBytes("added metadata"));
                info = XRayLevelEffectiveCatalog.Inspect(Build(fixture, directory), PROFILE);
                XRayLevelSourceDiff diff = XRayLevelSourceManifest.Compare(record, info.Effective, info.Find("x"), null);
                Assert.IsTrue(diff.Changes.Exists(change => change.VirtualPath == THM && change.Category == XRayLevelSourceManifest.CATEGORY_THM && change.Kind == XRayLevelSourceChangeKind.ContentChanged));
                Assert.IsTrue(diff.Changes.Exists(change => change.VirtualPath == MISSING && change.Kind == XRayLevelSourceChangeKind.Added));
            }
        }

        [Test]
        public void ManualDirectoriesOverrideTheMo2Configuration()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                string mods = directory.Combine("external/mods");
                string profiles = directory.Combine("external/profiles");
                string overwrite = directory.Combine("external/overwrite");
                directory.WriteText("external/profiles/" + PROFILE + "/modlist.txt", "+" + MOD_A);
                directory.WriteText("external/mods/" + MOD_A + "/gamedata/" + SHARED, "mod");
                directory.WriteText("external/overwrite/gamedata/" + SHARED, "overwrite");
                XRaySourceConfiguration configuration = Configuration(fixture, directory);
                configuration.ModsFolder = mods;
                configuration.ProfilesFolder = profiles;
                configuration.OverwriteFolder = overwrite;
                XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
                Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
                var gamedata = new XRayEffectiveGamedata(XRayAssetIndex.Build(plan));
                Assert.AreEqual("overwrite", Encoding.UTF8.GetString(gamedata.ReadAllBytes(SHARED)));
                Assert.AreEqual(Mo2VirtualFileSystem.OVERWRITE_NAME, gamedata.GetOrigin(SHARED));
                Assert.AreEqual(2, gamedata.GetCandidates(SHARED).Count);
            }
        }

        [Test]
        public void LegacyDdsAndSourceSizeChangesKeepPngGuidAndMaterialReference()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                Mo2Fixture fixture = CreateProfile(scope.Directory, "+" + MOD_A);
                const string virtualPath = "textures/trees/pine.dds";
                string source = fixture.AddModFile(MOD_A, virtualPath, XRayTestBinaries.Bc1Solid(2, 2, 200, 10, 10));
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    string legacyPath = XRayLevelPaths.TextureFolder(virtualPath) + "/pine.dds";
                    Directory.CreateDirectory(Path.GetDirectoryName(legacyPath));
                    File.WriteAllBytes(legacyPath, XRayTestBinaries.Bc1Solid(16, 16, 200, 10, 10));
                    AssetDatabase.ImportAsset(legacyPath, ImportAssetOptions.ForceSynchronousImport);
                    XRayAssetMarker.Write(legacyPath, new XRayAssetMarker(virtualPath + XRayTextureImporter.ALBEDO_KEY_SUFFIX, XRayTextureRole.Albedo.ToString(), MOD_A, source, string.Empty));
                    var legacy = AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(legacyPath);
                    Assert.IsNotNull(legacy);
                    string guid = AssetDatabase.AssetPathToGUID(legacyPath);
                    var material = new UnityEngine.Material(UnityEngine.Shader.Find("HDRP/MonolithLit"));
                    string materialPath = scope.Folder + "/linked.mat";
                    material.SetTexture("_BaseColorMap", legacy);
                    AssetDatabase.CreateAsset(material, materialPath);
                    XRayLevelTextureSet first = Import(fixture, scope);
                    StringAssert.EndsWith(".png", first.AlbedoPath);
                    var texture = AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(first.AlbedoPath);
                    Assert.AreEqual(2, texture.width);
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(first.AlbedoPath));
                    Assert.AreEqual(first.AlbedoPath, AssetDatabase.GetAssetPath(material.GetTexture("_BaseColorMap")));
                    Assert.IsFalse(File.Exists(legacyPath));
                    File.WriteAllBytes(source, XRayTestBinaries.Bc1Solid(16, 16, 10, 200, 10));
                    File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));
                    XRayLevelTextureSet second = Import(fixture, scope);
                    Assert.AreEqual(first.AlbedoPath, second.AlbedoPath);
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(second.AlbedoPath));
                    Assert.AreEqual(16, AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(second.AlbedoPath).width);
                    Assert.AreEqual(second.AlbedoPath, AssetDatabase.GetAssetPath(material.GetTexture("_BaseColorMap")));
                    File.WriteAllBytes(source, XRayTestBinaries.Bc1Solid(2, 2, 10, 10, 200));
                    File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(2));
                    XRayLevelTextureSet third = Import(fixture, scope);
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(third.AlbedoPath));
                    texture = AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(third.AlbedoPath);
                    Assert.IsNotNull(texture);
                    Assert.AreEqual(2, texture.width);
                    Assert.AreEqual(third.AlbedoPath, AssetDatabase.GetAssetPath(material.GetTexture("_BaseColorMap")));
                }
                finally
                {
                    XRayLevelPaths.UseRoot(null);
                }
            }
        }

        [TestCase(2, 4)]
        [TestCase(8, 16)]
        public void Bc1PngKeepsTransparentPixelsAndRepairsExistingDxt1Override(int size, int transparentPixelCount)
        {
            using (var scope = new XRayImportTestScope(null))
            {
                Mo2Fixture fixture = CreateProfile(scope.Directory, "+" + MOD_A);
                byte[] data = XRayTestBinaries.Bc1Solid(size, size, 200, 10, 10);
                Array.Fill(data, byte.MaxValue, DdsHeader.HEADER_SIZE + sizeof(uint), sizeof(uint));
                fixture.AddModFile(MOD_A, "textures/trees/pine.dds", data);
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    XRayLevelTextureSet first = Import(fixture, scope);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(first.AlbedoPath);
                    string guid = AssetDatabase.AssetPathToGUID(first.AlbedoPath);
                    byte[] png = File.ReadAllBytes(first.AlbedoPath);
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    Assert.AreEqual(transparentPixelCount, CountTransparentPixels(first.AlbedoPath));

                    TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
                    standalone.format = TextureImporterFormat.DXT1;
                    importer.SetPlatformTextureSettings(standalone);
                    importer.SaveAndReimport();
                    Assert.AreEqual(0, CountTransparentPixels(first.AlbedoPath));

                    XRayLevelTextureSet repaired = Import(fixture, scope);
                    Assert.AreEqual(first.AlbedoPath, repaired.AlbedoPath);
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(repaired.AlbedoPath));
                    CollectionAssert.AreEqual(png, File.ReadAllBytes(repaired.AlbedoPath));
                    Assert.AreEqual(transparentPixelCount, CountTransparentPixels(repaired.AlbedoPath));
                }
                finally
                {
                    XRayLevelPaths.UseRoot(null);
                }
            }
        }

        private static int CountTransparentPixels(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return Array.FindAll(texture.GetPixels32(), pixel => pixel.a == 0).Length;
        }

        [Test]
        public void GeometryReuseRequiresMatchingSettingsAndStableGeometry()
        {
            using (var directory = new TestDirectory())
            {
                var previous = new XRayLevelImportRecord { Completed = true, Settings = "settings", PrefabPath = directory.WriteText("level.prefab", "prefab"), ScenePath = directory.WriteText("level.unity", "scene") };
                previous.Source.ImporterVersion = XRayLevelImportJob.IMPORTER_VERSION;
                var diff = new XRayLevelSourceDiff { HasPrevious = true };
                diff.Changes.Add(new XRayLevelSourceChange { Kind = XRayLevelSourceChangeKind.WinnerChanged, Category = XRayLevelSourceManifest.CATEGORY_TEXTURE });
                Assert.IsTrue(XRayLevelImportJob.CanReuseGeometry(previous, diff, "settings"));
                Assert.IsFalse(XRayLevelImportJob.CanReuseGeometry(previous, diff, "new settings"));
                diff.Changes[0].Category = XRayLevelSourceManifest.CATEGORY_LEVEL;
                Assert.IsFalse(XRayLevelImportJob.CanReuseGeometry(previous, diff, "settings"));
                diff.Changes[0].Category = XRayLevelSourceManifest.CATEGORY_TEXTURE;
                diff.Changes[0].Kind = XRayLevelSourceChangeKind.Removed;
                Assert.IsFalse(XRayLevelImportJob.CanReuseGeometry(previous, diff, "settings"));
            }
        }

        [Test]
        public void ChangedThmMetadataRegeneratesMaterialInPlaceUnderPreserve()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                Mo2Fixture fixture = CreateProfile(scope.Directory, "+" + MOD_A, "+" + MOD_B);
                fixture.AddModFile(MOD_A, "textures/floor/test_floor.dds", XRayTestBinaries.Bc1Solid(16, 16, 120, 100, 80));
                fixture.AddModFile(MOD_A, "textures/floor/test_floor_bump.dds", XRayTestBinaries.Bc3Solid(16, 16, 128, 128, 128, 255));
                fixture.AddModFile(MOD_A, "textures/floor/control.dds", XRayTestBinaries.Bc1Solid(16, 16, 10, 20, 30));
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    (Material first, Material control, XRayLevelMaterialLibrary _) = CreateFloorMaterials(fixture, scope);
                    string path = AssetDatabase.GetAssetPath(first);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    string controlPath = AssetDatabase.GetAssetPath(control);
                    byte[] controlBytes = File.ReadAllBytes(controlPath);
                    string controlMeta = File.ReadAllText(controlPath + ".meta");
                    Assert.AreEqual(0.0f, first.GetFloat("_MonolithHasBump"));
                    fixture.AddModFile(MOD_B, "textures/floor/test_floor.thm", XRayTestBinaries.Thm("floor\\test_floor_bump", 0));
                    (Material second, Material controlAgain, XRayLevelMaterialLibrary library) = CreateFloorMaterials(fixture, scope);
                    Assert.AreEqual(path, AssetDatabase.GetAssetPath(second));
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
                    Assert.AreEqual(0, library.Created);
                    CollectionAssert.AreEqual(new[] { path }, library.Regenerated);
                    Assert.AreEqual(1.0f, second.GetFloat("_MonolithHasBump"));
                    Assert.IsNotNull(second.GetTexture("_MonolithBump"));
                    StringAssert.Contains("|m1w0b2|", XRayAssetMarker.ReadKey(path));
                    Assert.AreEqual(controlPath, AssetDatabase.GetAssetPath(controlAgain));
                    CollectionAssert.AreEqual(controlBytes, File.ReadAllBytes(controlPath));
                    Assert.AreEqual(controlMeta, File.ReadAllText(controlPath + ".meta"));
                }
                finally
                {
                    XRayLevelPaths.UseRoot(null);
                }
            }
        }

        [Test]
        public void ThmParserChangeIsReportedWithoutSourceChanges()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateProfile(directory, "+" + MOD_A);
                WriteGameArchive(directory, "db/levels/level_x.db0", Entry("levels/x/level", "level bytes"));
                byte[] thm = XRayTestBinaries.Thm("floor\\test_floor_bump", 0);
                int position = 0;
                while (BitConverter.ToUInt32(thm, position) != XRayThmReader.CHUNK_DETAIL_EXT)
                {
                    position += 8 + (int)BitConverter.ToUInt32(thm, position + 4);
                }

                BitConverter.GetBytes(29u).CopyTo(thm, position + 4);
                fixture.AddModFile(MOD_A, "textures/floor/test_floor.thm", thm);
                XRayLevelGamedataInfo info = XRayLevelEffectiveCatalog.Inspect(Build(fixture, directory), PROFILE);
                var record = new XRayLevelImportRecord { LevelName = "x" };
                record.Source.SourceType = XRayLevelSourceManifest.SOURCE_TYPE_MO2;
                record.AddDependency(XRayLevelDependencyCategory.Materials, "THM metadata", "textures/floor/test_floor.thm", null, "textures/floor/test_floor.thm", XRayLevelDependencyStatus.Found, "test");
                XRayLevelSourceManifest.Collect(record, info.Effective, info.Find("x"));
                record.Source.ThmParserVersion = 1;
                XRayLevelSourceDiff diff = XRayLevelSourceManifest.Compare(record, info.Effective, info.Find("x"), null);
                Assert.IsEmpty(diff.Changes);
                CollectionAssert.AreEqual(new[] { "textures/floor/test_floor.thm" }, diff.MetadataReparse);
                Assert.IsTrue(diff.ReimportRequired);
                record.Source.ThmParserVersion = XRayThmReader.PARSER_VERSION;
                Assert.IsFalse(XRayLevelSourceManifest.Compare(record, info.Effective, info.Find("x"), null).ReimportRequired);
                string settings = JsonUtility.ToJson(new XRayLevelImportSettings { MaterialUpdateMode = XRayLevelMaterialUpdateMode.Update });
                Assert.AreEqual(XRayLevelImportJob.GeometrySettings(JsonUtility.ToJson(new XRayLevelImportSettings())), XRayLevelImportJob.GeometrySettings(settings));
            }
        }

        private static (Material Floor, Material Control, XRayLevelMaterialLibrary Library) CreateFloorMaterials(Mo2Fixture fixture, XRayImportTestScope scope)
        {
            XRayAssetIndex index = Build(fixture, scope.Directory);
            var record = new XRayLevelImportRecord();
            var textures = new XRayLevelTextureLibrary(index, "x", record, false, true);
            XRayLevelTextureSet floor = textures.Request("floor\\test_floor", true, "test");
            XRayLevelTextureSet control = textures.Request("floor\\control", true, "test");
            textures.ImportAll();
            scope.Settle();
            var library = new XRayLevelMaterialLibrary(record, XRayLevelMaterialUpdateMode.Preserve);
            var surface = new XRayLevelSurface { Kind = XRayLevelSurfaceKind.Opaque, RuleName = "opaque", HemiMode = XRayLevelHemiMode.Constant };
            Material floorMaterial = library.Get(new XRayLevelMaterialRequest { ShaderName = "default", Surface = surface, Textures = floor });
            Material controlMaterial = library.Get(new XRayLevelMaterialRequest { ShaderName = "default", Surface = surface, Textures = control });
            AssetDatabase.SaveAssets();
            return (floorMaterial, controlMaterial, library);
        }

        [Test]
        public void AnomalySpaceIsSeparatedFromTrueStalkerAssets()
        {
            string space = XRayLevelPaths.SpaceRoot("profile_12345678");
            Assert.AreEqual(XRayLevelPaths.ANOMALY_ROOT + "/profile_12345678", space);
            Assert.AreEqual(XRayLevelPaths.ROOT, XRayLevelPaths.SpaceRoot(null));
            XRayLevelPaths.UseRoot(space);
            try
            {
                StringAssert.StartsWith(space + "/Textures/", XRayLevelPaths.TextureFolder("textures/terrain/grass.dds"));
                StringAssert.StartsWith(space + "/escape/", XRayLevelPaths.ScenePath("escape"));
            }
            finally
            {
                XRayLevelPaths.UseRoot(null);
            }

            Assert.AreEqual(XRayLevelPaths.ROOT + "/Textures/terrain", XRayLevelPaths.TextureFolder("textures/terrain/grass.dds"));
            Assert.AreEqual(XRayLevelPaths.ROOT + "/escape/Scenes/escape.unity", XRayLevelPaths.ScenePath("escape"));
        }

        [Test]
        public void RealMo2ProfileResolvesArchivesAndModLevels()
        {
            if (!Directory.Exists(XRayAssetSourceUserSettings.DEFAULT_MO2_FOLDER))
            {
                Assert.Ignore("The real MO2 installation is not available on this machine.");
            }

            var configuration = new XRaySourceConfiguration { Mode = XRaySourceMode.Mo2Effective, Mo2Folder = XRayAssetSourceUserSettings.DEFAULT_MO2_FOLDER, LocalAppDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(XRayLevelSourceCache.MO2_INDEX_SCOPE);
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            Assert.IsEmpty(index.UnreadableArchives);
            Assert.Greater(index.Statistics.LoadedArchiveCount, 0);
            XRayLevelGamedataInfo info = XRayLevelEffectiveCatalog.Inspect(index, plan.ProfileName);
            XRayLevelEntry modded = info.Levels.Find(level => level.IsModded);
            Assert.IsNotNull(modded, "No level added by a mod was found.");
            Assert.AreEqual(XRayLevelProbeStatus.Ready, XRayLevelProbe.Run(info.Effective, modded).Status);
            Assert.IsTrue(info.Effective.Exists("shaders.xr"));
            Assert.Greater(info.Effective.ReadAllBytes("shaders.xr").Length, 0);
        }

        private static XRayLevelTextureSet Import(Mo2Fixture fixture, XRayImportTestScope scope)
        {
            XRayAssetIndex index = Build(fixture, scope.Directory);
            var record = new XRayLevelImportRecord();
            var library = new XRayLevelTextureLibrary(index, "x", record, false, true);
            XRayLevelTextureSet set = library.Request("trees\\pine", false, "test");
            library.ImportAll();
            scope.Settle();
            return set;
        }

        private static XRayIndexCandidate Winner(XRayAssetIndex index, string path)
        {
            index.TryGet(path, out XRayIndexedFile file);
            return file.Winner;
        }

        private static XRayArchiveTestEntry Entry(string name, string text)
        {
            return new XRayArchiveTestEntry(name.Replace('/', '\\'), Encoding.ASCII.GetBytes(text), false);
        }

        private static void WriteGameArchive(TestDirectory directory, string relative, params XRayArchiveTestEntry[] entries)
        {
            directory.WriteFile(GAME + "/" + relative, XRayArchiveTestWriter.Write(entries));
        }

        private static Mo2Fixture CreateProfile(TestDirectory directory, params string[] modList)
        {
            var fixture = new Mo2Fixture(directory);
            fixture.WriteIni(PROFILE);
            fixture.AddProfile(PROFILE, modList);
            Directory.CreateDirectory(directory.Combine(GAME + "/db"));
            Directory.CreateDirectory(directory.Combine(GAME + "/gamedata"));
            return fixture;
        }

        private static string Mo2ProfilePath(Mo2Fixture fixture)
        {
            return Path.Combine(fixture.Mo2Root, "profiles", PROFILE, "modlist.txt");
        }

        private static XRaySourceConfiguration Configuration(Mo2Fixture fixture, TestDirectory directory)
        {
            var configuration = new XRaySourceConfiguration { Mode = XRaySourceMode.Mo2Effective, Mo2Folder = fixture.Mo2Root, Mo2Profile = PROFILE, GameFolder = directory.Combine(GAME), IncludeGameGamedata = true };
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(XRayLevelSourceCache.MO2_INDEX_SCOPE);
            return configuration;
        }

        private static XRayAssetIndex Build(Mo2Fixture fixture, TestDirectory directory)
        {
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(Configuration(fixture, directory));
            Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
            return XRayAssetIndex.Build(plan);
        }
    }
}
