using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class MonolithSourceBindingTests
    {
        [Test]
        public void DetailChainUsesIndependentEffectiveThmAndTextureWinners()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                Mo2Fixture fixture = Fixture(scope.Directory);
                fixture.AddModFile("High", "textures/base.thm", Thm("base_bump", 3, XRayTextureDescription.FLAG_BUMP_DETAIL, "detail/surface", 12.0f));
                fixture.AddModFile("High", "textures/detail/surface.thm", Thm("detail/normal", 2, 0, string.Empty, 1.0f));
                fixture.AddModFile("Low", "textures/detail/surface.thm", Thm("wrong", 2, 0, string.Empty, 1.0f));
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    var record = new XRayLevelImportRecord();
                    var library = new XRayLevelTextureLibrary(Build(fixture), "x", record, true, true);
                    XRayLevelTextureSet set = library.Request("base", true, "fixture");
                    library.ImportAll();
                    scope.Settle();
                    Assert.AreEqual(12.0, set.Description.DetailScale);
                    Assert.AreEqual(XRayBumpMode.UseParallax, set.Description.BumpMode);
                    Assert.AreEqual("High", set.DetailResolution.Diffuse.Winner.Origin);
                    Assert.AreEqual("Low", set.DetailResolution.Bump.Winner.Origin);
                    Assert.AreEqual("detail/normal", set.DetailResolution.Description.BumpName);
                    Assert.IsNotNull(XRayLevelTextureLibrary.Load(set.CorrectionPath));
                    Assert.IsNotNull(XRayLevelTextureLibrary.Load(set.DetailPath));
                    Assert.IsNotNull(XRayLevelTextureLibrary.Load(set.DetailBumpPath));
                    Texture2D correction = XRayLevelTextureLibrary.Load(set.DetailCorrectionPath);
                    Assert.AreEqual(1, correction.width);
                    Assert.IsFalse(correction.isDataSRGB);
                    StringAssert.EndsWith(".png", set.DetailCorrectionPath);
                    Assert.IsTrue(record.Dependencies.Any(dependency => dependency.Role == "Detail THM metadata" && dependency.SearchPath == "textures/detail/surface.thm"));
                    XRayLevelSourceBinding binding = Binding(set, "LM");
                    Assert.AreEqual(2, binding.RecommendedDetail);
                    Assert.AreEqual(2, binding.RecommendedParallax);
                    Assert.AreEqual(12.0f, binding.DetailScale);
                    using (var material = new MaterialScope(set))
                    {
                        MonolithMaterialConfigurator.ConfigureSourceBindings(material.Value, binding);
                        Assert.AreSame(XRayLevelTextureLibrary.Load(set.CorrectionPath), material.Value.GetTexture(XRayLevelSourceBindings.CORRECTION));
                        Assert.AreEqual(1.0f, material.Value.GetFloat(XRayLevelSourceBindings.HAS_CORRECTION));
                        Assert.AreEqual(0.0f, material.Value.GetFloat("_MonolithSourceProfile"));
                        Assert.AreEqual(0.0f, material.Value.GetFloat("_MonolithSourceDetail"));
                        Assert.AreEqual(0.0f, material.Value.GetFloat("_MonolithSourceParallax"));
                    }
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [TestCase("LM", 3, false, 2)]
        [TestCase("MODEL", 3, false, 1)]
        [TestCase("D_TREE", 3, false, 1)]
        [TestCase("LM_AREF", 2, false, 0)]
        [TestCase("LM_AREF", 3, false, 2)]
        [TestCase("LM", 3, true, 0)]
        [TestCase("unknown", 3, false, 0)]
        public void RecommendedParallaxFollowsBlenderAndSourceFlags(string blender, int bumpMode, bool flora, int expected)
        {
            XRayTextureDescription description = XRayThmReader.Read(Thm("base_bump", bumpMode, 0, string.Empty, 1.0f), "fixture");
            XRayLevelSourceBinding binding = XRayLevelSourceBindings.Resolve(description, blender, blender.Contains("AREF") ? XRayLevelSurfaceKind.AlphaTest : XRayLevelSurfaceKind.Opaque, flora, false, true, 0.0f, "correction.png", null, null, null);
            Assert.AreEqual(expected, binding.RecommendedParallax);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PackedUncompressedChannelsRemainLinear(bool rgba)
        {
            using (var scope = new XRayImportTestScope(null))
            {
                Mo2Fixture fixture = Fixture(scope.Directory);
                byte[] bytes = XRayTestBinaries.Bgra32(1, 1, new byte[] { 37, 89, 151, 213 });
                if (rgba)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(0x000000FFu), 0, bytes, 92, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(0x00FF0000u), 0, bytes, 100, 4);
                    bytes[128] = 37;
                    bytes[130] = 151;
                }

                fixture.AddModFile("High", "textures/packed.dds", bytes);
                var importer = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>());
                XRayTextureAssetResult result = importer.ImportPacked(new XRayTextureResolver(Build(fixture)).ResolveFile("textures/packed.dds"), scope.TexturesFolder);
                importer.ImportPending();
                scope.Settle();
                Texture2D imported = XRayLevelTextureLibrary.Load(result.AssetPath);
                Assert.IsFalse(imported.isDataSRGB);
                Assert.AreEqual(TextureImporterType.Default, ((TextureImporter)AssetImporter.GetAtPath(result.AssetPath)).textureType);
                var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                try
                {
                    Assert.IsTrue(readback.LoadImage(File.ReadAllBytes(result.AssetPath)));
                    Assert.AreEqual(new Color32(37, 89, 151, 213), (Color32)readback.GetPixel(0, 0));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(readback);
                }
            }
        }

        [Test]
        public void MissingCorrectionAndIncompleteDetailDisableOnlyUnsupportedOperations()
        {
            XRayTextureDescription description = XRayThmReader.Read(Thm("base_bump", 3, XRayTextureDescription.FLAG_BUMP_DETAIL, "detail/surface", 8.0f), "fixture");
            XRayLevelSourceBinding binding = XRayLevelSourceBindings.Resolve(description, "LM", XRayLevelSurfaceKind.Opaque, false, false, true, 0.0f, null, "detail.png", "detail_bump.png", null);
            Assert.AreEqual(0, binding.RecommendedParallax);
            Assert.AreEqual(1, binding.RecommendedDetail);
            Assert.IsNull(binding.DetailBumpPath);
            Assert.IsTrue(binding.Reasons.Any(reason => reason.Contains("bump#")));
            XRayTextureDescription unflagged = XRayThmReader.Read(Thm("base_bump", 2, 0, "detail/surface", 8.0f), "fixture");
            Assert.IsFalse(XRayLevelSourceBindings.DeclaresDetail(unflagged));
        }

        [Test]
        public void InvalidDetailScaleDoesNotReachMaterialBindings()
        {
            XRayTextureDescription description = XRayThmReader.Read(Thm("base_bump", 2, XRayTextureDescription.FLAG_DIFFUSE_DETAIL, "detail/surface", float.NaN), "fixture");
            XRayLevelSourceBinding binding = XRayLevelSourceBindings.Resolve(description, "LM", XRayLevelSurfaceKind.Opaque, false, false, true, 0.0f, null, "detail.png", null, null);
            Assert.AreEqual(0, binding.RecommendedDetail);
            Assert.IsNull(binding.DetailPath);
        }

        [Test]
        public void ManualOverrideIncludingClearedSlotIsStickyAcrossChangingSource()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                XRayLevelTextureSet set = Import(scope);
                using (var material = new MaterialScope(set))
                {
                    XRayLevelSourceBinding binding = Binding(set, "LM");
                    MonolithMaterialConfigurator.ConfigureSourceBindings(material.Value, binding);
                    Assert.IsFalse(XRayLevelSourceBindings.Plan(material.Value, binding).Modifies);
                    material.Value.SetTexture(XRayLevelSourceBindings.CORRECTION, null);
                    Texture manualDetail = XRayLevelTextureLibrary.Load(set.CorrectionPath);
                    material.Value.SetTexture(XRayLevelSourceBindings.DETAIL, manualDetail);
                    material.Value.SetFloat(XRayLevelSourceBindings.DETAIL_SCALE, 77.0f);
                    material.Value.SetFloat("_MonolithSourceProfile", 0.0f);
                    material.Value.SetVector("_MonolithPom", new Vector4(40, 10, 0.04f, 0.2f));
                    MonolithMaterialConfigurator.ConfigureSourceBindings(material.Value, binding);
                    binding.CorrectionPath = null;
                    binding.DetailScale = 77.0f;
                    MonolithMaterialConfigurator.ConfigureSourceBindings(material.Value, binding);
                    binding.CorrectionPath = set.CorrectionPath;
                    binding.DetailScale = 5.0f;
                    XRayLevelBindingPlan plan = MonolithMaterialConfigurator.ConfigureSourceBindings(material.Value, binding);
                    Assert.IsTrue(plan.HasUserOverrides);
                    Assert.IsNull(material.Value.GetTexture(XRayLevelSourceBindings.CORRECTION));
                    Assert.AreSame(manualDetail, material.Value.GetTexture(XRayLevelSourceBindings.DETAIL));
                    Assert.AreEqual(77.0f, material.Value.GetFloat(XRayLevelSourceBindings.DETAIL_SCALE));
                    Assert.AreEqual(0.0f, material.Value.GetFloat(XRayLevelSourceBindings.RECOMMENDED_PARALLAX));
                    Assert.AreEqual(0.0f, material.Value.GetFloat(XRayLevelSourceBindings.HAS_CORRECTION));
                    Assert.AreEqual(new Vector4(40, 10, 0.04f, 0.2f), material.Value.GetVector("_MonolithPom"));
                    Assert.IsFalse(XRayLevelSourceBindings.Plan(material.Value, binding).Modifies);
                }
            }
        }

        [Test]
        public void ClearingHeightAndDetailCorrectionUpdatesInspectorSafetyFlags()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                XRayLevelTextureSet set = Import(scope);
                using (var material = new MaterialScope(set))
                {
                    MonolithMaterialConfigurator.ConfigureSourceBindings(material.Value, Binding(set, "LM"));
                    Assert.AreEqual(1.0f, material.Value.GetFloat(XRayLevelSourceBindings.HAS_CORRECTION));
                    Assert.AreEqual(1.0f, material.Value.GetFloat(XRayLevelSourceBindings.HAS_DETAIL_BUMP));
                    material.Value.SetTexture(XRayLevelSourceBindings.CORRECTION, null);
                    material.Value.SetTexture(XRayLevelSourceBindings.DETAIL_CORRECTION, null);
                    MonolithMaterialInspector.UpdateTexturePresence(material.Value);
                    Assert.AreEqual(0.0f, material.Value.GetFloat(XRayLevelSourceBindings.HAS_CORRECTION));
                    Assert.AreEqual(0.0f, material.Value.GetFloat(XRayLevelSourceBindings.HAS_DETAIL_BUMP));
                    Assert.AreEqual(1.0f, material.Value.GetFloat(XRayLevelSourceBindings.HAS_DETAIL));
                }
            }
        }

        [Test]
        public void GeneratedBindingsFollowSourceAndEmptyLegacySlotsAreNotOverrides()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                XRayLevelTextureSet set = Import(scope);
                using (var material = new MaterialScope(set))
                {
                    XRayLevelSourceBinding binding = Binding(set, "LM");
                    XRayLevelBindingPlan first = MonolithMaterialConfigurator.ConfigureSourceBindings(material.Value, binding);
                    Assert.IsFalse(first.HasUserOverrides);
                    binding.DetailScale = 4.5f;
                    MonolithMaterialConfigurator.ConfigureSourceBindings(material.Value, binding);
                    Assert.AreEqual(4.5f, material.Value.GetFloat(XRayLevelSourceBindings.DETAIL_SCALE));
                    binding.CorrectionPath = null;
                    binding.RecommendedParallax = 0;
                    MonolithMaterialConfigurator.ConfigureSourceBindings(material.Value, binding);
                    Assert.IsNull(material.Value.GetTexture(XRayLevelSourceBindings.CORRECTION));
                    Assert.AreEqual(0.0f, material.Value.GetFloat(XRayLevelSourceBindings.HAS_CORRECTION));
                }
            }
        }

        [Test]
        public void PreserveReimportKeepsMaterialGuidReferencesAndUnrelatedOverrides()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                XRayLevelTextureSet set = Import(scope);
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    var request = new XRayLevelMaterialRequest { ShaderName = "default", Surface = new XRayLevelSurface { Kind = XRayLevelSurfaceKind.Opaque, BlenderClass = "LM", RuleName = "opaque" }, Textures = set };
                    var legacy = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve);
                    Material material = legacy.Get(request);
                    material.SetFloat("_MonolithGlossScale", 0.42f);
                    material.SetFloat("_MonolithSourceProfile", 0.0f);
                    AssetDatabase.SaveAssetIfDirty(material);
                    string path = AssetDatabase.GetAssetPath(material);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    string key = XRayAssetMarker.ReadKey(path);
                    var owner = new GameObject("Material reference fixture");
                    try
                    {
                        MeshRenderer renderer = owner.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = material;
                        var updated = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve, true);
                        Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(updated.Get(request))));
                        Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(renderer.sharedMaterial)));
                        Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
                        Assert.AreEqual(key, XRayAssetMarker.ReadKey(path));
                        Assert.AreEqual(0.42f, material.GetFloat("_MonolithGlossScale"));
                        Assert.IsNotNull(material.GetTexture(XRayLevelSourceBindings.CORRECTION));
                        Assert.IsFalse(XRayLevelSourceBindings.Plan(material, Binding(set, "LM")).Modifies);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(owner);
                    }
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [Test]
        public void ResourceOnlyRefreshBacksUpExactAssetsAndIsIdempotent()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                string root = XRayLevelPaths.ANOMALY_ROOT + "/Phase04Tests";
                Assert.IsFalse(Directory.Exists(root) || File.Exists(root + ".meta"), "The dedicated Phase04Tests fixture root must not exist before the test.");
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(root);
                try
                {
                    Mo2Fixture fixture = Fixture(scope.Directory);
                    fixture.AddModFile("High", "textures/base.thm", Thm("base_bump", 3, 0, string.Empty, 1.0f));
                    XRayAssetIndex index = Build(fixture);
                    var textures = new XRayLevelTextureLibrary(index, "x", new XRayLevelImportRecord(), true, true);
                    XRayLevelTextureSet set = textures.Request("base", true, "fixture");
                    textures.ImportAll();
                    scope.Settle();
                    var request = new XRayLevelMaterialRequest { ShaderName = "default", Surface = new XRayLevelSurface { Kind = XRayLevelSurfaceKind.Opaque, BlenderClass = "LM", RuleName = "opaque" }, Textures = set };
                    Material material = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve).Get(request);
                    material.SetFloat(XRayLevelSourceBindings.DETAIL_SCALE, 17.0f);
                    AssetDatabase.SaveAssetIfDirty(material);
                    string path = AssetDatabase.GetAssetPath(material);
                    string before = XRayLevelSourceRefresh.HashFile(path);
                    string meta = XRayLevelSourceRefresh.HashFile(path + ".meta");
                    Texture albedo = material.GetTexture("_BaseColorMap");
                    Texture bump = material.GetTexture("_MonolithBump");
                    fixture.AddModFile("High", "textures/base.thm", Thm("base_bump", 3, XRayTextureDescription.FLAG_BUMP_DETAIL, "detail/surface", 12.0f));
                    index = Build(fixture);
                    var refresh = new XRayLevelSourceRefresh(index, root);
                    XRayLevelSourceRefreshEntry entry = refresh.Inspect(path, "x");
                    Assert.IsTrue(entry.CanApply, entry.Error);
                    Assert.IsTrue(entry.Dependencies.Any(dependency => dependency.CreatesAsset));
                    Assert.AreEqual(before, XRayLevelSourceRefresh.HashFile(path));
                    string archive = Path.Combine(XRayLevelPaths.ArchiveRoot, "GraphicsPort/Anomaly_SSS23_5_ES/Phase04_ImporterMaterialBindings/RegressionFixtures", Guid.NewGuid().ToString("N"));
                    XRayLevelSourceRefresh.WritePreview(new[] { entry }, Path.Combine(archive, "DryRun.csv"));
                    refresh.Apply(new[] { entry }, archive);
                    Assert.AreEqual(before, XRayLevelSourceRefresh.HashFile(Path.Combine(archive, "Backups", entry.Guid + ".mat")));
                    Assert.AreEqual(meta, XRayLevelSourceRefresh.HashFile(path + ".meta"));
                    Assert.AreEqual(entry.Guid, AssetDatabase.AssetPathToGUID(path));
                    Assert.AreSame(albedo, material.GetTexture("_BaseColorMap"));
                    Assert.AreSame(bump, material.GetTexture("_MonolithBump"));
                    Assert.AreEqual(17.0f, material.GetFloat(XRayLevelSourceBindings.DETAIL_SCALE));
                    Assert.IsNotNull(material.GetTexture(XRayLevelSourceBindings.CORRECTION));
                    XRayLevelSourceRefreshEntry second = refresh.Inspect(path, "x");
                    Assert.IsFalse(second.Plan.Modifies);
                    Assert.IsFalse(second.Dependencies.Any(dependency => dependency.CreatesAsset));
                    string after = XRayLevelSourceRefresh.HashFile(path);
                    refresh.Apply(new[] { second }, Path.Combine(archive, "Second"));
                    Assert.AreEqual(after, XRayLevelSourceRefresh.HashFile(path));
                    material.SetFloat(XRayLevelSourceBindings.DETAIL_SCALE, 18.0f);
                    Assert.Throws<InvalidOperationException>(() => refresh.Apply(new[] { second }, Path.Combine(archive, "Stale")));
                    string changedSource = fixture.AddModFile("Low", "textures/base_bump#.dds", XRayTestBinaries.Bc3Solid(4, 4, 64, 128, 128, 240));
                    File.SetLastWriteTimeUtc(changedSource, DateTime.UtcNow.AddSeconds(1));
                    XRayLevelSourceRefreshEntry stale = new XRayLevelSourceRefresh(Build(fixture), root).Inspect(path, "x");
                    Assert.IsFalse(stale.CanApply);
                    Assert.IsNull(stale.Binding.CorrectionPath);
                    Assert.IsNull(stale.ProjectedBinding.CorrectionPath);
                    Assert.AreEqual(0, stale.Binding.RecommendedParallax);
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                    AssetDatabase.DeleteAsset(root);
                }
            }
        }

        [Test]
        public void TrueStalkerAndUnrelatedRootsCannotEnterSavedRefresh()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = Fixture(directory);
                Assert.Throws<ArgumentException>(() => new XRayLevelSourceRefresh(Build(fixture), XRayLevelPaths.ROOT));
            }
        }

        [Test]
        public void DetailDependenciesCanComeFromPackedGameArchives()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                Mo2Fixture fixture = Fixture(scope.Directory);
                Directory.CreateDirectory(scope.Directory.Combine("game/gamedata"));
                byte[] archive = XRayArchiveTestWriter.Write(new XRayArchiveTestEntry("textures/detail/normal#.dds", XRayTestBinaries.Bgra32(1, 1, new byte[] { 127, 128, 129, 42 }), true));
                scope.Directory.WriteFile("game/db/textures/texture.db0", archive);
                File.Delete(scope.Directory.Combine("MO2/mods/Low/gamedata/textures/detail/normal#.dds"));
                XRaySourceConfiguration configuration = fixture.CreateConfiguration("Profile");
                configuration.Mode = XRaySourceMode.Mo2Effective;
                configuration.GameFolder = scope.Directory.Combine("game");
                XRayAssetIndex index = XRayAssetIndex.Build(Mo2VirtualFileSystem.Plan(configuration));
                XRayTextureResolution detail = new XRayTextureResolver(index).Resolve("detail/surface");
                Assert.IsTrue(detail.BumpCorrection.Winner.IsArchive);
                CollectionAssert.AreEqual(XRayTestBinaries.Bgra32(1, 1, new byte[] { 127, 128, 129, 42 }), detail.BumpCorrection.Winner.ReadAllBytes());
            }
        }

        private static Mo2Fixture Fixture(TestDirectory directory)
        {
            var fixture = new Mo2Fixture(directory);
            fixture.WriteIni("Profile");
            fixture.AddProfile("Profile", "+High", "+Low");
            fixture.AddModFile("High", "textures/base.dds", XRayTestBinaries.Bc1Solid(4, 4, 120, 100, 80));
            fixture.AddModFile("High", "textures/base.thm", Thm("base_bump", 3, XRayTextureDescription.FLAG_BUMP_DETAIL, "detail/surface", 12.0f));
            fixture.AddModFile("Low", "textures/base_bump.dds", XRayTestBinaries.Bc3Solid(4, 4, 50, 255, 128, 128));
            fixture.AddModFile("Low", "textures/base_bump#.dds", XRayTestBinaries.Bc3Solid(4, 4, 128, 128, 128, 180));
            fixture.AddModFile("High", "textures/detail/surface.dds", XRayTestBinaries.Bc1Solid(4, 4, 100, 110, 120));
            fixture.AddModFile("High", "textures/detail/surface.thm", Thm("detail/normal", 2, 0, string.Empty, 1.0f));
            fixture.AddModFile("Low", "textures/detail/normal.dds", XRayTestBinaries.Bc3Solid(4, 4, 128, 255, 128, 128));
            fixture.AddModFile("Low", "textures/detail/normal#.dds", XRayTestBinaries.Bgra32(1, 1, new byte[] { 127, 128, 129, 42 }));
            var header = new byte[176];
            byte[] classId = Encoding.ASCII.GetBytes("LM      ");
            Array.Reverse(classId);
            Buffer.BlockCopy(classId, 0, header, 0, classId.Length);
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("default"), 0, header, 8, 7);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(2u);
                writer.Write(header.Length + 8);
                writer.Write(0u);
                writer.Write(header.Length);
                writer.Write(header);
                fixture.AddModFile("High", "shaders.xr", stream.ToArray());
            }

            return fixture;
        }

        private static XRayAssetIndex Build(Mo2Fixture fixture)
        {
            XRaySourceConfiguration configuration = fixture.CreateConfiguration("Profile");
            configuration.IndexScope.Add("shaders.xr");
            return XRayAssetIndex.Build(Mo2VirtualFileSystem.Plan(configuration));
        }

        private static XRayLevelTextureSet Import(XRayImportTestScope scope)
        {
            string previous = XRayLevelPaths.Root;
            XRayLevelPaths.UseRoot(scope.Folder);
            try
            {
                var library = new XRayLevelTextureLibrary(Build(Fixture(scope.Directory)), "x", new XRayLevelImportRecord(), true, true);
                XRayLevelTextureSet set = library.Request("base", true, "fixture");
                library.ImportAll();
                scope.Settle();
                return set;
            }
            finally
            {
                XRayLevelPaths.UseRoot(previous);
            }
        }

        private static XRayLevelSourceBinding Binding(XRayLevelTextureSet set, string blender)
        {
            return XRayLevelSourceBindings.Resolve(set.Description, blender, XRayLevelSurfaceKind.Opaque, false, false, true, 0.0f, set.CorrectionPath, set.DetailPath, set.DetailBumpPath, set.DetailCorrectionPath);
        }

        private static byte[] Thm(string bump, int mode, uint flags, string detail, float scale)
        {
            using (var input = new BinaryReader(new MemoryStream(XRayTestBinaries.Thm(bump, flags))))
            using (var stream = new MemoryStream())
            using (var output = new BinaryWriter(stream))
            {
                while (input.BaseStream.Position < input.BaseStream.Length)
                {
                    uint id = input.ReadUInt32();
                    byte[] payload = input.ReadBytes(input.ReadInt32());
                    if (id == 0x0815)
                    {
                        byte[] name = Encoding.ASCII.GetBytes(detail + "\0");
                        payload = name.Concat(BitConverter.GetBytes(scale)).ToArray();
                    }

                    if (id == 0x0817)
                    {
                        Buffer.BlockCopy(BitConverter.GetBytes(mode), 0, payload, 4, 4);
                    }

                    output.Write(id);
                    output.Write(payload.Length);
                    output.Write(payload);
                }

                return stream.ToArray();
            }
        }

        private sealed class MaterialScope : IDisposable
        {
            public MaterialScope(XRayLevelTextureSet set)
            {
                Value = new Material(Shader.Find(MonolithMaterialConfigurator.SHADER_NAME));
                MonolithMaterialConfigurator.Configure(Value, set.Description, XRayLevelTextureLibrary.Load(set.AlbedoPath), XRayLevelTextureLibrary.Load(set.BumpPath), null, true);
            }

            public Material Value { get; }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Value);
            }
        }
    }
}
