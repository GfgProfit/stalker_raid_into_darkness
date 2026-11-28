using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class WeatherSunRadianceTests
    {
        private const float TOLERANCE = 0.00001f;

        private Scene _scene;
        private GameObject _root;
        private XRayWeatherProfile _profile;
        private float _savedRadianceScale;

        [SetUp]
        public void SetUp()
        {
            if (XRayWeatherController.Active != null) Assert.Ignore("This isolated fixture preserves the existing weather owner.");
            foreach (MonolithSourceLighting provider in Object.FindObjectsByType<MonolithSourceLighting>())
            {
                if (provider.isActiveAndEnabled) Assert.Ignore("This isolated fixture preserves the existing source-lighting owner.");
            }
            _savedRadianceScale = Shader.GetGlobalFloat("_MonolithBoundRadianceScale");
            _scene = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("Weather Sun radiance fixture");
            SceneManager.MoveGameObjectToScene(_root, _scene);
        }

        [TearDown]
        public void TearDown()
        {
            bool hadFixture = _scene.IsValid() || _root != null;
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.ClosePreviewScene(_scene);
            if (_profile != null) Object.DestroyImmediate(_profile);
            if (_root != null) Object.DestroyImmediate(_root);
            if (hadFixture) Shader.SetGlobalFloat("_MonolithBoundRadianceScale", _savedRadianceScale);
            _profile = null;
            _root = null;
            _scene = default;
        }

        [TestCase(false, 1.0f, 512.0f)]
        [TestCase(true, 1.0f, 1.0f)]
        [TestCase(false, 3.0f, 3.0f)]
        public void WeatherFallbackPreservesAuthoredScaleAndExplicitBindings(bool explicitBinding, float authoredScale, float expectedWeatherScale)
        {
            Assert.AreEqual(512.0f, MonolithSourceLighting.DEFAULT_WEATHER_RADIANCE_SCALE);
            Light sun = _root.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.7f, 0.8f, 0.9f);
            sun.intensity = 2250.0f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(35.0f, 15.0f, 0.0f);
            MonolithSourceLighting lighting = _root.AddComponent<MonolithSourceLighting>();
            lighting.Mode = MonolithSourceLighting.LightingMode.Bound;
            lighting.BoundRadianceScale = authoredScale;
            if (explicitBinding) lighting.SetBindings(new[] { sun }, new[] { new Vector3(0.2f, 0.3f, 0.4f) });
            string authored = EditorJsonUtility.ToJson(lighting);
            Color savedColor = sun.color;
            float savedIntensity = sun.intensity;
            Quaternion savedRotation = sun.transform.rotation;
            var sourceColor = new Vector3(0.948f, 0.958f, 0.89f);
            Vector3 sourceDirection = new Vector3(0.2f, -0.9f, 0.3f).normalized;
            Assert.AreEqual(authoredScale, lighting.EffectiveBoundRadianceScale);
            Assert.AreEqual(authoredScale, Shader.GetGlobalFloat("_MonolithBoundRadianceScale"));
            Assert.IsTrue(lighting.TrySetWeatherLight(_root, sun, sourceColor, sourceDirection));
            Assert.AreEqual(expectedWeatherScale, lighting.EffectiveBoundRadianceScale);
            Assert.AreEqual(expectedWeatherScale, Shader.GetGlobalFloat("_MonolithBoundRadianceScale"));
            Assert.AreEqual(authoredScale, lighting.BoundRadianceScale);
            Assert.AreEqual(authored, EditorJsonUtility.ToJson(lighting));
            Assert.AreEqual(1, Shader.GetGlobalInt("_MonolithBoundLightCount"));
            Vector4 publishedColor = Shader.GetGlobalVectorArray("_MonolithBoundColor64")[0];
            Vector4 publishedDirection = Shader.GetGlobalVectorArray("_MonolithBoundDirectionRange64")[0];
            for (int i = 0; i < 3; i++)
            {
                Assert.That(publishedColor[i], Is.EqualTo(sourceColor[i]).Within(TOLERANCE));
                Assert.That(publishedDirection[i], Is.EqualTo(sourceDirection[i]).Within(TOLERANCE));
            }
            Assert.AreEqual(savedColor, sun.color);
            Assert.AreEqual(savedIntensity, sun.intensity);
            Assert.AreEqual(savedRotation, sun.transform.rotation);
            Assert.AreEqual(LightShadows.Soft, sun.shadows);
            lighting.ReleaseWeatherLight(_root);
            Assert.AreEqual(authoredScale, lighting.EffectiveBoundRadianceScale);
            Assert.AreEqual(authoredScale, Shader.GetGlobalFloat("_MonolithBoundRadianceScale"));
            Assert.AreEqual(authored, EditorJsonUtility.ToJson(lighting));
            Assert.AreEqual(explicitBinding ? 1 : 0, Shader.GetGlobalInt("_MonolithBoundLightCount"));
        }

        [TestCase(0.0)]
        [TestCase(43200.0)]
        public void FreshBuilderUsesCalibratedScaleWithoutChangingTheWeatherSunFormula(double seconds)
        {
            Assert.AreEqual(Mathf.Pow(2.0f, XRayLevelSceneBuilder.FIXED_EXPOSURE), MonolithSourceLighting.DEFAULT_WEATHER_RADIANCE_SCALE);
            _profile = Profile();
            XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(_root, _profile);
            MonolithSourceLighting lighting = _root.GetComponentInChildren<MonolithSourceLighting>();
            Light sun = _root.GetComponentInChildren<Light>();
            Assert.IsNotNull(lighting);
            Assert.IsNotNull(sun);
            Assert.AreEqual(MonolithSourceLighting.DEFAULT_WEATHER_RADIANCE_SCALE, lighting.BoundRadianceScale);
            Assert.AreEqual(MonolithSourceLighting.DEFAULT_WEATHER_RADIANCE_SCALE, lighting.EffectiveBoundRadianceScale);
            string authored = EditorJsonUtility.ToJson(lighting);
            float authoredIntensity = sun.intensity;
            Assert.IsTrue(_profile.TryEvaluate(seconds, out XRayWeatherSnapshot expected, out string diagnostic), diagnostic);
            Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
            controller.Seek(seconds);
            Vector3 expectedSource = Vector3.Scale(new Vector3(expected.SunColor.r, expected.SunColor.g, expected.SunColor.b), _profile.SunLightScale);
            Assert.AreEqual(expected.SunColor, controller.Snapshot.SunColor);
            Assert.That(Vector3.Distance(expected.SunDirection, controller.Snapshot.SunDirection), Is.LessThan(TOLERANCE));
            Assert.That(Vector3.Distance(expectedSource, controller.SourceLightColor), Is.LessThan(TOLERANCE));
            Assert.That(Vector3.Distance(expected.SunDirection, sun.transform.forward), Is.LessThan(TOLERANCE));
            Vector4 published = Shader.GetGlobalVectorArray("_MonolithBoundColor64")[0];
            Color actualRadiance = sun.color.linear * sun.intensity;
            Color expectedRadiance = new Color(expectedSource.x, expectedSource.y, expectedSource.z).linear * authoredIntensity;
            for (int i = 0; i < 3; i++)
            {
                Assert.That(published[i], Is.EqualTo(expectedSource[i]).Within(TOLERANCE));
                Assert.That(actualRadiance[i], Is.EqualTo(expectedRadiance[i]).Within(TOLERANCE * Mathf.Max(1.0f, expectedRadiance[i])));
            }
            Assert.AreEqual(MonolithSourceLighting.DEFAULT_WEATHER_RADIANCE_SCALE, Shader.GetGlobalFloat("_MonolithBoundRadianceScale"));
            Assert.AreEqual(authored, EditorJsonUtility.ToJson(lighting));
            controller.EndPreview();
            Assert.AreEqual(authoredIntensity, sun.intensity);
            Assert.AreEqual(MonolithSourceLighting.DEFAULT_WEATHER_RADIANCE_SCALE, lighting.EffectiveBoundRadianceScale);
            Assert.AreEqual(authored, EditorJsonUtility.ToJson(lighting));
        }

        private static XRayWeatherProfile Profile()
        {
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            var first = new XRayWeatherSnapshot("00:00:00", 0.0, new Color(0.08f, 0.1f, 0.14f), Color.gray, Color.gray, 0.0f, 1.0f, 0.01f, 0.0f, Color.white, Color.gray, 1.0f, 400.0f, 500.0f, Color.gray, Color.clear, 0.0f, default, default);
            var second = new XRayWeatherSnapshot("12:00:00", 43200.0, new Color(0.474f, 0.479f, 0.445f), Color.gray, Color.gray, 0.0f, 1.0f, 0.01f, 0.0f, Color.white, Color.gray, 1.0f, 400.0f, 500.0f, Color.gray, Color.clear, 0.0f, default, default);
            var positions = new Vector2[24];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(24.0f, -47.0f);
            profile.Initialize("sun_radiance", "sun_radiance_fixture", "analytic weather Sun inputs", new[] { first, second }, positions, Vector4.one, null, null, new Vector3(2.0f, 2.0f, 2.0f));
            return profile;
        }
    }
}
