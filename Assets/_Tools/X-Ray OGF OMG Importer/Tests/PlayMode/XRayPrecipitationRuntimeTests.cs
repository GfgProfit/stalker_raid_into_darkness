using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayPrecipitationRuntimeTests
    {
        private Scene _scene;

        [UnityTearDown]
        public IEnumerator ReleaseOwnedScene()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [UnityTest]
        public IEnumerator RuntimeOwnershipClockPauseDisableAndSceneRecreationPreserveSharedConfiguration()
        {
            Assert.IsTrue(Application.isPlaying);
            Assert.IsNull(XRayWeatherController.Active, "Use the isolated Test Runner scene.");
            GameConfig shared = GameConfig.Load();
            Assert.IsNotNull(shared);
            string before = JsonUtility.ToJson(shared) + JsonUtility.ToJson(shared.Precipitation);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                _scene = SceneManager.CreateScene("Precipitation Runtime Regression " + cycle);
                Scene scene = _scene;
                var root = new GameObject("Owned weather lifecycle fixture");
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                MonolithWeatherInputs weather = root.AddComponent<MonolithWeatherInputs>();
                MonolithSourceLighting lighting = root.AddComponent<MonolithSourceLighting>();
                lighting.Mode = MonolithSourceLighting.LightingMode.Bound;
                lighting.BoundRadianceScale = MonolithSourceLighting.DEFAULT_WEATHER_RADIANCE_SCALE;
                XRayFloraWindProvider wind = root.AddComponent<XRayFloraWindProvider>();
                var sunObject = new GameObject("Fixture sun");
                sunObject.transform.SetParent(root.transform, false);
                Light sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sunObject.AddComponent<HDAdditionalLightData>();
                XRayWeatherController controller = root.AddComponent<XRayWeatherController>();
                controller.Configure(null, weather, lighting, wind, sun, false);
                root.SetActive(true);
                controller.SetPaused(true);
                yield return null;
                Assert.AreSame(shared, controller.Configuration);
                Assert.AreSame(shared.Precipitation, controller.EffectivePrecipitationConfig);
                Assert.AreEqual(shared.DefaultTimeScale, controller.TimeScale);
                Assert.IsFalse(controller.TimeScaleOverride.HasValue);
                XRayWeatherProfile rain = FindRain(shared.OrdinaryWeatherCatalog, out double seconds);
                controller.SetProfile(rain);
                controller.Seek(seconds);
                foreach (float scale in new[] { 0.0f, 1.0f, 7.0f })
                {
                    controller.SetTimeScale(scale);
                    controller.SetPaused(false);
                    double world = controller.GameTimeSeconds;
                    float real = controller.Precipitation.RealTime;
                    controller.TickSimulation(1.0f);
                    controller.SetPaused(true);
                    Assert.That(controller.GameTimeSeconds, Is.EqualTo((world + scale) % 86400.0).Within(0.00001));
                    Assert.That(controller.Precipitation.RealTime, Is.EqualTo(real + 1.0f).Within(0.00001f));
                    real = controller.Precipitation.RealTime;
                    yield return null;
                    Assert.AreEqual(real, controller.Precipitation.RealTime);
                }
                controller.SetPrecipitationOverride(false);
                Assert.IsFalse(controller.Precipitation.Enabled);
                controller.SetPrecipitationOverride(true);
                controller.SetRainVisualOverride(false);
                Assert.IsTrue(controller.Precipitation.Enabled);
                controller.enabled = false;
                Assert.IsFalse(controller.PrecipitationOverride.HasValue);
                Assert.IsFalse(controller.RainVisualOverride.HasValue);
                Assert.IsFalse(controller.Precipitation.Enabled);
                Assert.AreEqual(0, controller.Precipitation.Pass.ViewCount);
                controller.enabled = true;
                controller.SetPaused(true);
                yield return null;
                Assert.IsTrue(controller.Precipitation.Enabled);
                XRayPrecipitationSystem system = controller.Precipitation;
                yield return SceneManager.UnloadSceneAsync(scene);
                yield return null;
                Assert.IsNull(XRayWeatherController.Active);
                Assert.AreEqual(0L, system.EstimatedBytes);
                Assert.AreSame(shared, GameConfig.Load());
                Assert.AreEqual(before, JsonUtility.ToJson(shared) + JsonUtility.ToJson(shared.Precipitation));
            }
        }

        private static XRayWeatherProfile FindRain(XRayWeatherCatalog catalog, out double seconds)
        {
            foreach (XRayWeatherProfile profile in catalog.Profiles)
            {
                for (int index = 0; index < profile.KeyframeCount; index++)
                {
                    XRayWeatherSnapshot frame = profile.GetKeyframe(index);
                    if (frame.RainDensity <= 0.0f) continue;
                    seconds = frame.GameSeconds;
                    return profile;
                }
            }
            seconds = 0.0;
            Assert.Fail("The shared catalog must include authored rain.");
            return null;
        }
    }
}
