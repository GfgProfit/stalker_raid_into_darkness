using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayWeatherCelestialMilitaryCheck
    {
        private const string PRODUCTION_ASSETS = "E:/Projects/stalker_raid_into_darkness/Assets";
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";
        private const int WIDTH = 640;
        private const int HEIGHT = 360;
        private const int WARMUP = 8;
        private const float NATIVE_BEAM_CONTRAST = 0.015f;

        private static readonly Vector3 _spruceCrown = new Vector3(-28.55f, 6.2f, 340.37f);
        private static readonly Vector3 _spruceCrownExtents = new Vector3(5.5f, 7.1f, 5.7f);
        private static readonly Vector3 _militaryGrate = new Vector3(-424.846f, -9.248f, 372.880f);
        private static readonly Vector3 _militaryGrateExtents = new Vector3(1.5f, 1.5f, 1.5f);

        public static string Run(string archive)
        {
            return RunInternal(archive, false);
        }

        public static string RunVisualComparison(string archive)
        {
            return RunInternal(archive, true);
        }

        public static string RunNativeBeams(string archive)
        {
            return RunInternal(archive, false, true);
        }

        private static string RunInternal(string archive, bool compareVisuals, bool compareNative = false)
        {
            if (string.IsNullOrWhiteSpace(archive) || !Path.GetFullPath(archive).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(archive)) throw new ArgumentException("Select the existing celestial task Archive folder.", nameof(archive));
            if (!string.Equals(Application.dataPath, PRODUCTION_ASSETS, StringComparison.OrdinalIgnoreCase) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("Military celestial validation requires the idle Production Editor.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.isLoaded || !scene.path.EndsWith("/l07_military/Scenes/l07_military.unity", StringComparison.Ordinal)) throw new InvalidOperationException("Keep the existing military scene active.");
            for (int index = 0; index < SceneManager.sceneCount; index++) if (SceneManager.GetSceneAt(index).isDirty) throw new InvalidOperationException("Preserve the unsaved scene before military validation: " + SceneManager.GetSceneAt(index).path);
            string output = Path.Combine(archive, "MilitaryCelestial_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            string before = XRayWeatherCelestialValidation.InspectHost();
            File.WriteAllText(Path.Combine(output, "HostBefore.json"), before);
            var result = new Result { scene = scene.path, api = SystemInfo.graphicsDeviceType.ToString(), width = WIDTH, height = HEIGHT, warmup = WARMUP };
            var cases = new List<Case>();
            XRayWeatherController controller = Find<XRayWeatherController>(scene);
            if (controller.PreviewActive || controller.IsTransitioning || XRayWeatherPreview.Controller != null || XRayWeatherController.Active != null)
            {
                result.status = "NOT_RUN";
                result.reason = "An existing preview, transition or active owner must be preserved.";
                result.restorationPassed = before == XRayWeatherCelestialValidation.InspectHost();
                Write(output, "Result.json", result);
                return output;
            }

            Camera authoredCamera = Find<Camera>(scene, "Level Camera");
            HDAdditionalCameraData authoredData = authoredCamera.GetComponent<HDAdditionalCameraData>();
            var serialized = new SerializedObject(controller);
            XRayWeatherProfile originalProfile = controller.ActiveProfile;
            double originalSeconds = controller.GameTimeSeconds;
            bool originalPaused = controller.Paused;
            float originalScale = controller.TimeScale;
            bool? originalShafts = controller.SunShaftsOverride;
            bool? originalFlare = controller.LensFlareOverride;
            var fog = serialized.FindProperty("_fogRenderer").objectReferenceValue as XRayWeatherFogRenderer;
            var sky = serialized.FindProperty("_skyRenderer").objectReferenceValue as XRayWeatherSkyRenderer;
            var sun = serialized.FindProperty("_sun").objectReferenceValue as Light;
            var skyVolume = sky == null ? null : new SerializedObject(sky).FindProperty("_volume").objectReferenceValue as Volume;
            var authoredProfile = serialized.FindProperty("_profile").objectReferenceValue as XRayWeatherProfile;
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(authoredProfile))?.Replace('\\', '/');
            XRayWeatherProfile clear = AssetDatabase.LoadAssetAtPath<XRayWeatherProfile>(folder + "/w_clear1.asset");
            if (authoredData == null || authoredData.volumeLayerMask.value == 0 || fog == null || sky == null || sun == null || skyVolume == null || authoredProfile == null || clear == null || clear.SourceIdentity != authoredProfile.SourceIdentity) throw new InvalidOperationException("Military authored camera, weather references and same-source clear profile are required.");
            var assignedSettings = serialized.FindProperty("_celestialSettings").objectReferenceValue as XRayWeatherCelestialSettings;
            XRayWeatherCelestialSettings settings = assignedSettings != null ? assignedSettings : Resources.Load<XRayWeatherCelestialSettings>(XRayWeatherCelestialSettings.RESOURCE_PATH);
            if (settings == null || settings.Shader == null || !settings.Shader.isSupported) throw new InvalidOperationException("Import the celestial library before checking the existing scene.");
            HDAdditionalLightData sunData = sun.GetComponent<HDAdditionalLightData>();
            if (compareNative && sunData == null) throw new InvalidOperationException("Military native beams require HDRP directional light data.");
            result.originalVolumeMask = authoredData.volumeLayerMask.value;
            result.authoredCamera = GlobalObjectId.GetGlobalObjectIdSlow(authoredCamera).ToString();
            string authoredCameraState = CameraSignature(authoredCamera, authoredData);
            string authoredFogState = ProfileSignature(fog.Volume);
            string authoredSkyState = ProfileSignature(skyVolume);
            string authoredSunState = SunSignature(sun, sunData);
            GameObject temporary = null;
            try
            {
                if (!XRayWeatherPreview.Start(controller)) throw new InvalidOperationException("Military preview could not acquire its original providers: " + controller.Diagnostic);
                controller.SetPaused(true);
                controller.SetTimeScale(0.0f);
                controller.SetProfile(clear);
                temporary = new GameObject("Military Celestial Validation Camera");
                temporary.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(temporary, scene);
                Camera camera = temporary.AddComponent<Camera>();
                camera.CopyFrom(authoredCamera);
                camera.scene = scene;
                camera.targetTexture = null;
                camera.enabled = false;
                camera.orthographic = false;
                camera.aspect = (float)WIDTH / HEIGHT;
                HDAdditionalCameraData data = temporary.GetComponent<HDAdditionalCameraData>();
                if (data == null) data = temporary.AddComponent<HDAdditionalCameraData>();
                authoredData.CopyTo(data);
                if (controller.Celestial == null) throw new InvalidOperationException("The existing scene did not resolve its celestial library.");
                result.automaticResourceBinding = assignedSettings == null;
                if (compareNative)
                {
                    result.nativeCases = new[]
                    {
                        CompareNativeBeams(controller, camera, sun, sunData, output, "Sun", 28800.0),
                        CompareNativeBeams(controller, camera, sun, sunData, output, "Moon", 0.0)
                    };
                    result.behaviorPassed = result.nativeCases[0].passed && result.nativeCases[1].passed;
                }
                else if (compareVisuals)
                {
                    result.visualCases = new[]
                    {
                        CompareVisuals(controller, camera, authoredCamera, output, "Noon", 43200.0, true),
                        CompareVisuals(controller, camera, authoredCamera, output, "Morning", 28800.0, true),
                        CompareVisuals(controller, camera, authoredCamera, output, "Sunset", 72000.0, true),
                        CompareVisuals(controller, camera, authoredCamera, output, "LevelView", 43200.0, false),
                        CompareVisuals(controller, camera, authoredCamera, output, "Midnight", 0.0, true)
                    };
                    result.behaviorPassed = result.visualCases[0].passed && result.visualCases[1].passed && result.visualCases[4].passed;
                }
                else
                {
                    cases.Add(CaptureCase(controller, camera, data, authoredCamera, fog, sky, sun, output, "ClearNoon", 43200.0, result.originalVolumeMask));
                    cases.Add(CaptureCase(controller, camera, data, authoredCamera, fog, sky, sun, output, "ClearMidnight", 0.0, result.originalVolumeMask));
                    result.behaviorPassed = cases.TrueForAll(item => item.passed);
                }
                result.captureWritten = true;
                if (!result.behaviorPassed) result.reason = "At least one fixed camera case lacks stack, dispatch, visible signal, or complete disable evidence.";
            }
            catch (Exception exception)
            {
                result.reason = exception.Message;
                File.WriteAllText(Path.Combine(output, "Failure.txt"), exception.ToString());
            }
            finally
            {
                try
                {
                    if (XRayWeatherPreview.Controller == controller) XRayWeatherPreview.Stop();
                    else if (controller.PreviewActive) controller.EndPreview();
                    if (temporary != null) Object.DestroyImmediate(temporary);
                    controller.SetProfile(originalProfile);
                    controller.Seek(originalSeconds);
                    controller.SetPaused(originalPaused);
                    controller.SetTimeScale(originalScale);
                    controller.SetCelestialEffectOverrides(originalShafts, originalFlare);
                    string after = XRayWeatherCelestialValidation.InspectHost();
                    File.WriteAllText(Path.Combine(output, "HostAfter.json"), after);
                    result.restorationPassed = before == after && authoredCameraState == CameraSignature(authoredCamera, authoredData) && authoredFogState == ProfileSignature(fog.Volume) && authoredSkyState == ProfileSignature(skyVolume) && authoredSunState == SunSignature(sun, sunData) && controller.Celestial == null && XRayWeatherController.Active == null;
                }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(output, "RestorationFailure.txt"), exception.ToString());
                }
                result.cases = cases.ToArray();
                result.status = result.captureWritten && result.behaviorPassed && result.restorationPassed ? "PASS" : "FAIL";
                if (!result.restorationPassed && string.IsNullOrEmpty(result.reason)) result.reason = "The original military state differs from its same-lifetime snapshot.";
                Write(output, "Result.json", result);
            }
            return output;
        }

        private static NativeCase CompareNativeBeams(XRayWeatherController controller, Camera camera, Light sun, HDAdditionalLightData sunData, string output, string label, double seconds)
        {
            controller.Seek(seconds);
            controller.SetCelestialEffectOverrides(true, false);
            Vector3 incoming = controller.Snapshot.SunDirection.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, incoming).normalized;
            Vector3 shadowCenter = _spruceCrown + incoming * 12.0f;
            bool moon = controller.Snapshot.SunDefinition?.StartsWith("moon", StringComparison.OrdinalIgnoreCase) == true;
            var record = new NativeCase { label = label, seconds = seconds, definition = controller.Snapshot.SunDefinition, lightDirection = incoming, surfaceIntensity = sun.intensity, volumetricDimmer = sunData.volumetricDimmer, volumetricShadowDimmer = sunData.volumetricShadowDimmer, poses = new NativePose[moon ? 8 : 6] };
            var crown = new Bounds(_spruceCrown, _spruceCrownExtents * 2.0f);
            record.poses[0] = CaptureNativePose(controller, camera, sunData, output, label + "_SpruceLeft", shadowCenter + incoming * 7.0f - side * 11.0f - Vector3.up * 7.0f, shadowCenter, shadowCenter, incoming, crown);
            record.poses[1] = CaptureNativePose(controller, camera, sunData, output, label + "_SpruceRight", shadowCenter + incoming * 7.0f + side * 12.0f - Vector3.up * 7.0f, shadowCenter, shadowCenter, incoming, crown);
            record.poses[2] = CaptureNativePose(controller, camera, sunData, output, label + "_SpruceWide", shadowCenter + incoming * 15.0f + side * 10.0f - Vector3.up * 5.0f, shadowCenter, shadowCenter, incoming, crown);
            Vector3 lowerShadow = _spruceCrown + incoming * 22.0f;
            record.poses[3] = CaptureNativePose(controller, camera, sunData, output, label + "_SpruceLow", lowerShadow + incoming * 9.0f + side * 11.0f + Vector3.up * 4.0f, lowerShadow - Vector3.up * 4.0f, lowerShadow, incoming, crown);
            Vector3 closeShadow = _spruceCrown + incoming * 17.0f;
            record.poses[4] = CaptureNativePose(controller, camera, sunData, output, label + "_SpruceClose", closeShadow + incoming * 6.0f - side * 7.0f + Vector3.up * 2.5f, closeShadow - Vector3.up * 3.2f, closeShadow, incoming, crown);
            Vector3 grateShadow = _militaryGrate + incoming * 3.0f;
            var grate = new Bounds(_militaryGrate, _militaryGrateExtents * 2.0f);
            record.poses[5] = CaptureNativePose(controller, camera, sunData, output, label + "_Grate", grateShadow + incoming * 5.0f + side * 4.0f + Vector3.up, grateShadow, grateShadow, incoming, grate);
            if (moon)
            {
                float fieldOfView = camera.fieldOfView;
                try
                {
                    camera.fieldOfView = 48.0f;
                    Vector3 forwardShadow = _spruceCrown + incoming * 8.0f;
                    record.poses[6] = CaptureNativePose(controller, camera, sunData, output, label + "_SpruceForward", forwardShadow + incoming * 24.0f + side * 3.0f, forwardShadow - Vector3.up * 3.5f, forwardShadow, incoming, crown);
                    Vector3 forwardSideShadow = _spruceCrown + incoming * 10.0f;
                    record.poses[7] = CaptureNativePose(controller, camera, sunData, output, label + "_SpruceForwardSide", forwardSideShadow + incoming * 22.0f - side * 6.0f + Vector3.up * 2.0f, forwardSideShadow - Vector3.up * 2.5f, forwardSideShadow, incoming, crown);
                }
                finally
                {
                    camera.fieldOfView = fieldOfView;
                }
            }
            foreach (NativePose pose in record.poses) record.beamVisible |= pose.passed;
            controller.SetCelestialEffectOverrides(false, false);
            XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, label + "_ShaftsDisabled"), WIDTH, HEIGHT, WARMUP);
            Fog off = HDCamera.GetOrCreate(camera).volumeStack.GetComponent<Fog>();
            record.disabled = off.enabled.value && !off.enableVolumetricFog.value && controller.Celestial.ShaftsIntensity == 0.0f;
            camera.transform.SetPositionAndRotation(record.poses[2].position, Quaternion.LookRotation(-incoming, Vector3.up));
            controller.SetCelestialEffectOverrides(false, true);
            XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, label + "_LensFlareOnly"), WIDTH, HEIGHT, WARMUP);
            XRayWeatherCelestialPass pass = Pass(controller);
            record.lensFlareDraws = pass == null || pass.LastCamera != camera ? 0 : pass.LastFlareDraws;
            record.passed = record.beamVisible && record.disabled && record.lensFlareDraws > 0;
            Write(output, label + "_Native.json", record);
            return record;
        }

        private static NativePose CaptureNativePose(XRayWeatherController controller, Camera camera, HDAdditionalLightData sunData, string output, string label, Vector3 position, Vector3 target, Vector3 shadowCenter, Vector3 incoming, Bounds caster)
        {
            if (Physics.Raycast(new Vector3(position.x, 100.0f, position.z), Vector3.down, out RaycastHit surface, 500.0f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) position.y = Mathf.Max(position.y, surface.point.y + 1.4f);
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            float shadow = sunData.volumetricShadowDimmer;
            Color[] a = XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, label + "_BeamA"), WIDTH, HEIGHT, WARMUP);
            Color[] repeat = XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, label + "_BeamB"), WIDTH, HEIGHT, WARMUP);
            Color[] noShadow;
            try
            {
                sunData.volumetricShadowDimmer = 0.0f;
                noShadow = XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, label + "_NoVolumeShadows"), WIDTH, HEIGHT, WARMUP);
            }
            finally
            {
                sunData.volumetricShadowDimmer = shadow;
            }
            HDCamera hdCamera = HDCamera.GetOrCreate(camera);
            Fog fog = hdCamera.volumeStack.GetComponent<Fog>();
            XRayWeatherCelestialPass pass = Pass(controller);
            Vector3 beamStart = camera.WorldToViewportPoint(shadowCenter - incoming * 4.0f);
            Vector3 beamEnd = camera.WorldToViewportPoint(shadowCenter + incoming * 4.0f);
            Vector2 beamAxis = new Vector2((beamEnd.x - beamStart.x) * WIDTH, (beamEnd.y - beamStart.y) * HEIGHT).normalized;
            var record = new NativePose { label = label, position = position, rotation = camera.transform.rotation, sourceForward = Vector3.Dot(camera.transform.forward, -incoming), nativeFog = fog.enabled.value && fog.enableVolumetricFog.value && hdCamera.frameSettings.IsEnabled(FrameSettingsField.Volumetrics), shadowsEnabled = hdCamera.frameSettings.IsEnabled(FrameSettingsField.ShadowMaps), screenShafts = pass != null && pass.LastCamera == camera && pass.LastShaftsDrawn, volumetricDimmer = sunData.volumetricDimmer, depthExtent = fog.depthExtent.value, anisotropy = fog.anisotropy.value };
            MeasureNativeBeams(output, label, a, repeat, noShadow, beamAxis, ProjectBounds(camera, caster), record);
            record.passed = record.nativeFog && record.shadowsEnabled && !record.screenShafts && shadow > 0.0f && record.contrastOutsideCaster >= NATIVE_BEAM_CONTRAST && record.contrastOutsideCaster > record.repeatContrast * 5.0f && record.affectedFraction > 0.02f;
            return record;
        }

        private static Rect ProjectBounds(Camera camera, Bounds bounds)
        {
            var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 position = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1.0f : 1.0f, (corner & 2) == 0 ? -1.0f : 1.0f, (corner & 4) == 0 ? -1.0f : 1.0f));
                Vector3 viewport = camera.WorldToViewportPoint(position);
                if (viewport.z <= 0.0f) continue;
                minimum = Vector2.Min(minimum, viewport);
                maximum = Vector2.Max(maximum, viewport);
            }
            return maximum.x >= minimum.x ? Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y) : new Rect();
        }

        private static void MeasureNativeBeams(string output, string label, Color[] a, Color[] repeat, Color[] noShadow, Vector2 beamAxis, Rect caster, NativePose record)
        {
            var delta = new float[a.Length];
            var image = new Color[a.Length];
            for (int index = 0; index < a.Length; index++)
            {
                delta[index] = Mathf.Max(0.0f, DisplayLuma(noShadow[index]) - DisplayLuma(repeat[index]));
                float value = Mathf.Clamp01(delta[index] * 4.0f);
                image[index] = new Color(value, value, value, 1.0f);
            }
            var beamValues = new List<float>();
            var repeatValues = new List<float>();
            var airValues = new List<float>();
            Vector2 cross = new Vector2(-beamAxis.y, beamAxis.x) * 6.0f;
            Vector2 along = beamAxis * 6.0f;
            float crossChange = 0.0f;
            float alongChange = 0.0f;
            int affected = 0;
            for (int y = HEIGHT / 8; y < HEIGHT * 7 / 8; y += 2)
            {
                for (int x = WIDTH / 8; x < WIDTH * 7 / 8; x += 2)
                {
                    int index = y * WIDTH + x;
                    float value = delta[index];
                    beamValues.Add(value);
                    repeatValues.Add(Mathf.Abs(DisplayLuma(repeat[index]) - DisplayLuma(a[index])));
                    if (!caster.Contains(new Vector2((float)x / WIDTH, (float)y / HEIGHT))) airValues.Add(value);
                    if (value >= NATIVE_BEAM_CONTRAST) affected++;
                    crossChange += Mathf.Abs(value - delta[(y + Mathf.RoundToInt(cross.y)) * WIDTH + x + Mathf.RoundToInt(cross.x)]);
                    alongChange += Mathf.Abs(value - delta[(y + Mathf.RoundToInt(along.y)) * WIDTH + x + Mathf.RoundToInt(along.x)]);
                }
            }
            beamValues.Sort();
            repeatValues.Sort();
            airValues.Sort();
            record.contrast = beamValues[beamValues.Count * 95 / 100] - beamValues[beamValues.Count * 20 / 100];
            record.repeatContrast = repeatValues[repeatValues.Count * 95 / 100];
            record.affectedFraction = (float)affected / beamValues.Count;
            record.crossToAlong = crossChange / Mathf.Max(alongChange, 0.000001f);
            record.contrastOutsideCaster = airValues.Count == 0 ? 0.0f : airValues[airValues.Count * 95 / 100] - airValues[airValues.Count * 20 / 100];
            var texture = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, false, false);
            try
            {
                texture.SetPixels(image);
                texture.Apply(false, false);
                File.WriteAllBytes(Path.Combine(output, label + "_ShadowDifference_x4.png"), texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static float DisplayLuma(Color color)
        {
            Color gamma = color.gamma;
            return 0.2126f * Mathf.Clamp01(gamma.r) + 0.7152f * Mathf.Clamp01(gamma.g) + 0.0722f * Mathf.Clamp01(gamma.b);
        }

        private static VisualCase CompareVisuals(XRayWeatherController controller, Camera camera, Camera authored, string output, string label, double seconds, bool aim)
        {
            controller.Seek(seconds);
            XRayWeatherSnapshot snapshot = controller.Snapshot;
            Vector3 direction = -snapshot.SunDirection.normalized;
            camera.transform.SetPositionAndRotation(authored.transform.position, authored.transform.rotation);
            if (aim)
            {
                camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                Vector3 ray = camera.ViewportPointToRay(new Vector3(0.22f, 0.76f)).direction;
                camera.transform.rotation = Quaternion.FromToRotation(ray, direction) * camera.transform.rotation;
            }
            Color[] off = VisualFrame(controller, camera, output, label + "_Off", false, false, false);
            Color[] offRepeat = VisualFrame(controller, camera, output, label + "_OffRepeat", false, false, false);
            Color[] sourceShafts = VisualFrame(controller, camera, output, label + "_SourceShafts", true, false, false);
            Color[] expressiveShafts = VisualFrame(controller, camera, output, label + "_ExpressiveShafts", true, false, true);
            Color[] sourceFlare = VisualFrame(controller, camera, output, label + "_SourceFlare", false, true, false);
            int sourceDraws = Pass(controller)?.LastFlareDraws ?? 0;
            Color[] expressiveFlare = VisualFrame(controller, camera, output, label + "_ExpressiveFlare", false, true, true);
            int expressiveDraws = Pass(controller)?.LastFlareDraws ?? 0;
            VisualFrame(controller, camera, output, label + "_SourceBoth", true, true, false);
            VisualFrame(controller, camera, output, label + "_ExpressiveBoth", true, true, true);
            HDCamera hdCamera = HDCamera.GetOrCreate(camera);
            Fog effectiveFog = hdCamera.volumeStack.GetComponent<Fog>();
            VisualEnvironment environment = hdCamera.volumeStack.GetComponent<VisualEnvironment>();
            Exposure exposure = hdCamera.volumeStack.GetComponent<Exposure>();
            bool moon = snapshot.SunDefinition?.StartsWith("moon", StringComparison.OrdinalIgnoreCase) == true;
            var record = new VisualCase { label = label, seconds = seconds, definition = snapshot.SunDefinition, sourceUv = Pass(controller)?.LastSourceUv ?? Vector2.zero, sourceForward = Vector3.Dot(camera.transform.forward, direction), rawIntensity = snapshot.SunShaftsIntensity, effectiveIntensity = controller.Celestial.ShaftsIntensity, noise = MeanDelta(off, offRepeat), sourceShafts = MeanDelta(off, sourceShafts), expressiveShafts = MeanDelta(off, expressiveShafts), sourceFlare = MeanDelta(off, sourceFlare), expressiveFlare = MeanDelta(off, expressiveFlare), flareVariantDelta = MeanDelta(sourceFlare, expressiveFlare), shaftVariantDelta = MeanDelta(sourceShafts, expressiveShafts), sourceFlareDraws = sourceDraws, expressiveFlareDraws = expressiveDraws, cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation, volumeMask = camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value, exposure = exposure.fixedExposure.value, fogAndSky = effectiveFog.enabled.value && effectiveFog.colorMode.value == FogColorMode.SkyColor && environment.skyType.value == XRayWeatherSkySettings.SKY_ID, defaultExpressive = controller.ExpressiveDaylight };
            double tolerance = Math.Max(0.000001, record.noise * 3.0);
            record.passed = record.fogAndSky && record.defaultExpressive && (moon ? record.flareVariantDelta < tolerance && record.shaftVariantDelta < tolerance && sourceDraws == 1 && expressiveDraws == 1 : record.expressiveShafts > record.sourceShafts * 1.25 && record.flareVariantDelta > tolerance && expressiveDraws > sourceDraws);
            Write(output, label + "_Visual.json", record);
            return record;
        }

        private static Color[] VisualFrame(XRayWeatherController controller, Camera camera, string output, string label, bool shafts, bool flare, bool expressive)
        {
            controller.Celestial.Publish(controller.Snapshot, shafts, flare, 0.0f, true, expressive);
            return XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, label), WIDTH, HEIGHT, WARMUP);
        }

        private static Case CaptureCase(XRayWeatherController controller, Camera camera, HDAdditionalCameraData data, Camera authoredCamera, XRayWeatherFogRenderer fog, XRayWeatherSkyRenderer sky, Light sun, string output, string label, double seconds, int mask)
        {
            controller.Seek(seconds);
            Vector3 direction = -controller.Snapshot.SunDirection.normalized;
            if (direction.sqrMagnitude < 0.999f || !float.IsFinite(direction.sqrMagnitude)) throw new InvalidOperationException("Military weather source direction is invalid for " + label);
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            camera.transform.SetPositionAndRotation(authoredCamera.transform.position, Quaternion.LookRotation(direction, up));
            controller.SetCelestialEffectOverrides(false, false);
            Color[] offA = XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, label + "_OffA"), WIDTH, HEIGHT, WARMUP);
            Color[] offB = XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, label + "_OffB"), WIDTH, HEIGHT, WARMUP);
            XRayWeatherCelestialPass offPass = Pass(controller);
            bool off = controller.Celestial.ShaftsIntensity == 0.0f && (offPass == null || !offPass.enabled);
            int drawsBefore = offPass == null ? 0 : offPass.RenderCount;
            controller.SetCelestialEffectOverrides(true, true);
            Color[] on = XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, label + "_On"), WIDTH, HEIGHT, WARMUP);
            XRayWeatherCelestialPass pass = Pass(controller);
            HDCamera hdCamera = HDCamera.GetOrCreate(camera);
            Fog effectiveFog = hdCamera.volumeStack.GetComponent<Fog>();
            VisualEnvironment environment = hdCamera.volumeStack.GetComponent<VisualEnvironment>();
            Exposure exposure = hdCamera.volumeStack.GetComponent<Exposure>();
            var record = new Case { label = label, time = seconds, definition = controller.Snapshot.SunDefinition, sourceColor = controller.Snapshot.SunColor, rawIntensity = controller.Snapshot.SunShaftsIntensity, effectiveIntensity = controller.Celestial.ShaftsIntensity, cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation, exposureMode = exposure.mode.value.ToString(), fixedExposure = exposure.fixedExposure.value, antialiasing = data.antialiasing.ToString(), off = off, fog = fog.HasWeather && effectiveFog.enabled.value && effectiveFog.colorMode.value == FogColorMode.SkyColor, sky = sky.HasWeather && environment.skyType.value == SkySettings.GetUniqueID<XRayWeatherSkySettings>(), owner = controller.PreviewActive && XRayWeatherController.Active == controller && XRayWeatherPreview.Controller == controller, volumeLayer = controller.Celestial.Volume == null ? -1 : controller.Celestial.Volume.gameObject.layer, mask = data.volumeLayerMask.value, baselineNoise = MeanDelta(offA, offB), effectDelta = MeanDelta(offB, on) };
            record.maskUnchanged = record.mask == mask && record.volumeLayer >= 0 && (mask & (1 << record.volumeLayer)) != 0;
            record.passInvoked = pass != null && pass.LastCamera == camera && pass.RenderCount > drawsBefore;
            record.shaftsDrawn = record.passInvoked && pass.LastShaftsDrawn;
            record.flareDraws = record.passInvoked ? pass.LastFlareDraws : 0;
            record.sourceUv = record.passInvoked ? pass.LastSourceUv : Vector2.zero;
            record.sunIntensity = sun.intensity;
            record.sunColor = sun.color;
            record.sunAligned = sun.isActiveAndEnabled && sun.type == LightType.Directional && float.IsFinite(sun.intensity) && Vector3.Dot(sun.transform.forward, -direction) > 0.9999f;
            record.passed = record.off && record.fog && record.sky && record.owner && record.maskUnchanged && record.shaftsDrawn && record.flareDraws > 0 && record.sunAligned && record.effectDelta > record.baselineNoise * 2.0 + 0.000001;
            Write(output, label + "_State.json", record);
            return record;
        }

        private static XRayWeatherCelestialPass Pass(XRayWeatherController controller)
        {
            CustomPassVolume volume = controller.Celestial?.Volume;
            return volume != null && volume.customPasses.Count == 1 ? volume.customPasses[0] as XRayWeatherCelestialPass : null;
        }

        private static T Find<T>(Scene scene, string name = null) where T : Component
        {
            T result = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T candidate in root.GetComponentsInChildren<T>(true))
                {
                    if (name != null && candidate.name != name) continue;
                    if (result != null) throw new InvalidOperationException("Military contains more than one required " + typeof(T).Name + ".");
                    result = candidate;
                }
            }
            if (result == null) throw new InvalidOperationException("Military does not contain the required " + typeof(T).Name + ".");
            return result;
        }

        private static string CameraSignature(Camera camera, HDAdditionalCameraData data)
        {
            return EditorJsonUtility.ToJson(camera) + EditorJsonUtility.ToJson(data) + EditorJsonUtility.ToJson(camera.transform);
        }

        private static string SunSignature(Light sun, HDAdditionalLightData data)
        {
            return EditorJsonUtility.ToJson(sun) + EditorJsonUtility.ToJson(sun.transform) + (data == null ? string.Empty : EditorJsonUtility.ToJson(data));
        }

        private static string ProfileSignature(Volume volume)
        {
            if (volume == null) return "null";
            var parts = new List<string> { EditorJsonUtility.ToJson(volume) };
            foreach (VolumeProfile profile in new[] { volume.sharedProfile, volume.HasInstantiatedProfile() ? volume.profile : null })
            {
                parts.Add(profile == null ? "null" : profile.GetEntityId().ToString() + EditorJsonUtility.ToJson(profile));
                if (profile != null) foreach (VolumeComponent component in profile.components) parts.Add(component == null ? "null" : EditorJsonUtility.ToJson(component));
            }
            return string.Join("\n", parts);
        }

        private static double MeanDelta(Color[] a, Color[] b)
        {
            double sum = 0.0;
            for (int index = 0; index < a.Length; index++) sum += Math.Abs((double)a[index].r - b[index].r) + Math.Abs((double)a[index].g - b[index].g) + Math.Abs((double)a[index].b - b[index].b);
            return sum / (a.Length * 3.0);
        }

        private static void Write(string output, string name, object record)
        {
            File.WriteAllText(Path.Combine(output, name), JsonUtility.ToJson(record, true));
        }

        [Serializable]
        private sealed class Result
        {
            public string status;
            public string reason;
            public string scene;
            public string api;
            public string authoredCamera;
            public int originalVolumeMask;
            public int width;
            public int height;
            public int warmup;
            public bool automaticResourceBinding;
            public bool captureWritten;
            public bool behaviorPassed;
            public bool restorationPassed;
            public Case[] cases;
            public VisualCase[] visualCases;
            public NativeCase[] nativeCases;
        }

        [Serializable]
        private sealed class NativeCase
        {
            public string label;
            public double seconds;
            public string definition;
            public Vector3 lightDirection;
            public float surfaceIntensity;
            public float volumetricDimmer;
            public float volumetricShadowDimmer;
            public NativePose[] poses;
            public bool beamVisible;
            public bool disabled;
            public int lensFlareDraws;
            public bool passed;
        }

        [Serializable]
        private sealed class NativePose
        {
            public string label;
            public Vector3 position;
            public Quaternion rotation;
            public float sourceForward;
            public bool nativeFog;
            public bool shadowsEnabled;
            public bool screenShafts;
            public float volumetricDimmer;
            public float depthExtent;
            public float anisotropy;
            public float contrast;
            public float repeatContrast;
            public float affectedFraction;
            public float crossToAlong;
            public float contrastOutsideCaster;
            public bool passed;
        }

        [Serializable]
        private sealed class VisualCase
        {
            public string label;
            public double seconds;
            public string definition;
            public Vector2 sourceUv;
            public float sourceForward;
            public float rawIntensity;
            public float effectiveIntensity;
            public double noise;
            public double sourceShafts;
            public double expressiveShafts;
            public double sourceFlare;
            public double expressiveFlare;
            public double flareVariantDelta;
            public double shaftVariantDelta;
            public int sourceFlareDraws;
            public int expressiveFlareDraws;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public int volumeMask;
            public float exposure;
            public bool fogAndSky;
            public bool defaultExpressive;
            public bool passed;
        }

        [Serializable]
        private sealed class Case
        {
            public string label;
            public string definition;
            public string exposureMode;
            public string antialiasing;
            public double time;
            public Color sourceColor;
            public Color sunColor;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public Vector2 sourceUv;
            public float fixedExposure;
            public float rawIntensity;
            public float effectiveIntensity;
            public float sunIntensity;
            public int volumeLayer;
            public int mask;
            public int flareDraws;
            public double baselineNoise;
            public double effectDelta;
            public bool off;
            public bool fog;
            public bool sky;
            public bool owner;
            public bool maskUnchanged;
            public bool passInvoked;
            public bool shaftsDrawn;
            public bool sunAligned;
            public bool passed;
        }
    }
}
