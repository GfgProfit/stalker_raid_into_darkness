using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherResourceTests
    {
        [TestCase(DdsFormat.BC1, 0)]
        [TestCase(DdsFormat.BC2, 0)]
        [TestCase(DdsFormat.BC3, 0)]
        [TestCase(DdsFormat.BC7, 0)]
        [TestCase(DdsFormat.Rgba32, 0)]
        [TestCase(DdsFormat.Bgra32, 0)]
        [TestCase(DdsFormat.Bgr24, 0)]
        [TestCase(DdsFormat.RgbaHalf, 0)]
        [TestCase(DdsFormat.RgbaHalf, 48)]
        public void CubemapFacesAndAuthoredMipsRemainExact(DdsFormat format, int trailingBytes)
        {
            byte[] source = Cube(format);
            if (trailingBytes > 0) Array.Resize(ref source, source.Length + trailingBytes);
            DdsHeader header = DdsHeader.Parse(source);
            using (var fixture = new Fixture(source))
            {
                var importer = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>()) { ContentKeys = true };
                XRayTextureAssetResult result = importer.ImportWeather(fixture.File, fixture.AssetsFolder, true);
                Assert.AreEqual(XRayResourceStatus.Imported, result.Status, result.Note);
                if (trailingBytes > 0) StringAssert.Contains("trailing source bytes outside the declared surfaces=" + trailingBytes, result.Note);
                var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(result.AssetPath);
                Assert.IsNotNull(cube);
                Assert.AreEqual(header.Width, cube.width);
                Assert.AreEqual(header.MipCount, cube.mipmapCount);
                Assert.IsFalse(GraphicsFormatUtility.IsSRGBFormat(cube.graphicsFormat));
                Assert.AreEqual(FilterMode.Bilinear, cube.filterMode);
                Assert.AreEqual(TextureWrapMode.Clamp, cube.wrapMode);
                Assert.IsFalse(cube.isReadable);
                string guid = AssetDatabase.AssetPathToGUID(result.AssetPath);
                var serialized = new SerializedObject(cube);
                serialized.FindProperty("m_IsReadable").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                int offset = header.DataOffset;
                for (int face = 0; face < 6; face++)
                {
                    int width = header.Width;
                    int height = header.Height;
                    for (int mip = 0; mip < header.MipCount; mip++)
                    {
                        int length = DdsHeader.GetLevelSize(format, width, height);
                        byte[] expected = Expected(source, offset, length, format);
                        CollectionAssert.AreEqual(expected, cube.GetPixelData<byte>(mip, (CubemapFace)face).ToArray(), format + " face=" + face + " mip=" + mip);
                        offset += length;
                        width = Math.Max(1, width >> 1);
                        height = Math.Max(1, height >> 1);
                    }
                }
                var reimporter = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>()) { ContentKeys = true };
                XRayTextureAssetResult repeated = reimporter.ImportWeather(fixture.File, fixture.AssetsFolder, true);
                Assert.AreEqual(XRayResourceStatus.Reused, repeated.Status);
                Assert.AreEqual(result.AssetPath, repeated.AssetPath);
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(repeated.AssetPath));
                CollectionAssert.AreEqual(source, System.IO.File.ReadAllBytes(fixture.File.Winner.FullPath));
            }
        }

        [Test]
        public void HalfFloatMetadataPreservesEightBytePixelsWithoutLegacyDecoding()
        {
            DdsHeader header = DdsHeader.Parse(Cube(DdsFormat.RgbaHalf));
            Assert.AreEqual(DdsFormat.RgbaHalf, header.Format);
            Assert.AreEqual(8, DdsHeader.GetBytesPerPixel(header.Format));
            Assert.AreEqual(128, DdsHeader.GetLevelSize(header.Format, 4, 4));
            Assert.IsFalse(DdsDecoder.CanDecode(header));
        }

        [Test]
        public void ImportedCubemapPreservesAsymmetricD3dFaceOrientationOnGpu()
        {
            byte[] source = Cube(DdsFormat.Rgba32);
            DdsHeader header = DdsHeader.Parse(source);
            int faceLength = 4 * 4 * 4 + 2 * 2 * 4 + 4;
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int offset = header.DataOffset + face * faceLength + (y * 4 + x) * 4;
                        source[offset] = (byte)(face * 29 + x * 7 + 1);
                        source[offset + 1] = (byte)(y * 47 + face * 3 + 2);
                        source[offset + 2] = (byte)(x * 13 + y * 19 + face * 5 + 3);
                        source[offset + 3] = 255;
                    }
                }
            }
            using (var fixture = new Fixture(source))
            {
                var importer = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>());
                XRayTextureAssetResult result = importer.ImportWeather(fixture.File, fixture.AssetsFolder, true);
                Assert.AreEqual(XRayResourceStatus.Imported, result.Status, result.Note);
                Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(result.AssetPath);
                ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Shaders/XRayWeatherCubeProbe.compute");
                Assert.IsNotNull(shader);
                int kernel = shader.FindKernel("Probe");
                using (var buffer = new ComputeBuffer(1, sizeof(float) * 4))
                {
                    shader.SetTexture(kernel, "_Cube", cube);
                    shader.SetBuffer(kernel, "_Result", buffer);
                    var actual = new Vector4[1];
                    for (int face = 0; face < 6; face++)
                    {
                        for (int v = 0; v < 2; v++)
                        {
                            for (int u = 0; u < 2; u++)
                            {
                                float sc = u == 0 ? -0.5f : 0.5f;
                                float tc = v == 0 ? -0.5f : 0.5f;
                                Vector3 direction = face switch { 0 => new Vector3(1.0f, -tc, -sc), 1 => new Vector3(-1.0f, -tc, sc), 2 => new Vector3(sc, 1.0f, tc), 3 => new Vector3(sc, -1.0f, -tc), 4 => new Vector3(sc, -tc, 1.0f), _ => new Vector3(-sc, -tc, -1.0f) };
                                shader.SetVector("_Direction", direction);
                                shader.Dispatch(kernel, 1, 1, 1);
                                buffer.GetData(actual);
                                int x = u == 0 ? 1 : 3;
                                int y = v == 0 ? 1 : 3;
                                int offset = header.DataOffset + face * faceLength + (y * 4 + x) * 4;
                                for (int channel = 0; channel < 4; channel++) Assert.That(actual[0][channel], Is.EqualTo(source[offset + channel] / 255.0f).Within(0.00001f), "face=" + face + " u=" + u + " v=" + v + " channel=" + channel);
                            }
                        }
                    }
                }
            }
        }

        [TestCase("truncated")]
        [TestCase("missing-face")]
        [TestCase("wrong-shape")]
        public void InvalidCubeLayoutDoesNotCreateTextureAssets(string error)
        {
            byte[] source = Cube(DdsFormat.BC1);
            if (error == "truncated") Array.Resize(ref source, source.Length - 1);
            if (error == "missing-face") Write(source, 112, 0x200);
            using (var fixture = new Fixture(source))
            {
                var importer = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>());
                XRayTextureAssetResult result = importer.ImportWeather(fixture.File, fixture.AssetsFolder, error != "wrong-shape");
                Assert.AreEqual(XRayResourceStatus.Unsupported, result.Status);
                Assert.IsNull(result.AssetPath);
                Assert.AreEqual(0, AssetDatabase.FindAssets("t:Texture", new[] { fixture.AssetsFolder }).Length);
            }
        }

        private static byte[] Expected(byte[] source, int offset, int length, DdsFormat format)
        {
            var expected = new byte[format == DdsFormat.Bgr24 ? length / 3 * 4 : length];
            if (format == DdsFormat.Bgr24)
            {
                for (int pixel = 0; pixel < length / 3; pixel++)
                {
                    expected[pixel * 4] = source[offset + pixel * 3 + 2];
                    expected[pixel * 4 + 1] = source[offset + pixel * 3 + 1];
                    expected[pixel * 4 + 2] = source[offset + pixel * 3];
                    expected[pixel * 4 + 3] = 255;
                }
            }
            else Buffer.BlockCopy(source, offset, expected, 0, length);
            return expected;
        }

        private static byte[] Cube(DdsFormat format)
        {
            int dataOffset = format == DdsFormat.BC7 ? 148 : 128;
            int faceLength = DdsHeader.GetLevelSize(format, 4, 4) + DdsHeader.GetLevelSize(format, 2, 2) + DdsHeader.GetLevelSize(format, 1, 1);
            var bytes = new byte[dataOffset + faceLength * 6];
            Write(bytes, 0, DdsHeader.MAGIC);
            Write(bytes, 4, 124);
            Write(bytes, 8, 0xA1007);
            Write(bytes, 12, 4);
            Write(bytes, 16, 4);
            Write(bytes, 28, 3);
            Write(bytes, 76, 32);
            Write(bytes, 108, 0x401008);
            Write(bytes, 112, 0xFE00);
            if (format == DdsFormat.BC1 || format == DdsFormat.BC2 || format == DdsFormat.BC3 || format == DdsFormat.BC7 || format == DdsFormat.RgbaHalf)
            {
                Write(bytes, 80, 4);
                uint fourCc = format == DdsFormat.BC1 ? 0x31545844u : format == DdsFormat.BC2 ? 0x33545844u : format == DdsFormat.BC3 ? 0x35545844u : format == DdsFormat.BC7 ? 0x30315844u : 113u;
                Write(bytes, 84, fourCc);
                if (format == DdsFormat.BC7)
                {
                    Write(bytes, 128, DdsHeader.DXGI_BC7_UNORM);
                    Write(bytes, 132, 3);
                    Write(bytes, 136, 4);
                    Write(bytes, 140, 1);
                }
            }
            else
            {
                Write(bytes, 80, format == DdsFormat.Bgr24 ? 0x40u : 0x41u);
                Write(bytes, 88, format == DdsFormat.Bgr24 ? 24u : 32u);
                Write(bytes, 92, format == DdsFormat.Rgba32 ? 0xFFu : 0xFF0000u);
                Write(bytes, 96, 0xFF00);
                Write(bytes, 100, format == DdsFormat.Rgba32 ? 0xFF0000u : 0xFFu);
                Write(bytes, 104, format == DdsFormat.Bgr24 ? 0u : 0xFF000000u);
            }
            int offset = dataOffset;
            for (int face = 0; face < 6; face++)
            {
                int size = 4;
                for (int mip = 0; mip < 3; mip++)
                {
                    int length = DdsHeader.GetLevelSize(format, size, size);
                    for (int i = 0; i < length; i++) bytes[offset + i] = (byte)((face * 41 + mip * 13 + i * 7) & 255);
                    offset += length;
                    size = Math.Max(1, size >> 1);
                }
            }
            return bytes;
        }

        private static void Write(byte[] bytes, int offset, uint value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture(byte[] source)
            {
                string id = Guid.NewGuid().ToString("N");
                string archive = Environment.GetEnvironmentVariable("XRAY_WEATHER_RESOURCE_TEST_DIR") ?? "E:/Projects/stalker_raid_into_darkness_Archive/GraphicsPort/Phase07_WeatherAtmosphere/Phase07B/ResourceTests";
                string folder = Path.Combine(archive, id, "Source");
                Directory.CreateDirectory(Path.Combine(folder, "textures"));
                System.IO.File.WriteAllBytes(Path.Combine(folder, "textures/cube.dds"), source);
                XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(new XRaySourceConfiguration { Mode = XRaySourceMode.CustomFolder, CustomFolder = folder, IncludeGameGamedata = false });
                Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
                var resolver = new XRayTextureResolver(XRayAssetIndex.Build(plan));
                File = resolver.ResolveFile("textures/cube.dds");
                Assert.IsTrue(File.IsResolved);
                AssetsFolder = "Assets/__XRayWeatherResources_" + id;
                AssetDatabase.CreateFolder("Assets", "__XRayWeatherResources_" + id);
            }

            public XRayResolvedFile File { get; }
            public string AssetsFolder { get; }

            public void Dispose()
            {
                AssetDatabase.DeleteAsset(AssetsFolder);
            }
        }
    }
}
