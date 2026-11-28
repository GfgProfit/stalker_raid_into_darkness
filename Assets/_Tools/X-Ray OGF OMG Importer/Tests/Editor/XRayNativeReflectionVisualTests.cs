using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Unity.Jobs;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayNativeReflectionVisualTests
    {
        private const int SIZE = 256;
        private const int PROBE_SIZE = 128;
        private const float HALF_MIN_SUBNORMAL = 1f / 16777216f;
        private const string EMPTY_SCENE = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/NativeSubmissionEmpty.unity";

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator NativeCubemapPreservesReflectedSourcesAndOffscreenShadows()
        {
            return RenderScenario(false);
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator NativePlanarPreservesReflectedSourcesAndOffscreenShadows()
        {
            return RenderScenario(true);
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(-0f, -HALF_MIN_SUBNORMAL, 0f)]
        [TestCase(0f, HALF_MIN_SUBNORMAL, 0f)]
        [TestCase(-HALF_MIN_SUBNORMAL, HALF_MIN_SUBNORMAL, HALF_MIN_SUBNORMAL * 2f)]
        [TestCase(1f, 0.99951171875f, 0f)]
        [TestCase(1f, 1.0009765625f, 0f)]
        [TestCase(-1f, -0.99951171875f, 0f)]
        [TestCase(1f, 0.9990234375f, 0.0009765625f)]
        [TestCase(127.9375f, 128f, 0f)]
        [TestCase(127.875f, 128f, 0.125f)]
        [TestCase(1f, 1.0001f, 0.00010001659393310547f)]
        [TestCase(91.5f, 91.5625f, 0f)]
        [TestCase(138f, 0f, 138f)]
        public void HalfPrecisionAllowanceCoversOnlyAdjacentRepresentableValues(float first, float second, float expectedResidual)
        {
            Assert.That(HalfPrecisionResidual(first, second, out _), Is.EqualTo(expectedResidual));
        }

        [Test]
        public void PrecisionBudgetRequiresHalfBufferFormatAndKeepsRawDifferences()
        {
            var first = new[] { new Color(91.5f, 91.5f, 91.5f, 1f) };
            var second = new[] { new Color(91.5625f, 91.5625f, 91.5f, 1f) };
            XRayNativeReflectionComparison half = Compare(first, second, first, first, GraphicsFormat.R16G16B16A16_SFloat);
            XRayNativeReflectionComparison full = Compare(first, second, first, first, GraphicsFormat.R32G32B32A32_SFloat);
            Assert.That(half.maximumError, Is.EqualTo(0.0625f));
            Assert.That(half.maximumResidual, Is.Zero);
            Assert.That(half.rawNonzeroChangedPixels, Is.EqualTo(1));
            Assert.That(half.adjacentHalfBudgetPixels, Is.EqualTo(1));
            Assert.That(half.adjacentHalfBudgetChannels, Is.EqualTo(2));
            Assert.That(full.maximumResidual, Is.EqualTo(full.maximumError));
            Assert.That(full.adjacentHalfBudgetPixels, Is.Zero);
            second[0].a = 0.999f;
            XRayNativeReflectionComparison alpha = Compare(first, second, first, first, GraphicsFormat.R16G16B16A16_SFloat);
            Assert.That(alpha.maximumAlphaError, Is.GreaterThan(0f));
            Assert.That(alpha.maximumResidual, Is.EqualTo(alpha.maximumAlphaError));
        }

        private static IEnumerator RenderScenario(bool planar)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Native reflection validation requires a graphics device.");
            if (!StageUtility.GetCurrentStageHandle().Equals(StageUtility.GetMainStageHandle())) Assert.Ignore("The native reflection fixture requires the Main Stage.");
            HdrpTestWarmup.Ensure();
            Scene initialScene = SceneManager.GetActiveScene();
            bool restoreDefaultScene = initialScene.IsValid() && string.IsNullOrEmpty(initialScene.path);
            if (!initialScene.IsValid() || restoreDefaultScene && !OwnsDefaultTestScene(initialScene)) Assert.Ignore("A saved active scene or an explicitly allowed unchanged NUnit bootstrap scene is required for the additive native reflection fixture.");
            NewSceneSetup restoreSceneSetup = initialScene.rootCount == 0 ? NewSceneSetup.EmptyScene : NewSceneSetup.DefaultGameObjects;
            var owned = new List<UnityEngine.Object>();
            RenderTexture initialTarget = RenderTexture.active;
            bool initialAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            Scene scene = default;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                if (restoreDefaultScene)
                {
                    Assert.That(File.Exists(EMPTY_SCENE), Is.True);
                    EditorSceneManager.OpenScene(EMPTY_SCENE, OpenSceneMode.Single);
                }
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Native reflection visual fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                Camera camera = CreateCamera(root, scene);
                var target = new RenderTexture(SIZE, SIZE, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                owned.Add(target);
                target.Create();
                camera.targetTexture = target;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                owned.Add(profile);
                profile.Add<Fog>(true).enabled.Override(false);
                VisualEnvironment environment = profile.Add<VisualEnvironment>(true);
                environment.skyType.Override(0);
                environment.skyAmbientMode.Override(SkyAmbientMode.Static);
                Exposure exposure = profile.Add<Exposure>(true);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(0f);
                var volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000;
                volume.sharedProfile = profile;
                var sun = new GameObject("Native reflection fixture sun").AddComponent<Light>();
                sun.transform.SetParent(root.transform);
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Hard;
                sun.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -0.6f, 1f));
                HDAdditionalLightData sunData = sun.gameObject.AddComponent<HDAdditionalLightData>();
                sun.intensity = 3000f;
                sun.lightUnit = LightUnit.Lux;
                sunData.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
                var receiverMaterial = new Material(Shader.Find("HDRP/Lit"));
                owned.Add(receiverMaterial);
                receiverMaterial.SetColor("_BaseColor", Color.gray);
                receiverMaterial.SetFloat("_Smoothness", 0f);
                CreateReceiver(root, receiverMaterial, new Vector3(0f, -0.05f, 0f), new Vector3(12f, 0.1f, 12f));
                CreateReceiver(root, receiverMaterial, new Vector3(0f, 2f, 2f), new Vector3(12f, 4f, 0.1f));
                var prototype = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Mesh cube = prototype.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(prototype);
                Material material = SourceMaterial(owned);
                CompileNativePasses(material, owned);
                var casterRoot = new GameObject("Native reflection offscreen casters");
                casterRoot.transform.SetParent(root.transform);
                var casters = casterRoot.AddComponent<XRayLevelInstancedRenderer>();
                casters.enabled = false;
                var casterBatches = new List<XRayInstanceBatch>();
                foreach (float x in new[] { 4f, 4.5f })
                {
                    Vector3 position = new Vector3(x, 3f, -1f);
                    casterBatches.Add(new XRayInstanceBatch("reflection offscreen caster", cube, new[] { material }, new Bounds(position, Vector3.one), new[] { Matrix4x4.Translate(position) }, new[] { new Vector4(1f, 0f, 0f, 0f) }, true));
                }
                casters.BatchSet = Set(owned, "reflection casters", casterBatches);
                var payloadRoot = new GameObject("Native reflected sources");
                payloadRoot.transform.SetParent(root.transform);
                var sources = payloadRoot.AddComponent<XRayLevelInstancedRenderer>();
                sources.enabled = false;
                Mesh quad = Quad(owned);
                var sourceBatches = new List<XRayInstanceBatch>();
                for (int index = 0; index < 2; index++)
                {
                    Vector3 position = new Vector3(index == 0 ? -1.2f : 1.2f, 1.1f, 0f);
                    Matrix4x4 matrix = Matrix4x4.TRS(position, Quaternion.identity, new Vector3(1.1f, 1.4f, 1f));
                    sourceBatches.Add(new XRayInstanceBatch("reflection lit payload", quad, new[] { material }, GeometryUtility.CalculateBounds(quad.vertices, matrix), new[] { matrix }, new[] { new Vector4(index == 0 ? 0.25f : 0.85f, 0f, 0f, 0f) }, true));
                }
                sources.BatchSet = Set(owned, "reflection sources", sourceBatches);
                HDProbe probe = CreateProbe(root, camera, planar);
                probe.enabled = false;
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                foreach (XRayInstanceBatch batch in casterBatches) Assert.That(GeometryUtility.TestPlanesAABB(planes, batch.Bounds), Is.False, "The caster must stay outside the main camera frustum.");
                var standardCasterObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                standardCasterObject.name = "Native reflection ordinary shadow control";
                standardCasterObject.transform.SetParent(root.transform);
                standardCasterObject.transform.position = new Vector3(4f, 3.6f, -3f);
                standardCasterObject.transform.localScale = Vector3.one * 0.9f;
                MeshRenderer standardCaster = standardCasterObject.GetComponent<MeshRenderer>();
                standardCaster.sharedMaterial = receiverMaterial;
                standardCaster.shadowCastingMode = ShadowCastingMode.On;
                Assert.That(GeometryUtility.TestPlanesAABB(planes, standardCaster.bounds), Is.False, "The ordinary shadow control must stay outside the main camera frustum.");
                XRayNativeReflectionFrame baseline = null;
                XRayNativeReflectionFrame native = null;
                XRayNativeReflectionFrame noSourceCaster = null;
                XRayNativeReflectionFrame noCaster = null;
                XRayNativeReflectionFrame noSources = null;
                IEnumerator capture = Capture(camera, target, probe, casters, sources, standardCaster, false, true, true, true, value => baseline = value);
                while (capture.MoveNext()) yield return capture.Current;
                capture = Capture(camera, target, probe, casters, sources, standardCaster, true, true, true, true, value => native = value);
                while (capture.MoveNext()) yield return capture.Current;
                capture = Capture(camera, target, probe, casters, sources, standardCaster, true, false, true, true, value => noSourceCaster = value);
                while (capture.MoveNext()) yield return capture.Current;
                capture = Capture(camera, target, probe, casters, sources, standardCaster, true, false, true, false, value => noCaster = value);
                while (capture.MoveNext()) yield return capture.Current;
                capture = Capture(camera, target, probe, casters, sources, standardCaster, true, false, false, true, value => noSources = value);
                while (capture.MoveNext()) yield return capture.Current;
                float maximum = 0f;
                foreach (Color color in baseline.main) maximum = Mathf.Max(maximum, Luminance(color));
                foreach (Color color in baseline.reflection) maximum = Mathf.Max(maximum, Luminance(color));
                Assert.That(maximum, Is.GreaterThan(0.000001f), "The reflection fixture must contain lit visible surfaces.");
                var pipeline = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
                GraphicsFormat mainFormat = pipeline != null ? (GraphicsFormat)pipeline.currentPlatformRenderPipelineSettings.colorBufferFormat : GraphicsFormat.None;
                GraphicsFormat reflectionFormat = probe.realtimeTexture.graphicsFormat;
                XRayNativeReflectionComparison main = Compare(baseline.main, native.main, noSourceCaster.main, noSources.main, mainFormat);
                XRayNativeReflectionComparison reflected = Compare(baseline.reflection, native.reflection, noCaster.reflection, noSources.reflection, reflectionFormat);
                XRayNativeReflectionComparison mainShadowControl = Compare(baseline.main, noSourceCaster.main, noSourceCaster.main, noSources.main, mainFormat);
                XRayNativeReflectionComparison reflectionAllCastersControl = Compare(baseline.reflection, noCaster.reflection, noCaster.reflection, noSources.reflection, reflectionFormat);
                XRayNativeReflectionComparison reflectionNativeSourceControl = Compare(native.reflection, noSourceCaster.reflection, noSourceCaster.reflection, noSources.reflection, reflectionFormat);
                Assert.That(mainShadowControl.changedPixels >= baseline.main.Length * 0.005f || mainShadowControl.rawNonzeroChangedPixels >= baseline.main.Length * 0.005f || mainShadowControl.maximumAlphaError > 0f || mainShadowControl.maximumResidual >= 0.02f, Is.True, "Removing native-source caster shadows must fail the unchanged main-image A/B acceptance limits.");
                Assert.That(reflectionAllCastersControl.changedPixels >= baseline.reflection.Length * 0.005f || reflectionAllCastersControl.rawNonzeroChangedPixels >= baseline.reflection.Length * 0.005f || reflectionAllCastersControl.maximumAlphaError > 0f || reflectionAllCastersControl.maximumResidual >= 0.02f, Is.True, "Removing all caster shadows must fail the unchanged reflection-image A/B acceptance limits.");
                Assert.That(reflectionNativeSourceControl.changedPixels, Is.LessThan(native.reflection.Length * 0.005f));
                Assert.That(reflectionNativeSourceControl.rawNonzeroChangedPixels, Is.LessThan(native.reflection.Length * 0.005f));
                Assert.That(reflectionNativeSourceControl.maximumAlphaError, Is.Zero);
                Assert.That(reflectionNativeSourceControl.maximumResidual, Is.LessThan(0.02f));
                Assert.That(baseline.metrics.reflectionCommands, Is.EqualTo(0));
                Assert.That(native.metrics.reflectionCommands, Is.GreaterThan(0), "The actual reflection must use native commands: " + native.metrics.fallbackReason);
                Assert.That(native.metrics.reflectionInstances, Is.GreaterThan(0));
                Assert.That(native.metrics.reflectionViews, Is.GreaterThan(0));
                Assert.That(native.metrics.shadowCommands, Is.GreaterThan(0));
                Assert.That(native.metrics.shadowInstances, Is.GreaterThan(0));
                Assert.That(native.metrics.legacySubmissionCalls, Is.Zero);
                Assert.That(native.metrics.legacySubmittedInstances, Is.Zero);
                Assert.That(native.metrics.reflectionCameraEnds, Is.GreaterThan(0), "HDRP must render real hidden reflection cameras.");
                Assert.That(native.metrics.reflectionCamerasWithoutShadows, Is.EqualTo(0), "Actual reflection cameras must retain ShadowMaps.");
                Assert.That(native.metrics.nativeBuffersStable, Is.True, "Reflection registration must preserve the main camera's prepared GPU buffers.");
                Assert.That(native.metrics.updateAfter, Is.GreaterThan(native.metrics.updateBefore), "B must update the actual probe texture, not reuse A.");
                Assert.That(main.casterControlPixels, Is.GreaterThan(32), "The offscreen caster must contribute a visible main-camera shadow.");
                Assert.That(main.sourceControlPixels, Is.GreaterThan(32), "The main source control must remain observable.");
                Assert.That(reflected.sourceControlPixels, Is.GreaterThan(32), "The actual reflection must visibly include the source renderers.");
                Assert.That(reflected.casterControlPixels, Is.GreaterThan(16), "The actual reflection must retain the caster contribution with shadows enabled.");
                Assert.That(main.changedPixels, Is.LessThan(baseline.main.Length * 0.005f));
                Assert.That(reflected.changedPixels, Is.LessThan(baseline.reflection.Length * 0.005f));
                Assert.That(main.rawNonzeroChangedPixels, Is.LessThan(baseline.main.Length * 0.005f));
                Assert.That(reflected.rawNonzeroChangedPixels, Is.LessThan(baseline.reflection.Length * 0.005f));
                Assert.That(main.maximumAlphaError, Is.Zero);
                Assert.That(reflected.maximumAlphaError, Is.Zero);
                Assert.That(main.maximumResidual, Is.LessThan(0.02f));
                Assert.That(reflected.maximumResidual, Is.LessThan(0.02f));
            }
            finally
            {
                RenderTexture.active = initialTarget;
                ShaderUtil.allowAsyncCompilation = initialAsyncCompilation;
                if (initialScene.IsValid() && initialScene.isLoaded) SceneManager.SetActiveScene(initialScene);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                for (int index = owned.Count - 1; index >= 0; index--) if (owned[index] != null) UnityEngine.Object.DestroyImmediate(owned[index]);
                if (restoreDefaultScene) EditorSceneManager.NewScene(restoreSceneSetup, NewSceneMode.Single);
            }
        }

        private static IEnumerator Capture(Camera camera, RenderTexture target, HDProbe probe, XRayLevelInstancedRenderer casters, XRayLevelInstancedRenderer sources, MeshRenderer standardCaster, bool native, bool showCasters, bool showSources, bool standardCasterShadows, Action<XRayNativeReflectionFrame> completed)
        {
            XRayNativeReflectionFrame frame = null;
            casters.NativeSubmissionOverride = native;
            sources.NativeSubmissionOverride = native;
            standardCaster.shadowCastingMode = standardCasterShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            typeof(XRayLevelInstancedRenderer).GetField("_castShadows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(casters, showCasters);
            for (int step = 0; step < 2; step++)
            {
                casters.enabled = false;
                sources.enabled = false;
                probe.enabled = false;
                yield return null;
                int reflectionCameraEnds = 0;
                int reflectionCamerasWithInheritedSky = 0;
                int reflectionCamerasWithoutShadows = 0;
                object casterBuffer = null;
                object sourceBuffer = null;
                bool nativeBuffersStable = true;
                var culling = new XRayNativeReflectionCullingObserver();
                void Begin(ScriptableRenderContext context, Camera candidate)
                {
                    ShaderUtil.allowAsyncCompilation = false;
                    if (!native || !showSources) return;
                    culling.Attach(casters, "casters");
                    culling.Attach(sources, "sources");
                    if (candidate == camera)
                    {
                        casterBuffer = Buffer(casters);
                        sourceBuffer = Buffer(sources);
                        nativeBuffersStable = casterBuffer != null && sourceBuffer != null;
                    }
                    else if (candidate.cameraType == CameraType.Reflection)
                    {
                        nativeBuffersStable &= casterBuffer != null && sourceBuffer != null && ReferenceEquals(casterBuffer, Buffer(casters)) && ReferenceEquals(sourceBuffer, Buffer(sources));
                    }
                }
                void End(ScriptableRenderContext context, Camera candidate)
                {
                    if (candidate.cameraType == CameraType.Reflection)
                    {
                        reflectionCameraEnds++;
                        HDCamera reflectionCamera = HDCamera.GetOrCreate(candidate);
                        if (!reflectionCamera.frameSettings.IsEnabled(FrameSettingsField.ShadowMaps)) reflectionCamerasWithoutShadows++;
                        VisualEnvironment reflectionEnvironment = reflectionCamera.volumeStack.GetComponent<VisualEnvironment>();
                        if (reflectionEnvironment.skyType.value != 0 || reflectionEnvironment.skyAmbientMode.value != SkyAmbientMode.Static) reflectionCamerasWithInheritedSky++;
                    }
                }
                try
                {
                    casters.enabled = showSources;
                    sources.enabled = showSources;
                    probe.enabled = true;
                    RenderPipelineManager.beginCameraRendering += Begin;
                    RenderPipelineManager.endCameraRendering += End;
                    uint updateBefore = probe.realtimeTexture != null ? probe.realtimeTexture.updateCount : 0;
                    uint renderCountBefore = RenderCount(probe);
                    probe.RequestRenderNextUpdate();
                    camera.Render();
                    var metrics = new XRayNativeReflectionCaptureMetrics
                    {
                        updateBefore = updateBefore,
                        updateAfter = probe.realtimeTexture != null ? probe.realtimeTexture.updateCount : 0,
                        renderCountBefore = renderCountBefore,
                        renderCountAfter = RenderCount(probe),
                        reflectionCommands = casters.NativeReflectionCommands + sources.NativeReflectionCommands,
                        reflectionInstances = casters.NativeReflectionInstances + sources.NativeReflectionInstances,
                        reflectionViews = casters.NativeReflectionViews + sources.NativeReflectionViews,
                        shadowCommands = casters.NativeShadowCommands + sources.NativeShadowCommands,
                        shadowInstances = casters.NativeShadowInstances + sources.NativeShadowInstances,
                        legacySubmissionCalls = casters.LastSubmittedCalls + sources.LastSubmittedCalls,
                        legacySubmittedInstances = casters.LastSubmittedInstances + sources.LastSubmittedInstances,
                        reflectionCameraEnds = reflectionCameraEnds,
                        reflectionCamerasWithInheritedSky = reflectionCamerasWithInheritedSky,
                        reflectionCamerasWithoutShadows = reflectionCamerasWithoutShadows,
                        nativeBuffersStable = nativeBuffersStable,
                        fallbackReason = casters.NativeSubmissionFallbackReason + ";" + sources.NativeSubmissionFallbackReason
                    };
                    XRayNativeReflectionCullingMetadata cullingMetadata = culling.Snapshot();
                    if (native && showCasters && showSources)
                    {
                        Assert.That(cullingMetadata.error, Is.Null);
                        bool lightObserved = false;
                        bool reflectionLightObserved = false;
                        int gameShadowCommands = 0;
                        foreach (XRayNativeReflectionCullingViewMetadata view in cullingMetadata.views)
                        {
                            if (view.viewType != BatchCullingViewType.Light.ToString()) continue;
                            lightObserved = true;
                            Assert.That(view.ownerCameraEntityId, Is.Not.Null.And.Not.Empty);
                            reflectionLightObserved |= view.ownerCameraType == CameraType.Reflection.ToString();
                            if (view.shadowCommands == 0) continue;
                            Assert.That(view.ownerCameraType, Is.EqualTo(CameraType.Game.ToString()));
                            Assert.That(view.ownerCameraEntityId, Is.EqualTo(camera.GetEntityId().ToString()));
                            gameShadowCommands += view.shadowCommands;
                        }
                        Assert.That(lightObserved, Is.True, "The native reflection fixture must execute an actual BRG Light view.");
                        Assert.That(reflectionLightObserved, Is.True, "Actual reflection Light callbacks must expose their owning reflection cameras.");
                        Assert.That(gameShadowCommands, Is.GreaterThan(0), "Native source shadow commands must belong to the prepared Game camera.");
                    }
                    Assert.That(probe.realtimeTexture, Is.Not.Null, "The requested reflection must create an actual HDRP texture.");
                    Assert.That(metrics.updateAfter, Is.GreaterThan(metrics.updateBefore), "Each fixed capture frame must update the probe texture.");
                    Assert.That(metrics.renderCountAfter, Is.GreaterThan(metrics.renderCountBefore), "HDRP must complete a real probe capture, not only allocate its texture.");
                    Assert.That(metrics.reflectionCamerasWithInheritedSky, Is.EqualTo(0), "Every reflection camera must use the owned deterministic black static ambient environment.");
                    Color[] main = Read(target);
                    Color[] reflected = ReadProbe(probe.realtimeTexture, out _, out _);
                    AssertFinite(main, "Main camera");
                    AssertFinite(reflected, "Reflection");
                    frame = new XRayNativeReflectionFrame { main = main, reflection = reflected, metrics = metrics };
                }
                finally
                {
                    RenderPipelineManager.beginCameraRendering -= Begin;
                    RenderPipelineManager.endCameraRendering -= End;
                    try
                    {
                        culling.Dispose();
                    }
                    finally
                    {
                        casters.enabled = false;
                        sources.enabled = false;
                        probe.enabled = false;
                    }
                }
            }
            completed(frame);
        }

        private static HDProbe CreateProbe(GameObject root, Camera camera, bool planar)
        {
            var probeRoot = new GameObject(planar ? "Native fixture planar reflection" : "Native fixture cubemap reflection");
            probeRoot.transform.SetParent(root.transform);
            HDProbe probe;
            if (planar)
            {
                probe = probeRoot.AddComponent<PlanarReflectionProbe>();
                probeRoot.transform.position = new Vector3(0f, 0.05f, 0f);
                probe.settingsRaw.proxySettings.mirrorPositionProxySpace = Vector3.zero;
                probe.settingsRaw.proxySettings.mirrorRotationProxySpace = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                probe.settingsRaw.resolutionScalable.useOverride = true;
                probe.settingsRaw.resolutionScalable.@override = (PlanarReflectionAtlasResolution)PROBE_SIZE;
                probe.settingsRaw.frustum.fieldOfViewMode = ProbeSettings.Frustum.FOVMode.Viewer;
                probe.settingsRaw.frustum.viewerScale = 1f;
            }
            else
            {
                probeRoot.AddComponent<ReflectionProbe>();
                probe = probeRoot.AddComponent<HDAdditionalReflectionData>();
                probeRoot.transform.position = new Vector3(0f, 1.5f, -3f);
                probe.settingsRaw.cubeResolution.useOverride = true;
                probe.settingsRaw.cubeResolution.@override = (CubeReflectionResolution)PROBE_SIZE;
            }
            probeRoot.SetActive(false);
            probe.mode = ProbeSettings.Mode.Realtime;
            probe.realtimeMode = ProbeSettings.RealtimeMode.OnDemand;
            probe.timeSlicing = false;
            probe.influenceVolume.shape = InfluenceShape.Box;
            probe.influenceVolume.boxSize = new Vector3(20f, 20f, 20f);
            probe.settingsRaw.proxySettings.useInfluenceVolumeAsProxyVolume = true;
            probe.settingsRaw.cameraSettings.customRenderingSettings = true;
            probe.settingsRaw.cameraSettings.frustum.nearClipPlaneRaw = 0.05f;
            probe.settingsRaw.cameraSettings.frustum.farClipPlaneRaw = 100f;
            probe.settingsRaw.cameraSettings.culling.useOcclusionCulling = false;
            probe.settingsRaw.cameraSettings.bufferClearing.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            probe.settingsRaw.cameraSettings.bufferClearing.backgroundColorHDR = Color.black;
            probe.settingsRaw.cameraSettings.volumes.anchorOverride = camera.transform;
            probe.settingsRaw.cameraSettings.volumes.layerMask = 1 << root.layer;
            foreach (FrameSettingsField field in DisabledFields())
            {
                probe.frameSettings.SetEnabled(field, false);
                probe.frameSettingsOverrideMask.mask[(uint)field] = true;
            }
            probe.frameSettings.SetEnabled(FrameSettingsField.ShadowMaps, true);
            probe.frameSettingsOverrideMask.mask[(uint)FrameSettingsField.ShadowMaps] = true;
            probe.weight = 1f;
            probe.multiplier = 1f;
            probe.fadeDistance = 100f;
            probeRoot.SetActive(true);
            return probe;
        }

        private static Camera CreateCamera(GameObject root, Scene scene)
        {
            var camera = new GameObject("Native reflection fixture camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.scene = scene;
            camera.enabled = false;
            camera.transform.position = new Vector3(0f, 8f, -8f);
            camera.transform.LookAt(new Vector3(0f, 1f, 0f));
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
            data.volumeLayerMask = 1 << root.layer;
            foreach (FrameSettingsField field in DisabledFields())
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            foreach (FrameSettingsField field in new[] { FrameSettingsField.ShadowMaps, FrameSettingsField.ReflectionProbe, FrameSettingsField.PlanarProbe })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            return camera;
        }

        private static FrameSettingsField[] DisabledFields()
        {
            return new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.Volumetrics, FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl };
        }

        private static void CreateReceiver(GameObject root, Material material, Vector3 position, Vector3 scale)
        {
            var receiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
            receiver.transform.SetParent(root.transform);
            receiver.transform.position = position;
            receiver.transform.localScale = scale;
            MeshRenderer renderer = receiver.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Material SourceMaterial(List<UnityEngine.Object> owned)
        {
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { enableInstancing = true };
            owned.Add(material);
            material.SetColor("_BaseColor", new Color(0.3f, 0.6f, 0.2f, 1f));
            material.SetFloat("_MonolithLevelWind", 0f);
            material.SetFloat("_MonolithFloraWind", 0f);
            material.SetFloat("_MonolithDebug", 0f);
            material.EnableKeyword("_MONOLITH_LEVEL");
            return material;
        }

        private static void CompileNativePasses(Material material, List<UnityEngine.Object> owned)
        {
            var compilation = new Material(material) { enableInstancing = false };
            owned.Add(compilation);
            compilation.DisableKeyword("INSTANCING_ON");
            compilation.EnableKeyword("DOTS_INSTANCING_ON");
            foreach (string pass in new[] { "ForwardOnly", "DepthForwardOnly", "ShadowCaster", "MotionVectors" })
            {
                int index = compilation.FindPass(pass);
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                ShaderUtil.CompilePass(compilation, index, true);
                Assert.That(ShaderUtil.IsPassCompiled(compilation, index), Is.True, pass);
            }
        }

        private static Mesh Quad(List<UnityEngine.Object> owned)
        {
            var mesh = new Mesh { name = "Native reflected lit quad" };
            owned.Add(mesh);
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static XRayInstanceBatchSet Set(List<UnityEngine.Object> owned, string name, List<XRayInstanceBatch> batches)
        {
            var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            owned.Add(set);
            set.Initialize(name, 16f, batches);
            set.PrepareMatrices(name);
            return set;
        }

        private static Color[] Read(RenderTexture target)
        {
            RenderTexture initial = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                return pixels.GetPixels();
            }
            finally
            {
                RenderTexture.active = initial;
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        private static object Buffer(XRayLevelInstancedRenderer renderer)
        {
            object backend = typeof(XRayLevelInstancedRenderer).GetField("_nativeRenderer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(renderer);
            return backend != null ? typeof(XRayInstanceNativeRenderer).GetField("_buffer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(backend) : null;
        }

        private static uint RenderCount(HDProbe probe)
        {
            return (uint)typeof(HDProbe).GetField("m_RealtimeRenderCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(probe);
        }

        private static Color[] ReadProbe(RenderTexture target, out int width, out int height)
        {
            if (target.dimension != TextureDimension.Cube)
            {
                width = target.width;
                height = target.height;
                return Read(target);
            }
            width = target.width * 3;
            height = target.height * 2;
            var atlas = new Color[width * height];
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            RenderTexture initial = RenderTexture.active;
            try
            {
                for (int face = 0; face < 6; face++)
                {
                    Graphics.SetRenderTarget(target, 0, (CubemapFace)face);
                    pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    pixels.Apply();
                    Color[] colors = pixels.GetPixels();
                    for (int row = 0; row < target.height; row++) Array.Copy(colors, row * target.width, atlas, (face / 3 * target.height + row) * width + face % 3 * target.width, target.width);
                }
                return atlas;
            }
            finally
            {
                RenderTexture.active = initial;
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        private static XRayNativeReflectionComparison Compare(Color[] baseline, Color[] native, Color[] noCaster, Color[] noSources, GraphicsFormat format)
        {
            Assert.That(native.Length, Is.EqualTo(baseline.Length));
            Assert.That(noCaster.Length, Is.EqualTo(baseline.Length));
            Assert.That(noSources.Length, Is.EqualTo(baseline.Length));
            bool half = format == GraphicsFormat.R16G16B16A16_SFloat;
            var result = new XRayNativeReflectionComparison { pixels = baseline.Length, precisionFormat = format.ToString(), precisionMethod = half ? "Budget only finite exactly round-tripping binary16 RGB pairs with ordered half-bit distance at most one. All other RGB and alpha errors retain the original absolute tolerance. Raw errors and changed-pixel count remain unchanged." : "No precision allowance; original absolute RGB and alpha errors." };
            for (int index = 0; index < baseline.Length; index++)
            {
                float error = Error(baseline[index], native[index]);
                result.maximumError = Mathf.Max(result.maximumError, error);
                float residual = error;
                bool budgetedPixel = false;
                if (half)
                {
                    residual = 0f;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        residual = Mathf.Max(residual, HalfPrecisionResidual(baseline[index][channel], native[index][channel], out bool budgeted));
                        if (!budgeted) continue;
                        result.adjacentHalfBudgetChannels++;
                        budgetedPixel = true;
                    }
                }
                if (budgetedPixel) result.adjacentHalfBudgetPixels++;
                float alphaError = Mathf.Abs(baseline[index].a - native[index].a);
                if (error > 0f || alphaError > 0f) result.rawNonzeroChangedPixels++;
                result.maximumAlphaError = Mathf.Max(result.maximumAlphaError, alphaError);
                result.maximumResidual = Mathf.Max(result.maximumResidual, Mathf.Max(residual, alphaError));
                if (error > 0.001f) result.changedPixels++;
                if (Error(native[index], noCaster[index]) > 0.01f) result.casterControlPixels++;
                if (Error(native[index], noSources[index]) > 0.01f) result.sourceControlPixels++;
            }
            return result;
        }

        private static float HalfPrecisionResidual(float first, float second, out bool budgeted)
        {
            budgeted = false;
            if (!float.IsFinite(first) || !float.IsFinite(second)) return float.PositiveInfinity;
            float error = Mathf.Abs(first - second);
            ushort firstHalf = Mathf.FloatToHalf(first);
            ushort secondHalf = Mathf.FloatToHalf(second);
            if (Mathf.HalfToFloat(firstHalf) != first || Mathf.HalfToFloat(secondHalf) != second) return error;
            int firstOrdered = (firstHalf & 0x8000) != 0 ? 0x8000 - (firstHalf & 0x7fff) : 0x8000 + firstHalf;
            int secondOrdered = (secondHalf & 0x8000) != 0 ? 0x8000 - (secondHalf & 0x7fff) : 0x8000 + secondHalf;
            if (Math.Abs(firstOrdered - secondOrdered) > 1) return error;
            budgeted = error > 0f;
            return 0f;
        }

        private static void AssertFinite(Color[] colors, string name)
        {
            for (int index = 0; index < colors.Length; index++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    Assert.That(float.IsFinite(colors[index][channel]), Is.True, name + " non-finite pixel " + index + " channel " + channel);
                }
            }
        }

        private static bool OwnsDefaultTestScene(Scene scene)
        {
            if (Environment.GetEnvironmentVariable("RVM_NATIVE_ALLOW_DEFAULT_SCENE") != "1" || SceneManager.sceneCount != 1 || !string.IsNullOrEmpty(scene.path) || scene.isDirty || scene.name != "Untitled" && !string.IsNullOrEmpty(scene.name)) return false;
            string name = TestContext.CurrentContext.Test.FullName;
            string prefix = typeof(XRayNativeReflectionVisualTests).FullName + ".";
            if (name != prefix + nameof(NativeCubemapPreservesReflectedSourcesAndOffscreenShadows) && name != prefix + nameof(NativePlanarPreservesReflectedSourcesAndOffscreenShadows)) return false;
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

        private static float Luminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }

        private static float Error(Color first, Color second)
        {
            return Mathf.Max(Mathf.Abs(first.r - second.r), Mathf.Max(Mathf.Abs(first.g - second.g), Mathf.Abs(first.b - second.b)));
        }
    }
}
