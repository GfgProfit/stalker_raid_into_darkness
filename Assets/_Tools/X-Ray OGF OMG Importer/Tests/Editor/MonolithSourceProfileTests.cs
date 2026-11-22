using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace XRay.Importer.Tests
{
    public sealed class MonolithSourceProfileTests
    {
        private const string COMPUTE_PATH = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/MonolithSourceProfileValidation.compute";
        private const string REFERENCE_PATH = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/MonolithSourceProfileReference.txt";
        private const string MATERIAL_PATH = "Assets/_Source/Tests/Weapons/Fixtures/MonolithHDRP/AUG_A3_Monolith_Test.mat";
        private const string OUTPUT_VARIABLE = "MONOLITH_PARITY_OUTPUT";
        private const int CASES_PER_SET = 24;
        private const int REFERENCE_COLUMNS = 39;
        private const double ABSOLUTE_TOLERANCE = 2e-6;
        private const double RELATIVE_TOLERANCE = 2e-5;
        private const float SINGULAR_DYNAMIC_GLOSS = 0.99f;

        private static readonly Vector3[] _albedo = { new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.05f, 0.04f, 0.03f), new Vector3(0.9f, 0.85f, 0.8f), new Vector3(0.8f, 0.3f, 0.1f), new Vector3(0.2f, 0.6f, 0.9f) };
        private static readonly float[] _gloss = { 0.0f, 1.0f / 255.0f, 0.02f, 0.1f, 0.25f, 0.5f, 0.75f, 1.0f };
        private static readonly Vector2[] _thm = { new Vector2(0, 0), new Vector2(0, 0.1f), new Vector2(0, 0.3f), new Vector2(1, 0), new Vector2(1, 0.3f), new Vector2(2, 0), new Vector2(2, 0.85f), new Vector2(3, 0), new Vector2(3, 0.3f), new Vector2(3, 1) };
        private static readonly float[] _dynamicGloss = { 0.0f, 0.71864f, 0.7556f, SINGULAR_DYNAMIC_GLOSS };
        private static readonly Vector3[] _sourceLight = { new Vector3(1, 1, 1), new Vector3(0.35f, 0.6f, 1.4f) };
        private static readonly Vector3[] _sunColor = { new Vector3(1, 1, 1), new Vector3(0.9f, 0.75f, 0.55f) };
        private static readonly Vector2[] _lightSetup = { new Vector2(0.5f, 1.0f), new Vector2(0.2f, 0.4f) };
        private static readonly Vector4 _floraFixes = new Vector4(1.5f, 1.0f, 0.0f, 0.0f);
        private static readonly Vector3[,] _geometry =
        {
            { new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1) },
            { new Vector3(0, 0, 1), new Vector3(0.3f, 0, 1), new Vector3(-0.5f, 0.2f, 1) },
            { new Vector3(0, 0, 1), new Vector3(1, 0, 0.05f), new Vector3(0.1f, 0.2f, 1) },
            { new Vector3(0, 0, 1), new Vector3(0.2f, 0.1f, 1), new Vector3(0, 1, 0.03f) },
            { new Vector3(0, 0, 1), new Vector3(0.2f, 0.3f, 1), new Vector3(0.2f, 0.1f, -1) },
            { new Vector3(0, 0, 1), new Vector3(0, -0.9f, 0.44f), new Vector3(0, 0.6f, -0.8f) },
            { new Vector3(0.3f, -0.2f, 0.93f), new Vector3(-0.4f, 0.5f, 0.77f), new Vector3(0.6f, 0.3f, 0.74f) },
            { new Vector3(0, 0, 1), new Vector3(0, 0.3f, -0.2f), new Vector3(0.3f, 0.2f, 1) },
            { new Vector3(0, 0, 1), new Vector3(0.5f, 0, 1), new Vector3(-0.5f, 0, 1) },
            { new Vector3(0.7f, 0, 0.7f), new Vector3(0, 0.2f, 1), new Vector3(-0.98f, 0.1f, 0.15f) }
        };
        private static readonly string[] _profileOutputs = { "metalness", "roughness", "material", "flora", "albedoR", "albedoG", "albedoB", "f0R", "f0G", "f0B", "directR", "directG", "directB", "ambientDiffuseR", "ambientDiffuseG", "ambientDiffuseB", "ambientSpecularR", "ambientSpecularG", "ambientSpecularB" };
        private static readonly int[] _profileReference = { 7, 8, 9, 10, 11, 12, 13, 15, 16, 17, 21, 22, 23, 24, 25, 26, 28, 29, 30 };
        private static readonly int[] _profileUnity = { 0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 12, 13, 14, 16, 17, 18, 20, 21, 22 };

        [Test]
        public void DefaultProfileKeepsExistingMonolithMaterials()
        {
            var shader = Shader.Find("HDRP/MonolithLit");
            Assert.IsNotNull(shader);
            Assert.IsTrue(shader.isSupported);
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
            {
                Assert.AreNotEqual(ShaderCompilerMessageSeverity.Error, message.severity, message.message);
            }

            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(COMPUTE_PATH);
            Assert.IsNotNull(compute);
            foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(compute))
            {
                Assert.AreNotEqual(ShaderCompilerMessageSeverity.Error, message.severity, message.message);
            }

            var material = new Material(shader);
            try
            {
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceProfile"));
                Assert.AreEqual(new Vector4(1.5f, 1.0f, 0.0f, 0.0f), material.GetVector("_MonolithFloraFixes2"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }

            var fixture = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            Assert.IsNotNull(fixture);
            Assert.AreEqual(0.0f, fixture.GetFloat("_MonolithSourceProfile"));
        }

        [Test]
        public void SourceProfileMatchesAnomalyLmodelReference()
        {
            List<float[]> reference = LoadReference();
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(COMPUTE_PATH);
            Assert.IsNotNull(shader);
            int kernel = shader.FindKernel("EvaluateProfile");
            int sets = _dynamicGloss.Length * _sourceLight.Length * _sunColor.Length * _lightSetup.Length;
            Assert.AreEqual(sets * CASES_PER_SET, reference.Count);
            var maxAbsolute = new double[_profileOutputs.Length];
            var maxRelative = new double[_profileOutputs.Length];
            var failures = new List<string>();
            var rows = new StringBuilder("set,k,output,unity,reference,absolute,relative\n");
            int singular = 0;
            for (int set = 0; set < sets; set++)
            {
                Parameters(set, out float dynamicGloss, out Vector3 sourceLight, out Vector3 sunColor, out Vector2 lightSetup);
                Vector4[] cases = Cases(set);
                var results = new Vector4[CASES_PER_SET * 6];
                using (var input = new ComputeBuffer(cases.Length, 16))
                using (var output = new ComputeBuffer(results.Length, 16))
                {
                    input.SetData(cases);
                    shader.SetBuffer(kernel, "_Cases", input);
                    shader.SetBuffer(kernel, "_Results", output);
                    shader.SetInt("_CaseCount", CASES_PER_SET);
                    shader.SetVector("_SourceLight", new Vector4(sourceLight.x, sourceLight.y, sourceLight.z, dynamicGloss));
                    shader.SetVector("_SunColor", sunColor);
                    shader.SetVector("_LightSetup", new Vector4(lightSetup.x, lightSetup.y, 0.0f, 0.0f));
                    shader.SetVector("_FloraFixes", _floraFixes);
                    shader.Dispatch(kernel, 1, 1, 1);
                    output.GetData(results);
                }

                for (int k = 0; k < CASES_PER_SET; k++)
                {
                    float[] expected = reference[set * CASES_PER_SET + k];
                    Assert.AreEqual(set, (int)expected[0]);
                    Assert.AreEqual(k, (int)expected[1]);
                    for (int o = 0; o < _profileOutputs.Length; o++)
                    {
                        Vector4 vector = results[k * 6 + _profileUnity[o] / 4];
                        float unity = vector[_profileUnity[o] % 4];
                        float value = expected[_profileReference[o]];
                        if (float.IsNaN(value) || float.IsInfinity(value))
                        {
                            singular++;
                            Assert.AreEqual(SINGULAR_DYNAMIC_GLOSS, dynamicGloss, $"non-finite reference outside the documented singular domain: set {set} case {k} {_profileOutputs[o]}");
                            Assert.IsFalse(float.IsNaN(unity) || float.IsInfinity(unity), $"set {set} case {k} {_profileOutputs[o]}");
                            continue;
                        }

                        Compare(unity, value, o, maxAbsolute, maxRelative, failures, $"set {set} case {k} {_profileOutputs[o]}");
                        rows.Append(set).Append(',').Append(k).Append(',').Append(_profileOutputs[o]).Append(',').Append(Format(unity)).Append(',').Append(Format(value)).Append(',').Append(Format(Math.Abs(unity - value))).Append(',').Append(Format(Relative(unity, value))).Append('\n');
                    }
                }
            }

            Report("profile", _profileOutputs, maxAbsolute, maxRelative, singular, rows);
            Assert.IsEmpty(failures, string.Join("\n", failures.GetRange(0, Math.Min(20, failures.Count))));
        }

        [Test]
        public void BrdfTermsMatchAnomalyReference()
        {
            List<float[]> reference = LoadReference();
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(COMPUTE_PATH);
            Assert.IsNotNull(shader);
            int kernel = shader.FindKernel("EvaluateTerms");
            var inputs = new Vector4[reference.Count * 2];
            for (int i = 0; i < reference.Count; i++)
            {
                float[] row = reference[i];
                inputs[i * 2] = new Vector4(row[36], row[37], row[35], row[38]);
                inputs[i * 2 + 1] = new Vector4(row[18], row[19], row[20], row[14]);
            }

            var results = new Vector4[reference.Count];
            using (var input = new ComputeBuffer(inputs.Length, 16))
            using (var output = new ComputeBuffer(results.Length, 16))
            {
                input.SetData(inputs);
                shader.SetBuffer(kernel, "_Cases", input);
                shader.SetBuffer(kernel, "_Results", output);
                shader.SetInt("_CaseCount", reference.Count);
                shader.Dispatch(kernel, (reference.Count + 63) / 64, 1, 1);
                output.GetData(results);
            }

            string[] names = { "ggxR", "ggxG", "ggxB", "burley" };
            int[] columns = { 32, 33, 34, 27 };
            var maxAbsolute = new double[names.Length];
            var maxRelative = new double[names.Length];
            var failures = new List<string>();
            var rows = new StringBuilder("set,k,output,unity,reference,absolute,relative\n");
            int singular = 0;
            for (int i = 0; i < reference.Count; i++)
            {
                for (int o = 0; o < names.Length; o++)
                {
                    float value = reference[i][columns[o]];
                    float unity = results[i][o];
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        singular++;
                        Assert.IsFalse(float.IsNaN(unity) || float.IsInfinity(unity), $"row {i} {names[o]}");
                        continue;
                    }

                    Compare(unity, value, o, maxAbsolute, maxRelative, failures, $"row {i} {names[o]}");
                    rows.Append((int)reference[i][0]).Append(',').Append((int)reference[i][1]).Append(',').Append(names[o]).Append(',').Append(Format(unity)).Append(',').Append(Format(value)).Append(',').Append(Format(Math.Abs(unity - value))).Append(',').Append(Format(Relative(unity, value))).Append('\n');
                }
            }

            Report("terms", names, maxAbsolute, maxRelative, singular, rows);
            Assert.IsEmpty(failures, string.Join("\n", failures.GetRange(0, Math.Min(20, failures.Count))));
        }

        private static void Compare(float unity, float value, int output, double[] maxAbsolute, double[] maxRelative, List<string> failures, string label)
        {
            double absolute = Math.Abs(unity - value);
            maxAbsolute[output] = Math.Max(maxAbsolute[output], absolute);
            maxRelative[output] = Math.Max(maxRelative[output], Relative(unity, value));
            if (float.IsNaN(unity) || absolute > ABSOLUTE_TOLERANCE + RELATIVE_TOLERANCE * Math.Abs(value))
            {
                failures.Add($"{label}: unity {Format(unity)} reference {Format(value)}");
            }
        }

        private static double Relative(float unity, float value)
        {
            return Math.Abs(unity - value) / Math.Max(1e-6, Math.Abs(value));
        }

        private static void Report(string name, string[] outputs, double[] maxAbsolute, double[] maxRelative, int singular, StringBuilder rows)
        {
            var summary = new StringBuilder();
            for (int i = 0; i < outputs.Length; i++)
            {
                summary.Append(outputs[i]).Append(": abs ").Append(Format(maxAbsolute[i])).Append(" rel ").Append(Format(maxRelative[i])).Append('\n');
            }

            summary.Append("singular reference values: ").Append(singular).Append('\n');
            TestContext.WriteLine(summary.ToString());
            string folder = Environment.GetEnvironmentVariable(OUTPUT_VARIABLE);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "SourceProfile_" + name + "_Errors.csv"), rows.ToString());
                File.WriteAllText(Path.Combine(folder, "SourceProfile_" + name + "_Summary.txt"), summary.ToString());
            }
        }

        private static string Format(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static List<float[]> LoadReference()
        {
            var result = new List<float[]>();
            foreach (string line in File.ReadAllLines(REFERENCE_PATH))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(',');
                Assert.AreEqual(REFERENCE_COLUMNS, parts.Length);
                var row = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    row[i] = float.Parse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture);
                }

                result.Add(row);
            }

            return result;
        }

        private static float DecodeMaterial(Vector2 thm)
        {
            float raw = thm.x + (thm.x < 4 ? thm.y : 0.0f);
            float normalized = raw < 5.0f ? (raw + 0.5f) / 4.0f : raw;
            uint quantized = (uint)(normalized / 1.333333333f * 31.0f);
            return (quantized & 31u) / 31.0f * 1.333333333f;
        }

        private static void Parameters(int set, out float dynamicGloss, out Vector3 sourceLight, out Vector3 sunColor, out Vector2 lightSetup)
        {
            dynamicGloss = _dynamicGloss[set % _dynamicGloss.Length];
            sourceLight = _sourceLight[set / _dynamicGloss.Length % _sourceLight.Length];
            sunColor = _sunColor[set / (_dynamicGloss.Length * _sourceLight.Length) % _sunColor.Length];
            lightSetup = _lightSetup[set / (_dynamicGloss.Length * _sourceLight.Length * _sunColor.Length) % _lightSetup.Length];
        }

        private static Vector4[] Cases(int set)
        {
            var vectors = new Vector4[CASES_PER_SET * 4];
            for (int k = 0; k < CASES_PER_SET; k++)
            {
                int material = k % _thm.Length;
                int gloss = (k + set * 5) % _gloss.Length;
                int albedo = (k * 3 + set) % _albedo.Length;
                int geometry = (k * 7 + set * 3) % _geometry.GetLength(0);
                int local = (k / _thm.Length + set) % 3;
                Vector3 color = _albedo[albedo];
                Vector3 normal = _geometry[geometry, 0].normalized;
                Vector3 view = _geometry[geometry, 1].normalized;
                Vector3 light = _geometry[geometry, 2].normalized;
                vectors[k * 4] = new Vector4(color.x, color.y, color.z, _gloss[gloss]);
                vectors[k * 4 + 1] = new Vector4(normal.x, normal.y, normal.z, DecodeMaterial(_thm[material]));
                vectors[k * 4 + 2] = new Vector4(view.x, view.y, view.z, local);
                vectors[k * 4 + 3] = new Vector4(light.x, light.y, light.z, 0.0f);
            }

            return vectors;
        }
    }
}
