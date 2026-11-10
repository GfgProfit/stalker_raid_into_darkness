using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class XRayLevelAnomalySourceTests
    {
        private const string ARCHIVE = "E:/Projects/stalker_raid_into_darkness_Archive/AnomalyLocationImport_20261110/Validation/SourceFixtures";
        private const string PROFILE = "AnomalySourceTests";
        private const string MOD = "[Локация] Levels_0.53";
        private const string TARGET = "l02_target";

        private TestDirectory _directory;
        private Mo2Fixture _fixture;
        private string _assetRoot;

        [SetUp]
        public void SetUp()
        {
            _directory = new TestDirectory(ARCHIVE);
            _fixture = new Mo2Fixture(_directory);
            _fixture.WriteIni(PROFILE);
            _fixture.AddProfile(PROFILE, "+" + MOD);
            _fixture.AddBaseFile("configs/system.ltx", Encoding.UTF8.GetBytes("[zone_electro]\nclass = ZS_GALAN\n"));
            _assetRoot = "Assets/_Source/Tests/AnomalyImportFixture_" + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(_assetRoot))
            {
                Assert.That(AssetDatabase.DeleteAsset(_assetRoot), Is.True, _assetRoot);
            }

            _directory?.Dispose();
        }

        [TestCase("[Локация] Levels_0.53", false, "levels053")]
        [TestCase("Anomaly 1.5.3", true, "anomaly")]
        [TestCase("[Локация] My Pack 1.2", false, "mypack12")]
        public void SharedSpawnNameIdentifiesItsSourcePack(string source, bool baseSource, string expected)
        {
            Assert.That(XRayLevelAnomalyImporter.SpawnFileStem(source, baseSource), Is.EqualTo(expected));
        }

        [TestCase("ZS_GALAN", true)]
        [TestCase("Z_MBALD", true)]
        [TestCase("ZS_RADIO", true)]
        [TestCase("Z_CFIRE", false)]
        [TestCase("SPC_RS_S", false)]
        [TestCase(null, false)]
        public void HazardClassificationIncludesOriginalAndScriptedZonesButExcludesCampfires(string className, bool expected)
        {
            Assert.That(XRayLevelAnomalyImporter.IsAnomalyClass(className), Is.EqualTo(expected));
        }

        [Test]
        public void WinningSpawnOverridesBaseEvenWhenBaseGeometryWouldOwnTheLocation()
        {
            _fixture.AddBaseFile(XRayLevelAnomalyImporter.SPAWN_PATH, XRayLevelSpawnReaderTests.Fixture(radius: 2f));
            _fixture.AddModFile(MOD, XRayLevelAnomalyImporter.SPAWN_PATH, XRayLevelSpawnReaderTests.Fixture(radius: 7f));
            XRayLevelAnomalyImportPlan plan = Prepare(Build());
            Assert.That(plan.Origin, Is.EqualTo(MOD));
            Assert.That(plan.Data.Anomalies[0].Shapes[0].Radius, Is.EqualTo(7d));
            Assert.That(Path.GetFileName(plan.SpawnAssetPath), Is.EqualTo("levels053_all.spawn"));
        }

        [Test]
        public void OverwriteWinsOverEnabledModsAndBase()
        {
            _fixture.AddBaseFile(XRayLevelAnomalyImporter.SPAWN_PATH, XRayLevelSpawnReaderTests.Fixture(radius: 2f));
            _fixture.AddModFile(MOD, XRayLevelAnomalyImporter.SPAWN_PATH, XRayLevelSpawnReaderTests.Fixture(radius: 7f));
            _fixture.AddOverwriteFile(XRayLevelAnomalyImporter.SPAWN_PATH, XRayLevelSpawnReaderTests.Fixture(radius: 9f));
            XRayAssetIndex index = Build();
            Assert.That(index.TryGet(XRayLevelAnomalyImporter.SPAWN_PATH, out XRayIndexedFile spawn), Is.True);
            Assert.That(index.GetSource(spawn.Winner).Kind, Is.EqualTo(XRayAssetSourceKind.Overwrite));
            Assert.That(Prepare(index).Data.Anomalies[0].Shapes[0].Radius, Is.EqualTo(9d));
        }

        [Test]
        public void MissingLevelInWinnerDoesNotFallBackToCompatibleLosingSpawn()
        {
            _fixture.AddBaseFile(XRayLevelAnomalyImporter.SPAWN_PATH, XRayLevelSpawnReaderTests.Fixture());
            _fixture.AddModFile(MOD, XRayLevelAnomalyImporter.SPAWN_PATH, XRayLevelSpawnReaderTests.Fixture(levelName: "l03_winner"));
            XRaySourceException error = Assert.Throws<XRaySourceException>(() => Prepare(Build()));
            Assert.That(error.Message, Does.Contain(TARGET));
            Assert.That(error.Message, Does.Contain(MOD));
            Assert.That(Directory.Exists(_assetRoot), Is.False);
        }

        [Test]
        public void MultipleLocationsReuseOneSharedSpawnWithoutChangingItsGuid()
        {
            _fixture.AddModFile(MOD, XRayLevelAnomalyImporter.SPAWN_PATH, XRayLevelSpawnReaderTests.Fixture());
            XRayAssetIndex index = Build();
            XRayLevelAnomalyImportPlan first = Prepare(index);
            string guid = AssetDatabase.AssetPathToGUID(first.SpawnAssetPath);
            Assert.That(guid, Is.Not.Empty);
            XRayLevelAnomalyImportPlan second = XRayLevelAnomalyImporter.Prepare(index, "l01_other", _assetRoot, null);
            Assert.That(second.SpawnAssetPath, Is.EqualTo(first.SpawnAssetPath));
            Assert.That(second.Sha256, Is.EqualTo(first.Sha256));
            Assert.That(AssetDatabase.AssetPathToGUID(second.SpawnAssetPath), Is.EqualTo(guid));
            Assert.That(Directory.GetFiles(_assetRoot, "*.spawn", SearchOption.AllDirectories).Length, Is.EqualTo(1));
        }

        [Test]
        public void ChangedSpawnContentKeepsPreviousBytesAndGuidInItsSharedCopy()
        {
            byte[] original = XRayLevelSpawnReaderTests.Fixture(radius: 2f);
            _fixture.AddModFile(MOD, XRayLevelAnomalyImporter.SPAWN_PATH, original);
            XRayLevelAnomalyImportPlan first = Prepare(Build());
            string originalGuid = AssetDatabase.AssetPathToGUID(first.SpawnAssetPath);
            byte[] replacement = XRayLevelSpawnReaderTests.Fixture(radius: 8f);
            _fixture.AddModFile(MOD, XRayLevelAnomalyImporter.SPAWN_PATH, replacement);
            XRayLevelAnomalyImportPlan second = Prepare(Build());
            Assert.That(second.SpawnAssetPath, Is.Not.EqualTo(first.SpawnAssetPath));
            Assert.That(second.SpawnAssetPath, Does.Contain(second.Sha256));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(first.SpawnAssetPath));
            CollectionAssert.AreEqual(replacement, File.ReadAllBytes(second.SpawnAssetPath));
            Assert.That(AssetDatabase.AssetPathToGUID(first.SpawnAssetPath), Is.EqualTo(originalGuid));
        }

        [Test]
        public void MissingHazardClassFailsBeforeWritingTheSharedSource()
        {
            _fixture.AddBaseFile("configs/system.ltx", Encoding.UTF8.GetBytes("[zone_electro]\n"));
            _fixture.AddModFile(MOD, XRayLevelAnomalyImporter.SPAWN_PATH, XRayLevelSpawnReaderTests.Fixture());
            XRaySourceException error = Assert.Throws<XRaySourceException>(() => Prepare(Build()));
            Assert.That(error.Message, Does.Contain("zone_electro"));
            Assert.That(error.Message, Does.Contain("class"));
            Assert.That(Directory.Exists(_assetRoot), Is.False);
        }

        [Test]
        public void GeometryComparisonIgnoresAnomalyFlagAndCatalogSelection()
        {
            var before = new XRayLevelImportSettings { ImportAnomalies = false, AnomalyCatalog = null };
            var after = new XRayLevelImportSettings { ImportAnomalies = true, AnomalyCatalog = XRayLevelAnomalyCatalog.LoadDefault() };
            Assert.That(XRayLevelImportJob.GeometrySettings(JsonUtility.ToJson(after)), Is.EqualTo(XRayLevelImportJob.GeometrySettings(JsonUtility.ToJson(before))));
        }

        private XRayLevelAnomalyImportPlan Prepare(XRayAssetIndex index)
        {
            return XRayLevelAnomalyImporter.Prepare(index, TARGET, _assetRoot, null);
        }

        private XRayAssetIndex Build()
        {
            XRaySourceConfiguration configuration = _fixture.CreateConfiguration(PROFILE);
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "configs", "spawns" });
            XRaySourcePlan source = Mo2VirtualFileSystem.Plan(configuration);
            Assert.That(source.IsValid, Is.True, string.Join("; ", source.Errors));
            XRayAssetIndex index = XRayAssetIndex.Build(source);
            index.ExtractionFolder = _directory.Combine("ArchiveCache");
            return index;
        }
    }
}
