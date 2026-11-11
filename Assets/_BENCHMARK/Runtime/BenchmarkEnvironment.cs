using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rvm.Benchmark
{
    public static class BenchmarkEnvironment
    {
        public static BenchmarkMetadata Capture(BenchmarkScenario scenario, Camera camera, Transform[] positions, string seriesId, int repetition)
        {
            var result = new BenchmarkMetadata
            {
                runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N"),
                seriesId = seriesId,
                repetition = repetition,
                startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                scenario = scenario.ScenarioId,
                scene = camera.gameObject.scene.path,
                mode = scenario.Mode.ToString(),
                execution = Application.isEditor ? "Editor" : Debug.isDebugBuild ? "Development Player" : "Release Player",
                unity = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                os = SystemInfo.operatingSystem,
                cpu = SystemInfo.processorType,
                gpu = SystemInfo.graphicsDeviceName,
                ramMb = SystemInfo.systemMemorySize,
                gpuRamMb = SystemInfo.graphicsMemorySize,
                width = Screen.width,
                height = Screen.height,
                cameraPixelWidth = camera.scaledPixelWidth,
                cameraPixelHeight = camera.scaledPixelHeight,
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                vsync = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                fullscreen = Screen.fullScreenMode.ToString(),
                widthScale = ScalableBufferManager.widthScaleFactor,
                heightScale = ScalableBufferManager.heightScaleFactor,
                cameraSettings = CameraSignature(camera),
                renderPipeline = GraphicsSettings.currentRenderPipeline == null ? "unavailable" : GraphicsSettings.currentRenderPipeline.name,
                positionSeconds = scenario.PositionSeconds,
                warmupSeconds = scenario.WarmupSeconds,
                stabilizationSeconds = scenario.StabilizationSeconds,
                profilerCapture = scenario.ProfilerCapture,
                profilerWasEnabled = UnityEngine.Profiling.Profiler.enabled,
                tags = scenario.Tags,
                positions = new BenchmarkPose[4]
            };
            var keys = scenario.PositionKeys;
            for (int i = 0; i < 4; i++)
            {
                result.positions[i] = new BenchmarkPose { key = keys[i], position = positions[i].position, rotation = positions[i].rotation };
            }
            result.warnings.Add("20 seconds is a short sample and does not characterize rare long stalls. FPS lows need at least 100/1000 valid frames respectively.");
            result.warnings.Add("World simulation, weather, physics, random state and time are not frozen or reset. Warmup and repeated runs advance them naturally.");
            result.warnings.Add("Recorder values are process/frame totals, not camera-exclusive; Editor/SceneView work and other cameras can contribute.");
            result.warnings.Add("Graphics memory counters represent Unity accounting, not a complete hardware VRAM residency measurement. CPU and GPU times overlap and are not summed.");
            CollectWorld(camera, result.environment, result.warnings);
            return result;
        }

        public static string CameraSignature(Camera camera)
        {
            return string.Format(CultureInfo.InvariantCulture, "name={0};fov={1:R};near={2:R};far={3:R};ortho={4};size={5:R};aspect={6:R};rect={7};mask={8};HDR={9};MSAA={10};dynamic={11};physical={12};lens={13:R};sensor={14};shift={15};target={16}", camera.name, camera.fieldOfView, camera.nearClipPlane, camera.farClipPlane, camera.orthographic, camera.orthographicSize, camera.aspect, camera.rect, camera.cullingMask, camera.allowHDR, camera.allowMSAA, camera.allowDynamicResolution, camera.usePhysicalProperties, camera.focalLength, camera.sensorSize, camera.lensShift, camera.targetTexture == null ? "screen" : camera.targetTexture.name);
        }

        public static void CollectWorld(Camera camera, List<BenchmarkEntry> entries, List<string> warnings)
        {
            int weatherCount = 0;
            foreach (var root in camera.gameObject.scene.GetRootGameObjects())
            {
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null)
                    {
                        continue;
                    }
                    if (component is IBenchmarkMetadataProvider provider)
                    {
                        try
                        {
                            provider.CollectBenchmarkMetadata(entries);
                        }
                        catch (Exception exception)
                        {
                            warnings.Add("Metadata provider failed: " + component.GetType().FullName + ": " + exception.Message);
                        }
                    }
                    if (component.GetType().FullName != "XRay.Importer.Levels.XRayWeatherController")
                    {
                        continue;
                    }
                    string prefix = "weather." + weatherCount++ + ".";
                    Add(entries, prefix + "enabled", component.isActiveAndEnabled.ToString());
                    foreach (string propertyName in new[] { "GameTimeSeconds", "Paused", "TimeScale", "ConfiguredTimeOfDay", "ActiveProfile", "ActiveFx", "FxPhase", "Diagnostic" })
                    {
                        try
                        {
                            PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                            object value = property == null ? null : property.GetValue(component);
                            string text = value is UnityEngine.Object asset ? asset == null ? "none" : asset.name : Convert.ToString(value, CultureInfo.InvariantCulture);
                            Add(entries, prefix + propertyName, property == null ? "unavailable" : text ?? "none");
                        }
                        catch (Exception exception)
                        {
                            Add(entries, prefix + propertyName, "unavailable: " + exception.GetType().Name);
                        }
                    }
                }
            }
            if (weatherCount == 0)
            {
                Add(entries, "weather", "unavailable: no known public weather provider in the benchmark scene");
            }
            Add(entries, "anomalies", "unavailable: no validated state provider; attach IBenchmarkMetadataProvider for explicit conditions");
            Add(entries, "weatherSampling", "Start/end snapshots only; intervening weather evolution is not captured or controlled");
            foreach (var component in camera.GetComponents<Component>())
            {
                if (component != null && component.GetType().FullName == "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData")
                {
                    Add(entries, "camera.hdrp", JsonUtility.ToJson(component));
                }
            }
        }

        public static void Add(List<BenchmarkEntry> entries, string key, string value)
        {
            entries.Add(new BenchmarkEntry { key = key, value = value });
        }
    }
}
