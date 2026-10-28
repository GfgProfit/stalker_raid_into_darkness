using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor;
using XRay.Importer.Editor.Levels;

namespace XRay.Importer.Tests
{
    public sealed class MonolithSourceTerrainTests
    {
        private const string ROOT = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/";

        [Test]
        public void TerrainMatchesIndependentEffectiveHlsl()
        {
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ROOT + "MonolithSourceTerrainValidation.compute");
            Assert.IsNotNull(shader);
            TextAsset fixture = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Fixtures/MonolithSourceTerrainReference.txt");
            Assert.IsNotNull(fixture);
            Vector4[] cases = MonolithSourceTerrainData.Cases();
            int count = cases.Length / MonolithSourceTerrainData.CASE_STRIDE;
            var textures = new List<Texture2D>();
            var results = new Vector4[count * MonolithSourceTerrainData.RESULT_STRIDE];
            try
            {
                int kernel = shader.FindKernel("Evaluate");
                string[] channels = { "R", "G", "B", "A" };
                for (int layer = 0; layer < 4; layer++)
                {
                    for (int kind = 0; kind < 3; kind++)
                    {
                        Texture2D texture = MonolithSourceTerrainData.CreateTexture(layer, kind);
                        textures.Add(texture);
                        shader.SetTexture(kernel, (kind == 0 ? "_Color" : kind == 1 ? "_Bump" : "_Height") + channels[layer], texture);
                    }
                }
                using (var input = new ComputeBuffer(cases.Length, 16))
                using (var output = new ComputeBuffer(results.Length, 16))
                {
                    input.SetData(cases);
                    shader.SetBuffer(kernel, "_Cases", input);
                    shader.SetBuffer(kernel, "_Results", output);
                    shader.SetInt("_CaseCount", count);
                    shader.Dispatch(kernel, (count + 63) / 64, 1, 1);
                    output.GetData(results);
                }
                foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(shader)) Assert.AreNotEqual(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.severity, message.message);
                Assert.AreEqual(1.0f, results[0].x, "GPU dispatch must execute before comparing the reference.");
                Compare(cases, results, fixture.text);
            }
            finally
            {
                foreach (Texture2D texture in textures) UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TerrainFeaturesAreOptIn()
        {
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            try
            {
                foreach (string property in new[] { "_MonolithSourceProfile", "_MonolithSourceTerrain", "_MonolithTerrainSource", "_MonolithTerrainParallax", "_MonolithTerrainOcclusion", "_MonolithTerrainVirtualHeight", "_MonolithHasTerrainHeight", "_MonolithHasTerrainSourceColor" }) Assert.AreEqual(0.0f, material.GetFloat(property), property);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void MissingOrForeignTerrainHeightsDisableHeightEvaluation()
        {
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            var height = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                material.SetFloat("_MonolithTerrainSource", (float)XRayTerrainSourceProfile.AnomalyAtmospherics);
                material.SetTexture("_MonolithTerrainSourceBase", height);
                material.SetTexture("_MonolithTerrainMask", height);
                foreach (string channel in new[] { "R", "G", "B", "A" }) material.SetTexture("_MonolithTerrainBump" + channel, height);
                foreach (string channel in new[] { "R", "G", "B", "A" }) material.SetTexture("_MonolithTerrainSourceColor" + channel, height);
                foreach (string channel in new[] { "R", "G", "B", "A" }) material.SetTexture("_MonolithTerrainHeight" + channel, height);
                MonolithMaterialInspector.UpdateTexturePresence(material);
                Assert.AreEqual(1.0f, material.GetFloat("_MonolithHasTerrainHeight"));
                Assert.AreEqual(1.0f, material.GetFloat("_MonolithHasTerrainSourceColor"));
                material.SetTexture("_MonolithTerrainSourceColorR", null);
                MonolithMaterialInspector.UpdateTexturePresence(material);
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithHasTerrainSourceColor"));
                material.SetTexture("_MonolithTerrainHeightG", null);
                MonolithMaterialInspector.UpdateTexturePresence(material);
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithHasTerrainHeight"));
                material.SetTexture("_MonolithTerrainHeightG", height);
                material.SetFloat("_MonolithTerrainSource", (float)XRayTerrainSourceProfile.TrueStalker);
                MonolithMaterialInspector.UpdateTexturePresence(material);
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithHasTerrainHeight"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceProfile"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithSourceTerrain"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(height);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TerrainSourceMetadataDoesNotChangeExistingMaterialIdentity()
        {
            var terrain = new XRayLevelTerrainTextures();
            string identity = terrain.Key();
            terrain.Source = new XRayTerrainSourceBinding();
            terrain.SourceBasePath = "source_base";
            for (int i = 0; i < terrain.HeightPaths.Length; i++)
            {
                terrain.HeightPaths[i] = "height_" + i;
                terrain.SourceColorPaths[i] = "source_color_" + i;
            }
            Assert.AreEqual(identity, terrain.Key());
        }

        [TestCase(XRayTerrainSourceBinding.ANOMALY_SHADER_HASH, XRayTerrainSourceBinding.ANOMALY_BRDF_HASH, XRayTerrainSourceProfile.AnomalyAtmospherics)]
        [TestCase(XRayTerrainSourceBinding.TRUE_STALKER_SHADER_HASH, XRayTerrainSourceBinding.TRUE_STALKER_BRDF_HASH, XRayTerrainSourceProfile.TrueStalker)]
        [TestCase(XRayTerrainSourceBinding.ANOMALY_SHADER_HASH, XRayTerrainSourceBinding.TRUE_STALKER_BRDF_HASH, XRayTerrainSourceProfile.Unknown)]
        [TestCase(XRayTerrainSourceBinding.TRUE_STALKER_SHADER_HASH, XRayTerrainSourceBinding.ANOMALY_BRDF_HASH, XRayTerrainSourceProfile.Unknown)]
        [TestCase("", "", XRayTerrainSourceProfile.Unknown)]
        public void TerrainContractRequiresMatchingShaderAndBrdf(string shaderHash, string brdfHash, XRayTerrainSourceProfile expected)
        {
            Assert.AreEqual(expected, XRayTerrainSourceBinding.Identify(shaderHash, brdfHash));
        }

        private static void Compare(Vector4[] cases, Vector4[] results, string reference)
        {
            string[] lines = reference.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(results.Length, lines.Length);
            string[] names = { "Terrain_Layer_Blend_Parity", "Terrain_Normal_Parity", "Terrain_POM_Parity", "Terrain_AO_Parity" };
            var reports = new StringBuilder[names.Length];
            for (int group = 0; group < reports.Length; group++) reports[group] = new StringBuilder("API,Case,Mode,SourceSingular,MaxAbsolute,MaxRelative,Failures\n");
            int failures = 0;
            string firstFailure = null;
            int count = cases.Length / MonolithSourceTerrainData.CASE_STRIDE;
            for (int i = 0; i < count; i++)
            {
                bool singular = cases[i * MonolithSourceTerrainData.CASE_STRIDE + 5].Equals(Vector4.zero);
                var absolute = new float[4];
                var relative = new float[4];
                var errors = new int[4];
                for (int v = 0; v < MonolithSourceTerrainData.RESULT_STRIDE; v++)
                {
                    string[] values = lines[i * MonolithSourceTerrainData.RESULT_STRIDE + v].Split(' ');
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int group = v == 0 || v == 5 || v >= 13 && v <= 17 ? 0 : v == 6 && channel < 3 || v == 7 && channel < 3 || v >= 9 && v <= 12 ? 1 : v >= 1 && v <= 3 || v == 7 && channel == 3 || v == 8 ? 2 : 3;
                        float actual = results[i * MonolithSourceTerrainData.RESULT_STRIDE + v][channel];
                        float expected = float.Parse(values[channel], CultureInfo.InvariantCulture);
                        bool finite = !float.IsNaN(actual) && !float.IsInfinity(actual);
                        float delta = singular ? 0.0f : Mathf.Abs(actual - expected);
                        bool passed = finite && (singular || !float.IsNaN(expected) && !float.IsInfinity(expected) && delta <= 2e-6f + 2e-5f * Mathf.Abs(expected));
                        absolute[group] = Mathf.Max(absolute[group], delta);
                        relative[group] = Mathf.Max(relative[group], singular ? 0.0f : delta / Mathf.Max(1e-8f, Mathf.Abs(expected)));
                        if (!passed)
                        {
                            errors[group]++;
                            failures++;
                            if (firstFailure == null) firstFailure = "Case=" + i + " Vector=" + v + " Channel=" + channel + " Expected=" + expected.ToString("R", CultureInfo.InvariantCulture) + " Actual=" + actual.ToString("R", CultureInfo.InvariantCulture);
                        }
                    }
                }
                for (int group = 0; group < reports.Length; group++) reports[group].Append(SystemInfo.graphicsDeviceType).Append(',').Append(i).Append(',').Append(cases[i * MonolithSourceTerrainData.CASE_STRIDE].w).Append(',').Append(singular).Append(',').Append(absolute[group].ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(relative[group].ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(errors[group]).AppendLine();
            }
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE05_OUTPUT");
            if (!string.IsNullOrEmpty(output))
            {
                if (!Path.GetFullPath(output).StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Terrain diagnostics must be stored in Archive.");
                Directory.CreateDirectory(output);
                for (int group = 0; group < reports.Length; group++) File.WriteAllText(Path.Combine(output, names[group] + ".csv"), reports[group].ToString());
                using (var writer = new BinaryWriter(File.Create(Path.Combine(output, "Terrain_GPU_Readback.bin"))))
                {
                    foreach (Vector4 value in results)
                    {
                        for (int channel = 0; channel < 4; channel++) writer.Write(value[channel]);
                    }
                }
            }
            Assert.AreEqual(0, failures, firstFailure);
        }
    }
}
