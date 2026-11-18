using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Editor;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class MonolithFloraSourceTests
    {
        private const int SIZE = 16;
        private const float QUANT = 32768.0f;
        private const string ANOMALY_ROOT = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d";
        private const string TS_ROOT = "Assets/_Source/Levels";
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [Test]
        public void OriginalFloraHlslMatchesMonolithFlora()
        {
            string template = File.ReadAllText(MonolithFloraReferenceSource.TEMPLATE);
            Shader actualShader = Shader.Find("Hidden/XRay/MonolithFloraValidation");
            Assert.IsNotNull(actualShader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(actualShader), Messages(actualShader));
            Material actual = Own(new Material(actualShader));
            Texture2D waves = Own(Waves());
            for (int group = 0; group < MonolithFloraReferenceSource.GROUP_COUNT; group++)
            {
                string reference = MonolithFloraReferenceSource.Build(group, template, out _);
                Shader referenceShader = Own(ShaderUtil.CreateShaderAsset(reference));
                Assert.IsNotNull(referenceShader);
                Assert.IsFalse(ShaderUtil.ShaderHasError(referenceShader), MonolithFloraReferenceSource.NAMES[group] + "\n" + Messages(referenceShader));
                Material expected = Own(new Material(referenceShader));
                for (int index = 0; index < 8; index++)
                {
                    var random = new System.Random(1000 * group + index);
                    Texture2D baseTexture = Own(Surface(random, true));
                    Texture2D bump = Own(Surface(random, false));
                    Texture2D correction = Own(Surface(random, false));
                    foreach (Material material in new[] { actual, expected })
                    {
                        material.SetTexture("_Base", baseTexture);
                        material.SetTexture("_Bump", bump);
                        material.SetTexture("_BumpX", correction);
                        material.SetTexture("_Waves", waves);
                    }
                    Configure(random, index, group, actual, expected);
                    foreach (int stage in MonolithFloraReferenceSource.STAGES[group])
                    {
                        actual.SetFloat("_Stage", stage);
                        expected.SetFloat("_Stage", stage);
                        Color[] reference0 = Render(expected);
                        Color[] observed = Render(actual);
                        for (int pixel = 0; pixel < reference0.Length; pixel++)
                        {
                            for (int channel = 0; channel < 4; channel++)
                            {
                                float r = reference0[pixel][channel];
                                float o = observed[pixel][channel];
                                if (float.IsNaN(r) || float.IsInfinity(r))
                                {
                                    continue;
                                }
                                Assert.IsFalse(float.IsNaN(o) || float.IsInfinity(o), "Nonfinite Monolith output " + MonolithFloraReferenceSource.NAMES[group] + " stage " + stage);
                                double delta = Math.Abs(r - o);
                                double limit = 0.00002 + 0.000002 * Math.Abs(r);
                                Assert.LessOrEqual(delta, limit, MonolithFloraReferenceSource.NAMES[group] + " case " + index + " stage " + stage + " pixel " + pixel + " channel " + channel + " reference " + r.ToString("R", CultureInfo.InvariantCulture) + " observed " + o.ToString("R", CultureInfo.InvariantCulture));
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void FloraSourceIsExplicitAndGatedByBindings()
        {
            Shader shader = Shader.Find(MonolithMaterialConfigurator.SHADER_NAME);
            Assert.IsNotNull(shader);
            Material material = Own(new Material(shader));
            Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraSource"));
            Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraWetness"));
            Assert.AreEqual(new Vector4(0.3f, 0.38f, 0.5f, 0.55f), material.GetVector("_MonolithFloraFixes1"));
            Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraVariationEnabled"));
            Assert.AreEqual(new Vector4(0.025f, 0.1f, 0.025f, 0.05f), material.GetVector("_MonolithFloraVariation"));
            Texture2D bump = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            Texture2D correction = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            Assert.IsFalse(MonolithMaterialConfigurator.ConfigureFloraSource(material, MonolithMaterialConfigurator.FLORA_SOURCE_ANOMALY, correction));
            Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraSource"));
            material.EnableKeyword("_MONOLITH_LEVEL_FLORA");
            Assert.IsFalse(MonolithMaterialConfigurator.ConfigureFloraSource(material, MonolithMaterialConfigurator.FLORA_SOURCE_ANOMALY, null));
            material.SetTexture("_MonolithBump", bump);
            material.SetFloat("_MonolithHasBump", 1.0f);
            Assert.IsTrue(MonolithMaterialConfigurator.ConfigureFloraSource(material, MonolithMaterialConfigurator.FLORA_SOURCE_ANOMALY, null));
            Assert.AreEqual(1.0f, material.GetFloat("_MonolithFloraSource"));
            Assert.AreEqual(0.0f, material.GetFloat("_MonolithHasBumpCorrection"));
            Assert.IsFalse(MonolithMaterialConfigurator.ConfigureFloraSource(material, MonolithMaterialConfigurator.FLORA_SOURCE_TRUE_STALKER, null));
            Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraSource"));
            Assert.IsTrue(MonolithMaterialConfigurator.ConfigureFloraSource(material, MonolithMaterialConfigurator.FLORA_SOURCE_TRUE_STALKER, correction));
            Assert.AreEqual(2.0f, material.GetFloat("_MonolithFloraSource"));
            Assert.AreEqual(correction, material.GetTexture("_MonolithBumpCorrection"));
            Assert.AreEqual(1.0f, material.GetFloat("_MonolithHasBumpCorrection"));
            Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceProfile"));
            Assert.IsFalse(MonolithMaterialConfigurator.ConfigureFloraSource(material, 3.0f, correction));
            Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraSource"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FloraColorVariationRequiresExplicitOptInOnGpu(bool detail)
        {
            Material defaults = Own(new Material(Shader.Find(MonolithMaterialConfigurator.SHADER_NAME)));
            Material material = Own(new Material(Shader.Find("Hidden/XRay/MonolithFloraValidation")));
            Texture2D texture = Own(new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true));
            var color = new Color(0.24f, 0.37f, 0.12f, 1.0f);
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            material.SetTexture("_Base", texture);
            material.SetVector("_Uv", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
            Vector4 variation = defaults.GetVector("_MonolithFloraVariation");
            variation.z = detail ? variation.x : 0.2f;
            foreach (float phase in new[] { 0.0f, 0.27f, 0.61f, 0.98f })
            {
                material.SetVector("_Extra", new Vector4(phase, 0.0f, 0.0f, 0.0f));
                material.SetFloat("_Stage", 22.0f);
                material.SetFloat("_VariationEnabled", defaults.GetFloat("_MonolithFloraVariationEnabled"));
                material.SetVector("_Variation", variation);
                Color[] disabled = Render(material);
                material.SetVector("_Variation", Vector4.zero);
                Color[] neutral = Render(material);
                material.SetVector("_Variation", variation);
                material.SetFloat("_VariationEnabled", 1.0f);
                Color[] enabled = Render(material);
                material.SetFloat("_Stage", 1.0f);
                Color[] legacy = Render(material);
                float changed = 0.0f;
                for (int pixel = 0; pixel < disabled.Length; pixel++)
                {
                    for (int channel = 0; channel < 3; channel++)
                    {
                        Assert.That(disabled[pixel][channel], Is.EqualTo(neutral[pixel][channel]).Within(0.000001f));
                        Assert.That(disabled[pixel][channel], Is.EqualTo(color[channel]).Within(0.000001f));
                        Assert.That(enabled[pixel][channel], Is.EqualTo(legacy[pixel][channel]).Within(0.000001f));
                        changed = Mathf.Max(changed, Mathf.Abs(enabled[pixel][channel] - disabled[pixel][channel]));
                    }
                }
                Assert.Greater(changed, 0.001f);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void VerifiedFloraBindingsKeepColorVariationExplicit(bool detail)
        {
            Material material = Own(new Material(Shader.Find(MonolithMaterialConfigurator.SHADER_NAME)));
            Texture2D bump = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            MonolithMaterialConfigurator.Configure(material, null, null, bump, null, true);
            material.EnableKeyword(detail ? "_MONOLITH_LEVEL_DETAIL" : "_MONOLITH_LEVEL_FLORA");
            var request = new XRayLevelMaterialRequest { Detail = detail, RuntimeDetail = detail, Surface = new XRayLevelSurface { Flora = !detail, Kind = XRayLevelSurfaceKind.AlphaTest } };
            XRayLevelBindingPlan plan = XRayFloraMaterialBindings.Plan(material, request, XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraSourceProfile.AnomalySss, true);
            XRayFloraMaterialBindings.Apply(material, plan, XRayFloraSourceBinding.ANOMALY_CONTRACT_HASH);
            Assert.AreEqual(1.0f, material.GetFloat(detail ? "_MonolithGrassSource" : "_MonolithFloraSource"));
            Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraVariationEnabled"));
            material.SetFloat("_MonolithFloraVariationEnabled", 1.0f);
            plan = XRayFloraMaterialBindings.Plan(material, request, XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraSourceProfile.AnomalySss, false);
            XRayFloraMaterialBindings.Apply(material, plan, XRayFloraSourceBinding.ANOMALY_CONTRACT_HASH);
            Assert.AreEqual(1.0f, material.GetFloat("_MonolithFloraVariationEnabled"));
        }

        [Test]
        [Category("ImportedLevelIntegration")]
        public void SavedAnomalyFloraAndGrassDoNotEnableColorVariation()
        {
            int leaves = 0;
            int grass = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { ANOMALY_ROOT + "/Materials" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader.name != MonolithMaterialConfigurator.SHADER_NAME) continue;
                bool detail = material.IsKeywordEnabled("_MONOLITH_LEVEL_DETAIL") && material.GetFloat("_MonolithGrassSource") == 1.0f;
                bool leaf = material.IsKeywordEnabled("_MONOLITH_LEVEL_FLORA") && material.GetFloat("_MonolithFloraSource") == 1.0f;
                if (!detail && !leaf) continue;
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithFloraVariationEnabled"), path);
                Assert.AreEqual(new Vector4(0.025f, 0.1f, 0.025f, 0.05f), material.GetVector("_MonolithFloraVariation"), path);
                if (detail) grass++;
                if (leaf) leaves++;
            }
            Assert.Greater(leaves, 0);
            Assert.Greater(grass, 0);
        }

        [Test]
        public void SavedFloraMaterialsStayOnLegacyPath()
        {
            int flora = 0;
            foreach (string folder in new[] { ANOMALY_ROOT + "/Materials", TS_ROOT + "/Materials" })
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".mat", StringComparison.Ordinal)) continue;
                    string text = File.ReadAllText(path);
                    if (!text.Contains("_MONOLITH_LEVEL_FLORA")) continue;
                    flora++;
                    Match stored = Regex.Match(text, @"- _MonolithFloraSource: ([0-9.eE+-]+)");
                    Assert.IsTrue(!stored.Success || float.Parse(stored.Groups[1].Value, CultureInfo.InvariantCulture) == 0.0f, path);
                }
            }
            Assert.Greater(flora, 0);
        }

        private static void Configure(System.Random random, int index, int group, Material actual, Material expected)
        {
            float wetness = new[] { 0.0f, 0.35f, 1.0f, 0.6f, 0.0f, 0.12f, 0.0f, 0.9f }[index];
            Vector3 translation = new Vector3(Range(random, -180, 180), Range(random, -5, 40), Range(random, -220, 220));
            float scale = Range(random, 0.6f, 1.8f);
            Matrix4x4 xform = Matrix4x4.TRS(translation, Quaternion.Euler(Range(random, -4, 4), Range(random, 0, 360), Range(random, -4, 4)), new Vector3(scale, scale * Range(random, 0.9f, 1.1f), scale));
            Vector3 vertex = new Vector3(Range(random, -3, 3), Range(random, 0, 14), Range(random, -3, 3));
            float u = Mathf.Round(Range(random, 0, 1) * QUANT) / QUANT;
            float v = Mathf.Round(Range(random, 0, 1) * QUANT) / QUANT;
            float rigidity = Mathf.Round(Range(random, 0, 32768));
            float hemiByte = Mathf.Round(Range(random, 0, 255)) / 255.0f;
            Vector4 hemi = new Vector4(Range(random, 0.2f, 1.2f), Range(random, -0.1f, 0.3f), 0, Range(random, -0.2f, 1.3f));
            Vector3 terrain = new Vector3(Range(random, -0.4f, 0.4f), 1.0f, Range(random, -0.4f, 0.4f)).normalized;
            if (group == 3 && index == 7) terrain = Vector3.down;
            float instanceAlpha = new[] { 0.2f, 0.7f, 1.0f, 0.45f, 0.05f, 0.95f, 0.6f, 1.0f }[index];
            Vector4 position = new Vector4(Range(random, -1, 1), Range(random, -1, 1), Range(random, 1, 40), new[] { 0.0f, 0.25f, 0.5f, 1.0f, 1.2f, 0.29f, 0.31f, 0.75f }[index]);
            Vector3 lightDirection = new Vector3(Range(random, -1, 1), Range(random, -1, 1), Range(random, -1, 1));
            if (lightDirection.sqrMagnitude < 0.01f) lightDirection = Vector3.down;
            Vector4 anim = new Vector4(Range(random, -300, 300), Range(random, -300, 300), Range(random, 0, 80), Range(random, 0, 900));
            Vector4 animPrev = anim - new Vector4(Range(random, 0, 0.05f), Range(random, 0, 0.05f), Range(random, 0, 0.02f), 0.016f);
            float waveTime = Range(random, 0, 200);
            Vector4 wave = new Vector4(0.1f, 0.01f, 0.11f, waveTime) / (2.0f * Mathf.PI);
            float rotation = Range(random, 0, 6.2831f);
            float amplitude = Range(random, 0.001f, 0.05f);
            Vector4 wind = new Vector4(Mathf.Sin(rotation) * amplitude, 0, Mathf.Cos(rotation) * amplitude, 0);
            var array = new Vector4[244];
            array[0] = xform.GetRow(0);
            array[1] = xform.GetRow(1);
            array[2] = xform.GetRow(2);
            array[3] = new Vector4(0.5f, 0.5f, 0.5f, hemi.w);
            var exdata = new Vector4[61];
            exdata[0] = new Vector4(terrain.x, terrain.y, terrain.z, instanceAlpha);
            Vector4 extra = new Vector4(Range(random, 0, 1), index % 2 == 0 ? 0.03f : 0.4f, 0, 0);
            Vector4 windParams = new Vector4(Range(random, 0, 6.2831f), new[] { 0.0f, 150.0f, 800.0f, 1500.0f, 60.0f, 2500.0f, 400.0f, 1000.0f }[index], 0, 0);
            Vector4 lightColor = new Vector4(Range(random, 0.2f, 1.5f), Range(random, 0.2f, 1.5f), Range(random, 0.2f, 1.5f), 0.71864f);
            Vector4 sunColor = new Vector4(Range(random, 0.3f, 1.4f), Range(random, 0.3f, 1.4f), Range(random, 0.3f, 1.4f), 0);
            foreach (Material material in new[] { actual, expected })
            {
                material.SetVector("_Uv", new Vector4(1, 1, 0, 0));
                material.SetVector("_Rain", new Vector4(0, wetness, 0, 0));
                material.SetVector("_Fixes1", index % 2 == 0 ? new Vector4(0.3f, 0.38f, 0.5f, 0.55f) : new Vector4(0.12f, 0.6f, 0.25f, 0.9f));
                material.SetVector("_Fixes2", new Vector4(1.5f, index % 3 == 0 ? 1.0f : 0.4f, 0, 0));
                material.SetVector("_Variation", index % 2 == 0 ? new Vector4(0.025f, 0.1f, 0.025f, 0.05f) : new Vector4(0.2f, 0.37f, 0.3f, 0.11f));
                material.SetVector("_Extra", extra);
                material.SetMatrix("_Xform", xform);
                material.SetVector("_Vertex", vertex);
                material.SetVector("_VertexData", new Vector4(hemiByte, u, v, rigidity));
                material.SetVector("_HemiScaleBias", hemi);
                material.SetVector("_WindParams", windParams);
                material.SetVector("_WindGrass", index % 2 == 0 ? new Vector4(9.7f, 1.5f, 1.6f, 0.5f) : new Vector4(3.0f, 0.7f, 2.4f, 1.1f));
                material.SetVector("_WindTrees", index % 2 == 0 ? new Vector4(11.1f, 0.17f, 0.9f, 0.1f) : new Vector4(4.0f, 0.6f, 1.4f, 0.0f));
                material.SetVector("_WindAnim", anim);
                material.SetVector("_WindAnimPrev", animPrev);
                material.SetVector("_Wave", wave);
                material.SetVector("_Wind", wind);
                material.SetVector("_Consts", new Vector4(1.0f / QUANT, 1.0f / QUANT, 0, 0));
                material.SetVector("_Terrain", new Vector4(terrain.x, terrain.y, terrain.z, instanceAlpha));
                material.SetVector("_Position", position);
                material.SetVector("_ScreenRes", new Vector4(1920, 1080, 1.0f / 1920, 1.0f / 1080));
                material.SetVector("_LightDirection", lightDirection.normalized);
                material.SetVector("_LightColor", lightColor);
                material.SetVector("_SunColor", sunColor);
                material.SetVector("_LightSetup", new Vector4(0.5f, 1.0f, 0, 0));
                material.SetFloat("_GrassAlign", index % 3 == 0 ? 0.0f : 1.0f);
                material.SetVectorArray("array", array);
                material.SetVectorArray("exdata", exdata);
            }
        }

        private static float Range(System.Random random, float minimum, float maximum)
        {
            return (float)(minimum + (maximum - minimum) * random.NextDouble());
        }

        private static Texture2D Surface(System.Random random, bool albedo)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[16];
            float[] alphas = { 0.0f, 200.0f / 255.0f, 200.0f / 255.0f - 0.0001f, 200.0f / 255.0f + 0.0001f, 170.0f / 255.0f, 170.0f / 255.0f - 0.0001f, 1.0f, 0.5f };
            float[] channels = { 0.0f, 1.0f, 0.5f, 0.49f, 0.51f, 0.25f, 0.75f, 0.98f };
            for (int i = 0; i < 16; i++)
            {
                if (albedo) pixels[i] = new Color(Range(random, 0, 1), Range(random, 0, 1), Range(random, 0, 1), i < 8 ? alphas[i] : Range(random, 0, 1));
                else pixels[i] = i < 8 ? new Color(channels[i], channels[7 - i], channels[(i + 3) % 8], channels[(i + 5) % 8]) : new Color(Range(random, 0, 1), Range(random, 0, 1), Range(random, 0, 1), Range(random, 0, 1));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D Waves()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var random = new System.Random(77);
            var pixels = new Color[64];
            for (int i = 0; i < 64; i++) pixels[i] = new Color(Range(random, 0, 1), Range(random, 0, 1), Range(random, 0, 1), Range(random, 0, 1));
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private Color[] Render(Material material)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(SIZE, SIZE, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(SIZE, SIZE, TextureFormat.RGBAFloat, false, true);
            try
            {
                Graphics.Blit(null, target, material, 0);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
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

        private static string Messages(Shader shader)
        {
            var text = new StringBuilder();
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader)) text.AppendLine(message.message + " at " + message.file + ":" + message.line);
            return text.ToString();
        }

        private T Own<T>(T item) where T : UnityEngine.Object
        {
            _owned.Add(item);
            return item;
        }

    }
}
