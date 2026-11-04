using System;
using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherFlareAdaptationTests
    {
        private const double NOON = 43200.0;
        private const float TOLERANCE = 0.0001f;

        [Test]
        public void EveryCatalogKeyAndMidpointHasFiniteContinuousVisibility()
        {
            XRayWeatherCatalog catalog = Catalog();
            int samples = 0;
            foreach (XRayWeatherProfile profile in catalog.Profiles)
            {
                for (int i = 0; i < profile.KeyframeCount; i++)
                {
                    double key = profile.GetKeyframe(i).GameSeconds;
                    double next = profile.GetKeyframe((i + 1) % profile.KeyframeCount).GameSeconds;
                    if (next < key) next += XRayWeatherProfile.DAY_SECONDS;
                    Vector2 at = Resolve(profile, key);
                    Vector2 before = Resolve(profile, key - 0.1);
                    Vector2 after = Resolve(profile, key + 0.1);
                    Vector2 midpoint = Resolve(profile, (key + next) * 0.5);
                    AssertVisibility(at, profile.CycleName);
                    AssertVisibility(midpoint, profile.CycleName);
                    Assert.That(Vector2.Distance(before, after), Is.LessThan(0.025f), profile.CycleName + "/" + key);
                    float visibilityBefore = XRayWeatherFlareVisibility.AtTime(before, key - 0.1);
                    float visibilityAfter = XRayWeatherFlareVisibility.AtTime(after, key + 0.1);
                    Assert.That(Mathf.Abs(visibilityAfter - visibilityBefore), Is.LessThan(0.025f), profile.CycleName + "/" + key);
                    samples += 2;
                }
            }
            Assert.AreEqual(1200, samples);
            TestContext.WriteLine($"{catalog.Profiles.Count} profiles, {samples} key and midpoint visibility samples.");
        }

        [Test]
        public void AtmosphericDefaultsAndPerProfileOverrideKeepLunarResponseSeparate()
        {
            XRayWeatherCatalog catalog = Catalog();
            XRayWeatherProfile clear = Find(catalog, "w_clear1");
            XRayWeatherProfile partly = Find(catalog, "w_partly1");
            XRayWeatherProfile dense = Find(catalog, "w_cloudy2_dark");
            Assert.That(Resolve(clear, NOON).x, Is.EqualTo(1.0f).Within(TOLERANCE));
            Assert.That(Resolve(clear, 0.0).y, Is.EqualTo(1.0f).Within(TOLERANCE));
            Assert.That(Resolve(partly, NOON).x, Is.GreaterThan(0.0f).And.LessThan(1.0f));
            Assert.That(Resolve(dense, NOON).x, Is.Zero);
            Assert.That(Resolve(dense, 0.0).y, Is.GreaterThan(0.0f).And.LessThan(Resolve(clear, 0.0).y));
            Assert.That(Resolve(Find(catalog, "w_cloudy3_dark"), NOON).x, Is.Zero);
            Assert.That(Resolve(Find(catalog, "w_storm1"), 64800.0).x, Is.Zero);
            Assert.That(Resolve(Find(catalog, "w_cloudy3"), NOON).x, Is.EqualTo(0.22f).Within(TOLERANCE));
            Vector2 twilight = Resolve(dense, NOON);
            Assert.That(XRayWeatherFlareVisibility.AtTime(twilight, 16200.0), Is.EqualTo((twilight.x + twilight.y) * 0.5f).Within(TOLERANCE));
            Assert.That(XRayWeatherFlareVisibility.AtTime(twilight, 73800.0), Is.EqualTo((twilight.x + twilight.y) * 0.5f).Within(TOLERANCE));
            foreach (double boundary in new[] { 14400.0, 18000.0, 72000.0, 75600.0 })
            {
                float before = XRayWeatherFlareVisibility.AtTime(twilight, boundary - 0.1);
                float after = XRayWeatherFlareVisibility.AtTime(twilight, boundary + 0.1);
                Assert.That(Mathf.Abs(after - before), Is.LessThan(TOLERANCE));
            }
            Assert.IsTrue(clear.TryEvaluate(NOON, out XRayWeatherSnapshot sample, out string error), error);
            var replacement = new Vector2(0.25f, 0.6f);
            Assert.AreEqual(replacement, XRayWeatherFlareVisibility.Resolve(clear, sample, replacement));
        }

        [TestCase(0.0f)]
        [TestCase(1.0f)]
        public void RendererVisibilityUsesBothTransitionProfilesAtTheSameClock(float scale)
        {
            XRayWeatherCatalog catalog = Catalog();
            XRayWeatherProfile clear = Find(catalog, "w_clear1");
            XRayWeatherProfile target = Find(catalog, "w_cloudy2_dark");
            var root = new GameObject("Flare visibility transition regression");
            try
            {
                XRayWeatherController controller = Begin(root, catalog, clear, NOON);
                controller.SetTimeScale(scale);
                Assert.IsTrue(controller.TransitionTo(target, 2.0f), controller.Diagnostic);
                Assert.AreSame(target, controller.ActiveProfile);
                CheckVisibility(controller, clear, target, 0.0f);
                controller.TickPreview(0.5f);
                CheckVisibility(controller, clear, target, 0.25f);
                controller.TickPreview(1.0f);
                CheckVisibility(controller, clear, target, 0.75f);
                controller.TickPreview(0.5f);
                CheckVisibility(controller, clear, target, 1.0f);
                Assert.IsFalse(controller.IsTransitioning);
                Assert.AreEqual(NOON + 2.0 * scale, controller.GameTimeSeconds);
                Assert.AreEqual(0.0f, controller.Celestial.EffectiveIntensity);
                if (scale == 0.0f) Assert.AreEqual("sunset_sunrise_small_cloud", controller.Celestial.FlareDefinition?.Name);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DarkSourceEffectOffAndReleaseCannotRetainAnOpticalContribution()
        {
            XRayWeatherCatalog catalog = Catalog();
            XRayWeatherProfile clear = Find(catalog, "w_clear1");
            XRayWeatherProfile dense = Find(catalog, "w_cloudy2_dark");
            var root = new GameObject("Flare contribution regression");
            try
            {
                XRayWeatherController controller = Begin(root, catalog, clear, NOON);
                XRayWeatherCelestialRenderer renderer = controller.Celestial;
                float clearIntensity = renderer.EffectiveIntensity;
                Assert.That(clearIntensity, Is.GreaterThan(0.0f));
                Assert.That(renderer.SourceBrightness, Is.EqualTo(root.GetComponentInChildren<Light>().intensity).Within(TOLERANCE));
                controller.SetFlareAppearanceOverrides(0.25f, true, 0.0f);
                Assert.That(renderer.EffectiveIntensity, Is.EqualTo(clearIntensity * 0.25f).Within(TOLERANCE));
                Assert.That(renderer.FlareFade, Is.EqualTo(1.0f));
                controller.SetProfile(dense);
                Assert.AreEqual(0.0f, renderer.EffectiveIntensity);
                controller.Seek(16200.0);
                Assert.That(renderer.WeatherVisibility, Is.EqualTo(XRayWeatherFlareVisibility.AtTime(Resolve(dense, 16200.0), 16200.0)).Within(TOLERANCE));
                controller.Seek(NOON);
                controller.SetFlareAppearanceOverrides(1.0f, false, 1.0f);
                Assert.AreEqual(1.0f, renderer.EffectiveIntensity);
                controller.SetProfile(clear);
                controller.Seek(18000.0);
                Assert.AreEqual(0.0f, renderer.SourceBrightness);
                Assert.AreEqual(0.0f, renderer.EffectiveIntensity);
                controller.SetFlareAppearanceOverrides(1.0f, true, 1.0f);
                controller.Seek(18001.0);
                Assert.That(renderer.SourceBrightness, Is.GreaterThan(0.0f).And.LessThan(1000.0f));
                Assert.That(renderer.WeatherVisibility, Is.GreaterThan(0.0f));
                Assert.That(renderer.UntintedBrightness, Is.GreaterThan(0.0f).And.LessThan(0.01f));
                controller.Seek(0.0);
                Assert.AreEqual("moon_full_clear", renderer.FlareDefinition?.Name);
                Assert.That(renderer.UntintedBrightness, Is.GreaterThan(0.0f));
                Assert.That(renderer.EffectiveIntensity, Is.GreaterThan(0.0f));
                controller.SetCelestialEffectOverrides(true, false);
                Assert.AreEqual(0.0f, renderer.EffectiveIntensity);
                controller.SetCelestialEffectOverrides(true, true);
                controller.EndPreview();
                Assert.AreEqual(0.0f, renderer.EffectiveIntensity);
                Assert.AreEqual(0.0f, renderer.SourceBrightness);
                Assert.IsNull(renderer.GeometryVisibility);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static XRayWeatherCatalog Catalog()
        {
            XRayWeatherCatalog catalog = XRayWeatherImporter.LoadPreparedCatalog();
            if (catalog == null) Assert.Ignore("Prepare the generated ordinary weather catalog.");
            Assert.AreEqual(21, catalog.Profiles.Count);
            return catalog;
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

        private static XRayWeatherController Begin(GameObject root, XRayWeatherCatalog catalog, XRayWeatherProfile profile, double time)
        {
            XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
            controller.ConfigureCatalog(catalog);
            Assert.IsTrue(controller.BeginPreview(time, profile), controller.Diagnostic);
            controller.SetCelestialEffectOverrides(true, true);
            controller.SetFlareAppearanceOverrides(1.0f, true, 1.0f);
            controller.SetPaused(false);
            return controller;
        }

        private static Vector2 Resolve(XRayWeatherProfile profile, double time)
        {
            Assert.IsTrue(profile.TryEvaluate(time, out XRayWeatherSnapshot snapshot, out string diagnostic), diagnostic);
            return XRayWeatherFlareVisibility.Resolve(profile, snapshot);
        }

        private static void AssertVisibility(Vector2 value, string cycle)
        {
            Assert.That(value.x, Is.InRange(0.0f, 1.0f), cycle);
            Assert.That(value.y, Is.InRange(0.0f, 1.0f), cycle);
        }

        private static void CheckVisibility(XRayWeatherController controller, XRayWeatherProfile from, XRayWeatherProfile to, float progress)
        {
            Vector2 first = Resolve(from, controller.GameTimeSeconds);
            Vector2 second = Resolve(to, controller.GameTimeSeconds);
            float expected = Mathf.Lerp(first.x, second.x, progress);
            Assert.That(controller.Celestial.WeatherVisibility, Is.EqualTo(expected).Within(TOLERANCE));
            Assert.That(controller.Celestial.EffectiveIntensity, Is.EqualTo(expected).Within(TOLERANCE));
        }
    }
}
