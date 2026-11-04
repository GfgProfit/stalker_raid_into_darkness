using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherCelestialTests
    {
        private const float TOLERANCE = 0.000001f;

        [TestCase(0.0f, 0.07f)]
        [TestCase(0.045f, 0.09835f)]
        [TestCase(0.09f, 0.1267f)]
        [TestCase(0.18f, 0.1834f)]
        [TestCase(0.32f, 0.2716f)]
        [TestCase(-1.0f, 0.0f)]
        [TestCase(2.0f, 1.0f)]
        [TestCase(float.NaN, 0.0f)]
        [TestCase(float.PositiveInfinity, 0.0f)]
        public void ShaftsUsesIndependentSourceValues(float raw, float expected)
        {
            Assert.That(XRayWeatherCelestialRenderer.ResolveShaftsIntensity(raw, 0.1f, 0.7f), Is.EqualTo(expected).Within(TOLERANCE));
        }

        [TestCase(0.25f, 0.285f, "moon_full_clear", 0.24955f)]
        [TestCase(0.5f, 0.25f, "moon_full_verycloudydark", 0.2275f)]
        [TestCase(0.75f, 0.215f, "moon_full_verycloudydark", 0.20545f)]
        public void CycleBlendKeepsRawIntensityAndSelectsOneDefinition(float weight, float raw, string definition, float effective)
        {
            XRayWeatherSnapshot a = Frame(0.0, "moon_full_clear", 0.32f);
            XRayWeatherSnapshot b = Frame(0.0, "moon_full_verycloudydark", 0.18f);
            XRayWeatherSnapshot mixed = XRayWeatherSnapshot.Blend(a, b, weight);
            Assert.That(mixed.SunShaftsIntensity, Is.EqualTo(raw).Within(TOLERANCE));
            Assert.That(mixed.SunDefinition, Is.EqualTo(definition));
            Assert.That(XRayWeatherCelestialRenderer.ResolveShaftsIntensity(mixed.SunShaftsIntensity, 0.1f, 0.7f), Is.EqualTo(effective).Within(TOLERANCE));
            Assert.That(a.SunShaftsIntensity, Is.EqualTo(0.32f));
            Assert.That(b.SunShaftsIntensity, Is.EqualTo(0.18f));
        }

        [TestCase(86399.0, "moon_full_verycloudydark", 0.24883333f)]
        [TestCase(0.0, "moon_full_clear", 0.25f)]
        [TestCase(1.0, "moon_full_clear", 0.25116667f)]
        [TestCase(86401.0, "moon_full_clear", 0.25116667f)]
        public void DescriptorSelectionAndRawInterpolationWrapMidnight(double seconds, string definition, float expected)
        {
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            try
            {
                profile.Initialize("test", "celestial fixture", "source midpoint", new[] { Frame(60.0, "moon_full_clear", 0.32f), Frame(86340.0, "moon_full_verycloudydark", 0.18f) }, new Vector2[24]);
                Assert.That(profile.TryEvaluate(seconds, out XRayWeatherSnapshot snapshot, out string diagnostic), Is.True, diagnostic);
                Assert.That(snapshot.SunDefinition, Is.EqualTo(definition));
                Assert.That(snapshot.SunShaftsIntensity, Is.EqualTo(expected).Within(TOLERANCE));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FadeUsesOldDownTimeAndNewRiseTimeWhileAStationaryClockKeepsTheWeight()
        {
            var clear = new XRayWeatherFlareDefinition("moon_full_clear", default, default, null, 120.0f, 120.0f);
            var rain = new XRayWeatherFlareDefinition("moon_full_verycloudydark", default, default, null, 40.0f, 30.0f);
            var fade = new XRayWeatherFlareFade();
            fade.Step(clear, 0.0f, true);
            fade.Step(rain, 0.0f, false);
            fade.Step(rain, 30.0f, false);
            Assert.That(fade.Definition, Is.SameAs(clear));
            Assert.That(fade.Weight, Is.EqualTo(0.75f));
            fade.Step(rain, 0.0f, false);
            Assert.That(fade.Weight, Is.EqualTo(0.75f));
            fade.Step(rain, 89.0f, false);
            Assert.That(fade.Definition, Is.SameAs(clear));
            Assert.That(fade.Weight, Is.EqualTo(1.0f / 120.0f).Within(TOLERANCE));
            fade.Step(rain, 1.1f, false);
            Assert.That(fade.Definition, Is.SameAs(rain));
            Assert.That(fade.Weight, Is.EqualTo(0.0275f).Within(TOLERANCE));
            fade.Step(rain, 8.9f, false);
            Assert.That(fade.Weight, Is.EqualTo(0.25f).Within(TOLERANCE));
            fade.Step(null, 0.0f, true);
            Assert.That(fade.Definition, Is.Null);
            Assert.That(fade.Weight, Is.Zero);
            fade.Step(clear, 0.0f, true);
            Assert.That(fade.Definition, Is.SameAs(clear));
            Assert.That(fade.Weight, Is.EqualTo(1.0f));
        }

        [Test]
        public void LiteralNoneIsAValidDefinitionAndEmptyIsAbsent()
        {
            var settings = ScriptableObject.CreateInstance<XRayWeatherCelestialSettings>();
            try
            {
                var none = new XRayWeatherFlareDefinition("none", default, default, null, 30.0f, 30.0f);
                settings.Initialize(null, 0.1f, 0.7f, true, new[] { none });
                Assert.That(settings.FindDefinition("none"), Is.SameAs(none));
                Assert.That(settings.FindDefinition(""), Is.Null);
                Assert.That(settings.FindDefinition("unknown"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(false, false, 0.0f)]
        [TestCase(true, false, 0.07f)]
        [TestCase(false, true, 0.0f)]
        [TestCase(true, true, 0.07f)]
        public void EffectsAreIndependentAndReleaseClearsTheirState(bool shafts, bool flare, float expected)
        {
            var settings = ScriptableObject.CreateInstance<XRayWeatherCelestialSettings>();
            var owner = new GameObject("Celestial behavior fixture");
            var none = new XRayWeatherFlareDefinition("none", default, default, null, 30.0f, 30.0f);
            settings.Initialize(null, 0.1f, 0.7f, true, new[] { none });
            var renderer = new XRayWeatherCelestialRenderer(owner, settings);
            try
            {
                renderer.Publish(Frame(0.0, "none", 0.0f), shafts, flare, 0.0f, true);
                Assert.That(renderer.ShaftsIntensity, Is.EqualTo(expected).Within(TOLERANCE));
                if (flare) Assert.That(renderer.FlareDefinition, Is.SameAs(none));
                else Assert.That(renderer.FlareDefinition, Is.Null);
                Assert.That(renderer.FlareFade, Is.EqualTo(flare ? 1.0f : 0.0f));
                renderer.Release();
                Assert.That(renderer.Volume, Is.Null);
                Assert.That(renderer.ShaftsIntensity, Is.Zero);
                Assert.That(renderer.FlareDefinition, Is.Null);
            }
            finally
            {
                renderer.Release();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static XRayWeatherSnapshot Frame(double seconds, string definition, float raw)
        {
            return new XRayWeatherSnapshot(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), seconds, Color.black, Color.gray, Color.gray, 0.0f, 0.0f, 0.0f, 0.0f, Color.black, Color.gray, 0.5f, 400.0f, 1000.0f, Color.gray, Color.clear, 0.0f, default, default, sunDefinition: definition, sunShaftsIntensity: raw);
        }
    }
}
