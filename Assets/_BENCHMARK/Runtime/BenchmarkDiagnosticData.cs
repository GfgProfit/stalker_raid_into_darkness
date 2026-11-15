using System;
using System.Collections.Generic;

namespace Rvm.Benchmark
{
    [Serializable]
    public sealed class BenchmarkDiagnosticMarker
    {
        public string name;
        public string category;
        public string unit;
        public string scope;
        public string reason;
        public bool available;
        public bool selfTimeAvailable;
        public BenchmarkSourceIdentity identity;
        public bool sourceFound;
        public int samplesSeen;
        public int validSamples;
        public int invalidSamples;
        public string validityStatus;
        public List<BenchmarkSampleRead> rawEvidence = new List<BenchmarkSampleRead>();
        public List<BenchmarkDiagnosticWindow> windows = new List<BenchmarkDiagnosticWindow>();
        public List<BenchmarkGpuObservation> observations = new List<BenchmarkGpuObservation>();
    }

    [Serializable]
    public sealed class BenchmarkDiagnosticWindow
    {
        public int position;
        public string window;
        public long callCount;
        public int contextRows;
        public BenchmarkDistribution inclusive;
    }

    [Serializable]
    public sealed class BenchmarkGpuObservation
    {
        public int observedUnityFrame;
        public int observedPosition;
        public int sourceUnityFrame = -1;
        public int sourcePosition = -1;
        public double milliseconds;
        public long calls;
    }

    [Serializable]
    public sealed class BenchmarkDiagnosticSnapshot
    {
        public int unityFrame;
        public int position;
        public bool transition;
        public double elapsedSeconds;
        public double collectionMicroseconds;
        public List<BenchmarkEntry> entries = new List<BenchmarkEntry>();
    }

    [Serializable]
    public sealed class BenchmarkPhysicalSourceEvidence
    {
        public string role;
        public string logicalKey;
        public int physicalOrdinal;
        public BenchmarkSourceIdentity identity;
        public List<BenchmarkSampleRead> rawEvidence;
    }

    [Serializable]
    public sealed class BenchmarkDiagnosticDocument
    {
        public int schemaVersion = 3;
        public int measurementVersion = 3;
        public long qpcFrequency = BenchmarkClock.Frequency;
        public List<BenchmarkPhysicalSourceEvidence> physicalAliases = new List<BenchmarkPhysicalSourceEvidence>();
        public int discoveredPhysicalSources;
        public int selectedUniqueSources;
        public string[] rawStatusLegend = Enum.GetNames(typeof(BenchmarkSampleStatus));
        public string runId;
        public string signature;
        public string status;
        public List<string> limitations = new List<string>();
        public List<BenchmarkDiagnosticMarker> markers = new List<BenchmarkDiagnosticMarker>();
        public List<BenchmarkDiagnosticSnapshot> snapshots = new List<BenchmarkDiagnosticSnapshot>();
    }
}
