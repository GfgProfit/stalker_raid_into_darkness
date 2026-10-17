using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Shaders;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class TextureResolutionTests
    {
        [Test]
        public void ThmDeclaredBumpIsResolvedWithCorrectionMap()
        {
            using (var directory = new TestDirectory())
            {
                var fixture = new Mo2Fixture(directory);
                fixture.WriteIni("Main");
                fixture.AddProfile("Main", "+Retexture");
                fixture.AddBaseFile("textures/wpn/wpn_ak74/wpn_ak74.dds", XRayTestBinaries.Bc1Solid(4, 4, 255, 0, 0));
                fixture.AddBaseFile("textures/wpn/wpn_ak74/wpn_ak74.thm", XRayTestBinaries.Thm("wpn\\wpn_ak74\\wpn_ak74_bump", 0));
                fixture.AddModFile("Retexture", "textures/wpn/wpn_ak74/wpn_ak74_bump.dds", XRayTestBinaries.Bc3Solid(4, 4, 128, 128, 128, 255));
                fixture.AddBaseFile("textures/wpn/wpn_ak74/wpn_ak74_bump#.dds", XRayTestBinaries.Bc1Solid(4, 4, 128, 128, 128));
                XRayTextureResolution resolution = Resolve(fixture, "wpn\\wpn_ak74\\wpn_ak74");
                Assert.IsTrue(resolution.Diffuse.IsResolved);
                StringAssert.StartsWith("thm", resolution.DescriptionOrigin);
                Assert.IsTrue(resolution.Bump.IsResolved);
                Assert.AreEqual("Retexture", resolution.Bump.Winner.SourceName);
                Assert.IsTrue(resolution.BumpCorrection.IsResolved);
                Assert.AreEqual(Mo2VirtualFileSystem.BASE_ANOMALY_NAME, resolution.BumpCorrection.Winner.SourceName);
            }
        }

        [Test]
        public void TexturesLtxSpecificationIsUsedWithoutThm()
        {
            using (var directory = new TestDirectory())
            {
                var fixture = new Mo2Fixture(directory);
                fixture.WriteIni("Main");
                fixture.AddProfile("Main");
                fixture.AddBaseFile("textures.ltx", Encoding.ASCII.GetBytes("[association]\n        wpn\\wpn_box = detail\\detail_metal, 4.000000, usage[diffuse_or_bump]\n[specification]\n        wpn\\wpn_box = bump_mode[use:wpn\\wpn_box_bump], material[2.85]\n[types]\n        wpn\\wpn_box = 0\n"));
                fixture.AddBaseFile("textures/wpn/wpn_box.dds", XRayTestBinaries.Bc1Solid(4, 4, 0, 255, 0));
                fixture.AddBaseFile("textures/wpn/wpn_box_bump.dds", XRayTestBinaries.Bc3Solid(4, 4, 0, 0, 0, 0));
                XRayTextureResolution resolution = Resolve(fixture, "wpn\\wpn_box");
                StringAssert.StartsWith("textures.ltx", resolution.DescriptionOrigin);
                Assert.IsTrue(resolution.Bump.IsResolved);
                Assert.IsFalse(resolution.BumpCorrection.IsResolved);
                Assert.IsTrue(resolution.Notes.Exists(note => note.Contains("detail\\detail_metal")));
            }
        }

        [Test]
        public void UndeclaredBumpIsNotInvented()
        {
            using (var directory = new TestDirectory())
            {
                var fixture = new Mo2Fixture(directory);
                fixture.WriteIni("Main");
                fixture.AddProfile("Main");
                fixture.AddBaseFile("textures/wpn/plain.dds", XRayTestBinaries.Bc1Solid(4, 4, 0, 0, 255));
                fixture.AddBaseFile("textures/wpn/plain_bump.dds", XRayTestBinaries.Bc3Solid(4, 4, 0, 0, 0, 0));
                XRayTextureResolution resolution = Resolve(fixture, "wpn\\plain");
                Assert.IsNull(resolution.Bump);
            }
        }

        [Test]
        public void SameBasenameFromDifferentPathsGetsDistinctAssetNames()
        {
            var existing = new Dictionary<string, string>();
            var claimed = new HashSet<string>();
            string first = XRayAssetNaming.Place("Assets/M/Textures", XRayAssetNaming.CreateCandidates("textures\\wpn\\ak74\\body_diff.dds", string.Empty), ".dds", "a", path => existing.TryGetValue(path, out string key) ? key : null, claimed);
            existing[first] = "a";
            string second = XRayAssetNaming.Place("Assets/M/Textures", XRayAssetNaming.CreateCandidates("textures\\wpn\\aks74\\body_diff.dds", string.Empty), ".dds", "b", path => existing.TryGetValue(path, out string key) ? key : null, new HashSet<string>());
            existing[second] = "b";
            string again = XRayAssetNaming.Place("Assets/M/Textures", XRayAssetNaming.CreateCandidates("textures\\wpn\\aks74\\body_diff.dds", string.Empty), ".dds", "b", path => existing.TryGetValue(path, out string key) ? key : null, new HashSet<string>());
            Assert.AreEqual("Assets/M/Textures/body_diff.dds", first);
            Assert.AreEqual("Assets/M/Textures/aks74_body_diff.dds", second);
            Assert.AreEqual(second, again);
        }

        [Test]
        public void DdsHeadersAreParsed()
        {
            Assert.AreEqual(DdsFormat.BC1, DdsHeader.Parse(XRayTestBinaries.Bc1Solid(8, 4, 1, 2, 3)).Format);
            Assert.AreEqual(DdsFormat.BC3, DdsHeader.Parse(XRayTestBinaries.Bc3Solid(4, 4, 1, 2, 3, 4)).Format);
            Assert.AreEqual(DdsFormat.BC2, DdsHeader.Parse(XRayTestBinaries.Dxt3Solid(4, 4, 1, 2, 3, 4)).Format);
            Assert.AreEqual(DdsFormat.Bgra32, DdsHeader.Parse(XRayTestBinaries.Bgra32(1, 1, new byte[] { 1, 2, 3, 4 })).Format);
        }

        [Test]
        public void DdsBlocksAreDecoded()
        {
            byte[] bc1 = XRayTestBinaries.Bc1Solid(4, 4, 255, 0, 0);
            byte[] pixels = DdsDecoder.DecodeTopLevel(bc1, DdsHeader.Parse(bc1));
            CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, new[] { pixels[0], pixels[1], pixels[2], pixels[3] });
            byte[] bc3 = XRayTestBinaries.Bc3Solid(4, 4, 0, 255, 0, 77);
            pixels = DdsDecoder.DecodeTopLevel(bc3, DdsHeader.Parse(bc3));
            CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 77 }, new[] { pixels[60], pixels[61], pixels[62], pixels[63] });
            byte[] dxt3 = XRayTestBinaries.Dxt3Solid(4, 4, 0, 0, 255, 15);
            pixels = DdsDecoder.DecodeTopLevel(dxt3, DdsHeader.Parse(dxt3));
            Assert.AreEqual(255, pixels[3]);
            byte[] bgra = XRayTestBinaries.Bgra32(1, 1, new byte[] { 10, 20, 30, 40 });
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 40 }, DdsDecoder.DecodeTopLevel(bgra, DdsHeader.Parse(bgra)));
        }

        [Test]
        public void LegacyDdsIsRewrittenWithExplicitSrgbHeader()
        {
            byte[] bc1 = XRayTestBinaries.Bc1Solid(4, 4, 255, 0, 0);
            DdsHeader header = DdsHeader.Parse(bc1);
            Assert.IsTrue(DdsColorSpace.TryGetDxgiFormat(header.Format, true, out uint dxgi));
            byte[] rewritten = DdsColorSpace.WithDx10Header(bc1, header, dxgi);
            DdsHeader parsed = DdsHeader.Parse(rewritten);
            Assert.IsTrue(parsed.HasDx10Header);
            Assert.IsTrue(parsed.IsSrgb);
            Assert.AreEqual(DdsFormat.BC1, parsed.Format);
            CollectionAssert.AreEqual(DdsDecoder.DecodeTopLevel(bc1, header), DdsDecoder.DecodeTopLevel(rewritten, parsed));
        }

        [Test]
        public void XRayBumpIsConvertedToTangentSpaceNormal()
        {
            var bump = new byte[] { 200, 255, 128, 128 };
            byte[] normal = XRayBumpConverter.ToTangentNormalMap(bump, 1, 1, null, 0, 0, false);
            Assert.AreEqual(128, normal[0], 1);
            Assert.AreEqual(128, normal[1], 1);
            Assert.AreEqual(255, normal[2], 1);
            var tilted = new byte[] { 0, 128, 255, 128 };
            byte[] flipped = XRayBumpConverter.ToTangentNormalMap(tilted, 1, 1, null, 0, 0, true);
            byte[] straight = XRayBumpConverter.ToTangentNormalMap(tilted, 1, 1, null, 0, 0, false);
            Assert.Greater(straight[1], 200);
            Assert.Less(flipped[1], 55);
            var correction = new byte[] { 255, 128, 128, 0 };
            byte[] corrected = XRayBumpConverter.ToTangentNormalMap(bump, 1, 1, correction, 1, 1, false);
            Assert.Greater(corrected[0], 200);
        }

        [Test]
        public void ShaderRulesClassifyXRayShaders()
        {
            XRayShaderRules rules = XRayShaderRules.CreateDefault();
            Assert.AreEqual(XRaySurfaceKind.Opaque, rules.Resolve("models\\weapons").Kind);
            Assert.AreEqual(XRaySurfaceKind.Opaque, rules.Resolve("models\\model").Kind);
            Assert.AreEqual(XRaySurfaceKind.Transparent, rules.Resolve("models\\transparent").Kind);
            Assert.AreEqual(XRaySurfaceKind.Transparent, rules.Resolve("models\\lfo_glass_lens_weapons").Kind);
            Assert.AreEqual(XRaySurfaceKind.Collimator, rules.Resolve("models\\collimator").Kind);
            Assert.AreEqual(XRaySurfaceKind.Collimator, rules.Resolve("models\\lfo_light_dot_weapons").Kind);
            Assert.AreEqual(XRaySurfaceKind.AlphaTest, rules.Resolve("models\\model_aref").Kind);
            Assert.AreEqual(XRaySurfaceKind.Emissive, rules.Resolve("models\\selflight").Kind);
            XRaySurfaceDescription unknown = rules.Resolve("models\\something_new");
            Assert.AreEqual(XRaySurfaceKind.Opaque, unknown.Kind);
            Assert.AreEqual("unknown", unknown.RuleName);
        }

        private static XRayTextureResolution Resolve(Mo2Fixture fixture, string texture)
        {
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(fixture.CreateConfiguration("Main"));
            Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
            return new XRayTextureResolver(XRayAssetIndex.Build(plan)).Resolve(texture);
        }
    }
}
