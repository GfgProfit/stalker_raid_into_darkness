using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace XRay.Importer.Tests
{
    public sealed class MonolithSourceTextureTests
    {
        private const string ROOT = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/";
        private const string OUTPUT_VARIABLE = "MONOLITH_PHASE03_OUTPUT";
        private const float ABSOLUTE_TOLERANCE = 0.000002f;
        private const float RELATIVE_TOLERANCE = 0.00002f;
        private const float FLIPPED_TC_TOLERANCE = 0.002f;
        private const float FLIPPED_SAMPLE_TOLERANCE = 0.008f;

        [Test]
        public void SourceTextureMatchesIndependentSloadReference()
        {
            string[] lines = File.ReadAllLines(ROOT + "Fixtures/MonolithSourceTextureReference.txt");
            var groups = new Dictionary<int, List<float[]>>();
            foreach (string line in lines)
            {
                if (line.StartsWith("# texture", StringComparison.Ordinal))
                {
                    string[] header = line.Split(' ');
                    Assert.AreEqual(header[4], MonolithSourceTextureData.Hash(int.Parse(header[2], CultureInfo.InvariantCulture)), header[3]);
                    continue;
                }

                float[] row = Array.ConvertAll(line.Split(' '), value => float.Parse(value, CultureInfo.InvariantCulture));
                int key = (int)row[0] * 100 + (int)row[1];
                if (!groups.TryGetValue(key, out List<float[]> group))
                {
                    group = new List<float[]>();
                    groups[key] = group;
                }
                group.Add(row);
            }

            Assert.AreEqual(9 * MonolithSourceTextureData.SetPom.Length, groups.Count);
            var detail = new List<string> { "orientation,variant,set,case,albedoError,detailTcError,glossError" };
            var bump = new List<string> { "orientation,variant,set,case,tangentNormalError,viewNormalError,glossError" };
            var pom = new List<string> { "orientation,variant,set,case,tcError,heightError,lightingRelativeError" };
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ROOT + "MonolithSourceTextureValidation.compute");
            Assert.IsNotNull(shader);
            foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(shader))
            {
                Assert.AreNotEqual(ShaderCompilerMessageSeverity.Error, message.severity, message.message);
            }

            for (int orientation = 0; orientation < 2; orientation++)
            {
                var textures = new Texture2D[MonolithSourceTextureData.TextureNames.Length];
                for (int i = 0; i < textures.Length; i++) textures[i] = MonolithSourceTextureData.Create(i, orientation == 1);
                try
                {
                    foreach (KeyValuePair<int, List<float[]>> pair in groups)
                    {
                        int variant = pair.Key / 100;
                        int set = pair.Key % 100;
                        Vector4[] actual = Run(shader, textures, variant, set, orientation, pair.Value);
                        for (int k = 0; k < pair.Value.Count; k++)
                        {
                            Compare(pair.Value[k], actual, k, variant, set, orientation, detail, bump, pom);
                        }
                    }
                }
                finally
                {
                    foreach (Texture2D texture in textures) UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            WriteReport("Detail_Diffuse_Parity.csv", detail);
            WriteReport("Detail_Bump_Parity.csv", bump);
            WriteReport("POM_Parity_Results.csv", pom);
        }

        [Test]
        public void SourceTextureFeaturesAreOptIn()
        {
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.IsNotNull(shader);
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
            {
                Assert.AreNotEqual(ShaderCompilerMessageSeverity.Error, message.severity, message.message);
            }

            var material = new Material(shader);
            try
            {
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceDetail"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceParallax"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceVirtualHeight"));
                Assert.AreEqual(50.0f, material.GetFloat("_MonolithSourceTextureRange"));
                Assert.AreEqual(1.0f, material.GetFloat("_MonolithDetailScale"));
                Assert.AreEqual(0.02f, material.GetFloat("_MonolithParallaxHeight"));
                Assert.AreEqual(new Vector4(36.0f, 12.0f, 0.035f, 0.5f), material.GetVector("_MonolithPom"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceProfile"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static Vector4[] Run(ComputeShader shader, Texture2D[] textures, int variant, int set, int orientation, List<float[]> rows)
        {
            int kernel = shader.FindKernel("Evaluate");
            var cases = new Vector4[rows.Count * MonolithSourceTextureData.CASE_STRIDE];
            for (int k = 0; k < rows.Count; k++)
            {
                for (int c = 0; c < MonolithSourceTextureData.CASE_STRIDE; c++)
                {
                    int o = 3 + c * 4;
                    cases[k * MonolithSourceTextureData.CASE_STRIDE + c] = new Vector4(rows[k][o], rows[k][o + 1], rows[k][o + 2], rows[k][o + 3]);
                }
            }

            shader.SetFloat("_Parallax", MonolithSourceTextureData.VariantParallax[variant]);
            shader.SetFloat("_DetailMode", MonolithSourceTextureData.VariantDetail[variant]);
            shader.SetFloat("_ParallaxHeight", MonolithSourceTextureData.SetParallaxHeight[set]);
            shader.SetFloat("_DetailScale", MonolithSourceTextureData.SetDetailScale[set]);
            shader.SetVector("_Pom", MonolithSourceTextureData.SetPom[set]);
            shader.SetFloat("_FlipV", orientation == 1 ? 0.0f : 1.0f);
            shader.SetFloat("_FlipGreen", orientation == 1 ? 1.0f : 0.0f);
            shader.SetTexture(kernel, "_Base", textures[0]);
            shader.SetTexture(kernel, "_Bump", textures[1]);
            shader.SetTexture(kernel, "_BumpX", textures[2 + MonolithSourceTextureData.SetHeight[set]]);
            shader.SetTexture(kernel, "_Detail", textures[5]);
            shader.SetTexture(kernel, "_DetailBump", textures[6]);
            shader.SetTexture(kernel, "_DetailBumpX", textures[7]);
            var results = new Vector4[rows.Count * MonolithSourceTextureData.RESULT_STRIDE];
            using (var input = new ComputeBuffer(cases.Length, 16))
            using (var output = new ComputeBuffer(results.Length, 16))
            {
                input.SetData(cases);
                shader.SetBuffer(kernel, "_Cases", input);
                shader.SetBuffer(kernel, "_Results", output);
                shader.SetInt("_CaseCount", rows.Count);
                shader.Dispatch(kernel, (rows.Count + 63) / 64, 1, 1);
                output.GetData(results);
            }
            return results;
        }

        private static void Compare(float[] row, Vector4[] actual, int k, int variant, int set, int orientation, List<string> detail, List<string> bump, List<string> pom)
        {
            int r = 3 + MonolithSourceTextureData.CASE_STRIDE * 4;
            Vector4[] expected = new Vector4[MonolithSourceTextureData.RESULT_STRIDE];
            for (int c = 0; c < expected.Length; c++) expected[c] = new Vector4(row[r + c * 4], row[r + c * 4 + 1], row[r + c * 4 + 2], row[r + c * 4 + 3]);
            Vector4[] result = new Vector4[MonolithSourceTextureData.RESULT_STRIDE];
            Array.Copy(actual, k * MonolithSourceTextureData.RESULT_STRIDE, result, 0, result.Length);
            string label = $"orientation {orientation} variant {variant} set {set} case {k}";
            foreach (Vector4 value in result)
            {
                for (int c = 0; c < 4; c++) Assert.IsFalse(float.IsNaN(value[c]) || float.IsInfinity(value[c]), label);
            }

            bool hasDetail = MonolithSourceTextureData.VariantDetail[variant] > 0;
            Vector3 expectedNormal = ((Vector3)expected[2]).normalized;
            float tcBound = orientation == 0 ? 0.0f : FLIPPED_TC_TOLERANCE;
            float sampleBound = orientation == 0 ? 0.0f : FLIPPED_SAMPLE_TOLERANCE;
            float tcError = Mathf.Max(Check(expected[0].x, result[0].x, label + " tc.x", tcBound), Check(expected[0].y, result[0].y, label + " tc.y", tcBound));
            float detailTcError = 0;
            if (hasDetail)
            {
                Check(expected[0].z, result[0].z, label + " detail tc.x", tcBound * 12.0f);
                Check(expected[0].w, result[0].w, label + " detail tc.y", tcBound * 12.0f);
                detailTcError = Mathf.Max(Mathf.Abs(expected[0].z - result[0].z), Mathf.Abs(expected[0].w - result[0].w));
            }

            float albedoError = 0;
            float normalError = 0;
            float viewNormalError = 0;
            for (int c = 0; c < 3; c++)
            {
                albedoError = Mathf.Max(albedoError, Check(expected[5][c], result[5][c], label + " albedo " + c, sampleBound));
                normalError = Mathf.Max(normalError, Check(expectedNormal[c], result[2][c], label + " tangent normal " + c, sampleBound));
                viewNormalError = Mathf.Max(viewNormalError, Check(expected[3][c], result[3][c], label + " view normal " + c, sampleBound));
            }

            float glossError = Check(expected[1].w, result[1].w, label + " gloss", sampleBound);
            float heightError = Check(expected[2].w, result[2].w, label + " height", sampleBound);
            Check(expected[4].w, result[4].w, label + " quantized gloss", sampleBound);
            float lightError = 0;
            for (int c = 0; c < 3; c++)
            {
                if (orientation == 0) Check(expected[4][c], result[4][c], label + " lighting " + c, 0.0f);
                lightError = Mathf.Max(lightError, Mathf.Abs(expected[4][c] - result[4][c]) / Mathf.Max(1e-6f, Mathf.Abs(expected[4][c])));
            }

            string prefix = FormattableString.Invariant($"{(orientation == 0 ? "source" : "unity")},{variant},{set},{k}");
            if (hasDetail) detail.Add(FormattableString.Invariant($"{prefix},{albedoError:R},{detailTcError:R},{glossError:R}"));
            if (MonolithSourceTextureData.VariantDetail[variant] == 2) bump.Add(FormattableString.Invariant($"{prefix},{normalError:R},{viewNormalError:R},{glossError:R}"));
            if (MonolithSourceTextureData.VariantParallax[variant] > 0) pom.Add(FormattableString.Invariant($"{prefix},{tcError:R},{heightError:R},{lightError:R}"));
        }

        private static float Check(float expected, float actual, string label, float bound)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(Mathf.Max(bound, ABSOLUTE_TOLERANCE + RELATIVE_TOLERANCE * Mathf.Abs(expected))), label);
            return Mathf.Abs(actual - expected);
        }

        private static void WriteReport(string name, List<string> lines)
        {
            string output = Environment.GetEnvironmentVariable(OUTPUT_VARIABLE);
            if (string.IsNullOrEmpty(output)) return;
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, name), lines);
        }
    }
}
