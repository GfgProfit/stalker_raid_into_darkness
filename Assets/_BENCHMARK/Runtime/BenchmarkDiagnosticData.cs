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
        public List<BenchmarkDiagnosticWindow> windows = new List<BenchmarkDiagnosticWindow>();
        public List<BenchmarkGpuObservation> observations = new List<BenchmarkGpuObservation>();
    }

    [Serializable]
    public sealed class BenchmarkDiagnosticWindow
    {
        public int position;
        public string window;
        public long callCount;
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
    public sealed class BenchmarkDiagnosticDocument
    {
        public int schemaVersion = 2;
        public string runId;
        public string signature;
        public string status;
        public List<string> limitations = new List<string>();
        public List<BenchmarkDiagnosticMarker> markers = new List<BenchmarkDiagnosticMarker>();
        public List<BenchmarkDiagnosticSnapshot> snapshots = new List<BenchmarkDiagnosticSnapshot>();
    }
}
