using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherSceneTests
    {
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator ImportedRvmWeatherDrivesRealVegetationAndSourceLight()
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE07A_OUTPUT");
            Assert.IsNotEmpty(output);
            Assert.IsTrue(Path.GetFullPath(output).StartsWith(@"E:\Projects\stalker_raid_into_darkness_Archive\", StringComparison.OrdinalIgnoreCase));
            output = Path.Combine(output, "WeatherScene");
            Directory.CreateDirectory(output);
            HdrpTestWarmup.Ensure();
            XRayWeatherProfile clear = XRayWeatherImporter.ImportCurrent("w_clear1");
            XRayWeatherProfile rain = XRayWeatherImporter.ImportCurrent("w_rain1");
            var rows = new StringBuilder("Scenario,GameSeconds,SunR,SunG,SunB,DirectionX,DirectionY,DirectionZ,WindDirection,WindVelocity,WindTime,AnimX,AnimY,PreviousX,PreviousY,ImageDelta,SourceLightR,SourceLightG,SourceLightB\n");
            try
            {
                File.WriteAllText(Path.Combine(output, "w_clear1.json"), JsonUtility.ToJson(clear, true));
                File.WriteAllText(Path.Combine(output, "w_rain1.json"), JsonUtility.ToJson(rain, true));
                using (var fixture = new XRayWeatherSceneFixture(clear))
                {
                    XRayWeatherController controller = fixture.Controller;
                    controller.BeginPreview();
                    Assert.IsTrue(controller.PreviewActive, controller.Diagnostic);
                    File.WriteAllText(Path.Combine(output, "Camera.txt"), "Position=" + fixture.Camera.transform.position + " Rotation=" + fixture.Camera.transform.rotation + " OrthographicSize=" + fixture.Camera.orthographicSize + " Exposure=fixed6;Postprocess=true;ExposureControl=true;FixtureSun=3lux (diagnostic, not source unit conversion) API=" + SystemInfo.graphicsDeviceType);
                    controller.SetTimeScale(120.0f);
                    controller.SetPaused(false);
                    Color[] previous = null;
                    foreach (double seconds in new[] { 25200.0, 46800.0, 72000.0 })
                    {
                        controller.Seek(seconds);
                        Color[] pixels = fixture.Capture(Path.Combine(output, "clear_" + seconds.ToString(CultureInfo.InvariantCulture)));
                        Record(rows, "clear", controller, previous, pixels);
                        previous = pixels;
                        Assert.AreEqual(fixture.Wind.State.Anim, fixture.Wind.State.AnimPrev);
                        yield return null;
                    }
                    controller.Seek(46800.0);
                    controller.TickPreview(0.1f);
                    double before = controller.GameTimeSeconds;
                    int ticks = fixture.Wind.State.Ticks;
                    fixture.Capture(null);
                    fixture.Capture(null);
                    fixture.RenderSecondCamera();
                    Assert.AreEqual(before, controller.GameTimeSeconds, "Rendering additional cameras/passes must not advance simulation.");
                    Assert.AreEqual(ticks, fixture.Wind.State.Ticks);
                    Color[] wind0 = fixture.Capture(Path.Combine(output, "wind_0"), 34.0f);
                    for (int tick = 0; tick < 20; tick++) controller.TickPreview(0.1f);
                    Color[] wind1 = fixture.Capture(Path.Combine(output, "wind_1"), 34.0f);
                    float windDelta = Difference(wind0, wind1);
                    Assert.Greater(windDelta, 0.00001f, "Real imported vegetation must visibly deform under imported weather inputs.");
                    Record(rows, "wind", controller, wind0, wind1);
                    controller.SetPaused(true);
                    double paused = controller.GameTimeSeconds;
                    controller.TickPreview(1.0f);
                    Assert.AreEqual(paused, controller.GameTimeSeconds);
                    Assert.AreEqual(fixture.Wind.State.Anim, fixture.Wind.State.AnimPrev);
                    controller.SetPaused(false);
                    controller.TransitionTo(rain, 2.0f);
                    previous = fixture.Capture(Path.Combine(output, "transition_0"));
                    Record(rows, "transition_0", controller, null, previous);
                    controller.TickPreview(1.0f);
                    Color[] middle = fixture.Capture(Path.Combine(output, "transition_05"));
                    Record(rows, "transition_05", controller, previous, middle);
                    controller.TickPreview(1.0f);
                    Color[] end = fixture.Capture(Path.Combine(output, "transition_1"));
                    Record(rows, "transition_1", controller, previous, end);
                    Assert.AreSame(rain, controller.ActiveProfile);
                    Assert.IsFalse(controller.IsTransitioning);
                    Assert.Greater(Difference(previous, end), 0.00001f, "Real weather transition must change rendered source lighting with fixed exposure.");
                    controller.EndPreview();
                    Assert.IsNull(XRayWeatherController.Active);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clear);
                UnityEngine.Object.DestroyImmediate(rain);
                File.WriteAllText(Path.Combine(output, "Snapshots.csv"), rows.ToString());
            }
        }

        private static void Record(StringBuilder rows, string scenario, XRayWeatherController controller, Color[] before, Color[] after)
        {
            XRayWeatherSnapshot snapshot = controller.Snapshot;
            XRayFloraWindState state = XRayFloraWindProvider.Active.State;
            Vector4 published = Shader.GetGlobalVectorArray("_MonolithBoundColor64")[0];
            Assert.AreEqual(controller.SourceLightColor.x, published.x, 0.000001f);
            Assert.AreEqual(controller.SourceLightColor.y, published.y, 0.000001f);
            Assert.AreEqual(controller.SourceLightColor.z, published.z, 0.000001f);
            Assert.AreEqual(snapshot.WindDirectionRadians, state.Weather.DirectionRadians);
            Assert.AreEqual(snapshot.WindVelocity, state.Weather.Velocity);
            rows.AppendLine(string.Join(",", scenario, Num(controller.GameTimeSeconds), Num(snapshot.SunColor.r), Num(snapshot.SunColor.g), Num(snapshot.SunColor.b), Num(snapshot.SunDirection.x), Num(snapshot.SunDirection.y), Num(snapshot.SunDirection.z), Num(snapshot.WindDirectionRadians), Num(snapshot.WindVelocity), Num(state.Time), Num(state.Anim.x), Num(state.Anim.y), Num(state.AnimPrev.x), Num(state.AnimPrev.y), Num(before == null ? 0.0 : Difference(before, after)), Num(published.x), Num(published.y), Num(published.z)));
        }

        private static string Num(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static float Difference(Color[] before, Color[] after)
        {
            float maximum = 0.0f;
            for (int i = 0; i < before.Length; i++) maximum = Mathf.Max(maximum, Mathf.Abs(before[i].r - after[i].r), Mathf.Abs(before[i].g - after[i].g), Mathf.Abs(before[i].b - after[i].b));
            return maximum;
        }
    }
}
