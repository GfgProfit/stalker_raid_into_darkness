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
        private const string NATIVE_VIEW_SCOPE = "Native B emits BRG Camera commands and Light shadow commands for the prepared owner Game camera; a successful native path skips the complete legacy instance loop, while unsupported contracts use legacy fallback. Shadow commands count emitted source/submesh commands per accepted Light view; shadow instances repeat source instance references across submeshes/views and are not unique casters or GPU draws. Reflection commands/instances/batches/views and registered reflection views are separate. Reflection coverage requires actual view and command evidence; callbacks or registered views alone do not establish emitted geometry. Technical Game and reflection preparation use separate callback populations; native culling elapsed includes observed Camera/Light/reflection callbacks and its mean is per view callback. Normalize elapsed sums only against a declared population; they are not exclusive CPU or a full frame budget. Registered views do not prove executed commands.";

        private static readonly MethodInfo DRAW = typeof(XRayLevelInstancedRenderer).GetMethod("OnBeginCameraRendering", BindingFlags.Instance | BindingFlags.NonPublic);
        private static BenchmarkGroupingState _modeState;
        private static BenchmarkNativeSubmissionState _nativeState;
        private static string _nativeOutput;
        private static LocationBatchingBenchmark _technicalObserver;

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
        private BenchmarkNativeCullingObserver _nativeCulling;
        private bool _nativeCullingRestored;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Application.isEditor) return;
            string nativeMode = BenchmarkNativeSubmissionState.RequestedMode(args);
            bool groupingRequested = Array.IndexOf(args, "-location-batching") >= 0;
            if (!groupingRequested && nativeMode == null) return;
            if (Array.IndexOf(args, "-clean-route") < 0 && Array.IndexOf(args, "-clean-route-no-timing") < 0 && Array.IndexOf(args, "-clean-route-config-check") < 0) throw new ArgumentException("A grouping override requires an explicit benchmark route session.");
            string mode = groupingRequested ? Argument(args, "-location-batching") : null;
            bool grouping = groupingRequested && BenchmarkGroupingState.Enabled(mode);
            string output = BenchmarkOutput.ValidateDirectory(Argument(args, "-clean-route-output"));
            var renderers = FindObjectsByType<XRayLevelInstancedRenderer>(FindObjectsInactive.Include);
            Application.quitting += RestoreMode;
            if (nativeMode != null)
            {
                _nativeState = new BenchmarkNativeSubmissionState(renderers, nativeMode);
                _nativeOutput = output;
                BenchmarkOutput.WriteJson(Path.Combine(output, "native-submission-mode.json"), CaptureNativeMode("startup"));
            }
            if (groupingRequested)
            {
                _modeState = new BenchmarkGroupingState(renderers, mode);
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
            }
            if (Array.IndexOf(args, "-batching-counters") < 0) return;
            var observer = new GameObject("Benchmark Location Batching").AddComponent<LocationBatchingBenchmark>();
            observer._renderers = renderers;
            observer._output = output;
            observer._start = Time.realtimeSinceStartupAsDouble;
            observer._capture = Array.IndexOf(args, "-batching-capture") >= 0;
            observer._nativeCulling = new BenchmarkNativeCullingObserver();
            _technicalObserver = observer;
            foreach (var renderer in renderers)
            {
                if (!renderer.isActiveAndEnabled || renderer.BatchSet == null) continue;
                var original = (Action<ScriptableRenderContext, Camera>)Delegate.CreateDelegate(typeof(Action<ScriptableRenderContext, Camera>), renderer, DRAW);
                var callback = new MeasuredCallback(original, renderer, observer._nativeCulling);
                observer._callbacks.Add(callback);
                RenderPipelineManager.beginCameraRendering -= original;
                RenderPipelineManager.beginCameraRendering += callback.Invoke;
            }
            RenderPipelineManager.endCameraRendering += observer.Record;
        }

        private void OnDisable()
        {
            StopObserving();
        }

        private void StopObserving()
        {
            try
            {
                ReleaseNativeCulling();
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= Record;
                foreach (var callback in _callbacks)
                {
                    RenderPipelineManager.beginCameraRendering -= callback.Invoke;
                    if (callback.Original.Target is XRayLevelInstancedRenderer renderer && renderer != null && renderer.isActiveAndEnabled) RenderPipelineManager.beginCameraRendering += callback.Original;
                }
                _callbacks.Clear();
                if (ReferenceEquals(_technicalObserver, this)) _technicalObserver = null;
            }
        }

        public static void RestoreMode()
        {
            Application.quitting -= RestoreMode;
            try
            {
                if (_technicalObserver != null)
                {
                    var observer = _technicalObserver;
                    try
                    {
                        observer.StopObserving();
                    }
                    finally
                    {
                        observer.enabled = false;
                    }
                }
                if (_nativeState != null && !string.IsNullOrEmpty(_nativeOutput)) BenchmarkOutput.WriteJson(Path.Combine(_nativeOutput, "native-submission-after.json"), CaptureNativeMode("before-restoration"));
            }
            finally
            {
                try
                {
                    _nativeState?.Dispose();
                }
                finally
                {
                    _nativeState = null;
                    _nativeOutput = null;
                    _technicalObserver = null;
                    _modeState?.Dispose();
                    _modeState = null;
                }
            }
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
                _nativeCulling?.Reset();
            }
            if (!_pending || Time.realtimeSinceStartupAsDouble - _changed < 1) return;
            _pending = false;
            var report = new Pose { frame = Time.frameCount, position = _position, rotation = _rotation, memoryBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() };
            using (var process = Process.GetCurrentProcess())
            {
                process.Refresh();
                report.privateMemoryBytes = process.PrivateMemorySize64;
                report.workingSetBytes = process.WorkingSet64;
            }
            report.managedHeapBytes = GC.GetTotalMemory(false);
            foreach (var renderer in _renderers)
            {
                if (!renderer.isActiveAndEnabled || renderer.BatchSet == null) continue;
                report.submissions += renderer.LastSubmittedCalls;
                report.submittedInstances += renderer.LastSubmittedInstances;
                report.colorInstances += renderer.LastVisibleInstances;
                report.visibleGroups += renderer.LastVisibleBatches;
                report.shadowOnlyGroups += renderer.LastShadowOnlyBatches;
                report.rejectedShadowBatches += renderer.LastRejectedShadowBatches;
                report.nativeActiveRenderers += renderer.NativeSubmissionActive ? 1 : 0;
                report.nativeMemoryBytes += renderer.NativeSubmissionMemoryBytes;
                report.nativeCameraCommands += renderer.NativeCameraCommands;
                report.nativeCameraInstances += renderer.NativeCameraInstances;
                report.nativeCameraBatches += renderer.NativeCameraBatches;
                report.nativeShadowCommands += renderer.NativeShadowCommands;
                report.nativeShadowInstances += renderer.NativeShadowInstances;
                report.nativeReflectionCommands += renderer.NativeReflectionCommands;
                report.nativeReflectionInstances += renderer.NativeReflectionInstances;
                report.nativeReflectionBatches += renderer.NativeReflectionBatches;
                report.nativeReflectionViews += renderer.NativeReflectionViews;
                report.nativeRegisteredReflectionViews += renderer.NativeRegisteredReflectionViews;
                report.nativeCulledBatches += renderer.NativeCulledBatches;
                report.nativeCulledInstances += renderer.NativeCulledInstances;
                report.nativeShadowViews += renderer.NativeShadowViews;
                report.nativeCullingInvocations += renderer.NativeCullingInvocations;
                report.nativeCullingOutputBytes += renderer.NativeCullingOutputBytes;
                report.nativeFallbacks.Add(new NativeFallback { renderer = renderer.name, entityId = renderer.GetEntityId().ToString(), reason = renderer.NativeSubmissionFallbackReason });
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
                report.reflectionCpuSamples += callback.ReflectionCount;
                report.reflectionCpuMicrosecondsSum += callback.ReflectionTicks * 1000000.0 / Stopwatch.Frequency;
                report.reflectionCpuMicrosecondsMax = Math.Max(report.reflectionCpuMicrosecondsMax, callback.ReflectionMaximum * 1000000.0 / Stopwatch.Frequency);
            }
            report.cpuMicrosecondsMean = report.cpuSamples > 0 ? report.cpuMicrosecondsSum / report.cpuSamples : 0;
            report.reflectionCpuMicrosecondsMean = report.reflectionCpuSamples > 0 ? report.reflectionCpuMicrosecondsSum / report.reflectionCpuSamples : 0;
            if (_nativeCulling != null)
            {
                var measurement = _nativeCulling.Capture();
                report.nativeCullingObserverSupported = measurement.Supported;
                report.nativeCullingTimingAvailable = measurement.Available;
                report.nativeCullingElapsedSamples = measurement.Samples;
                report.nativeCullingElapsedMicrosecondsSum = measurement.Ticks * 1000000.0 / Stopwatch.Frequency;
                report.nativeCullingElapsedMicrosecondsMean = measurement.Samples > 0 ? report.nativeCullingElapsedMicrosecondsSum / measurement.Samples : 0;
                report.nativeCullingElapsedMicrosecondsMax = measurement.MaximumTicks * 1000000.0 / Stopwatch.Frequency;
                report.nativeCullingObservedGroups = measurement.ObservedGroups;
                report.nativeCullingUnavailableReason = measurement.UnavailableReason;
            }
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

        private void ReleaseNativeCulling()
        {
            if (_nativeCulling == null || _nativeCullingRestored) return;
            _nativeCullingRestored = true;
            try
            {
                _nativeCulling.Dispose();
            }
            finally
            {
                if (!string.IsNullOrEmpty(_output))
                {
                    var native = _nativeCulling.Capture();
                    var evidence = new NativeCullingRestoration { supported = _nativeCulling.Supported, restored = _nativeCulling.RestorationFailures == 0, failures = _nativeCulling.RestorationFailures, error = _nativeCulling.RestorationError, observedGroups = native.ObservedGroups, nativeCullingTimingAvailable = native.Available, nativeCullingSamples = native.Samples, nativeCullingMicrosecondsSum = native.Ticks * 1000000.0 / Stopwatch.Frequency };
                    foreach (var callback in _callbacks)
                    {
                        evidence.gameCpuSamples += callback.Count;
                        evidence.gameCpuMicrosecondsSum += callback.Ticks * 1000000.0 / Stopwatch.Frequency;
                        evidence.reflectionCpuSamples += callback.ReflectionCount;
                        evidence.reflectionCpuMicrosecondsSum += callback.ReflectionTicks * 1000000.0 / Stopwatch.Frequency;
                    }
                    BenchmarkOutput.WriteJson(Path.Combine(_output, "native-culling-observer-restoration.json"), evidence);
                }
            }
        }

        private static NativeMode CaptureNativeMode(string phase)
        {
            var evidence = new NativeMode { mode = _nativeState.Mode, buildGuid = Application.buildGUID, requestedEnabled = BenchmarkNativeSubmissionState.Enabled(_nativeState.Mode), phase = phase };
            foreach (var renderer in _nativeState.Renderers)
            {
                if (renderer == null) continue;
                evidence.renderers++;
                evidence.overriddenRenderers += renderer.NativeSubmissionOverride == evidence.requestedEnabled ? 1 : 0;
                evidence.activeRenderers += renderer.NativeSubmissionActive ? 1 : 0;
                evidence.nativeMemoryBytes += renderer.NativeSubmissionMemoryBytes;
                evidence.cameraCommands += renderer.NativeCameraCommands;
                evidence.cameraInstances += renderer.NativeCameraInstances;
                evidence.cameraBatches += renderer.NativeCameraBatches;
                evidence.shadowCommands += renderer.NativeShadowCommands;
                evidence.shadowInstances += renderer.NativeShadowInstances;
                evidence.reflectionCommands += renderer.NativeReflectionCommands;
                evidence.reflectionInstances += renderer.NativeReflectionInstances;
                evidence.reflectionBatches += renderer.NativeReflectionBatches;
                evidence.reflectionViews += renderer.NativeReflectionViews;
                evidence.registeredReflectionViews += renderer.NativeRegisteredReflectionViews;
                evidence.culledBatches += renderer.NativeCulledBatches;
                evidence.culledInstances += renderer.NativeCulledInstances;
                evidence.shadowViews += renderer.NativeShadowViews;
                evidence.cullingInvocations += renderer.NativeCullingInvocations;
                evidence.cullingOutputBytes += renderer.NativeCullingOutputBytes;
                evidence.nativeFallbacks.Add(new NativeFallback { renderer = renderer.name, entityId = renderer.GetEntityId().ToString(), reason = renderer.NativeSubmissionFallbackReason });
                if (renderer.BatchSet == null) continue;
                evidence.sourceBatches += renderer.BatchSet.Batches.Count;
                evidence.sourceInstances += renderer.BatchSet.InstanceCount;
            }
            return evidence;
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
            private readonly XRayLevelInstancedRenderer _renderer;
            private readonly BenchmarkNativeCullingObserver _nativeCulling;

            public Action<ScriptableRenderContext, Camera> Original { get; }
            public long Ticks { get; private set; }
            public long Maximum { get; private set; }
            public int Count { get; private set; }
            public long ReflectionTicks { get; private set; }
            public long ReflectionMaximum { get; private set; }
            public int ReflectionCount { get; private set; }

            public MeasuredCallback(Action<ScriptableRenderContext, Camera> original, XRayLevelInstancedRenderer renderer, BenchmarkNativeCullingObserver nativeCulling)
            {
                Original = original;
                _renderer = renderer;
                _nativeCulling = nativeCulling;
            }

            public void Invoke(ScriptableRenderContext context, Camera camera)
            {
                if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.Reflection)
                {
                    Original(context, camera);
                    _nativeCulling.Attach(_renderer);
                    return;
                }
                long start = Stopwatch.GetTimestamp();
                Original(context, camera);
                long elapsed = Stopwatch.GetTimestamp() - start;
                if (camera.cameraType == CameraType.Game)
                {
                    Ticks += elapsed;
                    Maximum = Math.Max(Maximum, elapsed);
                    Count++;
                }
                else
                {
                    ReflectionTicks += elapsed;
                    ReflectionMaximum = Math.Max(ReflectionMaximum, elapsed);
                    ReflectionCount++;
                }
                _nativeCulling.Attach(_renderer);
            }

            public void Reset()
            {
                Ticks = 0;
                Maximum = 0;
                Count = 0;
                ReflectionTicks = 0;
                ReflectionMaximum = 0;
                ReflectionCount = 0;
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
            public int rejectedShadowBatches;
            public int nativeActiveRenderers;
            public int nativeCameraCommands;
            public int nativeCameraInstances;
            public int nativeCameraBatches;
            public int nativeShadowCommands;
            public int nativeShadowInstances;
            public int nativeReflectionCommands;
            public int nativeReflectionInstances;
            public int nativeReflectionBatches;
            public int nativeReflectionViews;
            public int nativeRegisteredReflectionViews;
            public int nativeCulledBatches;
            public int nativeCulledInstances;
            public int nativeShadowViews;
            public int nativeCullingInvocations;
            public int nativeCullingElapsedSamples;
            public int nativeCullingObservedGroups;
            public bool nativeCullingObserverSupported;
            public bool nativeCullingTimingAvailable;
            public int cpuSamples;
            public int reflectionCpuSamples;
            public double cpuMicrosecondsSum;
            public double cpuMicrosecondsMean;
            public double cpuMicrosecondsMax;
            public double reflectionCpuMicrosecondsSum;
            public double reflectionCpuMicrosecondsMean;
            public double reflectionCpuMicrosecondsMax;
            public double nativeCullingElapsedMicrosecondsSum;
            public double nativeCullingElapsedMicrosecondsMean;
            public double nativeCullingElapsedMicrosecondsMax;
            public long memoryBytes;
            public long privateMemoryBytes;
            public long workingSetBytes;
            public long managedHeapBytes;
            public long groupMatrixPayloadBytes;
            public long groupDataPayloadBytes;
            public long nativeMemoryBytes;
            public long nativeCullingOutputBytes;
            public List<NativeFallback> nativeFallbacks = new List<NativeFallback>();
            public string nativeCullingUnavailableReason;
            public string nativeViewScope = NATIVE_VIEW_SCOPE;
            public string scope = "Technical run only: submissions/submittedInstances count legacy RenderMeshInstanced API invocations/arguments. Zero legacy calls in native B means zero calls to that API, not zero GPU geometry/shadow work. Successful native B submits Camera and owner Game Light shadow views without the complete legacy instance loop; fallback retains the original path. Native camera/shadow commands are emitted BatchRendererGroup source/submesh commands per view, with repeated instance references across submeshes/views; these are not GPU draws or unique objects. Culling invocations include calls before guards; output bytes sum actual native output allocations across views, not resident or managed allocation totals. Source color/shadow candidate counts are independent legacy geometric candidates and do not include native rejection. Native memory is reported payload/storage, not total process memory. Process private bytes, working set and GC.GetTotalMemory(false) managed heap are sampled once at this technical pose; managed heap is an approximate heap snapshot, not allocation flow or zero-allocation evidence. Unity memoryBytes remains Profiler.GetTotalAllocatedMemoryLong. These scopes overlap and are not added. Renderer callback elapsed excludes later native culling. Separate culling elapsed wraps the original synchronous BatchRendererGroup OnPerformCulling delegate across observed camera/light views, weighted by completed callback samples; it excludes subsequently scheduled jobs and GPU work and is not exclusive CPU execution time. Native timing is unavailable unless nativeCullingTimingAvailable is true; unsupported metadata and absent/changed callbacks retain explicit reasons. Screenshots are route evidence, not pixel equivalence.";
        }

        [Serializable]
        private sealed class NativeMode
        {
            public string mode;
            public string buildGuid;
            public string phase;
            public bool requestedEnabled;
            public int renderers;
            public int overriddenRenderers;
            public int activeRenderers;
            public int sourceBatches;
            public int sourceInstances;
            public int cameraCommands;
            public int cameraInstances;
            public int cameraBatches;
            public int shadowCommands;
            public int shadowInstances;
            public int reflectionCommands;
            public int reflectionInstances;
            public int reflectionBatches;
            public int reflectionViews;
            public int registeredReflectionViews;
            public int culledBatches;
            public int culledInstances;
            public int shadowViews;
            public int cullingInvocations;
            public long nativeMemoryBytes;
            public long cullingOutputBytes;
            public List<NativeFallback> nativeFallbacks = new List<NativeFallback>();
            public string nativeViewScope = NATIVE_VIEW_SCOPE;
            public string scope = "Explicit benchmark-session override only; A selects legacy and B requests native Camera plus owner Game Light shadow submission without writing production GameConfig assets. Production GameConfig defaults are native on and grouping off; this session restores the prior nullable override. Active describes the current render context and can be false after EndContext; completed use requires published camera/shadow commands and declared view coverage. Startup counts do not prove activation. Missing or incompatible HDRP owner-camera scope API and unsupported rendering contracts use legacy fallback before native activation. Before-restoration evidence observes final published state. Culling invocations include calls before guards; output bytes sum native output allocations across views. Main timing runs install no culling recorder or callback observer. View command/instance counts repeat sources and are not GPU draw/unique-object counts.";
        }

        [Serializable]
        private sealed class NativeFallback
        {
            public string renderer;
            public string entityId;
            public string reason;
        }

        [Serializable]
        private sealed class NativeCullingRestoration
        {
            public bool supported;
            public bool restored;
            public int failures;
            public string error;
            public int observedGroups;
            public int gameCpuSamples;
            public double gameCpuMicrosecondsSum;
            public int reflectionCpuSamples;
            public double reflectionCpuMicrosecondsSum;
            public bool nativeCullingTimingAvailable;
            public int nativeCullingSamples;
            public double nativeCullingMicrosecondsSum;
            public string scope = "Technical teardown snapshot of the final observed window since the most recent pose reset, not whole-route totals. Game/reflection preparation have separate callback populations; native culling includes observed Camera and owner Game Light views plus any registered reflection views. Reflection coverage must be established from the observed views and commands. These synchronous elapsed scopes exclude downstream pipeline work, asynchronous jobs and GPU work; overlapping scopes do not form a full frame or exclusive CPU budget.";
        }
    }
}
