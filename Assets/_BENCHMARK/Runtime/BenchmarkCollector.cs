using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Rvm.Benchmark
{
    public sealed class BenchmarkCollector : IDisposable
    {
        public const int METRIC_COUNT = 23;
        public const int CPU_MAIN = 0;
        public const int CPU_RENDER = 1;
        public const int CPU_TOTAL = 2;
        public const int PRESENT_WAIT = 3;
        public const int GPU = 4;

        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[METRIC_COUNT];
        private readonly FrameTiming[] _latest = new FrameTiming[16];
        private readonly bool _frameTimingEnabled;
        private readonly ulong _frequency;
        private readonly HashSet<ulong> _timingStamps;
        private bool _disposed;

        public BenchmarkMetric[] Metrics { get; }
        public double[] Values { get; }
        public FrameTiming[] Timings { get; }
        public int TimingCount { get; private set; }
        public bool TimingOverflow { get; private set; }
        public int MatchedTimings { get; private set; }
        public string Alignment { get; private set; } = "unavailable: frame timing timestamps have not been validated";

        public BenchmarkCollector(int capacity, BenchmarkMode mode)
        {
            Values = new double[capacity * METRIC_COUNT];
            for (int i = 0; i < Values.Length; i++)
            {
                Values[i] = double.NaN;
            }
            Timings = new FrameTiming[capacity + 256];
            _timingStamps = new HashSet<ulong>(Timings.Length);
            Metrics = new[]
            {
                Metric("cpu_main", "ms", "Main Thread"),
                Metric("cpu_render", "ms", "Render Thread"),
                Metric("cpu_total", "ms", null),
                Metric("present_wait", "ms", null),
                Metric("gpu", "ms", null),
                Metric("gc_alloc", "bytes", "GC Allocated In Frame"),
                Metric("draw_calls", "count", "Draw Calls Count"),
                Metric("setpass", "count", "SetPass Calls Count"),
                Metric("batches", "count", "Batches Count"),
                Metric("triangles", "count", "Triangles Count"),
                Metric("vertices", "count", "Vertices Count"),
                Metric("allocated_memory", "bytes", "Total Used Memory"),
                Metric("managed_memory", "bytes", "GC Used Memory"),
                Metric("graphics_memory", "bytes", "Gfx Used Memory"),
                Metric("texture_memory", "bytes", "Texture Memory"),
                Metric("shadow_casters", "count", "Shadow Casters Count"),
                Metric("reserved_memory", "bytes", "Total Reserved Memory"),
                Metric("render_texture_memory", "bytes", "Render Textures Bytes"),
                Metric("gc_alloc_count", "count", "GC Allocation In Frame Count"),
                Metric("system_memory", "bytes", "System Used Memory"),
                Metric("dynamic_batches", "count", "Dynamic Batches Count"),
                Metric("static_batches", "count", "Static Batches Count"),
                Metric("instanced_batches", "count", "Instanced Batches Count")
            };
            _frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();
            _frequency = FrameTimingManager.GetCpuTimerFrequency();
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            try
            {
                for (int i = 0; i < Metrics.Length; i++)
                {
                    var metric = Metrics[i];
                    if (i >= 15 && mode == BenchmarkMode.Standard)
                    {
                        metric.reason = "Disabled in Standard mode.";
                        continue;
                    }
                    if (i >= CPU_TOTAL && i <= GPU)
                    {
                        metric.source = "FrameTimingManager (timestamp-aligned after collection)";
                        metric.reason = _frameTimingEnabled ? "No validated source-frame samples yet." : "Frame Timing Stats is disabled or unsupported; benchmark does not change PlayerSettings.";
                        continue;
                    }
                    string category = i < 2 ? "Internal" : (i >= 6 && i <= 10) || i == 15 || i == 17 || i >= 20 ? "Render" : "Memory";
                    bool found = false;
                    for (int j = 0; j < handles.Count; j++)
                    {
                        var description = ProfilerRecorderHandle.GetDescription(handles[j]);
                        var expected = metric.unit == "ms" ? ProfilerMarkerDataUnit.TimeNanoseconds : metric.unit == "bytes" ? ProfilerMarkerDataUnit.Bytes : ProfilerMarkerDataUnit.Count;
                        if (description.Name != metric.source || description.Category.Name != category || description.UnitType != expected)
                        {
                            continue;
                        }
                        found = true;
                        _recorders[i] = ProfilerRecorder.StartNew(description.Category, description.Name, 1);
                        metric.source = category + "/" + description.Name + " (previous completed frame)";
                        metric.reason = _recorders[i].Valid ? "No completed samples yet." : "Recorder is invalid in this execution mode.";
                        break;
                    }
                    if (!found)
                    {
                        metric.reason = "Exact counter/category/unit is absent from ProfilerRecorderHandle.GetAvailable in this execution mode.";
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void ReadPreviousFrame(int row)
        {
            int offset = row * METRIC_COUNT;
            for (int i = 0; i < METRIC_COUNT; i++)
            {
                if (!_recorders[i].Valid || _recorders[i].Count == 0)
                {
                    continue;
                }
                long value = _recorders[i].LastValue;
                if (value < 0 || (i < 2 && value == 0))
                {
                    continue;
                }
                Values[offset + i] = Metrics[i].unit == "ms" ? value * 0.000001 : value;
                Metrics[i].available = true;
                Metrics[i].reason = null;
            }
        }

        public void PollTimings()
        {
            if (!_frameTimingEnabled)
            {
                return;
            }
            uint count = FrameTimingManager.GetLatestTimings((uint)_latest.Length, _latest);
            for (int i = 0; i < count; i++)
            {
                AppendTiming(_latest[i]);
            }
            FrameTimingManager.CaptureFrameTimings();
        }

        public void AlignTimings(BenchmarkFrame[] frames, int count, long endTicks, bool windows)
        {
            if (!windows || !BenchmarkClock.WindowsQpc || _frequency == 0 || _frequency != (ulong)BenchmarkClock.Frequency)
            {
                Alignment = "unavailable: requires Windows QPC and equal FrameTimingManager/native QPC frequencies";
                return;
            }
            if (count < 2)
            {
                Alignment = "unavailable: insufficient frame boundaries";
                return;
            }
            int[] matches = new int[count];
            int[] timingIndices = new int[count];
            for (int i = 0; i < TimingCount; i++)
            {
                var timing = Timings[i];
                if (timing.frameStartTimestamp > long.MaxValue)
                {
                    continue;
                }
                long source = (long)timing.frameStartTimestamp;
                int row = SourceRow(frames, count, source);
                if (row < 1 || row >= count || source <= frames[row - 1].startTicks || source > frames[row].startTicks || timing.cpuTimePresentCalled <= (ulong)frames[row].startTicks || timing.cpuTimePresentCalled > (ulong)endTicks)
                {
                    continue;
                }
                matches[row]++;
                timingIndices[row] = i;
            }
            for (int row = 0; row < count; row++)
            {
                if (matches[row] != 1)
                {
                    continue;
                }
                var timing = Timings[timingIndices[row]];
                int offset = row * METRIC_COUNT;
                SetTiming(offset + CPU_TOTAL, timing.cpuFrameTime);
                SetTiming(offset + PRESENT_WAIT, timing.cpuMainThreadPresentWaitTime, true);
                SetTiming(offset + GPU, timing.gpuFrameTime);
                if (double.IsNaN(Values[offset + CPU_MAIN]))
                {
                    SetTiming(offset + CPU_MAIN, timing.cpuMainThreadFrameTime);
                }
                if (double.IsNaN(Values[offset + CPU_RENDER]))
                {
                    SetTiming(offset + CPU_RENDER, timing.cpuRenderThreadFrameTime);
                }
                MatchedTimings++;
                for (int metric = CPU_MAIN; metric <= GPU; metric++)
                {
                    if (!double.IsNaN(Values[row * METRIC_COUNT + metric]))
                    {
                        Metrics[metric].available = true;
                        Metrics[metric].reason = null;
                        if (metric < CPU_TOTAL && !Metrics[metric].source.Contains("FrameTimingManager"))
                        {
                            Metrics[metric].source += "; FrameTimingManager fallback where recorder has no valid sample";
                        }
                    }
                }
            }
            Alignment = MatchedTimings == 0 ? "unavailable: no timestamps fit recorded Windows QPC frame intervals" : "Windows QPC: frequency checked; frameStartTimestamp bracketed by consecutive Update ticks; Present must follow matched Update; ambiguous and first frames excluded";
        }

        public static int SourceRow(BenchmarkFrame[] frames, int count, long source)
        {
            int left = 0;
            int right = count;
            while (left < right)
            {
                int middle = left + (right - left) / 2;
                if (frames[middle].startTicks < source)
                {
                    left = middle + 1;
                }
                else
                {
                    right = middle;
                }
            }
            return left;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            for (int i = 0; i < _recorders.Length; i++)
            {
                _recorders[i].Dispose();
            }
        }

        private void SetTiming(int index, double value, bool allowZero = false)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value) && (value > 0 || (allowZero && value == 0)))
            {
                Values[index] = value;
            }
        }

        private void AppendTiming(FrameTiming timing)
        {
            if (timing.frameStartTimestamp == 0 || _timingStamps.Contains(timing.frameStartTimestamp))
            {
                return;
            }
            if (TimingCount == Timings.Length)
            {
                TimingOverflow = true;
                return;
            }
            _timingStamps.Add(timing.frameStartTimestamp);
            Timings[TimingCount++] = timing;
        }

        private static BenchmarkMetric Metric(string id, string unit, string counter)
        {
            return new BenchmarkMetric { id = id, unit = unit, source = counter };
        }
    }
}
