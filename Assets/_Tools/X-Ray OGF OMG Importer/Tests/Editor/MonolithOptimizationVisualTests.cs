using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class MonolithOptimizationVisualTests
    {
        private const int SIZE = 128;
        private const string ARCHIVE = "E:/Projects/stalker_raid_into_darkness_Archive/";

        [Test]
        [Timeout(180000)]
        public void EnvironmentAndWetSurfaceImagesPreserveFiniteReferencePixels()
        {
            HdrpTestWarmup.Ensure();
            string baselineRoot = Environment.GetEnvironmentVariable("MONOLITH_OPTIMIZATION_OUTPUT");
            string phase = Environment.GetEnvironmentVariable("MONOLITH_OPTIMIZATION_PHASE") ?? "Check";
            if (!string.IsNullOrEmpty(baselineRoot))
            {
                baselineRoot = Path.GetFullPath(baselineRoot).Replace('\\', '/');
                Assert.That(baselineRoot, Does.StartWith(ARCHIVE));
                Assert.That(phase, Is.EqualTo("A").Or.EqualTo("B"));
            }
            using (var fixture = new MonolithOptimizationVisualFixture(SIZE))
            {
                var images = new Dictionary<string, Color[]>(StringComparer.Ordinal);
                fixture.Weather(false, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                images.Add("EnvironmentLegacy", fixture.Capture());
                fixture.Weather(true, Vector4.zero);
                images.Add("EnvironmentZero", fixture.Capture());
                fixture.Weather(true, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                images.Add("EnvironmentOne", fixture.Capture());
                fixture.Weather(true, new Vector4(0.35f, 0.65f, 0.0f, 0.0f));
                images.Add("EnvironmentTwo", fixture.Capture());
                fixture.Weather(true, new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
                images.Add("EnvironmentFour", fixture.Capture());
                for (int debug = 45; debug <= 48; debug++)
                {
                    fixture.Weather(false, Vector4.zero);
                    Color[] legacy = fixture.Capture(debug);
                    fixture.Weather(true, new Vector4(0.35f, 0.65f, 0.0f, 0.0f));
                    Color[] weather = fixture.Capture(debug);
                    AssertEquivalent(legacy, weather, "Diagnostic weather independence " + debug);
                    images.Add("EnvironmentDebug" + debug, weather);
                }
                fixture.Surface(Vector4.zero, 0.0f, false);
                images.Add("DryNormal", fixture.Capture(22.0f));
                fixture.Surface(new Vector4(1.0f, 0.0f, 1.0f, 0.0f), 0.0f, false);
                images.Add("FilmNormal", fixture.Capture(22.0f));
                images.Add("FilmLighting", fixture.Capture());
                fixture.Surface(Vector4.one, 0.0f, false);
                images.Add("PuddleNoRainNormal", fixture.Capture(22.0f));
                images.Add("PuddleNoRainLighting", fixture.Capture());
                fixture.Surface(Vector4.one, 1.0f, false);
                images.Add("PuddleRainNormal", fixture.Capture(22.0f));
                images.Add("PuddleRainLighting", fixture.Capture());
                fixture.Surface(Vector4.one, 1.0f, true);
                images.Add("PuddleFarNormal", fixture.Capture(22.0f));
                fixture.Surface(Vector4.zero, 0.0f, false);
                fixture.Direct(1.0f, 0.0f);
                images.Add("DirectOrdinary", fixture.Capture());
                fixture.Direct(3.0f, 0.0f);
                images.Add("DirectMetal", fixture.Capture());
                fixture.Direct(0.0f, 0.1f);
                images.Add("DirectFlora", fixture.Capture());
                fixture.Direct(1.0f, 0.0f);
                fixture.Lights(false);
                images.Add("DirectNoLights", fixture.Capture());
                foreach (KeyValuePair<string, Color[]> image in images)
                {
                    AssertFinite(image.Value, image.Key);
                }
                if (!string.IsNullOrEmpty(baselineRoot) && phase == "B")
                {
                    foreach (KeyValuePair<string, Color[]> image in images)
                    {
                        AssertEquivalent(Read(Path.Combine(baselineRoot, "A", image.Key + ".rgba32f")), image.Value, image.Key);
                    }
                }
                Assert.That(Difference(images["EnvironmentOne"], images["EnvironmentTwo"]), Is.GreaterThan(0.0001));
                Assert.That(Difference(images["EnvironmentLegacy"], images["EnvironmentZero"]), Is.GreaterThan(0.0001));
                Assert.That(Difference(images["PuddleNoRainNormal"], images["PuddleRainNormal"]), Is.GreaterThan(0.00001));
                Assert.That(Difference(images["DryNormal"], images["PuddleNoRainNormal"]), Is.GreaterThan(0.00001));
                Assert.That(Difference(images["DirectOrdinary"], images["DirectMetal"]), Is.GreaterThan(0.00001));
                Assert.That(Difference(images["DirectOrdinary"], images["DirectNoLights"]), Is.GreaterThan(0.00001));
            }
        }

        [Test]
        [Timeout(60000)]
        public void LevelHemisphereKeepsWeatherFillWithoutScreenSpaceGi()
        {
            using (var fixture = new MonolithOptimizationVisualFixture(SIZE))
            {
                var daylight = new Vector4(0.15f, 0.18f, 0.21f, 0.0f);
                fixture.Lights(false);
                fixture.LevelHemisphere(0.0f, Vector4.zero);
                Color[] unlit = fixture.Capture();
                fixture.LevelHemisphere(0.0f, daylight);
                Color[] shaded = fixture.Capture();
                fixture.LevelHemisphere(0.0f, daylight * 0.05f);
                Color[] night = fixture.Capture();
                fixture.LevelHemisphere(1.0f, daylight);
                Color[] exposed = fixture.Capture();
                fixture.LevelHemisphere(1.0f, Vector4.zero);
                Color[] exposedWithoutFill = fixture.Capture();
                fixture.LevelHemisphere(0.0f, daylight);
                fixture.Weather(false, Vector4.zero);
                Color[] weatherDisabled = fixture.Capture();
                AssertFinite(shaded, "Shaded level geometry");
                Assert.That(Difference(shaded, unlit), Is.GreaterThan(0.005));
                Assert.That(Difference(night, unlit), Is.LessThan(Difference(shaded, unlit) * 0.2));
                AssertEquivalent(exposedWithoutFill, exposed, "Exposed level geometry");
                AssertEquivalent(unlit, weatherDisabled, "Inactive level weather environment");
            }
        }

        [Test]
        [Timeout(60000)]
        public void TerrainAndBushShadowsKeepWeatherFillWithoutScreenSpaceGi()
        {
            using (var fixture = new MonolithOptimizationVisualFixture(SIZE))
            {
                var daylight = new Vector4(384.0f, 416.0f, 448.0f, 0.0f);
                int center = SIZE / 2 * SIZE + SIZE / 2;
                fixture.Lights(false);
                fixture.LevelHemisphere(0.55f, daylight);
                foreach (bool terrain in new[] { true, false })
                {
                    string surface = terrain ? "Terrain" : "Bush";
                    fixture.OutdoorSurface(terrain);
                    fixture.Weather(false, Vector4.zero);
                    Color[] unlit = fixture.Capture();
                    fixture.Weather(true, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                    Color[] hemisphere = fixture.Capture(36.0f);
                    Color[] shaded = fixture.Capture();
                    AssertFinite(shaded, surface);
                    Assert.That(hemisphere[center].r, Is.InRange(0.5f, 0.6f), surface + " hemisphere");
                    Assert.That(shaded[center].grayscale, Is.GreaterThan(unlit[center].grayscale + 0.005f), surface + " shaded luminance");
                }
            }
        }

        private static void AssertFinite(Color[] pixels, string name)
        {
            for (int index = 0; index < pixels.Length; index++)
            {
                Color pixel = pixels[index];
                Assert.IsTrue(float.IsFinite(pixel.r) && float.IsFinite(pixel.g) && float.IsFinite(pixel.b) && float.IsFinite(pixel.a), name + " non-finite pixel " + index);
            }
        }

        private static void AssertEquivalent(Color[] reference, Color[] candidate, string name)
        {
            Assert.AreEqual(reference.Length, candidate.Length, name);
            AssertFinite(reference, name + " reference");
            AssertFinite(candidate, name + " candidate");
            float maximum = 0.0f;
            for (int index = 0; index < reference.Length; index++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    float error = Mathf.Abs(reference[index][channel] - candidate[index][channel]);
                    maximum = Mathf.Max(maximum, error);
                    Assert.That(error, Is.LessThanOrEqualTo(0.00005f + Mathf.Abs(reference[index][channel]) * 0.00001f), name + " pixel " + index + " channel " + channel + " max " + maximum);
                }
            }
        }

        private static double Difference(Color[] left, Color[] right)
        {
            double sum = 0.0;
            for (int index = 0; index < left.Length; index++) sum += Math.Abs(left[index].r - right[index].r) + Math.Abs(left[index].g - right[index].g) + Math.Abs(left[index].b - right[index].b);
            return sum / (left.Length * 3);
        }

        private static Color[] Read(string path)
        {
            Assert.IsTrue(File.Exists(path), "Missing baseline float image " + path);
            var pixels = new Color[SIZE * SIZE];
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                Assert.AreEqual(SIZE, reader.ReadInt32());
                Assert.AreEqual(SIZE, reader.ReadInt32());
                for (int index = 0; index < pixels.Length; index++) pixels[index] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Assert.AreEqual(reader.BaseStream.Length, reader.BaseStream.Position);
            }
            return pixels;
        }
    }
}
