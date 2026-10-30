using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class MonolithFloraImportTests
    {
        private const string TEMP_PARENT = "Assets/__XRayFloraUnitTests_tmp";
        private const string ANOMALY_ROOT = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d";
        private static readonly string[] AUTHORED_TEXTURES = { "textures/lfo/lfo_trees/adler/trees_atp_alder_a.dds", "textures/lfo/lfo_trees/spruce/trees_atp_spruce_broken.dds" };
        private static readonly int[] PROJECTED = { 2048, 1024, 512, 256, 128, 64, 32, 16 };

        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private string _temp;

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            if (_temp != null && AssetDatabase.IsValidFolder(_temp)) Assert.IsTrue(AssetDatabase.DeleteAsset(_temp));
            if (_temp != null) Assert.IsFalse(Directory.Exists(_temp) || File.Exists(_temp + ".meta"));
            if (AssetDatabase.IsValidFolder(TEMP_PARENT) && Directory.GetFileSystemEntries(TEMP_PARENT).Length == 0) AssetDatabase.DeleteAsset(TEMP_PARENT);
            _temp = null;
        }

        [Test]
        public void AuthoredMipChainFlipIsBlockExact()
        {
            var random = new System.Random(6123);
            foreach (string fourCC in new[] { "DXT1", "DXT3", "DXT5" })
            {
                int blockSize = fourCC == "DXT1" ? 8 : 16;
                int width = 16;
                int height = 8;
                var levels = new List<byte[]>();
                for (int mip = 0; mip < 4; mip++)
                {
                    int w = Math.Max(1, width >> mip);
                    int h = Math.Max(1, height >> mip);
                    var level = new byte[Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * blockSize];
                    random.NextBytes(level);
                    levels.Add(level);
                }
                byte[] source = Dds(width, height, fourCC, levels);
                DdsHeader header = DdsHeader.Parse(source);
                Assert.AreEqual(4, header.MipCount);
                Assert.IsTrue(DdsMipChain.TryReadFlipped(source, header, out List<byte[]> flipped, out string reason), reason);
                Assert.AreEqual(levels.Count, flipped.Count);
                for (int mip = 0; mip < levels.Count; mip++)
                {
                    int w = Math.Max(1, width >> mip);
                    int h = Math.Max(1, height >> mip);
                    byte[] original = DdsDecoder.DecodeTopLevel(Dds(w, h, fourCC, new List<byte[]> { levels[mip] }), DdsHeader.Parse(Dds(w, h, fourCC, new List<byte[]> { levels[mip] })));
                    byte[] mirrored = DdsDecoder.DecodeTopLevel(Dds(w, h, fourCC, new List<byte[]> { flipped[mip] }), DdsHeader.Parse(Dds(w, h, fourCC, new List<byte[]> { flipped[mip] })));
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w * 4; x++)
                        {
                            Assert.AreEqual(original[(h - 1 - y) * w * 4 + x], mirrored[y * w * 4 + x], fourCC + " mip " + mip + " row " + y);
                        }
                    }
                }
            }
            Assert.IsFalse(DdsMipChain.TryReadFlipped(Dds(6, 6, "DXT1", new List<byte[]> { new byte[32] }), DdsHeader.Parse(Dds(6, 6, "DXT1", new List<byte[]> { new byte[32] })), out _, out string odd));
            StringAssert.Contains("multiple of 4", odd);
        }

        [Test]
        public void RealAuthoredMipsAreExactOnGpuAndPreserveCoverage()
        {
            HdrpTestWarmup.Ensure();
            string output = Output();
            _temp = TempFolder();
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "textures" });
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
            var effective = new XRayEffectiveGamedata(XRayAssetIndex.Build(plan));
            var importer = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>());
            Shader shader = Shader.Find("Hidden/XRay/MonolithFloraMipValidation");
            Assert.IsNotNull(shader);
            Material material = Own(new Material(shader));
            var exact = new StringBuilder("Texture,Winner,SHA256,Mip,Width,Height,ComparedTexels,MismatchedAlpha,AuthoredCoverage,LegacyPngCoverage,LegacyPngMips\n");
            var projected = new StringBuilder("Texture,Representation,ProjectedSize,AlphaTestPixels,Coverage,MeanLod,NonFinite\n");
            foreach (string virtualPath in AUTHORED_TEXTURES)
            {
                XRayResolvedFile file = new XRayTextureResolver(effective.Index).ResolveFile(virtualPath);
                Assert.IsTrue(file.IsResolved, virtualPath);
                byte[] bytes = file.Winner.ReadAllBytes();
                DdsHeader header = DdsHeader.Parse(bytes);
                XRayTextureAssetResult result = importer.ImportAuthoredMips(file, _temp, 16, -0.5f);
                Assert.AreEqual(XRayResourceStatus.Imported, result.Status, result.Note);
                var authored = AssetDatabase.LoadAssetAtPath<Texture2D>(result.AssetPath);
                Assert.IsNotNull(authored);
                Assert.AreEqual(header.MipCount, authored.mipmapCount);
                Assert.AreEqual(header.Width, authored.width);
                Assert.AreEqual(header.Height, authored.height);
                Assert.IsFalse(authored.isDataSRGB);
                Assert.AreEqual(GraphicsFormat.RGBA_DXT1_UNorm, authored.graphicsFormat);
                Assert.AreEqual(16, authored.anisoLevel);
                Assert.AreEqual(-0.5f, authored.mipMapBias);
                Assert.AreEqual(FilterMode.Trilinear, authored.filterMode);
                XRayTextureAssetResult reused = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>()).ImportAuthoredMips(file, _temp, 16, -0.5f);
                Assert.AreEqual(XRayResourceStatus.Reused, reused.Status);
                Assert.AreEqual(result.AssetPath, reused.AssetPath);
                Texture2D legacy = LegacyPng(virtualPath);
                Assert.IsTrue(DdsMipChain.TryReadFlipped(bytes, header, out List<byte[]> unused, out string reason), reason);
                authored.mipMapBias = 0.0f;
                int offset = header.DataOffset;
                int width = header.Width;
                int height = header.Height;
                for (int mip = 0; mip < header.MipCount; mip++)
                {
                    int size = DdsHeader.GetLevelSize(header.Format, width, height);
                    var level = new byte[size];
                    Buffer.BlockCopy(bytes, offset, level, 0, size);
                    offset += size;
                    byte[] single = Dds(width, height, "DXT1", new List<byte[]> { level });
                    byte[] cpu = DdsDecoder.DecodeTopLevel(single, DdsHeader.Parse(single));
                    Color32[] gpu = Read(material, authored, mip, width, height);
                    int mismatches = 0;
                    int covered = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            byte expected = cpu[((height - 1 - y) * width + x) * 4 + 3];
                            byte observed = gpu[y * width + x].a;
                            if (expected != observed) mismatches++;
                            if (expected >= 200) covered++;
                        }
                    }
                    double legacyCoverage = double.NaN;
                    if (legacy != null && mip < legacy.mipmapCount)
                    {
                        Color32[] legacyPixels = Read(material, legacy, mip, Math.Max(1, legacy.width >> mip), Math.Max(1, legacy.height >> mip));
                        int legacyCovered = 0;
                        foreach (Color32 pixel in legacyPixels) if (pixel.a >= 200) legacyCovered++;
                        legacyCoverage = (double)legacyCovered / legacyPixels.Length;
                    }
                    exact.AppendLine(string.Join(",", Csv(virtualPath), Csv(file.Winner.SourceName), Sha(bytes), mip, width, height, width * height, mismatches, ((double)covered / (width * height)).ToString("R", CultureInfo.InvariantCulture), legacyCoverage.ToString("R", CultureInfo.InvariantCulture), legacy != null ? legacy.mipmapCount : 0));
                    File.WriteAllText(Path.Combine(output, "Flora_Mip_Exact.csv"), exact.ToString());
                    Assert.AreEqual(0, mismatches, virtualPath + " mip " + mip);
                    width = Math.Max(1, width >> 1);
                    height = Math.Max(1, height >> 1);
                }
                authored.mipMapBias = -0.5f;
                foreach ((string name, Texture2D texture) in new[] { ("AuthoredDDS", authored), ("LegacyPNG", legacy) })
                {
                    if (texture == null) continue;
                    foreach (int size in PROJECTED)
                    {
                        material.SetTexture("_Texture", texture);
                        material.SetFloat("_Mode", 2);
                        material.SetFloat("_Cutoff", 200.0f / 255.0f);
                        material.SetVector("_Uv", new Vector4(1, 1, 0, 0));
                        Color[] pixels = Render(material, size, size, RenderTextureFormat.ARGBFloat);
                        int pass = 0;
                        int nonFinite = 0;
                        double lod = 0;
                        foreach (Color pixel in pixels)
                        {
                            if (float.IsNaN(pixel.r) || float.IsInfinity(pixel.r) || float.IsNaN(pixel.g) || float.IsInfinity(pixel.g)) nonFinite++;
                            if (pixel.r > 0.5f) pass++;
                            lod += pixel.g;
                        }
                        Assert.AreEqual(0, nonFinite);
                        projected.AppendLine(string.Join(",", Csv(virtualPath), name, size, pass, ((double)pass / pixels.Length).ToString("R", CultureInfo.InvariantCulture), (lod / pixels.Length).ToString("R", CultureInfo.InvariantCulture), nonFinite));
                        File.WriteAllText(Path.Combine(output, "Flora_Mip_Coverage_Results.csv"), projected.ToString());
                    }
                }
            }
        }

        [Test]
        public void DetailAlphaFollowsVerifiedSource()
        {
            var cases = new[]
            {
                (XRayFloraImportMode.Preserve, XRayFloraSourceProfile.AnomalySss, true, 170.0f),
                (XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraSourceProfile.AnomalySss, false, 200.0f),
                (XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraSourceProfile.AnomalySss, true, 200.0f),
                (XRayFloraImportMode.EnableVerifiedTrueStalker, XRayFloraSourceProfile.TrueStalker, true, 170.0f),
                (XRayFloraImportMode.EnableVerifiedTrueStalker, XRayFloraSourceProfile.TrueStalker, false, 200.0f),
                (XRayFloraImportMode.EnableVerifiedTrueStalker, XRayFloraSourceProfile.AnomalySss, false, 170.0f),
                (XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraSourceProfile.Unknown, false, 170.0f)
            };
            var rows = new StringBuilder("Mode,Profile,DetailBump,ExpectedCutoff,Cutoff,ShadowCutoff,Tag\n");
            foreach ((XRayFloraImportMode mode, XRayFloraSourceProfile profile, bool bump, float expected) in cases)
            {
                Material material = DetailMaterial();
                var request = new XRayLevelMaterialRequest { ShaderName = "details\\blend", Detail = true, Surface = DetailSurface(), Textures = new XRayLevelTextureSet { BumpPath = bump ? "Assets/bump.dds" : null } };
                Assert.IsTrue(XRayFloraMaterialBindings.Applies(material, request));
                XRayLevelBindingPlan plan = XRayFloraMaterialBindings.Plan(material, request, mode, profile, false);
                XRayFloraMaterialBindings.Apply(material, plan, "contract");
                rows.AppendLine(string.Join(",", mode, profile, bump, expected, material.GetFloat("_AlphaCutoff") * 255.0f, material.GetFloat("_AlphaCutoffShadow") * 255.0f, Csv(material.GetTag(XRayFloraMaterialBindings.TAG, false, string.Empty))));
                foreach (string property in new[] { "_AlphaCutoff", "_AlphaCutoffShadow", "_AlphaCutoffPrepass", "_AlphaCutoffPostpass" }) Assert.AreEqual(expected / 255.0f, material.GetFloat(property), 1e-6f, mode + " " + profile + " " + property);
                Assert.AreEqual(0.0f, material.GetFloat("_CullMode"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraSource"));
            }
            File.WriteAllText(Path.Combine(Output(), "Flora_Detail_Alpha.csv"), rows.ToString());
        }

        [Test]
        public void LeafPlanIsolatesSourcesAndKeepsOverrides()
        {
            _temp = TempFolder();
            Texture2D legacyAlbedo = Asset(new Texture2D(4, 4, TextureFormat.RGBA32, false, false), "legacy_albedo.asset");
            Texture2D authored = Asset(new Texture2D(4, 4, GraphicsFormat.RGBA_DXT1_UNorm, 3, TextureCreationFlags.None), "authored.asset");
            Texture2D bump = Asset(new Texture2D(4, 4, TextureFormat.RGBA32, false, true), "bump.asset");
            Texture2D correction = Asset(new Texture2D(4, 4, TextureFormat.RGBA32, false, true), "correction.asset");
            var rows = new StringBuilder("Case,FloraSource,SourceProfile,DoubleSided,Cull,AlbedoSrgb,Albedo,Correction,HasCorrection,Overrides\n");
            Func<Material> leaf = () =>
            {
                Material material = Own(new Material(Shader.Find(MonolithMaterialConfigurator.SHADER_NAME)));
                MonolithMaterialConfigurator.Configure(material, null, legacyAlbedo, bump, null, true);
                material.EnableKeyword("_MONOLITH_LEVEL_FLORA");
                material.SetFloat("_MonolithLevelFlora", 1.0f);
                material.SetFloat("_DoubleSidedEnable", 1.0f);
                material.SetFloat("_CullMode", 0.0f);
                material.SetFloat("_CullModeForward", 0.0f);
                material.EnableKeyword("_DOUBLESIDED_ON");
                foreach (string property in new[] { "_AlphaCutoff", "_AlphaCutoffShadow", "_AlphaCutoffPrepass", "_AlphaCutoffPostpass" }) material.SetFloat(property, XRayFloraSourceBinding.AREF);
                return material;
            };
            XRayLevelMaterialRequest Request(string authoredPath, string correctionPath)
            {
                return new XRayLevelMaterialRequest { ShaderName = "flora\\leaf_wave", Surface = LeafSurface(), Textures = new XRayLevelTextureSet { AlbedoPath = AssetDatabase.GetAssetPath(legacyAlbedo), BumpPath = AssetDatabase.GetAssetPath(bump), CorrectionPath = correctionPath }, Flora = new XRayLevelFloraTextures { AuthoredAlbedoPath = authoredPath, CorrectionPath = correctionPath } };
            }
            void Record(string name, Material material, XRayLevelBindingPlan plan)
            {
                rows.AppendLine(string.Join(",", name, material.GetFloat("_MonolithFloraSource"), material.GetFloat("_MonolithSourceProfile"), material.GetFloat("_DoubleSidedEnable"), material.GetFloat("_CullMode"), material.GetFloat("_MonolithAlbedoSrgb"), Csv(AssetDatabase.GetAssetPath(material.GetTexture("_BaseColorMap"))), Csv(AssetDatabase.GetAssetPath(material.GetTexture("_MonolithBumpCorrection"))), material.GetFloat("_MonolithHasBumpCorrection"), plan.Changes.FindAll(change => change.Ownership == XRayLevelBindingOwnership.UserOverride).Count));
            }

            Material anomaly = leaf();
            XRayLevelBindingPlan anomalyPlan = XRayFloraMaterialBindings.Plan(anomaly, Request(AssetDatabase.GetAssetPath(authored), null), XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraSourceProfile.AnomalySss, false);
            Assert.IsTrue(XRayFloraMaterialBindings.Apply(anomaly, anomalyPlan, XRayFloraSourceBinding.ANOMALY_CONTRACT_HASH));
            Record("Anomaly", anomaly, anomalyPlan);
            Assert.AreEqual(1.0f, anomaly.GetFloat("_MonolithFloraSource"));
            Assert.AreEqual(1.0f, anomaly.GetFloat("_MonolithSourceProfile"));
            Assert.AreEqual(0.0f, anomaly.GetFloat("_DoubleSidedEnable"));
            Assert.AreEqual((float)CullMode.Back, anomaly.GetFloat("_CullMode"));
            Assert.AreEqual((float)CullMode.Back, anomaly.GetFloat("_CullModeForward"));
            Assert.IsFalse(anomaly.IsKeywordEnabled("_DOUBLESIDED_ON"));
            Assert.AreEqual(authored, anomaly.GetTexture("_BaseColorMap"));
            Assert.AreEqual(0.0f, anomaly.GetFloat("_MonolithAlbedoSrgb"));
            Assert.IsNull(anomaly.GetTexture("_MonolithBumpCorrection"));
            Assert.AreEqual(XRayFloraSourceBinding.ANOMALY_CONTRACT_HASH, anomaly.GetTag("XRayFloraContractSHA256", false));
            Assert.IsFalse(XRayFloraMaterialBindings.Apply(anomaly, XRayFloraMaterialBindings.Plan(anomaly, Request(AssetDatabase.GetAssetPath(authored), null), XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraSourceProfile.AnomalySss, false), XRayFloraSourceBinding.ANOMALY_CONTRACT_HASH), "A repeated plan must be a no-op.");
            anomaly.SetFloat("_CullMode", 0.0f);
            anomaly.SetFloat("_CullModeForward", 0.0f);
            anomaly.SetTexture("_BaseColorMap", legacyAlbedo);
            XRayLevelBindingPlan overridePlan = XRayFloraMaterialBindings.Plan(anomaly, Request(AssetDatabase.GetAssetPath(authored), null), XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraSourceProfile.AnomalySss, false);
            XRayFloraMaterialBindings.Apply(anomaly, overridePlan, XRayFloraSourceBinding.ANOMALY_CONTRACT_HASH);
            Record("AnomalyUserOverrides", anomaly, overridePlan);
            Assert.AreEqual(0.0f, anomaly.GetFloat("_CullMode"));
            Assert.AreEqual(legacyAlbedo, anomaly.GetTexture("_BaseColorMap"));
            Assert.AreEqual(1.0f, anomaly.GetFloat("_MonolithAlbedoSrgb"));
            Assert.AreEqual(1.0f, anomaly.GetFloat("_MonolithFloraSource"));

            Material mismatch = leaf();
            XRayLevelBindingPlan mismatchPlan = XRayFloraMaterialBindings.Plan(mismatch, Request(AssetDatabase.GetAssetPath(authored), AssetDatabase.GetAssetPath(correction)), XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraSourceProfile.TrueStalker, false);
            Assert.IsFalse(XRayFloraMaterialBindings.Apply(mismatch, mismatchPlan, string.Empty));
            Record("AnomalyModeOnTrueStalkerSource", mismatch, mismatchPlan);
            Assert.AreEqual(0.0f, mismatch.GetFloat("_MonolithFloraSource"));
            Assert.AreEqual(1.0f, mismatch.GetFloat("_DoubleSidedEnable"));

            Material missing = leaf();
            XRayLevelBindingPlan missingPlan = XRayFloraMaterialBindings.Plan(missing, Request(null, null), XRayFloraImportMode.EnableVerifiedTrueStalker, XRayFloraSourceProfile.TrueStalker, false);
            XRayFloraMaterialBindings.Apply(missing, missingPlan, XRayFloraSourceBinding.TRUE_STALKER_CONTRACT_HASH);
            Record("TrueStalkerMissingCorrection", missing, missingPlan);
            Assert.AreEqual(0.0f, missing.GetFloat("_MonolithFloraSource"));
            Assert.AreEqual(0.0f, missing.GetFloat("_MonolithSourceProfile"));
            Assert.AreEqual(1.0f, missing.GetFloat("_DoubleSidedEnable"));
            Assert.AreEqual(0.0f, missing.GetFloat("_CullMode"));
            Assert.AreEqual(legacyAlbedo, missing.GetTexture("_BaseColorMap"));

            Material trueStalker = leaf();
            trueStalker.SetFloat("_MonolithSourceProfile", 1.0f);
            XRayLevelBindingPlan trueStalkerPlan = XRayFloraMaterialBindings.Plan(trueStalker, Request(null, AssetDatabase.GetAssetPath(correction)), XRayFloraImportMode.EnableVerifiedTrueStalker, XRayFloraSourceProfile.TrueStalker, false);
            XRayFloraMaterialBindings.Apply(trueStalker, trueStalkerPlan, XRayFloraSourceBinding.TRUE_STALKER_CONTRACT_HASH);
            Record("TrueStalker", trueStalker, trueStalkerPlan);
            Assert.AreEqual(2.0f, trueStalker.GetFloat("_MonolithFloraSource"));
            Assert.AreEqual(0.0f, trueStalker.GetFloat("_MonolithSourceProfile"));
            Assert.AreEqual(correction, trueStalker.GetTexture("_MonolithBumpCorrection"));
            Assert.AreEqual(1.0f, trueStalker.GetFloat("_MonolithHasBumpCorrection"));
            Assert.AreEqual((float)CullMode.Back, trueStalker.GetFloat("_CullMode"));
            Assert.AreEqual(legacyAlbedo, trueStalker.GetTexture("_BaseColorMap"));
            File.WriteAllText(Path.Combine(Output(), "Flora_Leaf_Plans.csv"), rows.ToString());
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator AnomalyFullImportLegacyKeepsExistingRendering()
        {
            using (var fixture = new XRayFloraLevelFixture(false))
            {
                yield return Import(fixture, XRayFloraImportMode.Preserve);
                Material material = fixture.Material();
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraSource"));
                Assert.AreEqual(1.0f, material.GetFloat("_DoubleSidedEnable"));
                Assert.AreEqual(0.0f, material.GetFloat("_CullMode"));
                Assert.IsEmpty(material.GetTag(XRayFloraMaterialBindings.TAG, false, string.Empty));
                Assert.IsTrue(material.IsKeywordEnabled("_MONOLITH_LEVEL_FLORA"));
                StringAssert.DoesNotEndWith(".asset", AssetDatabase.GetAssetPath(material.GetTexture("_BaseColorMap")));
                Assert.IsEmpty(XRayLevelImportJob.LastRecord.Source.FloraProfile ?? string.Empty);
            }
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator AnomalyFullImportEnablesVerifiedFloraAndAuthoredMips()
        {
            string root;
            using (var fixture = new XRayFloraLevelFixture(false))
            {
                root = fixture.Root;
                Assert.IsTrue(fixture.PartialMipChain, "The Anomaly acceptance input must use an authored partial DDS mip chain.");
                yield return Import(fixture, XRayFloraImportMode.EnableVerifiedAnomaly);
                Material material = fixture.Material();
                Assert.AreEqual(1.0f, material.GetFloat("_MonolithFloraSource"));
                Assert.AreEqual(1.0f, material.GetFloat("_MonolithSourceProfile"));
                Assert.AreEqual(0.0f, material.GetFloat("_DoubleSidedEnable"));
                Assert.AreEqual((float)CullMode.Back, material.GetFloat("_CullMode"));
                Assert.IsFalse(material.IsKeywordEnabled("_DOUBLESIDED_ON"));
                Assert.AreEqual(XRayFloraSourceBinding.AREF, material.GetFloat("_AlphaCutoff"), 1e-6f);
                var albedo = material.GetTexture("_BaseColorMap") as Texture2D;
                Assert.IsNotNull(albedo);
                StringAssert.EndsWith("_authored.asset", AssetDatabase.GetAssetPath(albedo));
                Assert.IsFalse(albedo.isDataSRGB);
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithAlbedoSrgb"));
                Assert.Less(albedo.mipmapCount, Mathf.FloorToInt(Mathf.Log(Mathf.Max(albedo.width, albedo.height), 2)) + 1);
                Assert.AreEqual(XRayFloraSourceBinding.ANOMALY_CONTRACT_HASH, material.GetTag("XRayFloraContractSHA256", false));
                Assert.AreEqual(XRayFloraSourceProfile.AnomalySss.ToString(), XRayLevelImportJob.LastRecord.Source.FloraProfile);
                Assert.IsTrue(XRayLevelImportJob.LastRecord.Dependencies.Exists(dependency => dependency.Role == "Flora authored albedo mips"));
                Assert.IsTrue(XRayLevelImportJob.LastRecord.Dependencies.Exists(dependency => dependency.Role == "Flora source contract"));
                string materialGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material));
                string albedoGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(albedo));
                AssetDatabase.SaveAssets();
                Dictionary<string, string> before = Snapshot(root);
                yield return Import(fixture, XRayFloraImportMode.EnableVerifiedAnomaly);
                AssetDatabase.SaveAssets();
                Dictionary<string, string> after = Snapshot(root);
                WriteComparison(fixture, before, after, "Idempotence");
                CollectionAssert.AreEquivalent(before, after, "A repeated flora import must not change saved asset bytes.");
                material = fixture.Material();
                Assert.AreEqual(materialGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)));
                Assert.AreEqual(albedoGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.GetTexture("_BaseColorMap"))));
                material.SetFloat("_CullMode", 0.0f);
                material.SetFloat("_CullModeForward", 0.0f);
                material.SetFloat("_AlphaCutoff", 0.5f);
                AssetDatabase.SaveAssetIfDirty(material);
                yield return Import(fixture, XRayFloraImportMode.EnableVerifiedAnomaly);
                material = fixture.Material();
                Assert.AreEqual(0.0f, material.GetFloat("_CullMode"));
                Assert.AreEqual(0.5f, material.GetFloat("_AlphaCutoff"));
                Assert.AreEqual(1.0f, material.GetFloat("_MonolithFloraSource"));
                WriteComparison(fixture, before, Snapshot(root), "Overrides");
                yield return Winding(fixture, "Anomaly");
            }
            Assert.IsFalse(Directory.Exists(root) || File.Exists(root + ".meta"));
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator TrueStalkerFullImportBindsCorrectionWithProfileZero()
        {
            string root;
            using (var fixture = new XRayFloraLevelFixture(true))
            {
                root = fixture.Root;
                yield return Import(fixture, XRayFloraImportMode.EnableVerifiedTrueStalker);
                Material material = fixture.Material();
                Assert.AreEqual(2.0f, material.GetFloat("_MonolithFloraSource"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceProfile"));
                Assert.AreEqual(1.0f, material.GetFloat("_MonolithHasBumpCorrection"));
                Texture correction = material.GetTexture("_MonolithBumpCorrection");
                Assert.IsNotNull(correction);
                StringAssert.EndsWith("_bump__packed.dds", AssetDatabase.GetAssetPath(correction).ToLowerInvariant());
                Assert.IsFalse(((Texture2D)correction).isDataSRGB);
                Assert.AreEqual((float)CullMode.Back, material.GetFloat("_CullMode"));
                Assert.AreEqual(XRayFloraSourceBinding.TRUE_STALKER_CONTRACT_HASH, material.GetTag("XRayFloraContractSHA256", false));
                Assert.AreEqual(XRayFloraSourceProfile.TrueStalker.ToString(), XRayLevelImportJob.LastRecord.Source.FloraProfile);
                AssetDatabase.SaveAssets();
                Dictionary<string, string> before = Snapshot(root);
                yield return Import(fixture, XRayFloraImportMode.EnableVerifiedTrueStalker);
                AssetDatabase.SaveAssets();
                Dictionary<string, string> after = Snapshot(root);
                WriteComparison(fixture, before, after, "Idempotence");
                CollectionAssert.AreEquivalent(before, after, "A repeated flora import must not change saved asset bytes.");
                yield return Winding(fixture, "TrueStalker");
                string capture = Path.Combine(fixture.Output() ?? Output(), "Real_TS_escape_Imported");
                Directory.CreateDirectory(capture);
                Material imported = fixture.Material();
                Material legacy = new Material(imported);
                _owned.Add(legacy);
                legacy.SetFloat("_MonolithFloraSource", 0.0f);
                legacy.SetFloat("_MonolithHasBumpCorrection", 0.0f);
                legacy.SetTexture("_MonolithBumpCorrection", null);
                legacy.SetFloat("_DoubleSidedEnable", 1.0f);
                legacy.SetFloat("_CullMode", 0.0f);
                legacy.SetFloat("_CullModeForward", 0.0f);
                legacy.EnableKeyword("_DOUBLESIDED_ON");
                yield return Capture(capture, fixture.Batches().Batches[0], legacy, null, (Texture2D)imported.GetTexture("_MonolithBumpCorrection"), true);
            }
            Assert.IsFalse(Directory.Exists(root) || File.Exists(root + ".meta"));
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator TrueStalkerMissingCorrectionKeepsLegacyFlora()
        {
            using (var fixture = new XRayFloraLevelFixture(true))
            {
                fixture.RemoveCorrection();
                yield return Import(fixture, XRayFloraImportMode.EnableVerifiedTrueStalker);
                Material material = fixture.Material();
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraSource"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceProfile"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithHasBumpCorrection"));
                Assert.AreEqual(1.0f, material.GetFloat("_DoubleSidedEnable"));
                Assert.AreEqual(0.0f, material.GetFloat("_CullMode"));
            }
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator MismatchedOrChangedFloraSourceKeepsLegacy()
        {
            using (var fixture = new XRayFloraLevelFixture(false))
            {
                yield return Import(fixture, XRayFloraImportMode.EnableVerifiedTrueStalker);
                Material material = fixture.Material();
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraSource"));
                Assert.AreEqual(1.0f, material.GetFloat("_DoubleSidedEnable"));
                Assert.IsEmpty(material.GetTag(XRayFloraMaterialBindings.TAG, false, string.Empty));
            }
            using (var fixture = new XRayFloraLevelFixture(false))
            {
                fixture.ReplaceFloraShader();
                yield return Import(fixture, XRayFloraImportMode.EnableVerifiedAnomaly);
                Material material = fixture.Material();
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraSource"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceProfile"));
                Assert.AreEqual(XRayFloraSourceProfile.Unknown.ToString(), XRayLevelImportJob.LastRecord.Source.FloraProfile);
            }
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator AnomalyEscapeFloraMipAndCullAB()
        {
            HdrpTestWarmup.Ensure();
            string output = Path.Combine(Output(), "Real_Anomaly_l01_escape");
            Directory.CreateDirectory(output);
            _temp = TempFolder();
            var set = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(ANOMALY_ROOT + "/l01_escape/Meshes/l01_escape_StaticProps.asset");
            Assert.IsNotNull(set, "Saved Anomaly l01_escape static props are required.");
            XRayInstanceBatch selected = null;
            foreach (XRayInstanceBatch batch in set.Batches)
            {
                Material candidate = batch.Materials != null && batch.Materials.Length > 0 ? batch.Materials[0] : null;
                if (candidate == null || !candidate.IsKeywordEnabled("_MONOLITH_LEVEL_FLORA") || batch.Count == 0) continue;
                if (!AssetDatabase.GetAssetPath(candidate.GetTexture("_BaseColorMap")).Contains("trees_atp_alder_a")) continue;
                if (selected == null || batch.Mesh.vertexCount > selected.Mesh.vertexCount) selected = batch;
            }
            Assert.IsNotNull(selected, "A saved alder leaf batch is required.");
            Material stored = selected.Materials[0];
            string storedPath = AssetDatabase.GetAssetPath(stored);
            string storedBytes = File.ReadAllText(storedPath);
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "textures" });
            var effective = new XRayEffectiveGamedata(XRayAssetIndex.Build(Mo2VirtualFileSystem.Plan(configuration)));
            XRayTextureAssetResult authoredResult = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>()).ImportAuthoredMips(new XRayTextureResolver(effective.Index).ResolveFile(AUTHORED_TEXTURES[0]), _temp, 16, -0.5f);
            var authored = AssetDatabase.LoadAssetAtPath<Texture2D>(authoredResult.AssetPath);
            Assert.IsNotNull(authored, authoredResult.Note);
            yield return Capture(output, selected, stored, authored, null, false);
            Assert.AreEqual(storedBytes, File.ReadAllText(storedPath));
            Assert.IsFalse(EditorUtility.IsDirty(stored));
        }

        private IEnumerator Winding(XRayFloraLevelFixture fixture, string name)
        {
            string output = fixture.Output() ?? Path.Combine(Output(), "Winding_" + name);
            Directory.CreateDirectory(output);
            XRayInstanceBatchSet set = fixture.Batches();
            XRayInstanceBatch batch = set.Batches[0];
            Mesh mesh = batch.Mesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            int agree = 0;
            int degenerate = 0;
            int reversedPairs = 0;
            var keys = new HashSet<string>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-12f)
                {
                    degenerate++;
                    continue;
                }
                if (Vector3.Dot(face, normals[triangles[i]] + normals[triangles[i + 1]] + normals[triangles[i + 2]]) > 0.0f) agree++;
                keys.Add(Key(a, b, c));
            }
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                if (keys.Contains(Key(vertices[triangles[i]], vertices[triangles[i + 2]], vertices[triangles[i + 1]]))) reversedPairs++;
            }
            int negative = 0;
            foreach (XRayInstanceBatch each in set.Batches) foreach (Matrix4x4 matrix in each.Matrices) if (matrix.determinant < 0.0f) negative++;
            Material material = fixture.Material();
            int[] coverage = new int[2];
            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            try
            {
                var geometry = new GameObject("Winding capture");
                temporary.Add(geometry);
                SceneManager.MoveGameObjectToScene(geometry, scene);
                Matrix4x4 matrix = batch.Matrices[0];
                geometry.transform.SetPositionAndRotation(matrix.GetColumn(3), matrix.rotation);
                geometry.transform.localScale = matrix.lossyScale;
                geometry.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = geometry.AddComponent<MeshRenderer>();
                Material copy = new Material(material);
                temporary.Add(copy);
                renderer.sharedMaterial = copy;
                Camera camera = Rig(scene, renderer.bounds, temporary, false);
                using (var target = new Target(camera, 320, 180))
                {
                    for (int mode = 0; mode < 2; mode++)
                    {
                        copy.SetFloat("_CullMode", mode == 0 ? 0.0f : (float)CullMode.Back);
                        copy.SetFloat("_CullModeForward", mode == 0 ? 0.0f : (float)CullMode.Back);
                        copy.SetFloat("_MonolithDebug", 85);
                        for (int warm = 0; warm < 4; warm++)
                        {
                            camera.Render();
                            yield return null;
                        }
                        foreach (Color pixel in target.Pixels()) if (pixel.g > 0.5f) coverage[mode]++;
                        target.Save(Path.Combine(output, "Winding_" + (mode == 0 ? "CullOff" : "CullBack")));
                    }
                }
            }
            finally
            {
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            File.WriteAllText(Path.Combine(output, "Flora_Culling_Winding.csv"), "Source,Mesh,Triangles,Degenerate,NormalAgreesWithClockwiseFront,ReversedDuplicatePairs,NegativeDeterminantInstances,Instances,AlphaPixelsCullOff,AlphaPixelsCullBack\n" + string.Join(",", name, Csv(AssetDatabase.GetAssetPath(mesh)), triangles.Length / 3, degenerate, agree, reversedPairs, negative, set.InstanceCount, coverage[0], coverage[1]) + "\n");
            Assert.Greater(coverage[0], 0);
            Assert.LessOrEqual(coverage[1], coverage[0]);
        }

        private IEnumerator Capture(string output, XRayInstanceBatch batch, Material stored, Texture2D authored, Texture2D correction, bool trueStalker)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            var metrics = new StringBuilder("Distance,Mode,Debug,AlphaPixels,NonFinite,MaxDeltaToLegacy\n");
            var shadows = new StringBuilder("Distance,Mode,ShadowedReceiverPixels\n");
            try
            {
                var geometry = new GameObject("Read-only flora A/B");
                temporary.Add(geometry);
                SceneManager.MoveGameObjectToScene(geometry, scene);
                Matrix4x4 matrix = batch.Matrices[0];
                geometry.transform.SetPositionAndRotation(matrix.GetColumn(3), matrix.rotation);
                geometry.transform.localScale = matrix.lossyScale;
                geometry.AddComponent<MeshFilter>().sharedMesh = batch.Mesh;
                MeshRenderer renderer = geometry.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.On;
                Material material = new Material(stored);
                temporary.Add(material);
                renderer.sharedMaterial = material;
                Bounds bounds = renderer.bounds;
                GameObject receiver = GameObject.CreatePrimitive(PrimitiveType.Quad);
                temporary.Add(receiver);
                SceneManager.MoveGameObjectToScene(receiver, scene);
                receiver.transform.position = new Vector3(bounds.center.x, bounds.min.y - 0.05f, bounds.center.z);
                receiver.transform.rotation = Quaternion.Euler(90, 0, 0);
                receiver.transform.localScale = Vector3.one * bounds.extents.magnitude * 6.0f;
                var receiverMaterial = new Material(Shader.Find("HDRP/Lit"));
                temporary.Add(receiverMaterial);
                receiver.GetComponent<MeshRenderer>().sharedMaterial = receiverMaterial;
                Camera camera = Rig(scene, bounds, temporary, true);
                Vector3 center = bounds.center;
                Vector3 direction = (camera.transform.position - center).normalized;
                File.WriteAllText(Path.Combine(output, "Settings.txt"), "Batch=" + batch.Name + "\nMesh=" + AssetDatabase.GetAssetPath(batch.Mesh) + "\nMaterial=" + AssetDatabase.GetAssetPath(stored) + "\nAuthored=" + AssetDatabase.GetAssetPath(authored) + "\nAPI=" + SystemInfo.graphicsDeviceType + "\nDevice=" + SystemInfo.graphicsDeviceName + "\nResolution=640x360\nFOV=50\nSun=3 lux directional with shadows\nExposure=0 fixed\nInstanceMatrix=" + matrix.ToString("F6") + "\n");
                using (var target = new Target(camera, 640, 360))
                {
                    foreach (float distance in new[] { 1.0f, 3.0f, 9.0f })
                    {
                        camera.transform.position = center + direction * bounds.extents.magnitude * distance;
                        camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, Vector3.up);
                        var legacy = new Dictionary<int, Color[]>();
                        foreach (string mode in authored != null ? new[] { "Legacy", "SourcePng", "SourceAuthoredMips", "SourceAuthoredMipsCullBack" } : new[] { "Legacy", "Source", "SourceCullBack" })
                        {
                            material.CopyPropertiesFromMaterial(stored);
                            if (mode != "Legacy")
                            {
                                Assert.IsTrue(MonolithMaterialConfigurator.ConfigureFloraSource(material, trueStalker ? MonolithMaterialConfigurator.FLORA_SOURCE_TRUE_STALKER : MonolithMaterialConfigurator.FLORA_SOURCE_ANOMALY, correction));
                                material.SetFloat("_MonolithSourceProfile", trueStalker ? 0.0f : 1.0f);
                            }
                            if (mode.StartsWith("SourceAuthored", StringComparison.Ordinal))
                            {
                                material.SetTexture("_BaseColorMap", authored);
                                material.SetFloat("_MonolithAlbedoSrgb", 0.0f);
                            }
                            if (mode.EndsWith("CullBack", StringComparison.Ordinal))
                            {
                                material.SetFloat("_DoubleSidedEnable", 0.0f);
                                material.SetFloat("_CullMode", (float)CullMode.Back);
                                material.SetFloat("_CullModeForward", (float)CullMode.Back);
                                material.DisableKeyword("_DOUBLESIDED_ON");
                            }
                            for (int warm = 0; warm < 4; warm++)
                            {
                                camera.Render();
                                yield return null;
                            }
                            foreach (int debug in new[] { 0, 1, 12, 67, 68, 85 })
                            {
                                material.SetFloat("_MonolithDebug", debug);
                                camera.Render();
                                camera.Render();
                                Color[] pixels = target.Pixels();
                                int alpha = 0;
                                int nonFinite = 0;
                                float delta = 0;
                                for (int pixel = 0; pixel < pixels.Length; pixel++)
                                {
                                    for (int channel = 0; channel < 3; channel++) if (float.IsNaN(pixels[pixel][channel]) || float.IsInfinity(pixels[pixel][channel])) nonFinite++;
                                    if (debug == 85 && pixels[pixel].g > 0.5f) alpha++;
                                    if (legacy.TryGetValue(debug, out Color[] baseline)) for (int channel = 0; channel < 3; channel++) delta = Mathf.Max(delta, Mathf.Abs(pixels[pixel][channel] - baseline[pixel][channel]));
                                }
                                if (mode == "Legacy") legacy[debug] = pixels;
                                metrics.AppendLine(string.Join(",", distance.ToString(CultureInfo.InvariantCulture), mode, debug, alpha, nonFinite, delta.ToString("R", CultureInfo.InvariantCulture)));
                                Assert.AreEqual(0, nonFinite, mode + " debug " + debug);
                                target.Save(Path.Combine(output, "D" + distance.ToString(CultureInfo.InvariantCulture) + "_" + mode + "_Debug" + debug));
                            }
                            material.SetFloat("_MonolithDebug", 0);
                            receiverMaterial.SetColor("_BaseColor", Color.white);
                            renderer.shadowCastingMode = ShadowCastingMode.Off;
                            camera.Render();
                            camera.Render();
                            Color[] unshadowed = target.Pixels();
                            renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                            camera.Render();
                            camera.Render();
                            Color[] shadowedFrame = target.Pixels();
                            renderer.shadowCastingMode = ShadowCastingMode.On;
                            int shadowed = 0;
                            for (int pixel = 0; pixel < shadowedFrame.Length; pixel++) if (shadowedFrame[pixel].grayscale < unshadowed[pixel].grayscale * 0.5f) shadowed++;
                            target.Save(Path.Combine(output, "D" + distance.ToString(CultureInfo.InvariantCulture) + "_" + mode + "_ShadowOnly"));
                            shadows.AppendLine(string.Join(",", distance.ToString(CultureInfo.InvariantCulture), mode, shadowed));
                        }
                    }
                }
            }
            finally
            {
                File.WriteAllText(Path.Combine(output, "Metrics.csv"), metrics.ToString());
                File.WriteAllText(Path.Combine(output, "Shadow_Silhouette.csv"), shadows.ToString());
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Camera Rig(Scene scene, Bounds bounds, List<UnityEngine.Object> temporary, bool shadows)
        {
            var rig = new GameObject("Flora rig");
            temporary.Add(rig);
            SceneManager.MoveGameObjectToScene(rig, scene);
            Camera camera = new GameObject("Flora Camera").AddComponent<Camera>();
            camera.transform.SetParent(rig.transform);
            camera.scene = scene;
            camera.enabled = false;
            camera.fieldOfView = 50;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 1000;
            camera.allowHDR = true;
            camera.useOcclusionCulling = false;
            camera.transform.position = bounds.center + new Vector3(0.35f, 0.25f, -1.0f).normalized * Mathf.Max(2.0f, bounds.extents.magnitude * 1.1f);
            camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);
            HDAdditionalCameraData data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            data.customRenderingSettings = true;
            data.volumeLayerMask = ~0;
            foreach (FrameSettingsField field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.ContactShadows, FrameSettingsField.Volumetrics, FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            temporary.Add(profile);
            profile.Add<Fog>(true).enabled.Override(false);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
            Exposure exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(0);
            VisualEnvironment environment = profile.Add<VisualEnvironment>(true);
            environment.skyType.Override(SkySettings.GetUniqueID<GradientSky>());
            GradientSky sky = profile.Add<GradientSky>(true);
            sky.top.Override(new Color(0.1f, 0.12f, 0.16f));
            sky.middle.Override(new Color(0.06f, 0.07f, 0.09f));
            sky.bottom.Override(new Color(0.03f, 0.025f, 0.02f));
            Volume volume = rig.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10000;
            volume.sharedProfile = profile;
            Light sun = new GameObject("Flora Sun").AddComponent<Light>();
            sun.transform.SetParent(rig.transform);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(60, -30, 0);
            sun.shadows = shadows ? LightShadows.Hard : LightShadows.None;
            sun.gameObject.AddComponent<HDAdditionalLightData>().SetIntensity(3, LightUnit.Lux);
            rig.AddComponent<MonolithWeatherInputs>().SetInputs(0.71864f, Vector3.one, new Vector4(0.5f, 1, 0, 0), new Vector4(1.5f, 1, 0, 0), 0);
            rig.AddComponent<MonolithSourceLighting>().Mode = MonolithSourceLighting.LightingMode.Native;
            return camera;
        }

        private static IEnumerator Import(XRayFloraLevelFixture fixture, XRayFloraImportMode mode)
        {
            Assert.IsNull(XRayLevelImportJob.Current);
            string previousRoot = XRayLevelPaths.Root;
            Assert.IsTrue(XRayLevelImportJob.Enqueue(fixture.Request(mode)));
            double start = EditorApplication.timeSinceStartup;
            while (XRayLevelImportJob.Current != null)
            {
                if (EditorApplication.timeSinceStartup - start > 300)
                {
                    XRayLevelImportJob.CancelCurrent();
                    Assert.Fail("Full flora import exceeded 300 seconds.");
                }
                yield return null;
            }
            Assert.AreEqual(XRayLevelJobState.Completed, XRayLevelImportJob.LastState, string.Join("; ", XRayLevelImportJob.LastRecord.Errors));
            Assert.IsTrue(XRayLevelImportJob.LastRecord.Completed);
            Assert.AreEqual(previousRoot, XRayLevelPaths.Root);
            AssetDatabase.SaveAssets();
            var manifest = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(XRayLevelPaths.ManifestPath(fixture.Root, fixture.LevelName));
            Assert.IsNotNull(manifest);
            Assert.IsTrue(manifest.Record.Completed);
            Assert.AreEqual(XRayLevelImportJob.LastRecord.Source.FloraProfile, manifest.Record.Source.FloraProfile);
            Assert.IsTrue(File.Exists(manifest.Record.PrefabPath));
            GameObject prefab = PrefabUtility.LoadPrefabContents(manifest.Record.PrefabPath);
            try
            {
                XRayLevelInstancedRenderer renderer = prefab.GetComponentInChildren<XRayLevelInstancedRenderer>(true);
                Assert.IsNotNull(renderer, "Tree batches must be referenced by the level instanced renderer.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
            string output = fixture.Output();
            if (output != null)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "Import_" + mode + "_" + DateTime.UtcNow.Ticks + "_Manifest.json"), XRayLevelImportJob.LastRecord.ToJson());
                File.WriteAllText(Path.Combine(output, "Import_" + mode + "_" + DateTime.UtcNow.Ticks + "_Material.json"), EditorJsonUtility.ToJson(fixture.Material(), true));
            }
        }

        private static void WriteComparison(XRayFloraLevelFixture fixture, Dictionary<string, string> before, Dictionary<string, string> after, string stage)
        {
            string output = fixture.Output();
            if (output == null) return;
            Directory.CreateDirectory(output);
            var rows = new List<string> { "Stage,Asset,BeforeSHA256,AfterSHA256,GUID,Equal" };
            foreach (KeyValuePair<string, string> pair in after)
            {
                before.TryGetValue(pair.Key, out string old);
                rows.Add(stage + "," + Csv(pair.Key) + "," + old + "," + pair.Value + "," + AssetDatabase.AssetPathToGUID(pair.Key) + "," + (old == pair.Value));
            }
            File.WriteAllLines(Path.Combine(output, stage + ".csv"), rows);
        }

        private static Dictionary<string, string> Snapshot(string root)
        {
            var result = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) result[file] = XRayLevelSourceRefresh.HashFile(file);
            return result;
        }

        private static string Key(Vector3 a, Vector3 b, Vector3 c)
        {
            string[] parts = { a.ToString("F5"), b.ToString("F5"), c.ToString("F5") };
            int start = 0;
            for (int i = 1; i < 3; i++) if (string.CompareOrdinal(parts[i], parts[start]) < 0) start = i;
            return parts[start] + "|" + parts[(start + 1) % 3] + "|" + parts[(start + 2) % 3];
        }

        private static Texture2D LegacyPng(string virtualPath)
        {
            string name = Path.GetFileNameWithoutExtension(virtualPath);
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Texture2D", new[] { ANOMALY_ROOT + "/Textures" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == name + ".png") return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        private Color32[] Read(Material material, Texture2D texture, int mip, int width, int height)
        {
            material.SetTexture("_Texture", texture);
            material.SetFloat("_Mode", 0);
            material.SetFloat("_Mip", mip);
            material.SetVector("_Uv", new Vector4(1, 1, 0, 0));
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(null, target, material, 0);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readback.Apply();
                return readback.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private static Color[] Render(Material material, int width, int height, RenderTextureFormat format)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, format, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            try
            {
                Graphics.Blit(null, target, material, 0);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readback.Apply();
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private Material DetailMaterial()
        {
            Material material = Own(new Material(Shader.Find(MonolithMaterialConfigurator.SHADER_NAME)));
            material.EnableKeyword("_MONOLITH_LEVEL_DETAIL");
            foreach (string property in new[] { "_AlphaCutoff", "_AlphaCutoffShadow", "_AlphaCutoffPrepass", "_AlphaCutoffPostpass" }) material.SetFloat(property, XRayLevelMaterialLibrary.DETAIL_AREF);
            material.SetFloat("_DoubleSidedEnable", 1.0f);
            material.SetFloat("_CullMode", 0.0f);
            return material;
        }

        private static XRayLevelSurface DetailSurface()
        {
            return new XRayLevelSurface { Kind = XRayLevelSurfaceKind.AlphaTest, AlphaCutoff = XRayLevelMaterialLibrary.DETAIL_AREF, DoubleSided = true, CastShadows = false, RuleName = "detail", BlenderClass = "D_STILL", HemiMode = XRayLevelHemiMode.Constant };
        }

        private static XRayLevelSurface LeafSurface()
        {
            return XRayLevelSurfaceRules.Classify(new XRayLevelShader(1, "flora\\leaf_wave/trees\\leaf"), null, true);
        }

        private Texture2D Asset(Texture2D texture, string name)
        {
            texture.Apply(false, false);
            AssetDatabase.CreateAsset(texture, _temp + "/" + name);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(_temp + "/" + name);
        }

        private string TempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TEMP_PARENT)) AssetDatabase.CreateFolder("Assets", TEMP_PARENT.Substring("Assets/".Length));
            string name = Guid.NewGuid().ToString("N").Substring(0, 8);
            string folder = TEMP_PARENT + "/" + name;
            Assert.IsFalse(Directory.Exists(folder));
            AssetDatabase.CreateFolder(TEMP_PARENT, name);
            return folder;
        }

        private T Own<T>(T item) where T : UnityEngine.Object
        {
            _owned.Add(item);
            return item;
        }

        private static byte[] Dds(int width, int height, string fourCC, List<byte[]> levels)
        {
            var header = new byte[128];
            Write(header, 0, 0x20534444);
            Write(header, 4, 124);
            Write(header, 8, 0x1 | 0x2 | 0x4 | 0x1000 | 0x80000 | (levels.Count > 1 ? 0x20000u : 0u));
            Write(header, 12, (uint)height);
            Write(header, 16, (uint)width);
            Write(header, 20, (uint)levels[0].Length);
            Write(header, 28, (uint)levels.Count);
            Write(header, 76, 32);
            Write(header, 80, 0x4);
            byte[] code = Encoding.ASCII.GetBytes(fourCC);
            for (int i = 0; i < 4; i++) header[84 + i] = code[i];
            Write(header, 108, 0x1000 | (levels.Count > 1 ? 0x400008u : 0u));
            var data = new List<byte>(header);
            foreach (byte[] level in levels) data.AddRange(level);
            return data.ToArray();
        }

        private static void Write(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private static string Sha(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Csv(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }

        private static string Output()
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE06B1_OUTPUT");
            Assert.IsNotEmpty(output);
            Assert.IsTrue(Path.GetFullPath(output).StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(output);
            return output;
        }

        private sealed class Target : IDisposable
        {
            private readonly Camera _camera;
            private readonly RenderTexture _target;
            private readonly Texture2D _pixels;
            private readonly int _width;
            private readonly int _height;

            public Target(Camera camera, int width, int height)
            {
                _camera = camera;
                _width = width;
                _height = height;
                _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                _pixels = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                _target.Create();
                camera.aspect = (float)width / height;
                camera.targetTexture = _target;
            }

            public Color[] Pixels()
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _target;
                _pixels.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
                _pixels.Apply();
                RenderTexture.active = previous;
                return _pixels.GetPixels();
            }

            public void Save(string path)
            {
                File.WriteAllBytes(path + ".exr", _pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                var display = new Texture2D(_width, _height, TextureFormat.RGBA32, false, true);
                try
                {
                    display.SetPixels(_pixels.GetPixels());
                    display.Apply();
                    File.WriteAllBytes(path + ".png", display.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(display);
                }
            }

            public void Dispose()
            {
                _camera.targetTexture = null;
                _target.Release();
                UnityEngine.Object.DestroyImmediate(_pixels);
                UnityEngine.Object.DestroyImmediate(_target);
            }
        }
    }
}
