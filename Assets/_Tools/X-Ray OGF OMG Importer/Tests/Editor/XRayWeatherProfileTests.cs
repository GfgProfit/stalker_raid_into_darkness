using System;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherProfileTests
    {
        private const float TOLERANCE = 0.000002f;
        private const string ARCHIVE = "E:/Projects/stalker_raid_into_darkness_Archive/GraphicsPort/Phase07_WeatherAtmosphere/Phase07A_WeatherRuntime/TestFixtures";

        [Test]
        public void SourceTimeWeightHonorsExactKeysAndMidnight()
        {
            XRayWeatherProfile profile = Profile();
            try
            {
                Assert.IsTrue(profile.TryEvaluate(3600.0, out XRayWeatherSnapshot exact, out string error), error);
                Assert.AreEqual(100.0f, exact.WindVelocity);
                Assert.IsTrue(profile.TryEvaluate(43200.0, out XRayWeatherSnapshot middle, out error), error);
                Assert.That(middle.WindVelocity, Is.EqualTo(300.0f).Within(TOLERANCE));
                Assert.That(middle.WindDirectionRadians, Is.EqualTo(Mathf.PI).Within(TOLERANCE));
                Assert.IsTrue(profile.TryEvaluate(0.0, out XRayWeatherSnapshot midnight, out error), error);
                Assert.That(midnight.WindVelocity, Is.EqualTo(300.0f).Within(TOLERANCE));
                Assert.That(midnight.AmbientColor.r, Is.EqualTo(0.3f).Within(TOLERANCE));
                Assert.That(midnight.WindDirectionRadians, Is.EqualTo(Mathf.PI).Within(TOLERANCE));
                Assert.IsTrue(profile.TryEvaluate(-86400.0, out XRayWeatherSnapshot previousDay, out error), error);
                Assert.AreEqual(midnight.WindVelocity, previousDay.WindVelocity);
                Assert.AreEqual(midnight.SunDirection, previousDay.SunDirection);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SourceSunUsesHourlyHeadingPitchAndUnchangedLevelAxes()
        {
            XRayWeatherProfile profile = Profile();
            try
            {
                Assert.That(profile.SunDirection(0.0).x, Is.EqualTo(-0.8660254038f).Within(TOLERANCE));
                Assert.That(profile.SunDirection(0.0).y, Is.EqualTo(-0.5f).Within(TOLERANCE));
                Assert.That(profile.SunDirection(0.0).z, Is.EqualTo(0.0f).Within(TOLERANCE));
                Assert.That(profile.SunDirection(1800.0).x, Is.EqualTo(0.0f).Within(TOLERANCE));
                Assert.That(profile.SunDirection(1800.0).y, Is.EqualTo(-0.5f).Within(TOLERANCE));
                Assert.That(profile.SunDirection(1800.0).z, Is.EqualTo(0.8660254038f).Within(TOLERANCE));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void TransitionRetainsFourSkyResourcesAndRejectsUnboundedComposition()
        {
            XRayWeatherResources a = XRayWeatherResources.Blend(new XRayWeatherResources("a"), new XRayWeatherResources("b"), 0.25f);
            XRayWeatherResources b = XRayWeatherResources.Blend(new XRayWeatherResources("c"), new XRayWeatherResources("d"), 0.75f);
            XRayWeatherResources result = XRayWeatherResources.Blend(a, b, 0.5f);
            float[] expected = { 0.375f, 0.125f, 0.125f, 0.375f };
            string[] names = { "a", "b", "c", "d" };
            for (int i = 0; i < names.Length; i++)
            {
                int slot = -1;
                for (int j = 0; j < 4; j++)
                {
                    if (result.Name(j) == names[i]) slot = j;
                }
                Assert.That(slot, Is.GreaterThanOrEqualTo(0));
                Assert.AreEqual(expected[i], result.Weights[slot]);
                Assert.IsNull(result.Texture(slot));
            }
            Assert.Throws<InvalidOperationException>(() => XRayWeatherResources.Blend(result, new XRayWeatherResources("e"), 0.5f));
        }

        [Test]
        public void IncompatibleSchemaAndNonFiniteInputsAreRejected()
        {
            XRayWeatherProfile profile = Profile();
            try
            {
                Assert.IsFalse(profile.TryEvaluate(double.NaN, out _, out _));
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("_schemaVersion").intValue = 99;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsFalse(profile.Validate(out _));
                Assert.IsFalse(profile.TryEvaluate(0.0, out _, out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [TestCase(0.001f)]
        [TestCase(float.MaxValue)]
        public void EffectiveVisibilityCannotInvertOrOverflowFogDistanceClamp(float visibilityScale)
        {
            XRayWeatherProfile profile = Profile();
            try
            {
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("_visibilityScale").floatValue = visibilityScale;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsFalse(profile.Validate(out _));
                Assert.IsFalse(profile.TryEvaluate(0.0, out _, out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ImporterUsesIncludesLastParentAndSourceCloudMultiplier()
        {
            string root = Fixture("#include \"base.ltx\"\n[01:00:00]:base,override\n[23:00:00]:base\nwind_direction=10\nwind_velocity=500\n");
            XRayWeatherProfile profile = XRayWeatherImporter.Import(Index(root), "fixture", "fixture-source");
            try
            {
                Assert.IsTrue(profile.TryEvaluate(3600.0, out XRayWeatherSnapshot frame, out string error), error);
                Assert.AreEqual(123.0f, frame.WindVelocity);
                Assert.That(frame.CloudsColor.r, Is.EqualTo(0.2f).Within(TOLERANCE));
                Assert.That(frame.CloudsColor.g, Is.EqualTo(0.4f).Within(TOLERANCE));
                Assert.That(frame.CloudsColor.b, Is.EqualTo(0.6f).Within(TOLERANCE));
                Assert.That(frame.CloudsColor.a, Is.EqualTo(0.8f).Within(TOLERANCE));
                Assert.That(frame.SkyRotationRadians, Is.EqualTo(Mathf.PI / 2.0f).Within(TOLERANCE));
                Assert.That(profile.Provenance, Does.Contain("sha256="));
                Assert.IsNull(frame.SkyTexture0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [TestCase("#include \"missing.ltx\"", "Missing weather include")]
        [TestCase("#include \"fixture.ltx\"", "Cyclic weather include")]
        [TestCase("![01:00:00]\nwind_velocity=9", "DLTX")]
        [TestCase("[01:00:00]:missing", "Missing weather parent")]
        [TestCase("[a]:b\n[b]:a", "Cyclic weather inheritance")]
        public void ImporterFailsClosedOnUnsupportedOrBrokenConfiguration(string configuration, string expected)
        {
            string root = Fixture(configuration);
            InvalidDataException error = Assert.Throws<InvalidDataException>(() => XRayWeatherImporter.Import(Index(root), "fixture", "fixture-source"));
            Assert.That(error.Message, Does.Contain(expected));
        }

        [Test]
        public void ImporterRejectsStandaloneDltxRatherThanIgnoringItsValues()
        {
            string root = Fixture("#include \"base.ltx\"\n[01:00:00]:base\n[23:00:00]:base\n");
            File.WriteAllText(Path.Combine(root, "configs/environment/weathers/mod_fixture_weather.ltx"), "![01:00:00]\nwind_velocity=999");
            InvalidDataException error = Assert.Throws<InvalidDataException>(() => XRayWeatherImporter.Import(Index(root), "fixture", "fixture-source"));
            Assert.That(error.Message, Does.Contain("standalone weather DLTX"));
        }

        private static XRayWeatherProfile Profile()
        {
            var positions = new Vector2[24];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(90.0f, -30.0f);
            positions[1] = new Vector2(-90.0f, -30.0f);
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            profile.Initialize("fixture", "engine-313a751d-anchors", "Source scalar interpolation and setHP anchors", new[] { Frame("01:00:00", 3600.0, 100.0f, 350.0f, 0.1f), Frame("23:00:00", 82800.0, 500.0f, 10.0f, 0.5f) }, positions);
            return profile;
        }

        private static XRayWeatherSnapshot Frame(string key, double seconds, float velocity, float direction, float ambient)
        {
            return new XRayWeatherSnapshot(key, seconds, Color.white, new Color(ambient, ambient, ambient), Color.white, direction * Mathf.Deg2Rad, velocity, 0.02f, 0.0f, Color.white, Color.gray, 1.0f, 400.0f, 410.0f, Color.gray, Color.clear, 0.0f, new XRayWeatherResources("missing-sky"), default);
        }

        private static string Fixture(string weather)
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE07A_OUTPUT");
            string root = Path.Combine(string.IsNullOrEmpty(output) ? ARCHIVE : Path.GetFullPath(output), "SourceFixtures", Guid.NewGuid().ToString("N"));
            if (!root.StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase) && !root.StartsWith("E:/Projects/stalker_raid_into_darkness_Archive/", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Weather fixture output must be in Archive.");
            Directory.CreateDirectory(Path.Combine(root, "configs/environment/weathers"));
            File.WriteAllText(Path.Combine(root, "configs/environment/weathers/fixture.ltx"), weather);
            File.WriteAllText(Path.Combine(root, "configs/environment/weathers/base.ltx"), "[base]\nambient=night\nambient_color=.1,.2,.3\nclouds_color=.2,.4,.6,.8,2\nclouds_texture=\nfar_plane=410\nfog_color=.01,.02,.03\nfog_density=1\nfog_distance=400\nhemisphere_color=.2,.3,.4,1\nrain_color=.2,.2,.2\nrain_density=0\nsky_color=.5,.5,.5\nsky_rotation=90\nsky_texture=sky/fixture\nsun=moon\nsun_color=.1,.2,.3\nsun_shafts_intensity=.2\nthunderbolt_collection=\nthunderbolt_duration=0\nthunderbolt_period=0\ntree_amplitude_intensity=.01\nwater_intensity=.1\nwind_direction=350\nwind_velocity=100\n[override]\nwind_velocity=123\n");
            var sun = new StringBuilder();
            for (int hour = 0; hour < 24; hour++) sun.AppendLine("[" + hour.ToString("00", CultureInfo.InvariantCulture) + ":00:00]\nsun_altitude=90\nsun_longitude=-30");
            File.WriteAllText(Path.Combine(root, "configs/environment/sun_positions.ltx"), sun.ToString());
            return root;
        }

        private static XRayAssetIndex Index(string root)
        {
            var configuration = new XRaySourceConfiguration { Mode = XRaySourceMode.CustomFolder, CustomFolder = root };
            configuration.IndexScope.Clear();
            configuration.IndexScope.Add("configs");
            return XRayAssetIndex.Build(Mo2VirtualFileSystem.Plan(configuration));
        }
    }
}
