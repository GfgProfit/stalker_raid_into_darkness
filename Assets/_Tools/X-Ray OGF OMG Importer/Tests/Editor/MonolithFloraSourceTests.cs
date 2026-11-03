using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
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
        private int _readbacks;

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [Test]
        public void OriginalFloraHlslMatchesMonolithFlora()
        {
            string output = Output();
            string template = File.ReadAllText(MonolithFloraReferenceSource.TEMPLATE);
            Shader actualShader = Shader.Find("Hidden/XRay/MonolithFloraValidation");
            Assert.IsNotNull(actualShader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(actualShader), Messages(actualShader));
            Material actual = Own(new Material(actualShader));
            var rows = new StringBuilder("Group,Source,Case,Stage,MaxAbsoluteError,MaxRelativeError,Tolerance,ComparedComponents,SourceUndefinedComponents\n");
            var manifests = new StringBuilder();
            Texture2D waves = Own(Waves());
            for (int group = 0; group < MonolithFloraReferenceSource.GROUP_COUNT; group++)
            {
                string reference = MonolithFloraReferenceSource.Build(group, template, out string manifest);
                manifests.AppendLine("[" + MonolithFloraReferenceSource.NAMES[group] + "]").Append(manifest);
                File.WriteAllText(Path.Combine(output, "Original_Reference_" + MonolithFloraReferenceSource.NAMES[group] + ".shader"), reference);
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
                        double maximum = 0;
                        double relative = 0;
                        int undefined = 0;
                        int compared = 0;
                        double tolerance = 0;
                        for (int pixel = 0; pixel < reference0.Length; pixel++)
                        {
                            for (int channel = 0; channel < 4; channel++)
                            {
                                float r = reference0[pixel][channel];
                                float o = observed[pixel][channel];
                                if (float.IsNaN(r) || float.IsInfinity(r))
                                {
                                    undefined++;
                                    continue;
                                }
                                Assert.IsFalse(float.IsNaN(o) || float.IsInfinity(o), "Nonfinite Monolith output " + MonolithFloraReferenceSource.NAMES[group] + " stage " + stage);
                                double delta = Math.Abs(r - o);
                                double limit = 0.00002 + 0.000002 * Math.Abs(r);
                                maximum = Math.Max(maximum, delta);
                                relative = Math.Max(relative, delta / Math.Max(0.0000001, Math.Abs(r)));
                                tolerance = Math.Max(tolerance, limit);
                                compared++;
                                Assert.LessOrEqual(delta, limit, MonolithFloraReferenceSource.NAMES[group] + " case " + index + " stage " + stage + " pixel " + pixel + " channel " + channel + " reference " + r.ToString("R", CultureInfo.InvariantCulture) + " observed " + o.ToString("R", CultureInfo.InvariantCulture));
                            }
                        }
                        rows.AppendLine(string.Join(",", group, MonolithFloraReferenceSource.NAMES[group], index, stage, maximum.ToString("R", CultureInfo.InvariantCulture), relative.ToString("R", CultureInfo.InvariantCulture), tolerance.ToString("R", CultureInfo.InvariantCulture), compared, undefined));
                        File.WriteAllText(Path.Combine(output, "Flora_Source_Parity.csv"), rows.ToString());
                    }
                }
            }
            File.WriteAllText(Path.Combine(output, "Original_Source_Manifest.txt"), manifests.ToString());
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
            string outputFolder = detail ? "GPU_ColorVariation_Grass" : "GPU_ColorVariation_Trees";
            foreach (float phase in new[] { 0.0f, 0.27f, 0.61f, 0.98f })
            {
                material.SetVector("_Extra", new Vector4(phase, 0.0f, 0.0f, 0.0f));
                material.SetFloat("_Stage", 22.0f);
                material.SetFloat("_VariationEnabled", defaults.GetFloat("_MonolithFloraVariationEnabled"));
                material.SetVector("_Variation", variation);
                Color[] disabled = Render(material, outputFolder);
                material.SetVector("_Variation", Vector4.zero);
                Color[] neutral = Render(material, outputFolder);
                material.SetVector("_Variation", variation);
                material.SetFloat("_VariationEnabled", 1.0f);
                Color[] enabled = Render(material, outputFolder);
                material.SetFloat("_Stage", 1.0f);
                Color[] legacy = Render(material, outputFolder);
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
            var rows = new StringBuilder("Material,Detail,Variation,Enabled\n");
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
                rows.AppendLine(path + "," + detail + ",\"" + material.GetVector("_MonolithFloraVariation") + "\"," + material.GetFloat("_MonolithFloraVariationEnabled"));
            }
            Assert.Greater(leaves, 0);
            Assert.Greater(grass, 0);
            File.WriteAllText(Path.Combine(Output(), "Flora_Color_Variation.csv"), rows.ToString());
        }

        [Test]
        public void SavedFloraMaterialsStayOnLegacyPath()
        {
            var rows = new StringBuilder("Material,FloraKeyword,StoredFloraSource\n");
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
                    rows.AppendLine(path + ",1," + (stored.Success ? stored.Groups[1].Value : "absent"));
                    Assert.IsTrue(!stored.Success || float.Parse(stored.Groups[1].Value, CultureInfo.InvariantCulture) == 0.0f, path);
                }
            }
            File.WriteAllText(Path.Combine(Output(), "Saved_Flora_Materials.csv"), rows.ToString());
            Assert.Greater(flora, 0);
        }

        [UnityEngine.TestTools.UnityTest]
        [Timeout(900000)]
        public IEnumerator AnomalyEscapeFloraSourceAB()
        {
            yield return RealLevel(false);
        }

        [UnityEngine.TestTools.UnityTest]
        [Timeout(900000)]
        public IEnumerator TrueStalkerEscapeFloraSourceAB()
        {
            yield return RealLevel(true);
        }

        private IEnumerator RealLevel(bool trueStalker)
        {
            HdrpTestWarmup.Ensure();
            string output = Path.Combine(Output(), trueStalker ? "Real_TS_escape" : "Real_Anomaly_l01_escape");
            Directory.CreateDirectory(output);
            string root = trueStalker ? TS_ROOT : ANOMALY_ROOT;
            string level = trueStalker ? "escape" : "l01_escape";
            var set = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(root + "/" + level + "/Meshes/" + level + "_StaticProps.asset");
            Assert.IsNotNull(set);
            XRayInstanceBatch selected = null;
            Material stored = null;
            Texture2D correction = null;
            foreach (XRayInstanceBatch batch in set.Batches)
            {
                Material candidate = batch.Materials != null && batch.Materials.Length > 0 ? batch.Materials[0] : null;
                if (candidate == null || !candidate.IsKeywordEnabled("_MONOLITH_LEVEL_FLORA") || candidate.GetTexture("_MonolithBump") == null || batch.Count == 0) continue;
                Texture2D candidateCorrection = Correction(candidate);
                if (trueStalker && candidateCorrection == null) continue;
                string albedoPath = AssetDatabase.GetAssetPath(candidate.GetTexture("_BaseColorMap"));
                if (!albedoPath.Contains("/trees")) continue;
                bool preferred = albedoPath.Contains(trueStalker ? "vetkagreen" : "alder");
                bool selectedPreferred = selected != null && AssetDatabase.GetAssetPath(stored.GetTexture("_BaseColorMap")).Contains(trueStalker ? "vetkagreen" : "alder");
                if (selected != null && (selectedPreferred && !preferred || selectedPreferred == preferred && Size(batch) <= Size(selected))) continue;
                selected = batch;
                stored = candidate;
                correction = candidateCorrection;
            }
            Assert.IsNotNull(selected, "No flora batch with required source bindings");
            string materialPath = AssetDatabase.GetAssetPath(stored);
            string storedBytes = File.ReadAllText(materialPath);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            var metrics = new StringBuilder("Mode,Debug,NonFinite,CoveredPixels\n");
            var comparisons = new StringBuilder("Comparison,Debug,MaxAbsoluteDelta,ComparedComponents\n");
            var timings = new StringBuilder("Mode,Frame,GpuNanoseconds,GpuBlocks\n");
            var pixelsByMode = new Dictionary<string, Color[]>();
            try
            {
                var geometry = new GameObject("Read-only imported flora capture");
                temporary.Add(geometry);
                SceneManager.MoveGameObjectToScene(geometry, scene);
                Matrix4x4 matrix = selected.Matrices[0];
                geometry.transform.SetPositionAndRotation(matrix.GetColumn(3), matrix.rotation);
                geometry.transform.localScale = matrix.lossyScale;
                geometry.AddComponent<MeshFilter>().sharedMesh = selected.Mesh;
                MeshRenderer renderer = geometry.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                Material material = new Material(stored);
                temporary.Add(material);
                renderer.sharedMaterial = material;
                Shader currentShader = material.shader;
                Shader frozenShader = FrozenShader();
                temporary.Add(frozenShader);
                Assert.IsFalse(ShaderUtil.ShaderHasError(frozenShader), Messages(frozenShader));
                Bounds bounds = renderer.bounds;
                var rig = new GameObject("Flora capture rig");
                temporary.Add(rig);
                SceneManager.MoveGameObjectToScene(rig, scene);
                Camera camera = new GameObject("Flora Capture Camera").AddComponent<Camera>();
                camera.transform.SetParent(rig.transform);
                camera.scene = scene;
                camera.enabled = false;
                camera.fieldOfView = 50;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 500;
                camera.aspect = 640.0f / 360;
                camera.allowHDR = true;
                camera.useOcclusionCulling = false;
                float distance = Mathf.Max(2.0f, bounds.extents.magnitude * 1.1f);
                camera.transform.position = bounds.center + new Vector3(0.35f, 0.25f, -1.0f).normalized * distance;
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
                Light sun = new GameObject("Flora Capture Sun").AddComponent<Light>();
                sun.transform.SetParent(rig.transform);
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.LookRotation((camera.transform.forward + Vector3.down * 0.6f).normalized, Vector3.up);
                sun.shadows = LightShadows.None;
                sun.gameObject.AddComponent<HDAdditionalLightData>().SetIntensity(3, LightUnit.Lux);
                rig.AddComponent<MonolithWeatherInputs>().SetInputs(0.71864f, Vector3.one, new Vector4(0.5f, 1, 0, 0), new Vector4(1.5f, 1, 0, 0), 0);
                rig.AddComponent<MonolithSourceLighting>().Mode = MonolithSourceLighting.LightingMode.Native;
                File.WriteAllText(Path.Combine(output, "Settings.txt"), "Source=" + (trueStalker ? "True Stalker Patch 1.5 escape" : "Anomaly MO2 l01_escape") + "\nBatch=" + selected.Name + "\nMesh=" + AssetDatabase.GetAssetPath(selected.Mesh) + "\nMaterial=" + materialPath + "\nCorrection=" + (correction != null ? AssetDatabase.GetAssetPath(correction) : "none") + "\nInstanceMatrix=" + matrix.ToString("F6") + "\nAPI=" + SystemInfo.graphicsDeviceType + "\nDevice=" + SystemInfo.graphicsDeviceName + "\nResolution=640x360\nCamera=" + camera.transform.position.ToString("F6") + "\nRotation=" + camera.transform.eulerAngles.ToString("F6") + "\nFOV=50\nSun=3 lux directional, shadows off\nExposure=0 fixed\nWeather=dynamic gloss 0.71864, sun color 1, ssfx_lightsetup_1 (0.5,1), ssfx_florafixes_2 (1.5,1), wetness 0\nFloraWetness=0\nInstanceData=non-instanced MeshRenderer, hemi scale 1 bias 0\nWarmup=6 renders per mode\n");
                var modes = new List<string> { "Frozen", "Legacy", "Source" };
                if (trueStalker) modes.Add("SourceWithoutCorrection");
                using (var target = new Target(camera))
                {
                    foreach (string mode in modes)
                    {
                        material.CopyPropertiesFromMaterial(stored);
                        material.shader = mode == "Frozen" ? frozenShader : currentShader;
                        if (mode == "Source")
                        {
                            Assert.IsTrue(MonolithMaterialConfigurator.ConfigureFloraSource(material, trueStalker ? MonolithMaterialConfigurator.FLORA_SOURCE_TRUE_STALKER : MonolithMaterialConfigurator.FLORA_SOURCE_ANOMALY, correction));
                            material.SetFloat("_MonolithSourceProfile", trueStalker ? 0.0f : 1.0f);
                        }
                        if (mode == "SourceWithoutCorrection")
                        {
                            material.SetFloat("_MonolithFloraSource", MonolithMaterialConfigurator.FLORA_SOURCE_TRUE_STALKER);
                            Assert.AreEqual(0.0f, material.GetFloat("_MonolithHasBumpCorrection"));
                        }
                        File.WriteAllText(Path.Combine(output, mode + "_Properties.json"), EditorJsonUtility.ToJson(material, true));
                        for (int warm = 0; warm < 6; warm++)
                        {
                            camera.Render();
                            yield return null;
                        }
                        int[] debugs = mode == "Source" ? new[] { 0, 1, 3, 7, 12, 13, 19, 30, 31, 67, 68, 85, 86, 87, 88 } : mode == "Frozen" ? new[] { 0, 1, 3, 7, 12, 67, 68 } : new[] { 0, 1, 3, 7, 12, 67, 68, 85 };
                        foreach (int debug in debugs)
                        {
                            material.SetFloat("_MonolithDebug", debug);
                            camera.Render();
                            camera.Render();
                            Color[] pixels = target.Pixels();
                            int nonFinite = 0;
                            int covered = 0;
                            foreach (Color pixel in pixels)
                            {
                                for (int channel = 0; channel < 3; channel++) if (float.IsNaN(pixel[channel]) || float.IsInfinity(pixel[channel])) nonFinite++;
                                if (debug == 85 && pixel.g > 0.5f) covered++;
                            }
                            metrics.AppendLine(mode + "," + debug + "," + nonFinite + "," + covered);
                            Assert.AreEqual(0, nonFinite, mode + " debug " + debug);
                            pixelsByMode[mode + "_" + debug] = pixels;
                            target.Save(Path.Combine(output, mode + "_D" + debug));
                        }
                        material.SetFloat("_MonolithDebug", 0);
                        CustomSampler sampler = CustomSampler.Create("Phase06A_Flora_" + mode, true);
                        Recorder recorder = sampler.GetRecorder();
                        bool previousProfiler = Profiler.enabled;
                        try
                        {
                            Profiler.enabled = true;
                            recorder.enabled = true;
                            for (int frame = 0; frame < 36; frame++)
                            {
                                sampler.Begin();
                                camera.Render();
                                sampler.End();
                                yield return null;
                                if (frame >= 12) timings.Append(mode).Append(',').Append(frame).Append(',').Append(recorder.gpuElapsedNanoseconds).Append(',').Append(recorder.gpuSampleBlockCount).AppendLine();
                            }
                        }
                        finally
                        {
                            recorder.enabled = false;
                            Profiler.enabled = previousProfiler;
                        }
                    }
                }
                foreach (int debug in new[] { 0, 1, 3, 7, 12, 67, 68 }) Compare(comparisons, "Frozen_vs_Legacy", debug, pixelsByMode["Frozen_" + debug], pixelsByMode["Legacy_" + debug], 0.00002f);
                foreach (int debug in new[] { 67, 68 })
                {
                    float maximum = Delta(pixelsByMode["Legacy_" + debug], pixelsByMode["Source_" + debug], 3);
                    comparisons.AppendLine("Legacy_vs_Source_Silhouette," + debug + "," + maximum.ToString("R", CultureInfo.InvariantCulture) + "," + pixelsByMode["Source_" + debug].Length * 3);
                }
                Assert.AreEqual(Coverage(pixelsByMode["Legacy_85"]), Coverage(pixelsByMode["Source_85"]), "Alpha silhouette changed by flora source mode");
                Assert.Greater(Coverage(pixelsByMode["Source_85"]), 0, "Flora mesh not visible");
                Assert.AreEqual(0.0f, Delta(pixelsByMode["Legacy_67"], pixelsByMode["Source_67"], 3), "Depth silhouette changed by flora source mode");
                if (trueStalker)
                {
                    foreach (int debug in new[] { 0, 1, 3, 7, 12, 67, 68, 85 }) Compare(comparisons, "Legacy_vs_SourceWithoutCorrection", debug, pixelsByMode["Legacy_" + debug], pixelsByMode["SourceWithoutCorrection_" + debug], 0.00002f);
                }
            }
            finally
            {
                File.WriteAllText(Path.Combine(output, "Metrics.csv"), metrics.ToString());
                File.WriteAllText(Path.Combine(output, "Comparisons.csv"), comparisons.ToString());
                File.WriteAllText(Path.Combine(output, "GPU_Timings.csv"), timings.ToString());
                Assert.AreEqual(storedBytes, File.ReadAllText(materialPath));
                Assert.IsFalse(EditorUtility.IsDirty(stored));
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static float Size(XRayInstanceBatch batch)
        {
            return batch.Mesh.bounds.size.magnitude * batch.Matrices[0].lossyScale.x;
        }

        private static Texture2D Correction(Material material)
        {
            string bumpPath = AssetDatabase.GetAssetPath(material.GetTexture("_MonolithBump"));
            if (string.IsNullOrEmpty(bumpPath) || !bumpPath.EndsWith("_bump_packed.dds", StringComparison.Ordinal)) return null;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(bumpPath.Substring(0, bumpPath.Length - "_bump_packed.dds".Length) + "_bump__packed.dds");
        }

        private static void Compare(StringBuilder rows, string name, int debug, Color[] a, Color[] b, float tolerance)
        {
            float maximum = Delta(a, b, 3);
            rows.AppendLine(name + "," + debug + "," + maximum.ToString("R", CultureInfo.InvariantCulture) + "," + a.Length * 3);
            Assert.LessOrEqual(maximum, tolerance, name + " debug " + debug);
        }

        private static float Delta(Color[] a, Color[] b, int channels)
        {
            Assert.AreEqual(a.Length, b.Length);
            float maximum = 0;
            for (int pixel = 0; pixel < a.Length; pixel++)
            {
                for (int channel = 0; channel < channels; channel++) maximum = Mathf.Max(maximum, Mathf.Abs(a[pixel][channel] - b[pixel][channel]));
            }
            return maximum;
        }

        private static int Coverage(Color[] pixels)
        {
            int covered = 0;
            foreach (Color pixel in pixels) if (pixel.g > 0.5f) covered++;
            return covered;
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

        private Color[] Render(Material material, string outputFolder = "GPU_Numeric")
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
                Color[] pixels = readback.GetPixels();
                string folder = Path.Combine(Output(), outputFolder);
                Directory.CreateDirectory(folder);
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

        private static Shader FrozenShader()
        {
            string folder = Environment.GetEnvironmentVariable("MONOLITH_PHASE06A_BASELINE");
            Assert.IsNotEmpty(folder);
            folder = Path.Combine(folder, "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders");
            string source = Expand(Path.Combine(folder, "MonolithLit.shader"));
            source = source.Replace("Shader \"HDRP/MonolithLit\"", "Shader \"Hidden/XRay/Phase06A_FrozenMonolithLit\"");
            Assert.IsFalse(Regex.IsMatch(source, "#include \\\"Monolith"));
            File.WriteAllText(Path.Combine(Output(), "Frozen_Checkpoint.shader"), source);
            return ShaderUtil.CreateShaderAsset(source);
        }

        private static string Expand(string file)
        {
            string source = File.ReadAllText(file);
            return Regex.Replace(source, "#include \\\"(Monolith[^\\\"]+)\\\"", match => Expand(Path.Combine(Path.GetDirectoryName(file), match.Groups[1].Value)));
        }

        private static string Output()
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE06A_OUTPUT");
            Assert.IsNotEmpty(output);
            Assert.IsTrue(Path.GetFullPath(output).StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(output);
            return output;
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

        private sealed class Target : IDisposable
        {
            private readonly Camera _camera;
            private readonly RenderTexture _target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            private readonly Texture2D _pixels = new Texture2D(640, 360, TextureFormat.RGBAFloat, false, true);

            public Target(Camera camera)
            {
                _camera = camera;
                _target.Create();
                camera.targetTexture = _target;
            }

            public Color[] Pixels()
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _target;
                _pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
                _pixels.Apply();
                RenderTexture.active = previous;
                return _pixels.GetPixels();
            }

            public void Save(string path)
            {
                using (var writer = new BinaryWriter(File.Open(path + ".rgba32f", FileMode.CreateNew)))
                {
                    foreach (Color pixel in _pixels.GetPixels())
                    {
                        for (int channel = 0; channel < 4; channel++) writer.Write(pixel[channel]);
                    }
                }
                File.WriteAllBytes(path + ".exr", _pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                var display = new Texture2D(640, 360, TextureFormat.RGBA32, false, true);
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
