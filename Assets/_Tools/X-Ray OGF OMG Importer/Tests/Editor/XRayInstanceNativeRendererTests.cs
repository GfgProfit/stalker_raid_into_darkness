using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayInstanceNativeRendererTests
    {
        private const int SIZE = 256;
        private const string EMPTY_SCENE = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/NativeSubmissionEmpty.unity";

        [Test]
        public void InvalidInputsAndRepeatedDisposalKeepNativeSubmissionInactive()
        {
            var backend = new XRayInstanceNativeRenderer();
            try
            {
                Assert.That(backend.Prepare(null, null, 0, true, null), Is.False);
                Assert.That(backend.Active, Is.False);
                Assert.That(backend.MemoryBytes, Is.EqualTo(0));
                Assert.DoesNotThrow(backend.Disable);
                Assert.DoesNotThrow(backend.Disable);
                Assert.DoesNotThrow(backend.Dispose);
                Assert.DoesNotThrow(backend.Dispose);
                Assert.That(backend.Active, Is.False);
                Assert.That(backend.MemoryBytes, Is.EqualTo(0));
            }
            finally
            {
                backend.Dispose();
            }
        }

        [Test]
        public void PreparedStaticOwnershipSupportsDisableAndExposedMatricesFallBack()
        {
            RequireGraphics();
            var owned = new List<UnityEngine.Object>();
            var root = new GameObject("Native submission ownership fixture");
            var backend = new XRayInstanceNativeRenderer();
            try
            {
                Camera camera = root.AddComponent<Camera>();
                camera.enabled = false;
                Material material = MonolithMaterial(owned, false);
                Mesh mesh = Quad(owned);
                Matrix4x4[] source = { Matrix4x4.Translate(new Vector3(0f, 0f, 4f)) };
                var batch = new XRayInstanceBatch("static", mesh, new[] { material }, new Bounds(new Vector3(0f, 0f, 4f), Vector3.one * 2f), source, new[] { new Vector4(0.2f, 0.4f, 0.7f, 0f) }, true);
                var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
                owned.Add(set);
                set.Initialize("native ownership", 16f, new List<XRayInstanceBatch> { batch });
                Assert.That(backend.Prepare(set, camera, 0, true, null), Is.False);
                set.PrepareMatrices("native ownership");
                source[0] = Matrix4x4.Translate(Vector3.one * 100f);
                Assert.That(backend.Prepare(set, camera, 0, true, null), Is.True);
                Assert.That(backend.Active, Is.True);
                Assert.That(batch.GetMatrix(0), Is.EqualTo(Matrix4x4.Translate(new Vector3(0f, 0f, 4f))));
                long bytes = backend.MemoryBytes;
                Assert.That(bytes, Is.GreaterThan(0));
                backend.Disable();
                Assert.That(backend.Active, Is.False);
                Assert.That(backend.MemoryBytes, Is.EqualTo(bytes));
                Assert.That(backend.Prepare(set, camera, 0, true, null), Is.True);
                Assert.That(backend.MemoryBytes, Is.EqualTo(bytes));
                Matrix4x4[] exposed = batch.Matrices;
                exposed[0] = Matrix4x4.Translate(new Vector3(9f, 0f, 4f));
                Assert.That(backend.Prepare(set, camera, 0, true, null), Is.False);
                Assert.That(backend.Active, Is.False);
                set.PrepareMatrices("native ownership revision");
                Assert.That(backend.Prepare(set, camera, 0, true, null), Is.True);
                Assert.That(batch.GetMatrix(0), Is.EqualTo(Matrix4x4.Translate(new Vector3(9f, 0f, 4f))));
                backend.Dispose();
                Assert.That(backend.Active, Is.False);
                Assert.That(backend.MemoryBytes, Is.EqualTo(0));
            }
            finally
            {
                backend.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                DestroyOwned(owned);
            }
        }

        [Test]
        public void ChangingToAnUnsupportedMaterialDisablesThePreparedPlan()
        {
            RequireGraphics();
            var owned = new List<UnityEngine.Object>();
            var root = new GameObject("Native material invalidation fixture");
            var backend = new XRayInstanceNativeRenderer();
            try
            {
                Camera camera = root.AddComponent<Camera>();
                camera.enabled = false;
                Mesh mesh = Quad(owned);
                Material material = MonolithMaterial(owned, false);
                var batch = new XRayInstanceBatch("material", mesh, new[] { material }, new Bounds(Vector3.zero, Vector3.one * 2f), new[] { Matrix4x4.identity }, new[] { Vector4.one }, true);
                XRayInstanceBatchSet set = Set(owned, "native material", new List<XRayInstanceBatch> { batch });
                Assert.That(backend.Prepare(set, camera, 0, true, null), Is.True);
                var unsupported = new Material(Shader.Find("HDRP/Lit")) { enableInstancing = true };
                owned.Add(unsupported);
                batch.Materials[0] = unsupported;
                Assert.That(backend.Prepare(set, camera, 0, true, null), Is.False);
                Assert.That(backend.Active, Is.False);
                batch.Materials[0] = material;
                Assert.That(backend.Prepare(set, camera, 0, true, null), Is.True);
            }
            finally
            {
                backend.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                DestroyOwned(owned);
            }
        }

        [Test]
        public void PreparedMirroredMatricesUseLegacyFallback()
        {
            RequireGraphics();
            var owned = new List<UnityEngine.Object>();
            var root = new GameObject("Native mirrored fallback fixture");
            var backend = new XRayInstanceNativeRenderer();
            try
            {
                Camera camera = root.AddComponent<Camera>();
                camera.enabled = false;
                Mesh mesh = Quad(owned);
                Material material = MonolithMaterial(owned, false);
                var batch = new XRayInstanceBatch("mirrored", mesh, new[] { material }, new Bounds(Vector3.zero, Vector3.one * 2f), new[] { Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) }, new[] { Vector4.one }, true);
                XRayInstanceBatchSet set = Set(owned, "native mirrored", new List<XRayInstanceBatch> { batch });
                Assert.That(backend.Prepare(set, camera, 0, true, null), Is.False);
                Assert.That(backend.Active, Is.False);
                Assert.That(backend.FallbackReason, Does.Contain("mirrored"));
            }
            finally
            {
                backend.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                DestroyOwned(owned);
            }
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator NativeSubmissionPreservesOffscreenShadowsAndPayloadAcrossCameras()
        {
            return RenderScenario(false);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator NativeSubmissionPreservesOffscreenPointShadowsAcrossCameras()
        {
            return RenderScenario(false, LightType.Point);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator NativeSubmissionPreservesOffscreenSpotShadowsAcrossCameras()
        {
            return RenderScenario(false, LightType.Spot);
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator NativeSubmissionPreservesLitWindAcrossCameras()
        {
            return RenderScenario(true);
        }

        private static IEnumerator RenderScenario(bool wind, LightType lightType = LightType.Directional)
        {
            RequireGraphics();
            HdrpTestWarmup.Ensure();
            if (wind && XRayFloraWindProvider.Active != null) Assert.Ignore("The native wind fixture requires exclusive ownership of the wind provider.");
            Scene initialScene = SceneManager.GetActiveScene();
            bool restoreDefaultScene = initialScene.IsValid() && string.IsNullOrEmpty(initialScene.path);
            if (!initialScene.IsValid() || restoreDefaultScene && !OwnsDefaultTestScene(initialScene))
            {
                string sceneState = initialScene.IsValid() ? "name=" + initialScene.name + ";path=" + initialScene.path + ";dirty=" + initialScene.isDirty + ";roots=" + initialScene.rootCount : "invalid";
                TestContext.WriteLine("Native fixture scene guard: " + sceneState + ";sceneCount=" + SceneManager.sceneCount + ";allowDefault=" + Environment.GetEnvironmentVariable("RVM_NATIVE_ALLOW_DEFAULT_SCENE") + ";test=" + TestContext.CurrentContext.Test.FullName);
                Assert.Ignore("A saved active scene or an explicitly allowed unchanged NUnit bootstrap scene is required for the additive native rendering fixture.");
            }
            NewSceneSetup restoreSceneSetup = initialScene.rootCount == 0 ? NewSceneSetup.EmptyScene : NewSceneSetup.DefaultGameObjects;
            var owned = new List<UnityEngine.Object>();
            RenderTexture initial = RenderTexture.active;
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            var windGlobals = wind ? new WindGlobals() : null;
            Scene scene = default;
            IEnumerator capture = null;
            ShadowViewObserver shadowViews = null;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                yield return null;
                if (restoreDefaultScene)
                {
                    Assert.That(File.Exists(EMPTY_SCENE), Is.True, "The required empty native rendering fixture is missing.");
                    EditorSceneManager.OpenScene(EMPTY_SCENE, OpenSceneMode.Single);
                }
                string output = Environment.GetEnvironmentVariable("RVM_NATIVE_VALIDATION_OUTPUT");
                if (string.IsNullOrEmpty(output)) output = @"E:\Projects\stalker_raid_into_darkness_Archive\InstanceNativeValidation";
                output = Path.Combine(Path.GetFullPath(output), DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff"));
                Assert.That(output, Does.StartWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\"));
                Directory.CreateDirectory(output);
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Native submission visual fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                XRayFloraWindProvider provider = null;
                if (wind)
                {
                    provider = root.AddComponent<XRayFloraWindProvider>();
                    provider.Profile = XRayFloraWindProfile.AnomalySsfx;
                    provider.SetWeather(90f, 1000f, 1f);
                    provider.ResetAnimation();
                    for (int tick = 0; tick < 32; tick++) provider.Tick(0.1f);
                    provider.Paused = true;
                    Assert.That(provider.State.Anim, Is.Not.EqualTo(provider.State.AnimPrev));
                    Assert.That(provider.State.Initialized, Is.True);
                }
                Camera camera = CreateCamera(root, scene);
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                owned.Add(profile);
                profile.Add<Fog>(true).enabled.Override(false);
                var exposure = profile.Add<Exposure>(true);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(0f);
                HDShadowSettings shadowSettings = profile.Add<HDShadowSettings>(true);
                shadowSettings.maxShadowDistance.Override(40f);
                shadowSettings.cascadeShadowSplitCount.Override(4);
                shadowSettings.cascadeShadowSplit0.Override(0.25f);
                shadowSettings.cascadeShadowSplit1.Override(0.5f);
                shadowSettings.cascadeShadowSplit2.Override(0.75f);
                var volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000;
                volume.sharedProfile = profile;
                var light = new GameObject("Native fixture sun").AddComponent<Light>();
                light.transform.SetParent(root.transform);
                light.type = lightType;
                light.shadows = LightShadows.Hard;
                light.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -1f, 0f));
                HDAdditionalLightData lightData = light.gameObject.AddComponent<HDAdditionalLightData>();
                lightData.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
                if (lightType == LightType.Directional) lightData.SetIntensity(3000f, LightUnit.Lux);
                else
                {
                    light.transform.position = new Vector3(8f, 8f, 0f);
                    light.range = 30f;
                    light.spotAngle = 90f;
                    lightData.SetIntensity(500000f, LightUnit.Lumen);
                }
                var receiverMaterial = new Material(Shader.Find("HDRP/Lit"));
                owned.Add(receiverMaterial);
                receiverMaterial.SetColor("_BaseColor", Color.gray);
                var receiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(receiver, scene);
                receiver.transform.SetParent(root.transform);
                receiver.transform.position = new Vector3(0f, -0.05f, 0f);
                receiver.transform.localScale = new Vector3(12f, 0.1f, 12f);
                receiver.GetComponent<MeshRenderer>().sharedMaterial = receiverMaterial;
                var prototype = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Mesh cube = prototype.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(prototype);
                Material casterMaterial = MonolithMaterial(owned, false);
                var casters = new List<XRayInstanceBatch>();
                foreach (float x in new[] { 4f, 4.5f })
                {
                    Vector3 position = new Vector3(x, 3f, 0f);
                    casters.Add(new XRayInstanceBatch("offscreen caster", cube, new[] { casterMaterial }, new Bounds(position, Vector3.one), new[] { Matrix4x4.Translate(position) }, new[] { new Vector4(1f, 0f, 0f, 0f) }, true));
                }
                var casterRenderer = root.AddComponent<XRayLevelInstancedRenderer>();
                casterRenderer.BatchSet = Set(owned, "native casters", casters);
                shadowViews = new ShadowViewObserver(casterRenderer);
                var payloadRoot = new GameObject("Native payload fixture");
                payloadRoot.transform.SetParent(root.transform);
                var payloadRenderer = payloadRoot.AddComponent<XRayLevelInstancedRenderer>();
                Material payloadMaterial = MonolithMaterial(owned, !wind);
                if (wind)
                {
                    payloadMaterial.SetFloat("_MonolithFloraWind", 1f);
                    payloadMaterial.SetFloat("_MonolithFloraWindPart", 1f);
                    payloadMaterial.SetShaderPassEnabled(XRayFloraWindBindings.MOTION_VECTORS_PASS, true);
                    payloadMaterial.SetColor("_BaseColor", new Color(0.3f, 0.6f, 0.2f, 1f));
                }
                CompileNativePasses(casterMaterial, owned);
                if (wind) CompileNativePasses(payloadMaterial, owned);
                Mesh quad = wind ? WindGrid(owned) : Quad(owned);
                var payloads = new List<XRayInstanceBatch>();
                Vector4[] values = { new Vector4(0.2f, 0.4f, 0.7f, 0f), new Vector4(0.8f, 0.25f, 0.1f, 0f) };
                for (int index = 0; index < values.Length; index++)
                {
                    Vector3 position = new Vector3(index == 0 ? -1.2f : 1.2f, wind ? 0.1f : 0.75f, -1f);
                    Matrix4x4 matrix = index == 0 ? Matrix4x4.Translate(position) : Matrix4x4.TRS(position, Quaternion.Euler(0f, 25f, 0f), new Vector3(1.3f, 0.8f, 0.9f));
                    Bounds bounds = GeometryUtility.CalculateBounds(quad.vertices, matrix);
                    payloads.Add(new XRayInstanceBatch("payload", quad, new[] { payloadMaterial }, bounds, new[] { matrix }, new[] { wind ? new Vector4(1f, 0f, 0f, 0f) : values[index] }, wind));
                }
                payloadRenderer.BatchSet = Set(owned, "native payloads", payloads);
                var target = new RenderTexture(SIZE, SIZE, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                owned.Add(target);
                target.Create();
                camera.targetTexture = target;
                var pixels = new Texture2D(SIZE, SIZE, TextureFormat.RGBAFloat, false, true);
                owned.Add(pixels);
                float previewMinimum = 0f;
                float previewScale = 0f;
                Vector4 windCurrent = provider != null ? provider.State.Anim : Vector4.zero;
                Vector4 windPrevious = provider != null ? provider.State.AnimPrev : Vector4.zero;
                for (int view = 0; view < 2; view++)
                {
                    yield return null;
                    camera.transform.position = new Vector3(view * 0.25f, 8f, -8f);
                    camera.transform.LookAt(new Vector3(0f, 0f, view * 0.5f));
                    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                    foreach (XRayInstanceBatch batch in casters) Assert.That(GeometryUtility.TestPlanesAABB(planes, batch.Bounds), Is.False);
                    casterRenderer.NativeSubmissionOverride = false;
                    payloadRenderer.NativeSubmissionOverride = false;
                    Color[] beforeMotion = null;
                    Color[] before = null;
                    capture = Capture(camera, target, pixels, Path.Combine(output, "view" + view + "_A"), value => before = value, wind ? value => beforeMotion = value : null);
                    while (capture.MoveNext()) yield return capture.Current;
                    Assert.That(casterRenderer.NativeCameraCommands, Is.EqualTo(0));
                    Assert.That(casterRenderer.NativeShadowCommands, Is.EqualTo(0));
                    Assert.That(payloadRenderer.NativeCameraCommands, Is.EqualTo(0));
                    Assert.That(payloadRenderer.NativeShadowCommands, Is.EqualTo(0));
                    int baselineFirstPayloadPixels = CountPayload(before, values[0]);
                    int baselineSecondPayloadPixels = CountPayload(before, values[1]);
                    if (view == 0)
                    {
                        float maximum = float.MinValue;
                        previewMinimum = float.MaxValue;
                        foreach (Color color in before)
                        {
                            previewMinimum = Mathf.Min(previewMinimum, Luminance(color));
                            maximum = Mathf.Max(maximum, Luminance(color));
                        }
                        Assert.That(maximum, Is.GreaterThan(previewMinimum));
                        previewScale = 0.8f / (maximum - previewMinimum);
                        File.WriteAllText(Path.Combine(output, "preview-transform.json"), "{\"reference\":\"view0_A\",\"minimumLuminance\":" + Number(previewMinimum) + ",\"linearScale\":" + Number(previewScale) + ",\"mapping\":\"LinearToGammaSpace(Clamp01((Rec709Luminance-minimumLuminance)*linearScale+0.1)); one fixed transform for all A/B/no-caster previews\",\"rawFormat\":\"RGBA float32 OpenEXR\"}");
                    }
                    SaveEvidence(Path.Combine(output, "view" + view + "_A"), pixels, before, previewMinimum, previewScale);
                    yield return null;
                    casterRenderer.NativeSubmissionOverride = true;
                    payloadRenderer.NativeSubmissionOverride = true;
                    bool compilingBeforeCapture = ShaderUtil.anythingCompiling;
                    Color[] afterMotion = null;
                    shadowViews.Reset();
                    Color[] after = null;
                    capture = Capture(camera, target, pixels, Path.Combine(output, "view" + view + "_B"), value => after = value, wind ? value => afterMotion = value : null);
                    while (capture.MoveNext()) yield return capture.Current;
                    Assert.That(shadowViews.MaximumLightSplits, Is.EqualTo(lightType == LightType.Directional ? 4 : lightType == LightType.Point ? 6 : 1), "The native fixture must execute the actual HDRP light splits.");
                    bool compilingAfterCapture = ShaderUtil.anythingCompiling;
                    if (wind)
                    {
                        Assert.That(provider.State.Anim, Is.EqualTo(windCurrent));
                        Assert.That(provider.State.AnimPrev, Is.EqualTo(windPrevious));
                        Assert.That(windCurrent, Is.Not.EqualTo(windPrevious));
                    }
                    NativeMetrics casterMetrics = Metrics(casterRenderer);
                    NativeMetrics payloadMetrics = Metrics(payloadRenderer);
                    File.WriteAllText(Path.Combine(output, "view" + view + "-native.json"), "{\"baselineFirstPayloadPixels\":" + baselineFirstPayloadPixels + ",\"baselineSecondPayloadPixels\":" + baselineSecondPayloadPixels + ",\"observedLightSplits\":" + shadowViews.MaximumLightSplits + ",\"compilingBeforeCapture\":" + compilingBeforeCapture.ToString().ToLowerInvariant() + ",\"compilingAfterCapture\":" + compilingAfterCapture.ToString().ToLowerInvariant() + ",\"commandBufferAsyncCompilation\":false,\"caster\":" + JsonUtility.ToJson(casterMetrics) + ",\"payload\":" + JsonUtility.ToJson(payloadMetrics) + "}");
                    SaveEvidence(Path.Combine(output, "view" + view + "_B"), pixels, after, previewMinimum, previewScale);
                    if (wind)
                    {
                        yield return null;
                        casterRenderer.NativeSubmissionOverride = false;
                        payloadRenderer.NativeSubmissionOverride = false;
                        Color[] inactiveGroupLegacy = null;
                        capture = Capture(camera, target, pixels, Path.Combine(output, "view" + view + "_inactive-group-legacy"), value => inactiveGroupLegacy = value);
                        while (capture.MoveNext()) yield return capture.Current;
                        SaveEvidence(Path.Combine(output, "view" + view + "_inactive-group-legacy"), pixels, inactiveGroupLegacy, previewMinimum, previewScale);
                        foreach (XRayLevelInstancedRenderer renderer in new[] { casterRenderer, payloadRenderer })
                        {
                            var backend = typeof(XRayLevelInstancedRenderer).GetField("_nativeRenderer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(renderer) as XRayInstanceNativeRenderer;
                            Assert.That(backend, Is.Not.Null);
                            Assert.That(backend.Active, Is.False);
                            Assert.That(typeof(XRayInstanceNativeRenderer).GetField("_group", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(backend), Is.Not.Null);
                        }
                        casterRenderer.NativeSubmissionOverride = true;
                        payloadRenderer.NativeSubmissionOverride = true;
                        Assert.That(provider.State.Anim, Is.EqualTo(windCurrent));
                        Assert.That(provider.State.AnimPrev, Is.EqualTo(windPrevious));
                        int inactiveGroupChanged = 0;
                        float inactiveGroupMaximumError = 0f;
                        for (int index = 0; index < before.Length; index++)
                        {
                            float error = Error(before[index], inactiveGroupLegacy[index]);
                            inactiveGroupMaximumError = Mathf.Max(inactiveGroupMaximumError, error);
                            if (error > 0.001f) inactiveGroupChanged++;
                        }
                        File.WriteAllText(Path.Combine(output, "view" + view + "-inactive-group-control.json"), "{\"changedPixels\":" + inactiveGroupChanged + ",\"maximumError\":" + Number(inactiveGroupMaximumError) + ",\"pixels\":" + before.Length + ",\"allocatedGroups\":2,\"activeGroups\":0}");
                        Assert.That(inactiveGroupChanged, Is.LessThan(before.Length * 0.005f));
                        Assert.That(inactiveGroupMaximumError, Is.LessThan(0.02f));
                    }
                    if (wind)
                    {
                        SaveEvidence(Path.Combine(output, "view" + view + "_motion-A"), pixels, beforeMotion, -0.00025f, 2000f);
                        SaveEvidence(Path.Combine(output, "view" + view + "_motion-B"), pixels, afterMotion, -0.00025f, 2000f);
                    }
                    yield return null;
                    casterRenderer.enabled = false;
                    Color[] clear = null;
                    capture = Capture(camera, target, pixels, Path.Combine(output, "view" + view + "_no-caster"), value => clear = value);
                    while (capture.MoveNext()) yield return capture.Current;
                    SaveEvidence(Path.Combine(output, "view" + view + "_no-caster"), pixels, clear, previewMinimum, previewScale);
                    yield return null;
                    casterRenderer.enabled = true;
                    int windPixels = 0;
                    if (wind)
                    {
                        provider.Reinitialize();
                        Color[] settledMotion = null;
                        Color[] settled = null;
                        capture = Capture(camera, target, pixels, Path.Combine(output, "view" + view + "_same-geometry-settled-wind"), value => settled = value, value => settledMotion = value);
                        while (capture.MoveNext()) yield return capture.Current;
                        SaveEvidence(Path.Combine(output, "view" + view + "_same-geometry-settled-wind"), pixels, settled, previewMinimum, previewScale);
                        SaveEvidence(Path.Combine(output, "view" + view + "_motion-same-geometry-settled-wind"), pixels, settledMotion, -0.00025f, 2000f);
                        yield return null;
                        payloadMaterial.SetFloat("_MonolithDebug", 37f);
                        Color[] windMask = null;
                        capture = Capture(camera, target, pixels, Path.Combine(output, "view" + view + "_wind-mask"), value => windMask = value);
                        while (capture.MoveNext()) yield return capture.Current;
                        SaveEvidence(Path.Combine(output, "view" + view + "_wind-mask"), pixels, windMask, 0f, 0.8f);
                        yield return null;
                        payloadMaterial.SetFloat("_MonolithDebug", 0f);
                        provider.State.Reset(provider.State.Weather);
                        provider.Paused = false;
                        for (int tick = 0; tick < 32; tick++) provider.Tick(0.1f);
                        provider.Paused = true;
                        Assert.That(provider.State.Anim, Is.EqualTo(windCurrent));
                        Assert.That(provider.State.AnimPrev, Is.EqualTo(windPrevious));
                        payloadMaterial.SetFloat("_MonolithFloraWind", 0f);
                        Color[] still = null;
                        capture = Capture(camera, target, pixels, Path.Combine(output, "view" + view + "_no-wind"), value => still = value);
                        while (capture.MoveNext()) yield return capture.Current;
                        SaveEvidence(Path.Combine(output, "view" + view + "_no-wind"), pixels, still, previewMinimum, previewScale);
                        yield return null;
                        payloadMaterial.SetFloat("_MonolithFloraWind", 1f);
                        for (int index = 0; index < after.Length; index++) if (Error(after[index], still[index]) > 0.01f) windPixels++;
                        int movingPixels = 0;
                        int motionControlPixels = 0;
                        int windMaskPixels = 0;
                        float maximumMotionError = 0f;
                        float maximumSettledColorError = 0f;
                        float maximumOutsidePhaseMotion = 0f;
                        for (int index = 0; index < afterMotion.Length; index++)
                        {
                            if (Motion(afterMotion[index]) > 0.000001f) movingPixels++;
                            float phaseMotion = Motion(afterMotion[index] - settledMotion[index]);
                            if (Error(windMask[index], Color.red) < 0.02f)
                            {
                                windMaskPixels++;
                                if (phaseMotion > 0.000001f) motionControlPixels++;
                            }
                            else maximumOutsidePhaseMotion = Mathf.Max(maximumOutsidePhaseMotion, phaseMotion);
                            maximumMotionError = Mathf.Max(maximumMotionError, Motion(beforeMotion[index] - afterMotion[index]));
                            maximumSettledColorError = Mathf.Max(maximumSettledColorError, Error(after[index], settled[index]));
                        }
                        File.WriteAllText(Path.Combine(output, "view" + view + "-motion.json"), "{\"movingPixelsIncludingCamera\":" + movingPixels + ",\"windMaskPixels\":" + windMaskPixels + ",\"windPhaseControlPixels\":" + motionControlPixels + ",\"maximumLegacyNativeError\":" + Number(maximumMotionError) + ",\"maximumSameGeometryColorError\":" + Number(maximumSettledColorError) + ",\"maximumOutsideWindPhaseMotion\":" + Number(maximumOutsidePhaseMotion) + ",\"control\":\"Identical current wind geometry with previous wind phase equal to current, subtracting common startup camera motion; differences must remain inside the current geometry mask\",\"comparisonChannels\":\"RG normalized screen displacement\",\"previewMinimumLuminance\":-0.00025,\"previewLinearScale\":2000,\"previewTransform\":\"One fixed transform for all motion A/B/settled frames in both views\"}");
                        Assert.That(windMaskPixels, Is.GreaterThan(100), "The wind motion control must cover the visible current geometry.");
                        Assert.That(motionControlPixels, Is.GreaterThan(100), "The native wind previous phase must contribute actual motion inside the current geometry.");
                        Assert.That(maximumSettledColorError, Is.LessThan(0.000001f), "Settling the previous wind phase must preserve the current geometry and color.");
                        Assert.That(maximumOutsidePhaseMotion, Is.LessThan(0.000001f), "Changing only the previous wind phase must preserve camera and background motion outside the wind geometry.");
                        Assert.That(maximumMotionError, Is.LessThan(0.01f / SIZE), "Native and legacy wind motion differ by more than 1% of a pixel.");
                        File.WriteAllText(Path.Combine(output, "view" + view + "-wind.json"), "{\"changedPixels\":" + windPixels + ",\"current\":" + JsonUtility.ToJson(provider.State.Anim) + ",\"previous\":" + JsonUtility.ToJson(provider.State.AnimPrev) + ",\"profile\":\"AnomalySsfx\",\"part\":\"trunk\",\"debug\":0}");
                    }
                    int changed = 0;
                    int shadowPixels = 0;
                    int previewShadowPixels = 0;
                    int noCasterChangedPixels = 0;
                    float maximumError = 0f;
                    float noCasterMaximumError = 0f;
                    for (int index = 0; index < before.Length; index++)
                    {
                        float error = Error(before[index], after[index]);
                        maximumError = Mathf.Max(maximumError, error);
                        if (error > 0.001f) changed++;
                        float noCasterError = Error(before[index], clear[index]);
                        noCasterMaximumError = Mathf.Max(noCasterMaximumError, noCasterError);
                        if (noCasterError > 0.001f) noCasterChangedPixels++;
                        if (Error(after[index], clear[index]) > 0.01f) shadowPixels++;
                        if (Mathf.Abs(PreviewValue(after[index], previewMinimum, previewScale) - PreviewValue(clear[index], previewMinimum, previewScale)) > 0.05f) previewShadowPixels++;
                    }
                    int firstPayloadPixels = CountPayload(after, values[0]);
                    int secondPayloadPixels = CountPayload(after, values[1]);
                    File.WriteAllText(Path.Combine(output, "view" + view + ".json"), "{\"changedPixels\":" + changed + ",\"shadowPixels\":" + shadowPixels + ",\"previewShadowPixels\":" + previewShadowPixels + ",\"noCasterChangedPixels\":" + noCasterChangedPixels + ",\"noCasterMaximumError\":" + Number(noCasterMaximumError) + ",\"baselineFirstPayloadPixels\":" + baselineFirstPayloadPixels + ",\"baselineSecondPayloadPixels\":" + baselineSecondPayloadPixels + ",\"firstPayloadPixels\":" + firstPayloadPixels + ",\"secondPayloadPixels\":" + secondPayloadPixels + ",\"maximumError\":" + Number(maximumError) + ",\"pixels\":" + before.Length + ",\"casterNative\":" + JsonUtility.ToJson(casterMetrics) + ",\"payloadNative\":" + JsonUtility.ToJson(payloadMetrics) + "}");
                    if (!wind)
                    {
                        Assert.That(baselineFirstPayloadPixels, Is.GreaterThan(100), "Legacy positive-scale payload control.");
                        Assert.That(baselineSecondPayloadPixels, Is.GreaterThan(100), "Legacy second payload control.");
                        Assert.That(firstPayloadPixels, Is.GreaterThan(100));
                        Assert.That(secondPayloadPixels, Is.GreaterThan(100));
                    }
                    else
                    {
                        Assert.That(windPixels, Is.GreaterThan(100), "The lit native wind fixture did not show a measurable deformation against the wind-off control.");
                        Assert.That(payloadMetrics.shadowViews, Is.GreaterThan(0));
                        Assert.That(payloadMetrics.shadowInstances, Is.GreaterThan(0));
                        Assert.That(payloadMetrics.legacySubmissionCalls, Is.Zero);
                    }
                    Assert.That(payloadMetrics.cullingInvocations, Is.GreaterThan(0), "Payload native callback was not invoked: " + payloadMetrics.fallbackReason);
                    Assert.That(payloadMetrics.cameraCommands, Is.EqualTo(1));
                    Assert.That(payloadMetrics.cameraInstances, Is.EqualTo(2));
                    Assert.That(casterMetrics.shadowViews, Is.GreaterThan(0));
                    Assert.That(casterMetrics.shadowCommands, Is.GreaterThan(0));
                    Assert.That(casterMetrics.shadowInstances, Is.GreaterThan(0));
                    Assert.That(casterMetrics.legacySubmissionCalls, Is.Zero);
                    Assert.That(casterMetrics.legacySubmittedInstances, Is.Zero);
                    Assert.That(shadowPixels, Is.GreaterThan(100));
                    Assert.That(previewShadowPixels, Is.GreaterThan(100));
                    Assert.That(noCasterChangedPixels >= before.Length * 0.005f || noCasterMaximumError >= 0.02f, Is.True, "Removing the caster must fail the unchanged A/B image acceptance limits.");
                    Assert.That(changed, Is.LessThan(before.Length * 0.005f));
                    Assert.That(maximumError, Is.LessThan(0.02f));
                }
            }
            finally
            {
                (capture as IDisposable)?.Dispose();
                shadowViews?.Dispose();
                RenderTexture.active = initial;
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                if (initialScene.IsValid() && initialScene.isLoaded) SceneManager.SetActiveScene(initialScene);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                DestroyOwned(owned);
                windGlobals?.Restore();
                if (restoreDefaultScene) EditorSceneManager.NewScene(restoreSceneSetup, NewSceneMode.Single);
            }
        }

        private static void RequireGraphics()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Native submission requires a graphics device.");
        }

        private static bool OwnsDefaultTestScene(Scene scene)
        {
            if (Environment.GetEnvironmentVariable("RVM_NATIVE_ALLOW_DEFAULT_SCENE") != "1" || SceneManager.sceneCount != 1 || !string.IsNullOrEmpty(scene.path) || scene.isDirty || scene.name != "Untitled" && !string.IsNullOrEmpty(scene.name)) return false;
            string name = TestContext.CurrentContext.Test.FullName;
            string prefix = typeof(XRayInstanceNativeRendererTests).FullName + ".";
            if (name != prefix + nameof(NativeSubmissionPreservesOffscreenShadowsAndPayloadAcrossCameras) && name != prefix + nameof(NativeSubmissionPreservesOffscreenPointShadowsAcrossCameras) && name != prefix + nameof(NativeSubmissionPreservesOffscreenSpotShadowsAcrossCameras) && name != prefix + nameof(NativeSubmissionPreservesLitWindAcrossCameras)) return false;
            GameObject[] roots = scene.GetRootGameObjects();
            if (roots.Length == 0) return true;
            if (roots.Length != 2) return false;
            bool camera = false;
            bool light = false;
            foreach (GameObject root in roots)
            {
                if (root.transform.childCount != 0) return false;
                camera |= root.name == "Main Camera" && root.GetComponent<Camera>() != null;
                light |= root.name == "Directional Light" && root.GetComponent<Light>() != null;
            }
            return camera && light;
        }

        private static Material MonolithMaterial(List<UnityEngine.Object> owned, bool diagnostic)
        {
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { enableInstancing = true };
            owned.Add(material);
            material.SetColor("_BaseColor", Color.gray);
            material.SetFloat("_MonolithLevelWind", 0f);
            material.SetFloat("_MonolithFloraWind", 0f);
            material.SetFloat("_MonolithDebug", diagnostic ? 37f : 0f);
            material.EnableKeyword("_MONOLITH_LEVEL");
            return material;
        }

        private static Mesh Quad(List<UnityEngine.Object> owned)
        {
            var mesh = new Mesh { name = "Native payload quad" };
            owned.Add(mesh);
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh WindGrid(List<UnityEngine.Object> owned)
        {
            var mesh = new Mesh { name = "Native lit wind grid" };
            owned.Add(mesh);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color32>();
            var uv = new List<Vector2>();
            var wind = new List<Vector2>();
            var triangles = new List<int>();
            for (int row = 0; row < 5; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    float u = column * 0.25f;
                    float v = row * 0.25f;
                    vertices.Add(new Vector3(u - 0.5f, v * 4f, 0f));
                    normals.Add(new Vector3(-0.5f, 0.5f, -0.7071068f));
                    tangents.Add(new Vector4(0.8164966f, 0f, -0.5773503f, 1f));
                    colors.Add(new Color32(255, 128, 128, 255));
                    uv.Add(new Vector2(u, v));
                    wind.Add(new Vector2(v, 1f));
                    if (row == 4 || column == 4) continue;
                    int index = row * 5 + column;
                    triangles.AddRange(new[] { index, index + 5, index + 1, index + 1, index + 5, index + 6 });
                }
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, new List<Vector2>(new Vector2[vertices.Count]));
            mesh.SetUVs(2, new List<Vector2>(new Vector2[vertices.Count]));
            mesh.SetUVs(3, wind);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void CompileNativePasses(Material material, List<UnityEngine.Object> owned)
        {
            var compilationMaterial = new Material(material) { enableInstancing = false };
            owned.Add(compilationMaterial);
            compilationMaterial.DisableKeyword("INSTANCING_ON");
            compilationMaterial.EnableKeyword("DOTS_INSTANCING_ON");
            foreach (string name in new[] { "ForwardOnly", "DepthForwardOnly", "ShadowCaster", "MotionVectors" })
            {
                int index = compilationMaterial.FindPass(name);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), name);
                ShaderUtil.CompilePass(compilationMaterial, index, true);
                Assert.That(ShaderUtil.IsPassCompiled(compilationMaterial, index), Is.True, "DOTS shader compilation did not complete for " + name);
            }
        }

        private static XRayInstanceBatchSet Set(List<UnityEngine.Object> owned, string name, List<XRayInstanceBatch> batches)
        {
            var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            owned.Add(set);
            set.Initialize(name, 16f, batches);
            set.PrepareMatrices(name);
            return set;
        }

        private static Camera CreateCamera(GameObject root, Scene scene)
        {
            var camera = new GameObject("Native fixture camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.scene = scene;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 3f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.aspect = 1f;
            camera.useOcclusionCulling = false;
            var data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = Color.black;
            data.customRenderingSettings = true;
            foreach (FrameSettingsField field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.Volumetrics, FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.ShadowMaps, true);
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.ShadowMaps] = true;
            return camera;
        }

        private static IEnumerator Capture(Camera camera, RenderTexture target, Texture2D pixels, string evidencePrefix, Action<Color[]> completed, Action<Color[]> motion = null)
        {
            var renderCamera = UnityEngine.Object.Instantiate(camera.gameObject, camera.transform.parent).GetComponent<Camera>();
            renderCamera.scene = camera.scene;
            renderCamera.targetTexture = target;
            RTHandle motionTarget = null;
            RTHandle motionSource = null;
            AOVRequestDataCollection requests = null;
            HDAdditionalCameraData data = renderCamera.GetComponent<HDAdditionalCameraData>();
            int motionCallbacks = 0;
            RenderTexture initial = RenderTexture.active;
            try
            {
                if (motion != null)
                {
                    foreach (FrameSettingsField field in new[] { FrameSettingsField.MotionVectors, FrameSettingsField.ObjectMotionVectors })
                    {
                        data.renderingPathCustomFrameSettings.SetEnabled(field, true);
                        data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                    }
                    motionTarget = RTHandles.Alloc(SIZE, SIZE, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "Native fixture motion AOV");
                    var builder = new AOVRequestBuilder();
                    builder.Add(AOVRequest.NewDefault(), buffer => motionTarget, null, new[] { AOVBuffers.MotionVectors }, (command, textures, properties) =>
                    {
                        motionSource = textures[0];
                        motionCallbacks++;
                    });
                    requests = builder.Build();
                    data.SetAOVRequests(requests);
                }
                ShaderUtil.allowAsyncCompilation = false;
                renderCamera.Render();
                int firstFrame = Time.frameCount;
                int firstRenderedFrame = Time.renderedFrameCount;
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
                pixels.Apply();
                Color[] first = pixels.GetPixels();
                RenderTexture.active = initial;
                yield return null;
                ShaderUtil.allowAsyncCompilation = false;
                renderCamera.Render();
                int secondFrame = Time.frameCount;
                int secondRenderedFrame = Time.renderedFrameCount;
                RenderTexture previous = RenderTexture.active;
                if (motion != null)
                {
                    FrameSettings actual = HDCamera.GetOrCreate(renderCamera).frameSettings;
                    Assert.That(actual.IsEnabled(FrameSettingsField.MotionVectors), Is.True, "The fixture must read actual Game camera motion vectors.");
                    Assert.That(actual.IsEnabled(FrameSettingsField.ObjectMotionVectors), Is.True, "The fixture must render actual object motion vectors.");
                    Assert.That(motionCallbacks, Is.GreaterThanOrEqualTo(2), "Both fixture renders must deliver the motion-vector AOV.");
                    Assert.That(motionSource, Is.Not.Null);
                    RenderTexture.active = motionSource.rt;
                    pixels.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
                    pixels.Apply();
                    motion(pixels.GetPixels());
                }
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
                pixels.Apply();
                RenderTexture.active = previous;
                Color[] result = pixels.GetPixels();
                Vector4 firstValue = new Vector4(0.2f, 0.4f, 0.7f, 0f);
                Vector4 secondValue = new Vector4(0.8f, 0.25f, 0.1f, 0f);
                File.WriteAllText(evidencePrefix + "-frames.json", "{\"policy\":\"Two fixed Camera.Render calls separated by one editor yield; state mutations follow a separate editor yield; no image-based retries\",\"firstFrame\":" + firstFrame + ",\"secondFrame\":" + secondFrame + ",\"firstRenderedFrame\":" + firstRenderedFrame + ",\"secondRenderedFrame\":" + secondRenderedFrame + ",\"firstFrameFirstPayloadPixels\":" + CountPayload(first, firstValue) + ",\"firstFrameSecondPayloadPixels\":" + CountPayload(first, secondValue) + ",\"secondFrameFirstPayloadPixels\":" + CountPayload(result, firstValue) + ",\"secondFrameSecondPayloadPixels\":" + CountPayload(result, secondValue) + ",\"commandBufferCompilationScope\":false,\"allowAsyncCompilation\":" + ShaderUtil.allowAsyncCompilation.ToString().ToLowerInvariant() + ",\"anythingCompiling\":" + ShaderUtil.anythingCompiling.ToString().ToLowerInvariant() + "}");
                completed(result);
            }
            finally
            {
                RenderTexture.active = initial;
                if (requests != null)
                {
                    data.SetAOVRequests(null);
                    requests.Dispose();
                }
                if (motionTarget != null) RTHandles.Release(motionTarget);
                UnityEngine.Object.DestroyImmediate(renderCamera.gameObject);
            }
        }

        private static void SaveEvidence(string prefix, Texture2D raw, Color[] colors, float minimum, float scale)
        {
            var range = new CaptureRange { minimum = new Color(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue), maximum = new Color(float.MinValue, float.MinValue, float.MinValue, float.MinValue), pixelCount = colors.Length };
            var display = new Color[colors.Length];
            for (int index = 0; index < colors.Length; index++)
            {
                Color color = colors[index];
                for (int channel = 0; channel < 4; channel++)
                {
                    Assert.That(float.IsFinite(color[channel]), Is.True, prefix + " non-finite pixel " + index + " channel " + channel);
                    range.minimum[channel] = Mathf.Min(range.minimum[channel], color[channel]);
                    range.maximum[channel] = Mathf.Max(range.maximum[channel], color[channel]);
                }
                float value = PreviewValue(color, minimum, scale);
                display[index] = new Color(value, value, value, 1f);
            }
            File.WriteAllText(prefix + "-range.json", JsonUtility.ToJson(range, true));
            raw.SetPixels(colors);
            raw.Apply();
            File.WriteAllBytes(prefix + ".exr", raw.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            var preview = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true);
            try
            {
                preview.SetPixels(display);
                preview.Apply();
                File.WriteAllBytes(prefix + ".png", preview.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(preview);
            }
        }

        private static int CountPayload(Color[] colors, Vector4 expected)
        {
            int count = 0;
            Color value = new Color(expected.x, expected.y, expected.z, 1f);
            foreach (Color color in colors) if (Error(color, value) < 0.02f) count++;
            return count;
        }

        private static NativeMetrics Metrics(XRayLevelInstancedRenderer renderer)
        {
            return new NativeMetrics { activeAfterCapture = renderer.NativeSubmissionActive, cameraCommands = renderer.NativeCameraCommands, cameraInstances = renderer.NativeCameraInstances, shadowCommands = renderer.NativeShadowCommands, shadowInstances = renderer.NativeShadowInstances, shadowViews = renderer.NativeShadowViews, cullingInvocations = renderer.NativeCullingInvocations, legacySubmissionCalls = renderer.LastSubmittedCalls, legacySubmittedInstances = renderer.LastSubmittedInstances, memoryBytes = renderer.NativeSubmissionMemoryBytes, fallbackReason = renderer.NativeSubmissionFallbackReason };
        }

        private static void DestroyOwned(List<UnityEngine.Object> owned)
        {
            for (int index = owned.Count - 1; index >= 0; index--) UnityEngine.Object.DestroyImmediate(owned[index]);
        }

        private static float Luminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }

        private static float PreviewValue(Color color, float minimum, float scale)
        {
            return Mathf.LinearToGammaSpace(Mathf.Clamp01((Luminance(color) - minimum) * scale + 0.1f));
        }

        private static float Error(Color first, Color second)
        {
            return Mathf.Max(Mathf.Abs(first.r - second.r), Mathf.Max(Mathf.Abs(first.g - second.g), Mathf.Abs(first.b - second.b)));
        }

        private static float Motion(Color color)
        {
            return Mathf.Max(Mathf.Abs(color.r), Mathf.Abs(color.g));
        }

        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        [Serializable]
        private sealed class NativeMetrics
        {
            public bool activeAfterCapture;
            public int cameraCommands;
            public int cameraInstances;
            public int shadowCommands;
            public int shadowInstances;
            public int shadowViews;
            public int cullingInvocations;
            public int legacySubmissionCalls;
            public int legacySubmittedInstances;
            public long memoryBytes;
            public string fallbackReason;
        }

        [Serializable]
        private sealed class CaptureRange
        {
            public Color minimum;
            public Color maximum;
            public int pixelCount;
        }

        private sealed class ShadowViewObserver : IDisposable
        {
            private static readonly FieldInfo BACKEND = typeof(XRayLevelInstancedRenderer).GetField("_nativeRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo GROUP = typeof(XRayInstanceNativeRenderer).GetField("_group", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo CULLING = typeof(BatchRendererGroup).GetField("m_PerformCulling", BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly XRayLevelInstancedRenderer _renderer;
            private readonly Dictionary<BatchRendererGroup, BatchRendererGroup.OnPerformCulling> _originals = new Dictionary<BatchRendererGroup, BatchRendererGroup.OnPerformCulling>();
            private readonly Dictionary<BatchRendererGroup, BatchRendererGroup.OnPerformCulling> _wrappers = new Dictionary<BatchRendererGroup, BatchRendererGroup.OnPerformCulling>();

            public int MaximumLightSplits { get; private set; }

            public ShadowViewObserver(XRayLevelInstancedRenderer renderer)
            {
                _renderer = renderer;
                RenderPipelineManager.beginCameraRendering += Attach;
            }

            public void Reset()
            {
                MaximumLightSplits = 0;
            }

            public void Dispose()
            {
                RenderPipelineManager.beginCameraRendering -= Attach;
                foreach (KeyValuePair<BatchRendererGroup, BatchRendererGroup.OnPerformCulling> entry in _originals)
                {
                    object current = CULLING.GetValue(entry.Key);
                    if (ReferenceEquals(current, _wrappers[entry.Key])) CULLING.SetValue(entry.Key, entry.Value);
                    else if (!ReferenceEquals(current, entry.Value)) throw new InvalidOperationException("The native fixture shadow observer could not restore its callback.");
                }
                _originals.Clear();
                _wrappers.Clear();
            }

            private void Attach(ScriptableRenderContext context, Camera camera)
            {
                if (_renderer == null) return;
                object backend = BACKEND.GetValue(_renderer);
                var group = backend != null ? GROUP.GetValue(backend) as BatchRendererGroup : null;
                if (group == null || _originals.ContainsKey(group)) return;
                Assert.That(CULLING, Is.Not.Null);
                var original = CULLING.GetValue(group) as BatchRendererGroup.OnPerformCulling;
                Assert.That(original, Is.Not.Null);
                BatchRendererGroup.OnPerformCulling wrapper = (rendererGroup, cullingContext, output, userContext) =>
                {
                    if (cullingContext.viewType == BatchCullingViewType.Light) MaximumLightSplits = Math.Max(MaximumLightSplits, cullingContext.cullingSplits.Length);
                    return original(rendererGroup, cullingContext, output, userContext);
                };
                _originals.Add(group, original);
                _wrappers.Add(group, wrapper);
                CULLING.SetValue(group, wrapper);
            }
        }

        private sealed class WindGlobals
        {
            private readonly string[] _properties = { XRayFloraWindState.PARAMS_PROPERTY, XRayFloraWindState.TREES_SETUP_PROPERTY, XRayFloraWindState.ANIM_PROPERTY, XRayFloraWindState.ANIM_PREV_PROPERTY, XRayFloraWindState.TREE_WIND_PROPERTY, XRayFloraWindState.TREE_WAVE_PROPERTY, XRayFloraWindState.TREE_WIND_PREV_PROPERTY, XRayFloraWindState.TREE_WAVE_PREV_PROPERTY };
            private readonly Vector4[] _values;
            private readonly float _active;
            private readonly int _benders;
            private readonly Vector4[] _currentBenders;
            private readonly Vector4[] _previousBenders;

            public WindGlobals()
            {
                _active = Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY);
                _values = new Vector4[_properties.Length];
                for (int index = 0; index < _properties.Length; index++) _values[index] = Shader.GetGlobalVector(_properties[index]);
                _benders = Shader.GetGlobalInteger(XRayGrassBenderState.COUNT_PROPERTY);
                _currentBenders = Shader.GetGlobalVectorArray(XRayGrassBenderState.CURRENT_PROPERTY);
                _previousBenders = Shader.GetGlobalVectorArray(XRayGrassBenderState.PREVIOUS_PROPERTY);
            }

            public void Restore()
            {
                Shader.SetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY, _active);
                for (int index = 0; index < _properties.Length; index++) Shader.SetGlobalVector(_properties[index], _values[index]);
                Shader.SetGlobalInteger(XRayGrassBenderState.COUNT_PROPERTY, _benders);
                if (_currentBenders != null && _currentBenders.Length > 0) Shader.SetGlobalVectorArray(XRayGrassBenderState.CURRENT_PROPERTY, _currentBenders);
                if (_previousBenders != null && _previousBenders.Length > 0) Shader.SetGlobalVectorArray(XRayGrassBenderState.PREVIOUS_PROPERTY, _previousBenders);
            }
        }
    }
}
