using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherEditorTests
    {
        [Test]
        public void WeatherConnectionIsOptInAndIdempotent()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Weather connection fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            XRayWeatherProfile profile = Profile();
            try
            {
                Assert.IsNull(new XRayLevelImportSettings().WeatherProfile);
                Assert.IsNull(new XRayLevelSceneContent().WeatherProfile);
                XRayWeatherController first = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
                XRayWeatherController second = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
                Assert.AreSame(first, second);
                Assert.AreEqual(1, root.GetComponentsInChildren<XRayWeatherController>(true).Length);
                Assert.AreEqual(1, root.GetComponentsInChildren<MonolithWeatherInputs>(true).Length);
                Assert.AreEqual(1, root.GetComponentsInChildren<MonolithSourceLighting>(true).Length);
                Assert.AreEqual(1, root.GetComponentsInChildren<XRayFloraWindProvider>(true).Length);
                Assert.AreEqual(1, root.GetComponentsInChildren<Light>(true).Length);
                var serialized = new SerializedObject(first);
                Assert.AreSame(profile, serialized.FindProperty("_profile").objectReferenceValue);
                Assert.AreSame(root.GetComponentInChildren<MonolithWeatherInputs>(), serialized.FindProperty("_weatherInputs").objectReferenceValue);
                Assert.AreSame(root.GetComponentInChildren<MonolithSourceLighting>(), serialized.FindProperty("_sourceLighting").objectReferenceValue);
                Assert.AreSame(root.GetComponentInChildren<XRayFloraWindProvider>(), serialized.FindProperty("_windProvider").objectReferenceValue);
                Assert.AreSame(root.GetComponentInChildren<Light>(), serialized.FindProperty("_sun").objectReferenceValue);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void InvalidProfileIsRejectedBeforeCreatingConsumers()
        {
            var root = new GameObject("Invalid weather connection fixture");
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            try
            {
                Assert.Throws<ArgumentException>(() => XRayLevelSceneBuilder.ConfigureWeather(root, profile));
                Assert.AreEqual(0, root.transform.childCount);
                Assert.AreEqual(0, root.GetComponentsInChildren<XRayWeatherController>(true).Length);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PreviewClockRebasesAndRestoresWithoutSerializingTicks()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Weather preview fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            XRayWeatherProfile profile = Profile();
            try
            {
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("_timeScale").floatValue = 60.0f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                controller.ApplySettings();
                XRayFloraWindProvider wind = root.GetComponentInChildren<XRayFloraWindProvider>();
                Light sun = root.GetComponentInChildren<Light>();
                Color originalColor = sun.color;
                Quaternion originalRotation = sun.transform.rotation;
                string controllerBefore = EditorJsonUtility.ToJson(controller);
                string windBefore = EditorJsonUtility.ToJson(wind);
                Assert.IsTrue(XRayWeatherPreview.Start(controller));
                Assert.IsTrue(wind.WeatherDriven);
                double initialTime = controller.GameTimeSeconds;
                Assert.IsFalse(XRayWeatherPreview.Advance(10.0, true));
                Assert.IsTrue(XRayWeatherPreview.Advance(10.05, true));
                Assert.That(controller.GameTimeSeconds, Is.EqualTo(initialTime + 3.0).Within(0.00001));
                int ticks = wind.State.Ticks;
                Assert.IsFalse(XRayWeatherPreview.Advance(10.05, true));
                Assert.IsFalse(XRayFloraWindPreview.Advance(10.1, true));
                Assert.AreEqual(ticks, wind.State.Ticks);
                controller.SetPaused(true);
                Assert.IsTrue(XRayWeatherPreview.Advance(10.1, true));
                Assert.That(controller.GameTimeSeconds, Is.EqualTo(initialTime + 3.0).Within(0.00001));
                controller.SetPaused(false);
                Assert.IsFalse(XRayWeatherPreview.Advance(500.0, true));
                Assert.IsFalse(XRayWeatherPreview.Advance(600.0, false));
                Assert.IsFalse(XRayWeatherPreview.Advance(700.0, true));
                Assert.IsTrue(XRayWeatherPreview.Advance(700.05, true));
                Assert.That(controller.GameTimeSeconds, Is.EqualTo(initialTime + 6.0).Within(0.00002));
                Assert.AreEqual(controllerBefore, EditorJsonUtility.ToJson(controller));
                Assert.AreEqual(windBefore, EditorJsonUtility.ToJson(wind));
                XRayWeatherPreview.Stop();
                Assert.IsFalse(controller.PreviewActive);
                Assert.IsFalse(wind.WeatherDriven);
                Assert.AreEqual(originalColor, sun.color);
                Assert.That(Quaternion.Angle(originalRotation, sun.transform.rotation), Is.LessThan(0.0001f));
                Assert.IsTrue(XRayWeatherPreview.Start(controller));
                controller.enabled = false;
                Assert.IsFalse(XRayWeatherPreview.Advance(800.0, true));
                Assert.IsFalse(wind.WeatherDriven);
            }
            finally
            {
                XRayWeatherPreview.Stop();
                Object.DestroyImmediate(profile);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator FutureImportConnectsOneWeatherControllerAndReimportsIdempotently()
        {
            string hostPath = PrepareImportHost();
            try
            {
                using (var fixture = new XRayFloraLevelFixture(false))
                {
                    XRayLevelPaths.EnsureFolder(fixture.Root);
                    XRayWeatherProfile profile = Profile();
                    string profilePath = fixture.Root + "/Weather.asset";
                    AssetDatabase.CreateAsset(profile, profilePath);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        XRayLevelImportRequest request = fixture.Request(XRayFloraImportMode.EnableVerifiedAnomaly, XRayFloraWindImportMode.EnableVerifiedAnomaly);
                        request.Settings.WeatherProfile = profile;
                        XRayLevelImportJob.Enqueue(request);
                        while (XRayLevelImportJob.Current != null) yield return null;
                        Assert.AreEqual(XRayLevelJobState.Completed, XRayLevelImportJob.LastState, string.Join("; ", XRayLevelImportJob.LastRecord.Errors));
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fixture.Root + "/" + fixture.LevelName + "/Prefabs/" + fixture.LevelName + ".prefab");
                        Assert.IsNotNull(prefab);
                        XRayWeatherController[] controllers = prefab.GetComponentsInChildren<XRayWeatherController>(true);
                        Assert.AreEqual(1, controllers.Length, "pass " + pass);
                        Assert.AreEqual(1, prefab.GetComponentsInChildren<XRayFloraWindProvider>(true).Length, "pass " + pass);
                        Assert.AreEqual(1, prefab.GetComponentsInChildren<MonolithWeatherInputs>(true).Length, "pass " + pass);
                        Assert.AreEqual(1, prefab.GetComponentsInChildren<MonolithSourceLighting>(true).Length, "pass " + pass);
                        Assert.AreSame(profile, new SerializedObject(controllers[0]).FindProperty("_profile").objectReferenceValue);
                    }
                }
            }
            finally
            {
                RestoreImportHost(hostPath);
            }
        }

        [Test]
        public void SavingSceneEndsPreviewAndRestoresAuthoredLightInputs()
        {
            string hostPath = PrepareSceneSaveHost();
            Scene previous = SceneManager.GetActiveScene();
            Assert.IsTrue(previous.IsValid() && previous.isLoaded && !EditorSceneManager.IsPreviewScene(previous));
            string previousPath = previous.path;
            bool previousDirty = previous.isDirty;
            int previousCount = SceneManager.sceneCount;
            string identity = Guid.NewGuid().ToString("N");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            string path = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/__WeatherSave_" + identity + ".unity";
            var root = new GameObject("Weather save fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            SceneManager.SetActiveScene(previous);
            XRayWeatherProfile profile = Profile();
            try
            {
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
                Light sun = root.GetComponentInChildren<Light>();
                Color color = sun.color;
                float intensity = sun.intensity;
                Quaternion rotation = sun.transform.rotation;
                Assert.IsTrue(XRayWeatherPreview.Start(controller));
                controller.Seek(3600.0);
                Assert.That(sun.intensity, Is.LessThan(intensity));
                Assert.IsTrue(EditorSceneManager.SaveScene(scene, path));
                Assert.AreEqual(previousPath, previous.path);
                Assert.AreEqual(previousDirty, previous.isDirty);
                Assert.AreEqual(previousCount + 1, SceneManager.sceneCount);
                Assert.AreEqual(previous, SceneManager.GetActiveScene());
                Assert.IsNull(XRayWeatherPreview.Controller);
                Assert.IsFalse(controller.PreviewActive);
                Assert.IsFalse(root.GetComponentInChildren<XRayFloraWindProvider>().WeatherDriven);
                Assert.AreEqual(color, sun.color);
                Assert.AreEqual(intensity, sun.intensity);
                Assert.That(Quaternion.Angle(rotation, sun.transform.rotation), Is.LessThan(0.0001f));
            }
            finally
            {
                XRayWeatherPreview.Stop();
                if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
                Object.DestroyImmediate(profile);
                if (File.Exists(path) || File.Exists(path + ".meta")) Assert.IsTrue(AssetDatabase.DeleteAsset(path));
                RestoreImportHost(hostPath);
            }
        }

        private static string PrepareSceneSaveHost()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path)) return null;
            if (!Application.isBatchMode || !scene.IsValid() || !scene.isLoaded || scene.isDirty || scene.rootCount != 0 || SceneManager.sceneCount != 1) Assert.Ignore("Weather save regression requires a saved host or a clean empty batch host; user scenes were preserved.");
            string path = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/__WeatherSaveHost_" + Guid.NewGuid().ToString("N") + ".unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, path));
            return path;
        }

        private static string PrepareImportHost()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (Application.isBatchMode || !string.IsNullOrEmpty(scene.path)) return null;
            if (!scene.IsValid() || !scene.isLoaded || scene.isDirty || scene.rootCount != 0 || SceneManager.sceneCount != 1) Assert.Ignore("Weather import acceptance requires a saved active scene or one clean empty untitled host; loaded user scenes were preserved.");
            string path = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/__WeatherImportHost_" + Guid.NewGuid().ToString("N") + ".unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, path));
            return path;
        }

        private static void RestoreImportHost(string path)
        {
            if (path == null) return;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != path || scene.isDirty || scene.rootCount != 0 || SceneManager.sceneCount != 1) throw new InvalidOperationException("The task-owned weather import host changed during the test; its state was preserved instead of replacing loaded scenes.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (File.Exists(path) || File.Exists(path + ".meta")) Assert.IsTrue(AssetDatabase.DeleteAsset(path));
        }

        private static XRayWeatherProfile Profile()
        {
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            var keys = new[] { Frame("00:00:00", 0.0, Color.black, 0.0f), Frame("12:00:00", 43200.0, Color.white, 500.0f) };
            var positions = new Vector2[24];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(i * 10.0f, -30.0f);
            profile.Initialize("synthetic_editor_fixture", "synthetic regression input", "not a source-game profile", keys, positions, new Vector4(11.0f, 0.15f, 0.5f, 0.15f));
            return profile;
        }

        private static XRayWeatherSnapshot Frame(string key, double seconds, Color sun, float wind)
        {
            return new XRayWeatherSnapshot(key, seconds, sun, Color.gray, Color.gray, 0.0f, wind, 0.004f, 0.0f, Color.gray, Color.gray, 0.5f, 500.0f, 1000.0f, Color.gray, Color.gray, 0.0f, new XRayWeatherResources("missing_fixture_sky"), new XRayWeatherResources("missing_fixture_clouds"));
        }
    }
}
