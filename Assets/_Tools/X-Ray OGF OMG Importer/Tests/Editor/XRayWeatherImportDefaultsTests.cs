using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherImportDefaultsTests
    {
        [Test]
        public void DefaultIsAutomaticAndClonePreservesExplicitOptOut()
        {
            var settings = new XRayLevelImportSettings();
            Assert.AreEqual(XRayWeatherImportMode.Automatic, settings.WeatherImportMode);
            settings.WeatherImportMode = XRayWeatherImportMode.Disabled;
            Assert.AreEqual(XRayWeatherImportMode.Disabled, settings.Clone().WeatherImportMode);
            Assert.IsNull(XRayWeatherImporter.ResolveLevelProfile(settings, null, "Assets/UnusedWeatherImportFixture", new XRayLevelImportRecord()));
        }

        [Test]
        public void ExplicitProfilesAndSourceGuardsPreventUnexpectedAutomaticImports()
        {
            XRayWeatherProfile selected = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            try
            {
                var settings = new XRayLevelImportSettings { WeatherProfile = selected };
                Assert.AreSame(selected, XRayWeatherImporter.ResolveLevelProfile(settings, null, "Assets/UnusedWeatherImportFixture", null, false));
                settings.WeatherImportMode = XRayWeatherImportMode.CustomProfile;
                Assert.AreSame(selected, XRayWeatherImporter.ResolveLevelProfile(settings, null, "Assets/UnusedWeatherImportFixture", null));
                settings.WeatherImportMode = XRayWeatherImportMode.Disabled;
                Assert.IsNull(XRayWeatherImporter.ResolveLevelProfile(settings, null, "Assets/UnusedWeatherImportFixture", null));
                settings.WeatherImportMode = XRayWeatherImportMode.Automatic;
                settings.WeatherProfile = null;
                var record = new XRayLevelImportRecord();
                Assert.IsNull(XRayWeatherImporter.ResolveLevelProfile(settings, null, "Assets/UnusedWeatherImportFixture", record));
                Assert.IsNull(XRayWeatherImporter.ResolveLevelProfile(settings, null, "Assets/UnusedWeatherImportFixture", record, false));
                Assert.AreEqual(0, record.GeneratedAssets.Count);
                Assert.IsTrue(record.Limitations.Exists(value => value.Contains("saved scenes")));
            }
            finally
            {
                Object.DestroyImmediate(selected);
            }
        }

        [Test]
        public void LegacySceneStaysIneligibleAcrossSuccessiveImportRecords()
        {
            var legacy = new XRayLevelImportRecord { Settings = "{\"_weatherProfile\":null}" };
            bool first = XRayWeatherImporter.IsAutomaticEligible(true, legacy);
            Assert.IsFalse(first);
            var firstImport = new XRayLevelImportRecord { Settings = JsonUtility.ToJson(new XRayLevelImportSettings()) };
            StringAssert.Contains("_weatherImportMode", firstImport.Settings);
            firstImport.SetMetric(XRayWeatherImporter.AUTOMATIC_ELIGIBILITY_METRIC, first ? 1.0 : 0.0);
            bool second = XRayWeatherImporter.IsAutomaticEligible(true, firstImport);
            Assert.IsFalse(second);
            var secondImport = new XRayLevelImportRecord { Settings = firstImport.Settings };
            secondImport.SetMetric(XRayWeatherImporter.AUTOMATIC_ELIGIBILITY_METRIC, second ? 1.0 : 0.0);
            Assert.IsFalse(XRayWeatherImporter.IsAutomaticEligible(true, secondImport));
            Assert.IsTrue(XRayWeatherImporter.IsAutomaticEligible(false, null));
            var automatic = new XRayLevelImportRecord();
            automatic.SetMetric(XRayWeatherImporter.AUTOMATIC_ELIGIBILITY_METRIC, 1.0);
            Assert.IsTrue(XRayWeatherImporter.IsAutomaticEligible(true, automatic));
        }

        [Test]
        public void SkyFactoryRequiresResourcesPersistsComponentsAndPreservesExistingSettings()
        {
            string parent = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures";
            string folder = parent + "/WeatherDefaults_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string path = folder + "/SkyProfile.asset";
            XRayWeatherProfile weather = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            VolumeProfile authored = ScriptableObject.CreateInstance<VolumeProfile>();
            Cubemap cube = null;
            try
            {
                Initialize(weather, null);
                Assert.IsTrue(weather.Validate(out string diagnostic), diagnostic);
                Assert.IsFalse(XRayWeatherImporter.HasSerializedResources(weather));
                GradientSky gradient = authored.Add<GradientSky>(false);
                gradient.skyIntensityMode.Override(SkyIntensityMode.Lux);
                gradient.exposure.Override(10.5f);
                gradient.multiplier.Override(2.5f);
                gradient.desiredLuxValue.Override(321.0f);
                gradient.upperHemisphereLuxValue.Override(0.75f);
                authored.Add<Exposure>(true).fixedExposure.Override(9.0f);
                Shader shader = Shader.Find("XRay/Monolith Weather Sky");
                Assert.IsNotNull(shader);
                Assert.Throws<System.IO.InvalidDataException>(() => XRayWeatherSkySetup.GetOrCreateSkyProfile(weather, 12.0f, shader, gradient, authored, path));
                Assert.IsFalse(AssetDatabase.IsValidFolder(folder));
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
                cube = new Cubemap(1, TextureFormat.RGBA32, false);
                AssetDatabase.CreateAsset(cube, folder + "/Cube.asset");
                Initialize(weather, cube);
                VolumeProfile profile = XRayWeatherSkySetup.GetOrCreateSkyProfile(weather, 12.0f, shader, gradient, authored, path);
                Assert.AreEqual(2, profile.components.Count);
                Assert.IsFalse(profile.TryGet(out Exposure ignoredExposure));
                Assert.IsFalse(profile.TryGet(out Fog ignoredFog));
                Assert.IsTrue(profile.TryGet(out XRayWeatherSkySettings sky));
                Assert.IsTrue(profile.TryGet(out VisualEnvironment visual));
                Assert.AreEqual(XRayWeatherSkySettings.SKY_ID, visual.skyType.value);
                Assert.AreEqual(SkyIntensityMode.Lux, sky.skyIntensityMode.value);
                Assert.AreEqual(321.0f, sky.desiredLuxValue.value);
                Assert.AreEqual(0.75f, sky.upperHemisphereLuxValue.value);
                Assert.IsTrue(sky.UseHdrpIntensity);
                Assert.AreEqual(XRayWeatherSkySetup.DEFAULT_NIGHT_AMBIENT, sky.NightAmbient);
                Assert.AreEqual(XRayWeatherSkySetup.DEFAULT_NIGHT_DIFFUSE_MULTIPLIER, sky.NightDiffuseMultiplier);
                foreach (VolumeComponent component in profile.components)
                {
                    Assert.IsTrue(AssetDatabase.Contains(component));
                    Assert.AreEqual(path, AssetDatabase.GetAssetPath(component));
                }
                sky.exposure.Override(8.25f);
                sky.SetHdrpIntensity(false);
                sky.SetNightAmbient(Color.red);
                EditorUtility.SetDirty(sky);
                AssetDatabase.SaveAssetIfDirty(profile);
                Assert.AreSame(profile, XRayWeatherSkySetup.GetOrCreateSkyProfile(weather, 12.0f, shader, gradient, authored, path));
                Assert.AreEqual(8.25f, sky.exposure.value);
                Assert.IsFalse(sky.UseHdrpIntensity);
                Assert.AreEqual(Color.red, sky.NightAmbient);
                Assert.AreEqual(10.5f, gradient.exposure.value);
                Assert.AreEqual(9.0f, authored.components[1] is Exposure exposure ? exposure.fixedExposure.value : -1.0f);
            }
            finally
            {
                Object.DestroyImmediate(weather);
                foreach (VolumeComponent component in authored.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(authored);
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
                if (cube != null) Object.DestroyImmediate(cube);
            }
        }

        private static void Initialize(XRayWeatherProfile profile, Cubemap cube)
        {
            var frames = new[] { Frame("00:00:00", 0.0, cube), Frame("12:00:00", 43200.0, cube) };
            profile.Initialize("w_clear1", "automatic weather regression fixture", "not a source-game profile", frames, new Vector2[24]);
        }

        private static XRayWeatherSnapshot Frame(string key, double seconds, Cubemap cube)
        {
            return new XRayWeatherSnapshot(key, seconds, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.004f, 0.0f, Color.gray, Color.gray, 0.5f, 500.0f, 1000.0f, Color.gray, Color.clear, 0.0f, new XRayWeatherResources("fixture_sky", cube), new XRayWeatherResources(string.Empty), environmentResources: new XRayWeatherResources("fixture_environment", cube));
        }
    }
}
