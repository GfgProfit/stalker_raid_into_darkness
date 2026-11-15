using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rvm.Benchmark
{
    [Serializable]
    public sealed class BenchmarkCollectorEvidence
    {
        public int measurementVersion = 3;
        public string sampling = "Bounded stratified raw publications; at most 128 evidence rows per source, every eighth context row plus initial rows. Native source frame unknown; no hold-last.";
        public List<BenchmarkCollectorEvidenceSource> sources = new List<BenchmarkCollectorEvidenceSource>();
    }

    [Serializable]
    public sealed class BenchmarkCollectorEvidenceSource
    {
        public string id;
        public BenchmarkSourceIdentity identity;
        public List<BenchmarkSampleRead> rawEvidence;
    }

    [Serializable]
    public sealed class BenchmarkEntry
    {
        public string key;
        public string value;
    }

    public interface IBenchmarkMetadataProvider
    {
        void CollectBenchmarkMetadata(List<BenchmarkEntry> entries);
    }

    [Serializable]
    public sealed class BenchmarkPose
    {
        public string key;
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    public sealed class BenchmarkMetadata
    {
        public int schemaVersion = 1;
        public string runId;
        public string seriesId;
        public int repetition;
        public string startedUtc;
        public string finishedUtc;
        public string scenario;
        public string scene;
        public string mode;
        public string execution;
        public string gitSha = "unavailable";
        public string gitDirty = "unavailable";
        public string unity;
        public string hdrp = "unavailable";
        public string graphicsApi;
        public string os;
        public string cpu;
        public string gpu;
        public int ramMb;
        public int gpuRamMb;
        public int width;
        public int height;
        public int cameraPixelWidth;
        public int cameraPixelHeight;
        public string quality;
        public int vsync;
        public int targetFrameRate;
        public string fullscreen;
        public float widthScale;
        public float heightScale;
        public string cameraSettings;
        public string renderPipeline;
        public string[] tags;
        public BenchmarkPose[] positions;
        public double positionSeconds;
        public double warmupSeconds;
        public double stabilizationSeconds;
        public bool profilerCapture;
        public bool profilerWasEnabled;
        public int runtimeErrors;
        public string frameTimingAlignment;
        public int metadataVersion;
        public int measurementVersion;
        public string conditionProtocol;
        public string conditionEvidence;
        public string buildSourceDigest;
        public string renderSettingsDigest;
        public BenchmarkBuildIdentityEvidence buildIdentity;
        public bool shadowTrace;
        public int renderedMeasurementFrames;
        public double captureSeconds;
        public int captureStartUnityFrame = -1;
        public int captureEndUnityFrame = -1;
        public int vSyncCount;
        public double refreshRateHz;
        public int renderFrameInterval;
        public string instrumentationSignature;
        public string timerSignature;
        public string fixedWorldState;
        public string finalFramePacing;
        public string finalQuality;
        public List<BenchmarkEntry> environment = new List<BenchmarkEntry>();
        public List<BenchmarkEntry> endEnvironment = new List<BenchmarkEntry>();
        public List<string> warnings = new List<string>();
    }

    [Serializable]
    public sealed class BenchmarkMetric
    {
        public string id;
        public string unit;
        public string source;
        public string reason;
        public bool available;
        public BenchmarkSourceIdentity identity;
        public bool sourceFound;
        public int samplesSeen;
        public int invalidSamples;
    }

    public struct BenchmarkFrame
    {
        public int frameIndex;
        public int unityFrame;
        public int position;
        public bool transition;
        public long startTicks;
        public double timestamp;
        public double frameMs;
        public double collectorMicroseconds;
        public int gc0;
        public int gc1;
        public int gc2;
    }

    [Serializable]
    public sealed class BenchmarkDistribution
    {
        public bool available;
        public int samples;
        public double mean;
        public double min;
        public double max;
        public double p50;
        public double p90;
        public double p95;
        public double p99;
    }

    [Serializable]
    public sealed class BenchmarkFrameStats
    {
        public BenchmarkDistribution frameTime = new BenchmarkDistribution();
        public double averageFps;
        public double minFps;
        public double maxFps;
        public double low1Fps;
        public double low01Fps;
        public bool low1Available;
        public bool low01Available;
    }

    [Serializable]
    public sealed class BenchmarkMetricStats
    {
        public string id;
        public string unit;
        public string reason;
        public BenchmarkDistribution values;
    }

    [Serializable]
    public sealed class BenchmarkWindow
    {
        public int position;
        public string window;
        public BenchmarkFrameStats frames;
        public List<BenchmarkMetricStats> metrics = new List<BenchmarkMetricStats>();
        public double measuredSeconds;
        public int gc0;
        public int gc1;
        public int gc2;
    }

    [Serializable]
    public sealed class BenchmarkSummary
    {
        public int schemaVersion = 1;
        public string runId;
        public string status;
        public string error;
        public double plannedSeconds;
        public double measuredSeconds;
        public List<BenchmarkMetric> availability = new List<BenchmarkMetric>();
        public List<BenchmarkWindow> windows = new List<BenchmarkWindow>();
        public List<string> warnings = new List<string>();
    }
}
