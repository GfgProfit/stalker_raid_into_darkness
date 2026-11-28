#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class TeleportAnomalyLightTests
    {
        private const string TELEPORT_PATH = "Assets/_Source/Anomalies/Teleport/Teleport.prefab";
        private const float FLASH_DELAY = 0.8f;
        private const float FLASH_INTENSITY = 258750f;
        private const float FLASH_RANGE = 30f;
        private const float IDLE_INTENSITY = 64687.5f;
        private const float IDLE_RANGE = 15f;
        private const float TIMEOUT = 3f;

        private Scene _scene;
        private Scene _previousScene;
        private float _previousTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousScene = SceneManager.GetActiveScene();
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            _scene = SceneManager.CreateScene(nameof(TeleportAnomalyLightTests));
            SceneManager.SetActiveScene(_scene);
            bool hasListener = false;
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>())
            {
                hasListener |= listener.isActiveAndEnabled;
            }
            if (!hasListener)
            {
                new GameObject("TeleportAnomalyLightListener").AddComponent<AudioListener>();
            }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_previousScene.IsValid() && _previousScene.isLoaded)
            {
                SceneManager.SetActiveScene(_previousScene);
            }
            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
            Time.timeScale = _previousTimeScale;
        }

        [UnityTest]
        public IEnumerator OneLightKeepsItsAuthoredIdleAndDelayedDischargePhases()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TELEPORT_PATH);
            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab, new Vector3(10000f, 1000f, 10000f), Quaternion.identity);
            var visual = instance.GetComponent<OverlappingAnomalyVisual>();
            Assert.That(visual, Is.Not.Null);
            Light[] lights = instance.GetComponentsInChildren<Light>(true);
            Assert.That(lights, Has.Length.EqualTo(1));
            Light light = lights[0];
            AssertIdle(light);
            LightShadows shadows = light.shadows;

            float startedAt = Time.time;
            visual.PlayDischarge();
            Assert.That(visual.IsDischarging, Is.True);
            Assert.That(light.enabled, Is.False);
            yield return WaitForLight(light, true);
            Assert.That(Time.time - startedAt, Is.GreaterThanOrEqualTo(FLASH_DELAY - 0.075f));
            Assert.That(light.transform.localPosition, Is.EqualTo(new Vector3(0f, 1.5f, 0f)));
            Assert.That(light.color, Is.EqualTo(new Color(1.4f, 1.6f, 1.8f, 1f)));
            Assert.That(light.shadows, Is.EqualTo(shadows));
            Assert.That(light.range, Is.InRange(FLASH_RANGE * 0.75f, FLASH_RANGE * 1.001f));
            Assert.That(light.intensity / FLASH_INTENSITY, Is.EqualTo(light.range / FLASH_RANGE).Within(0.0001f));

            yield return WaitForLight(light, false);
            visual.ShowIdle();
            AssertIdle(light);
            Assert.That(light.shadows, Is.EqualTo(shadows));
            visual.Hide();
            Assert.That(light.enabled, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertIdle(Light light)
        {
            Assert.That(light.enabled, Is.True);
            Assert.That(light.range, Is.EqualTo(IDLE_RANGE).Within(0.0001f));
            Assert.That(light.intensity, Is.EqualTo(IDLE_INTENSITY).Within(0.01f));
            Assert.That(light.transform.localPosition, Is.EqualTo(new Vector3(0f, 3f, 0f)));
            Assert.That(light.color.b, Is.LessThanOrEqualTo(1f));
        }

        private static IEnumerator WaitForLight(Light light, bool expected)
        {
            float deadline = Time.time + TIMEOUT;
            while (light.enabled != expected && Time.time < deadline)
            {
                yield return null;
            }
            Assert.That(light.enabled, Is.EqualTo(expected));
        }
    }
}
#endif
