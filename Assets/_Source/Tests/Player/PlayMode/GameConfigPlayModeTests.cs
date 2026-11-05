using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class GameConfigPlayModeTests
    {
        [UnityTest]
        public IEnumerator RuntimeConsumersShareReadOnlyConfigurationAcrossReactivation()
        {
            GameConfig configuration = GameConfig.Load();
            Assert.IsNotNull(configuration);
            Assert.IsTrue(configuration.TryValidate(out string diagnostic), diagnostic);
            string configBefore = JsonUtility.ToJson(configuration);
            string ordinaryBefore = JsonUtility.ToJson(configuration.OrdinaryWeatherCatalog);
            string fxBefore = JsonUtility.ToJson(configuration.WeatherFxCatalog);
            Scene scene = SceneManager.CreateScene("GameConfig runtime regression");
            var firstRoot = new GameObject("First runtime configuration consumer");
            var secondRoot = new GameObject("Second runtime configuration consumer");
            SceneManager.MoveGameObjectToScene(firstRoot, scene);
            SceneManager.MoveGameObjectToScene(secondRoot, scene);
            try
            {
                XRayWeatherController first = firstRoot.AddComponent<XRayWeatherController>();
                XRayWeatherController second = secondRoot.AddComponent<XRayWeatherController>();
                yield return null;
                Assert.AreSame(configuration, first.Configuration);
                Assert.AreSame(configuration, second.Configuration);
                Assert.AreEqual(configuration.DefaultTimeScale, first.TimeScale);
                Assert.AreEqual(configuration.DefaultTimeScale, second.TimeScale);
                Assert.AreSame(configuration.OrdinaryWeatherCatalog, first.Catalog);
                Assert.AreSame(configuration.WeatherFxCatalog, second.FxCatalog);
                first.SetTimeScale(3.0f);
                yield return null;
                Assert.AreEqual(3.0f, first.TimeScale);
                Assert.AreEqual(configuration.DefaultTimeScale, second.TimeScale);
                first.ClearTimeScaleOverride();
                Assert.AreEqual(configuration.DefaultTimeScale, first.TimeScale);
                firstRoot.SetActive(false);
                yield return null;
                firstRoot.SetActive(true);
                yield return null;
                Assert.AreSame(configuration, first.Configuration);
                Assert.AreEqual(configuration.DefaultTimeScale, first.TimeScale);
                Assert.IsNull(first.TimeScaleOverride);
                Assert.AreEqual(configBefore, JsonUtility.ToJson(configuration));
                Assert.AreEqual(ordinaryBefore, JsonUtility.ToJson(configuration.OrdinaryWeatherCatalog));
                Assert.AreEqual(fxBefore, JsonUtility.ToJson(configuration.WeatherFxCatalog));
            }
            finally
            {
                Object.Destroy(firstRoot);
                Object.Destroy(secondRoot);
            }
            yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
