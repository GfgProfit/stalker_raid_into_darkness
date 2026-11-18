using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Dds;

namespace XRay.Importer.Tests
{
    public sealed class AssetGenerationTests
    {
        private const string BODY = "wpn\\aks74\\body_diff";
        private const string LENS = "wpn\\aks74\\lens";
        private const string MARK = "wpn\\wpn_addon_scope_kobra_mark";
        private const string OTHER_BODY = "wpn\\ak74m\\body_diff";

        private static readonly byte[] RED = { 255, 0, 0 };
        private static readonly byte[] BLUE = { 0, 0, 255 };

        [Test]
        public void ModelGetsHdrpMaterialsWithResolvedTextures()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string model = scope.ImportModel("wpn_test_hud.ogf", CreateModel());
                    string[] materials = scope.FindAssets("t:Material", scope.MaterialsFolder);
                    Assert.AreEqual(3, materials.Length, string.Join(", ", materials));
                    Material body = XRayImportTestScope.GetRendererMaterial(model, 0);
                    Material lens = XRayImportTestScope.GetRendererMaterial(model, 1);
                    Material mark = XRayImportTestScope.GetRendererMaterial(model, 2);
                    Assert.AreEqual(scope.MaterialsFolder + "/body_diff.mat", AssetDatabase.GetAssetPath(body));
                    StringAssert.StartsWith(scope.MaterialsFolder, AssetDatabase.GetAssetPath(lens));
                    StringAssert.StartsWith(scope.MaterialsFolder, AssetDatabase.GetAssetPath(mark));
                    Assert.AreEqual("HDRP/Lit", body.shader.name);
                    var albedo = body.GetTexture("_BaseColorMap") as Texture2D;
                    Assert.IsNotNull(albedo);
                    Assert.AreEqual(scope.TexturesFolder + "/body_diff.png", AssetDatabase.GetAssetPath(albedo));
                    Assert.IsNotNull(body.GetTexture("_NormalMap"));
                    Assert.AreEqual(0.0f, body.GetFloat("_SurfaceType"));
                    Assert.AreEqual(1.0f, lens.GetFloat("_SurfaceType"));
                    Assert.AreEqual(1.0f, mark.GetFloat("_SurfaceType"));
                    Assert.AreEqual(1.0f, mark.GetFloat("_BlendMode"));
                    Assert.IsNotNull(mark.GetTexture("_EmissiveColorMap"));
                    XRayResourceReport report = scope.LoadReport(model);
                    Assert.IsNotNull(report);
                    Assert.IsTrue(report.IsComplete);
                    XRayReportTexture bodyAlbedo = report.Materials[0].Textures[0];
                    Assert.AreEqual("HighMod", bodyAlbedo.Winner.Name);
                    Assert.AreEqual(1, bodyAlbedo.Losers.Count);
                    Assert.AreEqual("LowMod", bodyAlbedo.Losers[0].Name);
                }
            }
        }

        [Test]
        public void ImportedAlbedoPngUsesUnityMipsAndNormalMapIsLinear()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string model = scope.ImportModel("wpn_test_hud.ogf", CreateModel());
                    Material body = XRayImportTestScope.GetRendererMaterial(model, 0);
                    var albedo = (Texture2D)body.GetTexture("_BaseColorMap");
                    var albedoImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(albedo));
                    Assert.IsTrue(albedoImporter.mipmapEnabled);
                    Assert.Greater(albedo.mipmapCount, 1);
                    Assert.IsTrue(albedo.isDataSRGB, albedo.graphicsFormat.ToString());
                    Assert.AreEqual(GraphicsFormat.RGBA_DXT1_SRGB, albedo.graphicsFormat);
                    var normal = (Texture2D)body.GetTexture("_NormalMap");
                    var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal));
                    Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType);
                    Assert.IsFalse(normal.isDataSRGB);
                }
            }
        }

        [TestCase(4100, 4096, 2048, 2048)]
        [TestCase(1280, 512, 1280, 512)]
        public void DdsPngKeepsSourceSizeAndCapsOnlyTheImportedTexture(int width, int height, int importedWidth, int importedHeight)
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                fixture.AddModFile("HighMod", "textures/wpn/aks74/body_diff.dds", XRayTestBinaries.Bc1Solid(width, height, 0, 0, 255));
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string model = scope.ImportModel("wpn_test_hud.ogf", CreateModel());
                    Material material = XRayImportTestScope.GetRendererMaterial(model, 0);
                    var texture = (Texture2D)material.GetTexture("_BaseColorMap");
                    string path = AssetDatabase.GetAssetPath(texture);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
                    Assert.AreEqual(scope.TexturesFolder + "/body_diff.png", path);
                    Assert.AreEqual(width, sourceWidth);
                    Assert.AreEqual(height, sourceHeight);
                    Assert.AreEqual(importedWidth, texture.width);
                    Assert.AreEqual(importedHeight, texture.height);
                    Assert.IsTrue(importer.mipmapEnabled);
                    Assert.IsFalse(importer.ignoreMipmapLimit);
                    Assert.That(texture.mipmapCount, Is.GreaterThan(1));
                }
            }
        }


        [Test]
        public void ImportedDdsAndConvertedPngShareImageOrientation()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                byte[] dds = XRayTestBinaries.Bc1TopBottom(RED, BLUE);
                DdsHeader header = DdsHeader.Parse(dds);
                byte[] srgb = DdsColorSpace.WithDx10Header(dds, header, DdsHeader.DXGI_BC1_UNORM_SRGB);
                string ddsPath = scope.Folder + "/orientation.dds";
                File.WriteAllBytes(ddsPath, srgb);
                AssetDatabase.ImportAsset(ddsPath, ImportAssetOptions.ForceSynchronousImport);
                var ddsImporter = (IHVImageFormatImporter)AssetImporter.GetAtPath(ddsPath);
                ddsImporter.isReadable = true;
                ddsImporter.SaveAndReimport();
                var ddsTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(ddsPath);
                byte[] raw = ddsTexture.GetRawTextureData();
                string pngPath = scope.Folder + "/orientation.png";
                File.WriteAllBytes(pngPath, XRayTextureImporter.EncodeVerifiedPng(dds, header, false));
                AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceSynchronousImport);
                var pngImporter = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                pngImporter.isReadable = true;
                pngImporter.textureCompression = TextureImporterCompression.Uncompressed;
                pngImporter.SaveAndReimport();
                Color pngBottom = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath).GetPixel(0, 0);
                bool ddsFirstRowIsBottomBlock = raw[0] == dds[136] && raw[1] == dds[137];
                bool pngBottomIsBlue = pngBottom.b > 0.9f && pngBottom.r < 0.1f;
                Debug.Log($"[X-Ray probe] DDS raw first block equals file bottom block: {ddsFirstRowIsBottomBlock}; PNG bottom-left pixel: {pngBottom}");
                Assert.IsTrue(pngBottomIsBlue, "PNG conversion must keep the DDS top row at the top of the image.");
                Assert.AreEqual(pngBottomIsBlue, ddsFirstRowIsBottomBlock, "Copied DDS and converted PNG textures must have the same orientation in Unity.");
            }
        }

        [Test]
        public void MissingTextureStillCreatesEditableMaterial()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string model = scope.ImportModel("missing.ogf", XRayTestBinaries.StaticOgf(new[] { new OgfTestMesh("wpn\\does_not_exist", "models\\weapons") }));
                    Material material = XRayImportTestScope.GetRendererMaterial(model, 0);
                    StringAssert.StartsWith(scope.MaterialsFolder, AssetDatabase.GetAssetPath(material));
                    Assert.IsNull(material.GetTexture("_BaseColorMap"));
                    XRayResourceReport report = scope.LoadReport(model);
                    Assert.AreEqual(1, report.CountTextures(XRayResourceStatus.Missing));
                }
            }
        }

        [Test]
        public void ReimportCreatesNoDuplicatesAndNoImportLoop()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string model = scope.ImportModel("wpn_test_hud.ogf", CreateModel());
                    string[] materials = scope.FindAssets(string.Empty, scope.MaterialsFolder);
                    string[] textures = scope.FindAssets(string.Empty, scope.TexturesFolder);
                    Material before = XRayImportTestScope.GetRendererMaterial(model, 0);
                    scope.Reimport(model);
                    scope.Reimport(model);
                    Assert.IsTrue(XRayAssetGenerationCoordinator.Refresh(model, out XRayResourceReport _) == XRayGenerationOutcome.Completed);
                    scope.Settle();
                    CollectionAssert.AreEqual(materials, scope.FindAssets(string.Empty, scope.MaterialsFolder));
                    CollectionAssert.AreEqual(textures, scope.FindAssets(string.Empty, scope.TexturesFolder));
                    Assert.AreEqual(before, XRayImportTestScope.GetRendererMaterial(model, 0));
                    XRayAssetPostprocessor.ProcessedModelCount = 0;
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    Assert.AreEqual(0, XRayAssetPostprocessor.ProcessedModelCount, "The model was imported again without any change.");
                }
            }
        }

        [Test]
        public void UserMaterialChangesArePreserved()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string model = scope.ImportModel("wpn_test_hud.ogf", CreateModel());
                    Material body = XRayImportTestScope.GetRendererMaterial(model, 0);
                    body.SetColor("_BaseColor", Color.red);
                    body.SetFloat("_Smoothness", 0.123f);
                    EditorUtility.SetDirty(body);
                    AssetDatabase.SaveAssets();
                    scope.Reimport(model);
                    XRayAssetGenerationCoordinator.Refresh(model, out XRayResourceReport report);
                    scope.Settle();
                    body = XRayImportTestScope.GetRendererMaterial(model, 0);
                    Assert.AreEqual(Color.red, body.GetColor("_BaseColor"));
                    Assert.AreEqual(0.123f, body.GetFloat("_Smoothness"), 1e-5f);
                    Assert.AreEqual(XRayResourceStatus.Preserved, report.Materials[0].MaterialStatus);
                    scope.Options.Import.PreserveExistingMaterials = false;
                    XRayAssetGenerationCoordinator.Refresh(model, out report);
                    scope.Settle();
                    Assert.AreEqual(Color.white, XRayImportTestScope.GetRendererMaterial(model, 0).GetColor("_BaseColor"));
                    Assert.AreEqual(XRayResourceStatus.Updated, report.Materials[0].MaterialStatus);
                }
            }
        }

        [Test]
        public void ModelsInOneFolderShareTexturesAndMaterials()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string first = scope.ImportModel("first.ogf", XRayTestBinaries.StaticOgf(new[] { new OgfTestMesh(BODY, "models\\weapons") }));
                    string second = scope.ImportModel("second.ogf", XRayTestBinaries.StaticOgf(new[] { new OgfTestMesh(BODY, "models\\weapons") }));
                    Assert.AreEqual(XRayImportTestScope.GetRendererMaterial(first, 0), XRayImportTestScope.GetRendererMaterial(second, 0));
                    Assert.AreEqual(1, scope.FindAssets("t:Material", scope.MaterialsFolder).Length);
                    Assert.AreEqual(1, scope.FindAssets("body_diff t:Texture2D", scope.TexturesFolder).Length - CountNormals(scope));
                }
            }
        }

        [Test]
        public void SameFileNameFromDifferentFoldersIsKeptApart()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string model = scope.ImportModel("pair.ogf", XRayTestBinaries.StaticOgf(new[] { new OgfTestMesh(BODY, "models\\weapons"), new OgfTestMesh(OTHER_BODY, "models\\weapons") }));
                    var first = (Texture2D)XRayImportTestScope.GetRendererMaterial(model, 0).GetTexture("_BaseColorMap");
                    var second = (Texture2D)XRayImportTestScope.GetRendererMaterial(model, 1).GetTexture("_BaseColorMap");
                    Assert.AreNotEqual(AssetDatabase.GetAssetPath(first), AssetDatabase.GetAssetPath(second));
                    Assert.AreEqual(scope.TexturesFolder + "/body_diff.png", AssetDatabase.GetAssetPath(first));
                    Assert.AreEqual(scope.TexturesFolder + "/ak74m_body_diff.png", AssetDatabase.GetAssetPath(second));
                    Assert.AreNotEqual(XRayImportTestScope.GetRendererMaterial(model, 0), XRayImportTestScope.GetRendererMaterial(model, 1));
                }
            }
        }

        [Test]
        public void ProfileChangeUpdatesTextureOnExplicitRefresh()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string model = scope.ImportModel("single.ogf", XRayTestBinaries.StaticOgf(new[] { new OgfTestMesh(BODY, "models\\weapons") }));
                    string texture = scope.TexturesFolder + "/body_diff.png";
                    string guid = AssetDatabase.AssetPathToGUID(texture);
                    Assert.AreEqual("HighMod", XRayAssetMarker.Read(texture).Source);
                    scope.Options.Sources.Mo2Profile = "Reversed";
                    scope.Reimport(model);
                    Assert.AreEqual("HighMod", XRayAssetMarker.Read(texture).Source, "Automatic reimport must not replace imported textures.");
                    XRayAssetGenerationCoordinator.Refresh(model, out XRayResourceReport report);
                    scope.Settle();
                    Assert.AreEqual("LowMod", XRayAssetMarker.Read(texture).Source);
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(texture));
                    Assert.AreEqual(XRayResourceStatus.Updated, report.Materials[0].Textures[0].Status);
                    Assert.AreEqual("Reversed", report.Profile);
                }
            }
        }

        [Test]
        public void ReimportKeepsGeometryAndMaterialBindingsStable()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string model = scope.ImportModel("wpn_test_hud.ogf", CreateModel());
                    List<string> before = DescribeModel(model);
                    scope.Reimport(model);
                    CollectionAssert.AreEqual(before, DescribeModel(model));
                }
            }
        }

        [Test]
        public void MigratedMonolithMaterialKeepsGuidAndBindingWhenResourcesRefresh()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    string model = scope.ImportModel("migrated.ogf", XRayTestBinaries.StaticOgf(new[] { new OgfTestMesh(BODY, "models\\weapons") }));
                    Material material = XRayImportTestScope.GetRendererMaterial(model, 0);
                    string identity = MaterialIdentity(material);
                    List<string> remaps = DescribeMaterialRemaps(model);
                    string[] materials = scope.FindAssets("t:Material", scope.MaterialsFolder);
                    MonolithMaterialConfigurator.ConfigurePreservingSurface(material, null, material.GetTexture("_BaseColorMap") as Texture2D, null, null, true);
                    material.SetFloat("_MonolithEnvironmentMode", 1.0f);
                    material.EnableKeyword("_MONOLITH_ORIGINAL_ENVIRONMENT");
                    AssetDatabase.SaveAssetIfDirty(material);
                    scope.Options.Import.MaterialGenerationMode = XRayMaterialGenerationMode.MonolithHdrp;
                    scope.Options.Import.PreserveExistingMaterials = false;
                    XRayAssetGenerationCoordinator.Process(model, scope.Options, true, out _);
                    scope.Settle();
                    material = AssertPersistentMaterialBinding(model, scope, identity, remaps, materials, "Migrated material refresh");
                    Assert.That(material.shader.name, Is.EqualTo(MonolithMaterialConfigurator.SHADER_NAME));
                    Assert.That(material.GetTexture("_MonolithBump"), Is.Not.Null);
                    Assert.That(material.GetFloat("_MonolithEnvironmentMode"), Is.EqualTo(1.0f));
                    Assert.That(material.IsKeywordEnabled("_MONOLITH_ORIGINAL_ENVIRONMENT"), Is.True);
                    scope.Reimport(model);
                    material = AssertPersistentMaterialBinding(model, scope, identity, remaps, materials, "Migrated material reimport");
                    Assert.That(material.GetFloat("_MonolithEnvironmentMode"), Is.EqualTo(1.0f));
                    Assert.That(material.IsKeywordEnabled("_MONOLITH_ORIGINAL_ENVIRONMENT"), Is.True);
                }
            }
        }

        [TestCase("models\\model_aref", 0.0f, true, false)]
        [TestCase("models\\model_fur", 0.0f, true, true)]
        [TestCase("models\\glass", 1.0f, false, false)]
        public void MonolithGenerationPreservesSurfaceStateWhenResourcesRefresh(string shader, float surfaceType, bool alphaTest, bool doubleSided)
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    scope.Options.Import.MaterialGenerationMode = XRayMaterialGenerationMode.MonolithHdrp;
                    string model = scope.ImportModel("surface_monolith.ogf", XRayTestBinaries.StaticOgf(new[] { new OgfTestMesh(BODY, shader) }));
                    Material material = XRayImportTestScope.GetRendererMaterial(model, 0);
                    Assert.That(material.shader.name, Is.EqualTo(MonolithMaterialConfigurator.SHADER_NAME));
                    Assert.That(material.GetFloat("_SurfaceType"), Is.EqualTo(surfaceType));
                    Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.EqualTo(surfaceType > 0.0f));
                    Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.EqualTo(alphaTest));
                    Assert.That(material.IsKeywordEnabled("_DOUBLESIDED_ON"), Is.EqualTo(doubleSided));
                    Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo(surfaceType > 0.0f ? 10.0f : 0.0f));
                    int queue = material.renderQueue;
                    string identity = MaterialIdentity(material);
                    List<string> remaps = DescribeMaterialRemaps(model);
                    string[] materials = scope.FindAssets("t:Material", scope.MaterialsFolder);
                    scope.Options.Import.PreserveExistingMaterials = false;
                    XRayAssetGenerationCoordinator.Process(model, scope.Options, true, out _);
                    scope.Settle();
                    AssertPersistentMaterialBinding(model, scope, identity, remaps, materials, shader + " refresh");
                    scope.Reimport(model);
                    material = AssertPersistentMaterialBinding(model, scope, identity, remaps, materials, shader + " reimport");
                    Assert.That(material.GetFloat("_SurfaceType"), Is.EqualTo(surfaceType));
                    Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.EqualTo(surfaceType > 0.0f));
                    Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.EqualTo(alphaTest));
                    Assert.That(material.IsKeywordEnabled("_DOUBLESIDED_ON"), Is.EqualTo(doubleSided));
                    Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo(surfaceType > 0.0f ? 10.0f : 0.0f));
                    Assert.That(material.renderQueue, Is.EqualTo(queue));
                }
            }
        }

        public static Mo2Fixture CreateFixture(TestDirectory directory)
        {
            var fixture = new Mo2Fixture(directory);
            fixture.WriteIni("Main");
            fixture.AddProfile("Main", "+HighMod", "-DisabledMod", "+LowMod");
            fixture.AddProfile("Reversed", "+LowMod", "+HighMod");
            fixture.AddModFile("HighMod", "textures/wpn/aks74/body_diff.dds", XRayTestBinaries.Bc1Solid(8, 8, 255, 0, 0));
            fixture.AddModFile("LowMod", "textures/wpn/aks74/body_diff.dds", XRayTestBinaries.Bc1Solid(8, 8, 0, 0, 255));
            fixture.AddModFile("DisabledMod", "textures/wpn/aks74/body_diff.dds", XRayTestBinaries.Bc1Solid(8, 8, 0, 255, 0));
            fixture.AddBaseFile("textures/wpn/aks74/body_diff.thm", XRayTestBinaries.Thm("wpn\\aks74\\body_diff_bump", 0));
            fixture.AddBaseFile("textures/wpn/aks74/body_diff_bump.dds", XRayTestBinaries.Bc3Solid(8, 8, 128, 255, 128, 128));
            fixture.AddBaseFile("textures/wpn/aks74/body_diff_bump#.dds", XRayTestBinaries.Bc1Solid(8, 8, 128, 128, 128));
            fixture.AddBaseFile("textures/wpn/aks74/lens.dds", XRayTestBinaries.Bc3Solid(4, 4, 20, 20, 20, 100));
            fixture.AddBaseFile("textures/wpn/wpn_addon_scope_kobra_mark.dds", XRayTestBinaries.Dxt3Solid(4, 4, 255, 0, 0, 15));
            fixture.AddBaseFile("textures/wpn/ak74m/body_diff.dds", XRayTestBinaries.Bc1Solid(4, 4, 0, 255, 0));
            return fixture;
        }

        public static byte[] CreateModel()
        {
            return XRayTestBinaries.StaticOgf(new[] { new OgfTestMesh(BODY, "models\\weapons"), new OgfTestMesh(LENS, "models\\transparent"), new OgfTestMesh(MARK, "models\\collimator") });
        }

        private static int CountNormals(XRayImportTestScope scope)
        {
            return scope.FindAssets("body_diff_bump_normal t:Texture2D", scope.TexturesFolder).Length;
        }

        private static string MaterialIdentity(Material material)
        {
            Assert.That(material, Is.Not.Null);
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId), Is.True);
            return $"{AssetDatabase.GetAssetPath(material)}|{guid}|{localId}";
        }

        private static List<string> DescribeMaterialRemaps(string model)
        {
            var description = new List<string>();
            foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, Object> pair in AssetImporter.GetAtPath(model).GetExternalObjectMap())
            {
                if (pair.Value is Material material)
                {
                    description.Add($"{pair.Key.name}:{MaterialIdentity(material)}");
                }
            }

            description.Sort(System.StringComparer.Ordinal);
            return description;
        }

        private static Material AssertPersistentMaterialBinding(string model, XRayImportTestScope scope, string identity, List<string> remaps, string[] materials, string stage)
        {
            Material material = XRayImportTestScope.GetRendererMaterial(model, 0);
            string actual = MaterialIdentity(material);
            TestContext.WriteLine($"{stage}: Before={identity}; After={actual}");
            Assert.That(actual, Is.EqualTo(identity), stage + " changed material path, GUID or local file ID.");
            CollectionAssert.AreEqual(remaps, DescribeMaterialRemaps(model), stage + " changed importer material remaps.");
            CollectionAssert.AreEqual(materials, scope.FindAssets("t:Material", scope.MaterialsFolder), stage + " changed the material asset set.");
            return material;
        }

        private static List<string> DescribeModel(string model)
        {
            var description = new List<string>();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(model);
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                description.Add($"{filter.name}:{filter.sharedMesh.vertexCount}:{filter.sharedMesh.triangles.Length}:{AssetDatabase.GetAssetPath(renderer.sharedMaterial)}");
            }

            return description;
        }
    }
}
