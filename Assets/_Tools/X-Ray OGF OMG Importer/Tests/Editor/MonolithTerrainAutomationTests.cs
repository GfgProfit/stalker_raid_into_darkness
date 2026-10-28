using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class MonolithTerrainAutomationTests
    {
        private static readonly Dictionary<bool, Dictionary<string, byte[]>> SOURCES = new Dictionary<bool, Dictionary<string, byte[]>>();

        [TestCase(false, XRayTerrainImportMode.Preserve)]
        [TestCase(false, XRayTerrainImportMode.Bind)]
        [TestCase(false, XRayTerrainImportMode.EnableVerifiedAnomaly)]
        [TestCase(true, XRayTerrainImportMode.Preserve)]
        [TestCase(true, XRayTerrainImportMode.Bind)]
        [TestCase(true, XRayTerrainImportMode.EnableVerifiedAnomaly)]
        public void FutureImportBindsActualSourceContractAndReusesAssets(bool trueStalker, XRayTerrainImportMode mode)
        {
            using (var scope = new XRayImportTestScope(null))
            {
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    Mo2Fixture fixture = Fixture(scope, trueStalker);
                    XRayLevelMaterialRequest request = Import(scope, fixture);
                    bool enabled = !trueStalker && mode == XRayTerrainImportMode.EnableVerifiedAnomaly;
                    var record = new XRayLevelImportRecord();
                    Material material = new XRayLevelMaterialLibrary(record, XRayLevelMaterialUpdateMode.Preserve, !trueStalker, mode).Get(request);
                    Assert.AreEqual(trueStalker ? XRayTerrainSourceProfile.TrueStalker : XRayTerrainSourceProfile.AnomalyAtmospherics, request.Terrain.Source.Profile);
                    Assert.AreEqual(enabled ? 1.0f : 0.0f, material.GetFloat("_MonolithSourceTerrain"));
                    Assert.AreEqual(enabled ? 1.0f : 0.0f, material.GetFloat("_MonolithSourceProfile"));
                    Assert.AreEqual(enabled ? 1.0f : 0.0f, material.GetFloat("_MonolithTerrainParallax"));
                    Assert.AreEqual(enabled ? 1.0f : 0.0f, material.GetFloat("_MonolithTerrainOcclusion"));
                    foreach (string channel in new[] { "R", "G", "B", "A" })
                    {
                        Assert.IsNotNull(material.GetTexture("_MonolithTerrainDetail" + channel));
                        Assert.IsNotNull(material.GetTexture("_MonolithTerrainBump" + channel));
                        Assert.AreEqual(!trueStalker, material.GetTexture("_MonolithTerrainHeight" + channel) != null);
                        Assert.AreEqual(!trueStalker, material.GetTexture("_MonolithTerrainSourceColor" + channel) != null);
                    }
                    Assert.IsNotNull(material.GetTexture("_MonolithTerrainMask"));
                    Assert.AreEqual(!trueStalker, request.Terrain.Source.VerifiedMid);
                    Assert.AreEqual(!trueStalker, request.Terrain.Source.VerifiedLow);
                    if (!trueStalker) Assert.IsNotNull(material.GetTexture("_MonolithTerrainLodAtlas"));
                    AssetDatabase.SaveAssetIfDirty(material);
                    string path = AssetDatabase.GetAssetPath(material);
                    string before = XRayLevelSourceRefresh.HashFile(path);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    Dictionary<string, string> files = Snapshot(scope.Folder);
                    request = Import(scope, fixture);
                    Material second = new XRayLevelMaterialLibrary(record, XRayLevelMaterialUpdateMode.Preserve, !trueStalker, mode).Get(request);
                    AssetDatabase.SaveAssetIfDirty(second);
                    Assert.AreEqual(path, AssetDatabase.GetAssetPath(second));
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
                    Assert.AreEqual(before, XRayLevelSourceRefresh.HashFile(path));
                    CollectionAssert.AreEquivalent(files, Snapshot(scope.Folder));
                    Assert.IsFalse(XRayTerrainMaterialBindings.Plan(second, request.Terrain, mode).Modifies);
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [TestCase(XRayTerrainQuality.Medium)]
        [TestCase(XRayTerrainQuality.Low)]
        public void ImportOperationSelectsVerifiedQualityOnce(XRayTerrainQuality quality)
        {
            using (var scope = new XRayImportTestScope(null))
            {
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    XRayLevelMaterialRequest request = Import(scope, Fixture(scope, false));
                    Material material = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve, true, XRayTerrainImportMode.EnableVerifiedAnomaly, quality).Get(request);
                    Assert.AreEqual((float)quality, material.GetFloat("_MonolithTerrainQuality"));
                    Assert.AreEqual(1.0f, material.GetFloat("_MonolithSourceTerrain"));
                    material.SetFloat("_MonolithTerrainQuality", 0.0f);
                    XRayTerrainMaterialBindings.Apply(material, request.Terrain, XRayTerrainMaterialBindings.Plan(material, request.Terrain, XRayTerrainImportMode.EnableVerifiedAnomaly, false, quality));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithTerrainQuality"));
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [Test]
        public void LegacyTerrainDefaultsAreNotClassifiedAsManualOverrides()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                var material = new Material(Shader.Find("HDRP/MonolithLit"));
                try
                {
                    XRayLevelMaterialRequest request = Import(scope, Fixture(scope, false));
                    Assert.AreEqual(Vector4.one, material.GetVector("_MonolithTerrainColorSrgb"));
                    XRayLevelBindingPlan plan = XRayTerrainMaterialBindings.Plan(material, request.Terrain, XRayTerrainImportMode.Bind);
                    Assert.AreNotEqual(XRayLevelBindingOwnership.UserOverride, plan.Changes.Find(change => change.Property == "_MonolithTerrainColorSrgb").Ownership);
                    XRayTerrainMaterialBindings.Apply(material, request.Terrain, plan);
                    Assert.AreEqual(Vector4.zero, material.GetVector("_MonolithTerrainColorSrgb"));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceProfile"));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceTerrain"));
                    Assert.IsFalse(XRayTerrainMaterialBindings.Plan(material, request.Terrain, XRayTerrainImportMode.Bind).Modifies);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(material);
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [Test]
        public void ShaderNameWithoutTerrainBlenderCannotActivateSourceRendering()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    XRayLevelMaterialRequest request = Import(scope, Fixture(scope, false), true);
                    Assert.AreEqual(XRayTerrainSourceProfile.Unknown, request.Terrain.Source.Profile);
                    Material material = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve, true, XRayTerrainImportMode.EnableVerifiedAnomaly).Get(request);
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceProfile"));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceTerrain"));
                    Assert.IsNull(material.GetTexture("_MonolithTerrainHeightR"));
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [Test]
        public void SourceParametersFollowFsGameAndKeepManualOverrides()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    Mo2Fixture fixture = Fixture(scope, false);
                    string game = scope.Directory.Combine("source");
                    scope.Directory.WriteText("source/fsgame.ltx", "$app_data_root$ = true | false | $fs_root$ | profile_data\\");
                    scope.Directory.WriteText("source/profile_data/user.ltx", "ssfx_terrain_pom (24, 12, 0.03, 0.5)\nssfx_terrain_offset (-0.1, 0.1, 0.2, 0)\nr__tf_aniso 8\nr__tf_mipbias -0.25");
                    XRayLevelMaterialRequest request = Import(scope, fixture, false, game);
                    Material material = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve, true, XRayTerrainImportMode.EnableVerifiedAnomaly).Get(request);
                    Assert.AreEqual(new Vector4(24, 12, 0.03f, 0.5f), material.GetVector("_MonolithTerrainPom"));
                    Assert.AreEqual(new Vector4(-0.1f, 0.1f, 0.2f, 0), material.GetVector("_MonolithTerrainOffset"));
                    string hash = request.Terrain.Parameters.Hash;
                    var manual = new Vector4(18, 11, 0.02f, 0);
                    material.SetVector("_MonolithTerrainPom", manual);
                    AssetDatabase.SaveAssetIfDirty(material);
                    scope.Directory.WriteText("source/profile_data/user.ltx", "ssfx_terrain_pom (16, 15, 0.04, 1)\nssfx_terrain_offset (-0.2, 0.2, 0.3, 0)\nr__tf_aniso 8\nr__tf_mipbias -0.25");
                    request = Import(scope, fixture, false, game);
                    material = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve, true, XRayTerrainImportMode.Bind).Get(request);
                    Assert.AreNotEqual(hash, request.Terrain.Parameters.Hash);
                    Assert.AreEqual(manual, material.GetVector("_MonolithTerrainPom"));
                    Assert.AreEqual(new Vector4(-0.2f, 0.2f, 0.3f, 0), material.GetVector("_MonolithTerrainOffset"));
                    Assert.AreEqual(request.Terrain.Parameters.Hash, material.GetTag("XRayTerrainSettingsSHA256", false));
                    XRayLevelMaterialRequest ts = Import(scope, Fixture(scope, true), false, game);
                    Assert.IsNull(ts.Terrain.Parameters);
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [Test]
        public void InvalidSourceParametersAreNotApplied()
        {
            XRayTerrainSourceParameters parameters = XRayTerrainSourceParameters.Parse("ssfx_terrain_pom (0, 0, 0, 0)\nssfx_terrain_offset (NaN, 0, 0, 0)\nr__tf_aniso 32\nr__tf_mipbias Infinity");
            Assert.IsNull(parameters.Pom);
            Assert.IsNull(parameters.Offset);
            Assert.IsNull(parameters.Anisotropy);
            Assert.IsNull(parameters.MipBias);
        }

        [Test]
        public void TerrainRefreshPreservesNullTextureAndRenderingOverrides()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    Mo2Fixture fixture = Fixture(scope, false);
                    XRayLevelMaterialRequest request = Import(scope, fixture);
                    var library = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve, true, XRayTerrainImportMode.EnableVerifiedAnomaly);
                    Material material = library.Get(request);
                    material.SetTexture("_MonolithTerrainHeightR", null);
                    material.SetTexture("_MonolithTerrainSourceColorG", material.GetTexture("_MonolithTerrainSourceColorB"));
                    material.SetFloat("_MonolithTerrainParallax", 0.0f);
                    material.SetFloat("_MonolithTerrainOcclusion", 0.0f);
                    material.SetFloat("_MonolithTerrainDetailScale", 17.0f);
                    material.SetFloat("_MonolithTerrainQuality", 2.0f);
                    Texture overridden = material.GetTexture("_MonolithTerrainSourceColorG");
                    XRayLevelBindingPlan plan = XRayTerrainMaterialBindings.Plan(material, request.Terrain, XRayTerrainImportMode.EnableVerifiedAnomaly);
                    Assert.IsTrue(plan.HasUserOverrides);
                    XRayTerrainMaterialBindings.Apply(material, request.Terrain, plan);
                    Assert.IsNull(material.GetTexture("_MonolithTerrainHeightR"));
                    Assert.AreSame(overridden, material.GetTexture("_MonolithTerrainSourceColorG"));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithHasTerrainHeight"));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithTerrainParallax"));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithTerrainOcclusion"));
                    Assert.AreEqual(17.0f, material.GetFloat("_MonolithTerrainDetailScale"));
                    Assert.AreEqual(2.0f, material.GetFloat("_MonolithTerrainQuality"));
                    Assert.IsFalse(XRayTerrainMaterialBindings.Plan(material, request.Terrain, XRayTerrainImportMode.EnableVerifiedAnomaly).Modifies);
                    MonolithMaterialInspector.UpdateTexturePresence(material);
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithHasTerrainHeight"));
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ForeignOrChangedShaderCannotActivateAnomalyTerrain(bool trueStalker)
        {
            using (var scope = new XRayImportTestScope(null))
            {
                Mo2Fixture fixture = Fixture(scope, trueStalker);
                if (!trueStalker) fixture.AddModFile("High", "shaders/r3/lmodel.h", new byte[] { 1, 2, 3 });
                XRayTerrainSourceBinding source = XRayTerrainSourceBinding.Resolve(new XRayTextureResolver(Build(fixture)));
                var material = new Material(Shader.Find("HDRP/MonolithLit"));
                try
                {
                    foreach (string property in new[] { "_MonolithSourceProfile", "_MonolithSourceTerrain", "_MonolithTerrainParallax", "_MonolithTerrainOcclusion", "_MonolithTerrainVirtualHeight" }) material.SetFloat(property, 1.0f);
                    var terrain = new XRayLevelTerrainTextures { Source = source };
                    XRayTerrainMaterialBindings.Apply(material, terrain, XRayTerrainMaterialBindings.Plan(material, terrain, XRayTerrainImportMode.EnableVerifiedAnomaly));
                    foreach (string property in new[] { "_MonolithSourceProfile", "_MonolithSourceTerrain", "_MonolithTerrainParallax", "_MonolithTerrainOcclusion", "_MonolithTerrainVirtualHeight", "_MonolithHasTerrainHeight", "_MonolithHasTerrainSourceColor", "_MonolithTerrainMidAvailable", "_MonolithTerrainLowAvailable" }) Assert.AreEqual(0.0f, material.GetFloat(property), property);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }

        [Test]
        public void MissingHeightNeverUsesBumpCorrectionAsTerrainHeight()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    Mo2Fixture fixture = Fixture(scope, false, false);
                    XRayLevelMaterialRequest request = Import(scope, fixture);
                    Material material = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve, true, XRayTerrainImportMode.EnableVerifiedAnomaly).Get(request);
                    Assert.AreEqual(1.0f, material.GetFloat("_MonolithSourceTerrain"));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithHasTerrainHeight"));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithTerrainParallax"));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithTerrainOcclusion"));
                    Assert.IsNull(material.GetTexture("_MonolithTerrainHeightR"));
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [Test]
        public void NewWinnerAndPreviouslyMissingHeightsKeepAssetGuids()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    Mo2Fixture fixture = Fixture(scope, false, false);
                    XRayLevelMaterialRequest request = Import(scope, fixture);
                    Material material = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve, true, XRayTerrainImportMode.EnableVerifiedAnomaly).Get(request);
                    Texture overridden = material.GetTexture("_MonolithTerrainBumpB");
                    material.SetTexture("_MonolithTerrainBumpG", overridden);
                    AssetDatabase.SaveAssetIfDirty(material);
                    string path = AssetDatabase.GetAssetPath(material);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    string bump = request.Terrain.BumpPaths[0];
                    string bumpGuid = AssetDatabase.AssetPathToGUID(bump);
                    string before = XRayLevelSourceRefresh.HashFile(bump);
                    fixture.AddOverwriteFile("textures/detail/layer0_bump.dds", XRayTestBinaries.Bc3Solid(4, 4, 60, 190, 160, 90));
                    for (int i = 0; i < 4; i++) fixture.AddOverwriteFile("textures/detail/layer" + i + "_height.dds", XRayTestBinaries.Bc3Solid(4, 4, 100, 100, 100, 220));
                    request = Import(scope, fixture);
                    material = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve, true, XRayTerrainImportMode.EnableVerifiedAnomaly).Get(request);
                    Assert.AreEqual(path, AssetDatabase.GetAssetPath(material));
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
                    Assert.AreEqual(bumpGuid, AssetDatabase.AssetPathToGUID(request.Terrain.BumpPaths[0]));
                    Assert.AreNotEqual(before, XRayLevelSourceRefresh.HashFile(bump));
                    Assert.AreSame(overridden, material.GetTexture("_MonolithTerrainBumpG"));
                    Assert.AreEqual(1.0f, material.GetFloat("_MonolithHasTerrainHeight"));
                    Assert.AreEqual(1.0f, material.GetFloat("_MonolithTerrainParallax"));
                    Assert.AreEqual(1.0f, material.GetFloat("_MonolithTerrainOcclusion"));
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [Test]
        public void ChangedThmKeepsTerrainGuidAndManualScale()
        {
            using (var scope = new XRayImportTestScope(null))
            {
                string previous = XRayLevelPaths.Root;
                XRayLevelPaths.UseRoot(scope.Folder);
                try
                {
                    Mo2Fixture fixture = Fixture(scope, false);
                    XRayLevelMaterialRequest request = Import(scope, fixture);
                    Material material = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Preserve, true, XRayTerrainImportMode.Bind).Get(request);
                    material.SetFloat("_MonolithTerrainDetailScale", 19.0f);
                    AssetDatabase.SaveAssetIfDirty(material);
                    string path = AssetDatabase.GetAssetPath(material);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    byte[] thm = XRayTestBinaries.Thm(null, 0);
                    using (var stream = new MemoryStream(thm))
                    using (var reader = new BinaryReader(stream))
                    using (var writer = new BinaryWriter(stream))
                    {
                        while (stream.Position < stream.Length)
                        {
                            uint id = reader.ReadUInt32();
                            int length = reader.ReadInt32();
                            if (id == 0x0815)
                            {
                                stream.Position += length - 4;
                                writer.Write(12.0f);
                                break;
                            }
                            stream.Position += length;
                        }
                    }
                    fixture.AddModFile("High", "textures/base.thm", thm);
                    request = Import(scope, fixture);
                    Assert.AreEqual(12.0f, request.Terrain.DetailScale);
                    material = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Update, true, XRayTerrainImportMode.Bind).Get(request);
                    AssetDatabase.SaveAssetIfDirty(material);
                    Assert.AreEqual(path, AssetDatabase.GetAssetPath(material));
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
                    Assert.AreEqual(19.0f, material.GetFloat("_MonolithTerrainDetailScale"));
                    Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceProfile"));
                }
                finally
                {
                    XRayLevelPaths.UseRoot(previous);
                }
            }
        }

        [Test]
        public void SavedRefreshRejectsMoreThan32TargetsBeforeEditorAccess()
        {
            using (var directory = new TestDirectory())
            {
                var fixture = new Mo2Fixture(directory);
                fixture.WriteIni("Profile");
                fixture.AddProfile("Profile", "+High");
                var refresh = new XRayLevelSourceRefresh(Build(fixture), XRayLevelPaths.ANOMALY_ROOT + "/Fixture");
                Assert.Throws<ArgumentException>(() => refresh.Apply(new XRayLevelSourceRefreshEntry[33], directory.Root));
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ExplicitQualityMatchesOriginalPixelShader(int quality)
        {
            string root = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/";
            string template = File.ReadAllText(root + "MonolithTerrainQualityValidation.shader");
            string original = Encoding.UTF8.GetString(Source(false)[quality == 1 ? "shaders/r3/deffer_terrain_mid_flat.ps" : "shaders/r3/deffer_terrain_low_flat.ps"]);
            original = original.Replace("#include \"common.h\"", string.Empty).Replace("#include \"screenspace_mvectors.h\"", string.Empty).Replace("Texture2D s_lod_texture;", string.Empty).Replace("main(", "Original(").Replace("main\t(", "Original(");
            original = System.Text.RegularExpressions.Regex.Replace(original, @"\bmain\s*\(", "Original(");
            string shim = "#define s_base _Base\n#define s_mask _Mask\n#define s_lod_texture _Atlas\n#define smp_base sampler_Base\n#define smp_linear sampler_LinearRepeat\n#define def_virtualh 0.05\n#define ssfx_mv_calc(a,b,c,d) float4(0,0,0,0)\nstruct p_flat { float2 tcdh; float3 N; float3 position; float4 vel_curr; float4 vel_prev; };\nstruct f_deffer { float4 diffuse; float4 normal; float4 velocity; };\nf_deffer pack_gbuffer(float4 n, float4 p, float4 d) { f_deffer o = (f_deffer)0; o.diffuse = d; o.normal = n; return o; }\n";
            int start = template.IndexOf("            float4 Evaluate(", StringComparison.Ordinal);
            int end = template.IndexOf("            float4 Frag(", start, StringComparison.Ordinal);
            string wrapper = "float4 Evaluate(float2 tc) { p_flat i = (p_flat)0; i.tcdh = tc; i.N = _Normal.xyz; i.position = float3(0,0,_UvScale.w); f_deffer o = Original(i); return _Options.w > 0.5 ? o.normal : o.diffuse; }\n";
            string reference = template.Substring(0, start) + shim + original + wrapper + template.Substring(end);
            reference = reference.Replace("Hidden/XRay/TerrainQualityValidation", "Hidden/XRay/TerrainQualityReference" + quality);
            Shader referenceShader = ShaderUtil.CreateShaderAsset(reference);
            var actual = new Material(Shader.Find("Hidden/XRay/TerrainQualityValidation"));
            var expected = new Material(referenceShader);
            var textures = new List<Texture2D>();
            var rows = new StringBuilder("Quality,Case,Output,Scale,Distance,SourceSingular,MaxAbsolute,Failures\n");
            var actualPixels = new List<Color>();
            var expectedPixels = new List<Color>();
            try
            {
                Texture2D baseMap = QualityTexture(64, 0);
                Texture2D atlas = QualityTexture(64, 1);
                Texture2D mask = QualityTexture(1, 2);
                textures.AddRange(new[] { baseMap, atlas, mask });
                foreach (Material material in new[] { actual, expected })
                {
                    material.SetTexture("_Base", baseMap);
                    material.SetTexture("_Atlas", atlas);
                    material.SetTexture("_Mask", mask);
                    material.SetVector("_Normal", new Vector4(0.3f, 0.4f, 0.7f, 0.0f));
                }
                int index = 0;
                foreach (Vector4 weights in new[] { new Vector4(1, 0, 0, 0), new Vector4(1, 1, 1, 1), new Vector4(1, 3, 2, 7), new Vector4(0, 2, 4, 1), Vector4.zero })
                {
                    mask.SetPixel(0, 0, new Color(weights.x, weights.y, weights.z, weights.w));
                    mask.Apply(false, false);
                    foreach (float scale in new[] { 0.01f, 1.0f, 16.0f })
                    {
                        foreach (float distance in new[] { 0.0f, 180.0f, 300.0f })
                        {
                            for (int output = 0; output < 2; output++)
                            {
                                foreach (Material material in new[] { actual, expected })
                                {
                                    material.SetVector("_UvScale", new Vector4(scale, scale * 0.37f, 0.0013f, distance));
                                    material.SetVector("_Options", new Vector4(quality, 1, 1, output));
                                }
                                Color[] a = RenderQuality(actual);
                                Color[] b = RenderQuality(expected);
                                actualPixels.AddRange(a);
                                expectedPixels.AddRange(b);
                                float maximum = 0.0f;
                                int failures = 0;
                                for (int pixel = 0; pixel < a.Length; pixel++)
                                {
                                    for (int channel = 0; channel < 4; channel++)
                                    {
                                        bool singular = weights.Equals(Vector4.zero) && quality == 1;
                                        float delta = singular ? 0.0f : Mathf.Abs(a[pixel][channel] - b[pixel][channel]);
                                        maximum = Mathf.Max(maximum, delta);
                                        if (float.IsNaN(a[pixel][channel]) || float.IsInfinity(a[pixel][channel]) || !singular && (float.IsNaN(delta) || float.IsInfinity(delta) || delta > 2e-6f + 2e-5f * Mathf.Abs(b[pixel][channel]))) failures++;
                                    }
                                }
                                rows.Append(quality).Append(',').Append(index++).Append(',').Append(output).Append(',').Append(scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(distance.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(weights.Equals(Vector4.zero) && quality == 1).Append(',').Append(maximum.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(failures).AppendLine();
                                Assert.AreEqual(0, failures, "quality=" + quality + " case=" + index + " maximum=" + maximum);
                            }
                        }
                    }
                }
                foreach (Shader shader in new[] { actual.shader, referenceShader })
                {
                    foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader)) Assert.AreNotEqual(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.severity, message.message);
                }
            }
            finally
            {
                string folder = Environment.GetEnvironmentVariable("MONOLITH_PHASE05B_OUTPUT");
                if (!string.IsNullOrEmpty(folder))
                {
                    Assert.IsTrue(Path.GetFullPath(folder).StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase));
                    File.WriteAllText(Path.Combine(folder, "Terrain_Quality_" + quality + ".csv"), rows.ToString());
                    File.WriteAllText(Path.Combine(folder, "Original_Terrain_Quality_" + quality + ".shader"), reference);
                    WritePixels(Path.Combine(folder, "Terrain_Quality_" + quality + "_Actual.bin"), actualPixels);
                    WritePixels(Path.Combine(folder, "Terrain_Quality_" + quality + "_Reference.bin"), expectedPixels);
                }
                foreach (Texture2D texture in textures) UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(actual);
                UnityEngine.Object.DestroyImmediate(expected);
                UnityEngine.Object.DestroyImmediate(referenceShader);
            }
        }

        private static Texture2D QualityTexture(int size, int kind)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, size > 1, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 16, mipMapBias = -0.5f };
            var colors = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++) colors[y * size + x] = kind == 2 ? Color.white : new Color((x * 7 % 64) / 64.0f, (y * 3 % 64) / 64.0f, ((x + y) * 11 % 64) / 64.0f, 0.37f);
            }
            texture.SetPixels(colors);
            texture.Apply(size > 1, false);
            return texture;
        }

        [TestCase(1, 0, 1, 0)]
        [TestCase(2, 1, 0, 0)]
        [TestCase(3, 1, 1, 0)]
        [TestCase(0, 1, 1, 0)]
        public void MissingOrUnverifiedQualityFallsBackToHigh(float quality, float midAvailable, float lowAvailable, float expected)
        {
            var material = new Material(Shader.Find("Hidden/XRay/TerrainQualityValidation"));
            try
            {
                material.SetVector("_Options", new Vector4(quality, midAvailable, lowAvailable, 0));
                Color[] pixels = RenderQuality(material);
                Assert.AreEqual(expected, pixels[pixels.Length / 2].r);
                Assert.AreEqual(1.0f, pixels[pixels.Length / 2].a);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void WritePixels(string path, List<Color> pixels)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                foreach (Color color in pixels)
                {
                    for (int channel = 0; channel < 4; channel++) writer.Write(color[channel]);
                }
            }
        }

        private static Color[] RenderQuality(Material material)
        {
            RenderTexture previous = RenderTexture.active;
            var target = new RenderTexture(64, 48, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(64, 48, TextureFormat.RGBAFloat, false, true);
            try
            {
                target.Create();
                using (var commands = new UnityEngine.Rendering.CommandBuffer())
                {
                    commands.SetRenderTarget(target);
                    commands.ClearRenderTarget(false, true, Color.magenta);
                    commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                    Graphics.ExecuteCommandBuffer(commands);
                }
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 64, 48), 0, 0);
                pixels.Apply();
                return pixels.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        private static XRayLevelMaterialRequest Import(XRayImportTestScope scope, Mo2Fixture fixture, bool unrecognized = false, string gameFolder = null)
        {
            var record = new XRayLevelImportRecord();
            record.Source.GameFolder = gameFolder;
            record.Source.FsGame = gameFolder != null ? Path.Combine(gameFolder, "fsgame.ltx") : null;
            var textures = new XRayLevelTextureLibrary(Build(fixture), "arbitrary_name", record, true, true);
            var shader = new XRayLevelShader(0, unrecognized ? "levels\\unverified/base" : "fixture_terrain/base");
            XRayBlender blender = unrecognized ? new XRayBlender() : Blender();
            string[] channels = { "R", "G", "B", "A" };
            for (int i = 0; i < channels.Length; i++) blender.Properties["Detail map/R2-" + channels[i]] = "detail/layer" + i;
            XRayLevelTextureSet set = textures.Request(shader.Texture, false, "terrain automation fixture");
            XRayLevelTerrainTextures terrain = textures.RequestTerrain(shader, blender);
            textures.ImportAll();
            scope.Settle();
            Assert.IsTrue(record.Source.Files.Exists(file => file.Role == "Terrain THM" && file.ContentHash.Length == 71));
            Assert.IsTrue(record.Source.Files.Exists(file => !string.IsNullOrEmpty(file.AssetGuid) && file.ContentHash.Length == 71));
            Texture2D mask = XRayLevelTextureLibrary.Load(terrain.MaskPath);
            if (terrain.Source.Profile == XRayTerrainSourceProfile.AnomalyAtmospherics)
            {
                Assert.AreEqual((int)(terrain.Parameters?.Anisotropy ?? 16), mask.anisoLevel);
                Assert.AreEqual(terrain.Parameters?.MipBias ?? -0.5f, mask.mipMapBias);
                Assert.AreEqual(FilterMode.Trilinear, mask.filterMode);
            }
            XRayLevelSurface surface = XRayLevelSurfaceRules.Classify(shader, blender, false);
            Assert.AreEqual(XRayLevelSurfaceKind.Terrain, surface.Kind);
            return new XRayLevelMaterialRequest { ShaderName = shader.EngineShader, Surface = surface, Textures = set, Terrain = terrain };
        }

        private static XRayBlender Blender()
        {
            var header = new byte[XRayShaderLibrary.DESCRIPTION_SIZE];
            byte[] classId = Encoding.ASCII.GetBytes("BmmD    ");
            Array.Reverse(classId);
            Buffer.BlockCopy(classId, 0, header, 0, classId.Length);
            byte[] name = Encoding.ASCII.GetBytes("fixture_terrain");
            Buffer.BlockCopy(name, 0, header, 8, name.Length);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(XRayShaderLibrary.CHUNK_BLENDERS);
                writer.Write(header.Length + 8);
                writer.Write(0u);
                writer.Write(header.Length);
                writer.Write(header);
                XRayShaderLibrary library = XRayShaderLibrary.Read(stream.ToArray(), "Terrain automation fixture");
                Assert.IsTrue(library.TryGet("fixture_terrain", out XRayBlender blender));
                Assert.AreEqual("BmmD", blender.ClassId);
                return blender;
            }
        }

        private static Mo2Fixture Fixture(XRayImportTestScope scope, bool trueStalker, bool height = true)
        {
            var fixture = new Mo2Fixture(scope.Directory);
            fixture.WriteIni("Profile");
            fixture.AddProfile("Profile", "+High", "+Low");
            foreach (KeyValuePair<string, byte[]> pair in Source(trueStalker)) fixture.AddModFile("High", pair.Key, pair.Value);
            fixture.AddModFile("High", "textures/base.dds", XRayTestBinaries.Bc3Solid(4, 4, 120, 100, 80, 220));
            fixture.AddModFile("High", "textures/base.thm", XRayTestBinaries.Thm(null, 0));
            fixture.AddModFile("High", "textures/base_mask.dds", XRayTestBinaries.Bc3Solid(4, 4, 120, 100, 80, 220));
            fixture.AddModFile("High", "textures/terrain/default_lod_textures.dds", XRayTestBinaries.Bc3Solid(4, 4, 120, 100, 80, 220));
            for (int i = 0; i < 4; i++)
            {
                fixture.AddModFile("High", "textures/detail/layer" + i + ".dds", XRayTestBinaries.Bc3Solid(4, 4, (byte)(100 + i), 100, 80, 220));
                fixture.AddModFile("High", "textures/detail/layer" + i + "_bump.dds", XRayTestBinaries.Bc3Solid(4, 4, 50, 255, 128, 128));
                fixture.AddModFile("High", "textures/detail/layer" + i + "_bump#.dds", XRayTestBinaries.Bc3Solid(4, 4, 50, 255, 128, 128));
                if (height) fixture.AddModFile("High", "textures/detail/layer" + i + "_height.dds", XRayTestBinaries.Bc3Solid(4, 4, 100, 100, 100, 220));
            }
            return fixture;
        }

        private static Dictionary<string, byte[]> Source(bool trueStalker)
        {
            if (SOURCES.TryGetValue(trueStalker, out Dictionary<string, byte[]> existing)) return existing;
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.IndexScope.Clear();
            configuration.IndexScope.Add("shaders/r3");
            XRayAssetIndex index = trueStalker ? XRayAssetIndex.Build(XRay.Importer.Sources.Levels.XRayLevelGamedata.CreatePlan(XRay.Importer.Sources.Levels.XRayLevelGamedata.Inspect("E:/True Stalker Unpacked Patch 1.5"), null, false)) : XRayAssetIndex.Build(Mo2VirtualFileSystem.Plan(configuration));
            var resolver = new XRayTextureResolver(index);
            var result = new Dictionary<string, byte[]>();
            string[] paths = trueStalker ? new[] { "shaders/r3/deffer_impl_flat.ps", "shaders/r3/lmodel.h" } : new[] { "shaders/r3/deffer_terrain_high_flat_d.ps", "shaders/r3/lmodel.h", "shaders/r3/deffer_terrain_mid_flat.ps", "shaders/r3/deffer_terrain_low_flat.ps" };
            foreach (string path in paths)
            {
                XRayResolvedFile file = resolver.ResolveFile(path);
                Assert.IsTrue(file.IsResolved, "Source integration fixture requires effective local bytes: " + path);
                result[path] = file.Winner.ReadAllBytes();
            }
            SOURCES[trueStalker] = result;
            return result;
        }

        private static XRayAssetIndex Build(Mo2Fixture fixture)
        {
            XRaySourceConfiguration configuration = fixture.CreateConfiguration("Profile");
            configuration.IndexScope.Add("shaders");
            return XRayAssetIndex.Build(Mo2VirtualFileSystem.Plan(configuration));
        }

        private static Dictionary<string, string> Snapshot(string folder)
        {
            var files = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) files[file] = XRayLevelSourceRefresh.HashFile(file);
            return files;
        }
    }
}
