using System;
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
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherFogIntegrationTests
    {
        [Test]
        public void BuilderConnectsOneSerializedFogConsumerIdempotently()
        {
            if (XRayWeatherController.Active != null) Assert.Ignore("This isolated fixture preserves an existing weather owner.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWeatherProfile profile = Profile();
            try
            {
                var root = new GameObject("Weather fog integration fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
                XRayWeatherFogRenderer renderer = controller.GetComponent<XRayWeatherFogRenderer>();
                Assert.IsNotNull(renderer);
                string authored = EditorJsonUtility.ToJson(renderer);
                Assert.AreSame(controller, XRayLevelSceneBuilder.ConfigureWeather(root, profile));
                Assert.AreEqual(1, root.GetComponentsInChildren<XRayWeatherFogRenderer>(true).Length);
                Assert.AreEqual(authored, EditorJsonUtility.ToJson(renderer));
                Assert.AreSame(renderer, new SerializedObject(controller).FindProperty("_fogRenderer").objectReferenceValue);
                var volume = new SerializedObject(renderer).FindProperty("_volume").objectReferenceValue as Volume;
                Assert.IsNotNull(volume);
                Assert.IsTrue(volume.isGlobal);
                Assert.IsTrue(volume.transform.IsChildOf(controller.transform));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(profile);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LosingFogConsumerReleasesEveryWeatherInput(bool destroy)
        {
            if (XRayWeatherController.Active != null) Assert.Ignore("This isolated fixture preserves an existing weather owner.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWeatherProfile profile = Profile();
            try
            {
                var root = new GameObject("Weather fog release fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
                XRayWeatherFogRenderer renderer = controller.GetComponent<XRayWeatherFogRenderer>();
                XRayFloraWindProvider wind = root.GetComponentInChildren<XRayFloraWindProvider>();
                Light sun = root.GetComponentInChildren<Light>();
                Color color = sun.color;
                float intensity = sun.intensity;
                Quaternion rotation = sun.transform.rotation;
                Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                Assert.IsTrue(wind.WeatherDriven);
                if (destroy) Object.DestroyImmediate(renderer);
                else renderer.enabled = false;
                controller.TickPreview(0.1f);
                Assert.IsNull(XRayWeatherController.Active);
                Assert.IsFalse(wind.WeatherDriven);
                Assert.AreEqual(color, sun.color);
                Assert.AreEqual(intensity, sun.intensity);
                Assert.That(Quaternion.Angle(rotation, sun.transform.rotation), Is.LessThan(0.0001f));
                Assert.IsTrue(controller.enabled);
                controller.EndPreview();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void OwnerConflictPreservesSerializedFogEnabledAndBindings()
        {
            if (XRayWeatherController.Active != null) Assert.Ignore("This isolated fixture preserves an existing weather owner.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWeatherProfile profile = Profile();
            try
            {
                var rootA = new GameObject("Weather fog owner A");
                SceneManager.MoveGameObjectToScene(rootA, scene);
                XRayWeatherController controllerA = XRayLevelSceneBuilder.ConfigureWeather(rootA, profile);
                Assert.IsTrue(controllerA.BeginPreview(), controllerA.Diagnostic);
                var rootB = new GameObject("Weather fog owner B");
                SceneManager.MoveGameObjectToScene(rootB, scene);
                LogAssert.Expect(LogType.Error, "Only one MonolithWeatherInputs provider can be active.");
                LogAssert.Expect(LogType.Error, "Only one MonolithSourceLighting controller can be active.");
                XRayWeatherController controllerB = XRayLevelSceneBuilder.ConfigureWeather(rootB, profile);
                XRayWeatherFogRenderer rendererA = controllerA.GetComponent<XRayWeatherFogRenderer>();
                XRayWeatherFogRenderer rendererB = controllerB.GetComponent<XRayWeatherFogRenderer>();
                string authoredA = EditorJsonUtility.ToJson(rendererA);
                string authoredB = EditorJsonUtility.ToJson(rendererB);
                Assert.IsFalse(controllerB.BeginPreview());
                Assert.AreSame(controllerA, XRayWeatherController.Active);
                Assert.AreEqual(authoredA, EditorJsonUtility.ToJson(rendererA));
                Assert.AreEqual(authoredB, EditorJsonUtility.ToJson(rendererB));
                Assert.IsTrue(rendererA.enabled);
                Assert.IsTrue(rendererB.enabled);
                controllerA.EndPreview();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ReleaseRestoresAuthoredFogProfilesParametersAndOverrideStates()
        {
            if (XRayWeatherController.Active != null) Assert.Ignore("This isolated fixture preserves an existing weather owner.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWeatherProfile profile = Profile();
            var shared = ScriptableObject.CreateInstance<VolumeProfile>();
            Fog authoredFog = shared.Add<Fog>(false);
            authoredFog.enabled.value = false;
            authoredFog.meanFreePath.Override(123.0f);
            shared.Add<Exposure>(true).fixedExposure.Override(9.0f);
            var instance = Object.Instantiate(shared);
            try
            {
                var root = new GameObject("Native weather fog profile restoration fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                Volume volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = shared;
                volume.profile = instance;
                string sharedBefore = EditorJsonUtility.ToJson(authoredFog);
                instance.TryGet(out Fog instanceFog);
                string instanceBefore = EditorJsonUtility.ToJson(instanceFog);
                XRayWeatherFogRenderer renderer = root.AddComponent<XRayWeatherFogRenderer>();
                renderer.Configure(volume);
                Assert.IsTrue(renderer.TrySetWeather(profile, profile.GetKeyframe(0)), renderer.Diagnostic);
                Assert.AreNotSame(instance, volume.profile);
                Assert.AreSame(shared, volume.sharedProfile);
                Assert.IsTrue(volume.profile.TryGet(out Fog controlled));
                Assert.IsTrue(controlled.enabled.value);
                Assert.IsTrue(controlled.enabled.overrideState);
                Assert.IsFalse(controlled.enableVolumetricFog.value);
                Assert.AreEqual(0.0f, controlled.maxFogDistance.value);
                Assert.AreEqual(renderer.MeanFreePath, controlled.meanFreePath.value);
                Assert.IsTrue(controlled.meanFreePath.overrideState);
                Assert.AreEqual(FogColorMode.SkyColor, controlled.colorMode.value);
                Assert.AreEqual(XRayWeatherFogMath.ToSkyTint(profile.GetKeyframe(0).FogColor), controlled.tint.value);
                Assert.AreEqual(sharedBefore, EditorJsonUtility.ToJson(authoredFog));
                Assert.AreEqual(instanceBefore, EditorJsonUtility.ToJson(instanceFog));
                renderer.ReleaseWeather(profile);
                Assert.IsFalse(renderer.HasWeather);
                Assert.AreSame(instance, volume.profile);
                Assert.AreSame(shared, volume.sharedProfile);
                Assert.AreEqual(sharedBefore, EditorJsonUtility.ToJson(authoredFog));
                Assert.AreEqual(instanceBefore, EditorJsonUtility.ToJson(instanceFog));
                volume.profile = null;
                Assert.IsTrue(renderer.TrySetWeather(profile, profile.GetKeyframe(1)), renderer.Diagnostic);
                renderer.enabled = false;
                Assert.IsFalse(volume.HasInstantiatedProfile());
                Assert.AreSame(shared, volume.sharedProfile);
                Assert.AreEqual(sharedBefore, EditorJsonUtility.ToJson(authoredFog));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(profile);
                foreach (VolumeComponent component in shared.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(shared);
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void MissingAuthoredVolumeUsesTransientNativeFallbackAndReleasesIt()
        {
            if (XRayWeatherController.Active != null) Assert.Ignore("This isolated fixture preserves an existing weather owner.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWeatherProfile profile = Profile();
            try
            {
                var root = new GameObject("Native weather fog legacy fallback fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                XRayWeatherFogRenderer renderer = root.AddComponent<XRayWeatherFogRenderer>();
                string before = EditorJsonUtility.ToJson(renderer);
                Assert.IsNull(renderer.Volume);
                Assert.IsTrue(renderer.TrySetWeather(profile, profile.GetKeyframe(0)), renderer.Diagnostic);
                Volume fallback = renderer.Volume;
                Assert.IsNotNull(fallback);
                Assert.IsTrue(fallback.isGlobal);
                Assert.AreEqual(HideFlags.HideAndDontSave, fallback.gameObject.hideFlags);
                Assert.AreEqual(root.layer, fallback.gameObject.layer);
                Assert.IsTrue(fallback.profile.TryGet(out Fog fog));
                Assert.IsTrue(fog.enabled.value);
                Assert.AreEqual(0.0f, fog.multipleScatteringIntensity.value);
                Assert.IsNull(new SerializedObject(renderer).FindProperty("_volume").objectReferenceValue);
                Assert.AreEqual(before, EditorJsonUtility.ToJson(renderer));
                renderer.ReleaseWeather(profile);
                Assert.IsTrue(fallback == null);
                Assert.IsNull(renderer.Volume);
                Assert.IsFalse(renderer.HasWeather);
                Assert.AreEqual(before, EditorJsonUtility.ToJson(renderer));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(profile);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SeparateCompetingVolumeResolvesWeatherInCameraStackAndRestoresAuthoredState(bool fallback)
        {
            if (XRayWeatherController.Active != null) Assert.Ignore("This isolated fixture preserves an existing weather owner.");
            Assert.IsTrue(VolumeManager.instance.isInitialized, "HDRP must initialize the actual camera Volume stack before this regression runs.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWeatherProfile weather = Profile();
            var externalProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var authoredProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            Fog externalFog = externalProfile.Add<Fog>(false);
            externalFog.enabled.Override(true);
            externalFog.meanFreePath.Override(17.0f);
            externalFog.colorMode.Override(FogColorMode.ConstantColor);
            externalFog.tint.Override(Color.red);
            authoredProfile.Add<Fog>(false);
            try
            {
                var cameraObject = new GameObject("Competing Volume fixture camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.tag = "MainCamera";
                HDAdditionalCameraData cameraData = cameraObject.AddComponent<HDAdditionalCameraData>();
                cameraData.volumeLayerMask = 1 << 29;
                int originalMask = cameraData.volumeLayerMask.value;
                VolumeStack cameraStack = HDCamera.GetOrCreate(camera).volumeStack;
                var externalObject = new GameObject("Separate external Fog Volume");
                SceneManager.MoveGameObjectToScene(externalObject, scene);
                externalObject.layer = 29;
                Volume externalVolume = externalObject.AddComponent<Volume>();
                externalVolume.isGlobal = true;
                externalVolume.priority = 1000.0f;
                externalVolume.sharedProfile = externalProfile;
                var root = new GameObject("Competing weather Fog owner");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.layer = 0;
                Volume authoredVolume = null;
                if (!fallback)
                {
                    root.layer = 29;
                    authoredVolume = root.AddComponent<Volume>();
                    authoredVolume.isGlobal = true;
                    authoredVolume.priority = float.MaxValue;
                    authoredVolume.sharedProfile = authoredProfile;
                }
                XRayWeatherFogRenderer renderer = root.AddComponent<XRayWeatherFogRenderer>();
                renderer.Configure(authoredVolume);
                string externalBefore = EditorJsonUtility.ToJson(externalVolume);
                string externalFogBefore = EditorJsonUtility.ToJson(externalFog);
                string authoredBefore = EditorJsonUtility.ToJson(authoredProfile.components[0]);
                VolumeManager.instance.Update(cameraStack, camera.transform, cameraData.volumeLayerMask);
                Assert.AreEqual(17.0f, cameraStack.GetComponent<Fog>().meanFreePath.value);
                Assert.IsTrue(renderer.TrySetWeather(weather, weather.GetKeyframe(0)), renderer.Diagnostic);
                Volume controlled = renderer.Volume;
                Assert.AreNotSame(externalVolume, controlled);
                Assert.AreEqual(29, controlled.gameObject.layer);
                Assert.That(controlled.priority, Is.GreaterThan(externalVolume.priority));
                VolumeManager.instance.Update(cameraStack, camera.transform, cameraData.volumeLayerMask);
                Fog effective = cameraStack.GetComponent<Fog>();
                Assert.IsTrue(effective.enabled.value);
                Assert.AreEqual(renderer.MeanFreePath, effective.meanFreePath.value);
                Assert.AreEqual(FogColorMode.SkyColor, effective.colorMode.value);
                Assert.AreEqual(XRayWeatherFogMath.ToSkyTint(weather.GetKeyframe(0).FogColor), effective.tint.value);
                Assert.AreEqual(originalMask, cameraData.volumeLayerMask.value);
                Assert.IsTrue(externalVolume.isActiveAndEnabled);
                renderer.ReleaseWeather(weather);
                VolumeManager.instance.Update(cameraStack, camera.transform, cameraData.volumeLayerMask);
                Assert.AreEqual(17.0f, cameraStack.GetComponent<Fog>().meanFreePath.value);
                Assert.AreEqual(FogColorMode.ConstantColor, cameraStack.GetComponent<Fog>().colorMode.value);
                Assert.AreEqual(originalMask, cameraData.volumeLayerMask.value);
                Assert.AreEqual(externalBefore, EditorJsonUtility.ToJson(externalVolume));
                Assert.AreEqual(externalFogBefore, EditorJsonUtility.ToJson(externalFog));
                Assert.AreEqual(authoredBefore, EditorJsonUtility.ToJson(authoredProfile.components[0]));
                Assert.IsFalse(externalVolume.HasInstantiatedProfile());
                if (fallback) Assert.IsTrue(controlled == null);
                else
                {
                    Assert.IsFalse(authoredVolume.HasInstantiatedProfile());
                    Assert.AreSame(authoredProfile, authoredVolume.sharedProfile);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(weather);
                foreach (VolumeComponent component in externalProfile.components) Object.DestroyImmediate(component);
                foreach (VolumeComponent component in authoredProfile.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(externalProfile);
                Object.DestroyImmediate(authoredProfile);
            }
        }

        [Test]
        public void FreshBuilderFogAndSkyUseTheSameCameraVolumeLayerWithoutChangingItsMask()
        {
            if (XRayWeatherController.Active != null) Assert.Ignore("This isolated fixture preserves an existing weather owner.");
            Assert.IsTrue(VolumeManager.instance.isInitialized, "HDRP must initialize the actual camera Volume stack before this regression runs.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWeatherProfile weather = Profile();
            var authored = ScriptableObject.CreateInstance<VolumeProfile>();
            string folder = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/WeatherLayers_" + Guid.NewGuid().ToString("N");
            try
            {
                XRayLevelPaths.EnsureFolder(folder);
                var cube = new Cubemap(1, TextureFormat.RGBA32, false);
                AssetDatabase.CreateAsset(cube, folder + "/Cube.asset");
                var skyResource = new XRayWeatherResources("fixture_sky", cube);
                var environmentResource = new XRayWeatherResources("fixture_environment", cube);
                XRayWeatherSnapshot first = weather.GetKeyframe(0).WithResources(skyResource, default, environmentResource);
                XRayWeatherSnapshot second = weather.GetKeyframe(1).WithResources(skyResource, default, environmentResource);
                weather.Initialize("camera_layers", "camera_layers_fixture", "isolated camera Volume layer regression", new[] { first, second }, new Vector2[24], Vector4.one, null, null, Vector3.one);
                var root = new GameObject("Fresh weather builder camera layer fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                authored.Add<GradientSky>(true);
                Volume environment = root.AddComponent<Volume>();
                environment.isGlobal = true;
                environment.priority = 10.0f;
                environment.sharedProfile = authored;
                var cameraObject = new GameObject("Fresh weather builder fixture camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.tag = "MainCamera";
                HDAdditionalCameraData cameraData = cameraObject.AddComponent<HDAdditionalCameraData>();
                cameraData.volumeLayerMask = 1 << 29;
                int originalMask = cameraData.volumeLayerMask.value;
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, weather);
                Assert.IsTrue(XRayLevelSceneBuilder.ConfigureWeatherSky(root, controller, weather, folder + "/Sky.asset"));
                XRayWeatherFogRenderer fog = controller.GetComponent<XRayWeatherFogRenderer>();
                Volume sky = controller.transform.Find("Source Weather Sky").GetComponent<Volume>();
                Assert.AreEqual(29, fog.Volume.gameObject.layer);
                Assert.AreEqual(fog.Volume.gameObject.layer, sky.gameObject.layer);
                Assert.AreEqual(originalMask, cameraData.volumeLayerMask.value);
                Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                VolumeStack cameraStack = HDCamera.GetOrCreate(camera).volumeStack;
                VolumeManager.instance.Update(cameraStack, camera.transform, cameraData.volumeLayerMask);
                Assert.AreEqual(XRayWeatherSkySettings.SKY_ID, cameraStack.GetComponent<VisualEnvironment>().skyType.value);
                Assert.AreEqual(fog.MeanFreePath, cameraStack.GetComponent<Fog>().meanFreePath.value);
                controller.EndPreview();
                Assert.AreEqual(originalMask, cameraData.volumeLayerMask.value);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(weather);
                foreach (VolumeComponent component in authored.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(authored);
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void SavingAndReloadingSceneBPreservesSceneAFogOwnershipAndSerializedBindings()
        {
            if (XRayWeatherController.Active != null || XRayWeatherPreview.Controller != null) Assert.Ignore("This isolated fixture preserves an existing weather preview.");
            Scene host = SceneManager.GetActiveScene();
            string originalHostPath = host.path;
            bool temporaryHost = string.IsNullOrEmpty(originalHostPath);
            if (temporaryHost && !IsRunnerBootstrap(host)) Assert.Ignore("Additive scene serialization requires a saved host or the verified clean running Test Runner bootstrap; an untitled user scene is never saved.");
            bool hostDirty = host.isDirty;
            int hostRootCount = host.rootCount;
            XRayWeatherController hostController = null;
            foreach (GameObject root in host.GetRootGameObjects())
            {
                foreach (XRayWeatherController candidate in root.GetComponentsInChildren<XRayWeatherController>(true))
                {
                    if (hostController != null) Assert.Ignore("The existing host has multiple weather controllers; this fixture preserves their configuration.");
                    hostController = candidate;
                }
            }
            if (hostController != null && (!hostController.isActiveAndEnabled || hostController.IsTransitioning || hostController.GetComponent<XRayWeatherFogRenderer>() == null)) Assert.Ignore("The existing host requires one enabled idle controller with connected fog; its authored configuration was preserved.");
            XRayWeatherProfile savedProfile = hostController != null ? hostController.ActiveProfile : null;
            double savedTime = hostController != null ? hostController.GameTimeSeconds : 0.0;
            bool savedPaused = hostController != null && hostController.Paused;
            float savedTimeScale = hostController != null ? hostController.TimeScale : 1.0f;
            bool previewStarted = false;
            string folder = "Assets/_Source/Levels/WeatherFogValidation/__NativeFogSerialization_" + Guid.NewGuid().ToString("N");
            string profilePath = folder + "/Weather.asset";
            string prefabPath = folder + "/B.prefab";
            string scenePath = folder + "/B.unity";
            string hostPath = folder + "/Host.unity";
            Scene sceneA = default;
            Scene sceneB = default;
            XRayWeatherProfile profile = null;
            try
            {
                XRayLevelPaths.EnsureFolder(folder);
                if (temporaryHost)
                {
                    Assert.IsTrue(IsRunnerBootstrap(host), "Only the current running Test Runner's clean bootstrap may be saved as a synthetic host.");
                    Assert.IsTrue(EditorSceneManager.SaveScene(host, hostPath));
                }
                profile = Profile();
                AssetDatabase.CreateAsset(profile, profilePath);
                AssetDatabase.SaveAssetIfDirty(profile);
                sceneB = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                XRayWeatherController controllerA = hostController;
                if (controllerA == null)
                {
                    sceneA = EditorSceneManager.NewPreviewScene();
                    var rootA = new GameObject("Weather fog serialization owner A");
                    SceneManager.MoveGameObjectToScene(rootA, sceneA);
                    controllerA = XRayLevelSceneBuilder.ConfigureWeather(rootA, profile);
                }
                previewStarted = XRayWeatherPreview.Start(controllerA);
                Assert.IsTrue(previewStarted, controllerA.Diagnostic);
                XRayWeatherFogRenderer fogA = controllerA.GetComponent<XRayWeatherFogRenderer>();
                controllerA.Seek(21600.0);
                controllerA.SetPaused(true);
                Vector4 parametersA = fogA.Parameters;
                var volumeA = new SerializedObject(fogA).FindProperty("_volume").objectReferenceValue as Volume;
                VolumeProfile runtimeA = volumeA.profile;
                runtimeA.TryGet(out Fog nativeA);
                string nativeStateA = EditorJsonUtility.ToJson(nativeA);
                var rootB = new GameObject("Weather fog serialization owner B");
                SceneManager.MoveGameObjectToScene(rootB, sceneB);
                if (hostController == null) ExpectConsumerConflict();
                XRayWeatherController controllerB = XRayLevelSceneBuilder.ConfigureWeather(rootB, profile);
                Assert.IsFalse(controllerB.BeginPreview());
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(rootB, prefabPath);
                AssertFogBinding(prefab, profile);
                Object.DestroyImmediate(rootB);
                if (hostController == null) ExpectConsumerConflict();
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, sceneB);
                AssertFogBinding(instance, profile);
                Assert.IsTrue(EditorSceneManager.SaveScene(sceneB, scenePath));
                AssertOwnerA(controllerA, fogA, parametersA, runtimeA, nativeStateA);
                Assert.IsTrue(EditorSceneManager.CloseScene(sceneB, true));
                sceneB = default;
                if (hostController == null) ExpectConsumerConflict();
                sceneB = EditorSceneManager.OpenPreviewScene(scenePath);
                Assert.AreEqual(1, sceneB.rootCount);
                GameObject loaded = sceneB.GetRootGameObjects()[0];
                AssertFogBinding(loaded, profile);
                Assert.IsFalse(loaded.GetComponentInChildren<XRayWeatherController>(true).BeginPreview());
                AssertOwnerA(controllerA, fogA, parametersA, runtimeA, nativeStateA);
                EditorSceneManager.ClosePreviewScene(sceneB);
                sceneB = default;
                Assert.AreEqual(host, SceneManager.GetActiveScene());
                Assert.AreEqual(temporaryHost ? hostPath : originalHostPath, host.path);
                Assert.AreEqual(hostDirty, host.isDirty);
                Assert.AreEqual(hostRootCount, host.rootCount);
            }
            finally
            {
                if (sceneB.IsValid() && sceneB.isLoaded)
                {
                    if (EditorSceneManager.IsPreviewScene(sceneB)) EditorSceneManager.ClosePreviewScene(sceneB);
                    else EditorSceneManager.CloseScene(sceneB, true);
                }
                if (previewStarted) XRayWeatherPreview.Stop();
                if (hostController != null && previewStarted)
                {
                    hostController.SetProfile(savedProfile);
                    hostController.Seek(savedTime);
                    hostController.SetPaused(savedPaused);
                    hostController.SetTimeScale(savedTimeScale);
                }
                if (sceneA.IsValid() && sceneA.isLoaded) EditorSceneManager.ClosePreviewScene(sceneA);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null) AssetDatabase.DeleteAsset(scenePath);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) AssetDatabase.DeleteAsset(prefabPath);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(hostPath) != null) AssetDatabase.DeleteAsset(hostPath);
                if (AssetDatabase.LoadAssetAtPath<XRayWeatherProfile>(profilePath) != null) AssetDatabase.DeleteAsset(profilePath);
                else if (profile != null) Object.DestroyImmediate(profile);
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
        }

        internal static bool IsRunnerBootstrap(Scene host)
        {
            if (!host.IsValid() || !host.isLoaded || host.isDirty || !string.IsNullOrEmpty(host.path) || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            Type holderType = typeof(UnityEditor.TestTools.TestRunner.Api.TestRunnerApi).Assembly.GetType("UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder");
            if (holderType == null) return false;
            foreach (Object holder in Resources.FindObjectsOfTypeAll(holderType))
            {
                var runs = holderType.GetField("TestRuns")?.GetValue(holder) as System.Collections.IEnumerable;
                if (runs == null) continue;
                foreach (object run in runs)
                {
                    if (run == null) continue;
                    Type runType = run.GetType();
                    if (runType.GetField("isRunning")?.GetValue(run) is bool running && running && runType.GetField("InitTestScene")?.GetValue(run) is Scene bootstrap && bootstrap == host) return true;
                }
            }
            return false;
        }

        private static void ExpectConsumerConflict()
        {
            LogAssert.Expect(LogType.Error, "Only one MonolithWeatherInputs provider can be active.");
            LogAssert.Expect(LogType.Error, "Only one MonolithSourceLighting controller can be active.");
        }

        private static void AssertFogBinding(GameObject root, XRayWeatherProfile profile)
        {
            Assert.IsNotNull(root);
            XRayWeatherController controller = root.GetComponentInChildren<XRayWeatherController>(true);
            XRayWeatherFogRenderer renderer = root.GetComponentInChildren<XRayWeatherFogRenderer>(true);
            Assert.IsNotNull(controller);
            Assert.IsNotNull(renderer);
            Assert.AreEqual(1, root.GetComponentsInChildren<XRayWeatherFogRenderer>(true).Length);
            Assert.AreSame(profile, new SerializedObject(controller).FindProperty("_profile").objectReferenceValue);
            Assert.AreSame(renderer, new SerializedObject(controller).FindProperty("_fogRenderer").objectReferenceValue);
            var volume = new SerializedObject(renderer).FindProperty("_volume").objectReferenceValue as Volume;
            Assert.IsNotNull(volume);
            Assert.IsTrue(volume.isGlobal);
            Assert.IsTrue(volume.transform.IsChildOf(controller.transform));
            Assert.IsTrue(controller.enabled);
            Assert.IsTrue(renderer.enabled);
        }

        private static void AssertOwnerA(XRayWeatherController controller, XRayWeatherFogRenderer renderer, Vector4 parameters, VolumeProfile runtime, string nativeState)
        {
            Assert.AreSame(controller, XRayWeatherController.Active);
            Assert.AreSame(controller, XRayWeatherPreview.Controller);
            Assert.IsTrue(controller.PreviewActive);
            Assert.IsTrue(renderer.HasWeather);
            Assert.AreEqual(parameters, renderer.Parameters);
            var volume = new SerializedObject(renderer).FindProperty("_volume").objectReferenceValue as Volume;
            Assert.AreSame(runtime, volume.profile);
            Assert.IsTrue(runtime.TryGet(out Fog fog));
            Assert.AreEqual(nativeState, EditorJsonUtility.ToJson(fog));
        }

        private static XRayWeatherProfile Profile()
        {
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            var first = new XRayWeatherSnapshot("00:00:00", 0.0, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.01f, 0.0f, Color.white, Color.gray, 0.2f, 300.0f, 400.0f, Color.gray, Color.gray, 0.0f, default, default);
            var second = new XRayWeatherSnapshot("12:00:00", 43200.0, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.01f, 0.0f, Color.white, Color.gray, 0.8f, 150.0f, 250.0f, Color.gray, Color.gray, 0.0f, default, default);
            var positions = new Vector2[24];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(0.0f, -45.0f);
            profile.Initialize("fog_integration", "analytic_fog_integration", "isolated fog ownership fixture", new[] { first, second }, positions, Vector4.one, null, null, Vector3.one);
            return profile;
        }
    }
}
