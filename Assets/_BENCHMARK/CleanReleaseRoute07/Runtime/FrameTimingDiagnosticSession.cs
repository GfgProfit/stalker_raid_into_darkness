#if UNITY_EDITOR || CLEAN_RELEASE_ROUTE07
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Rvm.CleanReleaseRoute07
{
    public sealed class FrameTimingDiagnosticSession
    {
        private const int MAX_CAPACITY = 32768;
        private const int LATEST_CAPACITY = 16;

        private readonly FrameTiming[] _latest = new FrameTiming[LATEST_CAPACITY];
        private readonly Sample[] _samples;
        private readonly Dictionary<ulong, int> _indices;
        private readonly ulong _cpuTimerFrequency;
        private readonly ulong _gpuTimerFrequency;
        private readonly bool _featureEnabled;
        private readonly ClockSample _startClock;
        private int _count;
        private int _polls;
        private int _publications;
        private int _duplicates;
        private int _zeroTimestamps;
        private int _changedPublications;
        private int _positiveGpuLostPublications;
        private bool _overflow;
        private bool _saved;

        public int Count => _count;
        public bool Overflow => _overflow;

        public FrameTimingDiagnosticSession(int capacity = MAX_CAPACITY)
        {
            if (capacity <= 0 || capacity > MAX_CAPACITY)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            _samples = new Sample[capacity];
            _indices = new Dictionary<ulong, int>(capacity);
            _cpuTimerFrequency = FrameTimingManager.GetCpuTimerFrequency();
            _gpuTimerFrequency = FrameTimingManager.GetGpuTimerFrequency();
            _featureEnabled = FrameTimingManager.IsFeatureEnabled();
            _startClock = CaptureClock();
        }

        public static bool Requested(string[] args)
        {
            int index = Array.IndexOf(args, "-benchmark-frame-timing-diagnostic");
            if (index < 0)
            {
                return false;
            }
            if (Array.LastIndexOf(args, "-benchmark-frame-timing-diagnostic") != index || Array.IndexOf(args, "-clean-route") < 0 || Array.IndexOf(args, "-clean-route") != Array.LastIndexOf(args, "-clean-route"))
            {
                throw new ArgumentException("Frame timing diagnostics require exactly one explicit -clean-route and diagnostic flag.");
            }
            foreach (string forbidden in new[] { "-clean-route-config-check", "-clean-route-no-timing", "-benchmark-pix-diagnostic", "-benchmark-pix-capture-position", "-benchmark-cpu-diagnostic", "-benchmark-cpu-marker-config", "-punctual-shadow-mode", "-punctual-shadow-technical", "-punctual-shadow-capture", "-batching-counters", "-batching-capture", "-preparation-counters", "-preparation-capture", "-particle-counters", "-location-preparation", "-location-preparation-work", "-shadow-only-mode", "-shadow-only-technical", "-renderer-local", "-particle-frames-local", "-particle-frames-diagnostics", "-particle-frames-visual", "-anomaly-visual", "-anomaly-events" })
            {
                if (Array.IndexOf(args, forbidden) >= 0)
                {
                    throw new ArgumentException("Frame timing diagnostics forbid " + forbidden + ".");
                }
            }
            int grouping = Array.IndexOf(args, "-location-batching");
            if (grouping < 0 || grouping != Array.LastIndexOf(args, "-location-batching") || grouping + 1 >= args.Length || args[grouping + 1] != "U")
            {
                throw new ArgumentException("Frame timing diagnostics require explicit original grouping U.");
            }
            int native = Array.IndexOf(args, "-instance-native-submission");
            if (native >= 0 && (native != Array.LastIndexOf(args, "-instance-native-submission") || native + 1 >= args.Length || args[native + 1] != "A"))
            {
                throw new ArgumentException("Frame timing diagnostics require original native submission A.");
            }
            return true;
        }

        public void Poll(long timestamp, int unityFrame)
        {
            if (_saved)
            {
                return;
            }
            uint count = FrameTimingManager.GetLatestTimings(LATEST_CAPACITY, _latest);
            _polls++;
            for (int i = 0; i < count; i++)
            {
                Append(_latest[i], timestamp, unityFrame);
            }
            FrameTimingManager.CaptureFrameTimings();
        }

        private void Append(FrameTiming timing, long timestamp, int unityFrame)
        {
            _publications++;
            if (timing.frameStartTimestamp == 0)
            {
                _zeroTimestamps++;
                return;
            }
            if (_indices.TryGetValue(timing.frameStartTimestamp, out int index))
            {
                _duplicates++;
                var sample = _samples[index];
                if (sample.Timing.gpuFrameTime != timing.gpuFrameTime || sample.Timing.cpuFrameTime != timing.cpuFrameTime || sample.Timing.cpuMainThreadFrameTime != timing.cpuMainThreadFrameTime || sample.Timing.cpuMainThreadPresentWaitTime != timing.cpuMainThreadPresentWaitTime || sample.Timing.cpuRenderThreadFrameTime != timing.cpuRenderThreadFrameTime || sample.Timing.firstSubmitTimestamp != timing.firstSubmitTimestamp || sample.Timing.cpuTimePresentCalled != timing.cpuTimePresentCalled || sample.Timing.cpuTimeFrameComplete != timing.cpuTimeFrameComplete)
                {
                    _changedPublications++;
                    sample.ChangedPublications++;
                }
                if (sample.Timing.gpuFrameTime > 0 && timing.gpuFrameTime == 0)
                {
                    _positiveGpuLostPublications++;
                }
                sample.Timing = timing;
                sample.LastPollTimestamp = timestamp;
                sample.LastPollUnityFrame = unityFrame;
                sample.Publications++;
                _samples[index] = sample;
                return;
            }
            if (_count == _samples.Length)
            {
                _overflow = true;
                return;
            }
            _indices.Add(timing.frameStartTimestamp, _count);
            _samples[_count++] = new Sample { Timing = timing, FirstPollTimestamp = timestamp, LastPollTimestamp = timestamp, FirstPollUnityFrame = unityFrame, LastPollUnityFrame = unityFrame, Publications = 1 };
        }

        public void Save(string directory)
        {
            if (_saved)
            {
                return;
            }
            using (var writer = new StreamWriter(Path.Combine(directory, "frame-timings.csv")))
            {
                writer.WriteLine("sample,frame_start_timestamp,first_submit_timestamp,cpu_present_called,cpu_frame_complete,first_poll_timestamp,last_poll_timestamp,first_poll_unity_frame,last_poll_unity_frame,publications,changed_publications,cpu_frame_ms,cpu_main_ms,cpu_main_present_wait_ms,cpu_render_ms,gpu_frame_ms,width_scale,height_scale,sync_interval");
                for (int i = 0; i < _count; i++)
                {
                    Sample sample = _samples[i];
                    FrameTiming timing = sample.Timing;
                    writer.WriteLine(string.Join(",", i.ToString(CultureInfo.InvariantCulture), timing.frameStartTimestamp.ToString(CultureInfo.InvariantCulture), timing.firstSubmitTimestamp.ToString(CultureInfo.InvariantCulture), timing.cpuTimePresentCalled.ToString(CultureInfo.InvariantCulture), timing.cpuTimeFrameComplete.ToString(CultureInfo.InvariantCulture), sample.FirstPollTimestamp.ToString(CultureInfo.InvariantCulture), sample.LastPollTimestamp.ToString(CultureInfo.InvariantCulture), sample.FirstPollUnityFrame.ToString(CultureInfo.InvariantCulture), sample.LastPollUnityFrame.ToString(CultureInfo.InvariantCulture), sample.Publications.ToString(CultureInfo.InvariantCulture), sample.ChangedPublications.ToString(CultureInfo.InvariantCulture), timing.cpuFrameTime.ToString("R", CultureInfo.InvariantCulture), timing.cpuMainThreadFrameTime.ToString("R", CultureInfo.InvariantCulture), timing.cpuMainThreadPresentWaitTime.ToString("R", CultureInfo.InvariantCulture), timing.cpuRenderThreadFrameTime.ToString("R", CultureInfo.InvariantCulture), timing.gpuFrameTime.ToString("R", CultureInfo.InvariantCulture), timing.widthScale.ToString("R", CultureInfo.InvariantCulture), timing.heightScale.ToString("R", CultureInfo.InvariantCulture), timing.syncInterval.ToString(CultureInfo.InvariantCulture)));
                }
            }
            ClockSample endClock = CaptureClock();
            using (var writer = new StreamWriter(Path.Combine(directory, "frame-timing-clock-sync.csv")))
            {
                writer.WriteLine("phase,stopwatch_before,native_qpc,stopwatch_after,native_qpc_frequency,stopwatch_frequency,native_qpc_available");
                WriteClock(writer, "start", _startClock);
                WriteClock(writer, "end", endClock);
            }
            var evidence = new Evidence { instrumented = true, eligibleForFpsComparison = false, measurementClass = "frame-timing-diagnostic", featureEnabled = _featureEnabled, cpuTimerFrequency = _cpuTimerFrequency, gpuTimerFrequency = _gpuTimerFrequency, stopwatchFrequency = Stopwatch.Frequency, cpuClockFrequencyMatchesStopwatch = _cpuTimerFrequency != 0 && _cpuTimerFrequency == (ulong)Stopwatch.Frequency, nativeQpcAvailable = _startClock.Available && endClock.Available, nativeQpcFrequency = _startClock.Frequency, capacity = _samples.Length, latestCapacity = LATEST_CAPACITY, polls = _polls, publications = _publications, uniqueSamples = _count, duplicatePublications = _duplicates, changedPublications = _changedPublications, positiveGpuReplacedByZeroPublications = _positiveGpuLostPublications, zeroTimestampPublications = _zeroTimestamps, overflow = _overflow, documentedDelayFrames = 4, delayedTailDrained = false, scope = "Raw delayed FrameTimingManager publications; poll Unity frame is not the source Unity frame. Equal clock frequencies do not establish a common epoch; use retained native QPC/Stopwatch brackets and verify the FrameTimingManager timestamp domain before joining source windows. CPU durations are elapsed thread/frame timings, not OS running time. Overlapping CPU/GPU timings must not be added. Zero or nonfinite metrics are unavailable. Immediate route completion can omit the delayed final frames." };
            File.WriteAllText(Path.Combine(directory, "frame-timing-diagnostic.json"), JsonUtility.ToJson(evidence, true));
            _saved = true;
        }

        private static ClockSample CaptureClock()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer)
            {
                return default;
            }
            bool frequencyAvailable = QueryPerformanceFrequency(out long frequency);
            long before = Stopwatch.GetTimestamp();
            bool timestampAvailable = QueryPerformanceCounter(out long timestamp);
            long after = Stopwatch.GetTimestamp();
            return new ClockSample { StopwatchBefore = before, NativeQpc = timestamp, StopwatchAfter = after, Frequency = frequency, Available = frequencyAvailable && timestampAvailable };
        }

        private static void WriteClock(StreamWriter writer, string phase, ClockSample sample)
        {
            writer.WriteLine(string.Join(",", phase, sample.StopwatchBefore.ToString(CultureInfo.InvariantCulture), sample.NativeQpc.ToString(CultureInfo.InvariantCulture), sample.StopwatchAfter.ToString(CultureInfo.InvariantCulture), sample.Frequency.ToString(CultureInfo.InvariantCulture), Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture), sample.Available ? "1" : "0"));
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceCounter(out long timestamp);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceFrequency(out long frequency);

        private struct ClockSample
        {
            public long StopwatchBefore;
            public long NativeQpc;
            public long StopwatchAfter;
            public long Frequency;
            public bool Available;
        }

        private struct Sample
        {
            public FrameTiming Timing;
            public long FirstPollTimestamp;
            public long LastPollTimestamp;
            public int FirstPollUnityFrame;
            public int LastPollUnityFrame;
            public int Publications;
            public int ChangedPublications;
        }

        [Serializable]
        private sealed class Evidence
        {
            public bool instrumented;
            public bool eligibleForFpsComparison;
            public string measurementClass;
            public bool featureEnabled;
            public ulong cpuTimerFrequency;
            public ulong gpuTimerFrequency;
            public long stopwatchFrequency;
            public bool cpuClockFrequencyMatchesStopwatch;
            public bool nativeQpcAvailable;
            public long nativeQpcFrequency;
            public int capacity;
            public int latestCapacity;
            public int polls;
            public int publications;
            public int uniqueSamples;
            public int duplicatePublications;
            public int changedPublications;
            public int positiveGpuReplacedByZeroPublications;
            public int zeroTimestampPublications;
            public bool overflow;
            public int documentedDelayFrames;
            public bool delayedTailDrained;
            public string scope;
        }
    }
}
#endif
