#if UNITY_EDITOR || CLEAN_RELEASE_ROUTE07
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Rvm.CleanReleaseRoute07
{
    public sealed class ReleaseCpuDiagnosticSession : IDisposable
    {
        private const int MAX_CAPACITY = 32768;
        private const int MAX_MARKERS = 32;
        private const int MAX_CATALOG = 16384;
        private const uint THREAD_QUERY_LIMITED_INFORMATION = 0x0800;
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";

        private readonly Sample[] _samples;
        private readonly Selection[] _selections;
        private readonly ProfilerRecorder[] _recorders;
        private readonly ProfilerRecorderHandle[] _boundHandles;
        private readonly long[] _values;
        private readonly int[] _publicationCounts;
        private readonly byte[] _states;
        private readonly List<string> _inventory = new List<string>(512);
        private readonly string _configJson;
        private readonly ClockSample _startClock;
        private readonly uint _threadId;
        private IntPtr _thread;
        private int _count;
        private int _invalidReads;
        private int _disposeFailures;
        private bool _overflow;
        private bool _refreshed;
        private bool _disposed;
        private bool _saved;

        public int Count => _count;
        public bool Overflow => _overflow;
        public bool Valid => _count > 1 && !_overflow && _invalidReads == 0 && _disposeFailures == 0;

        public ReleaseCpuDiagnosticSession(string configPath = null, int capacity = MAX_CAPACITY)
        {
            if (capacity <= 0 || capacity > MAX_CAPACITY) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer) throw new PlatformNotSupportedException("Release CPU diagnostics require Windows thread CPU counters.");
            _configJson = configPath != null ? File.ReadAllText(ArchivePath(configPath)) : "{\"markers\":[]}";
            Configuration configuration = JsonUtility.FromJson<Configuration>(_configJson);
            if (configuration?.markers == null) throw new ArgumentException("CPU marker configuration requires a markers array.");
            _selections = configuration.markers;
            if (_selections.Length > MAX_MARKERS) throw new ArgumentException("CPU diagnostics support at most 32 explicit marker selections.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (Selection selection in _selections)
            {
                if (selection == null || string.IsNullOrEmpty(selection.category) || string.IsNullOrEmpty(selection.name) || string.IsNullOrEmpty(selection.unit) || !keys.Add(selection.category + "\n" + selection.name + "\n" + selection.unit)) throw new ArgumentException("Marker selections require unique exact category, name and unit values.");
            }
            _samples = new Sample[capacity];
            _recorders = new ProfilerRecorder[_selections.Length];
            _boundHandles = new ProfilerRecorderHandle[_selections.Length];
            _values = new long[checked(capacity * _selections.Length)];
            _publicationCounts = new int[_values.Length];
            _states = new byte[_values.Length];
            _threadId = GetCurrentThreadId();
            _startClock = CaptureClock();
            try
            {
                _thread = OpenThread(THREAD_QUERY_LIMITED_INFORMATION, false, _threadId);
                if (_thread == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot query the current main thread CPU service time.");
                Inventory("start");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static bool Requested(string[] args)
        {
            int index = Array.IndexOf(args, "-benchmark-cpu-diagnostic");
            if (index < 0)
            {
                if (Array.IndexOf(args, "-benchmark-cpu-marker-config") >= 0) throw new ArgumentException("CPU marker configuration requires -benchmark-cpu-diagnostic.");
                return false;
            }
            if (index != Array.LastIndexOf(args, "-benchmark-cpu-diagnostic") || Array.IndexOf(args, "-clean-route") < 0 || Array.IndexOf(args, "-clean-route") != Array.LastIndexOf(args, "-clean-route")) throw new ArgumentException("CPU diagnostics require exactly one explicit timing route and diagnostic flag.");
            foreach (string forbidden in new[] { "-clean-route-config-check", "-clean-route-no-timing", "-benchmark-pix-diagnostic", "-benchmark-frame-timing-diagnostic", "-punctual-shadow-mode", "-punctual-shadow-technical", "-punctual-shadow-capture", "-batching-counters", "-batching-capture", "-preparation-counters", "-preparation-capture", "-particle-counters", "-location-preparation", "-location-preparation-work", "-shadow-only-mode", "-shadow-only-technical", "-renderer-local", "-particle-frames-local", "-particle-frames-diagnostics", "-particle-frames-visual", "-anomaly-visual", "-anomaly-events" })
            {
                if (Array.IndexOf(args, forbidden) >= 0) throw new ArgumentException("CPU diagnostics forbid " + forbidden + ".");
            }
            int grouping = Array.IndexOf(args, "-location-batching");
            if (grouping < 0 || grouping != Array.LastIndexOf(args, "-location-batching") || grouping + 1 >= args.Length || args[grouping + 1] != "U") throw new ArgumentException("CPU diagnostics require explicit original grouping U.");
            int config = Array.IndexOf(args, "-benchmark-cpu-marker-config");
            if (config >= 0 && (config != Array.LastIndexOf(args, "-benchmark-cpu-marker-config") || config + 1 >= args.Length || args[config + 1].StartsWith("-", StringComparison.Ordinal))) throw new ArgumentException("CPU diagnostics require one marker configuration path.");
            int native = Array.IndexOf(args, "-instance-native-submission");
            if (native >= 0 && (native != Array.LastIndexOf(args, "-instance-native-submission") || native + 1 >= args.Length || args[native + 1] != "A" && args[native + 1] != "B")) throw new ArgumentException("CPU diagnostics require one native submission value A or B.");
            return true;
        }

        public void RefreshInventory()
        {
            if (_disposed || _refreshed) return;
            _refreshed = true;
            Inventory("after-warmup");
        }

        private void Inventory(string phase)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            if (handles.Count > MAX_CATALOG) { _overflow = true; return; }
            var unique = new HashSet<ProfilerRecorderHandle>();
            var descriptions = new List<ProfilerRecorderDescription>(handles.Count);
            var available = new List<ProfilerRecorderHandle>(handles.Count);
            foreach (ProfilerRecorderHandle handle in handles)
            {
                if (!unique.Add(handle)) continue;
                ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
                available.Add(handle);
                descriptions.Add(description);
                _inventory.Add(string.Join(",", phase, Stopwatch.GetTimestamp().ToString(CultureInfo.InvariantCulture), Csv(description.Category.Name), Csv(description.Name), description.UnitType, description.DataType, (int)description.Flags));
            }
            for (int marker = 0; marker < _selections.Length; marker++)
            {
                Selection selection = _selections[marker];
                int match = -1;
                int matches = 0;
                for (int index = 0; index < descriptions.Count; index++)
                {
                    ProfilerRecorderDescription description = descriptions[index];
                    if (description.Category.Name != selection.category || description.Name != selection.name || description.UnitType.ToString() != selection.unit) continue;
                    match = index;
                    matches++;
                }
                bool supported = matches == 1 && (descriptions[match].DataType == ProfilerMarkerDataType.Int64 || descriptions[match].DataType == ProfilerMarkerDataType.Int32 || descriptions[match].DataType == ProfilerMarkerDataType.UInt32) && (descriptions[match].Flags & MarkerFlags.SampleGPU) == 0;
                if (_recorders[marker].Valid && supported && available[match].Equals(_boundHandles[marker])) continue;
                if (_recorders[marker].Valid) _recorders[marker].Dispose();
                _recorders[marker] = default;
                if (!supported) continue;
                var options = ProfilerRecorderOptions.StartImmediately;
                if (descriptions[match].UnitType == ProfilerMarkerDataUnit.TimeNanoseconds) options |= ProfilerRecorderOptions.SumAllSamplesInFrame;
                _boundHandles[marker] = available[match];
                _recorders[marker] = new ProfilerRecorder(available[match], MAX_CAPACITY, options);
            }
        }

        public void Poll(long timestamp, int unityFrame, int position)
        {
            if (_disposed || _saved) return;
            if (_count == _samples.Length) { _overflow = true; return; }
            long before = Stopwatch.GetTimestamp();
            uint thread = GetCurrentThreadId();
            bool read = GetThreadTimes(_thread, out _, out _, out long kernel, out long user);
            long after = Stopwatch.GetTimestamp();
            bool valid = read && thread == _threadId && kernel >= 0 && user >= 0 && before >= timestamp && after >= before && (_count == 0 || timestamp > _samples[_count - 1].Timestamp && kernel >= _samples[_count - 1].Kernel && user >= _samples[_count - 1].User);
            if (!valid) _invalidReads++;
            _samples[_count] = new Sample { Timestamp = timestamp, Before = before, After = after, UnityFrame = unityFrame, Position = position, Thread = thread, Kernel = kernel, User = user, Valid = valid, Error = read ? 0 : Marshal.GetLastWin32Error() };
            for (int marker = 0; marker < _recorders.Length; marker++)
            {
                int destination = _count * _recorders.Length + marker;
                if (!_recorders[marker].Valid) continue;
                int count = _recorders[marker].Count;
                if (count == 0) { _states[destination] = 1; continue; }
                _values[destination] = _recorders[marker].LastValue;
                int complete = _recorders[marker].Count;
                _publicationCounts[destination] = complete;
                _states[destination] = count == complete ? (byte)2 : (byte)3;
                if (complete >= MAX_CAPACITY) { _states[destination] = 4; _overflow = true; }
            }
            _count++;
        }

        public static bool TryCpuMilliseconds(long previousKernel100ns, long previousUser100ns, long kernel100ns, long user100ns, long previousTimestamp, long timestamp, out double value)
        {
            value = double.NaN;
            if (previousKernel100ns < 0 || previousUser100ns < 0 || kernel100ns < previousKernel100ns || user100ns < previousUser100ns || previousTimestamp <= 0 || timestamp <= previousTimestamp) return false;
            value = (kernel100ns - previousKernel100ns) * 0.0001 + (user100ns - previousUser100ns) * 0.0001;
            return true;
        }

        public static bool TryClockOffsetBounds(long stopwatchBefore, long nativeQpc, long stopwatchAfter, long nativeFrequency, long stopwatchFrequency, out double minimum, out double maximum)
        {
            minimum = double.NaN;
            maximum = double.NaN;
            if (stopwatchBefore < 0 || stopwatchAfter < stopwatchBefore || nativeQpc <= 0 || nativeFrequency <= 0 || stopwatchFrequency <= 0) return false;
            minimum = nativeQpc / (double)nativeFrequency - stopwatchAfter / (double)stopwatchFrequency;
            maximum = nativeQpc / (double)nativeFrequency - stopwatchBefore / (double)stopwatchFrequency;
            return true;
        }

        public static double MarkerMilliseconds(long raw, string unit)
        {
            return raw >= 0 && unit == nameof(ProfilerMarkerDataUnit.TimeNanoseconds) ? raw * 0.000001 : double.NaN;
        }

        public void Save(string directory)
        {
            if (_saved) return;
            directory = ArchivePath(directory);
            Dispose();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "cpu-marker-config.json"), _configJson);
            using (var writer = new StreamWriter(Path.Combine(directory, "cpu-marker-inventory.csv")))
            {
                writer.WriteLine("phase,poll_timestamp,category,name,unit,data_type,flags");
                foreach (string row in _inventory) writer.WriteLine(row);
            }
            using (var writer = new StreamWriter(Path.Combine(directory, "cpu-thread-samples.csv")))
            {
                writer.WriteLine("sample,poll_timestamp,stopwatch_before,stopwatch_after,poll_unity_frame,position,os_thread_id,kernel_100ns,user_100ns,valid,win32_error");
                for (int index = 0; index < _count; index++)
                {
                    Sample sample = _samples[index];
                    writer.WriteLine(string.Join(",", index, sample.Timestamp, sample.Before, sample.After, sample.UnityFrame, sample.Position, sample.Thread, sample.Kernel, sample.User, sample.Valid ? 1 : 0, sample.Error));
                }
            }
            using (var writer = new StreamWriter(Path.Combine(directory, "cpu-marker-samples.csv")))
            {
                writer.WriteLine("sample,marker,poll_timestamp,poll_unity_frame,position,publication_count,last_value_raw,state");
                for (int index = 0; index < _count; index++)
                {
                    for (int marker = 0; marker < _recorders.Length; marker++)
                    {
                        int offset = index * _recorders.Length + marker;
                        writer.WriteLine(string.Join(",", index, marker, _samples[index].Timestamp, _samples[index].UnityFrame, _samples[index].Position, _publicationCounts[offset], _values[offset], _states[offset]));
                    }
                }
            }
            var evidence = new Evidence { instrumented = true, eligibleForFpsComparison = false, measurementClass = "release-cpu-diagnostic", valid = Valid, capacity = _samples.Length, samples = _count, markerSelections = _selections.Length, overflow = _overflow, invalidReads = _invalidReads, disposeFailures = _disposeFailures, inventoryRefreshedAfterWarmup = _refreshed, stopwatchFrequency = Stopwatch.Frequency, mainThreadId = _threadId, startClock = _startClock, endClock = CaptureClock(), scope = "OS kernel/user cumulative thread CPU service counters in 100ns units; their deltas are not elapsed wall time. Wall minus CPU is not a separation of ready/preempted/blocked waits. Counters are polled in Update and include diagnostic overhead. ProfilerRecorder LastValue is a polled completed aggregate, can cover other threads and has unknown source frame; publication count must be examined and held values must not be summed as new samples. Marker state: 0 unbound/unsupported/ambiguous, 1 no publication, 2 stable count across read, 3 concurrent publication, 4 capacity exhausted. No ring overwrite. No CPU stacks, GPU queues, self time or exclusive additive frame budget is captured. Inventory itself is refreshed once after warmup and can perturb the transition frame." };
            File.WriteAllText(Path.Combine(directory, "release-cpu-diagnostic.json"), JsonUtility.ToJson(evidence, true));
            _saved = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int index = 0; index < _recorders.Length; index++)
            {
                try
                {
                    if (_recorders[index].Valid) _recorders[index].Dispose();
                }
                catch
                {
                    _disposeFailures++;
                }
            }
            if (_thread != IntPtr.Zero && !CloseHandle(_thread)) _disposeFailures++;
            _thread = IntPtr.Zero;
            if (_disposeFailures != 0) Debug.LogError("Release CPU diagnostic resource disposal failed: " + _disposeFailures);
        }

        private static string ArchivePath(string path)
        {
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("CPU diagnostic files must remain inside Archive.");
            return full;
        }

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static ClockSample CaptureClock()
        {
            bool frequency = QueryPerformanceFrequency(out long hertz);
            long before = Stopwatch.GetTimestamp();
            bool available = QueryPerformanceCounter(out long timestamp);
            long after = Stopwatch.GetTimestamp();
            return new ClockSample { stopwatchBefore = before, nativeQpc = timestamp, stopwatchAfter = after, nativeQpcFrequency = hertz, available = frequency && available, utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) };
        }

        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint thread);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryPerformanceCounter(out long timestamp);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryPerformanceFrequency(out long frequency);

        private struct Sample
        {
            public long Timestamp;
            public long Before;
            public long After;
            public long Kernel;
            public long User;
            public int UnityFrame;
            public int Position;
            public uint Thread;
            public int Error;
            public bool Valid;
        }

        [Serializable]
        private sealed class Configuration
        {
            public Selection[] markers;
        }

        [Serializable]
        private sealed class Selection
        {
            public string category;
            public string name;
            public string unit;
        }

        [Serializable]
        private struct ClockSample
        {
            public long stopwatchBefore;
            public long nativeQpc;
            public long stopwatchAfter;
            public long nativeQpcFrequency;
            public bool available;
            public string utc;
        }

        [Serializable]
        private sealed class Evidence
        {
            public bool instrumented;
            public bool eligibleForFpsComparison;
            public string measurementClass;
            public bool valid;
            public int capacity;
            public int samples;
            public int markerSelections;
            public bool overflow;
            public int invalidReads;
            public int disposeFailures;
            public bool inventoryRefreshedAfterWarmup;
            public long stopwatchFrequency;
            public uint mainThreadId;
            public ClockSample startClock;
            public ClockSample endClock;
            public string scope;
        }
    }
}
#endif
