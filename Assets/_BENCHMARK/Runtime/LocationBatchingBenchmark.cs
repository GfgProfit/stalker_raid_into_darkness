using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace Rvm.Benchmark
{
    public sealed class LocationBatchingBenchmark : MonoBehaviour
    {
        private static readonly MethodInfo DRAW = typeof(XRayLevelInstancedRenderer).GetMethod("OnBeginCameraRendering", BindingFlags.Instance | BindingFlags.NonPublic);
        private static BenchmarkGroupingState _modeState;

        private readonly List<MeasuredCallback> _callbacks = new List<MeasuredCallback>();
        private XRayLevelInstancedRenderer[] _renderers;
        private string _output;
        private bool _capture;
        private bool _hasPose;
        private bool _pending;
        private Vector3 _position;
        private Quaternion _rotation;
        private double _changed;
        private double _start;
        private int _pose;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Application.isEditor || Array.IndexOf(args, "-location-batching") < 0) return;
            string mode = Argument(args, "-location-batching");
            if (Array.IndexOf(args, "-clean-route") < 0 && Array.IndexOf(args, "-clean-route-no-timing") < 0 && Array.IndexOf(args, "-clean-route-config-check") < 0) throw new ArgumentException("A grouping override requires an explicit benchmark route session.");
            bool grouping = BenchmarkGroupingState.Enabled(mode);
            string output = BenchmarkOutput.ValidateDirectory(Argument(args, "-clean-route-output"));
            var renderers = FindObjectsByType<XRayLevelInstancedRenderer>(FindObjectsInactive.Include);
            _modeState = new BenchmarkGroupingState(renderers, mode);
            Application.quitting += RestoreMode;
            var evidence = new Mode { mode = mode, buildGuid = Application.buildGUID, groupingEnabled = grouping, renderers = renderers.Length };
            foreach (var renderer in renderers)
            {
                var source = renderer.BatchSet;
                if (source == null) continue;
                if (source.GroupingVersion != XRayInstanceBatchSet.GROUPING_VERSION || source.MatrixPreparationVersion != XRayInstanceBatchSet.MATRIX_PREPARATION_VERSION) throw new InvalidOperationException("Location preparation is stale: " + source.name);
                evidence.sourceBatches += source.Batches.Count;
                evidence.sourceInstances += source.InstanceCount;
                evidence.preparedGroups += source.SubmissionGroups.Count;
                if (renderer.Configuration.InstanceSubmissionGroupingEnabled != grouping) throw new InvalidOperationException("Actual grouping configuration differs from the requested mode.");
            }
            BenchmarkOutput.WriteJson(Path.Combine(output, "batching-mode.json"), evidence);
            if (Array.IndexOf(args, "-batching-counters") < 0) return;
            var observer = new GameObject("Benchmark Location Batching").AddComponent<LocationBatchingBenchmark>();
            observer._renderers = renderers;
            observer._output = output;
            observer._start = Time.realtimeSinceStartupAsDouble;
            observer._capture = Array.IndexOf(args, "-batching-capture") >= 0;
            foreach (var renderer in renderers)
            {
                if (!renderer.isActiveAndEnabled || renderer.BatchSet == null) continue;
                var original = (Action<ScriptableRenderContext, Camera>)Delegate.CreateDelegate(typeof(Action<ScriptableRenderContext, Camera>), renderer, DRAW);
                var callback = new MeasuredCallback(original);
                observer._callbacks.Add(callback);
                RenderPipelineManager.beginCameraRendering -= original;
                RenderPipelineManager.beginCameraRendering += callback.Invoke;
            }
            RenderPipelineManager.endCameraRendering += observer.Record;
        }

        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= Record;
            foreach (var callback in _callbacks)
            {
                RenderPipelineManager.beginCameraRendering -= callback.Invoke;
                if (callback.Original.Target is XRayLevelInstancedRenderer renderer && renderer != null && renderer.isActiveAndEnabled) RenderPipelineManager.beginCameraRendering += callback.Original;
            }
        }

        public static void RestoreMode()
        {
            Application.quitting -= RestoreMode;
            _modeState?.Dispose();
            _modeState = null;
        }

        private void Record(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || Time.realtimeSinceStartupAsDouble - _start < 8) return;
            if (!_hasPose || camera.transform.position != _position || camera.transform.rotation != _rotation)
            {
                _hasPose = true;
                _pending = true;
                _position = camera.transform.position;
                _rotation = camera.transform.rotation;
                _changed = Time.realtimeSinceStartupAsDouble;
                foreach (var callback in _callbacks) callback.Reset();
            }
            if (!_pending || Time.realtimeSinceStartupAsDouble - _changed < 1) return;
            _pending = false;
            var report = new Pose { frame = Time.frameCount, position = _position, rotation = _rotation, memoryBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() };
            foreach (var renderer in _renderers)
            {
                if (!renderer.isActiveAndEnabled || renderer.BatchSet == null) continue;
                report.submissions += renderer.LastSubmittedCalls;
                report.submittedInstances += renderer.LastSubmittedInstances;
                report.colorInstances += renderer.LastVisibleInstances;
                report.visibleGroups += renderer.LastVisibleBatches;
                report.shadowOnlyGroups += renderer.LastShadowOnlyBatches;
                report.sourceInstances += renderer.BatchSet.InstanceCount;
                RecordVisibility(renderer, camera, report);
                var groups = (Array)typeof(XRayLevelInstancedRenderer).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(renderer);
                if (groups == null) continue;
                foreach (object group in groups)
                {
                    report.groupMatrixPayloadBytes += ((Array)group.GetType().GetProperty("Instances").GetValue(group)).Length * 128L;
                    report.groupDataPayloadBytes += ((Array)group.GetType().GetProperty("Data").GetValue(group)).Length * 16L;
                    bool active = (bool)group.GetType().GetProperty("Active").GetValue(group);
                    if (active) report.mergedGroups++;
                    var plan = (XRayInstanceBatchGroup)group.GetType().GetProperty("Plan").GetValue(group);
                    var visibility = (byte[])typeof(XRayLevelInstancedRenderer).GetField("_sourceVisibility", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(renderer);
                    bool color = false;
                    bool offscreen = false;
                    foreach (int index in plan.BatchIndices)
                    {
                        color |= visibility[index] == 2;
                        offscreen |= visibility[index] == 1;
                    }
                    if (!active && color && offscreen) report.mixedVisibilityFallbacks++;
                }
            }
            foreach (var callback in _callbacks)
            {
                report.cpuSamples += callback.Count;
                report.cpuMicrosecondsSum += callback.Ticks * 1000000.0 / Stopwatch.Frequency;
                report.cpuMicrosecondsMax = Math.Max(report.cpuMicrosecondsMax, callback.Maximum * 1000000.0 / Stopwatch.Frequency);
            }
            report.cpuMicrosecondsMean = report.cpuSamples > 0 ? report.cpuMicrosecondsSum / report.cpuSamples : 0;
            string prefix = Path.Combine(_output, "batching-pose" + _pose++);
            BenchmarkOutput.WriteJson(prefix + ".json", report);
            if (_capture) ScreenCapture.CaptureScreenshot(prefix + ".png");
        }

        private static string Argument(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing argument: " + key);
            return args[index + 1];
        }

        private static void RecordVisibility(XRayLevelInstancedRenderer renderer, Camera camera, Pose report)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var wind = XRayFloraWindBounds.ActiveState();
            bool shadows = (bool)typeof(XRayLevelInstancedRenderer).GetField("_castShadows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(renderer);
            float distance = (float)typeof(XRayLevelInstancedRenderer).GetField("_maxDistance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(renderer);
            float maximum = distance > 0 ? distance * distance : float.MaxValue;
            foreach (var batch in renderer.BatchSet.Batches)
            {
                report.sourceBatches++;
                Bounds bounds = XRayFloraWindBounds.Uses(batch.Materials, wind) ? XRayFloraWindBounds.Expand(batch.Bounds, wind) : batch.Bounds;
                bool eligible = batch.Mesh != null && batch.Count > 0 && bounds.SqrDistance(camera.transform.position) <= maximum;
                bool color = eligible && GeometryUtility.TestPlanesAABB(planes, bounds);
                if (color)
                {
                    report.colorSourceBatches++;
                    report.colorCandidateInstances += batch.Count;
                }
                else if (eligible && shadows && batch.CastShadows) report.shadowOnlySourceBatches++;
                else report.excludedSourceBatches++;
            }
        }

        private sealed class MeasuredCallback
        {
            public Action<ScriptableRenderContext, Camera> Original { get; }
            public long Ticks { get; private set; }
            public long Maximum { get; private set; }
            public int Count { get; private set; }

            public MeasuredCallback(Action<ScriptableRenderContext, Camera> original)
            {
                Original = original;
            }

            public void Invoke(ScriptableRenderContext context, Camera camera)
            {
                if (camera.cameraType != CameraType.Game)
                {
                    Original(context, camera);
                    return;
                }
                long start = Stopwatch.GetTimestamp();
                Original(context, camera);
                long elapsed = Stopwatch.GetTimestamp() - start;
                Ticks += elapsed;
                Maximum = Math.Max(Maximum, elapsed);
                Count++;
            }

            public void Reset()
            {
                Ticks = 0;
                Maximum = 0;
                Count = 0;
            }
        }

        [Serializable]
        private sealed class Mode
        {
            public string mode;
            public string buildGuid;
            public int sourceBatches;
            public int sourceInstances;
            public int preparedGroups;
            public bool groupingEnabled;
            public int renderers;
            public string scope = "U/G1 apply false/true to a private in-memory GameConfig clone before warmup. The same source sets and prepared plans remain intact. Production assets are never written. Counters and elapsed callback timing exist only with batching-counters; main series has no observer.";
        }

        [Serializable]
        private sealed class Pose
        {
            public int frame;
            public Vector3 position;
            public Quaternion rotation;
            public int submissions;
            public int submittedInstances;
            public int colorInstances;
            public int sourceInstances;
            public int sourceBatches;
            public int colorSourceBatches;
            public int colorCandidateInstances;
            public int shadowOnlySourceBatches;
            public int excludedSourceBatches;
            public int mergedGroups;
            public int mixedVisibilityFallbacks;
            public int visibleGroups;
            public int shadowOnlyGroups;
            public int cpuSamples;
            public double cpuMicrosecondsSum;
            public double cpuMicrosecondsMean;
            public double cpuMicrosecondsMax;
            public long memoryBytes;
            public long groupMatrixPayloadBytes;
            public long groupDataPayloadBytes;
            public string scope = "Technical run only: actual RenderMeshInstanced invocations and instance arguments, elapsed wall duration of the real renderer callback over the observed one-second window after route warmup. Timing includes native calls and is not exclusive CPU execution time; it excludes subsequent HDRP/native culling and GPU work. Source color candidates use wind-expanded source bounds and the camera frustum, not occlusion. Merged-group and mixed-fallback counts inspect the existing runtime scratch once per pose. Color instances are submission candidates, not visible pixels or GPU draws. Screenshots are route evidence, not pixel equivalence.";
        }
    }
}
