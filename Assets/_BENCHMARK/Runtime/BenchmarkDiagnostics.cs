using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Rvm.Benchmark
{
    public sealed class BenchmarkDiagnostics : IDisposable
    {
        private const int MAX_CPU_MARKERS = 160;
        private const int MAX_CPU_TIMERS = 96;
        private const int MAX_GPU_MARKERS = 64;
        private const int MAX_SNAPSHOTS = 256;
        private const int RAW_EVIDENCE_LIMIT = 128;

        private static readonly ProfilerMarker[] POSITION_MARKERS = { new ProfilerMarker("RVM.Benchmark.POS1"), new ProfilerMarker("RVM.Benchmark.POS2"), new ProfilerMarker("RVM.Benchmark.POS3"), new ProfilerMarker("RVM.Benchmark.POS4") };

        private readonly BenchmarkScenario _scenario;
        private readonly BenchmarkMetadata _metadata;
        private readonly BenchmarkSceneDiagnostics _scene;
        private readonly List<Counter> _cpu = new List<Counter>();
        private readonly List<Counter> _gpu = new List<Counter>();
        private readonly List<BenchmarkRecorderSource> _aliases = new List<BenchmarkRecorderSource>();
        private readonly List<BenchmarkPhysicalSourceEvidence> _aliasEvidence = new List<BenchmarkPhysicalSourceEvidence>();
        private List<ProfilerRecorderHandle> _catalog;
        private readonly bool _profilerEnabled;
        private readonly bool _cpuAreaEnabled;
        private readonly bool _rendererEnabled;
        private readonly System.Reflection.PropertyInfo _rendererSwitch;
        private readonly int _capacity;
        private readonly List<BenchmarkDiagnosticSnapshot> _rendering = new List<BenchmarkDiagnosticSnapshot>();
        private readonly List<BenchmarkDiagnosticSnapshot> _world = new List<BenchmarkDiagnosticSnapshot>();
        private double _nextSnapshot;
        private int _snapshotPosition = -1;
        private int _lastUnityFrame = -1;
        private int _cpuTimers;
        private int _frameGaps;
        private int _discoveredSources;
        private readonly Dictionary<string, int> _physicalCounts = new Dictionary<string, int>();
        private bool _disposed;

        public bool Sampling { get; private set; }
        public int RecorderCount
        {
            get
            {
                int count = 0;
                foreach (var counter in _cpu)
                {
                    count += counter.Source.Valid ? 1 : 0;
                }
                foreach (var counter in _gpu)
                {
                    count += counter.Source.Valid ? 1 : 0;
                }
                foreach (var source in _aliases)
                {
                    count += source.Valid ? 1 : 0;
                }
                return count;
            }
        }

        public BenchmarkDiagnostics(BenchmarkScenario scenario, Camera camera, BenchmarkMetadata metadata)
        {
            _scenario = scenario;
            _metadata = metadata;
            _capacity = Math.Min(scenario.FrameCapacity, 8192);
            _profilerEnabled = Profiler.enabled;
            _cpuAreaEnabled = Profiler.GetAreaEnabled(ProfilerArea.CPU);
            _scene = new BenchmarkSceneDiagnostics(camera, scenario.DiagnosticRendering);
            _rendererSwitch = _scene.RendererSwitch;
            _rendererEnabled = _rendererSwitch != null && (bool)_rendererSwitch.GetValue(null);
            try
            {
                if (scenario.DiagnosticCpu || scenario.DiagnosticGpu)
                {
                    Profiler.enabled = true;
                }
                if (scenario.DiagnosticCpu)
                {
                    Profiler.SetAreaEnabled(ProfilerArea.CPU, true);
                }
                _rendererSwitch?.SetValue(null, scenario.DiagnosticCpu || scenario.DiagnosticRendering);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void BeginSampling()
        {
            if (Sampling || _disposed)
            {
                return;
            }
            Sampling = true;
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            handles = BenchmarkRecorderSource.UniqueHandles(handles);
            _discoveredSources = handles.Count;
            _catalog = new List<ProfilerRecorderHandle>(handles);
            foreach (var handle in handles)
            {
                string key = BenchmarkRecorderSource.Key(ProfilerRecorderHandle.GetDescription(handle));
                _physicalCounts.TryGetValue(key, out int count);
                _physicalCounts[key] = count + 1;
            }
            var selected = new HashSet<string>();
            handles.Sort((a, b) =>
            {
                string first = ProfilerRecorderHandle.GetDescription(a).Name;
                string second = ProfilerRecorderHandle.GetDescription(b).Name;
                int priority = Priority(first).CompareTo(Priority(second));
                return priority == 0 ? string.CompareOrdinal(first, second) : priority;
            });
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                bool timing = description.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds;
                bool gpu = timing && (description.Flags & MarkerFlags.SampleGPU) != 0 && description.Category.Name == "Render";
                bool memory = (description.Category.Name == "Memory" || description.Category.Name == "Render") && (description.UnitType == ProfilerMarkerDataUnit.Bytes || description.UnitType == ProfilerMarkerDataUnit.Count);
                bool render = description.Category.Name == "Render" && description.UnitType == ProfilerMarkerDataUnit.Count;
                if (_cpu.Count < MAX_CPU_MARKERS && ((_scenario.DiagnosticCpu && timing && _cpuTimers < MAX_CPU_TIMERS && CpuCandidate(description.Name)) || (_scenario.DiagnosticMemory && memory && MemoryCandidate(description.Name)) || (_scenario.DiagnosticRendering && render && RenderCandidate(description.Name))))
                {
                    if (!selected.Add("CPU/" + BenchmarkRecorderSource.Key(description)))
                    {
                        continue;
                    }
                    Bind(_cpu, handle, description, false);
                    if (timing)
                    {
                        _cpuTimers++;
                    }
                }
            }
            handles.Sort((a, b) =>
            {
                string first = ProfilerRecorderHandle.GetDescription(a).Name;
                string second = ProfilerRecorderHandle.GetDescription(b).Name;
                int priority = GpuPriority(first).CompareTo(GpuPriority(second));
                return priority == 0 ? string.CompareOrdinal(first, second) : priority;
            });
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                if (_scenario.DiagnosticGpu && SystemInfo.supportsGpuRecorder && description.Category.Name == "Render" && description.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds && (description.Flags & MarkerFlags.SampleGPU) != 0 && _gpu.Count < MAX_GPU_MARKERS && GpuCandidate(description.Name))
                {
                    if (selected.Add("GPU/" + BenchmarkRecorderSource.Key(description)))
                    {
                        Bind(_gpu, handle, description, true);
                    }
                }
            }
            var signature = new StringBuilder(_metadata.instrumentationSignature);
            foreach (var counter in _cpu)
            {
                signature.Append('|').Append(counter.Data.identity.key).Append('/').Append(counter.Data.identity.kind).Append("/physical=").Append(counter.Data.identity.physicalSources);
            }
            foreach (var counter in _gpu)
            {
                signature.Append("|GPU/").Append(counter.Data.identity.key).Append("/physical=").Append(counter.Data.identity.physicalSources);
            }
            _metadata.instrumentationSignature = signature.ToString();
        }

        public void ReadPrevious(int row, BenchmarkFrame frame)
        {
            if (_disposed || !Sampling || frame.unityFrame == _lastUnityFrame)
            {
                return;
            }
            _lastUnityFrame = frame.unityFrame;
            bool consecutive = Time.frameCount == frame.unityFrame + 1;
            if (!consecutive)
            {
                _frameGaps++;
            }
            foreach (var counter in _cpu)
            {
                var sample = counter.Source.Read(Time.frameCount);
                sample.contextRow = row;
                sample.contextPosition = frame.position;
                sample.intervalStartUnityFrame = frame.unityFrame;
                if (!consecutive && sample.status == BenchmarkSampleStatus.Valid)
                {
                    sample.status = BenchmarkSampleStatus.AmbiguousInterval;
                    sample.Value = double.NaN;
                }
                if (counter.Data.rawEvidence.Count < RAW_EVIDENCE_LIMIT && (row % 8 == 0 || counter.Data.rawEvidence.Count < 4))
                {
                    sample.observedQpcTicks = BenchmarkClock.GetTimestamp();
                    counter.Data.rawEvidence.Add(sample);
                }
                counter.Data.samplesSeen += sample.publications;
                if (sample.status != BenchmarkSampleStatus.Valid)
                {
                    if (sample.status != BenchmarkSampleStatus.NoPublication)
                    {
                        counter.Data.invalidSamples++;
                        counter.Data.validityStatus = sample.status.ToString();
                        counter.Data.reason = BenchmarkRecorderSource.Reason(sample.status);
                    }
                    continue;
                }
                if (row >= _capacity)
                {
                    continue;
                }
                counter.Values[row] = sample.Value;
                counter.Calls[row] = sample.calls;
                counter.Data.validSamples++;
                counter.ObservedCalls += sample.calls;
            }
            foreach (var source in _aliases)
            {
                var sample = source.Read(Time.frameCount);
                sample.contextRow = row;
                sample.contextPosition = frame.position;
                sample.intervalStartUnityFrame = frame.unityFrame;
                source.RecordEvidence(sample);
            }
            if (_world.Count >= MAX_SNAPSHOTS || (frame.timestamp < _nextSnapshot && frame.position == _snapshotPosition))
            {
                return;
            }
            _nextSnapshot = frame.timestamp + _scenario.SnapshotSeconds;
            _snapshotPosition = frame.position;
            long start = BenchmarkClock.GetTimestamp();
            _world.Add(_scene.World(frame));
            if (_scenario.DiagnosticRendering || _scenario.DiagnosticMemory)
            {
                _rendering.Add(_scene.Rendering(frame, _scenario.DiagnosticRendering, _scenario.DiagnosticMemory));
            }
            foreach (var counter in _gpu)
            {
                var sample = counter.Source.Read(Time.frameCount);
                counter.Data.samplesSeen += sample.publications;
                sample.contextRow = row;
                sample.contextPosition = frame.position;
                sample.intervalStartUnityFrame = frame.unityFrame;
                if (counter.Data.rawEvidence.Count < RAW_EVIDENCE_LIMIT)
                {
                    sample.observedQpcTicks = BenchmarkClock.GetTimestamp();
                    counter.Data.rawEvidence.Add(sample);
                }
                if (sample.status == BenchmarkSampleStatus.Valid && sample.Value > 0 && sample.calls > 0)
                {
                    counter.Data.observations.Add(new BenchmarkGpuObservation { observedUnityFrame = Time.frameCount, observedPosition = frame.position, milliseconds = sample.Value, calls = sample.calls });
                    counter.Data.available = true;
                    counter.Data.validSamples++;
                }
                else if (sample.status != BenchmarkSampleStatus.NoPublication)
                {
                    counter.Data.invalidSamples++;
                    counter.Data.reason = BenchmarkRecorderSource.Reason(sample.status);
                }
            }
            double cost = (BenchmarkClock.GetTimestamp() - start) * 1000000.0 / BenchmarkClock.Frequency;
            _world[_world.Count - 1].collectionMicroseconds = cost;
            if (_rendering.Count > 0)
            {
                _rendering[_rendering.Count - 1].collectionMicroseconds = cost;
            }
        }

        public void StartMeasuredInterval()
        {
            foreach (var source in _aliases)
            {
                source.Arm();
            }
            foreach (var counter in _cpu)
            {
                counter.Source.Arm();
            }
            foreach (var counter in _gpu)
            {
                counter.Source.Arm();
            }
        }

        public void Position(int position)
        {
            if (!_disposed)
            {
                using (POSITION_MARKERS[position].Auto())
                {
                }
            }
        }

        public void Save(string directory, BenchmarkFrame[] frames, int count, BenchmarkSummary summary)
        {
            string folder = Path.Combine(directory, "diagnostic");
            Directory.CreateDirectory(folder);
            var cpu = Document(summary.status);
            var memory = Document(summary.status);
            var rendering = Document(summary.status);
            var gpu = Document(summary.status);
            var world = Document(summary.status);
            cpu.limitations.Add("Means/P95 use valid published intervals only; missing updates are not zero. Windows record contextRows and actual sample count. Source frame/POS association is not native-verified. Raw evidence status enum is decoded by rawStatusLegend. All-thread inclusive frame sums; nested/parallel markers overlap and MUST NOT be added. Exclusive/self time and thread attribution are unavailable: use the Timeline/Hierarchy capture workflow.");
            cpu.limitations.Add("Previous completed profiler frame is associated by consecutive Unity Update cadence, not a native sample frame ID. Skipped ambiguous frame gaps: " + _frameGaps + ". Editor and other camera work may contribute. One Diagnostic preparation frame after binding is excluded from the measured interval.");
            cpu.limitations.Add("Bit 7 is the HDRP shared-recorder option, not frame-end flush. Benchmark uses private handle-bound recorders without sharing/wrapping. Monotonic publication cursors prevent replay of missing updates; source frame remains unknown.");
            gpu.limitations.Add("Delayed GPU observations have NO source-frame or source-POS identity. ObservedPosition is observation context only. Only new native publications are read; GPU source frames remain unknown; no per-POS aggregate, sum, percentile, or bottleneck verdict is derived from them.");
            gpu.limitations.Add("Absent/zero samples do not prove a pass disabled. Actual HDRP effect enablement and custom shader costs require a capture. Frame Debugger is ordering evidence, not a GPU timer.");
            if (!_scenario.DiagnosticGpu || !SystemInfo.supportsGpuRecorder || _gpu.Count == 0)
            {
                gpu.limitations.Add("GPU pass recording unavailable/disabled on this runtime: capture required. SystemInfo.supportsGpuRecorder=" + SystemInfo.supportsGpuRecorder);
            }
            rendering.limitations.Add("Cached component inventory; newly spawned/destroyed objects can change coverage. Frustum candidates are not occlusion visibility. Renderer.isVisible includes other cameras. Shadow-configured lights are not measured shadow draws.");
            rendering.limitations.Add("Instancer visible counts precede LOD and include wind/distance tests. CPU submissions exclude separate impostor/reflection paths. Registered GPU batches and actual GPU pass draw calls are unavailable. Serialized batches are never substituted for these values.");
            memory.limitations.Add("Unity accounted/estimated bytes, not driver VRAM residency. No memory snapshot or native allocator call stacks are taken during measurement; use a separate Memory Profiler capture outside the interval.");
            memory.limitations.Add("Memory totals include diagnostic preallocated value/call buffers and native profiler resources. Compare only identical instrumentation/capacity; do not subtract unrelated runs to estimate game memory.");
            world.limitations.Add("Fixed mode freezes only existing stable weather profile/time. NPCs, physics, anomaly cycles and random state are not reset. Disabled visuals do not imply a disabled source. Unknown custom AI remains unavailable.");
            if (_cpu.Count >= MAX_CPU_MARKERS || _cpuTimers >= MAX_CPU_TIMERS)
            {
                cpu.limitations.Add("Marker capacity reached; unselected subsystem markers require a capture. Native memory/render counters have separate headroom from CPU timing markers.");
            }
            foreach (var counter in _cpu)
            {
                counter.Data.available = counter.Data.validSamples > 0 && (counter.Data.unit != "ms" || counter.ObservedCalls > 0);
                if (counter.Data.available)
                {
                    counter.Data.validityStatus = counter.Data.invalidSamples > 0 ? "ValidSubset" : "Valid";
                    counter.Data.reason = counter.Data.invalidSamples > 0 ? "Invalid publications excluded; see rawEvidence and sample statuses." : "";
                }
                else if (counter.Data.samplesSeen == 0)
                {
                    counter.Data.validityStatus = "NoSamples";
                    counter.Data.reason = "Source found but no new native publications observed.";
                }
                for (int position = -1; position < 4; position++)
                {
                    counter.Data.windows.Add(Aggregate(counter.Values, counter.Calls, frames, Math.Min(count, _capacity), position, false, counter.Data.available));
                    counter.Data.windows.Add(Aggregate(counter.Values, counter.Calls, frames, Math.Min(count, _capacity), position, true, counter.Data.available));
                }
                if (counter.Data.category == "Memory" || counter.Data.unit == "bytes")
                {
                    memory.markers.Add(counter.Data);
                }
                else if (counter.Data.unit == "count")
                {
                    rendering.markers.Add(counter.Data);
                }
                else
                {
                    cpu.markers.Add(counter.Data);
                }
            }
            foreach (var counter in _gpu)
            {
                counter.Data.reason = counter.Data.available ? "Delayed unaligned observations only; capture required for per-position costs." : "No valid GPU timing; capture required.";
                gpu.markers.Add(counter.Data);
            }
            rendering.snapshots.AddRange(_rendering);
            memory.snapshots.AddRange(_rendering);
            world.snapshots.AddRange(_world);
            foreach (var pair in new[] { ("cpu", cpu), ("gpu", gpu), ("rendering", rendering), ("memory", memory), ("world-state", world) })
            {
                BenchmarkOutput.WriteJson(Path.Combine(folder, pair.Item1 + ".json"), pair.Item2);
            }
            BenchmarkOutput.WriteJson(Path.Combine(folder, "physical-source-evidence.json"), new BenchmarkDiagnosticDocument { runId = _metadata.runId, physicalAliases = _aliasEvidence });
            WriteReport(folder, summary, cpu, gpu, rendering, memory, world);
        }

        public static BenchmarkDiagnosticWindow Aggregate(double[] values, long[] calls, BenchmarkFrame[] frames, int count, int position, bool stable, bool observed)
        {
            var selected = new List<double>();
            long total = 0;
            int contextRows = 0;
            if (observed)
            {
                for (int i = 0; i < count; i++)
                {
                    if ((position < 0 || frames[i].position == position) && (!stable || !frames[i].transition))
                    {
                        contextRows++;
                    }
                    if ((position < 0 || frames[i].position == position) && (!stable || !frames[i].transition) && !double.IsNaN(values[i]))
                    {
                        selected.Add(values[i]);
                        total += calls[i];
                    }
                }
            }
            return new BenchmarkDiagnosticWindow { position = position, window = stable ? "Stable" : "Full", callCount = total, contextRows = contextRows, inclusive = BenchmarkStatistics.Distribution(selected.ToArray()) };
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach (var counter in _cpu)
            {
                counter.Source.Dispose();
            }
            foreach (var counter in _gpu)
            {
                counter.Source.Dispose();
            }
            foreach (var source in _aliases)
            {
                source.Dispose();
            }
            _rendererSwitch?.SetValue(null, _rendererEnabled);
            Profiler.enabled = _profilerEnabled;
            Profiler.SetAreaEnabled(ProfilerArea.CPU, _cpuAreaEnabled);
        }

        private void Bind(List<Counter> list, ProfilerRecorderHandle handle, ProfilerRecorderDescription description, bool gpu)
        {
            BenchmarkRecorderSource source = null;
            try
            {
                source = new BenchmarkRecorderSource(handle, description, _physicalCounts[BenchmarkRecorderSource.Key(description)], gpu ? 8192 : 32768, gpu);
                var data = new BenchmarkDiagnosticMarker { name = description.Name, category = description.Category.Name, unit = description.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds ? "ms" : description.UnitType == ProfilerMarkerDataUnit.Bytes ? "bytes" : "count", identity = source.Identity, rawEvidence = source.Evidence, sourceFound = source.Valid, validityStatus = "NoSamples", scope = gpu ? "delayed GPU observation; source frame unknown" : source.Identity.aggregation + "; interval-associated, source Unity frame unknown", reason = "Source discovered; no interpreted samples yet; self time unavailable." };
                var counter = new Counter { Data = data, Source = source, Values = gpu ? null : new double[_capacity], Calls = gpu ? null : new long[_capacity] };
                if (!gpu)
                {
                    Array.Fill(counter.Values, double.NaN);
                }
                list.Add(counter);
                if (source.Identity.physicalSources > 1)
                {
                    int ordinal = 0;
                    foreach (var aliasHandle in _catalog)
                    {
                        var aliasDescription = ProfilerRecorderHandle.GetDescription(aliasHandle);
                        if (BenchmarkRecorderSource.Key(aliasDescription) != source.Identity.key)
                        {
                            continue;
                        }
                        if (aliasHandle.Equals(handle))
                        {
                            _aliasEvidence.Add(new BenchmarkPhysicalSourceEvidence { role = gpu ? "GPU" : "CPU", logicalKey = source.Identity.key, physicalOrdinal = ordinal++, identity = source.Identity, rawEvidence = source.Evidence });
                            continue;
                        }
                        var alias = new BenchmarkRecorderSource(aliasHandle, aliasDescription, source.Identity.physicalSources, 32768, gpu);
                        _aliases.Add(alias);
                        _aliasEvidence.Add(new BenchmarkPhysicalSourceEvidence { role = gpu ? "GPU" : "CPU", logicalKey = source.Identity.key, physicalOrdinal = ordinal++, identity = alias.Identity, rawEvidence = alias.Evidence });
                    }
                }
            }
            catch (Exception exception)
            {
                source?.Dispose();
                _metadata.warnings.Add("Diagnostic recorder unavailable: " + description.Name + ": " + exception.Message);
            }
        }

        private BenchmarkDiagnosticDocument Document(string status)
        {
            return new BenchmarkDiagnosticDocument { runId = _metadata.runId, signature = _metadata.instrumentationSignature, status = status, discoveredPhysicalSources = _discoveredSources, selectedUniqueSources = _cpu.Count + _gpu.Count };
        }

        private void WriteReport(string folder, BenchmarkSummary summary, params BenchmarkDiagnosticDocument[] documents)
        {
            var text = new StringBuilder("# Benchmark Diagnostic V2\n\nRun ID: " + _metadata.runId + "\n\nStatus: " + summary.status + "; " + _metadata.execution + "; " + _metadata.graphicsApi + "; VSync=" + _metadata.vsync + "; target=" + _metadata.targetFrameRate + "; refresh=" + _metadata.refreshRateHz + " Hz.\n\nOverall pacing, timestamp-aligned CPU/GPU frame totals and Full/Stable POS metrics: ../report.md. Diagnostics add profiler/recorder and low-frequency snapshot overhead; compare only identical instrumentation signatures. No optimization is applied.\n");
            foreach (var document in documents)
            {
                foreach (var limitation in document.limitations)
                {
                    text.AppendLine("\n- " + limitation);
                }
            }
            text.AppendLine("\n## CPU / rendering / memory by position\n\nMarker | Category | POS | Window | Unit | Calls | Mean | Max | P95\n--- | --- | --- | --- | --- | ---: | ---: | ---: | ---:");
            foreach (var counter in _cpu)
            {
                foreach (var window in counter.Data.windows)
                {
                    if (!window.inclusive.available)
                    {
                        continue;
                    }
                    text.AppendLine(counter.Data.name.Replace("|", "/") + " | " + counter.Data.category + " | " + (window.position + 1) + " | " + window.window + " | " + counter.Data.unit + " | " + window.callCount + " | " + BenchmarkOutput.Number(window.inclusive.mean) + " | " + BenchmarkOutput.Number(window.inclusive.max) + " | " + BenchmarkOutput.Number(window.inclusive.p95));
                }
            }
            text.AppendLine("\n## GPU observations\n\nName | Valid observations | Last observed ms | Maximum observed ms\n--- | ---: | ---: | ---:");
            foreach (var counter in _gpu)
            {
                double max = 0;
                foreach (var observation in counter.Data.observations)
                {
                    max = Math.Max(max, observation.milliseconds);
                }
                int n = counter.Data.observations.Count;
                text.AppendLine(counter.Data.name + " | " + n + " | " + (n > 0 ? BenchmarkOutput.Number(counter.Data.observations[n - 1].milliseconds) : "unavailable") + " | " + (n > 0 ? BenchmarkOutput.Number(max) : "unavailable"));
            }
            text.AppendLine("\n## Rendering, memory and world snapshots\n\nSnapshot entries and collection cost are preserved in rendering.json, memory.json and world-state.json with source-frame/POS context. GPU observation context is explicitly different. Component inventory is cached before warmup; reads occur at most once per interval or POS boundary, limited to 256 snapshots. No full scene traversal occurs per frame.\n\n## Unavailable and anomalies\n");
            foreach (var counter in _cpu)
            {
                if (!counter.Data.available)
                {
                    text.AppendLine("- " + counter.Data.name + ": " + counter.Data.reason);
                }
            }
            text.AppendLine("- Self/exclusive CPU and individual threads, independent frustum-only cost, GPU source-POS, GPU draw calls, exact native allocations/residency: capture required. Missing script/AI markers are unavailable, not zero. AnomalyCycle plain-class work is included in its caller when no dedicated marker exists.");
            foreach (var warning in summary.warnings)
            {
                text.AppendLine("- " + warning);
            }
            foreach (var window in summary.windows)
            {
                if (window.frames.frameTime.available && window.frames.frameTime.max > 3 * window.frames.frameTime.p50)
                {
                    text.AppendLine("- Observed long-frame outlier: POS " + (window.position + 1) + " " + window.window + ", maximum > 3 * median. This does not identify a cause.");
                }
            }
            text.AppendLine("\n## Capture workflow\n\n1. Select Diagnostic and optional Binary capture in Tools/RVM/Benchmark, retain this Run ID directory. Binary profiler.raw is outside the measured interval's file-writing code but Unity capture itself adds overhead. Never compare this capture with Standard as an equivalent workload.\n2. Open profiler.raw in Unity Profiler (CPU Timeline/Hierarchy), use RVM.Benchmark.POS1..POS4 boundaries and ../frames.csv unity_frame / transition rows. Inspect thread lanes, parent/child inclusive vs self, RVM.XRay.MatrixHistory / Submission / Lod, script Update/FixedUpdate, Physics, waits and GC. Do not sum inclusive children/parents.\n3. For GPU, attach Unity GPU Profiler or RenderDoc to a separately permitted Development Player. Save captures under this Run ID/captures/POSn with a sidecar containing runId, Unity frame, camera, POS, transition flag, graphics API, HDRP version and capture tool. Capture a stable frame after the POS marker. GPU capture/replay overhead makes it a separate diagnostic run, not a baseline.\n4. Inspect actual shadow/depth/GBuffer/deferred/forward/transparency/distortion/SSAO/SSR/SSGI/fog/cloud/sky/TAA/bloom/post/custom/motion/reflection passes only when present in the capture. Custom HLSL attribution requires tool-supported events/debug symbols; no shader cost is inferred from object counts or differences between unrelated frames. Frame Debugger only verifies draw ordering and pass enablement.\n5. If no source Unity frame can be read in a GPU tool, annotate pose/frame as unverified and capture one explicitly held pose outside the timed protocol; do not silently label a delayed GPU sample with the current POS. Memory snapshots are a separate operation after/before the run, saved under this directory.\n");
            SnapshotTable(text, "Rendering snapshots", _rendering, new[] { "lights.active", "lights.configuredShadowed", "particles.playingSystems", "particles.liveCount", "instancer.0.serializedBatches", "instancer.0.LastVisibleBatches", "instancer.0.LastVisibleInstances", "instancer.0.LastShadowOnlyBatches", "instancer.0.DiagnosticSubmittedBatches", "instancer.0.DiagnosticCpuSubmissions" });
            SnapshotTable(text, "World snapshots", _world, new[] { "weather.0.ActiveProfile", "weather.0.GameTimeSeconds", "weather.0.TimeScale", "weather.0.Paused", "weather.0.IsTransitioning", "weather.0.FxPhase", "anomalies.sourceActiveFlag", "anomalies.sourceDisabledFlag", "anomalies.enabledNonIdleCycles", "anomalies.visibleVisualComponents", "npc.activeNavMeshAgents" });
            SnapshotTable(text, "Texture streaming / GC snapshots", _rendering, new[] { "textureStreaming.currentTextureMemory", "textureStreaming.targetTextureMemory", "textureStreaming.streamingTextureCount", "textureStreaming.enabled", "gc.collections.gen0", "gc.collections.gen1", "gc.collections.gen2" });
            File.WriteAllText(Path.Combine(folder, "diagnostic-report.md"), text.ToString(), Encoding.UTF8);
        }

        private static void SnapshotTable(StringBuilder text, string title, List<BenchmarkDiagnosticSnapshot> snapshots, string[] keys)
        {
            text.AppendLine("\n## " + title + "\n\nPOS | Completed Unity frame | Seconds | Snapshot cost us | " + string.Join(" | ", keys));
            text.AppendLine("--- | ---: | ---: | ---: | " + string.Join(" | ", Array.ConvertAll(keys, value => "---")));
            foreach (var snapshot in snapshots)
            {
                text.Append((snapshot.position + 1) + " | " + snapshot.unityFrame + " | " + BenchmarkOutput.Number(snapshot.elapsedSeconds) + " | " + BenchmarkOutput.Number(snapshot.collectionMicroseconds));
                foreach (string key in keys)
                {
                    var entry = snapshot.entries.Find(value => value.key == key);
                    text.Append(" | ").Append(entry == null ? "unavailable" : entry.value.Replace("|", "/"));
                }
                text.AppendLine();
            }
        }

        private static bool ContainsAny(string name, params string[] terms)
        {
            foreach (string term in terms)
            {
                if (name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static int Priority(string name)
        {
            if (ContainsAny(name, "RVM.XRay"))
            {
                return 0;
            }
            if (ContainsAny(name, "WaitFor", "GC.Collect", "GarbageCollect", "Physics.Simulate", "Physics.Processing", "CharacterController", "AudioManager.Update", "Animator.Update", "Animation.Update") || (ContainsAny(name, "XRayWeather", "Anomaly") && ContainsAny(name, "Update()", "FixedUpdate()", "LateUpdate()")))
            {
                return 1;
            }
            if (ContainsAny(name, "PlayerLoop", "ScriptRun", "BehaviourUpdate", "BehaviourFixedUpdate", "BehaviourLateUpdate", "PreLateUpdate", "PhysicsFixedUpdate"))
            {
                return 2;
            }
            if (MemoryCandidate(name) || RenderCandidate(name))
            {
                return 3;
            }
            return 3;
        }

        private static bool CpuCandidate(string name)
        {
            return !ContainsAny(name, ".cctor(", ".ctor(", "UnityEditor", ".Editor.", ".Editor.dll", "AssemblyReloadEvents", "EditorApplication", "EditorSceneManager") && ContainsAny(name, "RVM.XRay", "PlayerLoop", "BehaviourUpdate", "BehaviourFixedUpdate", "BehaviourLateUpdate", "PreLateUpdate", "Camera.Render", "RenderPipeline", "ScriptRun", "Physics", "CharacterController", "XRayWeather", "Anomaly", "GarbageCollect", "GC.Collect", "WaitFor", "Animator.Update", "AudioManager", "Audio.Update", "Animation.Update");
        }

        private static int GpuPriority(string name)
        {
            switch (name)
            {
                case "RenderShadowMaps":
                case "RenderMomentShadowMaps":
                case "RenderEVSMShadowMaps":
                case "ScreenSpaceShadows":
                case "VolumetricLighting":
                case "Volumetric Lighting":
                case "VolumeVoxelization":
                case "Fog Volume And VFX Voxelization":
                case "VolumetricClouds":
                case "VolumetricCloudsShadow":
                case "RenderSky":
                case "RenderClouds":
                case "OpaqueAtmosphericScattering":
                case "CopyDepthBuffer":
                case "ForwardDepthPrepass":
                case "DeferredDepthPrepass":
                case "TransparentDepthPrepass":
                case "GBuffer":
                case "Deferred Lighting":
                case "RenderDeferredLightingCompute":
                case "ForwardOpaque":
                case "ForwardTransparent":
                case "ForwardPreRefraction":
                case "Distortion":
                case "ApplyDistortion":
                case "SsrTracing":
                case "SsrReprojection":
                case "SsrAccumulate":
                case "AmbientOcclusion":
                case "HorizonSSAO":
                case "SSGIPass":
                case "PostProcessing":
                case "TemporalAntialiasing":
                case "Bloom":
                case "ObjectsMotionVector":
                case "CameraMotionVectors":
                case "ConvolveReflectionProbe":
                case "ConvolvePlanarReflectionProbe":
                    return 0;
            }
            return ContainsAny(name, "Custom", "Volumetric", "Cloud", "Shadow", "Sky", "SSGI", "SSAO", "PostProcess", "Temporal") ? 1 : 2;
        }

        private static bool GpuCandidate(string name)
        {
            return !name.StartsWith("Inl_", StringComparison.Ordinal) && !ContainsAny(name, "Debug", "Display", "EditorOnly") && ContainsAny(name, "Shadow", "Volume", "Volumetric", "Cloud", "Sky", "Atmospher", "Depth", "GBuffer", "Deferred", "Forward", "Transparent", "Distort", "SSR", "SSAO", "SSGI", "AmbientOcclusion", "PostProcess", "TAA", "Temporal", "Bloom", "Custom", "Motion", "Reflection", "LightLoop");
        }

        private static bool MemoryCandidate(string name)
        {
            return ContainsAny(name, "Total Used", "Total Reserved", "GC Used", "GC Reserved", "Gfx Used", "Gfx Reserved", "Texture", "Mesh", "GC Alloc", "Native", "System Used", "Render Textures Bytes", "Used Buffers Bytes", "Used Textures Bytes", "Video Memory Bytes");
        }

        private static bool RenderCandidate(string name)
        {
            return ContainsAny(name, "Draw Calls", "SetPass", "Batches", "Triangles", "Vertices", "Shadow Casters", "Shadow Draw");
        }

        private sealed class Counter
        {
            public BenchmarkRecorderSource Source { get; set; }
            public BenchmarkDiagnosticMarker Data { get; set; }
            public double[] Values { get; set; }
            public long[] Calls { get; set; }
            public long ObservedCalls { get; set; }
        }
    }
}
