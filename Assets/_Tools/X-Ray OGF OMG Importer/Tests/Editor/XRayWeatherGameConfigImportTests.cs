using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Vfs;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherGameConfigImportTests
    {
        private const string ARCHIVE_ROOT = "E:/Projects/stalker_raid_into_darkness_Archive/";
        private const string FIXTURE_ROOT = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/";

        [Test]
        public void AutomaticWeatherReusesPreparedSharedAssets()
        {
            GameConfig config = GameConfig.Load();
            var record = new XRayLevelImportRecord();
            XRayWeatherProfile profile = XRayWeatherImporter.ResolveLevelProfile(new XRayLevelImportSettings(), null, "Assets/UnusedGameConfigFixture", record, out XRayWeatherCatalog catalog);
            Assert.AreSame(config.DefaultWeather, profile);
            Assert.AreSame(config.OrdinaryWeatherCatalog, catalog);
            Assert.AreEqual(21, catalog.Profiles.Count);
            Assert.AreEqual(7, config.WeatherFxCatalog.Effects.Count);
            Assert.IsEmpty(record.GeneratedAssets);
            Assert.AreEqual(3, record.Dependencies.Count);
            foreach (XRayLevelDependency dependency in record.Dependencies) Assert.AreEqual(XRayLevelDependencyStatus.Reused.ToString(), dependency.Status);
        }

        [Test]
        public void MissingLinkRepairPreservesAuthoredOverridesAndRepeatsWithoutChanges()
        {
            if (XRayWeatherPreview.Controller != null) Assert.Ignore("The fixture preserves the active user weather preview.");
            string folder = FIXTURE_ROOT + "GameConfigRepair_" + Guid.NewGuid().ToString("N");
            PrepareSavedHost(folder);
            Scene host = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(host);
            var authored = ScriptableObject.CreateInstance<VolumeProfile>();
            var authoredFog = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                XRayLevelPaths.EnsureFolder(folder);
                var root = new GameObject("GameConfig repair fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                Volume volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10.0f;
                volume.sharedProfile = authored;
                authored.Add<GradientSky>(true).exposure.Override(XRayLevelSceneBuilder.SKY_EXPOSURE);
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, GameConfig.Load().DefaultWeather, false);
                XRayWeatherFogRenderer generatedFog = controller.GetComponent<XRayWeatherFogRenderer>();
                Volume authoredFogVolume = generatedFog.Volume;
                authoredFogVolume.sharedProfile = authoredFog;
                authoredFogVolume.priority = 123.0f;
                authoredFog.Add<Fog>(true).meanFreePath.Override(789.0f);
                Object.DestroyImmediate(generatedFog);
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("_levelOverrides").intValue = (int)(XRayWeatherLevelOverrides.TimeScale | XRayWeatherLevelOverrides.LensFlare);
                serialized.FindProperty("_timeScale").floatValue = 3.0f;
                serialized.FindProperty("_lensFlareEnabled").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                controller.ApplySettings();
                Object wind = serialized.FindProperty("_windProvider").objectReferenceValue;
                Assert.IsTrue(XRayLevelSceneBuilder.ConfigureMissingGameWeather(root, folder + "/Sky.asset"));
                XRayWeatherSkyRenderer generated = controller.GetComponent<XRayWeatherSkyRenderer>();
                var generatedBindings = new SerializedObject(generated);
                var skyVolume = generatedBindings.FindProperty("_volume").objectReferenceValue as Volume;
                var external = new GameObject("External Authored Sky Renderer");
                external.transform.SetParent(root.transform, false);
                XRayWeatherSkyRenderer authoredRenderer = external.AddComponent<XRayWeatherSkyRenderer>();
                authoredRenderer.Configure(skyVolume, Shader.Find("XRay/Monolith Weather Sky"));
                controller.ConfigureSky(authoredRenderer);
                Object.DestroyImmediate(generated);
                string before = EditorJsonUtility.ToJson(controller);
                int components = root.GetComponentsInChildren<Component>(true).Length;
                Assert.IsTrue(XRayLevelSceneBuilder.ConfigureMissingGameWeather(root, folder + "/Sky.asset"));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(controller));
                Assert.AreEqual(components, root.GetComponentsInChildren<Component>(true).Length);
                Assert.AreEqual(3.0f, controller.TimeScale);
                Assert.IsFalse(controller.LensFlareEnabled);
                Assert.AreSame(authoredFog, authoredFogVolume.sharedProfile);
                Assert.AreEqual(123.0f, authoredFogVolume.priority);
                Assert.IsTrue(authoredFog.TryGet(out Fog retainedFog));
                Assert.AreEqual(789.0f, retainedFog.meanFreePath.value);
                serialized.Update();
                Assert.AreSame(wind, serialized.FindProperty("_windProvider").objectReferenceValue);
                Assert.AreSame(authoredRenderer, serialized.FindProperty("_skyRenderer").objectReferenceValue);
                Assert.AreEqual(1, root.GetComponentsInChildren<XRayWeatherController>(true).Length);
                Assert.AreEqual(1, root.GetComponentsInChildren<XRayWeatherSkyRenderer>(true).Length);
                if (XRayWeatherController.Active == null)
                {
                    Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                    controller.SetTimeScale(9.0f);
                    try
                    {
                        Assert.Throws<InvalidOperationException>(() => XRayLevelSceneBuilder.ConfigureMissingGameWeather(root, folder + "/Sky.asset"));
                        Assert.IsTrue(controller.PreviewActive);
                        Assert.AreSame(controller, XRayWeatherController.Active);
                        Assert.AreEqual(9.0f, controller.TimeScale);
                    }
                    finally
                    {
                        controller.EndPreview();
                    }
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                SceneManager.SetActiveScene(host);
                foreach (VolumeComponent component in authored.components) Object.DestroyImmediate(component);
                foreach (VolumeComponent component in authoredFog.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(authored);
                Object.DestroyImmediate(authoredFog);
                if (AssetDatabase.IsValidFolder(folder)) Assert.IsTrue(AssetDatabase.DeleteAsset(folder));
            }
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator MinimizedRealSourceImportsInheritsAndReloads()
        {
            string hostFolder = FIXTURE_ROOT + "GameConfigImportHost_" + Guid.NewGuid().ToString("N");
            bool ownsHost = PrepareSavedHost(hostFolder);
            string output = Output("MinimizedRealImport");
            try
            {
                using (var fixture = new XRayFloraLevelFixture(false, Path.Combine(output, "Sources")))
                {
                    XRayLevelImportRequest request = fixture.Request(XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraWindImportMode.EnableVerifiedAnomaly);
                    yield return ImportAndValidate(request, output);
                    byte[] scene = File.ReadAllBytes(ScenePath(request));
                    yield return ImportAndValidate(request, output);
                    CollectionAssert.AreEqual(scene, File.ReadAllBytes(ScenePath(request)));
                }
            }
            finally
            {
                if (ownsHost && AssetDatabase.IsValidFolder(hostFolder)) Assert.IsTrue(AssetDatabase.DeleteAsset(hostFolder));
            }
        }

        [UnityTest]
        [Explicit]
        [Timeout(1800000)]
        public IEnumerator FullRealEscapeImportsThroughProductionPipeline()
        {
            string root = FIXTURE_ROOT + "GameConfigRealImport_" + Guid.NewGuid().ToString("N");
            PrepareSavedHost(root);
            string output = Output("FullRealEscapeImport");
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "levels/l01_escape", "textures", "textures.ltx", "shaders", "shaders.xr", "gamemtl.xr", "configs" });
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
            var request = new XRayLevelImportRequest
            {
                LevelName = "l01_escape",
                SourcePlan = plan,
                OutputRoot = root,
                Identification = "Isolated full real level GameConfig integration validation",
                Settings = new XRayLevelImportSettings { ImportCollision = false, ImportDetails = false, CopySourceFiles = false, OpenSceneAfterImport = false, StaticLightShadows = false }
            };
            try
            {
                yield return ImportAndValidate(request, output);
            }
            finally
            {
                if (XRayLevelImportJob.Current == null && AssetDatabase.IsValidFolder(root)) Assert.IsTrue(AssetDatabase.DeleteAsset(root));
            }
        }

        private static IEnumerator ImportAndValidate(XRayLevelImportRequest request, string output)
        {
            output = Path.Combine(output, "Run_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            Assert.IsTrue(XRayLevelImportJob.Enqueue(request));
            while (XRayLevelImportJob.Current != null) yield return null;
            XRayLevelImportRecord record = XRayLevelImportJob.LastRecord;
            File.WriteAllText(Path.Combine(output, "ImportManifest.json"), JsonUtility.ToJson(record, true));
            Assert.AreEqual(XRayLevelJobState.Completed, XRayLevelImportJob.LastState, string.Join("; ", record.Errors));
            GameConfig config = GameConfig.Load();
            string configBefore = EditorJsonUtility.ToJson(config);
            string path = ScenePath(request);
            byte[] saved = File.ReadAllBytes(path);
            Scene host = SceneManager.GetActiveScene();
            for (int pass = 0; pass < 2; pass++)
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(host);
                try
                {
                    GameObject root = Array.Find(scene.GetRootGameObjects(), item => item.name == request.LevelName);
                    Assert.IsNotNull(root);
                    XRayWeatherController controller = root.GetComponentInChildren<XRayWeatherController>(true);
                    Assert.IsNotNull(controller);
                    controller.ApplySettings();
                    Assert.AreSame(config, controller.Configuration);
                    Assert.AreSame(config.OrdinaryWeatherCatalog, controller.Catalog);
                    Assert.AreSame(config.WeatherFxCatalog, controller.FxCatalog);
                    Assert.AreEqual(7.0f, controller.TimeScale);
                    Assert.AreEqual(XRayWeatherLevelOverrides.None, controller.LevelOverrides);
                    var serialized = new SerializedObject(controller);
                    Assert.IsNull(serialized.FindProperty("_profile").objectReferenceValue);
                    Assert.IsNull(serialized.FindProperty("_catalog").objectReferenceValue);
                    Assert.IsNull(serialized.FindProperty("_fxCatalog").objectReferenceValue);
                    var fog = root.GetComponentInChildren<XRayWeatherFogRenderer>(true);
                    Assert.IsNotNull(fog);
                    Assert.IsNotNull(fog.Volume);
                    XRayWeatherSkyRenderer sky = root.GetComponentInChildren<XRayWeatherSkyRenderer>(true);
                    Assert.IsNotNull(sky);
                    Camera camera = null;
                    foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                    {
                        if (sceneRoot.TryGetComponent(out Camera candidate)) camera = candidate;
                    }
                    Assert.IsNotNull(camera);
                    HDAdditionalCameraData cameraData = camera.GetComponent<HDAdditionalCameraData>();
                    Assert.IsNotNull(cameraData);
                    foreach (Volume volume in root.GetComponentsInChildren<Volume>(true)) Assert.AreNotEqual(0, cameraData.volumeLayerMask.value & (1 << volume.gameObject.layer));
                    Assert.IsNotNull(GraphicsSettings.GetRenderPipelineSettings<HDRPDefaultVolumeProfileSettings>()?.volumeProfile);
                    Assert.AreEqual(1, root.GetComponentsInChildren<XRayWeatherController>(true).Length);
                    int directionals = 0;
                    foreach (Light light in root.GetComponentsInChildren<Light>(true))
                    {
                        if (light.type == LightType.Directional) directionals++;
                    }
                    Assert.AreEqual(1, directionals);
                    string beforeRepair = EditorJsonUtility.ToJson(controller);
                    int count = root.GetComponentsInChildren<Component>(true).Length;
                    Assert.IsTrue(XRayLevelSceneBuilder.ConfigureMissingGameWeather(root, Path.ChangeExtension(path, null) + "_WeatherSky.asset"));
                    Assert.AreEqual(beforeRepair, EditorJsonUtility.ToJson(controller));
                    Assert.AreEqual(count, root.GetComponentsInChildren<Component>(true).Length);
                    var skyBindings = new SerializedObject(sky);
                    var skyVolume = skyBindings.FindProperty("_volume").objectReferenceValue as Volume;
                    Assert.IsNotNull(skyVolume);
                    VolumeProfile authoredSky = skyVolume.sharedProfile;
                    VolumeProfile authoredFog = fog.Volume.sharedProfile;
                    string skyBefore = EditorJsonUtility.ToJson(authoredSky);
                    string fogBefore = EditorJsonUtility.ToJson(authoredFog);
                    int originalMask = cameraData.volumeLayerMask.value;
                    Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                    try
                    {
                        controller.TickPreview(0.0f);
                        Assert.AreSame(controller, XRayWeatherController.Active);
                        Assert.AreSame(config.DefaultWeather, controller.ActiveProfile);
                        Assert.AreEqual(7.0f, controller.TimeScale);
                        Assert.IsTrue(sky.HasWeather, sky.Diagnostic);
                        Assert.IsTrue(fog.HasWeather, fog.Diagnostic);
                        Assert.IsNotNull(controller.Celestial);
                        Assert.AreEqual(config.FogEnabled, controller.FogEnabled);
                        Assert.AreEqual(config.SunShaftsEnabled, controller.SunShaftsEnabled);
                        Assert.AreEqual(config.LensFlareEnabled, controller.LensFlareEnabled);
                        Assert.AreEqual(config.ExpressiveDaylight, controller.ExpressiveDaylight);
                        Assert.AreEqual(config.LensFlareIntensity, controller.LensFlareIntensity);
                        Assert.AreEqual(config.AdaptLensFlareToWeather, controller.AdaptLensFlareToWeather);
                        Assert.AreEqual(config.ExpressiveFlareIntensity, controller.ExpressiveFlareIntensity);
                        Assert.AreEqual(config.FxLightingAdaptation, controller.FxLightingAdaptationGain);
                        Assert.IsTrue(VolumeManager.instance.isInitialized);
                        VolumeStack stack = HDCamera.GetOrCreate(camera).volumeStack;
                        VolumeManager.instance.Update(stack, camera.transform, cameraData.volumeLayerMask);
                        Assert.AreEqual(XRayWeatherSkySettings.SKY_ID, stack.GetComponent<VisualEnvironment>().skyType.value);
                        Assert.IsTrue(stack.GetComponent<Fog>().enabled.value);
                        Assert.AreEqual(fog.MeanFreePath, stack.GetComponent<Fog>().meanFreePath.value);
                        Assert.AreEqual(originalMask, cameraData.volumeLayerMask.value);
                        File.WriteAllLines(Path.Combine(output, "Effective_" + pass + ".csv"), new[] { "Check,Value", "SkyOwned," + sky.HasWeather, "FogOwned," + fog.HasWeather, "CelestialReady," + (controller.Celestial != null), "SkyType," + stack.GetComponent<VisualEnvironment>().skyType.value, "FogEnabled," + stack.GetComponent<Fog>().enabled.value, FormattableString.Invariant($"FogMeanFreePath,{fog.MeanFreePath:R}"), FormattableString.Invariant($"TimeScale,{controller.TimeScale:R}"), "CameraVolumeMask," + originalMask });
                    }
                    finally
                    {
                        controller.EndPreview();
                    }
                    Assert.IsFalse(sky.HasWeather);
                    Assert.IsFalse(fog.HasWeather);
                    Assert.AreSame(authoredSky, skyVolume.sharedProfile);
                    Assert.AreSame(authoredFog, fog.Volume.sharedProfile);
                    Assert.AreEqual(skyBefore, EditorJsonUtility.ToJson(authoredSky));
                    Assert.AreEqual(fogBefore, EditorJsonUtility.ToJson(authoredFog));
                    Assert.AreEqual(originalMask, cameraData.volumeLayerMask.value);
                    CollectionAssert.AreEqual(saved, File.ReadAllBytes(path));
                    Assert.AreEqual(configBefore, EditorJsonUtility.ToJson(config));
                    File.WriteAllText(Path.Combine(output, "Reload_" + pass + ".json"), beforeRepair);
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                    SceneManager.SetActiveScene(host);
                }
            }
        }

        private static string ScenePath(XRayLevelImportRequest request)
        {
            return request.OutputRoot + "/" + request.LevelName + "/Scenes/" + request.LevelName + ".unity";
        }

        private static bool PrepareSavedHost(string folder)
        {
            if (XRayLevelImportJob.Current != null || XRayWeatherPreview.Controller != null || XRayWeatherController.Active != null && XRayWeatherController.Active.PreviewActive) Assert.Ignore("Run import validation with no active import or user weather preview.");
            Scene host = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(host.path)) return false;
            if (!XRayWeatherFogIntegrationTests.IsRunnerBootstrap(host)) Assert.Ignore("A saved host or verified clean running Test Runner bootstrap is required. The untitled user scene was preserved.");
            XRayLevelPaths.EnsureFolder(folder);
            Assert.IsTrue(EditorSceneManager.SaveScene(host, folder + "/Host.unity"));
            return true;
        }

        private static string Output(string name)
        {
            string parent = Environment.GetEnvironmentVariable("GAME_CONFIG_VALIDATION_OUTPUT");
            if (string.IsNullOrWhiteSpace(parent)) parent = ARCHIVE_ROOT + "GameConfigIntegrationTests";
            if (!Path.GetFullPath(parent).Replace('\\', '/').StartsWith(ARCHIVE_ROOT, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("GameConfig validation outputs must be inside the canonical Archive directory.");
            string output = Path.Combine(parent, name + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            TestContext.WriteLine("GameConfig validation output " + output);
            return output;
        }
    }
}
