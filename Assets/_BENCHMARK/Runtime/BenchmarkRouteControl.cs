using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rvm.Benchmark
{
    [DefaultExecutionOrder(-32000)]
    public sealed class BenchmarkRouteControl : MonoBehaviour
    {
        private BenchmarkScenario _scenario;
        private Camera _camera;
        private BenchmarkSequence _sequence;
        private BenchmarkWorldState _worldState;
        private BenchmarkMetadata _metadata;
        private Vector3 _savedLocalPosition;
        private Quaternion _savedLocalRotation;
        private Vector3 _expectedPosition;
        private Quaternion _expectedRotation;
        private readonly List<RouteTransition> _transitions = new List<RouteTransition>(12);
        private long _startTicks;
        private double _measuredStarted;
        private double _measuredFinished;
        private int _observedFrames;
        private int _renderedFrames;
        private int _renderCallbacks;
        private int _lastRenderedFrame = -1;
        private int _warmupPosition = -1;
        private int _drainFrames;
        private int _runtimeErrors;
        private bool _started;
        private bool _cameraSaved;
        private bool _finishing;
        private string _error;

        public event Action<BenchmarkRouteControl> Completed;

        public bool Running { get; private set; }
        public bool Saved { get; private set; }
        public string LastDirectory { get; private set; }
        public string Error => _error;
        public int RenderedFrames => _renderedFrames;
        public BenchmarkMetadata Metadata => _metadata;
        public string RunId => _metadata?.runId;
        public string Phase => !Running ? "finished" : _sequence == null || !_started ? "preparation" : _sequence.Complete ? "drain" : _sequence.Measuring ? "measuring" : "warmup";
        public int Position => _sequence != null && _sequence.Measuring ? _sequence.Position : _warmupPosition;
        public bool Transition => _sequence != null && _sequence.Measuring && Seconds(BenchmarkClock.GetTimestamp() - _startTicks) - _sequence.PhaseStart < _scenario.StabilizationSeconds;

        public void Begin(BenchmarkRunner runner, string outputRoot, Action<BenchmarkMetadata> initializeMetadata = null)
        {
            if (Running || runner == null || runner.Running)
            {
                throw new InvalidOperationException("Route control requires an idle configured runner.");
            }
            string validation = runner.Validate();
            if (validation != null)
            {
                throw new InvalidOperationException(validation);
            }
            _scenario = runner.Scenario;
            _camera = runner.WorkingCamera;
            _worldState = BenchmarkWorldState.Acquire(_camera, _scenario);
            try
            {
                _metadata = BenchmarkEnvironment.Capture(_scenario, _camera, runner.Positions, null, 1);
                _metadata.mode = "ControlRouteNoCollector";
                initializeMetadata?.Invoke(_metadata);
                LastDirectory = BenchmarkOutput.Prepare(outputRoot, _metadata);
                _savedLocalPosition = _camera.transform.localPosition;
                _savedLocalRotation = _camera.transform.localRotation;
                _cameraSaved = true;
                _sequence = new BenchmarkSequence(_scenario.PositionSeconds, _scenario.WarmupSeconds);
                _transitions.Clear();
                _started = false;
                _observedFrames = 0;
                _renderedFrames = 0;
                _renderCallbacks = 0;
                _lastRenderedFrame = -1;
                _warmupPosition = -1;
                _drainFrames = 0;
                _runtimeErrors = 0;
                _measuredStarted = 0;
                _measuredFinished = 0;
                _error = null;
                Saved = false;
                MoveCamera(0, "preparation");
                _startTicks = BenchmarkClock.GetTimestamp();
                Running = true;
                RenderPipelineManager.endCameraRendering += OnCameraRendered;
                Application.logMessageReceived += OnLog;
            }
            catch
            {
                _worldState?.Dispose();
                _worldState = null;
                RestoreCamera();
                throw;
            }
        }

        public void Abort(string reason)
        {
            if (!Running || _finishing)
            {
                return;
            }
            _error = reason;
            Finish();
        }

        private void Update()
        {
            if (!Running || _finishing)
            {
                return;
            }
            try
            {
                if (_camera == null)
                {
                    throw new InvalidOperationException("Control camera was destroyed.");
                }
                _observedFrames++;
                double now = Seconds(BenchmarkClock.GetTimestamp() - _startTicks);
                if (!_started)
                {
                    _sequence.Start(now);
                    _started = true;
                }
                if (_sequence.Complete)
                {
                    if (++_drainFrames >= 8)
                    {
                        Finish();
                    }
                    return;
                }
                bool changed = _sequence.Advance(now);
                if (_sequence.Complete)
                {
                    _measuredFinished = now;
                    return;
                }
                if (_sequence.Measuring)
                {
                    if (_measuredStarted == 0)
                    {
                        _measuredStarted = now;
                    }
                    if (changed)
                    {
                        MoveCamera(_sequence.Position, "measuring");
                    }
                }
                else
                {
                    int position = _sequence.WarmupPosition(now);
                    if (position != _warmupPosition)
                    {
                        _warmupPosition = position;
                        MoveCamera(position, "warmup");
                    }
                }
            }
            catch (Exception exception)
            {
                Abort(exception.ToString());
            }
        }

        private void LateUpdate()
        {
            if (Running && !_finishing && _camera != null && ((_camera.transform.position - _expectedPosition).sqrMagnitude > 0.00000001f || Quaternion.Angle(_camera.transform.rotation, _expectedRotation) > 0.001f))
            {
                Abort("Another system moved the route-control camera.");
            }
        }

        private void OnDisable()
        {
            Abort("Route-control component disabled.");
        }

        private void OnDestroy()
        {
            Abort("Route-control component destroyed.");
        }

        private void OnCameraRendered(ScriptableRenderContext context, Camera camera)
        {
            if (!Running || camera != _camera)
            {
                return;
            }
            _renderCallbacks++;
            if (_lastRenderedFrame != Time.frameCount)
            {
                _lastRenderedFrame = Time.frameCount;
                _renderedFrames++;
            }
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Assert || type == LogType.Error || type == LogType.Exception)
            {
                _runtimeErrors++;
            }
        }

        private void MoveCamera(int position, string phase)
        {
            var pose = _metadata.positions[position];
            _expectedPosition = pose.position;
            _expectedRotation = pose.rotation;
            _camera.transform.SetPositionAndRotation(_expectedPosition, _expectedRotation);
            _transitions.Add(new RouteTransition { observedUnityFrame = Time.frameCount, realtimeSeconds = Time.realtimeSinceStartupAsDouble, phase = phase, position = position, cameraId = _camera.GetEntityId().ToString(), cameraPosition = pose.position, cameraRotation = pose.rotation });
        }

        private void Finish()
        {
            _finishing = true;
            Running = false;
            RenderPipelineManager.endCameraRendering -= OnCameraRendered;
            Application.logMessageReceived -= OnLog;
            double elapsed = Seconds(BenchmarkClock.GetTimestamp() - _startTicks);
            try
            {
                if (_camera != null)
                {
                    BenchmarkEnvironment.CollectWorld(_camera, _metadata.endEnvironment, _metadata.warnings);
                }
                _metadata.runtimeErrors = _runtimeErrors;
                _metadata.finishedUtc = DateTime.UtcNow.ToString("O");
                _metadata.finalFramePacing = QualitySettings.vSyncCount + "/" + Application.targetFrameRate + "/" + OnDemandRendering.renderFrameInterval;
                _metadata.finalQuality = QualitySettings.names[QualitySettings.GetQualityLevel()];
            }
            catch (Exception exception)
            {
                _error = exception.ToString();
            }
            finally
            {
                try
                {
                    _worldState?.Dispose();
                }
                catch (Exception exception)
                {
                    _error = "Weather restoration failed: " + exception;
                }
                _worldState = null;
                RestoreCamera();
            }
            try
            {
                var result = new RouteResult { runId = _metadata.runId, status = _error == null ? "completed" : "aborted", error = _error, elapsedSeconds = elapsed, measuredSeconds = _measuredFinished > _measuredStarted ? _measuredFinished - _measuredStarted : 0, observedUpdateFrames = _observedFrames, completedCameraRenderFrames = _renderedFrames, endCameraCallbacks = _renderCallbacks, renderedFramePolicy = "Unique Unity frames with endCameraRendering for the working camera; this proves pipeline camera rendering, not GPU presentation completion.", benchmarkCollectorActive = false, benchmarkRecordersActive = false, binaryCaptureActive = UnityEngine.Profiling.Profiler.enableBinaryLog, profilerEnabled = UnityEngine.Profiling.Profiler.enabled, runtimeErrors = _runtimeErrors, transitions = _transitions };
                BenchmarkOutput.WriteJson(Path.Combine(LastDirectory, "metadata.json"), _metadata);
                BenchmarkOutput.WriteJson(Path.Combine(LastDirectory, "route-control.json"), result);
                Saved = true;
            }
            catch (Exception exception)
            {
                Saved = false;
                Debug.LogError("Route-control result save failed: " + exception);
            }
            _finishing = false;
            Completed?.Invoke(this);
        }

        private void RestoreCamera()
        {
            if (_cameraSaved && _camera != null)
            {
                _camera.transform.localPosition = _savedLocalPosition;
                _camera.transform.localRotation = _savedLocalRotation;
            }
            _cameraSaved = false;
        }

        private static double Seconds(long ticks)
        {
            return ticks / (double)BenchmarkClock.Frequency;
        }

        [Serializable]
        private sealed class RouteTransition
        {
            public int observedUnityFrame;
            public double realtimeSeconds;
            public string phase;
            public int position;
            public string cameraId;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
        }

        [Serializable]
        private sealed class RouteResult
        {
            public string runId;
            public string status;
            public string error;
            public double elapsedSeconds;
            public double measuredSeconds;
            public int observedUpdateFrames;
            public int completedCameraRenderFrames;
            public int endCameraCallbacks;
            public string renderedFramePolicy;
            public bool benchmarkCollectorActive;
            public bool benchmarkRecordersActive;
            public bool binaryCaptureActive;
            public bool profilerEnabled;
            public int runtimeErrors;
            public List<RouteTransition> transitions;
        }
    }
}
