using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherNumericTests
    {
        private const double COLOR_TOLERANCE = 0.000002;
        private const double DIRECTION_TOLERANCE = 0.000002;
        private const double SCALAR_TOLERANCE = 0.0001;
        private const double RELATIVE_TOLERANCE = 0.000002;

        [Serializable]
        private sealed class ExpectedDocument
        {
            public ExpectedSample[] samples;
            public ExpectedTransition[] transitions;
        }

        [Serializable]
        private sealed class ExpectedSample
        {
            public string cycle;
            public double seconds;
            public string key0;
            public string key1;
            public double weight;
            public double[] sunDirection;
            public double[] sun_color;
            public double[] ambient_color;
            public double[] hemisphere_color;
            public double[] rain_color;
            public double[] fog_color;
            public double[] sky_color;
            public double[] source_light_color;
            public double wind_velocity;
            public double tree_amplitude_intensity;
            public double rain_density;
            public double fog_density;
            public double fog_distance;
            public double far_plane;
            public double sky_rotation;
            public double wind_direction;
            public double fog_near;
            public double fog_far;
            public double[] wind_anim_after_one_tick;
            public double[] wind_anim_before_one_tick;
        }

        [Serializable]
        private sealed class ExpectedTransition
        {
            public string from;
            public string to;
            public double seconds;
            public double transitionWeight;
            public double wind_velocity;
            public double tree_amplitude_intensity;
            public double rain_density;
            public double[] sun_color;
        }

        [Test]
        public void ImportedProfilesAndConsumersMatchIndependentSourceCalculations()
        {
            string run = Environment.GetEnvironmentVariable("PHASE07A_RUN");
            string expectedPath = string.IsNullOrWhiteSpace(run) ? null : Path.Combine(run, "Weather_Independent_Expected.json");
            if (expectedPath == null || !File.Exists(expectedPath)) Assert.Ignore("Independent source-weather reference artifact is unavailable; set PHASE07A_RUN.");
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE07A_OUTPUT");
            Assert.IsFalse(string.IsNullOrWhiteSpace(output));
            Assert.IsTrue(Path.GetFullPath(output).StartsWith(@"E:\Projects\stalker_raid_into_darkness_Archive\", StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(output);
            ExpectedDocument expected = JsonUtility.FromJson<ExpectedDocument>(File.ReadAllText(expectedPath));
            Assert.AreEqual(20, expected.samples.Length, "Reference contains ten independently sampled times per source cycle.");
            Assert.AreEqual(3, expected.transitions.Length, "Reference contains start, midpoint and end of the explicit crossfade.");
            XRayWeatherProfile clear = XRayWeatherImporter.ImportCurrent("w_clear1");
            XRayWeatherProfile rain = XRayWeatherImporter.ImportCurrent("w_rain1");
            var root = new GameObject("DIAGNOSTIC independent weather parity");
            var report = new StringBuilder("case,field,expected,actual,absolute_error,tolerance,status\n");
            var failures = new List<string>();
            try
            {
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, clear);
                Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                controller.SetTimeScale(0.0f);
                foreach (ExpectedSample row in expected.samples)
                {
                    XRayWeatherProfile profile = Select(row.cycle, clear, rain);
                    controller.SetProfile(profile);
                    controller.Seek(row.seconds);
                    XRayWeatherSnapshot snapshot = controller.Snapshot;
                    string id = row.cycle + "_" + Num(row.seconds);
                    Assert.AreEqual(row.key0, snapshot.Key0, id);
                    Assert.AreEqual(row.key1, snapshot.Key1, id);
                    Check(report, failures, id, "key_weight", row.weight, snapshot.ResourceBlend, COLOR_TOLERANCE);
                    ColorCheck(report, failures, id, "sun_color", row.sun_color, snapshot.SunColor);
                    ColorCheck(report, failures, id, "ambient_color", row.ambient_color, snapshot.AmbientColor);
                    ColorCheck(report, failures, id, "hemisphere_color", row.hemisphere_color, snapshot.HemisphereColor);
                    ColorCheck(report, failures, id, "rain_color", row.rain_color, snapshot.RainColor);
                    ColorCheck(report, failures, id, "fog_color", row.fog_color, snapshot.FogColor);
                    ColorCheck(report, failures, id, "sky_color", row.sky_color, snapshot.SkyColor);
                    for (int i = 0; i < 3; i++)
                    {
                        Check(report, failures, id, "sun_direction_" + i, row.sunDirection[i], snapshot.SunDirection[i], DIRECTION_TOLERANCE);
                        Check(report, failures, id, "source_light_" + i, row.source_light_color[i], controller.SourceLightColor[i], COLOR_TOLERANCE);
                    }
                    Check(report, failures, id, "wind_velocity", row.wind_velocity, snapshot.WindVelocity, 0.001);
                    Check(report, failures, id, "wind_direction", row.wind_direction, snapshot.WindDirectionRadians, DIRECTION_TOLERANCE);
                    Check(report, failures, id, "tree_amplitude_intensity", row.tree_amplitude_intensity, snapshot.TreeAmplitudeIntensity, COLOR_TOLERANCE);
                    Check(report, failures, id, "rain_density", row.rain_density, snapshot.RainDensity, COLOR_TOLERANCE);
                    Check(report, failures, id, "fog_density", row.fog_density, snapshot.FogDensity, COLOR_TOLERANCE);
                    Check(report, failures, id, "fog_distance", row.fog_distance, snapshot.FogDistance, SCALAR_TOLERANCE);
                    Check(report, failures, id, "far_plane", row.far_plane, snapshot.FarPlane, SCALAR_TOLERANCE);
                    Check(report, failures, id, "sky_rotation", row.sky_rotation, snapshot.SkyRotationRadians, DIRECTION_TOLERANCE);
                    Check(report, failures, id, "fog_near", row.fog_near, snapshot.FogNear, SCALAR_TOLERANCE);
                    Check(report, failures, id, "fog_far", row.fog_far, snapshot.FogFar, SCALAR_TOLERANCE);
                    WindCheck(report, failures, id, row, profile, snapshot);
                }
                foreach (ExpectedTransition row in expected.transitions)
                {
                    controller.SetProfile(Select(row.from, clear, rain));
                    controller.Seek(row.seconds);
                    Assert.IsTrue(controller.TransitionTo(Select(row.to, clear, rain), 2.0f));
                    controller.TickPreview((float)(row.transitionWeight * 2.0));
                    XRayWeatherSnapshot snapshot = controller.Snapshot;
                    string id = "transition_" + Num(row.transitionWeight);
                    ColorCheck(report, failures, id, "sun_color", row.sun_color, snapshot.SunColor);
                    Check(report, failures, id, "wind_velocity", row.wind_velocity, snapshot.WindVelocity, 0.001);
                    Check(report, failures, id, "tree_amplitude_intensity", row.tree_amplitude_intensity, snapshot.TreeAmplitudeIntensity, COLOR_TOLERANCE);
                    Check(report, failures, id, "rain_density", row.rain_density, snapshot.RainDensity, COLOR_TOLERANCE);
                }
            }
            finally
            {
                File.WriteAllText(Path.Combine(output, "Weather_Numeric_Parity.csv"), report.ToString());
                File.WriteAllText(Path.Combine(output, "Weather_Numeric_Tolerances.txt"), "Independent JavaScript source parsing and double arithmetic reference. Color/scalar float rounding: abs 2e-6 for colors; direction/trig abs 2e-6; distance abs 1e-4; velocity abs 1e-3; each also receives relative 2e-6. These bounds cover float parsing, interpolation, radians/trig and saved visibility scale multiplication across the sampled source range. Wind reference uses literal source engine equations with one 0.1s tick from reset; exact source input/tick order is fixed.\n");
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(rain);
                UnityEngine.Object.DestroyImmediate(clear);
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        private static void WindCheck(StringBuilder report, List<string> failures, string id, ExpectedSample row, XRayWeatherProfile profile, XRayWeatherSnapshot snapshot)
        {
            Assert.IsNotNull(row.wind_anim_after_one_tick, "Independent source wind reference is required.");
            Assert.IsNotNull(row.wind_anim_before_one_tick);
            var state = new XRayFloraWindState { Profile = XRayFloraWindProfile.AnomalySsfx, TreesSetup = profile.SsfxWindTrees };
            var weather = new XRayFloraWindWeather(snapshot.WindDirectionRadians, snapshot.WindVelocity, snapshot.TreeAmplitudeIntensity);
            state.Reset(weather);
            state.Advance(0.1f, weather);
            for (int i = 0; i < 4; i++)
            {
                Check(report, failures, id, "wind_anim_" + i, row.wind_anim_after_one_tick[i], state.Anim[i], COLOR_TOLERANCE);
                Check(report, failures, id, "wind_anim_previous_" + i, row.wind_anim_before_one_tick[i], state.AnimPrev[i], COLOR_TOLERANCE);
            }
        }

        private static XRayWeatherProfile Select(string cycle, XRayWeatherProfile clear, XRayWeatherProfile rain)
        {
            if (cycle == "w_clear1") return clear;
            if (cycle == "w_rain1") return rain;
            throw new ArgumentException("Unexpected reference weather cycle: " + cycle);
        }

        private static void ColorCheck(StringBuilder report, List<string> failures, string id, string field, double[] expected, Color actual)
        {
            for (int i = 0; i < expected.Length; i++) Check(report, failures, id, field + "_" + i, expected[i], actual[i], COLOR_TOLERANCE);
        }

        private static void Check(StringBuilder report, List<string> failures, string id, string field, double expected, double actual, double absoluteTolerance)
        {
            double error = Math.Abs(expected - actual);
            double tolerance = absoluteTolerance + RELATIVE_TOLERANCE * Math.Abs(expected);
            bool pass = !double.IsNaN(actual) && !double.IsInfinity(actual) && error <= tolerance;
            report.AppendLine(string.Join(",", id, field, Num(expected), Num(actual), Num(error), Num(tolerance), pass ? "PASS" : "FAIL"));
            if (!pass) failures.Add(id + ":" + field + " expected " + Num(expected) + " actual " + Num(actual));
        }

        private static string Num(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
