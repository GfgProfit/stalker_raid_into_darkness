using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using XRay.Importer.Levels;

namespace Rvm.Benchmark
{
    public sealed class LocationPreparationBenchmark : MonoBehaviour
    {
        private const int ITERATIONS = 256;

        private static readonly FieldInfo VERSION = typeof(XRayInstanceBatchSet).GetField("_matrixPreparationVersion", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo HISTORY = typeof(XRayLevelInstancedRenderer).GetMethod("History", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo EXPOSED = typeof(XRayInstanceBatch).GetField("_matricesExposed", BindingFlags.Instance | BindingFlags.NonPublic);

        private XRayLevelInstancedRenderer[] _renderers;
        private string _output;
        private double _start;
        private bool _work;
        private bool _capture;
        private bool _hasPose;
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private int _pose;
        private double _poseChangedAt;
        private bool _posePending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Application.isEditor || Array.IndexOf(args, "-location-preparation") < 0 && Array.IndexOf(args, "-location-preparation-work") < 0)
            {
                return;
            }
            string mode = Argument(args, "-location-preparation", "B");
            if (mode != "A" && mode != "B")
            {
                throw new ArgumentException("-location-preparation requires A or B.");
            }
            string output = BenchmarkOutput.ValidateDirectory(Argument(args, "-preparation-output", Argument(args, "-clean-route-output", null)));
            Directory.CreateDirectory(output);
            var renderers = FindObjectsByType<XRayLevelInstancedRenderer>(FindObjectsInactive.Include);
            var sets = new HashSet<XRayInstanceBatchSet>();
            var evidence = new ModeEvidence { mode = mode, unity = Application.unityVersion, debugBuild = UnityEngine.Debug.isDebugBuild, renderers = renderers.Length, cameraCountAtStartup = Camera.allCamerasCount };
            foreach (var renderer in renderers)
            {
                var set = renderer.BatchSet;
                if (set == null || !sets.Add(set))
                {
                    continue;
                }
                if (set.MatrixPreparationVersion != XRayInstanceBatchSet.MATRIX_PREPARATION_VERSION)
                {
                    throw new InvalidOperationException("Unprepared location in measurement: " + set.name);
                }
                evidence.sets.Add(new SetEvidence { location = set.LevelName, signature = set.MatrixInputSignature, version = set.MatrixPreparationVersion, batches = set.Batches.Count, instances = set.InstanceCount });
                if (mode == "A")
                {
                    VERSION.SetValue(set, 0);
                }
            }
            BenchmarkOutput.WriteJson(Path.Combine(output, "preparation-mode.json"), evidence);
            bool work = Array.IndexOf(args, "-location-preparation-work") >= 0;
            bool capture = Array.IndexOf(args, "-preparation-capture") >= 0;
            bool counters = capture || Array.IndexOf(args, "-preparation-counters") >= 0;
            if (work || counters)
            {
                var observer = new GameObject("Benchmark Matrix Preparation").AddComponent<LocationPreparationBenchmark>();
                observer._renderers = renderers;
                observer._output = output;
                observer._start = Time.realtimeSinceStartupAsDouble;
                observer._work = work;
                observer._capture = capture;
                if (counters)
                {
                    RenderPipelineManager.endCameraRendering += observer.Capture;
                }
            }
        }

        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= Capture;
        }

