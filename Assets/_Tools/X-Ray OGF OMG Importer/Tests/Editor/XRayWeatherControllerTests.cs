using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherControllerTests
    {
        private const float TOLERANCE = 0.000002f;

        [Test]
        public void ClockPauseSeekAndTransitionsKeepOneWindSequence()
        {
            XRayWeatherProfile first = Profile("first", new Color(0.2f, 0.4f, 0.6f), 0.0f, 500.0f);
            XRayWeatherProfile second = Profile("second", new Color(0.8f, 0.6f, 0.4f), Mathf.PI, 900.0f);
            GameObject root = Rig(first, out XRayWeatherController controller, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting);
            try
            {
                controller.BeginPreview();
                Assert.IsTrue(controller.PreviewActive, controller.Diagnostic);
                controller.Seek(0.0);
                controller.SetTimeScale(60.0f);
                controller.TickPreview(0.1f);
                Assert.That(controller.GameTimeSeconds, Is.EqualTo(6.0).Within(0.000001));
                Assert.That(wind.State.Anim.x, Is.EqualTo(0.0f).Within(TOLERANCE));
                Assert.That(wind.State.Anim.y, Is.EqualTo(0.05f).Within(TOLERANCE));
                Assert.That(wind.State.Anim.z, Is.EqualTo(0.0665f).Within(TOLERANCE));
                Assert.That(wind.State.Anim.w, Is.EqualTo(0.1f).Within(TOLERANCE));
                Vector4 phase = wind.State.Anim;
                double clock = controller.GameTimeSeconds;
                int ticks = wind.State.Ticks;
                lighting.Publish();
                lighting.Publish();
                wind.Tick(0.1f);
                Assert.AreEqual(clock, controller.GameTimeSeconds);
                Assert.AreEqual(ticks, wind.State.Ticks);
                controller.SetPaused(true);
                controller.TickPreview(5.0f);
                Assert.AreEqual(clock, controller.GameTimeSeconds);
                Assert.AreEqual(phase, wind.State.Anim);
                Assert.AreEqual(phase, wind.State.AnimPrev);
                controller.Seek(90000.0);
                Assert.AreEqual(3600.0, controller.GameTimeSeconds);
                Assert.AreEqual(wind.State.Anim, wind.State.AnimPrev);
                controller.SetPaused(false);
                controller.SetTimeScale(0.0f);
                Assert.IsTrue(controller.TransitionTo(second, 2.0f));
                Assert.That(controller.Snapshot.SunColor.r, Is.EqualTo(0.2f).Within(TOLERANCE));
                controller.TickPreview(1.0f);
                Assert.That(controller.Snapshot.SunColor.r, Is.EqualTo(0.5f).Within(TOLERANCE));
                Assert.That(controller.Snapshot.WindVelocity, Is.EqualTo(700.0f).Within(TOLERANCE));
                Assert.That(controller.Snapshot.WindDirectionRadians, Is.EqualTo(Mathf.PI * 0.5f).Within(TOLERANCE));
                Assert.IsFalse(controller.TransitionTo(first, 1.0f));
                controller.TickPreview(1.0f);
                Assert.IsFalse(controller.IsTransitioning);
                Assert.AreSame(second, controller.ActiveProfile);
                Assert.That(controller.Snapshot.SunColor.r, Is.EqualTo(0.8f).Within(TOLERANCE));
                controller.SetProfile(first);
                Assert.AreEqual(wind.State.Anim, wind.State.AnimPrev);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(first);
            }
        }

        [Test]
        public void PreviewRestoresAuthoredInputsBindingsAndLightWithoutSerializationWrites()
        {
            XRayWeatherProfile profile = Profile("restoration", new Color(0.7f, 0.3f, 0.1f), 1.0f, 900.0f);
            GameObject root = Rig(profile, out XRayWeatherController controller, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting);
            try
            {
                MonolithWeatherInputs weather = root.GetComponent<MonolithWeatherInputs>();
                Light sun = root.GetComponent<Light>();
                sun.color = new Color(0.11f, 0.22f, 0.33f);
                sun.intensity = 17.0f;
                sun.transform.rotation = Quaternion.Euler(19.0f, 37.0f, 5.0f);
                Color initialColor = sun.color;
                float initialIntensity = sun.intensity;
                Quaternion initialRotation = sun.transform.rotation;
                weather.SetInputs(0.73f, new Vector3(0.9f, 0.8f, 0.7f), new Vector4(0.2f, 0.4f, 0, 0), new Vector4(1.2f, 1, 0, 0), 0.4f);
                lighting.SetBindings(new[] { sun }, new[] { new Vector3(0.13f, 0.17f, 0.19f) });
                string weatherJson = EditorJsonUtility.ToJson(weather);
                string lightingJson = EditorJsonUtility.ToJson(lighting);
                string windJson = EditorJsonUtility.ToJson(wind);
                string controllerJson = EditorJsonUtility.ToJson(controller);
                controller.BeginPreview();
                controller.TickPreview(0.1f);
                Assert.IsTrue(wind.WeatherDriven);
                Assert.That(Shader.GetGlobalVector("_MonolithWeatherSun").x, Is.EqualTo(0.7f).Within(TOLERANCE));
                Assert.That(Shader.GetGlobalVectorArray("_MonolithBoundColor64")[0].x, Is.EqualTo(0.7f).Within(TOLERANCE));
                Assert.That(weather.Wetness, Is.EqualTo(0.4f));
                Assert.That(weather.DynamicGloss, Is.EqualTo(0.73f));
                Assert.AreEqual(weatherJson, EditorJsonUtility.ToJson(weather));
                Assert.AreEqual(lightingJson, EditorJsonUtility.ToJson(lighting));
                Assert.AreEqual(windJson, EditorJsonUtility.ToJson(wind));
                Assert.AreEqual(controllerJson, EditorJsonUtility.ToJson(controller));
                controller.EndPreview();
                Assert.IsNull(XRayWeatherController.Active);
                Assert.IsFalse(wind.WeatherDriven);
                Assert.AreEqual(initialColor, sun.color);
                Assert.AreEqual(initialIntensity, sun.intensity);
                Assert.That(Quaternion.Angle(initialRotation, sun.transform.rotation), Is.LessThan(0.0001f));
                Assert.That(Shader.GetGlobalVector("_MonolithWeatherSun").x, Is.EqualTo(0.9f).Within(TOLERANCE));
                Assert.That(Shader.GetGlobalVectorArray("_MonolithBoundColor64")[0].x, Is.EqualTo(0.13f).Within(TOLERANCE));
                Assert.AreEqual(wind.State.Anim, wind.State.AnimPrev);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PreviewRejectsSecondOwnerAndDisabledControllerReleasesConsumers()
        {
            XRayWeatherProfile profile = Profile("ownership", Color.white, 0.0f, 500.0f);
            GameObject root = Rig(profile, out XRayWeatherController first, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting);
            var secondRoot = new GameObject("DIAGNOSTIC standby weather");
            try
            {
                XRayWeatherController second = secondRoot.AddComponent<XRayWeatherController>();
                second.Configure(profile, root.GetComponent<MonolithWeatherInputs>(), lighting, wind, root.GetComponent<Light>());
                first.BeginPreview();
                second.BeginPreview();
                Assert.AreSame(first, XRayWeatherController.Active);
                Assert.IsFalse(second.PreviewActive);
                first.enabled = false;
                Assert.IsNull(XRayWeatherController.Active);
                Assert.IsFalse(wind.WeatherDriven);
                second.BeginPreview();
                Assert.AreSame(second, XRayWeatherController.Active);
                second.enabled = false;
                first.enabled = true;
                first.BeginPreview();
                Assert.AreSame(first, XRayWeatherController.Active);
                Assert.AreEqual(wind.State.Anim, wind.State.AnimPrev);
            }
            finally
            {
                Object.DestroyImmediate(secondRoot);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void InvalidProfileCannotOwnOrLeaveStaleWeatherGlobals()
        {
            XRayWeatherProfile profile = Profile("valid", Color.white, 0.0f, 500.0f);
            XRayWeatherProfile invalid = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            GameObject root = Rig(profile, out XRayWeatherController controller, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting);
            try
            {
                controller.BeginPreview();
                Assert.IsTrue(wind.WeatherDriven);
                controller.SetProfile(invalid);
                Assert.IsNull(XRayWeatherController.Active);
                Assert.IsFalse(wind.WeatherDriven);
                Assert.That(controller.Diagnostic, Does.Contain("schema"));
                controller.SetProfile(profile);
                Assert.AreSame(controller, XRayWeatherController.Active);
                Assert.AreEqual(wind.State.Anim, wind.State.AnimPrev);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(invalid);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DisabledConsumerReleasesAndReacquisitionRebasesPreviousWind()
        {
            XRayWeatherProfile profile = Profile("consumer_lifecycle", Color.white, 0.0f, 500.0f);
            GameObject root = Rig(profile, out XRayWeatherController controller, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting);
            try
            {
                MonolithWeatherInputs weather = root.GetComponent<MonolithWeatherInputs>();
                Assert.IsTrue(controller.BeginPreview());
                controller.TickPreview(0.1f);
                weather.enabled = false;
                controller.TickPreview(0.1f);
                Assert.IsNull(XRayWeatherController.Active);
                Assert.IsFalse(wind.WeatherDriven);
                weather.enabled = true;
                controller.TickPreview(0.1f);
                Assert.AreSame(controller, XRayWeatherController.Active);
                Assert.IsTrue(wind.WeatherDriven);
                Assert.AreEqual(wind.State.Anim, wind.State.AnimPrev);
                Assert.AreEqual(wind.State.TreeWind, wind.State.TreeWindPrev);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }

        [TestCase(MonolithSourceLighting.LightingMode.Native)]
        [TestCase(MonolithSourceLighting.LightingMode.Calibrated)]
        [TestCase(MonolithSourceLighting.LightingMode.Bound)]
        public void ExistingLightingModesPreserveEncodedBindingsAndLinearSunRadiance(MonolithSourceLighting.LightingMode mode)
        {
            XRayWeatherProfile profile = Profile("lighting_modes", new Color(0.2f, 0.4f, 0.7f), 0.0f, 500.0f, true, new Vector3(2.0f, 1.5f, 0.5f));
            GameObject root = Rig(profile, out XRayWeatherController controller, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting);
            try
            {
                Light sun = root.GetComponent<Light>();
                sun.intensity = 7.0f;
                lighting.Mode = mode;
                lighting.BoundRadianceScale = 3.0f;
                Assert.IsTrue(controller.BeginPreview());
                controller.Seek(3600.0);
                lighting.Publish();
                Assert.AreEqual(mode, lighting.Mode);
                Assert.AreEqual((float)mode, Shader.GetGlobalFloat("_MonolithSourceLightingMode"));
                Assert.That(sun.intensity, Is.EqualTo(7.0f * Mathf.GammaToLinearSpace(0.6f)).Within(TOLERANCE));
                Assert.AreEqual(3.0f, lighting.BoundRadianceScale);
                Assert.That(sun.color.maxColorComponent, Is.EqualTo(1.0f).Within(TOLERANCE));
                AssertSunRadiance(sun, 7.0f, new Vector3(0.4f, 0.6f, 0.35f));
                Assert.That(Shader.GetGlobalVector("_MonolithWeatherSun").x, Is.EqualTo(0.2f).Within(TOLERANCE));
                Assert.That(controller.SourceLightColor.x, Is.EqualTo(0.4f).Within(TOLERANCE));
                Vector4 source = Shader.GetGlobalVectorArray("_MonolithBoundColor64")[0];
                Assert.That(source.x, Is.EqualTo(0.4f).Within(TOLERANCE));
                Assert.That(source.y, Is.EqualTo(0.6f).Within(TOLERANCE));
                Assert.That(source.z, Is.EqualTo(0.35f).Within(TOLERANCE));
                Assert.That(Vector3.Distance(sun.transform.forward, controller.Snapshot.SunDirection), Is.LessThan(TOLERANCE));
                controller.EndPreview();
                Assert.AreEqual(7.0f, sun.intensity);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SunIntensityTracksNightDayTransitionsAndRestoresWithoutCompounding()
        {
            XRayWeatherProfile cycle = Profile("sun_intensity_cycle", Color.black, 0.0f, 500.0f, secondSun: new Color(0.25f, 0.5f, 1.0f));
            XRayWeatherProfile night = Profile("sun_intensity_night", Color.black, 0.0f, 500.0f);
            GameObject root = Rig(cycle, out XRayWeatherController controller, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting);
            try
            {
                Light sun = root.GetComponent<Light>();
                sun.intensity = 3000.0f;
                sun.color = new Color(0.8f, 0.6f, 0.4f);
                Color authoredColor = sun.color;
                Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                controller.SetTimeScale(0.0f);
                controller.Seek(0.0);
                Assert.AreEqual(0.0f, sun.intensity);
                Assert.AreEqual(Color.black, sun.color);
                controller.Seek(43200.0);
                Assert.That(sun.intensity, Is.EqualTo(3000.0f).Within(0.001f));
                AssertSunRadiance(sun, 3000.0f, new Vector3(0.25f, 0.5f, 1.0f));
                controller.Seek(21600.0);
                float halfIntensity = 3000.0f * Mathf.GammaToLinearSpace(0.5f);
                Assert.That(sun.intensity, Is.EqualTo(halfIntensity).Within(0.001f));
                AssertSunRadiance(sun, 3000.0f, new Vector3(0.125f, 0.25f, 0.5f));
                controller.Seek(43200.0);
                Assert.That(sun.intensity, Is.EqualTo(3000.0f).Within(0.001f));
                Assert.IsTrue(controller.TransitionTo(night, 2.0f));
                controller.TickPreview(1.0f);
                Assert.That(sun.intensity, Is.EqualTo(halfIntensity).Within(0.001f));
                AssertSunRadiance(sun, 3000.0f, new Vector3(0.125f, 0.25f, 0.5f));
                Assert.That(controller.SourceLightColor.x, Is.EqualTo(0.125f).Within(TOLERANCE));
                Assert.That(Shader.GetGlobalVectorArray("_MonolithBoundColor64")[0].z, Is.EqualTo(0.5f).Within(TOLERANCE));
                controller.TickPreview(1.0f);
                Assert.AreEqual(0.0f, sun.intensity);
                controller.EndPreview();
                Assert.AreEqual(3000.0f, sun.intensity);
                Assert.AreEqual(authoredColor, sun.color);
                Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                controller.Seek(21600.0);
                Assert.That(sun.intensity, Is.EqualTo(halfIntensity).Within(0.001f));
                controller.enabled = false;
                Assert.AreEqual(3000.0f, sun.intensity);
                Assert.AreEqual(authoredColor, sun.color);
                controller.enabled = true;
                Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                controller.Seek(21600.0);
                Assert.That(sun.intensity, Is.EqualTo(halfIntensity).Within(0.001f));
                controller.EndPreview();
                Assert.AreEqual(3000.0f, sun.intensity);
                Assert.AreEqual(authoredColor, sun.color);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cycle);
                Object.DestroyImmediate(night);
            }
        }

        [Test]
        public void FullExistingBindingsRejectWeatherSunWithoutSilentlyDroppingIt()
        {
            XRayWeatherProfile profile = Profile("binding_capacity", Color.white, 0.0f, 500.0f);
            GameObject root = Rig(profile, out XRayWeatherController controller, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting);
            try
            {
                var pointRoot = new GameObject("DIAGNOSTIC existing point binding");
                pointRoot.transform.SetParent(root.transform, false);
                Light point = pointRoot.AddComponent<Light>();
                point.type = LightType.Point;
                var lights = new Light[64];
                var colors = new Vector3[64];
                for (int i = 0; i < lights.Length; i++)
                {
                    lights[i] = point;
                    colors[i] = new Vector3(0.13f, 0.17f, 0.19f);
                }
                lighting.SetBindings(lights, colors);
                Assert.IsFalse(controller.BeginPreview());
                Assert.IsNull(XRayWeatherController.Active);
                Assert.IsFalse(wind.WeatherDriven);
                Assert.AreEqual(64, Shader.GetGlobalInt("_MonolithBoundLightCount"));
                Assert.That(Shader.GetGlobalVectorArray("_MonolithBoundColor64")[63].x, Is.EqualTo(0.13f).Within(TOLERANCE));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void UnconfirmedWindSetupRetainsExplicitDiagnosticInputsAndReportsBlockedParity()
        {
            XRayWeatherProfile profile = Profile("unconfirmed_wind_setup", Color.white, 0.0f, 500.0f, false);
            GameObject root = Rig(profile, out XRayWeatherController controller, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting);
            try
            {
                Vector4 authored = new Vector4(12.0f, 0.2f, 0.6f, 0.25f);
                wind.SetSsfxWindTrees(authored);
                Assert.IsTrue(controller.BeginPreview());
                controller.TickPreview(0.1f);
                Assert.AreEqual(authored, wind.State.TreesSetup);
                Assert.That(controller.Diagnostic, Does.Contain("INPUT BLOCKED"));
                Assert.That(wind.Diagnostic, Does.Contain("INPUT BLOCKED"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DifferentGlobalSunTablesRejectTransitionWithoutChangingSourceInputs()
        {
            XRayWeatherProfile first = Profile("first_sun_table", Color.white, 0.0f, 500.0f);
            XRayWeatherProfile opposite = Profile("opposite_sun_table", Color.black, Mathf.PI, 900.0f);
            XRayWeatherProfile same = Profile("same_sun_table", Color.black, Mathf.PI, 900.0f);
            var positions = new Vector2[24];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(180.0f, 45.0f);
            opposite.Initialize(opposite.CycleName, opposite.SourceIdentity, opposite.Provenance, new[] { opposite.GetKeyframe(0), opposite.GetKeyframe(1) }, positions, opposite.SsfxWindTrees, null, null, Vector3.one);
            GameObject root = Rig(first, out XRayWeatherController controller, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting);
            try
            {
                Assert.IsTrue(first.Validate(out string firstError), firstError);
                Assert.IsTrue(opposite.Validate(out string oppositeError), oppositeError);
                Assert.That(Vector3.Dot(first.SunDirection(0.0), opposite.SunDirection(0.0)), Is.LessThan(-0.999999f));
                Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                controller.SetTimeScale(0.0f);
                controller.Seek(0.0);
                Vector3 sourceColor = controller.SourceLightColor;
                Vector3 direction = controller.Snapshot.SunDirection;
                bool accepted = true;
                Assert.DoesNotThrow(() => accepted = controller.TransitionTo(opposite, 2.0f));
                Assert.IsFalse(accepted);
                Assert.That(controller.Diagnostic, Does.Contain("global sun position tables"));
                Assert.IsFalse(controller.IsTransitioning);
                Assert.AreSame(first, controller.ActiveProfile);
                Assert.AreEqual(sourceColor, controller.SourceLightColor);
                Assert.AreEqual(direction, controller.Snapshot.SunDirection);
                Assert.DoesNotThrow(() => controller.TickPreview(1.0f));
                Assert.AreSame(first, controller.ActiveProfile);
                Assert.AreEqual(sourceColor, controller.SourceLightColor);
                Assert.AreEqual(direction, controller.Snapshot.SunDirection);
                Assert.AreEqual(500.0f, wind.State.Weather.Velocity);
                Assert.IsTrue(controller.TransitionTo(same, 2.0f));
                Assert.DoesNotThrow(() => controller.TickPreview(1.0f));
                Assert.That(controller.Snapshot.SunColor.r, Is.EqualTo(0.5f).Within(TOLERANCE));
                Assert.DoesNotThrow(() => controller.TickPreview(1.0f));
                Assert.AreSame(same, controller.ActiveProfile);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(same);
                Object.DestroyImmediate(opposite);
                Object.DestroyImmediate(first);
            }
        }

        private static GameObject Rig(XRayWeatherProfile profile, out XRayWeatherController controller, out XRayFloraWindProvider wind, out MonolithSourceLighting lighting)
        {
            var root = new GameObject("DIAGNOSTIC weather controller fixture");
            Light sun = root.AddComponent<Light>();
            sun.type = LightType.Directional;
            MonolithWeatherInputs weather = root.AddComponent<MonolithWeatherInputs>();
            lighting = root.AddComponent<MonolithSourceLighting>();
            lighting.Mode = MonolithSourceLighting.LightingMode.Bound;
            wind = root.AddComponent<XRayFloraWindProvider>();
            controller = root.AddComponent<XRayWeatherController>();
            controller.Configure(profile, weather, lighting, wind, sun);
            return root;
        }

        private static void AssertSunRadiance(Light sun, float authoredIntensity, Vector3 sourceColor)
        {
            Color actual = sun.color.linear * sun.intensity;
            for (int i = 0; i < 3; i++) Assert.That(actual[i], Is.EqualTo(authoredIntensity * Mathf.GammaToLinearSpace(sourceColor[i])).Within(TOLERANCE * Mathf.Max(authoredIntensity, 1.0f)), "Sun linear radiance channel " + i);
        }

        private static XRayWeatherProfile Profile(string name, Color sun, float direction, float velocity, bool savedWindSetup = true, Vector3? sunlightScale = null, Color? secondSun = null)
        {
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            var first = new XRayWeatherSnapshot("00:00:00", 0.0, sun, Color.gray, Color.gray, direction, velocity, 0.01f, 0.8f, Color.white, Color.gray, 0.2f, 300.0f, 400.0f, Color.gray, Color.gray, 0.0f, default, default);
            var second = new XRayWeatherSnapshot("12:00:00", 43200.0, secondSun ?? sun, Color.gray, Color.gray, direction, velocity, 0.01f, 0.8f, Color.white, Color.gray, 0.2f, 300.0f, 400.0f, Color.gray, Color.gray, 0.0f, default, default);
            var positions = new Vector2[24];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(0.0f, -45.0f);
            profile.Initialize(name, "controller_regression_fixture", "independent analytic inputs; float tolerance 2e-6 covers trig and scalar float roundoff", new[] { first, second }, positions, savedWindSetup ? new Vector4(11.1f, 0.17f, 0.9f, 0.1f) : (Vector4?)null, null, null, sunlightScale ?? Vector3.one);
            return profile;
        }
    }
}
