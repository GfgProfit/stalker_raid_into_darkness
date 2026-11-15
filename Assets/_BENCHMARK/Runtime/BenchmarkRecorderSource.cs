using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

namespace Rvm.Benchmark
{
    public enum BenchmarkSampleStatus : byte
    {
        NoPublication,
        Valid,
        UnsupportedType,
        InvalidValue,
        AmbiguousInterval,
        AmbiguousIdentity,
        Quarantined,
        CapacityExceeded,
        ConcurrentPublication
    }

    public enum BenchmarkSampleKind
    {
        InclusiveTime,
        InstantaneousGauge,
        PublishedFrameTotal,
        DeviceCapacity,
        DelayedGpuTime,
        EventSum
    }

    [Serializable]
    public sealed class BenchmarkSourceIdentity
    {
        public string key;
        public string name;
        public string category;
        public string unit;
        public string dataType;
        public string flags;
        public int flagsValue;
        public int physicalSources;
        public string kind;
        public string aggregation;
    }

    [Serializable]
    public struct BenchmarkSampleRead
    {
        public BenchmarkSampleStatus status;
        public double Value { get; set; }
        public long firstRaw;
        public long lastRaw;
        public long rawPublicationSum;
        public bool rawSumAvailable;
        public long calls;
        public int publications;
        public int observedUnityFrame;
        public int sourceUnityFrame;
        public int contextRow;
        public int contextPosition;
        public int intervalStartUnityFrame;
        public long observedQpcTicks;
    }

    public sealed class BenchmarkRecorderSource : IDisposable
    {
        private readonly BenchmarkSampleKind _kind;
        private readonly bool _quarantined;
        private readonly bool _supported;
        private readonly bool _ambiguous;
        private readonly double _scale;
        private ProfilerRecorder _recorder;
        private int _consumed;
        private bool _disposed;

        public BenchmarkSourceIdentity Identity { get; }
        public List<BenchmarkSampleRead> Evidence { get; } = new List<BenchmarkSampleRead>(128);
        public bool Valid => !_disposed && _recorder.Valid;
        public bool IsRunning => Valid && _recorder.IsRunning;
        public int PublicationsSeen { get; private set; }
        public int InvalidReads { get; private set; }

        public BenchmarkRecorderSource(ProfilerRecorderHandle handle, ProfilerRecorderDescription description, int physicalSources, int capacity, bool gpu = false)
        {
            _kind = Classify(description.Name, description.UnitType, description.Flags, gpu);
            _quarantined = description.Category.Name == "Jobs" && description.Name == "WaitForCompleted";
            _supported = description.DataType == ProfilerMarkerDataType.Int64 || description.DataType == ProfilerMarkerDataType.Int32 || description.DataType == ProfilerMarkerDataType.UInt32;
            _ambiguous = physicalSources > 1;
            _scale = description.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds ? 0.000001 : 1;
            Identity = new BenchmarkSourceIdentity
            {
                key = Key(description),
                name = description.Name,
                category = description.Category.Name,
                unit = description.UnitType.ToString(),
                dataType = description.DataType.ToString(),
                flags = description.Flags.ToString(),
                flagsValue = (int)description.Flags,
                physicalSources = physicalSources,
                kind = _kind.ToString(),
                aggregation = _kind == BenchmarkSampleKind.InclusiveTime || _kind == BenchmarkSampleKind.EventSum || gpu ? "native SumAllSamplesInFrame; one new completed publication per associated interval; calls are not divided out" : "last newly published value; no consumer summation; no hold-last on missing updates"
            };
            var options = ProfilerRecorderOptions.StartImmediately;
            if (_kind == BenchmarkSampleKind.InclusiveTime || _kind == BenchmarkSampleKind.EventSum || gpu)
            {
                options |= ProfilerRecorderOptions.SumAllSamplesInFrame;
            }
            if (gpu)
            {
                options |= ProfilerRecorderOptions.GpuRecorder;
            }
            _recorder = new ProfilerRecorder(handle, capacity, options);
        }

        public void Arm()
        {
            if (Valid)
            {
                _consumed = _recorder.Count;
            }
        }

        public BenchmarkSampleRead Read(int observedUnityFrame)
        {
            var result = new BenchmarkSampleRead { status = BenchmarkSampleStatus.NoPublication, Value = double.NaN, observedUnityFrame = observedUnityFrame, sourceUnityFrame = -1 };
            if (!Valid)
            {
                return result;
            }
            int count = _recorder.Count;
            if (count >= _recorder.Capacity)
            {
                result.status = BenchmarkSampleStatus.CapacityExceeded;
                InvalidReads++;
                return result;
            }
            if (count <= _consumed)
            {
                return result;
            }
            int previous = _consumed;
            _consumed = count;
            result.publications = count - previous;
            PublicationsSeen += result.publications;
            result.firstRaw = _recorder.GetSample(previous).Value;
            var last = _recorder.GetSample(count - 1);
            result.lastRaw = last.Value;
            if (result.publications <= 64)
            {
                try
                {
                    long sum = 0;
                    for (int i = previous; i < count; i++)
                    {
                        sum = checked(sum + _recorder.GetSample(i).Value);
                    }
                    result.rawPublicationSum = sum;
                    result.rawSumAvailable = true;
                }
                catch (OverflowException)
                {
                    result.rawSumAvailable = false;
                }
            }
            result.calls = last.Count;
            result.status = Evaluate(_kind, _supported, _ambiguous, _quarantined, result.publications, result.lastRaw, result.calls);
            if (_kind == BenchmarkSampleKind.EventSum && !result.rawSumAvailable && result.status == BenchmarkSampleStatus.Valid)
            {
                result.status = BenchmarkSampleStatus.AmbiguousInterval;
            }
            if (_recorder.Count != count)
            {
                result.status = BenchmarkSampleStatus.ConcurrentPublication;
            }
            if (result.status == BenchmarkSampleStatus.Valid)
            {
                result.Value = (_kind == BenchmarkSampleKind.EventSum && result.rawSumAvailable ? result.rawPublicationSum : result.lastRaw) * _scale;
            }
            else
            {
                InvalidReads++;
            }
            return result;
        }

