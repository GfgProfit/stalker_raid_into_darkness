using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rvm.CleanReleaseRoute07;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Rvm.Benchmark.Tests
{
    public sealed class PunctualShadowDistanceVisualTests
    {
        private const int SIZE = 256;
        private const float DISPLAY_WHITE = 256f;
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive";
        private const string EMPTY_SCENE = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/NativeSubmissionEmpty.unity";

        [UnityTest]
        public IEnumerator PointShadowDistancePreservesIlluminationNearAndFar()
        {
            return RenderScenario(LightType.Point);
        }

        [UnityTest]
        public IEnumerator SpotShadowDistancePreservesIlluminationNearAndFar()
        {
            return RenderScenario(LightType.Spot);
        }

        [UnityTest]
        public IEnumerator DirectionalShadowsRemainUnchangedNearAndFar()
        {
            return RenderScenario(LightType.Directional);
        }

        private static IEnumerator RenderScenario(LightType type)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("The shadow-distance fixture requires a graphics device.");
            if (!StageUtility.GetCurrentStageHandle().Equals(StageUtility.GetMainStageHandle())) Assert.Ignore("The shadow-distance fixture requires the Main Stage.");
            var asset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (asset == null) Assert.Ignore("The shadow-distance fixture requires the active HDRP asset.");
            Scene initialScene = SceneManager.GetActiveScene();
            bool restoreDefaultScene = initialScene.IsValid() && string.IsNullOrEmpty(initialScene.path);
            if (!initialScene.IsValid() || restoreDefaultScene && !OwnsDefaultTestScene(initialScene)) Assert.Ignore("The shadow-distance fixture requires a saved scene or an explicitly allowed unchanged NUnit default scene; arbitrary unsaved scenes are preserved.");
            NewSceneSetup restoreSceneSetup = initialScene.rootCount == 0 ? NewSceneSetup.EmptyScene : NewSceneSetup.DefaultGameObjects;
            RenderTexture initialTarget = RenderTexture.active;
            bool initialAsync = ShaderUtil.allowAsyncCompilation;
            bool initialDirty = initialScene.isDirty;
            var owned = new List<UnityEngine.Object>();
            Scene scene = default;
            string outputRoot = Environment.GetEnvironmentVariable("PUNCTUAL_SHADOW_TEST_OUTPUT");
            if (string.IsNullOrEmpty(outputRoot)) outputRoot = Path.Combine(ARCHIVE, "PunctualShadowDistanceValidation");
            string output = Path.Combine(Path.GetFullPath(outputRoot), DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff") + "_" + type);
            Assert.That(output.StartsWith(ARCHIVE + "\\", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(Directory.Exists(output), Is.False);
            Directory.CreateDirectory(output);
            TestContext.WriteLine("Punctual shadow-distance evidence: " + output);
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                if (restoreDefaultScene)
                {
                    Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), EMPTY_SCENE)), Is.True);
                    EditorSceneManager.OpenScene(EMPTY_SCENE, OpenSceneMode.Single);
                }
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Punctual shadow-distance visual fixture") { layer = 30 };
                SceneManager.MoveGameObjectToScene(root, scene);
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                owned.Add(profile);
                profile.Add<Fog>(true).enabled.Override(false);
                var environment = profile.Add<VisualEnvironment>(true);
                environment.skyType.Override(0);
                environment.skyAmbientMode.Override(SkyAmbientMode.Static);
                var exposure = profile.Add<Exposure>(true);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(0f);
                profile.Add<HDShadowSettings>(true).maxShadowDistance.Override(200f);
                var volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000f;
                volume.sharedProfile = profile;
                var material = new Material(Shader.Find("HDRP/Lit"));
                owned.Add(material);
                material.SetColor("_BaseColor", new Color(0.65f, 0.65f, 0.65f, 1f));
                material.SetFloat("_Smoothness", 0f);
                foreach (string pass in new[] { "Forward", "GBuffer", "DepthOnly", "ShadowCaster" })
                {
                    int index = material.FindPass(pass);
                    if (index >= 0) ShaderUtil.CompilePass(material, index, true);
                }
                Primitive(root, material, "Receiver floor", new Vector3(0f, -0.05f, 0f), new Vector3(14f, 0.1f, 14f), false);
                Primitive(root, material, "Receiver wall", new Vector3(0f, 2f, 3f), new Vector3(12f, 4f, 0.1f), false);
                MeshRenderer caster = Primitive(root, material, "Shadow caster", new Vector3(0f, 1f, 0f), new Vector3(1.2f, 2f, 1.2f), true);
                Camera camera = CreateCamera(root, scene);
                var target = new RenderTexture(SIZE, SIZE, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                owned.Add(target);
                target.Create();
                camera.targetTexture = target;
                var lightObject = new GameObject("Controlled " + type) { layer = root.layer };
                lightObject.transform.SetParent(root.transform);
                Light light = lightObject.AddComponent<Light>();
                light.type = type;
                light.transform.position = new Vector3(-3f, 5f, -2f);
                light.transform.LookAt(new Vector3(0f, 0f, 0f));
                light.color = Color.white;
                light.range = 18f;
                if (type == LightType.Spot)
                {
                    light.spotAngle = 100f;
                    light.innerSpotAngle = 70f;
                }
                light.shadows = LightShadows.Hard;
                light.cullingMask = 1 << root.layer;
                HDAdditionalLightData data = lightObject.AddComponent<HDAdditionalLightData>();
                data.SetIntensity(type == LightType.Directional ? 300f : 4000f, type == LightType.Directional ? LightUnit.Lux : LightUnit.Candela);
                data.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
                data.shadowFadeDistance = 10000f;
                data.shadowDimmer = 1f;
                data.volumetricShadowDimmer = 1f;
                data.fadeDistance = 10000f;
                data.lightDimmer = 1f;
                data.volumetricDimmer = 0f;
                data.SetShadowResolution(512);
                float originalIntensity = light.intensity;
                float originalRange = light.range;
                GraphicsFormat format = (GraphicsFormat)asset.currentPlatformRenderPipelineSettings.colorBufferFormat;
                var frames = new List<Frame>();
                Vector3 look = new Vector3(0f, 1f, 0f);
                Vector3 direction = new Vector3(0f, 6f, -9f).normalized;
                for (int pose = 0; pose < 2; pose++)
                {
                    string label = pose == 0 ? "near" : "far";
                    camera.transform.position = look + direction * (pose == 0 ? 12f : 100f);
                    camera.transform.LookAt(look);
                    float distance = Vector3.Distance(camera.transform.position, light.transform.position);
                    if (pose == 0) Assert.That(distance, Is.LessThan(67.5f));
                    else Assert.That(distance, Is.GreaterThan(75f));
                    foreach (string mode in new[] { "A", "B" })
                    {
                        using (var trial = PunctualShadowDistanceTrial.Attach(camera, mode, new[] { light }))
                        {
                            Assert.That(light.enabled, Is.True);
                            Assert.That(light.type, Is.EqualTo(type));
                            Assert.That(light.shadows, Is.EqualTo(LightShadows.Hard));
                            Assert.That(light.intensity, Is.EqualTo(originalIntensity));
                            Assert.That(light.range, Is.EqualTo(originalRange));
                            Assert.That(data.lightDimmer, Is.EqualTo(1f));
                            Assert.That(data.fadeDistance, Is.EqualTo(10000f));
                            yield return Capture(camera, target, frames, label + "_" + mode, light, data);
                            trial.RecordEvidence(output, label + "_" + mode, true);
                        }
                        Assert.That(data.shadowFadeDistance, Is.EqualTo(10000f));
                        Assert.That(data.shadowDimmer, Is.EqualTo(1f));
                        Assert.That(data.volumetricShadowDimmer, Is.EqualTo(1f));
                    }
                    caster.shadowCastingMode = ShadowCastingMode.Off;
                    yield return Capture(camera, target, frames, label + "_no-caster", light, data);
                    caster.shadowCastingMode = ShadowCastingMode.On;
                    light.enabled = false;
                    yield return Capture(camera, target, frames, label + "_light-off", light, data);
                    light.enabled = true;
                }
                foreach (Frame frame in frames) Save(output, frame);
                File.WriteAllText(Path.Combine(output, "preview-transform.json"), JsonUtility.ToJson(new DisplayTransform(), true));
                var comparisons = new List<Comparison>();
                for (int pose = 0; pose < 2; pose++)
                {
                    string label = pose == 0 ? "near" : "far";
                    Frame baseline = Find(frames, label + "_A");
                    Frame noCaster = Find(frames, label + "_no-caster");
                    Frame noLight = Find(frames, label + "_light-off");
                    Comparison shadowControl = Compare(baseline, noCaster, format);
                    shadowControl.name = label + "_positive-shadow";
                    Comparison lightControl = Compare(noCaster, noLight, format);
                    lightControl.name = label + "_positive-light";
                    comparisons.Add(shadowControl);
                    comparisons.Add(lightControl);
                    File.WriteAllText(Path.Combine(output, "comparisons.json"), JsonUtility.ToJson(new Comparisons { type = type.ToString(), comparisons = comparisons.ToArray() }, true));
                    Assert.That(shadowControl.visibleChangedPixels, Is.GreaterThan(100), label + " baseline must contain caster shadows.");
                    Assert.That(lightControl.visibleChangedPixels, Is.GreaterThan(100), label + " receiver must remain lit without caster shadows.");
                    foreach (string mode in new[] { "B" })
                    {
                        Frame candidate = Find(frames, label + "_" + mode);
                        bool keepShadow = pose == 0 || type == LightType.Directional;
                        Comparison comparison = Compare(keepShadow ? baseline : noCaster, candidate, format);
                        comparison.name = label + "_" + mode + (keepShadow ? "_vs-original" : "_vs-no-caster");
                        comparisons.Add(comparison);
                        File.WriteAllText(Path.Combine(output, "comparisons.json"), JsonUtility.ToJson(new Comparisons { type = type.ToString(), comparisons = comparisons.ToArray() }, true));
                        Assert.That(comparison.maximumResidual, Is.LessThan(0.02f), comparison.name);
                        Assert.That(comparison.maximumAlphaError, Is.EqualTo(0f), comparison.name);
                        Assert.That(comparison.rawChangedPixels, Is.LessThan(SIZE * SIZE / 200), comparison.name + " changed more than0.5% of raw pixels.");
                        if (!keepShadow)
                        {
                            Comparison removed = Compare(baseline, candidate, format);
                            removed.name = label + "_" + mode + "_vs-original-shadow-removal";
                            comparisons.Add(removed);
                            Assert.That(removed.visibleChangedPixels, Is.GreaterThan(100), label + " must show removal of distant caster shadows.");
                        }
                        Comparison remainsLit = Compare(candidate, noLight, format);
                        remainsLit.name = label + "_" + mode + "_positive-light";
                        comparisons.Add(remainsLit);
                        File.WriteAllText(Path.Combine(output, "comparisons.json"), JsonUtility.ToJson(new Comparisons { type = type.ToString(), comparisons = comparisons.ToArray() }, true));
                        Assert.That(remainsLit.visibleChangedPixels, Is.GreaterThan(100), comparison.name + " must preserve illumination.");
                    }
                }
                File.WriteAllText(Path.Combine(output, "comparisons.json"), JsonUtility.ToJson(new Comparisons { type = type.ToString(), comparisons = comparisons.ToArray() }, true));
            }
            finally
            {
                RenderTexture.active = initialTarget;
                ShaderUtil.allowAsyncCompilation = initialAsync;
                for (int index = owned.Count - 1; index >= 0; index--) if (owned[index] != null) UnityEngine.Object.DestroyImmediate(owned[index]);
                if (initialScene.IsValid() && initialScene.isLoaded) SceneManager.SetActiveScene(initialScene);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (restoreDefaultScene) EditorSceneManager.NewScene(restoreSceneSetup, NewSceneMode.Single);
                else Assert.That(initialScene.isDirty, Is.EqualTo(initialDirty));
            }
        }

        private static bool OwnsDefaultTestScene(Scene scene)
        {
            if (Environment.GetEnvironmentVariable("RVM_NATIVE_ALLOW_DEFAULT_SCENE") != "1" || SceneManager.sceneCount != 1 || !string.IsNullOrEmpty(scene.path) || scene.isDirty || scene.name != "Untitled" && !string.IsNullOrEmpty(scene.name)) return false;
            string name = TestContext.CurrentContext.Test.FullName;
            string prefix = typeof(PunctualShadowDistanceVisualTests).FullName + ".";
            if (name != prefix + nameof(PointShadowDistancePreservesIlluminationNearAndFar) && name != prefix + nameof(SpotShadowDistancePreservesIlluminationNearAndFar) && name != prefix + nameof(DirectionalShadowsRemainUnchangedNearAndFar)) return false;
            GameObject[] roots = scene.GetRootGameObjects();
            if (roots.Length == 0) return true;
            if (roots.Length != 2) return false;
            bool camera = false;
            bool light = false;
            foreach (GameObject root in roots)
            {
                if (root.transform.childCount != 0) return false;
                bool cameraRoot = root.name == "Main Camera" && root.GetComponent<Camera>() != null;
                Light source = root.GetComponent<Light>();
                bool lightRoot = root.name == "Directional Light" && source != null && source.type == LightType.Directional;
                if (!cameraRoot && !lightRoot) return false;
                foreach (Component component in root.GetComponents<Component>())
                {
                    if (component == null) return false;
                    bool allowed = cameraRoot ? component is Transform || component is Camera || component is AudioListener || component is HDAdditionalCameraData : component is Transform || component is Light || component is HDAdditionalLightData;
                    if (!allowed) return false;
                }
                camera |= cameraRoot;
                light |= lightRoot;
            }
            return camera && light;
        }

        private static Camera CreateCamera(GameObject root, Scene scene)
        {
            var cameraObject = new GameObject("Punctual shadow-distance camera") { layer = root.layer };
            cameraObject.transform.SetParent(root.transform);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.aspect = 1f;
            camera.cullingMask = 1 << root.layer;
            camera.useOcclusionCulling = false;
            var data = cameraObject.AddComponent<HDAdditionalCameraData>();
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = Color.black;
            data.volumeLayerMask = 1 << root.layer;
            data.customRenderingSettings = true;
            foreach (var field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.Volumetrics, FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl, FrameSettingsField.ReflectionProbe, FrameSettingsField.PlanarProbe, FrameSettingsField.ContactShadows })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.ShadowMaps, true);
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.ShadowMaps] = true;
            return camera;
        }

        private static MeshRenderer Primitive(GameObject root, Material material, string name, Vector3 position, Vector3 scale, bool shadows)
        {
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = name;
            target.layer = root.layer;
            target.transform.SetParent(root.transform);
            target.transform.position = position;
            target.transform.localScale = scale;
            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return renderer;
        }

        private static IEnumerator Capture(Camera camera, RenderTexture target, List<Frame> frames, string name, Light light, HDAdditionalLightData data)
        {
            camera.Render();
            yield return null;
            camera.Render();
            RenderTexture initial = RenderTexture.active;
            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0f, 0f, SIZE, SIZE), 0, 0);
                texture.Apply();
                Color[] colors = texture.GetPixels();
                foreach (Color color in colors) for (int channel = 0; channel < 4; channel++) Assert.That(float.IsFinite(color[channel]), Is.True, name + " non-finite channel.");
                frames.Add(new Frame { Name = name, Colors = colors, State = new CaptureState { name = name, cameraPosition = camera.transform.position, lightPosition = light.transform.position, cameraLightDistance = Vector3.Distance(camera.transform.position, light.transform.position), lightType = light.type.ToString(), lightEnabled = light.enabled, lightShadows = light.shadows.ToString(), intensity = light.intensity, range = light.range, shadowFadeDistance = data.shadowFadeDistance, shadowDimmer = data.shadowDimmer, volumetricShadowDimmer = data.volumetricShadowDimmer, lightFadeDistance = data.fadeDistance, lightDimmer = data.lightDimmer } });
            }
            finally
            {
                RenderTexture.active = initial;
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Frame Find(List<Frame> frames, string name)
        {
            foreach (Frame frame in frames) if (frame.Name == name) return frame;
            throw new InvalidOperationException("Missing capture: " + name);
        }

        private static Comparison Compare(Frame reference, Frame candidate, GraphicsFormat format)
        {
            var result = new Comparison { reference = reference.Name, candidate = candidate.Name, precisionFormat = format.ToString() };
            for (int index = 0; index < reference.Colors.Length; index++)
            {
                Color a = reference.Colors[index];
                Color b = candidate.Colors[index];
                float raw = 0f;
                float residual = 0f;
                for (int channel = 0; channel < 3; channel++)
                {
                    float error = Mathf.Abs(a[channel] - b[channel]);
                    raw = Mathf.Max(raw, error);
                    bool adjacent = false;
                    if (format == GraphicsFormat.R16G16B16A16_SFloat)
                    {
                        ushort first = Mathf.FloatToHalf(a[channel]);
                        ushort second = Mathf.FloatToHalf(b[channel]);
                        int firstOrdered = (first & 0x8000) != 0 ? 0x8000 - (first & 0x7fff) : 0x8000 + first;
                        int secondOrdered = (second & 0x8000) != 0 ? 0x8000 - (second & 0x7fff) : 0x8000 + second;
                        adjacent = Mathf.HalfToFloat(first) == a[channel] && Mathf.HalfToFloat(second) == b[channel] && Math.Abs(firstOrdered - secondOrdered) <= 1;
                    }
                    residual = Mathf.Max(residual, adjacent ? 0f : error);
                }
                float alpha = Mathf.Abs(a.a - b.a);
                result.maximumError = Mathf.Max(result.maximumError, raw);
                result.maximumResidual = Mathf.Max(result.maximumResidual, residual);
                result.maximumAlphaError = Mathf.Max(result.maximumAlphaError, alpha);
                if (raw > 0f || alpha > 0f) result.rawChangedPixels++;
                if (raw > 0.01f) result.visibleChangedPixels++;
            }
            return result;
        }

        private static void Save(string output, Frame frame)
        {
            var range = new Range { minimum = new Color(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue), maximum = new Color(float.MinValue, float.MinValue, float.MinValue, float.MinValue) };
            var display = new Color[frame.Colors.Length];
            for (int index = 0; index < frame.Colors.Length; index++)
            {
                Color value = frame.Colors[index];
                for (int channel = 0; channel < 4; channel++)
                {
                    Assert.That(float.IsFinite(value[channel]), Is.True);
                    range.minimum[channel] = Mathf.Min(range.minimum[channel], value[channel]);
                    range.maximum[channel] = Mathf.Max(range.maximum[channel], value[channel]);
                }
                display[index] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(value.r / DISPLAY_WHITE)), Mathf.LinearToGammaSpace(Mathf.Clamp01(value.g / DISPLAY_WHITE)), Mathf.LinearToGammaSpace(Mathf.Clamp01(value.b / DISPLAY_WHITE)), 1f);
            }
            var raw = new Texture2D(SIZE, SIZE, TextureFormat.RGBAFloat, false, true);
            var preview = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true);
            try
            {
                raw.SetPixels(frame.Colors);
                raw.Apply();
                preview.SetPixels(display);
                preview.Apply();
                string path = Path.Combine(output, frame.Name);
                File.WriteAllBytes(path + ".exr", raw.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                File.WriteAllBytes(path + ".png", preview.EncodeToPNG());
                File.WriteAllText(path + "-range.json", JsonUtility.ToJson(range, true));
                File.WriteAllText(path + "-state.json", JsonUtility.ToJson(frame.State, true));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(raw);
                UnityEngine.Object.DestroyImmediate(preview);
            }
        }

        private sealed class Frame
        {
            public string Name { get; set; }
            public Color[] Colors { get; set; }
            public CaptureState State { get; set; }
        }

        [Serializable]
        private sealed class CaptureState
        {
            public string name;
            public Vector3 cameraPosition;
            public Vector3 lightPosition;
            public float cameraLightDistance;
            public string lightType;
            public bool lightEnabled;
            public string lightShadows;
            public float intensity;
            public float range;
            public float shadowFadeDistance;
            public float shadowDimmer;
            public float volumetricShadowDimmer;
            public float lightFadeDistance;
            public float lightDimmer;
        }

        [Serializable]
        private sealed class Comparison
        {
            public string name;
            public string reference;
            public string candidate;
            public string precisionFormat;
            public int rawChangedPixels;
            public int visibleChangedPixels;
            public float maximumError;
            public float maximumResidual;
            public float maximumAlphaError;
        }

        [Serializable]
        private sealed class Comparisons
        {
            public string type;
            public Comparison[] comparisons;
        }

        [Serializable]
        private sealed class Range
        {
            public int width = SIZE;
            public int height = SIZE;
            public Color minimum;
            public Color maximum;
        }

        [Serializable]
        private sealed class DisplayTransform
        {
            public float white = DISPLAY_WHITE;
            public string mapping = "sRGB(clamp(raw RGB /256,0,1)); one predeclared fixed mapping for every A/B/no-caster/light-off, near/far and Point/Spot/Directional frame.";
            public string raw = "RGBA float32 OpenEXR, finite verified before saving; no per-frame normalization.";
        }
    }
}
