using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace XRay.Importer.Levels
{
    public static class XRayPrecipitationPerformanceProbe
    {
        public const int WIDTH = 1920;
        public const int HEIGHT = 1080;

        private const string ARCHIVE = "E:\\Projects\\stalker_raid_into_darkness_Archive";
        private const string WETNESS_TICK_MARKER = "XRay.Precipitation.WetnessTick";

        private static readonly string[] SCENARIO_NAMES = { "baseline", "moderate_rain", "strong_rain", "rain_wetness", "rain_puddles", "full" };

        public sealed class Scenario
        {
            public string Name { get; }
            public Action Setup { get; }
            public Action<float> Tick { get; }
            public Func<long> EstimatedBytes { get; }
            public string TickScope { get; }
            public string Evidence { get; }
            public string UnavailableReason { get; }

            public Scenario(string name, Action setup, Action<float> tick = null, Func<long> estimatedBytes = null, string tickScope = null, string evidence = null, string unavailableReason = null)
            {
                Name = name;
                Setup = setup;
                Tick = tick;
                EstimatedBytes = estimatedBytes;
                TickScope = tickScope;
                Evidence = evidence;
                UnavailableReason = unavailableReason;
            }
        }

        public sealed class Settings
        {
            public string ArchiveFolder { get; set; }
            public string SourceRevision { get; set; }
            public string SourceManifestSha256 { get; set; }
            public int WarmupFrames { get; set; } = 30;
            public int SampleFrames { get; set; } = 120;
            public int CameraRenderWallSamples { get; set; } = 8;
            public bool AllowPersistentCameraTarget { get; set; }
        }

        public static IEnumerator Run(Camera camera, XRayWeatherController controller, Scenario[] scenarios, Settings settings, Action<string> completed = null)
        {
            Validate(camera, controller, scenarios, settings);
            string output = Path.Combine(Path.GetFullPath(settings.ArchiveFolder), "Performance_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            var report = new Report { utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), sourceRevision = settings.SourceRevision, sourceManifestSha256 = settings.SourceManifestSha256, unityVersion = Application.unityVersion, editor = Application.isEditor, operatingSystem = SystemInfo.operatingSystem, processor = SystemInfo.processorType, graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsVersion = SystemInfo.graphicsDeviceVersion, graphicsMemoryMb = SystemInfo.graphicsMemorySize, scene = camera.gameObject.scene.path, camera = camera.name, width = camera.pixelWidth, height = camera.pixelHeight, dynamicResolutionAllowed = camera.allowDynamicResolution, persistentTargetAllowed = settings.AllowPersistentCameraTarget, renderTargetPath = camera.targetTexture != null ? "Camera.targetTexture/" + camera.targetTexture.name : "Display backbuffer", renderTargetFormat = camera.targetTexture != null ? camera.targetTexture.graphicsFormat.ToString() : "Display backbuffer format", renderTargetWidth = camera.targetTexture != null ? camera.targetTexture.width : camera.pixelWidth, renderTargetHeight = camera.targetTexture != null ? camera.targetTexture.height : camera.pixelHeight, renderTargetScope = camera.targetTexture != null ? "Enabled camera automatic render into caller-owned persistent RenderTexture. The probe does not create, resize or assign targets. Native frame samples contain no manual Camera.Render." : "Enabled camera automatic render into display backbuffer.", qualityLevel = QualitySettings.GetQualityLevel(), vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate, frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), warmupFrames = settings.WarmupFrames, sampleFrames = settings.SampleFrames, nativeScope = "Native whole Editor/Player frame. Includes other active cameras, Editor and validation overhead. Counter samples refer to completed frames.", gpuScope = "FrameTimingManager GPU whole frame. Only positive values with new nonzero frameStartTimestamp are accepted. No Camera.Render stopwatch value is treated as GPU time.", cameraRenderScope = settings.CameraRenderWallSamples > 0 ? "Separate Camera.Render CPU wall time after native samples. Includes CPU submission and possible waits. Excluded from native GPU statistics." : "NotRequested. CameraRenderWallSamples=0. No manual Camera.Render stage is executed.", memoryScope = "Global Unity allocation, managed heap and graphics-driver allocation. EstimatedBytes is a bounded system estimate supplied by the scenario, not measured VRAM.", scenarios = new ScenarioRecord[scenarios.Length] };
            var timings = new FrameTiming[1];
            var rows = new StringBuilder(settings.SampleFrames * scenarios.Length * 220);
            rows.AppendLine("scenario,sample,nativeIntervalMs,frameTimingCpuMs,gpuMs,tickCpuSubmitMs,wetnessTickCpuSubmitMs,tickMainThreadGcBytes,drawCalls,frameGcBytes,totalAllocatedBytes,managedUsedBytes,graphicsDriverAllocatedBytes,estimatedSystemBytes,frameTimestamp,widthScale,heightScale");
            double millisecondsPerTick = 1000.0 / Stopwatch.Frequency;
            using (ProfilerRecorder draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1))
            using (ProfilerRecorder allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1))
            {
                for (int scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
                {
                    Scenario scenario = scenarios[scenarioIndex];
                    var result = new ScenarioRecord { name = scenario.Name, evidence = scenario.Evidence, tickScope = scenario.TickScope ?? (scenario.Tick != null ? "Supplied scenario tick CPU wall time including submission and possible waits." : "No supplied tick callback."), status = string.IsNullOrEmpty(scenario.UnavailableReason) ? "Measured" : "Unavailable", unavailableReason = scenario.UnavailableReason };
                    report.scenarios[scenarioIndex] = result;
                    if (!string.IsNullOrEmpty(scenario.UnavailableReason)) continue;
                    var samples = new Sample[settings.SampleFrames];
                    var renderWall = new double[settings.CameraRenderWallSamples];
                    scenario.Setup();
                    PrecipitationConfig config = controller.EffectivePrecipitationConfig;
                    result.quality = config != null ? config.Quality.ToString() : "Missing configuration";
                    result.maxDrops = config != null ? config.MaxDrops : 0;
                    result.dropBudget = config != null ? config.DropBudget : 0;
                    result.atlasResolution = config != null ? config.AtlasResolution : 0;
                    using (ProfilerRecorder wetnessTick = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, WETNESS_TICK_MARKER, 1))
                    {
                        for (int warmup = 0; warmup < settings.WarmupFrames; warmup++)
                        {
                            RequireCamera(camera, settings.AllowPersistentCameraTarget);
                            scenario.Tick?.Invoke(Time.unscaledDeltaTime);
                            if (report.frameTimingEnabled) FrameTimingManager.CaptureFrameTimings();
                            yield return null;
                        }
                        result.estimatedSystemBytesBeforeSamples = scenario.EstimatedBytes != null ? scenario.EstimatedBytes() : controller.Precipitation != null ? controller.Precipitation.EstimatedBytes : 0;
                        result.estimatedSystemBytesScope = "Constant snapshot after warmup repeated in each CSV row. A second endpoint is read after native samples. Estimates include owned resource budgets and runtime clone allocations, not measured VRAM. Both expensive queries are outside timed samples. One unmeasured native yield isolates the first query from frame timing samples.";
                        yield return null;
                        result.rainDensityAfterWarmup = controller.Precipitation != null ? controller.Precipitation.RainDensity : 0.0f;
                        result.precipitationEnabledAfterWarmup = controller.Precipitation != null && controller.Precipitation.Enabled;
                        result.rainVisualVisible = controller.RainVisualOverride ?? true;
                        result.cameraViewsAfterWarmup = controller.Precipitation != null && controller.Precipitation.Pass != null ? controller.Precipitation.Pass.ViewCount : 0;
                        ulong lastTimestamp = 0;
                        if (report.frameTimingEnabled && FrameTimingManager.GetLatestTimings(1, timings) > 0) lastTimestamp = timings[0].frameStartTimestamp;
                        for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                        {
                            RequireCamera(camera, settings.AllowPersistentCameraTarget);
                            var sample = new Sample { frameTimingCpuMs = -1.0, gpuMs = -1.0, tickCpuSubmitMs = -1.0, wetnessTickCpuSubmitMs = -1.0, tickGcBytes = -1, drawCalls = -1, frameGcBytes = -1 };
                            long frameStart = Stopwatch.GetTimestamp();
                            if (scenario.Tick != null)
                            {
                                long allocationStart = GC.GetAllocatedBytesForCurrentThread();
                                long tickStart = Stopwatch.GetTimestamp();
                                scenario.Tick(Time.unscaledDeltaTime);
                                sample.tickCpuSubmitMs = (Stopwatch.GetTimestamp() - tickStart) * millisecondsPerTick;
                                sample.tickGcBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                            }
                            if (report.frameTimingEnabled) FrameTimingManager.CaptureFrameTimings();
                            yield return null;
                            sample.nativeIntervalMs = (Stopwatch.GetTimestamp() - frameStart) * millisecondsPerTick;
                            sample.drawCalls = draws.Valid && draws.Count > 0 ? draws.LastValue : -1;
                            sample.frameGcBytes = allocations.Valid && allocations.Count > 0 ? allocations.LastValue : -1;
                            sample.wetnessTickCpuSubmitMs = wetnessTick.Valid && wetnessTick.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds && wetnessTick.Count > 0 && wetnessTick.LastValue > 0 ? wetnessTick.LastValue * 0.000001 : -1.0;
                            sample.totalAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
                            sample.managedUsedBytes = Profiler.GetMonoUsedSizeLong();
                            sample.graphicsDriverBytes = Profiler.GetAllocatedMemoryForGraphicsDriver();
                            sample.estimatedBytes = result.estimatedSystemBytesBeforeSamples;
                            if (report.frameTimingEnabled && FrameTimingManager.GetLatestTimings(1, timings) > 0)
                            {
                                FrameTiming timing = timings[0];
                                if (timing.frameStartTimestamp != 0 && timing.frameStartTimestamp != lastTimestamp)
                                {
                                    lastTimestamp = timing.frameStartTimestamp;
                                    sample.frameTimestamp = timing.frameStartTimestamp;
                                    sample.frameTimingCpuMs = timing.cpuFrameTime > 0.0 ? timing.cpuFrameTime : -1.0;
                                    sample.gpuMs = timing.gpuFrameTime > 0.0 ? timing.gpuFrameTime : -1.0;
                                    sample.widthScale = timing.widthScale;
                                    sample.heightScale = timing.heightScale;
                                }
                            }
                            samples[sampleIndex] = sample;
                        }
                        result.estimatedSystemBytesAfterSamples = scenario.EstimatedBytes != null ? scenario.EstimatedBytes() : controller.Precipitation != null ? controller.Precipitation.EstimatedBytes : 0;
                        for (int renderIndex = 0; renderIndex < renderWall.Length; renderIndex++)
                        {
                            RequireCamera(camera, settings.AllowPersistentCameraTarget);
                            long renderStart = Stopwatch.GetTimestamp();
                            camera.Render();
                            renderWall[renderIndex] = (Stopwatch.GetTimestamp() - renderStart) * millisecondsPerTick;
                            yield return null;
                        }
                        result.nativeIntervalMs = Summarize(samples, Metric.NativeInterval);
                        result.frameTimingCpuMs = Summarize(samples, Metric.FrameTimingCpu);
                        result.gpuMs = Summarize(samples, Metric.Gpu);
                        result.tickCpuSubmitMs = Summarize(samples, Metric.TickCpu);
                        result.wetnessTickCpuSubmitMs = Summarize(samples, Metric.WetnessTickCpu);
                        result.wetnessTickScope = "ProfilerRecorder Scripts/XRay.Precipitation.WetnessTick. Actual wetness Tick CPU time including dispatch submission and possible waits. Not GPU compute completion. Fresh recorder per scenario.";
                        result.wetnessTickUnit = wetnessTick.Valid ? wetnessTick.UnitType.ToString() : "Unavailable";
                        result.wetnessTickStatus = result.wetnessTickCpuSubmitMs.available ? "Measured positive CPU marker samples" : "Unavailable. No positive valid nanosecond marker samples were collected for this scenario.";
                        result.tickMainThreadGcBytes = Summarize(samples, Metric.TickGc);
                        result.drawCalls = Summarize(samples, Metric.DrawCalls);
                        result.frameGcBytes = Summarize(samples, Metric.FrameGc);
                        result.totalAllocatedBytes = Summarize(samples, Metric.TotalMemory);
                        result.managedUsedBytes = Summarize(samples, Metric.ManagedMemory);
                        result.graphicsDriverAllocatedBytes = Summarize(samples, Metric.GraphicsMemory);
                        result.estimatedSystemBytes = Summarize(samples, Metric.EstimatedMemory);
                        result.cameraRenderCpuWallMs = Summarize(renderWall, renderWall.Length);
                        result.gpuStatus = result.gpuMs.available ? "Measured positive FrameTimingManager samples" : report.frameTimingEnabled ? "Unavailable. No positive GPU timings with new timestamps were returned." : "Unavailable. FrameTimingManager.IsFeatureEnabled is false. Project settings were not changed.";
                        for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++) AppendRow(rows, scenario.Name, sampleIndex, samples[sampleIndex]);
                    }
                }
            }
            string path = Path.Combine(output, "Performance.json");
            File.WriteAllText(Path.Combine(output, "PerformanceSamples.csv"), rows.ToString());
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            completed?.Invoke(path);
        }

        private static void Validate(Camera camera, XRayWeatherController controller, Scenario[] scenarios, Settings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            RequireCamera(camera, settings.AllowPersistentCameraTarget);
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (string.IsNullOrWhiteSpace(settings.ArchiveFolder) || !Path.GetFullPath(settings.ArchiveFolder).StartsWith(ARCHIVE + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Performance reports require a task subdirectory under the project Archive.");
            if (string.IsNullOrWhiteSpace(settings.SourceRevision) || string.IsNullOrWhiteSpace(settings.SourceManifestSha256)) throw new ArgumentException("Supply the tested source revision and source manifest SHA256.");
            if (settings.WarmupFrames < 8 || settings.SampleFrames < 1 || settings.CameraRenderWallSamples < 0) throw new ArgumentOutOfRangeException(nameof(settings));
            if (scenarios == null || scenarios.Length != SCENARIO_NAMES.Length) throw new ArgumentException("Provide all six precipitation scenarios, with an explicit reason for unavailable scenarios.", nameof(scenarios));
            for (int i = 0; i < scenarios.Length; i++)
            {
                if (scenarios[i] == null || scenarios[i].Name != SCENARIO_NAMES[i] || string.IsNullOrEmpty(scenarios[i].UnavailableReason) && scenarios[i].Setup == null) throw new ArgumentException("Unexpected precipitation scenario at index " + i + ". Expected " + SCENARIO_NAMES[i], nameof(scenarios));
            }
        }

        private static void RequireCamera(Camera camera, bool allowPersistentCameraTarget)
        {
            if (camera == null || !camera.isActiveAndEnabled || camera.pixelWidth != WIDTH || camera.pixelHeight != HEIGHT) throw new InvalidOperationException("Use an enabled native-rendered 1920x1080 camera. The performance helper does not alter cameras or scene settings.");
            RenderTexture target = camera.targetTexture;
            if (target != null && (!allowPersistentCameraTarget || !target.IsCreated() || target.width != WIDTH || target.height != HEIGHT)) throw new InvalidOperationException("A persistent camera target requires explicit opt-in and an existing 1920x1080 RenderTexture.");
        }

        private static MetricSummary Summarize(Sample[] samples, Metric metric)
        {
            var values = new double[samples.Length];
            int count = 0;
            foreach (Sample sample in samples)
            {
                double value = Read(sample, metric);
                if (value >= 0.0 && !double.IsNaN(value) && !double.IsInfinity(value)) values[count++] = value;
            }
            return Summarize(values, count);
        }

        private static MetricSummary Summarize(double[] values, int count)
        {
            var result = new MetricSummary { available = count > 0, sampleCount = count };
            if (count == 0) return result;
            Array.Sort(values, 0, count);
            double total = 0.0;
            for (int i = 0; i < count; i++) total += values[i];
            result.minimum = values[0];
            result.maximum = values[count - 1];
            result.mean = total / count;
            result.p95 = values[Math.Min(count - 1, (int)Math.Ceiling(count * 0.95) - 1)];
            return result;
        }

        private static double Read(Sample sample, Metric metric)
        {
            switch (metric)
            {
                case Metric.NativeInterval: return sample.nativeIntervalMs;
                case Metric.FrameTimingCpu: return sample.frameTimingCpuMs;
                case Metric.Gpu: return sample.gpuMs;
                case Metric.TickCpu: return sample.tickCpuSubmitMs;
                case Metric.WetnessTickCpu: return sample.wetnessTickCpuSubmitMs;
                case Metric.TickGc: return sample.tickGcBytes;
                case Metric.DrawCalls: return sample.drawCalls;
                case Metric.FrameGc: return sample.frameGcBytes;
                case Metric.TotalMemory: return sample.totalAllocatedBytes;
                case Metric.ManagedMemory: return sample.managedUsedBytes;
                case Metric.GraphicsMemory: return sample.graphicsDriverBytes > 0 ? sample.graphicsDriverBytes : -1;
                case Metric.EstimatedMemory: return sample.estimatedBytes;
                default: throw new ArgumentOutOfRangeException(nameof(metric));
            }
        }

        private static void AppendRow(StringBuilder rows, string scenario, int index, Sample sample)
        {
            rows.Append(scenario).Append(',').Append(index).Append(',');
            rows.Append(sample.nativeIntervalMs.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(sample.frameTimingCpuMs.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(sample.gpuMs.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(sample.tickCpuSubmitMs.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(sample.wetnessTickCpuSubmitMs.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            rows.Append(sample.tickGcBytes).Append(',').Append(sample.drawCalls).Append(',').Append(sample.frameGcBytes).Append(',').Append(sample.totalAllocatedBytes).Append(',').Append(sample.managedUsedBytes).Append(',').Append(sample.graphicsDriverBytes).Append(',').Append(sample.estimatedBytes).Append(',').Append(sample.frameTimestamp).Append(',');
            rows.Append(sample.widthScale.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(sample.heightScale.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        private enum Metric
        {
            NativeInterval,
            FrameTimingCpu,
            Gpu,
            TickCpu,
            WetnessTickCpu,
            TickGc,
            DrawCalls,
            FrameGc,
            TotalMemory,
            ManagedMemory,
            GraphicsMemory,
            EstimatedMemory
        }

        private struct Sample
        {
            public double nativeIntervalMs;
            public double frameTimingCpuMs;
            public double gpuMs;
            public double tickCpuSubmitMs;
            public double wetnessTickCpuSubmitMs;
            public long tickGcBytes;
            public long drawCalls;
            public long frameGcBytes;
            public long totalAllocatedBytes;
            public long managedUsedBytes;
            public long graphicsDriverBytes;
            public long estimatedBytes;
            public ulong frameTimestamp;
            public float widthScale;
            public float heightScale;
        }

        [Serializable]
        private sealed class Report
        {
            public string utc;
            public string sourceRevision;
            public string sourceManifestSha256;
            public string unityVersion;
            public bool editor;
            public string operatingSystem;
            public string processor;
            public string graphicsDevice;
            public string graphicsApi;
            public string graphicsVersion;
            public int graphicsMemoryMb;
            public string scene;
            public string camera;
            public int width;
            public int height;
            public bool dynamicResolutionAllowed;
            public bool persistentTargetAllowed;
            public string renderTargetPath;
            public string renderTargetFormat;
            public int renderTargetWidth;
            public int renderTargetHeight;
            public string renderTargetScope;
            public int qualityLevel;
            public int vSyncCount;
            public int targetFrameRate;
            public bool frameTimingEnabled;
            public int warmupFrames;
            public int sampleFrames;
            public string nativeScope;
            public string gpuScope;
            public string cameraRenderScope;
            public string memoryScope;
            public ScenarioRecord[] scenarios;
        }

        [Serializable]
        private sealed class ScenarioRecord
        {
            public string name;
            public string evidence;
            public string tickScope;
            public string status;
            public string unavailableReason;
            public string quality;
            public int maxDrops;
            public int dropBudget;
            public int atlasResolution;
            public float rainDensityAfterWarmup;
            public bool precipitationEnabledAfterWarmup;
            public bool rainVisualVisible;
            public int cameraViewsAfterWarmup;
            public string gpuStatus;
            public string wetnessTickScope;
            public string wetnessTickUnit;
            public string wetnessTickStatus;
            public MetricSummary nativeIntervalMs;
            public MetricSummary frameTimingCpuMs;
            public MetricSummary gpuMs;
            public MetricSummary tickCpuSubmitMs;
            public MetricSummary wetnessTickCpuSubmitMs;
            public MetricSummary tickMainThreadGcBytes;
            public MetricSummary drawCalls;
            public MetricSummary frameGcBytes;
            public MetricSummary totalAllocatedBytes;
            public MetricSummary managedUsedBytes;
            public MetricSummary graphicsDriverAllocatedBytes;
            public string estimatedSystemBytesScope;
            public long estimatedSystemBytesBeforeSamples;
            public long estimatedSystemBytesAfterSamples;
            public MetricSummary estimatedSystemBytes;
            public MetricSummary cameraRenderCpuWallMs;
        }

        [Serializable]
        private sealed class MetricSummary
        {
            public bool available;
            public int sampleCount;
            public double minimum;
            public double maximum;
            public double mean;
            public double p95;
        }
    }
}
