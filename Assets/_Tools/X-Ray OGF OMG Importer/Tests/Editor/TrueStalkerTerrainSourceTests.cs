using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;

namespace XRay.Importer.Tests
{
    public sealed class TrueStalkerTerrainSourceTests
    {
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private int _readbacks;

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object item in _owned) UnityEngine.Object.DestroyImmediate(item);
            _owned.Clear();
        }

        [Test]
        public void OriginalHlslMatchesDrySurfaceAndExplicitLighting()
        {
            string output = TrueStalkerTerrainReference.Output();
            var rows = new StringBuilder("Case,Stage,Gamma22,MaxAbsoluteError,MaxRelativeError,ComparedComponents,Reference\n");
            Shader actualShader = Shader.Find("Hidden/XRay/TrueStalkerTerrainValidation");
            Assert.IsNotNull(actualShader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(actualShader), Messages(actualShader));
            var actual = Own(new Material(actualShader));
            for (int gamma = 0; gamma < 2; gamma++)
            {
                Shader shader = Own(TrueStalkerTerrainReference.Create(gamma == 1));
                Assert.IsFalse(ShaderUtil.ShaderHasError(shader), Messages(shader));
                var reference = Own(new Material(shader));
                Configure(actual);
                Configure(reference);
                actual.SetFloat("_Gamma22", gamma);
                foreach (Vector4 mask in new[] { new Vector4(1, 0, 0, 0), new Vector4(0, 0.6f, 0.4f, 0), new Vector4(0.1f, 0.2f, 0.3f, 0.4f), new Vector4(0.00001f, 0.49f, 0.5f, 0.00999f) })
                {
                    Texture2D maskTexture = Texture(mask, false);
                    actual.SetTexture("_Mask", maskTexture);
                    reference.SetTexture("_Mask", maskTexture);
                    for (int angle = 0; angle < 3; angle++)
                    {
                        Matrix4x4 transform = Matrix4x4.Rotate(Quaternion.Euler(angle * 23, angle * 37, angle * 11));
                        Vector3 geometric = new Vector3(0.1f, 0.2f, 0.76f).normalized;
                        Vector3 tangent = Vector3.Cross(geometric, Vector3.up).normalized;
                        Vector3 eye = -(geometric + tangent * (angle == 0 ? 0 : angle == 1 ? 1 : 20)).normalized * 4;
                        foreach (Material material in new[] { actual, reference })
                        {
                            material.SetMatrix("_ViewToWorld", transform);
                            material.SetVector("_Position", new Vector4(eye.x, eye.y, eye.z, 1));
                            material.SetVector("_Parameters", new Vector4(angle == 0 ? 1 : angle == 1 ? 17 : 200, 0, 0.95f, 0));
                        }
                        for (int stage = 0; stage <= 15; stage++)
                        {
                            actual.SetFloat("_Stage", stage);
                            reference.SetFloat("_Stage", stage);
                            Color[] expected = Render(reference);
                            Color[] observed = Render(actual);
                            double max = 0;
                            double relative = 0;
                            for (int pixel = 0; pixel < expected.Length; pixel++)
                            {
                                for (int channel = 0; channel < 4; channel++)
                                {
                                    Assert.IsFalse(float.IsNaN(expected[pixel][channel]) || float.IsInfinity(expected[pixel][channel]), "Original reference undefined stage " + stage);
                                    Assert.IsFalse(float.IsNaN(observed[pixel][channel]) || float.IsInfinity(observed[pixel][channel]), "Nonfinite output stage " + stage);
                                    max = Math.Max(max, Math.Abs(expected[pixel][channel] - observed[pixel][channel]));
                                    relative = Math.Max(relative, Math.Abs(expected[pixel][channel] - observed[pixel][channel]) / Math.Max(0.0000001, Math.Abs(expected[pixel][channel])));
                                }
                            }
                            rows.AppendLine(string.Join(",", "mask" + mask.ToString("F5").Replace(',', ';') + "_angle" + angle, stage, gamma, max.ToString("R", CultureInfo.InvariantCulture), relative.ToString("R", CultureInfo.InvariantCulture), expected.Length * 4, "original_TS_HLSL_explicit_inputs"));
                            File.WriteAllText(Path.Combine(output, "Source_Parity.csv"), rows.ToString());
                            Assert.LessOrEqual(max, 0.00002, "Stage " + stage + " gamma " + gamma + " angle " + angle);
                        }
                    }
                }
            }
        }

        [Test]
        public void SavedMeshAndOriginalTexturesMatchSourceExpressions()
        {
            const string ROOT = "Assets/_Source/Levels";
            const string LEVEL = "escape";
            Material stored = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/levels/escape/terrain/levels_escape_terrain_terrain_escape_terrain.mat");
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ROOT + "/escape/Meshes/Static/S14_C0886.asset");
            var preview = Own(new Material(stored));
            var actual = Own(new Material(Shader.Find("Hidden/XRay/TrueStalkerTerrainValidation")));
            var reference = Own(new Material(Own(TrueStalkerTerrainReference.Create(false))));
            Configure(actual);
            Configure(reference);
            var rows = new StringBuilder("Vertex,Stage,MaxAbsoluteError,MaxRelativeError,ComparedComponents\n");
            using (XRayTerrainDiagnosticBinding bindings = XRayTerrainDiagnosticBinding.Open(ROOT, LEVEL))
            {
                bindings.Bind(stored, preview, true);
                var colors = new List<Color>();
                var uv1 = new List<Vector2>();
                var uv2 = new List<Vector2>();
                mesh.GetColors(colors);
                mesh.GetUVs(1, uv1);
                mesh.GetUVs(2, uv2);
                foreach (int vertex in new[] { 0, mesh.vertexCount / 2, mesh.vertexCount - 1 })
                {
                    Vector3 t = new Vector3(colors[vertex].r, colors[vertex].g, colors[vertex].b);
                    Vector3 b = new Vector3(uv1[vertex].x, uv1[vertex].y, uv2[vertex].x);
                    Vector3 n = mesh.normals[vertex];
                    foreach (Material material in new[] { actual, reference })
                    {
                        material.SetTexture("_Base", preview.GetTexture("_MonolithTerrainSourceBase"));
                        material.SetTexture("_Mask", preview.GetTexture("_MonolithTerrainMask"));
                        foreach (char layer in "RGBA")
                        {
                            material.SetTexture("_Color" + layer, preview.GetTexture("_MonolithTerrainSourceColor" + layer));
                            material.SetTexture("_Bump" + layer, preview.GetTexture("_MonolithTerrainBump" + layer));
                        }
                        material.SetVector("_Uv", new Vector4(0, 0, mesh.uv[vertex].x, 1 - mesh.uv[vertex].y));
                        material.SetVector("_Parameters", new Vector4(preview.GetFloat("_MonolithTerrainDetailScale"), 0, 0.95f, 0));
                        material.SetVector("_Tangent", t * 4 - Vector3.one * 2);
                        material.SetVector("_Bitangent", b * 4 - Vector3.one * 2);
                        material.SetVector("_Geometric", n * 2);
                        material.SetVector("_RawT", new Vector4(t.z, t.y, t.x, 0));
                        material.SetVector("_RawB", new Vector4(b.z, b.y, b.x, 0));
                        material.SetVector("_RawN", new Vector4(n.z * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.x * 0.5f + 0.5f, uv2[vertex].y));
                    }
                    for (int stage = 0; stage <= 15; stage++)
                    {
                        actual.SetFloat("_Stage", stage);
                        reference.SetFloat("_Stage", stage);
                        double delta = Delta(Render(actual), Render(reference), out double relative);
                        rows.AppendLine(vertex + "," + stage + "," + delta.ToString("R", CultureInfo.InvariantCulture) + "," + relative.ToString("R", CultureInfo.InvariantCulture) + ",256");
                        File.WriteAllText(Path.Combine(TrueStalkerTerrainReference.Output(), "Saved_Inputs_Source_Parity.csv"), rows.ToString());
                        Assert.LessOrEqual(delta, 0.00002, "Saved mesh vertex " + vertex + " stage " + stage);
                    }
                }
            }
        }

        [Test]
        public void MaterialLutKeepsLocalTerrainAndDirectionalRemapSeparate()
        {
            var actual = Own(new Material(Shader.Find("Hidden/XRay/TrueStalkerTerrainValidation")));
            var reference = Own(new Material(Own(TrueStalkerTerrainReference.Create(false))));
            Configure(actual);
            Configure(reference);
            var rows = new StringBuilder("Material,Stage,MaxAbsoluteError,MaxRelativeError,ComparedComponents\n");
            foreach (float material in new[] { 0, 0.15f, 0.4f, 0.95f, 22.0f / 31 * 1.333333333f, 1.333333333f })
            {
                actual.SetVector("_Parameters", new Vector4(17, 0, material, 0));
                reference.SetVector("_Parameters", new Vector4(17, 0, material, 0));
                for (int stage = 10; stage < 14; stage++)
                {
                    actual.SetFloat("_Stage", stage);
                    reference.SetFloat("_Stage", stage);
                    double delta = Delta(Render(actual), Render(reference), out double relative);
                    rows.AppendLine(material.ToString("R", CultureInfo.InvariantCulture) + "," + stage + "," + delta.ToString("R", CultureInfo.InvariantCulture) + "," + relative.ToString("R", CultureInfo.InvariantCulture) + ",256");
                    File.WriteAllText(Path.Combine(TrueStalkerTerrainReference.Output(), "Material_Lut_Parity.csv"), rows.ToString());
                    Assert.LessOrEqual(delta, 0.00002);
                }
            }
        }

        [Test]
        public void ZeroMaskAndDegenerateBasisUseFiniteFallback()
        {
            var material = Own(new Material(Shader.Find("Hidden/XRay/TrueStalkerTerrainValidation")));
            Configure(material);
            material.SetTexture("_Mask", Texture(Vector4.zero, false));
            material.SetVector("_Tangent", Vector4.zero);
            material.SetVector("_Bitangent", Vector4.zero);
            material.SetVector("_Geometric", Vector4.zero);
            for (int stage = 0; stage < 10; stage++)
            {
                material.SetFloat("_Stage", stage);
                foreach (Color pixel in Render(material))
                {
                    for (int channel = 0; channel < 4; channel++) Assert.IsFalse(float.IsNaN(pixel[channel]) || float.IsInfinity(pixel[channel]));
                    if (stage == 0) Assert.AreEqual(new Color(1, 0, 0, 0), pixel);
                    if (stage == 2) Assert.AreEqual(new Vector3(0, 1, 0), new Vector3(pixel.r, pixel.g, pixel.b));
                }
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator VerifiedTsFullImportPreservesIdentityAndOverrides()
        {
            using (var fixture = new XRayTerrainLevelFixture(true))
            {
                yield return MonolithTerrainFullImportTests.Import(fixture, XRayTerrainImportMode.EnableVerifiedTrueStalker, XRayTerrainQuality.High);
                Material material = MonolithTerrainFullImportTests.Material(fixture);
                AssertTs(material, true);
                string path = AssetDatabase.GetAssetPath(material);
                string guid = AssetDatabase.AssetPathToGUID(path);
                string before = File.ReadAllText(path);
                yield return MonolithTerrainFullImportTests.Import(fixture, XRayTerrainImportMode.EnableVerifiedTrueStalker, XRayTerrainQuality.High);
                Assert.AreEqual(before, File.ReadAllText(path));
                material.SetFloat("_MonolithTrueStalkerSourceTerrain", 0);
                material.SetFloat("_MonolithTerrainDetailScale", 23);
                AssetDatabase.SaveAssetIfDirty(material);
                yield return MonolithTerrainFullImportTests.Import(fixture, XRayTerrainImportMode.EnableVerifiedTrueStalker, XRayTerrainQuality.High);
                material = MonolithTerrainFullImportTests.Material(fixture);
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)));
                AssertTs(material, false);
                Assert.AreEqual(23, material.GetFloat("_MonolithTerrainDetailScale"));
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator MissingTsBumpKeepsSourcePathDisabled()
        {
            using (var fixture = new XRayTerrainLevelFixture(true))
            {
                File.Delete(Path.Combine(fixture.InputRoot, fixture.BumpPath));
                yield return MonolithTerrainFullImportTests.Import(fixture, XRayTerrainImportMode.EnableVerifiedTrueStalker, XRayTerrainQuality.High);
                Material material = MonolithTerrainFullImportTests.Material(fixture);
                Assert.AreEqual(0, material.GetFloat("_MonolithTrueStalkerSourceTerrain"));
                Assert.AreEqual(0, material.GetFloat("_MonolithHasTerrainSourceColor"));
                Assert.AreEqual(0, material.GetFloat("_MonolithSourceProfile"));
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator ChangedTsSourceCannotEnableVerifiedPath()
        {
            using (var fixture = new XRayTerrainLevelFixture(true))
            {
                fixture.ReplaceWinner(true);
                yield return MonolithTerrainFullImportTests.Import(fixture, XRayTerrainImportMode.EnableVerifiedTrueStalker, XRayTerrainQuality.High);
                Material material = MonolithTerrainFullImportTests.Material(fixture);
                Assert.AreEqual(0, material.GetFloat("_MonolithTrueStalkerSourceTerrain"));
                Assert.AreEqual(0, material.GetFloat("_MonolithTrueStalkerVerified"));
                Assert.AreEqual(0, material.GetFloat("_MonolithSourceProfile"));
            }
        }

        private static void AssertTs(Material material, bool active)
        {
            Assert.AreEqual(active ? 1 : 0, material.GetFloat("_MonolithTrueStalkerSourceTerrain"));
            Assert.AreEqual(1, material.GetFloat("_MonolithTrueStalkerVerified"));
            Assert.AreEqual(2, material.GetFloat("_MonolithTerrainSource"));
            Assert.AreEqual(0, material.GetFloat("_MonolithSourceProfile"));
            Assert.AreEqual(0, material.GetFloat("_MonolithSourceTerrain"));
            Assert.AreEqual(0, material.GetFloat("_MonolithTerrainParallax"));
            Assert.AreEqual(0, material.GetFloat("_MonolithTerrainOcclusion"));
            Assert.AreEqual(0, material.GetFloat("_MonolithHasTerrainHeight"));
            Assert.AreEqual(1, material.GetFloat("_MonolithHasTerrainSourceColor"));
            Assert.AreEqual(XRayTerrainSourceBinding.TRUE_STALKER_CONTRACT_HASH, material.GetTag("XRayTrueStalkerTerrainSHA256", false));
            foreach (char layer in "RGBA")
            {
                Assert.IsFalse(((Texture2D)material.GetTexture("_MonolithTerrainSourceColor" + layer)).isDataSRGB);
                Assert.IsNull(material.GetTexture("_MonolithTerrainHeight" + layer));
            }
        }

        private void Configure(Material material)
        {
            material.SetTexture("_Base", Texture(new Vector4(0.35f, 0.48f, 0.29f, 0.63f), true));
            material.SetTexture("_Mask", Texture(Vector4.one, false));
            for (int i = 0; i < 4; i++)
            {
                material.SetTexture("_Color" + "RGBA"[i], Texture(new Vector4(0.2f + i * 0.1f, 0.65f - i * 0.1f, 0.4f, 1), true));
                material.SetTexture("_Bump" + "RGBA"[i], Texture(new Vector4(0.1f + i * 0.18f, 0.9f - i * 0.02f, 0.36f + i * 0.07f, 0.42f + i * 0.06f), true));
            }
            var lut = Own(new Texture3D(8, 8, 8, TextureFormat.RGBAFloat, false));
            var pixels = new Color[512];
            for (int z = 0; z < 8; z++)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++) pixels[x + 8 * (y + 8 * z)] = new Color(0.05f + 0.5f * x / 7 + 0.3f * z / 7, 0.1f + 0.4f * y / 7 + 0.2f * z / 7, 0, 1);
                }
            }
            lut.SetPixels(pixels);
            lut.Apply();
            lut.filterMode = FilterMode.Trilinear;
            lut.wrapMode = TextureWrapMode.Clamp;
            material.SetTexture("_MaterialLut", lut);
            for (int i = 0; i < 2; i++)
            {
                var cube = Own(new Cubemap(4, TextureFormat.RGBAFloat, false));
                for (int face = 0; face < 6; face++)
                {
                    var facePixels = new Color[16];
                    for (int pixel = 0; pixel < 16; pixel++) facePixels[pixel] = new Color(0.2f + face * 0.06f, 0.3f + i * 0.2f, 0.4f + pixel * 0.004f, 1);
                    cube.SetPixels(facePixels, (CubemapFace)face);
                }
                cube.Apply();
                material.SetTexture("_Env" + i, cube);
            }
            material.SetVector("_Uv", new Vector4(0.73f, 0.57f, 0.13f, 0.21f));
            material.SetVector("_Tangent", new Vector4(0.85f, 0.12f, 0.06f, 0));
            material.SetVector("_Bitangent", new Vector4(0.05f, 0.9f, 0.16f, 0));
            material.SetVector("_Geometric", new Vector4(0.1f, 0.2f, 0.76f, 0));
            material.SetVector("_RawT", new Vector4(0.54f, 0.52f, 0.73f, 0.3f));
            material.SetVector("_RawB", new Vector4(0.54f, 0.74f, 0.51f, 0.6f));
            material.SetVector("_RawN", new Vector4(0.75f, 0.55f, 0.53f, 0.63f));
            material.SetVector("_Position", new Vector4(1, -2, 4, 1));
            material.SetVector("_LightDirection", new Vector4(-0.2f, 0.4f, -0.8944272f, 0));
            material.SetVector("_LightPosition", new Vector4(2, 4, 1, 0.003f));
            material.SetVector("_EnvironmentColor", new Vector4(0.7f, 0.8f, 0.6f, 0.35f));
            material.SetVector("_Ambient", new Vector4(0.03f, 0.04f, 0.05f, 0));
            material.SetMatrix("_ViewToWorld", Matrix4x4.identity);
            material.SetVector("_Parameters", new Vector4(17, 0, 0.95f, 0));
        }

        private Texture2D Texture(Vector4 value, bool pattern)
        {
            var texture = Own(new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true));
            var pixels = new Color[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (Color)(pattern ? value * (0.9f + (i % 7) * 0.015f) : value);
            texture.SetPixels(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Repeat;
            return texture;
        }

        private Color[] Render(Material material)
        {
            RenderTexture target = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
            try
            {
                Graphics.Blit(null, target, material);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                readback.Apply();
                Color[] pixels = readback.GetPixels();
                string folder = Path.Combine(TrueStalkerTerrainReference.Output(), "GPU_Numeric", TestContext.CurrentContext.Test.ID);
                Directory.CreateDirectory(folder);
                string identityPath = Path.Combine(folder, "Identity.txt");
                if (!File.Exists(identityPath)) File.WriteAllText(identityPath, TestContext.CurrentContext.Test.FullName);
                string path = Path.Combine(folder, (_readbacks++).ToString("D5") + "_" + material.shader.name.Substring(material.shader.name.LastIndexOf('/') + 1) + "_S" + material.GetFloat("_Stage") + ".rgba32f");
                using (var writer = new BinaryWriter(File.Open(path, FileMode.CreateNew)))
                {
                    foreach (Color pixel in pixels)
                    {
                        for (int channel = 0; channel < 4; channel++) writer.Write(pixel[channel]);
                    }
                }
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private T Own<T>(T item) where T : UnityEngine.Object
        {
            _owned.Add(item);
            return item;
        }

        private static double Delta(Color[] actual, Color[] reference, out double relative)
        {
            double maximum = 0;
            relative = 0;
            Assert.AreEqual(reference.Length, actual.Length);
            for (int pixel = 0; pixel < actual.Length; pixel++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    Assert.IsFalse(float.IsNaN(actual[pixel][channel]) || float.IsInfinity(actual[pixel][channel]));
                    Assert.IsFalse(float.IsNaN(reference[pixel][channel]) || float.IsInfinity(reference[pixel][channel]));
                    maximum = Math.Max(maximum, Math.Abs(actual[pixel][channel] - reference[pixel][channel]));
                    relative = Math.Max(relative, Math.Abs(actual[pixel][channel] - reference[pixel][channel]) / Math.Max(0.0000001, Math.Abs(reference[pixel][channel])));
                }
            }
            return maximum;
        }

        private static string Messages(Shader shader)
        {
            var text = new StringBuilder();
            foreach (var message in ShaderUtil.GetShaderMessages(shader)) text.AppendLine(message.message + " at " + message.file + ":" + message.line);
            return text.ToString();
        }
    }
}
