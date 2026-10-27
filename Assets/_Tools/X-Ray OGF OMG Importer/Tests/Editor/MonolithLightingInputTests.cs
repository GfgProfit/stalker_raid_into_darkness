using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Tests
{
    public sealed class MonolithLightingInputTests
    {
        private const string ROOT = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/";
        private const float TOLERANCE = 0.000002f;

        [Test]
        public void LightingMatchesIndependentSourceHlsl()
        {
            string[] lines = File.ReadAllLines(ROOT + "Fixtures/MonolithLightingReference.txt");
            var input = new Vector4[lines.Length * 3];
            var expected = new Vector4[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                float[] row = Array.ConvertAll(lines[i].Split(' '), v => float.Parse(v, CultureInfo.InvariantCulture));
                input[i * 3] = new Vector4(row[0], row[1], row[2], 0);
                input[i * 3 + 1] = new Vector4(row[3], row[4], row[5], 0);
                input[i * 3 + 2] = new Vector4(row[6], row[7], row[8], 0);
                expected[i] = new Vector4(row[9], row[10], row[11], row[12]);
            }
            ComputeShader shader = Load();
            int kernel = shader.FindKernel("Lighting");
            using (var source = new ComputeBuffer(input.Length, 16))
            using (var target = new ComputeBuffer(expected.Length, 16))
            {
                source.SetData(input);
                shader.SetBuffer(kernel, "Input", source);
                shader.SetBuffer(kernel, "Output", target);
                shader.Dispatch(kernel, expected.Length, 1, 1);
                var actual = new Vector4[expected.Length];
                target.GetData(actual);
                var report = new string[expected.Length + 1];
                report[0] = "case,expectedAttenuation,actualAttenuation,maxAbsoluteError";
                for (int i = 0; i < actual.Length; i++)
                {
                    float error = 0;
                    for (int c = 0; c < 4; c++)
                    {
                        Assert.IsFalse(float.IsNaN(actual[i][c]) || float.IsInfinity(actual[i][c]), "case " + i);
                        error = Mathf.Max(error, Mathf.Abs(actual[i][c] - expected[i][c]));
                        Assert.That(actual[i][c], Is.EqualTo(expected[i][c]).Within(TOLERANCE + 0.00002f * Mathf.Abs(expected[i][c])), "case " + i + " channel " + c);
                    }
                    report[i + 1] = FormattableString.Invariant($"{i},{expected[i].x:R},{actual[i].x:R},{error:R}");
                }
                WriteReport("Light_Attenuation_Parity.csv", report);
            }
        }

        [Test]
        public void WeatherGlobalsOverrideOnlySourceProfileAndReset()
        {
            var gameObject = new GameObject("Monolith weather regression");
            try
            {
                var provider = gameObject.AddComponent<MonolithWeatherInputs>();
                ComputeShader shader = Load();
                int kernel = shader.FindKernel("Weather");
                shader.SetFloat("_LocalGloss", 0.42f);
                using (var target = new ComputeBuffer(5, 16))
                {
                    shader.SetBuffer(kernel, "Output", target);
                    var actual = new Vector4[5];
                    var report = new string[6];
                    report[0] = "wetness,sourceGloss,cpuGloss,gpuGloss";
                    for (int i = 0; i < 5; i++)
                    {
                        float wetness = i / 4.0f;
                        provider.SetWetness(wetness);
                        float expected = 0.65f + 0.11f * (0.65f + 0.35f * wetness) * 0.96f;
                        shader.SetFloat("_Profile", 1);
                        shader.Dispatch(kernel, 5, 1, 1);
                        target.GetData(actual);
                        Assert.That(provider.DynamicGloss, Is.EqualTo(expected).Within(TOLERANCE));
                        Assert.That(actual[i].x, Is.EqualTo(expected).Within(TOLERANCE));
                        Assert.That(actual[i].y, Is.EqualTo(expected).Within(TOLERANCE));
                        shader.SetFloat("_Profile", 0);
                        shader.Dispatch(kernel, 5, 1, 1);
                        target.GetData(actual);
                        Assert.That(actual[i].y, Is.EqualTo(0.42f).Within(TOLERANCE));
                        report[i + 1] = FormattableString.Invariant($"{wetness:R},{expected:R},{provider.DynamicGloss:R},{actual[i].x:R}");
                    }
                    provider.SetInputs(0.73f, new Vector3(0.9f, 0.75f, 0.55f), new Vector4(0.2f, 0.4f, 0, 0), new Vector4(1.2f, 1, 0, 0), 0.5f);
                    Assert.That(Shader.GetGlobalVector("_MonolithWeatherSun").x, Is.EqualTo(0.9f));
                    Assert.Throws<ArgumentOutOfRangeException>(() => provider.SetWetness(float.NaN));
                    Assert.Throws<ArgumentOutOfRangeException>(() => provider.SetInputs(0.99f, Vector3.one, Vector4.one, Vector4.one, 0));
                    provider.enabled = false;
                    shader.SetFloat("_Profile", 1);
                    shader.Dispatch(kernel, 5, 1, 1);
                    target.GetData(actual);
                    Assert.That(actual[0].y, Is.EqualTo(0.42f).Within(TOLERANCE));
                    Assert.AreEqual(0.0f, Shader.GetGlobalFloat("_MonolithWeatherActive"));
                    WriteReport("Weather_Parity_Results.csv", report);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LightBindingsKeepDifferentColorsAndReleaseGlobals()
        {
            var root = new GameObject("Monolith light regression");
            var second = new GameObject("Monolith second light");
            try
            {
                Light firstLight = root.AddComponent<Light>();
                firstLight.type = LightType.Point;
                Light secondLight = second.AddComponent<Light>();
                secondLight.type = LightType.Spot;
                second.transform.position = new Vector3(2, 3, 4);
                var controller = root.AddComponent<MonolithSourceLighting>();
                controller.SetBindings(new[] { firstLight, secondLight }, new[] { new Vector3(1, 0.2f, 0.1f), new Vector3(0.1f, 0.3f, 1) });
                controller.Mode = MonolithSourceLighting.LightingMode.Bound;
                Assert.AreEqual(1, controller.BoundRadianceScale);
                controller.BoundRadianceScale = 512;
                Assert.AreEqual(512, Shader.GetGlobalFloat("_MonolithBoundRadianceScale"));
                Assert.Throws<ArgumentOutOfRangeException>(() => controller.BoundRadianceScale = float.NaN);
                Assert.AreEqual(2, Shader.GetGlobalInt("_MonolithBoundLightCount"));
                Vector4[] colors = Shader.GetGlobalVectorArray("_MonolithBoundColor64");
                Assert.AreEqual(1.0f, colors[0].x);
                Assert.AreEqual(0.1f, colors[1].x);
                Assert.AreEqual(1.0f, colors[1].z);
                secondLight.enabled = false;
                controller.Publish();
                Assert.AreEqual(1, Shader.GetGlobalInt("_MonolithBoundLightCount"));
                var fullLights = new Light[64];
                var fullColors = new Vector3[64];
                for (int i = 0; i < fullLights.Length; i++)
                {
                    fullLights[i] = firstLight;
                    fullColors[i] = new Vector3(i / 63.0f, 0.2f, 0.4f);
                }
                controller.SetBindings(fullLights, fullColors);
                Assert.AreEqual(64, Shader.GetGlobalInt("_MonolithBoundLightCount"));
                Assert.AreEqual(64, Shader.GetGlobalVectorArray("_MonolithBoundColor64").Length);
                Assert.AreEqual(1.0f, Shader.GetGlobalVectorArray("_MonolithBoundColor64")[63].x);
                Assert.Throws<ArgumentException>(() => controller.SetBindings(new Light[65], new Vector3[65]));
                controller.enabled = false;
                Assert.AreEqual(0, Shader.GetGlobalInt("_MonolithBoundLightCount"));
                Assert.AreEqual(0.0f, Shader.GetGlobalFloat("_MonolithSourceLightingMode"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BindingsRefreshBeforeRenderingAndUseRequestedShadowMode()
        {
            var root = new GameObject("Monolith binding render regression");
            var target = new RenderTexture(16, 16, 24);
            try
            {
                Light light = root.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.Soft;
                var controller = root.AddComponent<MonolithSourceLighting>();
                controller.SetBindings(new[] { light }, new[] { new Vector3(0.2f, 0.4f, 0.8f) });
                controller.Mode = MonolithSourceLighting.LightingMode.Bound;
                var cameraObject = new GameObject("Binding render camera");
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 0;
                camera.targetTexture = target;
                cameraObject.AddComponent<HDAdditionalCameraData>().volumeLayerMask = 0;
                root.transform.position = new Vector3(12, 34, 56);
                light.range = 9;
                camera.Render();
                Vector4 position = Shader.GetGlobalVectorArray("_MonolithBoundPositionType64")[0];
                Assert.AreEqual(new Vector4(12, 34, 56, 1), position);
                Assert.AreEqual(9, Shader.GetGlobalVectorArray("_MonolithBoundDirectionRange64")[0].w);
                Assert.AreEqual(1, Shader.GetGlobalVectorArray("_MonolithBoundColor64")[0].w);
                light.shadows = LightShadows.None;
                camera.Render();
                Vector4 color = Shader.GetGlobalVectorArray("_MonolithBoundColor64")[0];
                Assert.AreEqual(new Vector4(0.2f, 0.4f, 0.8f, 2), color);
                controller.enabled = false;
                camera.Render();
                Assert.AreEqual(0, Shader.GetGlobalInt("_MonolithBoundLightCount"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void TerrainRemapIsOptInAndPreservesOtherMaterialIds()
        {
            float[] materialIds = { 0.0f, 0.15f, 0.751f, 0.90f, 0.911f, 0.95f, 0.989f, 0.991f, 1.2f };
            var input = new Vector4[materialIds.Length];
            for (int i = 0; i < input.Length; i++) input[i].x = materialIds[i];
            ComputeShader shader = Load();
            int kernel = shader.FindKernel("Terrain");
            using (var source = new ComputeBuffer(input.Length, 16))
            using (var target = new ComputeBuffer(input.Length, 16))
            {
                source.SetData(input);
                shader.SetBuffer(kernel, "Input", source);
                shader.SetBuffer(kernel, "Output", target);
                shader.Dispatch(kernel, input.Length, 1, 1);
                var actual = new Vector4[input.Length];
                target.GetData(actual);
                for (int i = 0; i < input.Length; i++) Assert.That(actual[i].x, Is.EqualTo(Mathf.Abs(materialIds[i] - 0.95f) <= 0.04f ? 0 : materialIds[i]).Within(TOLERANCE));
                Assert.AreEqual(0.25f, actual[1].y);
                Assert.Greater(actual[5].z, 0);
                Assert.AreEqual(0, actual[5].w);
            }
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            try
            {
                Assert.AreEqual(0, material.GetFloat("_MonolithTrueStalkerTerrain"));
                Assert.AreEqual(0, material.GetFloat("_MonolithSourceProfile"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void BarkSubsurfaceMaskPreservesLeavesAndWeatherOverrides()
        {
            ComputeShader shader = Load();
            int kernel = shader.FindKernel("FloraMask");
            Vector4[] input = { new Vector4(1, 0, 1, 0), new Vector4(1, 1, 1, 0), new Vector4(0, 0, 1, 0), new Vector4(0, 1, 1, 0), new Vector4(1, 1, 0, 0) };
            float[] expected = { 1.5f, 2, 0, 0, 1.5f };
            using (var source = new ComputeBuffer(input.Length, 16))
            using (var target = new ComputeBuffer(input.Length, 16))
            {
                source.SetData(input);
                shader.SetBuffer(kernel, "Input", source);
                shader.SetBuffer(kernel, "Output", target);
                shader.Dispatch(kernel, input.Length, 1, 1);
                var actual = new Vector4[input.Length];
                target.GetData(actual);
                for (int i = 0; i < actual.Length; i++)
                {
                    for (int c = 0; c < 4; c++) Assert.That(actual[i][c], Is.EqualTo(expected[i]).Within(TOLERANCE));
                }
            }
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            try
            {
                Assert.AreEqual(1, material.GetFloat("_MonolithFloraSubsurface"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static ComputeShader Load()
        {
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ROOT + "MonolithLightingValidation.compute");
            Assert.IsNotNull(shader);
            return shader;
        }

        private static void WriteReport(string name, string[] lines)
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE02_OUTPUT");
            if (string.IsNullOrEmpty(output)) return;
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, name), lines);
        }
    }
}