        private void Capture(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game)
            {
                return;
            }
            if (!_hasPose || camera.transform.position != _lastPosition || camera.transform.rotation != _lastRotation)
            {
                _lastPosition = camera.transform.position;
                _lastRotation = camera.transform.rotation;
                _hasPose = true;
                _poseChangedAt = Time.realtimeSinceStartupAsDouble;
                _posePending = true;
            }
            if (!_posePending || Time.realtimeSinceStartupAsDouble - _poseChangedAt < 1)
            {
                return;
            }
            _posePending = false;
            var report = new PoseEvidence { position = _lastPosition, rotation = _lastRotation, camera = camera.name, unityFrame = Time.frameCount, secondsAfterPoseChange = Time.realtimeSinceStartupAsDouble - _poseChangedAt, managedBytes = GC.GetTotalMemory(false), allocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), reservedBytes = Profiler.GetTotalReservedMemoryLong() };
            foreach (var renderer in _renderers)
            {
                report.visibleBatches += renderer.LastVisibleBatches;
                report.visibleInstances += renderer.LastVisibleInstances;
                report.shadowOnlyBatches += renderer.LastShadowOnlyBatches;
                report.rejectedShadowBatches += renderer.LastRejectedShadowBatches;
            }
            string prefix = Path.Combine(_output, "pose" + _pose++);
            BenchmarkOutput.WriteJson(prefix + ".json", report);
            if (_capture)
            {
                ScreenCapture.CaptureScreenshot(prefix + ".png");
            }
        }

        private void Update()
        {
            if (!_work || Time.realtimeSinceStartupAsDouble - _start < 8)
            {
                return;
            }
            enabled = false;
            try
            {
                var results = new WorkEvidence { allocationCounter = AllocationCounterControl.Measure() };
                foreach (var renderer in _renderers)
                {
                    if (renderer.BatchSet == null || !renderer.isActiveAndEnabled)
                    {
                        continue;
                    }
                    var index = Expression.Parameter(typeof(int));
                    var batch = Expression.Parameter(typeof(XRayInstanceBatch));
                    var call = Expression.Call(Expression.Constant(renderer), HISTORY, index, batch);
                    var observe = Expression.Lambda<Func<int, XRayInstanceBatch, object>>(Expression.Convert(call, typeof(object)), index, batch).Compile();
                    results.samples.Add(Measure(renderer, observe, "A1", false));
                    results.samples.Add(Measure(renderer, observe, "B1", true));
                    results.samples.Add(Measure(renderer, observe, "B2", true));
                    results.samples.Add(Measure(renderer, observe, "A2", false));
                    VERSION.SetValue(renderer.BatchSet, XRayInstanceBatchSet.MATRIX_PREPARATION_VERSION);
                }
                results.routeOverhead = RouteOverheadBenchmark.Measure();
                BenchmarkOutput.WriteJson(Path.Combine(_output, "history-work.json"), results);
                Application.Quit(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(_output, "history-work-error.txt"), exception.ToString());
                Application.Quit(2);
            }
        }

        private static WorkSample Measure(XRayLevelInstancedRenderer renderer, Func<int, XRayInstanceBatch, object> observe, string name, bool prepared)
        {
            var set = renderer.BatchSet;
            VERSION.SetValue(set, prepared ? XRayInstanceBatchSet.MATRIX_PREPARATION_VERSION : 0);
            int matrixUpdates = 0;
            foreach (var batch in set.Batches)
            {
                matrixUpdates += !prepared || (bool)EXPOSED.GetValue(batch) ? batch.Count : 0;
            }
            for (int iteration = 0; iteration < 16; iteration++)
            {
                for (int index = 0; index < set.Batches.Count; index++) observe(index, set.Batches[index]);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            object last = null;
            for (int iteration = 0; iteration < ITERATIONS; iteration++)
            {
                for (int index = 0; index < set.Batches.Count; index++) last = observe(index, set.Batches[index]);
            }
            double milliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            GC.KeepAlive(last);
            return new WorkSample { sample = name, location = set.LevelName, iterations = ITERATIONS, batchesPerIteration = set.Batches.Count, sourceInstances = set.InstanceCount, matrixUpdatesPerIteration = matrixUpdates, milliseconds = milliseconds, microsecondsPerIteration = milliseconds * 1000.0 / ITERATIONS, allocatedBytes = allocated };
        }

        private static string Argument(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        [Serializable]
        private sealed class ModeEvidence
        {
            public string mode;
            public string unity;
            public bool debugBuild;
            public int renderers;
            public int cameraCountAtStartup;
            public List<SetEvidence> sets = new List<SetEvidence>();
        }

        [Serializable]
        private sealed class SetEvidence
        {
            public string location;
            public string signature;
            public int version;
            public int batches;
            public int instances;
        }

        [Serializable]
        private sealed class WorkEvidence
        {
            public string scope = "Warmed local History pass microbenchmark on location data at a fixed Unity frame number. Compiled delegates included; expression compilation excluded. Not inclusive MatrixHistory CPU time in a real frame; excludes culling, submissions and GPU. Allocation deltas are evidence only if the same-thread positive control is valid.";
            public AllocationCounterControl.Result allocationCounter;
            public List<WorkSample> samples = new List<WorkSample>();
            public List<RouteOverheadBenchmark.Sample> routeOverhead = new List<RouteOverheadBenchmark.Sample>();
        }

        [Serializable]
        private sealed class PoseEvidence
        {
            public Vector3 position;
            public Quaternion rotation;
            public string camera;
            public int unityFrame;
            public double secondsAfterPoseChange;
            public int visibleBatches;
            public int visibleInstances;
            public int shadowOnlyBatches;
            public int rejectedShadowBatches;
            public long managedBytes;
            public long allocatedBytes;
            public long reservedBytes;
        }

        [Serializable]
        private sealed class WorkSample
        {
            public string sample;
            public string location;
            public int iterations;
            public int batchesPerIteration;
            public int sourceInstances;
            public int matrixUpdatesPerIteration;
            public double milliseconds;
            public double microsecondsPerIteration;
            public long allocatedBytes;
        }
    }
}
