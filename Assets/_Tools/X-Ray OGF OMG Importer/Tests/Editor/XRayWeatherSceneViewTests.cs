using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherSceneViewTests
    {
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator NativeSceneViewShowsImportedWeatherAndWindWithFixedCamera()
        {
            if (Application.isBatchMode) Assert.Ignore("A native Scene View requires GUI repaints; Camera.Render is not a substitute.");
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE07A_OUTPUT");
            Assert.IsNotEmpty(output);
            Assert.IsTrue(Path.GetFullPath(output).StartsWith(@"E:\Projects\stalker_raid_into_darkness_Archive\", StringComparison.OrdinalIgnoreCase));
            output = Path.Combine(output, "WeatherNativeSceneView");
            Directory.CreateDirectory(output);
            HdrpTestWarmup.Ensure();
            XRayWeatherProfile clear = XRayWeatherImporter.ImportCurrent("w_clear1");
            XRayWeatherProfile rain = XRayWeatherImporter.ImportCurrent("w_rain1");
            var rows = new StringBuilder("Scenario,GameSeconds,SunR,SunG,SunB,DirectionX,DirectionY,DirectionZ,WindDirection,WindVelocity,WindTime,CurrentX,CurrentY,PreviousX,PreviousY,MaxImageDelta\n");
            try
            {
                File.WriteAllText(Path.Combine(output, "w_clear1.json"), JsonUtility.ToJson(clear, true));
                File.WriteAllText(Path.Combine(output, "w_rain1.json"), JsonUtility.ToJson(rain, true));
                using (var fixture = new XRayWeatherSceneFixture(clear))
                using (var capture = new NativeCapture(fixture))
                {
                    XRayWeatherController controller = fixture.Controller;
                    controller.BeginPreview();
                    Assert.IsTrue(controller.PreviewActive, controller.Diagnostic);
                    controller.SetTimeScale(0.0f);
                    Color[] previous = null;
                    foreach (double seconds in new[] { 25200.0, 46800.0, 72000.0 })
                    {
                        controller.Seek(seconds);
                        Color[] pixels = null;
                        double time = controller.GameTimeSeconds;
                        int ticks = fixture.Wind.State.Ticks;
                        yield return capture.Frame(Path.Combine(output, "clear_" + Num(seconds)), value => pixels = value);
                        Assert.AreEqual(time, controller.GameTimeSeconds);
                        Assert.AreEqual(ticks, fixture.Wind.State.Ticks, "Native Scene View camera repaints must not tick weather or wind.");
                        Assert.IsNotNull(pixels);
                        Record(rows, "clear", controller, fixture.Wind.State, previous, pixels);
                        previous = pixels;
                    }
                    controller.Seek(46800.0);
                    Color[] clearPixels = null;
                    yield return capture.Frame(Path.Combine(output, "clear_fixed_noon"), value => clearPixels = value);
                    controller.SetProfile(rain);
                    Color[] rainPixels = null;
                    yield return capture.Frame(Path.Combine(output, "rain_fixed_noon"), value => rainPixels = value);
                    Record(rows, "rain", controller, fixture.Wind.State, clearPixels, rainPixels);
                    Assert.Greater(Difference(clearPixels, rainPixels), 0.00001f, "Confirmed clear/rain source light inputs must visibly change native Scene View pixels at fixed exposure.");
                    controller.SetProfile(clear);
                    fixture.SourceMaterial.SetFloat("_MonolithDebug", 34.0f);
                    controller.TickPreview(0.1f);
                    Color[] wind0 = null;
                    yield return capture.Frame(Path.Combine(output, "wind_0"), value => wind0 = value);
                    for (int tick = 0; tick < 20; tick++) controller.TickPreview(0.1f);
                    Color[] wind1 = null;
                    yield return capture.Frame(Path.Combine(output, "wind_1"), value => wind1 = value);
                    Record(rows, "wind_geometry", controller, fixture.Wind.State, wind0, wind1);
                    Assert.Greater(Difference(wind0, wind1), 0.00001f, "Imported tree geometry must visibly move under source weather wind during native repaints.");
                    fixture.SourceMaterial.SetFloat("_MonolithDebug", 0.0f);
                    controller.TransitionTo(rain, 2.0f);
                    for (int step = 0; step < 3; step++)
                    {
                        if (step > 0) controller.TickPreview(1.0f);
                        Color[] pixels = null;
                        yield return capture.Frame(Path.Combine(output, "transition_" + step), value => pixels = value);
                        Record(rows, "transition_" + step, controller, fixture.Wind.State, previous, pixels);
                        previous = pixels;
                    }
                    Assert.AreSame(rain, controller.ActiveProfile);
                    controller.EndPreview();
                }
            }
            finally
            {
                Object.DestroyImmediate(clear);
                Object.DestroyImmediate(rain);
                File.WriteAllText(Path.Combine(output, "Snapshots.csv"), rows.ToString());
            }
        }


        [UnityTest]
        [Timeout(180000)]
        public IEnumerator PairedGameAndNativeUseMatchedWeatherCaptureStages()
        {
            if (Application.isBatchMode) Assert.Ignore("The paired review requires a visible native Scene View.");
            Assert.IsFalse(EditorApplication.isPlayingOrWillChangePlaymode);
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE07A_OUTPUT");
            Assert.IsNotEmpty(output);
            Assert.IsTrue(Path.GetFullPath(output).StartsWith(@"E:\Projects\stalker_raid_into_darkness_Archive\", StringComparison.OrdinalIgnoreCase));
            output = Path.Combine(output, "PairedCaptureReview");
            Directory.CreateDirectory(output);
            HdrpTestWarmup.Ensure();
            XRayWeatherProfile profile = XRayWeatherImporter.ImportCurrent("w_clear1");
            RTHandle gameColor = null;
            RTHandle gameOutput = null;
            AOVRequestDataCollection gameRequests = null;
            try
            {
                File.WriteAllText(Path.Combine(output, "Profile.json"), JsonUtility.ToJson(profile, true));
                using (var fixture = new XRayWeatherSceneFixture(profile))
                using (var native = new NativeCapture(fixture, true))
                using (var lights = new PairedCullingCapture(fixture.Camera, native.Camera, fixture.Scene))
                {
                    XRayWeatherController controller = fixture.Controller;
                    Assert.IsTrue(controller.BeginPreview(), controller.Diagnostic);
                    controller.Seek(46800.0);
                    controller.SetTimeScale(0.0f);
                    controller.SetPaused(true);
                    fixture.SourceMaterial.SetFloat("_MonolithDebug", 0.0f);
                    yield return native.MatchPairedViewport();
                    Color[] warmup = null;
                    yield return native.Frame(Path.Combine(output, "Native_AOV_Output_Warmup"), value => warmup = value);
                    Camera sceneCamera = native.Camera;
                    Camera gameCamera = fixture.Camera;
                    HDAdditionalCameraData gameData = gameCamera.GetComponent<HDAdditionalCameraData>();
                    gameData.SetAOVRequests(null);
                    gameCamera.Render();
                    gameCamera.Render();
                    Color[] gameOriginal = ReadPairedFloat(gameCamera.targetTexture, Path.Combine(output, "Game_Original_Fixture_Without_AOV"));
                    WritePairedCameraState(gameCamera, Path.Combine(output, "Game_Original_Fixture_State.txt"));
                    MatchPairedPose(sceneCamera, gameCamera);
                    gameCamera.Render();
                    gameCamera.Render();
                    Color[] gamePoseOnly = ReadPairedFloat(gameCamera.targetTexture, Path.Combine(output, "Game_Pose_Only_Without_AOV"));
                    WritePairedCameraState(gameCamera, Path.Combine(output, "Game_Pose_Only_State.txt"));
                    FrameSettings originalFrameSettings = gameData.renderingPathCustomFrameSettings;
                    FrameSettingsOverrideMask originalOverrideMask = gameData.renderingPathCustomFrameSettingsOverrideMask;
                    LayerMask originalVolumeMask = gameData.volumeLayerMask;
                    ulong originalSceneCullingMask = gameCamera.overrideSceneCullingMask;
                    bool originalAllowHdr = gameCamera.allowHDR;
                    gameCamera.allowHDR = true;
                    gameCamera.Render();
                    gameCamera.Render();
                    Color[] gameHdrOnly = ReadPairedFloat(gameCamera.targetTexture, Path.Combine(output, "Game_AllowHDR_Only_Without_AOV"));
                    WritePairedCameraState(gameCamera, Path.Combine(output, "Game_AllowHDR_Only_State.txt"));
                    gameCamera.allowHDR = originalAllowHdr;
                    gameData.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Bloom, false);
                    gameData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Bloom] = true;
                    gameCamera.Render();
                    gameCamera.Render();
                    Color[] gameBloomOnly = ReadPairedFloat(gameCamera.targetTexture, Path.Combine(output, "Game_Bloom_Only_Without_AOV"));
                    WritePairedCameraState(gameCamera, Path.Combine(output, "Game_Bloom_Only_State.txt"));
                    gameData.renderingPathCustomFrameSettings = originalFrameSettings;
                    gameData.renderingPathCustomFrameSettingsOverrideMask = originalOverrideMask;
                    gameCamera.overrideSceneCullingMask = ulong.MaxValue;
                    gameData.volumeLayerMask = 1 << 31;
                    gameCamera.Render();
                    gameCamera.Render();
                    Color[] gameVisibilityOnly = ReadPairedFloat(gameCamera.targetTexture, Path.Combine(output, "Game_Visibility_Only_Without_AOV"));
                    WritePairedCameraState(gameCamera, Path.Combine(output, "Game_Visibility_Only_State.txt"));
                    gameCamera.overrideSceneCullingMask = originalSceneCullingMask;
                    gameData.volumeLayerMask = originalVolumeMask;
                    native.Data.CopyTo(gameData);
                    FrameSettings matched = HDCamera.GetOrCreate(sceneCamera).frameSettings;
                    native.Data.renderingPathCustomFrameSettings = matched;
                    gameData.renderingPathCustomFrameSettings = matched;
                    foreach (FrameSettingsField field in Enum.GetValues(typeof(FrameSettingsField)))
                    {
                        uint index = (uint)field;
                        if (index >= native.Data.renderingPathCustomFrameSettingsOverrideMask.mask.capacity) continue;
                        native.Data.renderingPathCustomFrameSettingsOverrideMask.mask[index] = true;
                        gameData.renderingPathCustomFrameSettingsOverrideMask.mask[index] = true;
                    }
                    sceneCamera.allowHDR = true;
                    gameCamera.allowHDR = true;
                    sceneCamera.allowMSAA = false;
                    gameCamera.allowMSAA = false;
                    gameCamera.cullingMask = sceneCamera.cullingMask;
                    gameCamera.overrideSceneCullingMask = sceneCamera.overrideSceneCullingMask;
                    MatchPairedPose(sceneCamera, gameCamera);
                    gameColor = RTHandles.Alloc(640, 640, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "Paired Game AOV Color");
                    gameOutput = RTHandles.Alloc(640, 640, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "Paired Game AOV Output");
                    int gameCallbacks = 0;
                    var request = AOVRequest.NewDefault();
                    request.SetOverrideRenderFormat(true);
                    var builder = new AOVRequestBuilder();
                    builder.Add(request, buffer => buffer == AOVBuffers.Color ? gameColor : gameOutput, null, new[] { AOVBuffers.Color, AOVBuffers.Output }, (command, textures, properties) => gameCallbacks++);
                    gameRequests = builder.Build();
                    double seconds = controller.GameTimeSeconds;
                    Vector4 wind = fixture.Wind.State.Anim;
                    int ticks = fixture.Wind.State.Ticks;
                    gameData.SetAOVRequests(null);
                    gameCamera.Render();
                    gameCamera.Render();
                    Color[] gameWithoutAov = ReadPairedFloat(gameCamera.targetTexture, Path.Combine(output, "Game_Final_Without_AOV"));
                    WritePairedCameraState(gameCamera, Path.Combine(output, "Game_Scene_Only_Baseline_State.txt"));
                    lights.WriteGame(Path.Combine(output, "Game_Scene_Only_Baseline_DiagnosticCameraCull.txt"));
                    ulong isolatedSceneMask = gameCamera.overrideSceneCullingMask;
                    Assert.AreEqual(UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(fixture.Scene), isolatedSceneMask);
                    Color[] gameControlledLeak = null;
                    try
                    {
                        gameCamera.overrideSceneCullingMask = ulong.MaxValue;
                        gameCamera.Render();
                        gameCamera.Render();
                        gameControlledLeak = ReadPairedFloat(gameCamera.targetTexture, Path.Combine(output, "Game_Controlled_MaxMask_Leak_Without_AOV"));
                        WritePairedCameraState(gameCamera, Path.Combine(output, "Game_Controlled_MaxMask_Leak_State.txt"));
                        lights.WriteGame(Path.Combine(output, "Game_Controlled_MaxMask_Leak_DiagnosticCameraCull.txt"));
                    }
                    finally
                    {
                        gameCamera.overrideSceneCullingMask = isolatedSceneMask;
                    }
                    gameCamera.Render();
                    gameCamera.Render();
                    Color[] gameRestored = ReadPairedFloat(gameCamera.targetTexture, Path.Combine(output, "Game_Restored_Scene_Only_Without_AOV"));
                    WritePairedCameraState(gameCamera, Path.Combine(output, "Game_Restored_Scene_Only_State.txt"));
                    lights.WriteGame(Path.Combine(output, "Game_Restored_Scene_Only_DiagnosticCameraCull.txt"));
                    gameData.SetAOVRequests(gameRequests);
                    Color[] nativeOutput = null;
                    yield return native.Frame(Path.Combine(output, "Native_AOV_Output"), value => nativeOutput = value);
                    Color[] nativeColor = ReadPairedFloat(native.ColorTexture, Path.Combine(output, "Native_AOV_Color"));
                    gameCamera.Render();
                    gameCamera.Render();
                    Assert.GreaterOrEqual(gameCallbacks, 2);
                    Color[] gameColorPixels = ReadPairedFloat(gameColor.rt, Path.Combine(output, "Game_AOV_Color"));
                    Color[] gameOutputPixels = ReadPairedFloat(gameOutput.rt, Path.Combine(output, "Game_AOV_Output"));
                    Color[] gameFinal = ReadPairedFloat(gameCamera.targetTexture, Path.Combine(output, "Game_Final_Target"));
                    yield return native.CaptureVisibleWindow(Path.Combine(output, "Native_Visible_Window"), nativeOutput);
                    WritePairedCameraState(gameCamera, Path.Combine(output, "Game_Matched_State.txt"));
                    WritePairedCameraState(sceneCamera, Path.Combine(output, "Native_Matched_State.txt"));
                    lights.WriteGame(Path.Combine(output, "Game_Final_DiagnosticCameraCull.txt"));
                    lights.WriteNative(Path.Combine(output, "Native_Final_DiagnosticCameraCull.txt"));
                    HDCamera gameHd = HDCamera.GetOrCreate(gameCamera);
                    HDCamera nativeHd = HDCamera.GetOrCreate(sceneCamera);
                    Exposure gameExposure = gameHd.volumeStack.GetComponent<Exposure>();
                    Exposure nativeExposure = nativeHd.volumeStack.GetComponent<Exposure>();
                    var frames = new StringBuilder("field,game,native\n");
                    foreach (FrameSettingsField field in Enum.GetValues(typeof(FrameSettingsField)))
                    {
                        if ((uint)field >= gameData.renderingPathCustomFrameSettingsOverrideMask.mask.capacity) continue;
                        bool gameValue = gameHd.frameSettings.IsEnabled(field);
                        bool nativeValue = nativeHd.frameSettings.IsEnabled(field);
                        frames.AppendLine(field + "," + gameValue + "," + nativeValue);
                    }
                    File.WriteAllText(Path.Combine(output, "Actual_Frame_Settings.csv"), frames.ToString());
                    string metrics = "comparison,max_rgb_delta,mean_abs_rgb_delta\n" + PairedDifference("GameAOVColor_NativeAOVColor", gameColorPixels, nativeColor) + PairedDifference("GameAOVOutput_NativeAOVOutput", gameOutputPixels, nativeOutput) + PairedDifference("GameFinalTarget_GameAOVOutput", gameFinal, gameOutputPixels) + PairedDifference("GameFinalWithoutAOV_GameFinalWithAOV", gameWithoutAov, gameFinal) + PairedDifference("GameOriginal_GamePoseOnly", gameOriginal, gamePoseOnly) + PairedDifference("GamePoseOnly_GameAllowHDROnly", gamePoseOnly, gameHdrOnly) + PairedDifference("GamePoseOnly_GameBloomOnly", gamePoseOnly, gameBloomOnly) + PairedDifference("GamePoseOnly_GameVisibilityOnly", gamePoseOnly, gameVisibilityOnly) + PairedDifference("GamePoseOnly_GameMatchedWithoutAOV", gamePoseOnly, gameWithoutAov) + PairedDifference("GameSceneOnlyBaseline_ControlledMaxMaskLeak", gameWithoutAov, gameControlledLeak) + PairedDifference("GameSceneOnlyBaseline_RestoredSceneOnly", gameWithoutAov, gameRestored);
                    File.WriteAllText(Path.Combine(output, "Stage_Comparison.csv"), metrics);
                    File.WriteAllText(Path.Combine(output, "Scene_Isolation_Contract.txt"), "FixtureScene=" + fixture.Scene.path + "#" + fixture.Scene.handle + "\nFixtureSceneMask=" + isolatedSceneMask + "\nControlledLeakChangesOnly=Game.overrideSceneCullingMask to ulong.MaxValue\nBaselineLeakRestoredCameraCullingMask=" + gameCamera.cullingMask + "\nBaselineLeakRestoredVolumeMask=" + gameData.volumeLayerMask.value + "\nCullMetadata=DiagnosticCameraCull via beginCameraRendering before HDRP actual Cull; not a GPU binding list\nHostLightPresenceInLeak=recorded, not required in a clean host\n");
                    File.WriteAllText(Path.Combine(output, "Contract.txt"), "Profile=w_clear1\nGameSeconds=" + Num(seconds) + "\nFrozenWind=" + wind.ToString("F8") + "\nSourceLight=" + controller.SourceLightColor.ToString("F8") + "\nSunLux=3 (unchanged diagnostic)\nSourceSunlightScale=" + profile.SunLightScale.ToString("F8") + "\nActualGameExposureMode=" + gameExposure.mode.value + "\nActualGameEV100=" + Num(gameExposure.fixedExposure.value) + "\nActualNativeExposureMode=" + nativeExposure.mode.value + "\nActualNativeEV100=" + Num(nativeExposure.fixedExposure.value) + "\nGameAllowHDR=" + gameCamera.allowHDR + "\nNativeAllowHDR=" + sceneCamera.allowHDR + "\nGameVolumeMask=" + gameData.volumeLayerMask.value + "\nNativeVolumeMask=" + native.Data.volumeLayerMask.value + "\nGameTargetFormat=" + gameCamera.targetTexture.graphicsFormat + "\nGameAOVColorFormat=" + gameColor.rt.graphicsFormat + "\nGameAOVOutputFormat=" + gameOutput.rt.graphicsFormat + "\nNativeAOVColorFormat=" + native.ColorTexture.graphicsFormat + "\nNativeAOVOutputFormat=" + native.OutputTexture.graphicsFormat + "\nOverrideInternalAOVRenderFormat=true on both requests\nGameView=" + gameCamera.worldToCameraMatrix.ToString("F8") + "\nNativeView=" + sceneCamera.worldToCameraMatrix.ToString("F8") + "\nGameProjection=" + gameCamera.projectionMatrix.ToString("F8") + "\nNativeProjection=" + sceneCamera.projectionMatrix.ToString("F8") + "\nRawCameraFiles=linear Float32 EXR\nCameraGammaSDRPNG=componentwise Color.gamma then RGBA32 encoding; values above display white cannot be represented\nSurfaceAboveDisplayWhite is not proof of clipping in raw HDR\nAOVColor=before postprocess\nAOVOutput=postprocess destination before final target blit\nGameFinalTarget=after final target blit\nNativeVisibleWindow=exact task-owned EditorWindow backbuffer via CaptureEditorWindow into non-sRGB UNorm target; display-encoded direct RGB24 PNG without manual gamma\nNativeWindowReference=raw-linear Native_AOV_Output.exr and once-gamma AOV central448 display PNG\nNativeWindowDisplayAssertion=UI-free central448 RGB max delta within 2/255 of own AOV display reference\nCrossAPIPixelParityAssertion=not imposed; measured matched-stage deltas are evidence\n");
                    Assert.AreEqual(seconds, controller.GameTimeSeconds);
                    Assert.AreEqual(wind, fixture.Wind.State.Anim);
                    Assert.AreEqual(ticks, fixture.Wind.State.Ticks);
                    Assert.AreEqual(640, sceneCamera.pixelWidth);
                    Assert.AreEqual(640, sceneCamera.pixelHeight);
                    Assert.AreEqual(640, gameCamera.pixelWidth);
                    Assert.AreEqual(640, gameCamera.pixelHeight);
                    Assert.AreEqual(sceneCamera.allowHDR, gameCamera.allowHDR);
                    Assert.That(Vector3.Distance(sceneCamera.transform.position, gameCamera.transform.position), Is.LessThan(0.000001f));
                    Assert.That(Quaternion.Angle(sceneCamera.transform.rotation, gameCamera.transform.rotation), Is.LessThan(0.0001f));
                    for (int i = 0; i < 16; i++) Assert.That(gameCamera.projectionMatrix[i], Is.EqualTo(sceneCamera.projectionMatrix[i]).Within(0.000001f));
                    foreach (FrameSettingsField field in Enum.GetValues(typeof(FrameSettingsField)))
                    {
                        if ((uint)field >= gameData.renderingPathCustomFrameSettingsOverrideMask.mask.capacity) continue;
                        Assert.AreEqual(nativeHd.frameSettings.IsEnabled(field), gameHd.frameSettings.IsEnabled(field), "Frame setting " + field);
                    }
                    Assert.AreEqual(ExposureMode.Fixed, gameExposure.mode.value);
                    Assert.AreEqual(ExposureMode.Fixed, nativeExposure.mode.value);
                    Assert.That(gameExposure.fixedExposure.value, Is.EqualTo(6.0f).Within(0.0001f));
                    Assert.That(nativeExposure.fixedExposure.value, Is.EqualTo(6.0f).Within(0.0001f));
                    lights.AssertFinalIsolation();
                    Assert.AreEqual(isolatedSceneMask, gameCamera.overrideSceneCullingMask);
                    foreach (Renderer renderer in fixture.Root.GetComponentsInChildren<Renderer>())
                    {
                        Assert.AreEqual(fixture.Scene, renderer.gameObject.scene);
                        Assert.AreEqual(31, renderer.gameObject.layer);
                    }
                    controller.EndPreview();
                    gameData.SetAOVRequests(null);
                }
            }
            finally
            {
                if (gameRequests != null) gameRequests.Dispose();
                if (gameColor != null) RTHandles.Release(gameColor);
                if (gameOutput != null) RTHandles.Release(gameOutput);
                Object.DestroyImmediate(profile);
            }
        }

        private static void MatchPairedPose(Camera source, Camera destination)
        {
            destination.orthographic = source.orthographic;
            destination.orthographicSize = source.orthographicSize;
            destination.nearClipPlane = source.nearClipPlane;
            destination.farClipPlane = source.farClipPlane;
            destination.aspect = source.aspect;
            destination.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            destination.worldToCameraMatrix = source.worldToCameraMatrix;
            destination.projectionMatrix = source.projectionMatrix;
        }

        private static void WritePairedCameraState(Camera camera, string path)
        {
            HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
            HDCamera hdCamera = HDCamera.GetOrCreate(camera);
            Exposure exposure = hdCamera.volumeStack.GetComponent<Exposure>();
            ColorAdjustments colorAdjustments = hdCamera.volumeStack.GetComponent<ColorAdjustments>();
            Tonemapping tonemapping = hdCamera.volumeStack.GetComponent<Tonemapping>();
            WhiteBalance whiteBalance = hdCamera.volumeStack.GetComponent<WhiteBalance>();
            LiftGammaGain liftGammaGain = hdCamera.volumeStack.GetComponent<LiftGammaGain>();
            Volume volume = camera.GetComponentInParent<Volume>();
            Assert.IsNotNull(volume);
            var state = new StringBuilder();
            state.AppendLine("CameraType=" + camera.cameraType);
            state.AppendLine("AllowHDR=" + camera.allowHDR);
            state.AppendLine("OverrideSceneCullingMask=" + camera.overrideSceneCullingMask.ToString(CultureInfo.InvariantCulture));
            state.AppendLine("CameraScene=" + camera.scene.name + "#" + camera.scene.handle);
            state.AppendLine("FixtureVolumeScene=" + volume.gameObject.scene.name + "#" + volume.gameObject.scene.handle);
            state.AppendLine("FixtureVolumeLayer=" + volume.gameObject.layer);
            state.AppendLine("FixtureVolumeRegisteredInMask=" + (Array.IndexOf(VolumeManager.instance.GetVolumes(data.volumeLayerMask), volume) >= 0));
            state.AppendLine("FixtureVolumeVisibleToCamera=" + UnityEditor.SceneManagement.StageUtility.IsGameObjectRenderedByCamera(volume.gameObject, camera));
            state.AppendLine("TargetFormat=" + (camera.targetTexture != null ? camera.targetTexture.graphicsFormat.ToString() : "window"));
            state.AppendLine("VolumeMask=" + data.volumeLayerMask.value);
            state.AppendLine("CustomRenderingSettings=" + data.customRenderingSettings);
            state.AppendLine("DefaultFrameSettings=" + data.defaultFrameSettings);
            state.AppendLine("OverrideMask=" + data.renderingPathCustomFrameSettingsOverrideMask.mask.humanizedData);
            state.AppendLine("ActualExposureMode=" + exposure.mode.value);
            state.AppendLine("ActualEV100=" + Num(exposure.fixedExposure.value));
            state.AppendLine("ActualExposureCompensation=" + Num(exposure.compensation.value));
            state.AppendLine("ActualColorAdjustmentsPostExposureEV=" + Num(colorAdjustments.postExposure.value));
            state.AppendLine("ActualColorAdjustmentsContrast=" + Num(colorAdjustments.contrast.value));
            state.AppendLine("ActualColorAdjustmentsColorFilter=" + colorAdjustments.colorFilter.value.ToString("F8"));
            state.AppendLine("ActualTonemappingMode=" + tonemapping.mode.value);
            state.AppendLine("ActualWhiteBalanceTemperature=" + Num(whiteBalance.temperature.value));
            state.AppendLine("ActualWhiteBalanceTint=" + Num(whiteBalance.tint.value));
            state.AppendLine("ActualLiftGammaGainGamma=" + liftGammaGain.gamma.value.ToString("F8"));
            state.AppendLine("Position=" + camera.transform.position.ToString("F8"));
            state.AppendLine("Rotation=" + camera.transform.rotation.ToString("F8"));
            state.AppendLine("Projection=" + camera.projectionMatrix.ToString("F8"));
            state.AppendLine("View=" + camera.worldToCameraMatrix.ToString("F8"));
            foreach (FrameSettingsField field in Enum.GetValues(typeof(FrameSettingsField)))
            {
                if ((uint)field >= data.renderingPathCustomFrameSettingsOverrideMask.mask.capacity) continue;
                state.AppendLine(field + "=" + hdCamera.frameSettings.IsEnabled(field));
            }
            File.WriteAllText(path, state.ToString());
        }

        private static Color[] ReadPairedFloat(RenderTexture texture, string stem)
        {
            Assert.IsNotNull(texture);
            Assert.AreEqual(640, texture.width);
            Assert.AreEqual(640, texture.height);
            var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                pixels.Apply(false, false);
                Color[] colors = pixels.GetPixels();
                string statistics = XRayWeatherSceneFixture.ColorImageStatistics(colors, out bool readable);
                File.WriteAllText(stem + "_Metrics.txt", statistics.Replace("WhiteClipped=", "SurfaceAboveDisplayWhite=") + "\nTextureFormat=" + texture.graphicsFormat + "\nRaw HDR values above 0.99 are not evidence of raw clipping.\n");
                File.WriteAllBytes(stem + ".exr", pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                var display = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
                try
                {
                    var encoded = (Color[])colors.Clone();
                    for (int i = 0; i < encoded.Length; i++) encoded[i] = encoded[i].gamma;
                    display.SetPixels(encoded);
                    display.Apply();
                    File.WriteAllBytes(stem + "_GammaSDR.png", display.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(display);
                }
                return colors;
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
            }
        }

        private static string PairedDifference(string name, Color[] a, Color[] b)
        {
            Assert.AreEqual(a.Length, b.Length);
            float maximum = 0.0f;
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    float delta = Mathf.Abs(a[i][channel] - b[i][channel]);
                    Assert.IsFalse(float.IsNaN(delta) || float.IsInfinity(delta));
                    maximum = Mathf.Max(maximum, delta);
                    sum += delta;
                }
            }
            return FormattableString.Invariant($"{name},{maximum:R},{sum / (a.Length * 3):R}\n");
        }

        private static void Record(StringBuilder rows, string scenario, XRayWeatherController controller, XRayFloraWindState wind, Color[] previous, Color[] pixels)
        {
            XRayWeatherSnapshot snapshot = controller.Snapshot;
            rows.AppendLine(string.Join(",", scenario, Num(controller.GameTimeSeconds), Num(snapshot.SunColor.r), Num(snapshot.SunColor.g), Num(snapshot.SunColor.b), Num(snapshot.SunDirection.x), Num(snapshot.SunDirection.y), Num(snapshot.SunDirection.z), Num(snapshot.WindDirectionRadians), Num(snapshot.WindVelocity), Num(wind.Time), Num(wind.Anim.x), Num(wind.Anim.y), Num(wind.AnimPrev.x), Num(wind.AnimPrev.y), Num(previous == null ? 0.0f : Difference(previous, pixels))));
        }

        private static float Difference(Color[] before, Color[] after)
        {
            float maximum = 0.0f;
            for (int i = 0; i < before.Length; i++) maximum = Mathf.Max(maximum, Mathf.Abs(before[i].r - after[i].r), Mathf.Abs(before[i].g - after[i].g), Mathf.Abs(before[i].b - after[i].b));
            return maximum;
        }

        private static string Num(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private sealed class PairedCullingCapture : IDisposable
        {
            private readonly Camera _gameCamera;
            private readonly Camera _nativeCamera;
            private readonly Scene _scene;
            private PairedLightState _game;
            private PairedLightState _native;

            public PairedCullingCapture(Camera gameCamera, Camera nativeCamera, Scene scene)
            {
                _gameCamera = gameCamera;
                _nativeCamera = nativeCamera;
                _scene = scene;
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            }

            public void WriteGame(string path)
            {
                Assert.IsNotNull(_game);
                File.WriteAllText(path, _game.Metadata);
            }

            public void WriteNative(string path)
            {
                Assert.IsNotNull(_native);
                File.WriteAllText(path, _native.Metadata);
            }

            public void AssertFinalIsolation()
            {
                Assert.IsNotNull(_game);
                Assert.IsNotNull(_native);
                Assert.IsTrue(_game.AllLightsInScene, "Final Game diagnostic cull must exclude lights outside the task-owned fixture scene.");
                Assert.IsTrue(_native.AllLightsInScene, "Final native diagnostic cull must exclude lights outside the task-owned fixture scene.");
                Assert.AreEqual(1, _game.Count);
                Assert.AreEqual(1, _native.Count);
                Assert.AreEqual(_game.FirstId, _native.FirstId);
                Assert.AreEqual(_game.FirstFinalColor, _native.FirstFinalColor);
            }

            private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
            {
                if (cameras.Contains(_gameCamera)) _gameCamera.cullingMask = 1 << 31;
                if (cameras.Contains(_nativeCamera)) _nativeCamera.cullingMask = 1 << 31;
            }

            private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
            {
                if (camera != _gameCamera && camera != _nativeCamera) return;
                Assert.AreEqual(1 << 31, camera.cullingMask);
                Assert.IsTrue(camera.TryGetCullingParameters(camera.stereoEnabled, out ScriptableCullingParameters parameters));
                CullingResults results = context.Cull(ref parameters);
                var state = new PairedLightState(camera, results, _scene);
                if (camera == _gameCamera) _game = state;
                else _native = state;
            }

            public void Dispose()
            {
                RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            }
        }

        private sealed class PairedLightState
        {
            public int Count { get; }
            public string FirstId { get; }
            public Color FirstFinalColor { get; }
            public bool AllLightsInScene { get; }
            public string Metadata { get; }

            public PairedLightState(Camera camera, CullingResults results, Scene scene)
            {
                var lights = results.visibleLights;
                Count = lights.Length;
                bool owned = true;
                var state = new StringBuilder("FixtureLayer31=Enforced on tracked transient cameras at beginContextRendering before HDRP culling parameter generation; verified at beginCameraRendering\nCapture=DiagnosticCameraCull via beginCameraRendering before HDRP actual Cull; not a GPU binding list\n");
                state.AppendLine("Camera=" + camera.name);
                state.AppendLine("CameraType=" + camera.cameraType);
                state.AppendLine("CameraCullingMask=" + camera.cullingMask);
                state.AppendLine("OverrideSceneCullingMask=" + camera.overrideSceneCullingMask);
                state.AppendLine("ExpectedScene=" + scene.path + "#" + scene.handle);
                state.AppendLine("VisibleLightCount=" + Count);
                for (int i = 0; i < lights.Length; i++)
                {
                    Light light = lights[i].light;
                    bool inScene = light != null && light.gameObject.scene == scene;
                    owned &= inScene;
                    state.AppendLine("Light[" + i + "].FinalColor=" + lights[i].finalColor.ToString("F8"));
                    state.AppendLine("Light[" + i + "].InFixtureScene=" + inScene);
                    if (light == null) continue;
                    if (i == 0)
                    {
                        FirstId = light.GetEntityId().ToString();
                        FirstFinalColor = lights[i].finalColor;
                    }
                    state.AppendLine("Light[" + i + "].Id=" + light.GetEntityId().ToString());
                    state.AppendLine("Light[" + i + "].Name=" + light.name);
                    state.AppendLine("Light[" + i + "].Type=" + light.type);
                    state.AppendLine("Light[" + i + "].Scene=" + light.gameObject.scene.path + "#" + light.gameObject.scene.handle);
                    state.AppendLine("Light[" + i + "].SceneName=" + light.gameObject.scene.name);
                    state.AppendLine("Light[" + i + "].ScenePath=" + light.gameObject.scene.path);
                    state.AppendLine("Light[" + i + "].Layer=" + light.gameObject.layer);
                    state.AppendLine("Light[" + i + "].CullingMask=" + light.cullingMask);
                    state.AppendLine("Light[" + i + "].Intensity=" + Num(light.intensity));
                }
                AllLightsInScene = owned;
                Metadata = state.ToString();
            }
        }

        private sealed class NativeCapture : IDisposable
        {
            private const int SIZE = 640;

            private readonly WeatherSceneView _view;
            private readonly Camera _camera;
            private readonly HDAdditionalCameraData _data;
            private readonly Material _material;
            private readonly Volume _volume;
            private readonly Texture2D _pixels;
            private readonly AOVRequestDataCollection _requests;
            private readonly RTHandle _color;
            private readonly RTHandle _rawColor;
            private readonly ulong _sceneCullingMask;
            private RTHandle _source;
            private RTHandle _rawSource;
            private int _version;

            public Camera Camera => _camera;
            public HDAdditionalCameraData Data => _data;
            public RenderTexture ColorTexture => _rawSource.rt;
            public RenderTexture OutputTexture => _source.rt;

            public NativeCapture(XRayWeatherSceneFixture fixture, bool pairedCapture = false)
            {
                _view = ScriptableObject.CreateInstance<WeatherSceneView>();
                _view.position = new Rect(100.0f, 100.0f, 680.0f, 680.0f);
                _view.ShowUtility();
                _view.Bind(fixture.Scene);
                _view.cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.Textured);
                _material = fixture.SourceMaterial;
                _volume = fixture.Root.GetComponent<Volume>();
                Assert.IsNotNull(_volume);
                _volume.enabled = false;
                foreach (Transform transform in fixture.Root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31;
                _volume.enabled = true;
                _camera = _view.camera;
                _camera.transform.SetParent(fixture.Root.transform);
                _camera.scene = fixture.Scene;
                _camera.cullingMask = 1 << 31;
                _sceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(fixture.Scene);
                Assert.AreNotEqual(0ul, _sceneCullingMask);
                _camera.overrideSceneCullingMask = _sceneCullingMask;
                _camera.enabled = false;
                _camera.orthographic = true;
                _camera.nearClipPlane = fixture.Camera.nearClipPlane;
                _camera.farClipPlane = fixture.Camera.farClipPlane;
                _camera.allowHDR = true;
                _camera.useOcclusionCulling = false;
                _data = _camera.GetComponent<HDAdditionalCameraData>();
                if (_data == null) _data = _camera.gameObject.AddComponent<HDAdditionalCameraData>();
                _data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                _data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                _data.backgroundColorHDR = new Color(0.02f, 0.03f, 0.04f, 1.0f);
                _data.customRenderingSettings = true;
                _data.volumeLayerMask = 1 << 31;
                foreach (FrameSettingsField field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.ContactShadows, FrameSettingsField.Volumetrics, FrameSettingsField.Bloom })
                {
                    _data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                    _data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                foreach (FrameSettingsField field in new[] { FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl })
                {
                    _data.renderingPathCustomFrameSettings.SetEnabled(field, true);
                    _data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                Bounds bounds = fixture.Root.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
                _view.orthographic = true;
                _view.LookAtDirect(bounds.center, Quaternion.identity, fixture.Camera.orthographicSize);
                _color = RTHandles.Alloc(SIZE, SIZE, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "Phase07A native SceneView output");
                if (pairedCapture) _rawColor = RTHandles.Alloc(SIZE, SIZE, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "Paired Native AOV Color");
                _pixels = new Texture2D(SIZE, SIZE, TextureFormat.RGBAFloat, false, true);
                var request = AOVRequest.NewDefault();
                request.SetOverrideRenderFormat(pairedCapture);
                var builder = new AOVRequestBuilder();
                builder.Add(request, buffer => buffer == AOVBuffers.Color ? _rawColor : _color, null, pairedCapture ? new[] { AOVBuffers.Color, AOVBuffers.Output } : new[] { AOVBuffers.Output }, (command, textures, properties) =>
                {
                    if (pairedCapture) _rawSource = textures[0];
                    _source = textures[pairedCapture ? 1 : 0];
                    _version++;
                });
                _requests = builder.Build();
                _data.SetAOVRequests(_requests);
            }

            public IEnumerator MatchPairedViewport()
            {
                _view.Focus();
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    _view.Repaint();
                    yield return null;
                    if (_camera.pixelWidth == SIZE && _camera.pixelHeight == SIZE) break;
                    Rect position = _view.position;
                    float scale = EditorGUIUtility.pixelsPerPoint;
                    position.width += (SIZE - _camera.pixelWidth) / scale;
                    position.height += (SIZE - _camera.pixelHeight) / scale;
                    _view.position = position;
                }
                _view.Repaint();
                yield return null;
                Assert.AreEqual(SIZE, _camera.pixelWidth);
                Assert.AreEqual(SIZE, _camera.pixelHeight);
            }

            public IEnumerator CaptureVisibleWindow(string stem, Color[] linearReference)
            {
                const int COMPARISON_MARGIN = 96;
                const int COMPARISON_SIZE = SIZE - COMPARISON_MARGIN * 2;

                EditorWindow previousWindow = EditorWindow.focusedWindow;
                RenderTexture previousTarget = RenderTexture.active;
                bool previousSrgbWrite = GL.sRGBWrite;
                RenderTexture target = null;
                Texture2D image = null;
                try
                {
                    Assert.IsNotNull(linearReference);
                    Assert.AreEqual(SIZE * SIZE, linearReference.Length);
                    _view.Focus();
                    _view.Repaint();
                    for (int frame = 0; frame < 3; frame++)
                    {
                        yield return null;
                        if (_view.hasFocus && EditorWindow.focusedWindow == _view) break;
                        _view.Focus();
                        _view.Repaint();
                    }
                    Assert.IsTrue(_view.hasFocus);
                    Assert.AreSame(_view, EditorWindow.focusedWindow);
                    Rect position = _view.position;
                    float scale = EditorGUIUtility.pixelsPerPoint;
                    int width = Mathf.RoundToInt(position.width * scale);
                    int height = Mathf.RoundToInt(position.height * scale);
                    var viewportProperty = typeof(SceneView).GetProperty("cameraViewVisualElement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    Assert.IsNotNull(viewportProperty);
                    var viewportElement = (UnityEngine.UIElements.VisualElement)viewportProperty.GetValue(_view);
                    Rect viewportBounds = viewportElement.worldBound;
                    int viewportX = Mathf.RoundToInt(viewportBounds.x * scale);
                    int viewportY = height - Mathf.RoundToInt(viewportBounds.yMax * scale);
                    Assert.AreEqual(SIZE, Mathf.RoundToInt(viewportBounds.width * scale));
                    Assert.AreEqual(SIZE, Mathf.RoundToInt(viewportBounds.height * scale));
                    Assert.GreaterOrEqual(viewportX, 0);
                    Assert.GreaterOrEqual(viewportY, 0);
                    Assert.LessOrEqual(viewportX + SIZE, width);
                    Assert.LessOrEqual(viewportY + SIZE, height);
                    target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    Assert.IsTrue(target.Create());
                    Assert.AreEqual(GraphicsFormat.R8G8B8A8_UNorm, target.graphicsFormat);
                    Assert.IsFalse(target.sRGB);
                    GL.sRGBWrite = false;
                    bool beforeCapture = GL.sRGBWrite;
                    bool captured = UnityEditorInternal.InternalEditorUtility.CaptureEditorWindow(_view, target);
                    bool afterCapture = GL.sRGBWrite;
                    Assert.IsTrue(captured, "The exact task-owned Scene View backbuffer must be available; desktop readback is not a fallback.");
                    Assert.IsTrue(_view.hasFocus);
                    Assert.AreSame(_view, EditorWindow.focusedWindow);
                    RenderTexture.active = target;
                    image = new Texture2D(width, height, TextureFormat.RGB24, false, true);
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    image.Apply();
                    Color[] colors = image.GetPixels();
                    Assert.AreEqual(width * height, colors.Length);
                    File.WriteAllBytes(stem + ".png", image.EncodeToPNG());
                    var capturedRegion = new Color[COMPARISON_SIZE * COMPARISON_SIZE];
                    var referenceRegion = new Color[capturedRegion.Length];
                    float maxDifference = 0.0f;
                    double sumDifference = 0.0;
                    for (int y = 0; y < COMPARISON_SIZE; y++)
                    {
                        for (int x = 0; x < COMPARISON_SIZE; x++)
                        {
                            int index = y * COMPARISON_SIZE + x;
                            Color actual = colors[(viewportY + y + COMPARISON_MARGIN) * width + viewportX + x + COMPARISON_MARGIN];
                            Color reference = linearReference[(y + COMPARISON_MARGIN) * SIZE + x + COMPARISON_MARGIN].gamma;
                            capturedRegion[index] = actual;
                            referenceRegion[index] = reference;
                            float red = Mathf.Abs(actual.r - reference.r);
                            float green = Mathf.Abs(actual.g - reference.g);
                            float blue = Mathf.Abs(actual.b - reference.b);
                            maxDifference = Mathf.Max(maxDifference, Mathf.Max(red, Mathf.Max(green, blue)));
                            sumDifference += red + green + blue;
                        }
                    }
                    double meanDifference = sumDifference / (capturedRegion.Length * 3.0);
                    WriteWindowPng(stem + "_Central448_Display.png", capturedRegion, COMPARISON_SIZE, COMPARISON_SIZE);
                    WriteWindowPng(stem + "_AOV_Central448_Display.png", referenceRegion, COMPARISON_SIZE, COMPARISON_SIZE);
                    File.WriteAllText(stem + "_Color_Stage_Comparison.csv", "comparison,max_rgb_delta,mean_abs_rgb_delta\nWindowCentral448_NativeAOVOutputDisplay," + Num(maxDifference) + "," + Num(meanDifference) + "\n");
                    File.WriteAllText(stem + ".txt", "Capture=UnityEditorInternal.InternalEditorUtility.CaptureEditorWindow of exact task-owned window object backbuffer\nCaptureSucceeded=" + captured + "\nWindowEntityId=" + _view.GetEntityId().ToString() + "\nWindowType=" + _view.GetType().FullName + "\nWindowTitle=" + _view.titleContent.text + "\nHasFocusAtRead=" + _view.hasFocus + "\nExactFocusedWindowAtRead=" + (EditorWindow.focusedWindow == _view) + "\nWindowRect=" + position + "\nPixelsPerPoint=" + scale + "\nReadback=" + width + "x" + height + "\nViewportBoundsInWindow=" + viewportBounds + "\nViewportReadback=" + viewportX + "," + viewportY + "," + SIZE + "," + SIZE + "\nCentralComparison=96,96,448,448 within camera viewport; toolbar, gizmo and bottom overlay excluded\nTargetGraphicsFormat=" + target.graphicsFormat + "\nTargetSRGB=" + target.sRGB + "\nActiveColorSpace=" + QualitySettings.activeColorSpace + "\nAPI=" + SystemInfo.graphicsDeviceType + "\nSRGBWriteBeforeCapture=" + beforeCapture + "\nSRGBWriteAfterCapture=" + afterCapture + "\nSDREncoding=Display-encoded backbuffer RGB transported through a non-sRGB UNorm target then direct RGB24 ReadPixels and PNG\nWindowRGBIsLinearHDR=false\nRawLinearReference=Native_AOV_Output.exr; full Float32 HDRP AOV postprocess destination\nDisplayReference=Color.gamma applied once to the already captured Native AOV Output for comparison\nMaxDisplayRGBDelta=" + Num(maxDifference) + "\nMeanDisplayRGBDelta=" + Num(meanDifference) + "\nMaxDisplayRGBDeltaTolerance=" + Num(2.0f / 255.0f) + "\nManualGamma=false for window image\nIncludesEditorWindowUI=true for full window PNG; false for central display PNGs\nDesktopRectangleReadback=false\nDesktopForegroundClaim=false\n");
                    Assert.LessOrEqual(maxDifference, 2.0f / 255.0f, "The UI-free owned-window display RGB must match once-sRGB Native AOV Output within two 8-bit quantization steps.");
                }
                finally
                {
                    RenderTexture.active = previousTarget;
                    if (image != null) Object.DestroyImmediate(image);
                    if (target != null)
                    {
                        target.Release();
                        Object.DestroyImmediate(target);
                    }
                    if (previousWindow != null && previousWindow != _view) previousWindow.Focus();
                    GL.sRGBWrite = previousSrgbWrite;
                }
            }

            private static void WriteWindowPng(string path, Color[] colors, int width, int height)
            {
                var image = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                try
                {
                    image.SetPixels(colors);
                    image.Apply();
                    File.WriteAllBytes(path, image.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(image);
                }
            }

            public IEnumerator Frame(string path, Action<Color[]> receive)
            {
                int version = _version;
                double limit = EditorApplication.timeSinceStartup + 15.0;
                while (_version < version + 3 && EditorApplication.timeSinceStartup < limit)
                {
                    _view.Repaint();
                    yield return null;
                }
                Assert.GreaterOrEqual(_version, version + 3, "Actual GUI Scene View repaints must produce three settled HDRP AOV callbacks without Camera.Render.");
                Assert.IsNotNull(_source);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = _source.rt;
                    _pixels.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
                    _pixels.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
                Color[] colors = _pixels.GetPixels();
                string statistics = XRayWeatherSceneFixture.ColorImageStatistics(colors, out bool readable);
                File.WriteAllText(path + "_ImageMetrics.txt", statistics);
                File.WriteAllBytes(path + ".exr", _pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                var png = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, false);
                try
                {
                    var display = (Color[])colors.Clone();
                    for (int i = 0; i < display.Length; i++) display[i] = display[i].gamma;
                    png.SetPixels(display);
                    png.Apply();
                    File.WriteAllBytes(path + ".png", png.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(png);
                }
                HDCamera hdCamera = HDCamera.GetOrCreate(_camera);
                FrameSettings actual = hdCamera.frameSettings;
                Exposure exposure = hdCamera.volumeStack.GetComponent<Exposure>();
                Bloom bloom = hdCamera.volumeStack.GetComponent<Bloom>();
                var overrideField = typeof(HDAdditionalCameraData).GetField("doesSceneViewOverrideExposure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var overrideValueField = typeof(HDAdditionalCameraData).GetField("sceneViewOverrideExposureValue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(exposure);
                Assert.IsNotNull(overrideField);
                Assert.IsNotNull(overrideValueField);
                bool sceneOverride = (bool)overrideField.GetValue(_data);
                float sceneOverrideValue = (float)overrideValueField.GetValue(_data);
                bool volumeRegistered = Array.IndexOf(VolumeManager.instance.GetVolumes(_data.volumeLayerMask), _volume) >= 0;
                bool volumeVisible = UnityEditor.SceneManagement.StageUtility.IsGameObjectRenderedByCamera(_volume.gameObject, _camera);
                string exposureMetadata = FormattableString.Invariant($"\nActualVolumeExposureMode={exposure.mode.value}\nActualVolumeFixedExposureEV100={exposure.fixedExposure.value:R}\nActualVolumeExposureCompensation={exposure.compensation.value:R}\nSceneViewExposureOverrideActive={sceneOverride}\nSceneViewExposureOverrideEV100={sceneOverrideValue:R}\nConfiguredVolumeLayerMask={_data.volumeLayerMask.value}\nFixtureVolumeRegisteredInMask={volumeRegistered}\nFixtureVolumeVisibleToCamera={volumeVisible}\nActualFrameBloom={actual.IsEnabled(FrameSettingsField.Bloom)}\nActualVolumeBloomIntensity={bloom.intensity.value:R}");
                File.WriteAllText(path + "_Camera.txt", "API=" + SystemInfo.graphicsDeviceType + "\nCameraType=" + _camera.cameraType + "\nSceneViewDrawMode=" + _view.cameraMode.drawMode + "\nPosition=" + _camera.transform.position.ToString("F8") + "\nRotation=" + _camera.transform.rotation.ToString("F8") + "\nOrthographicSize=" + Num(_camera.orthographicSize) + "\nPixelSize=" + _camera.pixelWidth + "x" + _camera.pixelHeight + "\nAovSize=" + SIZE + "x" + SIZE + "\nConfiguredFixedExposureEV100=" + XRayWeatherSceneFixture.FIXED_EXPOSURE_EV100 + "\nDiagnosticSunLux=" + XRayWeatherSceneFixture.DIAGNOSTIC_SUN_LUX + "\nExposureControl=" + actual.IsEnabled(FrameSettingsField.ExposureControl) + "\nPostprocess=" + actual.IsEnabled(FrameSettingsField.Postprocess) + "\nView=" + _camera.worldToCameraMatrix.ToString("F8") + "\nProjection=" + _camera.projectionMatrix.ToString("F8") + "\nAovCallbacks=" + _version + "\nCapture=Native SceneView GUI repaint and HDRP AOV Output after postprocess" + exposureMetadata);
                Assert.IsTrue(volumeRegistered && volumeVisible, "The task-owned native fixture Volume must be registered and visible to its Scene View camera.");
                Assert.AreEqual(_sceneCullingMask, _camera.overrideSceneCullingMask, "Native GUI repaints must preserve the task-owned preview scene culling mask.");
                Assert.IsFalse(sceneOverride, "A Scene View exposure override would invalidate the shared fixed-volume exposure contract.");
                Assert.AreEqual(ExposureMode.Fixed, exposure.mode.value);
                Assert.That(exposure.fixedExposure.value, Is.EqualTo(XRayWeatherSceneFixture.FIXED_EXPOSURE_EV100).Within(0.0001f));
                Assert.That(exposure.compensation.value, Is.EqualTo(0.0f).Within(0.000001f));
                Assert.IsTrue(actual.IsEnabled(FrameSettingsField.ExposureControl));
                Assert.IsFalse(actual.IsEnabled(FrameSettingsField.Bloom));
                if (_material.GetFloat("_MonolithDebug") < 0.5f) Assert.IsTrue(readable, "Native weather capture must contain nonflat visible foliage with fewer than 90% surface pixels at or above the 0.99 display-white threshold: " + statistics);
                receive(colors);
            }

            public void Dispose()
            {
                if (_data != null) _data.SetAOVRequests(null);
                _requests.Dispose();
                if (RenderTexture.active == _color.rt) RenderTexture.active = null;
                RTHandles.Release(_color);
                if (_rawColor != null) RTHandles.Release(_rawColor);
                Object.DestroyImmediate(_pixels);
                if (_view != null) _view.Close();
            }
        }

        private sealed class WeatherSceneView : SceneView
        {
            public void Bind(Scene scene)
            {
                customScene = scene;
                drawGizmos = false;
                showGrid = false;
                sceneLighting = true;
                sceneViewState.fxEnabled = true;
                sceneViewState.alwaysRefresh = true;
            }
        }
    }
}
