using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class NativeFogPreviewSelectionTests
    {
        [Test]
        public void SwitchingFromControllerInspectorToFogVolumeKeepsWeatherPreviewRunning()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) Assert.Ignore("Inspector preview lifetime requires Edit Mode.");
            XRayWeatherController controller = XRayWeatherController.Active;
            bool existingPreview = controller != null;
            if (existingPreview && (XRayWeatherPreview.Controller != controller || !controller.PreviewActive)) Assert.Ignore("This fixture preserves weather ownership outside the Editor preview manager.");
            if (!existingPreview && XRayWeatherPreview.Controller != null) Assert.Ignore("This fixture preserves an existing preview manager state.");
            if (controller == null) controller = ExistingController();
            if (controller != null && (!controller.isActiveAndEnabled || controller.IsTransitioning || controller.GetComponent<XRayWeatherFogRenderer>() == null)) Assert.Ignore("The authored controller must be enabled, idle and already connected to native fog.");
            XRayWeatherProfile savedProfile = controller != null ? controller.ActiveProfile : null;
            double savedTime = controller != null ? controller.GameTimeSeconds : 0.0;
            bool savedPaused = controller != null && controller.Paused;
            float savedTimeScale = controller != null ? controller.TimeScale : 1.0f;
            Scene fixture = default;
            XRayWeatherProfile profile = null;
            UnityEditor.Editor controllerEditor = null;
            UnityEditor.Editor volumeEditor = null;
            bool startedPreview = false;
            try
            {
                if (controller == null)
                {
                    fixture = EditorSceneManager.NewPreviewScene();
                    var root = new GameObject("Native fog Inspector selection fixture");
                    SceneManager.MoveGameObjectToScene(root, fixture);
                    profile = Profile();
                    controller = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
                }
                controllerEditor = UnityEditor.Editor.CreateEditor(controller);
                Assert.IsInstanceOf<XRayWeatherControllerEditor>(controllerEditor);
                if (!existingPreview)
                {
                    startedPreview = XRayWeatherPreview.Start(controller);
                    Assert.IsTrue(startedPreview, controller.Diagnostic);
                }
                XRayWeatherFogRenderer renderer = controller.GetComponent<XRayWeatherFogRenderer>();
                Assert.IsNotNull(renderer);
                Volume volume = renderer.Volume;
                Assert.IsNotNull(volume);
                Assert.IsTrue(volume.HasInstantiatedProfile());
                VolumeProfile runtimeProfile = volume.profile;
                AssertPreview(controller, renderer, volume, runtimeProfile);
                Object.DestroyImmediate(controllerEditor);
                controllerEditor = null;
                volumeEditor = UnityEditor.Editor.CreateEditor(volume);
                Assert.IsNotNull(volumeEditor);
                AssertPreview(controller, renderer, volume, runtimeProfile);
                Object.DestroyImmediate(volumeEditor);
                volumeEditor = null;
                controllerEditor = UnityEditor.Editor.CreateEditor(controller);
                Object.DestroyImmediate(controllerEditor);
                controllerEditor = null;
                AssertPreview(controller, renderer, volume, runtimeProfile);
            }
            finally
            {
                if (controllerEditor != null) Object.DestroyImmediate(controllerEditor);
                if (volumeEditor != null) Object.DestroyImmediate(volumeEditor);
                if (startedPreview)
                {
                    XRayWeatherPreview.Stop();
                    if (!fixture.IsValid() && controller != null)
                    {
                        controller.SetProfile(savedProfile);
                        controller.Seek(savedTime);
                        controller.SetPaused(savedPaused);
                        controller.SetTimeScale(savedTimeScale);
                    }
                }
                if (fixture.IsValid() && fixture.isLoaded) EditorSceneManager.ClosePreviewScene(fixture);
                if (profile != null) Object.DestroyImmediate(profile);
            }
        }

        private static XRayWeatherController ExistingController()
        {
            XRayWeatherController found = null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (XRayWeatherController controller in root.GetComponentsInChildren<XRayWeatherController>(true))
                    {
                        if (found != null) Assert.Ignore("This fixture preserves multiple authored weather controllers.");
                        found = controller;
                    }
                }
            }
            return found;
        }

        private static void AssertPreview(XRayWeatherController controller, XRayWeatherFogRenderer renderer, Volume volume, VolumeProfile profile)
        {
            Assert.AreSame(controller, XRayWeatherController.Active);
            Assert.AreSame(controller, XRayWeatherPreview.Controller);
            Assert.IsTrue(controller.PreviewActive);
            Assert.IsTrue(renderer.HasWeather);
            Assert.IsTrue(volume.HasInstantiatedProfile());
            Assert.AreSame(profile, volume.profile);
            Assert.IsTrue(profile.TryGet(out Fog fog));
            Assert.IsTrue(fog.enabled.value);
            Assert.IsTrue(fog.enabled.overrideState);
        }

        private static XRayWeatherProfile Profile()
        {
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            var first = new XRayWeatherSnapshot("00:00:00", 0.0, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.01f, 0.0f, Color.white, Color.gray, 0.2f, 300.0f, 400.0f, Color.gray, Color.gray, 0.0f, default, default);
            var second = new XRayWeatherSnapshot("12:00:00", 43200.0, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.01f, 0.0f, Color.white, Color.gray, 0.8f, 150.0f, 250.0f, Color.gray, Color.gray, 0.0f, default, default);
            profile.Initialize("native_fog_selection", "analytic_native_fog_selection", "Inspector lifetime regression fixture", new[] { first, second }, new Vector2[24], Vector4.one, null, null, Vector3.one);
            return profile;
        }
    }
}
