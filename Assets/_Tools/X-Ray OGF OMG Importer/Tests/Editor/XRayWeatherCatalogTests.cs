using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherCatalogTests
    {
        private const float TOLERANCE = 0.0001f;

        [TestCase("source")]
        [TestCase("settings")]
        [TestCase("sun")]
        public void CatalogRejectsProfilesFromAnotherContext(string mismatch)
        {
            var catalog = ScriptableObject.CreateInstance<XRayWeatherCatalog>();
            var baseline = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            var variant = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            try
            {
                XRayWeatherSnapshot[] frames = { ContextFrame("00:00:00", 0.0), ContextFrame("12:00:00", 43200.0) };
                var baselineSun = new Vector2[24];
                var variantSun = new Vector2[24];
                if (mismatch == "sun") variantSun[0].x = 1.0f;
                baseline.Initialize("baseline", "source", "fixture", frames, baselineSun);
                variant.Initialize("variant", mismatch == "source" ? "other" : "source", "fixture", frames, variantSun, visibilityScale: mismatch == "settings" ? 2.0f : 1.0f);
                catalog.Initialize(new[] { baseline });
                Assert.IsTrue(catalog.IsCompatible(baseline));
                Assert.IsFalse(catalog.IsCompatible(variant));
                Assert.Throws<ArgumentException>(() => catalog.Initialize(new[] { baseline, variant }));
                Assert.AreEqual(1, catalog.Profiles.Count);
            }
            finally
            {
                Object.DestroyImmediate(variant);
                Object.DestroyImmediate(baseline);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void SavedProfilesPreserveEffectiveLtxKeysAndFields()
        {
            XRayWeatherCatalog catalog = Catalog();
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "configs", "scripts", "textures/sky" });
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            if (!plan.IsValid) Assert.Ignore("The local RVM source is unavailable.");
            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            var source = new XRayEffectiveGamedata(index);
            XRayLtxDatabase positions = XRayLtxDatabase.LoadFile(index, "configs/environment/sun_positions.ltx");
            var textureGuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int frames = 0;
            foreach (XRayWeatherProfile profile in catalog.Profiles)
            {
                Assert.AreEqual(plan.ProfileName + "|" + plan.ModOrderFingerprint, profile.SourceIdentity, profile.CycleName);
                CheckSunPositions(positions, profile);
                string path = "configs/environment/weathers/" + profile.CycleName + ".ltx";
                XRayEffectiveFile file = source.Resolve(path);
                Assert.IsNotNull(file, path);
                string hash;
                using (SHA256 sha256 = SHA256.Create()) hash = BitConverter.ToString(sha256.ComputeHash(source.ReadAllBytes(path))).Replace("-", string.Empty).ToLowerInvariant();
                Assert.That(profile.Provenance, Does.Contain(path + "|" + file.SourceName + "|" + file.Location + "|sha256=" + hash), profile.CycleName);
                XRayLtxDatabase descriptors = XRayLtxDatabase.LoadFile(index, path);
                var keys = new SortedSet<string>(StringComparer.Ordinal);
                foreach (string key in descriptors.Sections.Keys)
                {
                    if (TimeSpan.TryParseExact(key, "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out _)) keys.Add(key);
                }
                Assert.AreEqual(keys.Count, profile.KeyframeCount, profile.CycleName);
                int frameIndex = 0;
                foreach (string key in keys)
                {
                    XRayWeatherSnapshot frame = profile.GetKeyframe(frameIndex++);
                    CheckFrame(descriptors, key, frame, textureGuids);
                    frames++;
                }
            }
            TestContext.WriteLine($"{catalog.Profiles.Count} profiles, {frames} source keys, {textureGuids.Count} distinct saved sky/environment/cloud textures.");
        }

        [Test]
        public void EveryCatalogCycleCanBePreviewedWithoutWritingTheAuthoredProfile()
        {
            XRayWeatherCatalog catalog = Catalog();
            XRayWeatherProfile initial = Find(catalog, "w_clear1");
            var root = new GameObject("Weather catalog preview regression");
            try
            {
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, initial);
                controller.ConfigureCatalog(catalog);
                string authored = EditorJsonUtility.ToJson(controller);
                Assert.IsTrue(controller.BeginPreview(-1.0, Find(catalog, "w_rain1")), controller.Diagnostic);
                Assert.AreSame(Find(catalog, "w_rain1"), controller.ActiveProfile);
                Assert.AreSame(initial, controller.AuthoredProfile);
                Assert.That(controller.GameTimeSeconds, Is.EqualTo(86399.0));
                controller.SetCelestialEffectOverrides(false, true);
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (XRayWeatherProfile profile in catalog.Profiles)
                {
                    Assert.IsTrue(names.Add(profile.CycleName), profile.CycleName);
                    Assert.IsTrue(catalog.Contains(profile) && catalog.IsCompatible(profile), profile.CycleName);
                    Assert.IsTrue(XRayWeatherImporter.HasSerializedResources(profile), profile.CycleName);
                    controller.SetProfile(profile);
                    Assert.AreSame(profile, controller.ActiveProfile);
                    foreach (double time in new[] { -1.0, 0.0, 1.0, 86399.0, 86400.0, 86401.0 })
                    {
                        controller.Seek(time);
                        Assert.IsTrue(profile.TryEvaluate(time, out XRayWeatherSnapshot expected, out string diagnostic), diagnostic);
                        Assert.AreEqual(expected.GameSeconds, controller.Snapshot.GameSeconds, profile.CycleName);
                        Assert.AreEqual(expected.SunColor, controller.Snapshot.SunColor, profile.CycleName);
                        Assert.AreEqual(expected.SkyResources.Weights, controller.Snapshot.SkyResources.Weights, profile.CycleName);
                        Assert.IsInstanceOf<Cubemap>(controller.Snapshot.SkyResources.Texture(0), profile.CycleName);
                        Assert.IsInstanceOf<Cubemap>(controller.Snapshot.EnvironmentResources.Texture(0), profile.CycleName);
                    }
                    Assert.IsFalse(controller.SunShaftsEnabled);
                    Assert.IsTrue(controller.LensFlareEnabled);
                }
                controller.EndPreview();
                Assert.AreNotSame(controller, XRayWeatherController.Active);
                Assert.AreEqual(authored, EditorJsonUtility.ToJson(controller));
                Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                Assert.AreSame(initial, controller.ActiveProfile);
                TestContext.WriteLine($"{names.Count} profiles selected at six wraparound samples each.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TestCase("w_clear1", "w_cloudy1")]
        [TestCase("w_clear1", "w_foggy1")]
        [TestCase("w_partly1", "w_rain1")]
        [TestCase("w_rain1", "w_storm1")]
        public void CatalogTransitionUsesOneClockAndRespectsPauseAndTimeScale(string from, string to)
        {
            XRayWeatherCatalog catalog = Catalog();
            XRayWeatherProfile first = Find(catalog, from);
            XRayWeatherProfile second = Find(catalog, to);
            var root = new GameObject("Weather catalog transition regression");
            try
            {
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, first);
                controller.ConfigureCatalog(catalog);
                Assert.IsTrue(controller.BeginPreview(86398.0, first), controller.Diagnostic);
                controller.SetPaused(false);
                controller.SetTimeScale(4.0f);
                Assert.IsTrue(controller.TransitionTo(second, 4.0f), controller.Diagnostic);
                Assert.IsFalse(controller.TransitionTo(first, 1.0f));
                controller.TickPreview(1.0f);
                CheckBlend(controller, first, second, 2.0, 0.25f);
                controller.SetPaused(true);
                controller.TickPreview(10.0f);
                CheckBlend(controller, first, second, 2.0, 0.25f);
                controller.Seek(43199.0);
                CheckBlend(controller, first, second, 43199.0, 0.25f);
                controller.SetPaused(false);
                controller.SetTimeScale(0.0f);
                controller.TickPreview(1.0f);
                CheckBlend(controller, first, second, 43199.0, 0.5f);
                controller.TickPreview(2.0f);
                Assert.IsFalse(controller.IsTransitioning);
                Assert.AreSame(second, controller.ActiveProfile);
                Assert.AreEqual(43199.0, controller.GameTimeSeconds);
                Assert.IsTrue(controller.TransitionTo(first, 0.0f), controller.Diagnostic);
                Assert.IsFalse(controller.IsTransitioning);
                Assert.AreSame(first, controller.ActiveProfile);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TestCase("w_rain1", "w_storm1", "storm_sunrise1", "", false)]
        [TestCase("w_rain1", "w_storm1", "storm_sunrise1", "", true)]
        [TestCase("w_clear1", "w_cloudy2_dark", "sunset_sunrise_small", "sunset_sunrise_small_cloud", false)]
        [TestCase("w_clear1", "w_cloudy2_dark", "sunset_sunrise_small", "sunset_sunrise_small_cloud", true)]
        public void StationaryClockTransitionCompletesWithTheTargetRendererDefinition(string from, string to, string sourceFlare, string targetFlare, bool singleTick)
        {
            const double NOON = 43200.0;
            const float DURATION = 0.25f;
            XRayWeatherCatalog catalog = Catalog();
            XRayWeatherProfile first = Find(catalog, from);
            XRayWeatherProfile second = Find(catalog, to);
            var root = new GameObject("Weather flare transition regression");
            try
            {
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, first);
                controller.ConfigureCatalog(catalog);
                Assert.IsTrue(controller.BeginPreview(NOON, first), controller.Diagnostic);
                controller.SetCelestialEffectOverrides(true, true);
                controller.SetPaused(false);
                controller.SetTimeScale(0.0f);
                Assert.IsNotNull(controller.Celestial);
                Assert.AreEqual(sourceFlare, controller.Celestial.FlareDefinition?.Name);
                Assert.AreEqual(1.0f, controller.Celestial.FlareFade);
                Assert.IsTrue(controller.TransitionTo(second, DURATION), controller.Diagnostic);
                float elapsed = singleTick ? 0.0f : DURATION * 0.375f;
                if (elapsed > 0.0f) controller.TickPreview(elapsed);
                controller.SetPaused(true);
                XRayWeatherFlareDefinition beforePause = controller.Celestial.FlareDefinition;
                float weightBeforePause = controller.Celestial.FlareFade;
                float progressBeforePause = controller.TransitionProgress;
                controller.TickPreview(5.0f);
                Assert.AreEqual(NOON, controller.GameTimeSeconds);
                Assert.AreEqual(progressBeforePause, controller.TransitionProgress);
                Assert.AreSame(beforePause, controller.Celestial.FlareDefinition);
                Assert.AreEqual(weightBeforePause, controller.Celestial.FlareFade);
                controller.SetPaused(false);
                if (!singleTick)
                {
                    controller.TickPreview(DURATION * 0.25f);
                    elapsed += DURATION * 0.25f;
                }
                controller.TickPreview(DURATION - elapsed);
                Assert.IsFalse(controller.IsTransitioning);
                Assert.AreSame(second, controller.ActiveProfile);
                Assert.AreEqual(targetFlare, controller.Snapshot.SunDefinition);
                Assert.AreEqual(string.IsNullOrEmpty(targetFlare) ? null : targetFlare, controller.Celestial.FlareDefinition?.Name);
                Assert.AreEqual(string.IsNullOrEmpty(targetFlare) ? 0.0f : 1.0f, controller.Celestial.FlareFade);
                Assert.AreEqual(NOON, controller.GameTimeSeconds);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static XRayWeatherCatalog Catalog()
        {
            XRayWeatherCatalog catalog = null;
            try
            {
                catalog = XRayWeatherImporter.LoadPreparedCatalog();
            }
            catch (InvalidDataException exception) when (exception.Message.StartsWith("Cannot resolve current weather source:", StringComparison.Ordinal))
            {
                Assert.Ignore("The local RVM source is unavailable.");
            }
            if (catalog == null) Assert.Ignore("Prepare the generated RVM ordinary weather catalog in Production.");
            Assert.AreEqual(21, catalog.Profiles.Count);
            return catalog;
        }

        private static XRayWeatherSnapshot ContextFrame(string key, double time)
        {
            return new XRayWeatherSnapshot(key, time, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.0f, 0.0f, Color.gray, Color.gray, 1.0f, 100.0f, 150.0f, Color.gray, Color.clear, 0.0f, default, default);
        }

        private static XRayWeatherProfile Find(XRayWeatherCatalog catalog, string cycle)
        {
            foreach (XRayWeatherProfile profile in catalog.Profiles)
            {
                if (profile.CycleName == cycle) return profile;
            }
            Assert.Fail("Missing catalog cycle " + cycle);
            return null;
        }

        private static void CheckBlend(XRayWeatherController controller, XRayWeatherProfile first, XRayWeatherProfile second, double time, float weight)
        {
            Assert.AreEqual(time, controller.GameTimeSeconds);
            Assert.AreEqual(weight, controller.TransitionProgress);
            Assert.IsTrue(first.TryEvaluate(time, out XRayWeatherSnapshot a, out string error), error);
            Assert.IsTrue(second.TryEvaluate(time, out XRayWeatherSnapshot b, out error), error);
            Assert.That(controller.Snapshot.FogDistance, Is.EqualTo(Mathf.Lerp(a.FogDistance, b.FogDistance, weight)).Within(TOLERANCE));
            Assert.That(controller.Snapshot.RainDensity, Is.EqualTo(Mathf.Lerp(a.RainDensity, b.RainDensity, weight)).Within(TOLERANCE));
            Assert.AreEqual(weight < 0.5f ? a.SunDefinition : b.SunDefinition, controller.Snapshot.SunDefinition);
            Assert.That(Vector4.Dot(controller.Snapshot.SkyResources.Weights, Vector4.one), Is.EqualTo(1.0f).Within(TOLERANCE));
            Assert.That(Vector4.Dot(controller.Snapshot.EnvironmentResources.Weights, Vector4.one), Is.EqualTo(1.0f).Within(TOLERANCE));
        }

        private static void CheckSunPositions(XRayLtxDatabase positions, XRayWeatherProfile profile)
        {
            for (int hour = 0; hour < 24; hour++)
            {
                string key = hour.ToString("00", CultureInfo.InvariantCulture) + ":00:00";
                double heading = ReadNumber(positions, key, "sun_altitude") * Math.PI / 180.0;
                double pitch = ReadNumber(positions, key, "sun_longitude") * Math.PI / 180.0;
                var expected = new Vector3((float)(-Math.Cos(pitch) * Math.Sin(heading)), (float)Math.Sin(pitch), (float)(Math.Cos(pitch) * Math.Cos(heading)));
                Assert.That(Vector3.Distance(expected, profile.SunDirection(hour * 3600.0)), Is.LessThan(TOLERANCE), profile.CycleName + "/" + key);
            }
        }

        private static void CheckFrame(XRayLtxDatabase data, string key, XRayWeatherSnapshot frame, Dictionary<string, string> guids)
        {
            Assert.AreEqual(key, frame.Key0);
            Assert.AreEqual(key, frame.Key1);
            Assert.AreEqual(TimeSpan.ParseExact(key, "hh\\:mm\\:ss", CultureInfo.InvariantCulture).TotalSeconds, frame.GameSeconds);
            Assert.AreEqual(ReadColor(data, key, "sun_color"), frame.SunColor);
            Assert.AreEqual(ReadColor(data, key, "ambient_color"), frame.AmbientColor);
            Assert.AreEqual(ReadColor(data, key, "hemisphere_color"), frame.HemisphereColor);
            Assert.AreEqual(ReadColor(data, key, "rain_color"), frame.RainColor);
            Assert.AreEqual(ReadColor(data, key, "fog_color"), frame.FogColor);
            Assert.AreEqual(ReadColor(data, key, "sky_color"), frame.SkyColor);
            float[] clouds = ReadNumbers(data, key, "clouds_color");
            float cloudScale = clouds.Length > 4 ? clouds[4] / 2.0f : 0.0f;
            Assert.AreEqual(new Color(clouds[0] * cloudScale, clouds[1] * cloudScale, clouds[2] * cloudScale, clouds[3]), frame.CloudsColor);
            Assert.That(frame.WindDirectionRadians, Is.EqualTo(ReadNumber(data, key, "wind_direction") * Mathf.Deg2Rad).Within(TOLERANCE));
            Assert.That(frame.SkyRotationRadians, Is.EqualTo(ReadNumber(data, key, "sky_rotation", "0") * Mathf.Deg2Rad).Within(TOLERANCE));
            Assert.AreEqual(ReadNumber(data, key, "wind_velocity"), frame.WindVelocity);
            Assert.AreEqual(ReadNumber(data, key, "tree_amplitude_intensity"), frame.TreeAmplitudeIntensity);
            Assert.AreEqual(ReadNumber(data, key, "rain_density"), frame.RainDensity);
            Assert.AreEqual(ReadNumber(data, key, "fog_density"), frame.FogDensity);
            Assert.AreEqual(ReadNumber(data, key, "fog_distance"), frame.FogDistance);
            Assert.AreEqual(ReadNumber(data, key, "far_plane"), frame.FarPlane);
            Assert.AreEqual(ReadNumber(data, key, "thunderbolt_period"), frame.ThunderboltPeriod);
            Assert.AreEqual(ReadNumber(data, key, "thunderbolt_duration"), frame.ThunderboltDuration);
            Assert.AreEqual(ReadNumber(data, key, "sun_shafts_intensity"), frame.SunShaftsIntensity);
            Assert.AreEqual(ReadNumber(data, key, "water_intensity"), frame.WaterIntensity);
            Assert.AreEqual(Read(data, key, "thunderbolt_collection"), frame.ThunderboltCollection);
            Assert.AreEqual(Read(data, key, "ambient"), frame.AmbientDefinition);
            Assert.AreEqual(Read(data, key, "sun"), frame.SunDefinition);
            string sky = Read(data, key, "sky_texture");
            CheckTexture(sky, frame.SkyResources, true, guids);
            CheckTexture(sky + "#small", frame.EnvironmentResources, true, guids);
            CheckTexture(Read(data, key, "clouds_texture"), frame.CloudsResources, false, guids);
        }

        private static void CheckTexture(string name, XRayWeatherResources resources, bool cubemap, Dictionary<string, string> guids)
        {
            Assert.AreEqual(name, resources.Name(0));
            if (string.IsNullOrEmpty(name)) return;
            Texture texture = resources.Texture(0);
            Assert.IsNotNull(texture, name);
            if (cubemap) Assert.IsInstanceOf<Cubemap>(texture, name);
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture));
            Assert.IsNotEmpty(guid, name);
            if (guids.TryGetValue(name, out string existing)) Assert.AreEqual(existing, guid, name);
            else guids.Add(name, guid);
        }

        private static string Read(XRayLtxDatabase data, string key, string field)
        {
            return XRayLtxDatabase.Unquote(data.GetValue(key, field));
        }

        private static float ReadNumber(XRayLtxDatabase data, string key, string field, string fallback = null)
        {
            return float.Parse(Read(data, key, field) ?? fallback, CultureInfo.InvariantCulture);
        }

        private static float[] ReadNumbers(XRayLtxDatabase data, string key, string field)
        {
            return Array.ConvertAll(Read(data, key, field).Trim('(', ')', ' ').Split(','), value => float.Parse(value, CultureInfo.InvariantCulture));
        }

        private static Color ReadColor(XRayLtxDatabase data, string key, string field)
        {
            float[] values = ReadNumbers(data, key, field);
            return new Color(values[0], values[1], values[2], values.Length == 4 ? values[3] : 1.0f);
        }
    }
}
