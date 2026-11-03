using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherSkyTests
    {
        private const float TOLERANCE = 0.00002f;

        [Test]
        public void FourDescriptorSkyContributionsReachNativeSettingsWithoutCollapsing()
        {
            Cubemap[] cubes = Cubes();
            XRayWeatherSkySettings settings = ScriptableObject.CreateInstance<XRayWeatherSkySettings>();
            try
            {
                XRayWeatherSnapshot snapshot = FourWay(cubes);
                settings.SetWeather(snapshot, SkyShader());
                Assert.AreSame(cubes[0], settings.Sky0);
                Assert.AreSame(cubes[2], settings.Sky1);
                Assert.AreSame(cubes[1], settings.Sky2);
                Assert.AreSame(cubes[3], settings.Sky3);
                AssertVector(new Vector4(0.375f, 0.125f, 0.125f, 0.375f), settings.Weights);
                AssertVector(new Vector4(127.0f / 255.0f, 63.0f / 255.0f, 191.0f / 255.0f, 1.0f), settings.SourceTint);
                Assert.That(settings.SourceRotation, Is.EqualTo(0.75f).Within(TOLERANCE));
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Destroy(cubes);
            }
        }

        [Test]
        public void SkyShaderBlendsEncodedResourcesBeforeSourceTintAndPower()
        {
            Cubemap[] cubes = Cubes();
            try
            {
                Color actual = RenderSky(cubes, new Vector4(0.375f, 0.125f, 0.125f, 0.375f), new Vector4(127.0f / 255.0f, 63.0f / 255.0f, 191.0f / 255.0f, 1.0f), Vector3.up, 0.0f);
                Assert.That(actual.r, Is.EqualTo(Math.Pow(0.325 * (127.0 / 255.0) * 1.70, 2.2)).Within(TOLERANCE));
                Assert.That(actual.g, Is.EqualTo(Math.Pow(0.4375 * (63.0 / 255.0) * 1.70, 2.2)).Within(TOLERANCE));
                Assert.That(actual.b, Is.EqualTo(Math.Pow(0.55 * (191.0 / 255.0) * 1.70, 2.2)).Within(TOLERANCE));
            }
            finally
            {
                Destroy(cubes);
            }
        }

        [Test]
        public void SkyShaderUsesSourceHalfBoxLowerHemisphereAndPositiveYaw()
        {
            Cubemap cube = Cube(Color.black, 256);
            try
            {
                Color[] colors = { new Color(0.1f, 0.1f, 0.1f), new Color(0.2f, 0.2f, 0.2f), new Color(0.3f, 0.3f, 0.3f), new Color(0.4f, 0.4f, 0.4f), new Color(0.5f, 0.5f, 0.5f), new Color(0.6f, 0.6f, 0.6f) };
                for (int i = 0; i < colors.Length; i++) Fill(cube, (CubemapFace)i, colors[i]);
                cube.Apply(false, false);
                Cubemap[] cubes = { cube, cube, cube, cube };
                Vector4 weights = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                Vector4 tint = Vector4.one;
                Assert.That(RenderSky(cubes, weights, tint, Vector3.up, 0.0f).r, Is.EqualTo(Math.Pow(0.3 * 1.70, 2.2)).Within(TOLERANCE));
                Assert.That(RenderSky(cubes, weights, tint, new Vector3(0.0f, -1.0f, 0.1f), 0.0f).r, Is.EqualTo(Math.Pow(0.4 * 1.70, 2.2)).Within(TOLERANCE));
                Assert.That(RenderSky(cubes, weights, tint, new Vector3(0.0f, 0.25f, 1.0f), Mathf.PI * 0.5f).r, Is.EqualTo(Math.Pow(0.2 * 1.70, 2.2)).Within(TOLERANCE));
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [TestCase(SkyIntensityMode.Exposure, 0.0f, 1.0f)]
        [TestCase(SkyIntensityMode.Exposure, 10.5f, 7.0f)]
        [TestCase(SkyIntensityMode.Multiplier, 10.5f, 1.0f)]
        [TestCase(SkyIntensityMode.Multiplier, 10.5f, 3.25f)]
        public void SkyPassHonorsHdrpIntensityAfterSourceLinearization(SkyIntensityMode mode, float exposure, float multiplier)
        {
            Cubemap[] cubes = Cubes();
            XRayWeatherSkySettings settings = ScriptableObject.CreateInstance<XRayWeatherSkySettings>();
            try
            {
                settings.SetWeather(FourWay(cubes), SkyShader());
                settings.SetHdrpIntensity(true);
                settings.skyIntensityMode.Override(mode);
                settings.exposure.Override(exposure);
                settings.multiplier.Override(multiplier);
                Color source = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation);
                Color actual = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation, settings);
                float intensity = mode == SkyIntensityMode.Exposure ? ColorUtils.ConvertEV100ToExposure(-exposure) : multiplier;
                for (int i = 0; i < 3; i++) Assert.That(actual[i], Is.EqualTo(source[i] * intensity).Within(TOLERANCE * Mathf.Max(1.0f, Mathf.Abs(source[i] * intensity))));
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Destroy(cubes);
            }
        }

        [Test]
        public void SkyPassPreservesUnitIntensityUntilHdrpIntensityIsExplicitlyEnabled()
        {
            Cubemap[] cubes = Cubes();
            XRayWeatherSkySettings settings = ScriptableObject.CreateInstance<XRayWeatherSkySettings>();
            try
            {
                settings.SetWeather(FourWay(cubes), SkyShader());
                Assert.IsFalse(settings.UseHdrpIntensity);
                Color source = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation);
                Color initial = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation, settings);
                AssertRgb(source, initial, TOLERANCE);
                settings.exposure.Override(10.5f);
                settings.multiplier.Override(7.0f);
                Color authored = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation, settings);
                AssertRgb(source, authored, TOLERANCE);
                int hash = settings.GetHashCode();
                settings.SetHdrpIntensity(true);
                Assert.AreNotEqual(hash, settings.GetHashCode());
                settings.SetHdrpIntensity(false);
                Assert.AreEqual(hash, settings.GetHashCode());
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Destroy(cubes);
            }
        }

        [TestCase(0.0f, 1.0f)]
        [TestCase(4.0f, 1.0f)]
        [TestCase(5.5f, 0.5f)]
        [TestCase(7.0f, 0.0f)]
        [TestCase(12.0f, 0.0f)]
        [TestCase(19.0f, 0.0f)]
        [TestCase(20.5f, 0.5f)]
        [TestCase(22.0f, 1.0f)]
        [TestCase(24.0f, 1.0f)]
        public void AuthoredNightAmbientFollowsGameClockAndPrecedesHdrpIntensity(float hour, float expectedWeight)
        {
            Cubemap[] cubes = Cubes();
            XRayWeatherSkySettings settings = ScriptableObject.CreateInstance<XRayWeatherSkySettings>();
            try
            {
                XRayWeatherSnapshot sourceSnapshot = FourWay(cubes);
                XRayWeatherSnapshot snapshot = Snapshot(sourceSnapshot.SkyResources, sourceSnapshot.EnvironmentResources, Color.white, hour * 3600.0);
                settings.SetWeather(snapshot, SkyShader());
                settings.SetHdrpIntensity(true);
                settings.skyIntensityMode.Override(SkyIntensityMode.Multiplier);
                settings.multiplier.Override(3.25f);
                Color ambient = new Color(0.003f, 0.009f, 0.025f);
                settings.SetNightAmbient(ambient);
                Assert.That(settings.NightWeight, Is.EqualTo(expectedWeight).Within(TOLERANCE));
                Color source = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation);
                Color actual = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation, settings);
                for (int i = 0; i < 3; i++) Assert.That(actual[i], Is.EqualTo((source[i] + ambient[i] * expectedWeight) * 3.25f).Within(TOLERANCE));
                settings.SetWeather(Snapshot(sourceSnapshot.SkyResources, sourceSnapshot.EnvironmentResources, Color.clear, hour * 3600.0), SkyShader());
                Assert.That(settings.NightWeight, Is.EqualTo(expectedWeight).Within(TOLERANCE));
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Destroy(cubes);
            }
        }

        [TestCase(20)]
        [TestCase(30)]
        [TestCase(60)]
        public void NightClockCurveIsContinuousMonotoneAndFrameBoundedAtFastTimeScale(int framesPerSecond)
        {
            XRayWeatherSkySettings settings = ScriptableObject.CreateInstance<XRayWeatherSkySettings>();
            try
            {
                double step = 10000.0 / framesPerSecond;
                float bound = (float)(step * 1.5 / 10800.0) + TOLERANCE;
                float previous = 1.0f;
                double previousTime = 0.0;
                for (double seconds = 0.0; seconds <= 86400.0 + step; seconds += step)
                {
                    settings.SetWeather(Snapshot(default, default, Color.white, seconds), null);
                    float weight = settings.NightWeight;
                    Assert.IsFalse(float.IsNaN(weight) || float.IsInfinity(weight));
                    Assert.That(weight, Is.InRange(0.0f, 1.0f));
                    Assert.That(Mathf.Abs(weight - previous), Is.LessThanOrEqualTo(bound));
                    if (previousTime >= 14400.0 && seconds <= 25200.0) Assert.That(weight, Is.LessThanOrEqualTo(previous + TOLERANCE));
                    if (previousTime >= 68400.0 && seconds <= 79200.0) Assert.That(weight, Is.GreaterThanOrEqualTo(previous - TOLERANCE));
                    previous = weight;
                    previousTime = seconds;
                }
                settings.SetWeather(Snapshot(default, default, Color.white, 86399.999), null);
                float beforeMidnight = settings.NightWeight;
                settings.SetWeather(Snapshot(default, default, Color.clear, 86400.001), null);
                Assert.That(settings.NightWeight, Is.EqualTo(beforeMidnight).Within(TOLERANCE));
                settings.SetWeather(Snapshot(default, default, Color.white, -0.001), null);
                Assert.That(settings.NightWeight, Is.EqualTo(beforeMidnight).Within(TOLERANCE));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void NightTransitionConfigurationSurvivesCloningAndControlsTheClockCurve()
        {
            XRayWeatherSkySettings settings = ScriptableObject.CreateInstance<XRayWeatherSkySettings>();
            XRayWeatherSkySettings clone = null;
            try
            {
                Assert.AreEqual(new Vector4(4.0f, 7.0f, 19.0f, 22.0f), settings.NightTransitionHours);
                settings.SetWeather(Snapshot(default, default, Color.white, 18000.0), null);
                float original = settings.NightWeight;
                int hash = settings.GetHashCode();
                Vector4 hours = new Vector4(2.0f, 8.0f, 18.0f, 23.0f);
                settings.SetNightTransitionHours(hours);
                Assert.AreNotEqual(hash, settings.GetHashCode());
                clone = Object.Instantiate(settings);
                Assert.AreEqual(hours, clone.NightTransitionHours);
                clone.SetWeather(Snapshot(default, default, Color.clear, 18000.0), null);
                Assert.That(clone.NightWeight, Is.EqualTo(0.5f).Within(TOLERANCE));
                Assert.AreNotEqual(original, clone.NightWeight);
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.SetNightTransitionHours(new Vector4(8.0f, 2.0f, 18.0f, 23.0f)));
            }
            finally
            {
                Object.DestroyImmediate(clone);
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(false, SkyIntensityMode.Exposure, 10.5f, 3.25f)]
        [TestCase(true, SkyIntensityMode.Exposure, 0.0f, 3.25f)]
        [TestCase(true, SkyIntensityMode.Exposure, 10.5f, 3.25f)]
        [TestCase(true, SkyIntensityMode.Multiplier, 10.5f, 3.25f)]
        public void NightPhysicalRadianceUsesTheCanonicalAuthoredSkyIntensity(bool useHdrpIntensity, SkyIntensityMode mode, float exposure, float multiplier)
        {
            XRayWeatherSkySettings settings = ScriptableObject.CreateInstance<XRayWeatherSkySettings>();
            try
            {
                settings.SetWeather(Snapshot(default, default, Color.white, 19800.0), SkyShader());
                Color ambient = new Color(0.003f, 0.009f, 0.025f);
                settings.SetNightAmbient(ambient);
                settings.SetHdrpIntensity(useHdrpIntensity);
                settings.skyIntensityMode.Override(mode);
                settings.exposure.Override(exposure);
                settings.multiplier.Override(multiplier);
                Vector3 actual = XRayWeatherSkyPass.GetNightRadiance(settings);
                float intensity = useHdrpIntensity ? settings.GetIntensityFromSettings() : 1.0f;
                for (int i = 0; i < 3; i++) Assert.That(actual[i], Is.EqualTo(ambient[i] * 0.5f * intensity).Within(TOLERANCE));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void NightPhysicalRadianceIsZeroWithoutAnAuthoredNightColor()
        {
            Assert.AreEqual(Vector3.zero, XRayWeatherSkyPass.GetNightRadiance(null));
            XRayWeatherSkySettings settings = ScriptableObject.CreateInstance<XRayWeatherSkySettings>();
            try
            {
                settings.SetWeather(Snapshot(default, default, Color.clear), SkyShader());
                settings.SetHdrpIntensity(true);
                settings.exposure.Override(10.5f);
                Assert.AreEqual(Vector3.zero, XRayWeatherSkyPass.GetNightRadiance(settings));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void WorldNightMultiplierChangesMaterialRadianceWithoutChangingTheRenderedSky()
        {
            Cubemap[] cubes = Cubes();
            XRayWeatherSkySettings settings = ScriptableObject.CreateInstance<XRayWeatherSkySettings>();
            try
            {
                XRayWeatherSnapshot snapshot = FourWay(cubes);
                settings.SetWeather(Snapshot(snapshot.SkyResources, snapshot.EnvironmentResources, Color.clear), SkyShader());
                settings.SetNightAmbient(new Color(0.003f, 0.009f, 0.025f));
                settings.SetHdrpIntensity(true);
                settings.skyIntensityMode.Override(SkyIntensityMode.Multiplier);
                settings.multiplier.Override(3.25f);
                Assert.AreEqual(1.0f, settings.NightDiffuseMultiplier);
                Vector3 initialRadiance = XRayWeatherSkyPass.GetNightRadiance(settings);
                Color initialSky = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation, settings);
                int hash = settings.GetHashCode();
                settings.SetNightDiffuseMultiplier(24.0f);
                Vector3 scaledRadiance = XRayWeatherSkyPass.GetNightRadiance(settings);
                Color scaledSky = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation, settings);
                Assert.AreNotEqual(hash, settings.GetHashCode());
                for (int i = 0; i < 3; i++) Assert.That(scaledRadiance[i], Is.EqualTo(initialRadiance[i] * 24.0f).Within(TOLERANCE));
                AssertRgb(initialSky, scaledSky, TOLERANCE);
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Destroy(cubes);
            }
        }

        [Test]
        public void NightAmbientDefaultsPreserveSourceAndAuthoredSettingsSurviveCloning()
        {
            Cubemap[] cubes = Cubes();
            XRayWeatherSkySettings settings = ScriptableObject.CreateInstance<XRayWeatherSkySettings>();
            XRayWeatherSkySettings clone = null;
            try
            {
                XRayWeatherSnapshot sourceSnapshot = FourWay(cubes);
                settings.SetWeather(Snapshot(sourceSnapshot.SkyResources, sourceSnapshot.EnvironmentResources, Color.clear), SkyShader());
                Assert.AreEqual(Color.black, settings.NightAmbient);
                Assert.AreEqual(1.0f, settings.NightWeight);
                var serialized = new SerializedObject(settings);
                Assert.IsFalse(serialized.FindProperty("_nightAmbient.m_OverrideState").boolValue);
                Color source = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation);
                Color actual = RenderSky(cubes, settings.Weights, settings.SourceTint, Vector3.up, settings.SourceRotation, settings);
                AssertRgb(source, actual, TOLERANCE);
                int hash = settings.GetHashCode();
                Color ambient = new Color(0.003f, 0.009f, 0.025f);
                settings.SetNightAmbient(ambient);
                Assert.AreNotEqual(hash, settings.GetHashCode());
                settings.SetHdrpIntensity(true);
                clone = Object.Instantiate(settings);
                Assert.AreEqual(ambient, clone.NightAmbient);
                Assert.AreEqual(1.0f, clone.NightWeight);
                Assert.IsTrue(clone.UseHdrpIntensity);
                Color clonedOutput = RenderSky(cubes, clone.Weights, clone.SourceTint, Vector3.up, clone.SourceRotation, clone);
                float intensity = ColorUtils.ConvertEV100ToExposure(-clone.exposure.value);
                for (int i = 0; i < 3; i++) Assert.That(clonedOutput[i], Is.EqualTo((source[i] + ambient[i]) * intensity).Within(TOLERANCE));
                settings.SetHdrpIntensity(false);
                settings.SetNightAmbient(Color.black);
                serialized.Update();
                Assert.IsTrue(serialized.FindProperty("_nightAmbient.m_OverrideState").boolValue);
                Assert.AreNotEqual(hash, settings.GetHashCode());
            }
            finally
            {
                Object.DestroyImmediate(clone);
                Object.DestroyImmediate(settings);
                Destroy(cubes);
            }
        }

        [Test]
        public void SkyReleaseAndDisableRestoreAuthoredAndInstantiatedVolumeProfiles()
        {
            var root = new GameObject("Weather sky ownership regression");
            VolumeProfile authored = ScriptableObject.CreateInstance<VolumeProfile>();
            VolumeProfile instantiated = ScriptableObject.CreateInstance<VolumeProfile>();
            Cubemap[] cubes = Cubes();
            try
            {
                VisualEnvironment environment = authored.Add<VisualEnvironment>(true);
                environment.skyType.Override(1);
                Volume volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = authored;
                XRayWeatherSkyRenderer renderer = root.AddComponent<XRayWeatherSkyRenderer>();
                renderer.Configure(volume, SkyShader());
                XRayWeatherSnapshot snapshot = FourWay(cubes);
                Assert.IsTrue(renderer.TrySetWeather(root, snapshot, 12.5), renderer.Diagnostic);
                Assert.AreNotSame(authored, volume.profile);
                Assert.AreEqual(1, environment.skyType.value);
                Assert.AreEqual(12.5, renderer.SourceTime);
                Assert.IsFalse(renderer.TrySetWeather(instantiated, snapshot, 20.0));
                renderer.ReleaseWeather(instantiated);
                Assert.IsTrue(renderer.HasWeather);
                renderer.ReleaseWeather(root);
                Assert.IsFalse(renderer.HasWeather);
                Assert.IsFalse(volume.HasInstantiatedProfile());
                Assert.AreSame(authored, volume.sharedProfile);
                volume.profile = instantiated;
                Assert.IsTrue(renderer.TrySetWeather(root, snapshot, 30.0), renderer.Diagnostic);
                renderer.enabled = false;
                Assert.AreSame(instantiated, volume.profile);
                Assert.IsFalse(renderer.HasWeather);
                renderer.enabled = true;
                Assert.IsTrue(renderer.TrySetWeather(root, snapshot, 40.0), renderer.Diagnostic);
                Object.DestroyImmediate(renderer);
                Assert.AreSame(instantiated, volume.profile);
                Assert.AreEqual(1, environment.skyType.value);
            }
            finally
            {
                Object.DestroyImmediate(root);
                for (int i = 0; i < authored.components.Count; i++) Object.DestroyImmediate(authored.components[i]);
                Object.DestroyImmediate(authored);
                Object.DestroyImmediate(instantiated);
                Destroy(cubes);
            }
        }

        [Test]
        public void MissingSkyReferenceCannotAcquireOrChangeAuthoredVolume()
        {
            var root = new GameObject("Weather sky missing reference regression");
            VolumeProfile authored = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                Volume volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = authored;
                XRayWeatherSkyRenderer renderer = root.AddComponent<XRayWeatherSkyRenderer>();
                renderer.Configure(volume, SkyShader());
                XRayWeatherSnapshot snapshot = Snapshot(new XRayWeatherResources("missing"), default);
                Assert.IsFalse(renderer.TrySetWeather(root, snapshot, 0.0));
                StringAssert.Contains("missing", renderer.Diagnostic);
                Assert.IsFalse(renderer.HasWeather);
                Assert.IsFalse(volume.HasInstantiatedProfile());
                Assert.AreSame(authored, volume.sharedProfile);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(authored);
            }
        }

        [Test]
        public void EnvironmentPublishesSourceCombineInputsAndPreservesMaterialObjectMasks()
        {
            var root = new GameObject("Weather environment globals regression");
            Cubemap[] cubes = Cubes();
            Material material = new Material(Shader.Find("HDRP/MonolithLit"));
            try
            {
                material.SetFloat("_MonolithEnvHemisphere", 0.37f);
                material.SetFloat("_MonolithEnvHudHemi", 0.19f);
                material.SetFloat("_MonolithEnvHudMask", 1.0f);
                MonolithWeatherInputs inputs = root.AddComponent<MonolithWeatherInputs>();
                Assert.IsTrue(inputs.TrySetWeatherInputs(root, Vector3.one));
                XRayWeatherSnapshot snapshot = FourWay(cubes);
                inputs.SetWeatherEnvironment(root, snapshot, new Vector2(0.5f, 1.25f));
                Assert.IsTrue(inputs.EnvironmentActive);
                AssertVector(new Vector4(0.0005f, 0.02f, 0.04f, 0.0f), Shader.GetGlobalVector("_MonolithWeatherAmbient"));
                AssertVector(new Vector4(1.000025f, 2.000025f, 3.000025f, 0.0f), Shader.GetGlobalVector("_MonolithWeatherEnvironmentColor"));
                AssertVector(new Vector4(0.375f, 0.125f, 0.125f, 0.375f), Shader.GetGlobalVector("_MonolithWeatherEnvironmentWeights"));
                int[] order = { 0, 2, 1, 3 };
                for (int i = 0; i < cubes.Length; i++) Assert.AreSame(cubes[order[i]], Shader.GetGlobalTexture("_MonolithWeatherEnvironment" + i));
                Assert.AreEqual(0.37f, material.GetFloat("_MonolithEnvHemisphere"));
                Assert.AreEqual(0.19f, material.GetFloat("_MonolithEnvHudHemi"));
                Assert.AreEqual(1.0f, material.GetFloat("_MonolithEnvHudMask"));
                inputs.SetWeatherEnvironment(root, snapshot, null);
                Assert.IsFalse(inputs.EnvironmentActive);
                Assert.AreEqual(0.0f, Shader.GetGlobalFloat("_MonolithWeatherEnvironmentActive"));
                inputs.SetWeatherEnvironment(root, snapshot, Vector2.one);
                inputs.ReleaseWeatherInputs(root);
                Assert.IsFalse(inputs.EnvironmentActive);
                Assert.AreEqual(0.0f, Shader.GetGlobalFloat("_MonolithWeatherEnvironmentActive"));
                Assert.IsTrue(inputs.TrySetWeatherInputs(root, Vector3.one));
                inputs.SetWeatherEnvironment(root, snapshot, Vector2.one);
                inputs.enabled = false;
                inputs.enabled = true;
                Assert.IsTrue(inputs.TrySetWeatherInputs(material, Vector3.one));
                Assert.IsFalse(inputs.EnvironmentActive);
                Assert.AreEqual(0.0f, Shader.GetGlobalFloat("_MonolithWeatherEnvironmentActive"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(material);
                Destroy(cubes);
            }
        }

        [Test]
        public void DestroyedSkyRendererReleasesControllerEnvironmentAndRestoresVolume()
        {
            var root = new GameObject("Weather sky destroyed consumer regression");
            Cubemap[] cubes = Cubes();
            XRayWeatherProfile profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            VolumeProfile authored = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                XRayWeatherSnapshot first = FourWay(cubes);
                profile.Initialize("destroyed_sky_regression", "analytic constants", "destroyed consumer lifetime", new[] { first, first.WithSampling(43200.0, Vector3.down) }, new Vector2[24], new Vector4(11.1f, 0.17f, 0.9f, 0.1f), null, null, Vector3.one, 1.0f, Vector2.one);
                Light sun = root.AddComponent<Light>();
                sun.type = LightType.Directional;
                Color originalColor = sun.color;
                Quaternion originalRotation = sun.transform.rotation;
                MonolithWeatherInputs inputs = root.AddComponent<MonolithWeatherInputs>();
                MonolithSourceLighting lighting = root.AddComponent<MonolithSourceLighting>();
                XRayFloraWindProvider wind = root.AddComponent<XRayFloraWindProvider>();
                Volume volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = authored;
                XRayWeatherSkyRenderer sky = root.AddComponent<XRayWeatherSkyRenderer>();
                sky.Configure(volume, SkyShader());
                XRayWeatherController controller = root.AddComponent<XRayWeatherController>();
                controller.Configure(profile, inputs, lighting, wind, sun);
                controller.ConfigureSky(sky);
                Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                Assert.IsTrue(inputs.EnvironmentActive);
                Object.DestroyImmediate(sky);
                controller.TickPreview(0.1f);
                Assert.IsNull(XRayWeatherController.Active);
                Assert.IsFalse(inputs.EnvironmentActive);
                Assert.AreEqual(0.0f, Shader.GetGlobalFloat("_MonolithWeatherEnvironmentActive"));
                Assert.IsFalse(volume.HasInstantiatedProfile());
                Assert.AreSame(authored, volume.sharedProfile);
                Assert.AreEqual(originalColor, sun.color);
                Assert.That(Quaternion.Angle(originalRotation, sun.transform.rotation), Is.LessThan(TOLERANCE));
                controller.EndPreview();
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(authored);
                Destroy(cubes);
            }
        }

        [Test]
        public void EnvironmentShaderSamplesFourCubesOnlyForOptedInAnomalyMaterials()
        {
            var root = new GameObject("Weather environment shader regression");
            Cubemap[] cubes = Cubes();
            try
            {
                MonolithWeatherInputs inputs = root.AddComponent<MonolithWeatherInputs>();
                Assert.IsTrue(inputs.TrySetWeatherInputs(root, Vector3.one));
                inputs.SetWeatherEnvironment(root, FourWay(cubes), Vector2.one);
                ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/XRayWeatherEnvironmentValidation.compute");
                Assert.IsNotNull(shader);
                int kernel = shader.FindKernel("WeatherEnvironment");
                shader.SetTexture(kernel, "_MonolithEnv0", cubes[0]);
                shader.SetTexture(kernel, "_MonolithEnv1", cubes[0]);
                shader.SetVector("_MonolithEnvColor", Vector4.one);
                shader.SetVector("_MonolithEnvAmbient", Vector4.zero);
                shader.SetFloat("_MonolithEnvHemisphere", 0.37f);
                shader.SetFloat("_MonolithEnvHudMask", 0.0f);
                shader.SetFloat("_MonolithTrueStalkerActive", 0.0f);
                shader.SetFloat("_MonolithDebug", 0.0f);
                for (int i = 0; i < 4; i++) shader.SetTexture(kernel, "_MonolithWeatherEnvironment" + i, Shader.GetGlobalTexture("_MonolithWeatherEnvironment" + i));
                using (var output = new ComputeBuffer(3, 16))
                {
                    var actual = new Vector4[3];
                    shader.SetBuffer(kernel, "Output", output);
                    shader.SetFloat("_MonolithSourceProfile", 1.0f);
                    shader.Dispatch(kernel, 1, 1, 1);
                    output.GetData(actual);
                    AssertRgb(new Color(0.325f, 0.4375f, 0.55f), actual[0], TOLERANCE);
                    Assert.That(actual[1].x, Is.EqualTo(Math.Pow(0.325 * 0.80002 * 0.37 + 0.001, 2.2)).Within(TOLERANCE));
                    Assert.That(actual[1].y, Is.EqualTo(Math.Pow(0.4375 * 1.60002 * 0.37 + 0.04, 2.2)).Within(TOLERANCE));
                    Assert.That(actual[1].z, Is.EqualTo(Math.Pow(0.55 * 2.40002 * 0.37 + 0.08, 2.2)).Within(TOLERANCE));
                    Assert.That(actual[2].z, Is.EqualTo(0.37f).Within(TOLERANCE));
                    shader.SetFloat("_MonolithSourceProfile", 0.0f);
                    shader.Dispatch(kernel, 1, 1, 1);
                    output.GetData(actual);
                    AssertRgb(new Color(0.1f, 0.2f, 0.3f), actual[0], TOLERANCE);
                    inputs.ReleaseWeatherInputs(root);
                    shader.SetFloat("_MonolithSourceProfile", 1.0f);
                    shader.Dispatch(kernel, 1, 1, 1);
                    output.GetData(actual);
                    AssertRgb(new Color(0.1f, 0.2f, 0.3f), actual[0], TOLERANCE);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Destroy(cubes);
            }
        }

        private static XRayWeatherSnapshot Snapshot(XRayWeatherResources sky, XRayWeatherResources environment, Color? sunColor = null, double gameSeconds = 0.0)
        {
            return new XRayWeatherSnapshot("00:00:00", gameSeconds, sunColor ?? Color.white, new Color(0.0001f, 0.02f, 0.04f), new Color(0.2f, 0.4f, 0.6f, 0.89f), 0.0f, 100.0f, 0.01f, 0.0f, Color.white, Color.gray, 0.1f, 300.0f, 400.0f, new Color(0.5f, 0.25f, 0.75f), Color.clear, 0.75f, sky, default, environmentResources: environment);
        }

        private static XRayWeatherSnapshot FourWay(Cubemap[] cubes)
        {
            XRayWeatherResources a = XRayWeatherResources.Blend(new XRayWeatherResources("a", cubes[0]), new XRayWeatherResources("b", cubes[1]), 0.25f);
            XRayWeatherResources b = XRayWeatherResources.Blend(new XRayWeatherResources("c", cubes[2]), new XRayWeatherResources("d", cubes[3]), 0.75f);
            XRayWeatherResources resources = XRayWeatherResources.Blend(a, b, 0.5f);
            return Snapshot(resources, resources);
        }

        private static Cubemap[] Cubes()
        {
            return new[] { Cube(new Color(0.1f, 0.2f, 0.3f)), Cube(new Color(0.2f, 0.3f, 0.4f)), Cube(new Color(0.3f, 0.5f, 0.7f)), Cube(new Color(0.6f, 0.7f, 0.8f)) };
        }

        private static Cubemap Cube(Color color, int size = 4)
        {
            var cube = new Cubemap(size, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None);
            cube.filterMode = FilterMode.Bilinear;
            cube.wrapMode = TextureWrapMode.Clamp;
            for (int i = 0; i < 6; i++) Fill(cube, (CubemapFace)i, color);
            cube.Apply(false, false);
            return cube;
        }

        private static void Fill(Cubemap cube, CubemapFace face, Color color)
        {
            Color[] pixels = new Color[cube.width * cube.width];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            cube.SetPixels(pixels, face);
        }

        private static Shader SkyShader()
        {
            Shader shader = Shader.Find("XRay/Monolith Weather Sky");
            Assert.IsNotNull(shader);
            Assert.IsTrue(shader.isSupported);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
            return shader;
        }

        private static Color RenderSky(Cubemap[] cubes, Vector4 weights, Vector4 tint, Vector3 direction, float rotation, XRayWeatherSkySettings settings = null)
        {
            var material = new Material(SkyShader());
            var target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            XRayWeatherSkyPass renderer = settings != null ? new XRayWeatherSkyPass() : null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                for (int i = 0; i < cubes.Length; i++) material.SetTexture("_XRaySky" + i, cubes[i]);
                material.SetVector("_XRaySkyWeights", weights);
                material.SetVector("_XRaySkyTint", tint);
                material.SetVector("_XRaySkyRotation", new Vector4(Mathf.Cos(rotation), Mathf.Sin(rotation), 0.0f, 0.0f));
                Matrix4x4 matrix = Matrix4x4.zero;
                matrix.SetRow(3, new Vector4(-direction.x, -direction.y, -direction.z, 1.0f));
                material.SetMatrix("_PixelCoordToViewDirWS", matrix);
                target.Create();
                GL.sRGBWrite = false;
                using (var commands = new CommandBuffer())
                {
                    commands.SetRenderTarget(target);
                    commands.ClearRenderTarget(false, true, Color.magenta);
                    if (renderer == null)
                    {
                        commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                    }
                    else
                    {
                        renderer.Build();
                        var parameters = new BuiltinSkyParameters { commandBuffer = commands, skySettings = settings, pixelCoordToViewDirMatrix = matrix };
                        renderer.RenderSky(parameters, true, false);
                    }
                    Graphics.ExecuteCommandBuffer(commands);
                }
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                readback.Apply(false, false);
                return readback.GetPixel(2, 2);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (renderer != null) renderer.Cleanup();
                target.Release();
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
            }
        }

        private static void AssertRgb(Color expected, Color actual, float tolerance)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance));
        }

        private static void AssertVector(Vector4 expected, Vector4 actual)
        {
            for (int i = 0; i < 4; i++) Assert.That(actual[i], Is.EqualTo(expected[i]).Within(TOLERANCE));
        }

        private static void Destroy(Cubemap[] cubes)
        {
            for (int i = 0; i < cubes.Length; i++) Object.DestroyImmediate(cubes[i]);
        }
    }
}