        public void RecordEvidence(BenchmarkSampleRead sample)
        {
            if (Evidence.Count < 128 && (sample.contextRow % 8 == 0 || Evidence.Count < 4))
            {
                sample.observedQpcTicks = BenchmarkClock.GetTimestamp();
                Evidence.Add(sample);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _recorder.Dispose();
            }
        }

        public static BenchmarkSampleStatus Evaluate(BenchmarkSampleKind kind, bool supported, bool ambiguous, bool quarantined, int publications, long value, long calls)
        {
            if (publications == 0)
            {
                return BenchmarkSampleStatus.NoPublication;
            }
            if (!supported)
            {
                return BenchmarkSampleStatus.UnsupportedType;
            }
            if (ambiguous)
            {
                return BenchmarkSampleStatus.AmbiguousIdentity;
            }
            if (quarantined)
            {
                return BenchmarkSampleStatus.Quarantined;
            }
            if (value < 0 || calls < 0)
            {
                return BenchmarkSampleStatus.InvalidValue;
            }
            if (kind == BenchmarkSampleKind.InclusiveTime && publications != 1)
            {
                return BenchmarkSampleStatus.AmbiguousInterval;
            }
            return BenchmarkSampleStatus.Valid;
        }

        public static string Reason(BenchmarkSampleStatus status)
        {
            switch (status)
            {
                case BenchmarkSampleStatus.Valid:
                    return "Fresh native publication; source Unity frame is not exposed by this API.";
                case BenchmarkSampleStatus.NoPublication:
                    return "No new native publication since the previous read; previous values are not reused.";
                case BenchmarkSampleStatus.UnsupportedType:
                    return "Native sample storage interpretation is not validated for this DataType.";
                case BenchmarkSampleStatus.AmbiguousIdentity:
                    return "Distinct native handles share the same public identity; raw representative retained, automatic interpretation excluded.";
                case BenchmarkSampleStatus.Quarantined:
                    return "Jobs/WaitForCompleted produced uptime-scale values in V2; recorder interpretation is quarantined pending native/capture cross-validation.";
                case BenchmarkSampleStatus.AmbiguousInterval:
                    return "Multiple completed timing publications arrived between reads; source-frame association is unknown.";
                case BenchmarkSampleStatus.CapacityExceeded:
                    return "Bounded native publication capacity reached; no wrapped or stale data is interpreted.";
                case BenchmarkSampleStatus.ConcurrentPublication:
                    return "Native source changed during the read; association is ambiguous.";
                default:
                    return "Negative or invalid native value/count.";
            }
        }

        public static string Key(ProfilerRecorderDescription description)
        {
            return description.Category.Name + "/" + description.Name + "/" + description.DataType + "/" + description.UnitType + "/" + (int)description.Flags;
        }

        public static List<ProfilerRecorderHandle> UniqueHandles(List<ProfilerRecorderHandle> handles)
        {
            var seen = new HashSet<ProfilerRecorderHandle>();
            var result = new List<ProfilerRecorderHandle>();
            foreach (var handle in handles)
            {
                if (seen.Add(handle))
                {
                    result.Add(handle);
                }
            }
            return result;
        }

        private static BenchmarkSampleKind Classify(string name, ProfilerMarkerDataUnit unit, MarkerFlags flags, bool gpu)
        {
            if (gpu)
            {
                return BenchmarkSampleKind.DelayedGpuTime;
            }
            if ((flags & MarkerFlags.Counter) == 0 && unit != ProfilerMarkerDataUnit.TimeNanoseconds)
            {
                return BenchmarkSampleKind.EventSum;
            }
            if (name == "Video Memory Bytes")
            {
                return BenchmarkSampleKind.DeviceCapacity;
            }
            if (name.Contains(" In Frame") || name.Contains("Draw Calls Count") || name.Contains("Batches Count") || name.Contains("Triangles Count") || name.Contains("Vertices Count") || name.Contains("SetPass Calls Count") || name.Contains("Shadow Casters Count") || name.Contains("Changes Count"))
            {
                return BenchmarkSampleKind.PublishedFrameTotal;
            }
            return unit == ProfilerMarkerDataUnit.TimeNanoseconds ? BenchmarkSampleKind.InclusiveTime : BenchmarkSampleKind.InstantaneousGauge;
        }
    }
}
