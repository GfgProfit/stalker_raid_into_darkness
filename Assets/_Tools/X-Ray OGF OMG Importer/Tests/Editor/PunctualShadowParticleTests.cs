using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class PunctualShadowParticleTests
    {
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive";

        private AsyncOperation _unload;

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator RenderedParticleDescriptorsUseRegisteredTemplateShadowFade()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Particle light validation requires a graphics device.");
            if (GraphicsSettings.currentRenderPipeline is not HDRenderPipelineAsset) Assert.Ignore("Particle light validation requires HDRP.");
            if (!StageUtility.GetCurrentStageHandle().Equals(StageUtility.GetMainStageHandle())) Assert.Ignore("Particle light validation requires the Main Stage.");
            for (int index = 0; index < SceneManager.sceneCount; index++) Assert.That(SceneManager.GetSceneAt(index).isDirty, Is.False, "Particle light validation preserves dirty Editor scenes.");
            yield return new EnterPlayMode();
            Exception failure = null;
            IEnumerator fixture = RenderInPlayMode();
            while (true)
            {
                bool moved = false;
                object current = null;
                try
                {
                    moved = fixture.MoveNext();
                    if (moved) current = fixture.Current;
                }
                catch (Exception error)
                {
                    failure = error;
                }
                if (failure != null || !moved) break;
                yield return current;
            }
            try
            {
                (fixture as IDisposable)?.Dispose();
            }
            catch (Exception error)
            {
                if (failure == null) failure = error;
            }
            while (_unload != null && !_unload.isDone) yield return null;
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            Assert.That(failure, Is.Null, failure != null ? failure.ToString() : null);
        }

        private IEnumerator RenderInPlayMode()
        {
            Assert.That(Application.isPlaying, Is.True);
            Scene initialScene = SceneManager.GetActiveScene();
            Scene scene = default;
            GameObject root = null;
            GameConfig configuration = null;
            Material material = null;
            RenderTexture target = null;
            Camera camera = null;
            Light template = null;
            Light control = null;
            ParticleSystem particles = null;
            HDAdditionalLightData templateData = null;
            PunctualShadowDistance policy = null;
            var evidence = new PunctualShadowParticleEvidence();
            string phase = "initial75";
            int render = 0;
            var particleBuffer = new ParticleSystem.Particle[4];
            string outputRoot = Environment.GetEnvironmentVariable("PUNCTUAL_SHADOW_TEST_OUTPUT");
            if (string.IsNullOrEmpty(outputRoot)) outputRoot = Path.Combine(ARCHIVE, "PunctualShadowParticleValidation");
            string output = Path.Combine(Path.GetFullPath(outputRoot), DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff") + "_ParticleInheritance");
            Assert.That(output.StartsWith(ARCHIVE + "\\", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(Directory.Exists(output), Is.False);
            Directory.CreateDirectory(output);
            TestContext.WriteLine("Particle light inheritance evidence: " + output);
            Action<ScriptableRenderContext, Camera> observe = (context, currentCamera) =>
            {
                if (currentCamera != camera || !camera.TryGetCullingParameters(out ScriptableCullingParameters parameters)) return;
                CullingResults results = context.Cull(ref parameters);
                evidence.visibleLightObservations += results.visibleLights.Length;
                evidence.cullSnapshots++;
                int particleCount = particles != null ? particles.GetParticles(particleBuffer) : 0;
                for (int slot = 0; slot < results.visibleLights.Length; slot++)
                {
                    VisibleLight visible = results.visibleLights[slot];
                    Light light = visible.light;
                    if (light == template) evidence.templateVisibleObservations++;
                    if (light == control) evidence.controlVisibleObservations++;
                    if (light != template || template.enabled || visible.lightType != LightType.Point) continue;
                    Vector3 position = visible.localToWorldMatrix.GetColumn(3);
                    if ((position - template.transform.position).sqrMagnitude < 0.0001f) continue;
                    int matchedParticle = -1;
                    for (int index = 0; index < particleCount; index++)
                    {
                        if ((position - particleBuffer[index].position).sqrMagnitude < 0.0001f)
                        {
                            matchedParticle = index;
                            break;
                        }
                    }
                    if (matchedParticle < 0) continue;
                    HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                    evidence.lights.Add(new PunctualShadowParticleObservation { phase = phase, render = render, frame = Time.frameCount, slot = slot, matchedParticle = matchedParticle, templateIdentity = light.GetEntityId().ToString(), name = light.name, templateEnabled = light.enabled, intensity = light.intensity, shadows = light.shadows.ToString(), position = position, particlePosition = particleBuffer[matchedParticle].position, templatePosition = template.transform.position, descriptorColor = visible.finalColor, descriptorRange = visible.range, hasHdData = data != null, fade = data != null ? data.shadowFadeDistance : -1f, templateFade = templateData.shadowFadeDistance });
                }
            };
            try
            {
                scene = SceneManager.CreateScene("Punctual particle inheritance fixture " + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(scene);
                root = new GameObject("Punctual particle inheritance fixture") { layer = 30 };
                SceneManager.MoveGameObjectToScene(root, scene);
                var cameraObject = new GameObject("Particle inheritance camera") { layer = root.layer };
                cameraObject.transform.SetParent(root.transform, false);
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = true;
                camera.scene = scene;
                camera.cullingMask = 1 << root.layer;
                camera.useOcclusionCulling = false;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                var cameraData = cameraObject.AddComponent<HDAdditionalCameraData>();
                cameraData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                cameraData.backgroundColorHDR = Color.black;
                cameraData.volumeLayerMask = 1 << root.layer;
                target = new RenderTexture(64, 64, 24);
                target.Create();
                camera.targetTexture = target;
                var controlObject = new GameObject("Punctual particle culling control") { layer = root.layer };
                controlObject.transform.SetParent(root.transform, false);
                controlObject.transform.position = new Vector3(0f, 1f, 5f);
                control = controlObject.AddComponent<Light>();
                control.type = LightType.Point;
                control.range = 8f;
                control.cullingMask = 1 << root.layer;
                controlObject.AddComponent<HDAdditionalLightData>();
                control.intensity = 1000f;
                control.lightUnit = LightUnit.Candela;
                var templateObject = new GameObject("Punctual particle template") { layer = root.layer };
                templateObject.transform.SetParent(root.transform, false);
                template = templateObject.AddComponent<Light>();
                template.type = LightType.Point;
                template.range = 8f;
                template.intensity = 1000f;
                template.shadows = LightShadows.Soft;
                template.cullingMask = 1 << root.layer;
                template.enabled = false;
                templateData = templateObject.AddComponent<HDAdditionalLightData>();
                templateData.shadowFadeDistance = 10000f;
                templateData.fadeDistance = 10000f;
                templateData.lightDimmer = 1f;
                templateData.shadowDimmer = 1f;
                templateData.volumetricDimmer = 0f;
                configuration = ScriptableObject.CreateInstance<GameConfig>();
                policy = templateObject.AddComponent<PunctualShadowDistance>();
                policy.ApplyConfiguration(configuration);
                Assert.That(templateData.shadowFadeDistance, Is.EqualTo(75f));
                Assert.That(template.enabled, Is.False);
                var particleObject = new GameObject("Punctual particle emitter") { layer = root.layer };
                particleObject.transform.SetParent(root.transform, false);
                particleObject.transform.position = new Vector3(0f, 0f, 5f);
                particles = particleObject.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.useAutoRandomSeed = false;
                particles.randomSeed = 1;
                var main = particles.main;
                main.playOnAwake = false;
                main.loop = false;
                main.startLifetime = 60f;
                main.startSpeed = 0f;
                main.startSize = 1f;
                main.startColor = Color.white;
                main.maxParticles = 4;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var emission = particles.emission;
                emission.enabled = false;
                var shape = particles.shape;
                shape.enabled = false;
                var lights = particles.lights;
                lights.enabled = true;
                lights.light = template;
                lights.ratio = 1f;
                lights.maxLights = 4;
                lights.useRandomDistribution = false;
                lights.useParticleColor = false;
                lights.sizeAffectsRange = false;
                lights.alphaAffectsIntensity = false;
                lights.range = new ParticleSystem.MinMaxCurve(1f);
                lights.intensity = new ParticleSystem.MinMaxCurve(1f);
                material = new Material(Shader.Find("HDRP/Unlit"));
                particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
                particles.Play(false);
                particles.Emit(new ParticleSystem.EmitParams { position = new Vector3(-0.5f, 0f, 5f), startLifetime = 60f, startSize = 1f, startColor = Color.white }, 1);
                particles.Emit(new ParticleSystem.EmitParams { position = new Vector3(0.5f, 0f, 5f), startLifetime = 60f, startSize = 1f, startColor = Color.white }, 1);
                RenderPipelineManager.endCameraRendering += observe;
                for (render = 0; render < 8; render++)
                {
                    yield return null;
                }
                evidence.particleCount = particles.particleCount;
                evidence.sourceIsPlaying = particles.isPlaying;
                evidence.sourceIsPaused = particles.isPaused;
                evidence.sourceIsEmitting = particles.isEmitting;
                evidence.sourceLightsModuleEnabled = particles.lights.enabled;
                evidence.sourceLightTemplateEnabled = template.enabled;
                int initialLights = Count(evidence, "initial75");
                evidence.initialGeneratedObservations = initialLights;
                Assert.That(particles.particleCount, Is.EqualTo(2));
                Assert.That(evidence.controlVisibleObservations, Is.GreaterThan(0), "The public culling snapshot must observe the ordinary control light.");
                AssertPhase(evidence, "initial75", 75f);
                phase = "configured120Pending";
                typeof(GameConfig).GetField("_punctualShadowDistance", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(configuration, 120f);
                typeof(GameConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(configuration, null);
                yield return null;
                yield return null;
                phase = "configured120";
                for (render = 8; render < 12; render++)
                {
                    yield return null;
                }
                AssertPhase(evidence, "configured120", 120f);
                phase = "disabledPending";
                typeof(GameConfig).GetField("_punctualShadowDistanceEnabled", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(configuration, false);
                typeof(GameConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(configuration, null);
                yield return null;
                yield return null;
                phase = "disabled";
                for (render = 12; render < 16; render++)
                {
                    yield return null;
                }
                AssertPhase(evidence, "disabled", 10000f);
                evidence.particleCount = particles.particleCount;
                evidence.initialGeneratedObservations = initialLights;
                evidence.templateFadeAfterDisable = templateData.shadowFadeDistance;
                phase = "particleLightsOffSettling";
                lights.enabled = false;
                yield return null;
                yield return null;
                phase = "particleLightsOff";
                int controlBefore = evidence.controlVisibleObservations;
                for (render = 16; render < 20; render++)
                {
                    yield return null;
                }
                evidence.generatedObservationsAfterModuleDisable = Count(evidence, phase);
                evidence.controlObservationsAfterModuleDisable = evidence.controlVisibleObservations - controlBefore;
                evidence.particleCountAfterModuleDisable = particles.particleCount;
                Assert.That(evidence.generatedObservationsAfterModuleDisable, Is.EqualTo(0), "Disabling the particle Lights module must remove the per-particle visible-light descriptors.");
                Assert.That(evidence.controlObservationsAfterModuleDisable, Is.GreaterThan(0), "The ordinary control must remain visible after the particle Lights module is disabled.");
                Assert.That(evidence.particleCountAfterModuleDisable, Is.EqualTo(2));
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= observe;
                File.WriteAllText(Path.Combine(output, "particle-inheritance.json"), JsonUtility.ToJson(evidence, true));
                if (policy != null) policy.Release();
                if (camera != null) camera.targetTexture = null;
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                if (configuration != null) UnityEngine.Object.DestroyImmediate(configuration);
                if (initialScene.IsValid() && initialScene.isLoaded) SceneManager.SetActiveScene(initialScene);
                if (scene.IsValid() && scene.isLoaded) _unload = SceneManager.UnloadSceneAsync(scene);
            }
        }

        private static int Count(PunctualShadowParticleEvidence evidence, string phase)
        {
            int count = 0;
            foreach (PunctualShadowParticleObservation observation in evidence.lights) if (observation.phase == phase) count++;
            return count;
        }

        private static void AssertPhase(PunctualShadowParticleEvidence evidence, string phase, float expectedFade)
        {
            Assert.That(Count(evidence, phase), Is.GreaterThan(0), phase + " must observe real per-particle visible-light descriptors.");
            foreach (PunctualShadowParticleObservation observation in evidence.lights)
            {
                if (observation.phase != phase) continue;
                Assert.That(observation.templateEnabled, Is.False);
                Assert.That(observation.hasHdData, Is.True, "HDRP must consume data from the descriptor's Light reference.");
                Assert.That(observation.fade, Is.EqualTo(expectedFade));
                Assert.That(observation.shadows, Is.EqualTo(LightShadows.Soft.ToString()));
                Assert.That(observation.descriptorColor.maxColorComponent, Is.GreaterThan(0f));
                Assert.That((observation.position - observation.particlePosition).sqrMagnitude, Is.LessThan(0.0001f));
                Assert.That((observation.position - observation.templatePosition).sqrMagnitude, Is.GreaterThanOrEqualTo(0.0001f));
            }
        }
    }
}
