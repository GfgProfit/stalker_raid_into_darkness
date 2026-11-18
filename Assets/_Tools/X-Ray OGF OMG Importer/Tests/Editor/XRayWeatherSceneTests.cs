using System.Collections;
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
            if (AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(XRayWeatherSceneFixture.BATCH_PATH) == null)
            {
                Assert.Ignore("This integration test requires the imported Anomaly escape vegetation.");
            }

            HdrpTestWarmup.Ensure();
            XRayWeatherProfile clear = XRayWeatherImporter.ImportCurrent("w_clear1");
            XRayWeatherProfile rain = XRayWeatherImporter.ImportCurrent("w_rain1");
            try
            {
                using (var fixture = new XRayWeatherSceneFixture(clear))
                {
                    XRayWeatherController controller = fixture.Controller;
                    controller.BeginPreview();
                    Assert.IsTrue(controller.PreviewActive, controller.Diagnostic);
                    controller.SetTimeScale(120.0f);
                    controller.SetPaused(false);
                    foreach (double seconds in new[] { 25200.0, 46800.0, 72000.0 })
                    {
                        controller.Seek(seconds);
                        fixture.Capture();
                        AssertPublishedInputs(controller);
                        Assert.AreEqual(fixture.Wind.State.Anim, fixture.Wind.State.AnimPrev);
                        yield return null;
                    }
                    controller.Seek(46800.0);
                    controller.TickPreview(0.1f);
                    double before = controller.GameTimeSeconds;
                    int ticks = fixture.Wind.State.Ticks;
                    fixture.Capture();
                    fixture.Capture();
                    fixture.RenderSecondCamera();
                    Assert.AreEqual(before, controller.GameTimeSeconds, "Rendering additional cameras/passes must not advance simulation.");
                    Assert.AreEqual(ticks, fixture.Wind.State.Ticks);
                    Color[] wind0 = fixture.Capture(34.0f);
                    for (int tick = 0; tick < 20; tick++) controller.TickPreview(0.1f);
                    Color[] wind1 = fixture.Capture(34.0f);
                    float windDelta = Difference(wind0, wind1);
                    Assert.Greater(windDelta, 0.00001f, "Real imported vegetation must visibly deform under imported weather inputs.");
                    AssertPublishedInputs(controller);
                    controller.SetPaused(true);
                    double paused = controller.GameTimeSeconds;
                    controller.TickPreview(1.0f);
                    Assert.AreEqual(paused, controller.GameTimeSeconds);
                    Assert.AreEqual(fixture.Wind.State.Anim, fixture.Wind.State.AnimPrev);
                    controller.SetPaused(false);
                    controller.TransitionTo(rain, 2.0f);
                    Color[] previous = fixture.Capture();
                    AssertPublishedInputs(controller);
                    controller.TickPreview(1.0f);
                    fixture.Capture();
                    AssertPublishedInputs(controller);
                    controller.TickPreview(1.0f);
                    Color[] end = fixture.Capture();
                    AssertPublishedInputs(controller);
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
            }
        }

        private static void AssertPublishedInputs(XRayWeatherController controller)
        {
            XRayWeatherSnapshot snapshot = controller.Snapshot;
            XRayFloraWindState state = XRayFloraWindProvider.Active.State;
            Vector4 published = Shader.GetGlobalVectorArray("_MonolithBoundColor64")[0];
            Assert.AreEqual(controller.SourceLightColor.x, published.x, 0.000001f);
            Assert.AreEqual(controller.SourceLightColor.y, published.y, 0.000001f);
            Assert.AreEqual(controller.SourceLightColor.z, published.z, 0.000001f);
            Assert.AreEqual(snapshot.WindDirectionRadians, state.Weather.DirectionRadians);
            Assert.AreEqual(snapshot.WindVelocity, state.Weather.Velocity);
        }

        private static float Difference(Color[] before, Color[] after)
        {
            float maximum = 0.0f;
            for (int i = 0; i < before.Length; i++) maximum = Mathf.Max(maximum, Mathf.Abs(before[i].r - after[i].r), Mathf.Abs(before[i].g - after[i].g), Mathf.Abs(before[i].b - after[i].b));
            return maximum;
        }
    }
}
