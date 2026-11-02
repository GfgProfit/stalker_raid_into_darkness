using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherRealLevelTests
    {
        private const string SCENE_PATH = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/l01_escape/Scenes/l01_escape.unity";
        private const int WIDTH = 640;
        private const int HEIGHT = 360;
        private const float EXPOSURE_EV100 = 9.0f;

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator WholeImportedAnomalySceneReceivesWeatherWithoutSavingAssets()
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE07A_OUTPUT");
            Assert.IsFalse(string.IsNullOrWhiteSpace(output));
            Assert.IsTrue(Path.GetFullPath(output).StartsWith(@"E:\Projects\stalker_raid_into_darkness_Archive\", StringComparison.OrdinalIgnoreCase));
            output = Path.Combine(output, "WeatherRealLevel");
            Directory.CreateDirectory(output);
            Assert.IsTrue(File.Exists(SCENE_PATH), "The existing imported Anomaly level is required.");
            byte[] sceneBytes = File.ReadAllBytes(SCENE_PATH);
            HdrpTestWarmup.Ensure();
            XRayWeatherProfile clear = XRayWeatherImporter.ImportCurrent("w_clear1");
            XRayWeatherProfile rain = XRayWeatherImporter.ImportCurrent("w_rain1");
            Scene scene = default;
            XRayWeatherController controller = null;
            VolumeProfile volumeProfile = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            Camera camera = null;
            RenderTexture originalTarget = null;
            var rows = new StringBuilder("scenario,game_seconds,key0,key1,sun_r,sun_g,sun_b,light_r,light_g,light_b,direction_x,direction_y,direction_z,wind_direction,wind_velocity,wind_time,current_x,current_y,previous_x,previous_y,image_delta\n");
            try
            {
                scene = EditorSceneManager.OpenPreviewScene(SCENE_PATH);
                GameObject[] importedRoots = scene.GetRootGameObjects();
                Assert.IsNotEmpty(importedRoots);
                var owner = new GameObject("DIAGNOSTIC whole imported level weather");
                SceneManager.MoveGameObjectToScene(owner, scene);
                foreach (GameObject importedRoot in importedRoots) importedRoot.transform.SetParent(owner.transform, true);
                foreach (Camera candidate in owner.GetComponentsInChildren<Camera>(true))
                {
                    candidate.enabled = false;
                    if (camera == null || candidate.name == "Level Camera") camera = candidate;
                }
                Assert.IsNotNull(camera, "The imported scene must contain its saved camera.");
                Assert.AreEqual(CameraType.Game, camera.cameraType);
                camera.scene = scene;
                camera.enabled = false;
                camera.useOcclusionCulling = false;
                camera.allowHDR = true;
                camera.aspect = (float)WIDTH / HEIGHT;
                HDAdditionalCameraData cameraData = camera.GetComponent<HDAdditionalCameraData>();
                if (cameraData == null) cameraData = camera.gameObject.AddComponent<HDAdditionalCameraData>();
                cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                cameraData.customRenderingSettings = true;
                cameraData.volumeLayerMask = ~0;
                foreach (FrameSettingsField field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.ContactShadows, FrameSettingsField.Volumetrics })
                {
                    cameraData.renderingPathCustomFrameSettings.SetEnabled(field, false);
                    cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                foreach (FrameSettingsField field in new[] { FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl })
                {
                    cameraData.renderingPathCustomFrameSettings.SetEnabled(field, true);
                    cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                var captureRig = new GameObject("DIAGNOSTIC fixed exposure");
                captureRig.transform.SetParent(owner.transform, false);
                volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                Exposure exposure = volumeProfile.Add<Exposure>(true);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(EXPOSURE_EV100);
                volumeProfile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
                volumeProfile.Add<Fog>(true).enabled.Override(false);
                Volume volume = captureRig.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 100000.0f;
                volume.sharedProfile = volumeProfile;
                controller = XRayLevelSceneBuilder.ConfigureWeather(owner, clear);
                XRayFloraWindProvider wind = owner.GetComponentInChildren<XRayFloraWindProvider>();
                Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                controller.SetTimeScale(0.0f);
                Assert.AreEqual(1, owner.GetComponentsInChildren<XRayWeatherController>(true).Length);
                target = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                target.Create();
                originalTarget = camera.targetTexture;
                camera.targetTexture = target;
                pixels = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBAFloat, false, true);
                File.WriteAllText(Path.Combine(output, "Capture_Settings.txt"), "SourceScene=" + SCENE_PATH + "\nSceneUsage=OpenPreviewScene; whole saved map; no SaveScene\nCamera=" + camera.name + "\nCameraType=" + camera.cameraType + "\nPosition=" + camera.transform.position.ToString("F8") + "\nRotation=" + camera.transform.rotation.ToString("F8") + "\nProjection=" + camera.projectionMatrix.ToString("F8") + "\nFOV=" + camera.fieldOfView + "\nResolution=640x360\nExposureEV100=9 fixed for every capture\nTonemapping=None\nFog/SSR/SSAO/SSGI/volumetrics=disabled in transient capture configuration\nAPI=" + SystemInfo.graphicsDeviceType + "\nDevice=" + SystemInfo.graphicsDeviceName + "\nImportedRoots=" + importedRoots.Length + "\nRenderers=" + owner.GetComponentsInChildren<Renderer>(true).Length + "\nInstancedRenderers=" + owner.GetComponentsInChildren<XRayLevelInstancedRenderer>(true).Length);
                Color[] clearNoon = null;
                Color[] rainNoon = null;
                foreach (XRayWeatherProfile profile in new[] { clear, rain })
                {
                    controller.SetProfile(profile);
                    foreach (double seconds in new[] { 25200.0, 46800.0, 72000.0 })
                    {
                        controller.Seek(seconds);
                        string id = profile.CycleName + "_" + Num(seconds);
                        Color[] captured = Capture(camera, target, pixels, Path.Combine(output, id));
                        Record(rows, id, controller, wind.State, 0.0f);
                        if (seconds == 46800.0)
                        {
                            if (profile == clear) clearNoon = captured;
                            else rainNoon = captured;
                        }
                        Assert.AreEqual(wind.State.Anim, wind.State.AnimPrev);
                        yield return null;
                    }
                }
                Assert.Greater(Difference(clearNoon, rainNoon), 0.00001f, "Actual imported map lighting must respond to different imported cycles at the same fixed camera/exposure.");
                controller.SetProfile(clear);
                controller.Seek(46800.0);
                Color[] wind0 = Capture(camera, target, pixels, Path.Combine(output, "wind_0"));
                for (int tick = 0; tick < 20; tick++) controller.TickPreview(0.1f);
                Color[] wind1 = Capture(camera, target, pixels, Path.Combine(output, "wind_1"));
                float windDifference = Difference(wind0, wind1);
                Record(rows, "wind_after_20_ticks", controller, wind.State, windDifference);
                Assert.Greater(windDifference, 0.00001f, "Saved map camera must observe animated vegetation while light/time stay fixed.");
                controller.SetTimeScale(120.0f);
                double clockBefore = controller.GameTimeSeconds;
                controller.TickPreview(0.1f);
                Assert.That(controller.GameTimeSeconds, Is.EqualTo(clockBefore + 12.0).Within(0.00001));
                controller.SetTimeScale(0.0f);
                controller.Seek(46800.0);
                Assert.IsTrue(controller.TransitionTo(rain, 2.0f));
                Color[] transitionStart = null;
                for (int step = 0; step < 3; step++)
                {
                    if (step > 0) controller.TickPreview(1.0f);
                    Color[] captured = Capture(camera, target, pixels, Path.Combine(output, "transition_" + step));
                    if (step == 0) transitionStart = captured;
                    Record(rows, "transition_" + step, controller, wind.State, Difference(transitionStart, captured));
                    yield return null;
                }
                Assert.IsFalse(controller.IsTransitioning);
                Assert.AreSame(rain, controller.ActiveProfile);
            }
            finally
            {
                File.WriteAllText(Path.Combine(output, "Weather_Inputs.csv"), rows.ToString());
                if (controller != null) controller.EndPreview();
                if (camera != null) camera.targetTexture = originalTarget;
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                if (volumeProfile != null) UnityEngine.Object.DestroyImmediate(volumeProfile);
                UnityEngine.Object.DestroyImmediate(rain);
                UnityEngine.Object.DestroyImmediate(clear);
                CollectionAssert.AreEqual(sceneBytes, File.ReadAllBytes(SCENE_PATH), "Read-only capture must preserve the saved scene bytes.");
            }
        }

        private static Color[] Capture(Camera camera, RenderTexture target, Texture2D pixels, string path)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
                pixels.Apply(false, false);
                Color[] colors = pixels.GetPixels();
                string metrics = XRayWeatherSceneFixture.ColorImageStatistics(colors, out bool readable);
                File.WriteAllText(path + "_ImageMetrics.txt", metrics);
                File.WriteAllBytes(path + ".exr", pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                var display = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false, false);
                try
                {
                    Color[] gamma = (Color[])colors.Clone();
                    for (int i = 0; i < gamma.Length; i++) gamma[i] = gamma[i].gamma;
                    display.SetPixels(gamma);
                    display.Apply(false, false);
                    File.WriteAllBytes(path + ".png", display.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(display);
                }
                Assert.IsTrue(readable, "Whole imported map must contain nonflat finite visible surfaces at the fixed exposure: " + metrics);
                return colors;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static void Record(StringBuilder rows, string id, XRayWeatherController controller, XRayFloraWindState wind, float imageDelta)
        {
            XRayWeatherSnapshot weather = controller.Snapshot;
            Vector3 light = controller.SourceLightColor;
            rows.AppendLine(string.Join(",", id, Num(controller.GameTimeSeconds), weather.Key0, weather.Key1, Num(weather.SunColor.r), Num(weather.SunColor.g), Num(weather.SunColor.b), Num(light.x), Num(light.y), Num(light.z), Num(weather.SunDirection.x), Num(weather.SunDirection.y), Num(weather.SunDirection.z), Num(weather.WindDirectionRadians), Num(weather.WindVelocity), Num(wind.Time), Num(wind.Anim.x), Num(wind.Anim.y), Num(wind.AnimPrev.x), Num(wind.AnimPrev.y), Num(imageDelta)));
        }

        private static float Difference(Color[] a, Color[] b)
        {
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            float difference = 0.0f;
            for (int i = 0; i < a.Length; i++) difference = Mathf.Max(difference, Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b));
            return difference;
        }

        private static string Num(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
