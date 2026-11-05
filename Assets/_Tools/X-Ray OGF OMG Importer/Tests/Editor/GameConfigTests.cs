using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class GameConfigTests
    {
        private const string LEGACY_SCENE = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/l07_military/Scenes/l07_military.unity";
        private const string LEGACY_PREFAB = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/l07_military/Prefabs/l07_military.prefab";
        private const double DAY_SECONDS = 86400.0;
        private const double NOON_SECONDS = 43200.0;

        [Test]
        public void SharedDefaultsReachBothConsumersWithExplicitLevelAndSessionPrecedence()
        {
            GameConfig canonical = GameConfig.Load();
            Assert.IsNotNull(canonical);
            GameConfig configuration = Object.Instantiate(canonical);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                XRayWeatherController first = CreateController(scene, "First weather consumer");
                XRayWeatherController second = CreateController(scene, "Second weather consumer");
                var settings = new SerializedObject(configuration);
                settings.FindProperty("_defaultTimeScale").floatValue = 7.0f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var level = new SerializedObject(first);
                Assert.AreEqual(1.0f, level.FindProperty("_timeScale").floatValue);
                ApplyConfiguration(first, configuration);
                Assert.AreEqual(7.0f, first.TimeScale);
                level.FindProperty("_timeScale").floatValue = 3.0f;
                level.ApplyModifiedPropertiesWithoutUndo();
                string firstAuthored = EditorJsonUtility.ToJson(first);
                string secondAuthored = EditorJsonUtility.ToJson(second);
                ApplyConfiguration(first, configuration);
                ApplyConfiguration(second, configuration);
                Assert.AreSame(configuration, first.Configuration);
                Assert.AreSame(configuration, second.Configuration);
                Assert.AreEqual(7.0f, first.TimeScale);
                Assert.AreEqual(7.0f, second.TimeScale);
                Assert.AreEqual(firstAuthored, EditorJsonUtility.ToJson(first));
                Assert.AreEqual(secondAuthored, EditorJsonUtility.ToJson(second));

                SetLevelOverrides(first, XRayWeatherLevelOverrides.TimeScale);
                ApplyConfiguration(first, configuration);
                Assert.AreEqual(3.0f, first.TimeScale);
                Assert.AreEqual(7.0f, second.TimeScale);
                settings.FindProperty("_defaultTimeScale").floatValue = 10.0f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                ApplyConfiguration(first, configuration);
                ApplyConfiguration(second, configuration);
                Assert.AreEqual(3.0f, first.TimeScale);
                Assert.AreEqual(10.0f, second.TimeScale);

                first.SetTimeScale(1.0f);
                ApplyConfiguration(first, configuration);
                Assert.AreEqual(1.0f, first.TimeScale);
                first.ClearTimeScaleOverride();
                Assert.AreEqual(3.0f, first.TimeScale);
                SetLevelOverrides(first, XRayWeatherLevelOverrides.None);
                ApplyConfiguration(first, configuration);
                Assert.AreEqual(10.0f, first.TimeScale);
                Assert.AreEqual(firstAuthored, EditorJsonUtility.ToJson(first));
                Assert.AreEqual(secondAuthored, EditorJsonUtility.ToJson(second));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void LegacyMilitaryPrefabInheritsCanonicalDefaultsWithoutSceneWrites()
        {
            GameConfig configuration = GameConfig.Load();
            Assert.IsNotNull(configuration);
            if (!File.Exists(LEGACY_SCENE) || !File.Exists(LEGACY_PREFAB)) Assert.Ignore("Import the accepted l07_military scene and prefab in Production to verify legacy serialized defaults.");
            byte[] sceneBefore = File.ReadAllBytes(LEGACY_SCENE);
            byte[] prefabBefore = File.ReadAllBytes(LEGACY_PREFAB);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LEGACY_PREFAB);
            Assert.IsNotNull(prefab);
            XRayWeatherController legacy = prefab.GetComponentInChildren<XRayWeatherController>(true);
            Assert.IsNotNull(legacy);
            Assert.AreEqual(1.0f, new SerializedObject(legacy).FindProperty("_timeScale").floatValue);
            Assert.IsFalse(legacy.LevelOverrides.HasFlag(XRayWeatherLevelOverrides.TimeScale));
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                XRayWeatherController consumer = CreateController(scene, "Legacy weather consumer");
                EditorUtility.CopySerialized(legacy, consumer);
                consumer.ApplySettings();
                Assert.AreSame(configuration, consumer.Configuration);
                Assert.AreEqual(configuration.DefaultTimeScale, consumer.TimeScale);
                Assert.AreSame(configuration.OrdinaryWeatherCatalog, consumer.Catalog);
                Assert.AreSame(configuration.WeatherFxCatalog, consumer.FxCatalog);
                Assert.AreEqual(21, consumer.Catalog.Profiles.Count);
                Assert.AreEqual(7, consumer.FxCatalog.Effects.Count);
                Assert.AreEqual(configuration.DefaultTimeOfDay * 3600.0, consumer.GameTimeSeconds);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            CollectionAssert.AreEqual(sceneBefore, File.ReadAllBytes(LEGACY_SCENE));
            CollectionAssert.AreEqual(prefabBefore, File.ReadAllBytes(LEGACY_PREFAB));
        }

        [Test]
        public void GraphicsDefaultsAndOverridesStayScopedToTheirConsumers()
        {
            GameConfig canonical = GameConfig.Load();
            Assert.IsNotNull(canonical);
            GameConfig configuration = Object.Instantiate(canonical);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                XRayWeatherController first = CreateController(scene, "Local graphics consumer");
                XRayWeatherController second = CreateController(scene, "Global graphics consumer");
                var settings = new SerializedObject(configuration);
                settings.FindProperty("_fogEnabled").boolValue = false;
                settings.FindProperty("_sunShaftsEnabled").boolValue = false;
                settings.FindProperty("_lensFlareEnabled").boolValue = false;
                settings.FindProperty("_fxLightingAdaptation").floatValue = 0.25f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var level = new SerializedObject(first);
                level.FindProperty("_sunShaftsEnabled").boolValue = true;
                level.FindProperty("_lensFlareEnabled").boolValue = true;
                level.FindProperty("_fxLightingAdaptation").floatValue = 1.5f;
                level.ApplyModifiedPropertiesWithoutUndo();
                SetLevelOverrides(first, XRayWeatherLevelOverrides.SunShafts | XRayWeatherLevelOverrides.LensFlare | XRayWeatherLevelOverrides.FxLightingAdaptation);
                ApplyConfiguration(first, configuration);
                ApplyConfiguration(second, configuration);
                Assert.IsFalse(first.FogEnabled);
                Assert.IsTrue(first.SunShaftsEnabled);
                Assert.IsTrue(first.LensFlareEnabled);
                Assert.AreEqual(1.5f, first.FxLightingAdaptationGain);
                Assert.IsFalse(second.FogEnabled);
                Assert.IsFalse(second.SunShaftsEnabled);
                Assert.IsFalse(second.LensFlareEnabled);
                Assert.AreEqual(0.25f, second.FxLightingAdaptationGain);

                first.SetFogOverride(true);
                first.SetCelestialEffectOverrides(false, false);
                first.SetFxLightingAdaptationOverride(0.5f);
                Assert.IsTrue(first.FogEnabled);
                Assert.IsFalse(first.SunShaftsEnabled);
                Assert.IsFalse(first.LensFlareEnabled);
                Assert.AreEqual(0.5f, first.FxLightingAdaptationGain);
                Assert.IsFalse(second.FogEnabled);
                Assert.AreEqual(0.25f, second.FxLightingAdaptationGain);
                Assert.IsFalse(configuration.FogEnabled);
                Assert.AreEqual(0.25f, configuration.FxLightingAdaptation);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(configuration);
            }
        }

        [TestCase(0.0f)]
        [TestCase(1.0f)]
        [TestCase(7.0f)]
        [TestCase(3.0f)]
        public void WeatherFxAdvancesByTheEffectiveWorldClockOnce(float timeScale)
        {
            GameConfig configuration = GameConfig.Load();
            Assert.IsNotNull(configuration);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Weather FX clock consumer");
                SceneManager.MoveGameObjectToScene(root, scene);
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, configuration.DefaultWeather);
                if (timeScale == 3.0f)
                {
                    var level = new SerializedObject(controller);
                    level.FindProperty("_timeScale").floatValue = 3.0f;
                    level.ApplyModifiedPropertiesWithoutUndo();
                    SetLevelOverrides(controller, controller.LevelOverrides | XRayWeatherLevelOverrides.TimeScale);
                }
                Assert.IsTrue(controller.BeginPreview(DAY_SECONDS - 1.0, configuration.DefaultWeather), controller.Diagnostic);
                if (timeScale <= 1.0f) controller.SetTimeScale(timeScale);
                controller.SetPaused(false);
                Assert.AreEqual(timeScale, controller.TimeScale);
                XRayWeatherFxProfile effect = controller.FxCatalog.FindVariant(XRayWeatherFxKind.Blowout, NOON_SECONDS);
                Assert.IsNotNull(effect);
                Assert.IsTrue(controller.StartFx(effect, 215.0), controller.Diagnostic);
                controller.TickPreview(1.0f);
                Assert.AreEqual(215.0 + timeScale, controller.FxTimeSeconds);
                Assert.AreEqual((DAY_SECONDS - 1.0 + timeScale) % DAY_SECONDS, controller.GameTimeSeconds);
                controller.SetPaused(true);
                controller.TickPreview(1.0f);
                Assert.AreEqual(215.0 + timeScale, controller.FxTimeSeconds);
                controller.Seek(NOON_SECONDS);
                Assert.AreEqual(215.0 + timeScale, controller.FxTimeSeconds);
                Assert.IsTrue(controller.StopFx());
                Assert.AreEqual(NOON_SECONDS, controller.GameTimeSeconds);
                Assert.AreSame(configuration.DefaultWeather, controller.ActiveProfile);
                controller.EndPreview();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void RevisionRefreshUpdatesLiveGraphicsAndPreservesPausedFxAndSessionTime()
        {
            GameConfig canonical = GameConfig.Load();
            Assert.IsNotNull(canonical);
            GameConfig configuration = Object.Instantiate(canonical);
            string canonicalBefore = JsonUtility.ToJson(canonical);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Live global configuration consumer");
                SceneManager.MoveGameObjectToScene(root, scene);
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, configuration.DefaultWeather);
                Assert.IsTrue(controller.BeginPreview(NOON_SECONDS, configuration.DefaultWeather), controller.Diagnostic);
                ApplyConfiguration(controller, configuration);
                XRayWeatherFogRenderer fog = controller.GetComponent<XRayWeatherFogRenderer>();
                Assert.IsNotNull(fog);
                Assert.IsTrue(fog.HasWeather);
                Assert.IsNotNull(controller.Celestial);
                controller.SetPaused(false);
                controller.TickPreview(1.0f);
                Assert.AreEqual(NOON_SECONDS + 7.0, controller.GameTimeSeconds);
                XRayWeatherFxProfile effect = controller.FxCatalog.FindVariant(XRayWeatherFxKind.Blowout, NOON_SECONDS);
                Assert.IsTrue(controller.StartFx(effect, 215.0), controller.Diagnostic);
                controller.SetPaused(true);
                double clock = controller.GameTimeSeconds;
                double fxClock = controller.FxTimeSeconds;
                string authored = EditorJsonUtility.ToJson(controller);
                int previousRevision = configuration.Revision;
                var settings = new SerializedObject(configuration);
                settings.FindProperty("_defaultTimeScale").floatValue = 10.0f;
                settings.FindProperty("_fogEnabled").boolValue = false;
                settings.FindProperty("_sunShaftsEnabled").boolValue = false;
                settings.FindProperty("_lensFlareEnabled").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreNotEqual(previousRevision, configuration.Revision);
                controller.TickPreview(0.0f);
                Assert.AreEqual(10.0f, controller.TimeScale);
                Assert.AreEqual(clock, controller.GameTimeSeconds);
                Assert.AreEqual(fxClock, controller.FxTimeSeconds);
                Assert.AreSame(effect, controller.ActiveFx);
                Assert.IsTrue(controller.Paused);
                Assert.IsFalse(fog.HasWeather);
                Assert.AreEqual(0.0f, controller.Celestial.ShaftsIntensity);
                Assert.AreEqual(0.0f, controller.Celestial.EffectiveIntensity);

                controller.SetTimeScale(3.0f);
                settings.FindProperty("_defaultTimeScale").floatValue = 11.0f;
                settings.FindProperty("_fogEnabled").boolValue = true;
                settings.FindProperty("_sunShaftsEnabled").boolValue = true;
                settings.FindProperty("_lensFlareEnabled").boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo();
                controller.TickPreview(0.0f);
                Assert.AreEqual(3.0f, controller.TimeScale);
                Assert.AreEqual(clock, controller.GameTimeSeconds);
                Assert.AreEqual(fxClock, controller.FxTimeSeconds);
                Assert.AreSame(effect, controller.ActiveFx);
                Assert.IsTrue(controller.Paused);
                Assert.IsTrue(fog.HasWeather);
                Assert.AreEqual(authored, EditorJsonUtility.ToJson(controller));
                controller.ClearTimeScaleOverride();
                Assert.AreEqual(11.0f, controller.TimeScale);
                controller.StopFx();
                controller.Seek(NOON_SECONDS);
                controller.SetPaused(false);
                controller.TickPreview(0.0f);
                Assert.That(controller.Celestial.ShaftsIntensity, Is.GreaterThan(0.0f));
                Assert.IsNotNull(controller.Celestial.FlareDefinition);
                controller.EndPreview();
                Assert.AreEqual(canonicalBefore, JsonUtility.ToJson(canonical));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void MissingOrInvalidConfigurationReportsAnActionableDiagnostic()
        {
            GameConfig canonical = GameConfig.Load();
            Assert.IsNotNull(canonical);
            GameConfig configuration = Object.Instantiate(canonical);
            string canonicalBefore = JsonUtility.ToJson(canonical);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                XRayWeatherController controller = CreateController(scene, "Invalid configuration consumer");
                LogAssert.Expect(LogType.Error, "Missing GameConfig. Include the canonical Resources/GameConfig.asset in the project.");
                ApplyConfiguration(controller, null);
                Assert.IsNull(controller.Configuration);
                Assert.IsNull(controller.Catalog);
                Assert.IsNull(controller.FxCatalog);
                Assert.AreNotSame(controller, XRayWeatherController.Active);
                Assert.That(controller.Diagnostic, Does.Contain("Missing GameConfig"));
                var settings = new SerializedObject(configuration);
                settings.FindProperty("_defaultTimeScale").floatValue = -1.0f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                LogAssert.Expect(LogType.Error, "GameConfig DefaultTimeScale must be finite and non-negative.");
                ApplyConfiguration(controller, configuration);
                Assert.AreNotSame(controller, XRayWeatherController.Active);
                Assert.That(controller.Diagnostic, Does.Contain("DefaultTimeScale"));
                Assert.AreEqual(canonicalBefore, JsonUtility.ToJson(canonical));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(configuration);
            }
        }

        private static XRayWeatherController CreateController(Scene scene, string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root.AddComponent<XRayWeatherController>();
        }

        private static void SetLevelOverrides(XRayWeatherController controller, XRayWeatherLevelOverrides overrides)
        {
            var level = new SerializedObject(controller);
            level.FindProperty("_levelOverrides").intValue = (int)overrides;
            level.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyConfiguration(XRayWeatherController controller, GameConfig configuration)
        {
            MethodInfo apply = typeof(XRayWeatherController).GetMethod("ApplyConfiguration", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(apply);
            apply.Invoke(controller, new object[] { configuration });
        }
    }
}
