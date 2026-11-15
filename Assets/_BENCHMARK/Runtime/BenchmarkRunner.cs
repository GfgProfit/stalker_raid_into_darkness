using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Rvm.Benchmark
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-32000)]
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        [SerializeField] private BenchmarkScenario _scenario;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform[] _positions = new Transform[4];
        [SerializeField] [HideInInspector] private string _buildGitSha = "unavailable";
        [SerializeField] [HideInInspector] private string _buildGitDirty = "unavailable";
        [SerializeField] [HideInInspector] private string _buildHdrp = "unavailable";

        private BenchmarkSequence _sequence;
        private BenchmarkCollector _collector;
        private BenchmarkDiagnostics _diagnostics;
        private BenchmarkWorldState _worldState;
        private BenchmarkFrame[] _frames;
        private BenchmarkMetadata _metadata;
        private Vector3 _savedLocalPosition;
        private Quaternion _savedLocalRotation;
        private Vector3 _expectedPosition;
        private Quaternion _expectedRotation;
        private long _runStartTicks;
        private long _frameStartTicks;
        private long _lastCollectorTicks;
        private int _frameUnityIndex;
        private int _framePosition;
        private int _count;
        private int _drainFrames;
        private int _warmupPosition = -1;
        private int _gc0;
        private int _gc1;
        private int _gc2;
        private int _runtimeErrors;
        private int _lastRenderedUnityFrame;
        private bool _running;
        private bool _finishing;
        private bool _hasFrame;
        private bool _sequenceStarted;
        private bool _transition;
        private bool _cameraSaved;
        private bool _ownsCapture;
        private bool _captureStarted;
        private double _captureStartedAt;
        private bool _savedProfilerEnabled;
        private bool _savedBinaryLog;
        private string _savedProfilerFile;
        private string _cameraSignature;
        private string _error;
        private string _firstRuntimeError;
        private string _conditionEvidence;
        private BenchmarkShadowTrace _shadowTrace;
        private bool _shadowTraceEnabled;
        [SerializeField] [HideInInspector] private string _buildSourceDigest = "unavailable";
        [SerializeField] [HideInInspector] private string _renderSettingsDigest = "unavailable";

        public static event Action<BenchmarkRunner> Completed;

        public BenchmarkScenario Scenario => _scenario;
        public Camera WorkingCamera => _camera;
        public Transform[] Positions => (Transform[])_positions.Clone();
        public bool Running => _running;
        public string BuildSourceDigest => _buildSourceDigest;
        public string RenderSettingsDigest => _renderSettingsDigest;
        public string BuildGitSha => _buildGitSha;
        public string BuildGitDirty => _buildGitDirty;
        public bool Saved { get; private set; }
        public string LastDirectory { get; private set; }
        public BenchmarkSummary LastSummary { get; private set; }
        public BenchmarkMetadata LastMetadata => _metadata;
        public BenchmarkTraceContext TraceContext => new BenchmarkTraceContext { runId = _metadata?.runId, phase = !_running ? "Idle" : _sequence == null || !_sequenceStarted ? "Preparation" : _sequence.Complete ? "Drain" : _sequence.Measuring ? "Measurement" : "Warmup", position = _sequence == null ? -1 : _sequence.Measuring ? _sequence.Position : _warmupPosition, transition = _transition };

        public void EnableShadowTrace(bool enabled)
        {
            if (_running)
            {
                throw new InvalidOperationException("Cannot change tracing during a run.");
            }
            _shadowTraceEnabled = enabled;
        }

        public void Configure(BenchmarkScenario scenario, Camera camera, Transform[] positions)
        {
            if (_running)
            {
                throw new InvalidOperationException("Cannot configure an active benchmark.");
            }
            _scenario = scenario;
            _camera = camera;
            _positions = (Transform[])positions.Clone();
        }

        public void SetBuildEnvironment(string sha, string dirty, string hdrp)
        {
            _buildGitSha = sha;
            _buildGitDirty = dirty;
            _buildHdrp = hdrp;
        }

        public void SetValidationContext(string sourceDigest, string evidence, string renderSettingsDigest = null)
        {
            _buildSourceDigest = sourceDigest;
            _conditionEvidence = evidence;
            if (renderSettingsDigest != null)
            {
                _renderSettingsDigest = renderSettingsDigest;
            }
        }

        public string Validate()
        {
            if (_scenario == null)
            {
                return "BenchmarkScenario is missing.";
            }
            string scenarioError = _scenario.Validate();
            if (scenarioError != null)
            {
                return scenarioError;
            }
            if (_camera == null || !_camera.isActiveAndEnabled || _camera.cameraType != CameraType.Game)
            {
                return "An enabled Game camera is required.";
            }
            if (_camera.gameObject.scene.path != _scenario.ScenePath || gameObject.scene != _camera.gameObject.scene)
            {
                return "Runner, scenario and camera must refer to the same loaded scene.";
            }
            if (_positions == null || _positions.Length != 4)
            {
                return "Exactly four serialized scene Transform references are required.";
            }
            for (int i = 0; i < 4; i++)
            {
                if (_positions[i] == null || _positions[i].gameObject.scene != gameObject.scene || _positions[i] == _camera.transform || _camera.transform.IsChildOf(_positions[i]))
                {
                    return "Position references must belong to the runner scene and cannot contain the camera.";
                }
                for (int j = 0; j < i; j++)
                {
                    if (_positions[j] == _positions[i])
                    {
                        return "Position references must be unique and ordered.";
                    }
                }
            }
            return null;
        }

        public void Begin(string outputRoot, string seriesId = null, int repetition = 1)
        {
            if (_running)
            {
                throw new InvalidOperationException("Benchmark is already running.");
            }
            string error = Validate();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }
            _count = 0;
            _drainFrames = 0;
            _warmupPosition = -1;
            _error = null;
            _runtimeErrors = 0;
            _lastRenderedUnityFrame = -1;
            _firstRuntimeError = null;
            _hasFrame = false;
            _sequenceStarted = false;
            _finishing = false;
            _captureStarted = false;
            Saved = false;
            LastSummary = null;
            LastDirectory = null;
            try
            {
                var identity = BenchmarkBuildIdentity.Capture();
                _worldState = BenchmarkWorldState.Acquire(_camera, _scenario);
                _metadata = BenchmarkEnvironment.Capture(_scenario, _camera, _positions, seriesId, repetition);
                _metadata.gitSha = _buildGitSha;
                _metadata.gitDirty = _buildGitDirty;
                _metadata.hdrp = _buildHdrp;
                _metadata.buildSourceDigest = _buildSourceDigest;
                _metadata.conditionEvidence = _conditionEvidence;
                _metadata.renderSettingsDigest = _renderSettingsDigest;
                _metadata.buildIdentity = identity;
                _metadata.shadowTrace = _shadowTraceEnabled;
                if (!Application.isEditor && identity.stamp != null)
                {
                    _metadata.buildSourceDigest = identity.stamp.sourceDigest;
                    _metadata.renderSettingsDigest = identity.stamp.renderSettingsDigest;
                    _metadata.gitSha = identity.stamp.gitSha;
                    _metadata.gitDirty = identity.stamp.gitDirty;
                    _metadata.hdrp = identity.stamp.hdrp;
                }
                if (!string.IsNullOrEmpty(_conditionEvidence))
                {
                    _metadata.conditionProtocol = JsonUtility.FromJson<BenchmarkConditionEvidence>(_conditionEvidence)?.protocolId;
                }
                LastDirectory = BenchmarkOutput.Prepare(outputRoot, _metadata);
                if (_shadowTraceEnabled)
                {
                    _shadowTrace = new BenchmarkShadowTrace(_camera, () => TraceContext);
                }
            }
            catch
            {
                _worldState?.Dispose();
                _worldState = null;
                throw;
            }
            _savedLocalPosition = _camera.transform.localPosition;
            _savedLocalRotation = _camera.transform.localRotation;
            _cameraSaved = true;
            _cameraSignature = _metadata.cameraSettings;
            BenchmarkEnvironment.Add(_metadata.environment, "application.runInBackground", Application.runInBackground.ToString());
            _running = true;
            Application.logMessageReceived += OnLogMessage;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            try
            {
                _frames = new BenchmarkFrame[_scenario.FrameCapacity];
                _collector = new BenchmarkCollector(_frames.Length, _scenario.Mode);
                _sequence = new BenchmarkSequence(_scenario.PositionSeconds, _scenario.WarmupSeconds);
                _diagnostics = _scenario.Mode == BenchmarkMode.Diagnostic ? new BenchmarkDiagnostics(_scenario, _camera, _metadata) : null;
                MoveCamera(0);
                _gc0 = GC.CollectionCount(0);
                _gc1 = GC.CollectionCount(1);
                _gc2 = GC.CollectionCount(2);
                _runStartTicks = BenchmarkClock.GetTimestamp();
            }
            catch (Exception exception)
            {
                Abort(exception.ToString());
            }
        }

        public void Abort(string reason)
        {
            if (!_running || _finishing)
            {
                return;
            }
            _error = reason;
            Finish();
        }

        private void Update()
        {
            if (!_running || _finishing)
            {
                return;
            }
            long entryTicks = BenchmarkClock.GetTimestamp();
            try
            {
                if (_camera == null)
                {
                    throw new InvalidOperationException("Benchmark camera was destroyed.");
                }
                if (_hasFrame)
                {
                    RecordPrevious(entryTicks);
                }
                _collector.PollTimings();
                double now = Seconds(entryTicks);
                if (!_sequenceStarted)
                {
                    _sequence.Start(now);
                    _sequenceStarted = true;
                }
                if (_sequence.Complete)
                {
                    if (++_drainFrames >= 8)
                    {
                        Finish();
                    }
                    return;
                }
                if (_diagnostics != null && !_diagnostics.Sampling && (_sequence.Measuring || now - _sequence.PhaseStart >= _scenario.WarmupSeconds))
                {
                    _diagnostics.BeginSampling();
                    _sequence = new BenchmarkSequence(_scenario.PositionSeconds, 0);
                    _sequenceStarted = false;
                    MoveCamera(0);
                    return;
                }
                bool changed = _sequence.Advance(now);
                if (_sequence.Complete)
                {
                    _hasFrame = false;
                    return;
                }
                if (_sequence.Measuring)
                {
                    if (changed)
                    {
                        MoveCamera(_sequence.Position);
                    }
                    if (_count == 0)
                    {
                        _collector.StartMeasuredInterval();
                        _diagnostics?.StartMeasuredInterval();
                        _diagnostics?.Position(0);
                        _gc0 = GC.CollectionCount(0);
                        _gc1 = GC.CollectionCount(1);
                        _gc2 = GC.CollectionCount(2);
                    }
                    if (_scenario.ProfilerCapture && !_captureStarted && _sequence.Position == 0 && now - _sequence.PhaseStart >= _scenario.StabilizationSeconds)
                    {
                        _captureStarted = true;
                        StartCapture();
                        _diagnostics?.Position(0);
                        _captureStartedAt = now;
                        _metadata.captureStartUnityFrame = Time.frameCount;
                    }
                    if (_ownsCapture && now - _captureStartedAt >= _scenario.CaptureSeconds)
                    {
                        _metadata.captureEndUnityFrame = Time.frameCount;
                        StopCapture();
                    }
                    _framePosition = _sequence.Position;
                    _transition = now - _sequence.PhaseStart < _scenario.StabilizationSeconds;
                    _frameStartTicks = entryTicks;
                    _frameUnityIndex = Time.frameCount;
                    _hasFrame = true;
                }
                else
                {
                    int position = _sequence.WarmupPosition(now);
                    if (_warmupPosition != position)
                    {
                        _warmupPosition = position;
                        MoveCamera(position);
                    }
                }
                _lastCollectorTicks = BenchmarkClock.GetTimestamp() - entryTicks;
            }
            catch (Exception exception)
            {
                Abort(exception.ToString());
            }
        }

        private void LateUpdate()
        {
            if (!_running || _finishing || _camera == null)
            {
                return;
            }
            if ((_camera.transform.position - _expectedPosition).sqrMagnitude > 0.00000001f || Quaternion.Angle(_camera.transform.rotation, _expectedRotation) > 0.001f)
            {
                Abort("Another system moved the benchmark camera. No gameplay components were disabled.");
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_running && !_finishing && camera == _camera && _hasFrame && _sequence != null && _sequence.Measuring && _frameUnityIndex == Time.frameCount && _lastRenderedUnityFrame != Time.frameCount)
            {
                _lastRenderedUnityFrame = Time.frameCount;
                _metadata.renderedMeasurementFrames++;
            }
        }

        private void OnDisable()
        {
            Abort("Runner disabled or Play Mode stopped manually.");
        }

        private void OnDestroy()
        {
            Abort("Runner destroyed.");
        }

        private void OnApplicationQuit()
        {
            Abort("Application quit before benchmark completion.");
        }

        private void RecordPrevious(long nowTicks)
        {
            _hasFrame = false;
            if (_count == _frames.Length)
            {
                throw new InvalidOperationException("Preallocated frame capacity exceeded; partial results preserved.");
            }
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            _frames[_count] = new BenchmarkFrame
            {
                frameIndex = _count,
                unityFrame = _frameUnityIndex,
                position = _framePosition,
                transition = _transition,
                startTicks = _frameStartTicks,
                timestamp = Seconds(_frameStartTicks - _runStartTicks),
                frameMs = Seconds(nowTicks - _frameStartTicks) * 1000,
                collectorMicroseconds = Seconds(_lastCollectorTicks) * 1000000,
                gc0 = gc0 - _gc0,
                gc1 = gc1 - _gc1,
                gc2 = gc2 - _gc2
            };
            _gc0 = gc0;
            _gc1 = gc1;
            _gc2 = gc2;
            _collector.ReadPreviousFrame(_count);
            _diagnostics?.ReadPrevious(_count, _frames[_count]);
            _count++;
        }

        private void MoveCamera(int position)
        {
            if (_positions[position] == null)
            {
                throw new InvalidOperationException("Benchmark position was destroyed.");
            }
            _expectedPosition = _metadata.positions[position].position;
            _expectedRotation = _metadata.positions[position].rotation;
            _camera.transform.SetPositionAndRotation(_expectedPosition, _expectedRotation);
            _diagnostics?.Position(position);
        }

        private void Finish()
        {
            _finishing = true;
            _running = false;
            Application.logMessageReceived -= OnLogMessage;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            _hasFrame = false;
            long endTicks = BenchmarkClock.GetTimestamp();
            try
            {
                StopCapture();
                _diagnostics?.Dispose();
                _metadata.finalFramePacing = QualitySettings.vSyncCount + "/" + Application.targetFrameRate + "/" + UnityEngine.Rendering.OnDemandRendering.renderFrameInterval;
                _metadata.finalQuality = QualitySettings.names[QualitySettings.GetQualityLevel()];
                if (_metadata.finalQuality != _metadata.quality)
                {
                    _metadata.warnings.Add("Quality changed during the run: " + _metadata.quality + " -> " + _metadata.finalQuality);
                }
                if (_metadata.finalFramePacing != _metadata.vsync + "/" + _metadata.targetFrameRate + "/" + _metadata.renderFrameInterval)
                {
                    _metadata.warnings.Add("Frame pacing changed during the run: " + _metadata.finalFramePacing);
                }
                _metadata.runtimeErrors = _runtimeErrors;
                if (_runtimeErrors > 0)
                {
                    _metadata.warnings.Add("Runtime emitted " + _runtimeErrors + " errors/exceptions/assertions. First: " + _firstRuntimeError + ". Performance is not a clean baseline.");
                }
                if (_camera != null && _metadata != null)
                {
                    BenchmarkEnvironment.CollectWorld(_camera, _metadata.endEnvironment, _metadata.warnings);
                    if (BenchmarkEnvironment.CameraSignature(_camera) != _cameraSignature)
                    {
                        _metadata.warnings.Add("Camera render settings changed during the run. Comparison requires review.");
                    }
                }
                try
                {
                    _worldState?.Dispose();
                }
                catch (Exception exception)
                {
                    _error = "Weather/time restoration failed: " + exception.Message;
                    _metadata.warnings.Add(_error);
                }
                _worldState = null;
                if (_collector != null)
                {
                    bool windows = Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer;
                    _collector.AlignTimings(_frames, _count, endTicks, windows);
                    _collector.Dispose();
                    _metadata.frameTimingAlignment = _collector.Alignment;
                    _metadata.finishedUtc = DateTime.UtcNow.ToString("O");
                    LastSummary = BenchmarkOutput.Summarize(_metadata, _frames, _count, _collector, _error);
                    var timers = new System.Text.StringBuilder(_metadata.frameTimingAlignment);
                    foreach (var metric in _collector.Metrics)
                    {
                        if (metric.unit == "ms")
                        {
                            timers.Append('|').Append(metric.id).Append('=').Append(metric.available).Append('/').Append(metric.source);
                        }
                    }
                    _metadata.timerSignature = timers.ToString();
                    var sourceSignature = new System.Text.StringBuilder(_metadata.instrumentationSignature);
                    foreach (var metric in _collector.Metrics)
                    {
                        if (metric.identity != null)
                        {
                            sourceSignature.Append("|Standard/").Append(metric.identity.key).Append('/').Append(metric.identity.kind).Append("/physical=").Append(metric.identity.physicalSources);
                        }
                    }
                    _metadata.instrumentationSignature = sourceSignature.ToString();
                    bool diagnosticsSaved = true;
                    try
                    {
                        _diagnostics?.Save(LastDirectory, _frames, _count, LastSummary);
                    }
                    catch (Exception exception)
                    {
                        diagnosticsSaved = false;
                        string warning = "Diagnostic save failed; standard raw results retained: " + exception.Message;
                        _metadata.warnings.Add(warning);
                        LastSummary.warnings.Add(warning);
                    }
                    _collector.SaveEvidence(LastDirectory);
                    Saved = BenchmarkOutput.Save(LastDirectory, _metadata, LastSummary, _frames, _count, _collector) && diagnosticsSaved;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("Benchmark finalize failed: " + exception + "; partial output: " + LastDirectory);
                Saved = false;
            }
            finally
            {
                if (_shadowTrace != null)
                {
                    try
                    {
                        _shadowTrace.Save(System.IO.Path.Combine(LastDirectory, "shadow-trace.json"));
                    }
                    catch (Exception exception)
                    {
                        Saved = false;
                        Debug.LogError("Shadow trace save failed: " + exception);
                    }
                    finally
                    {
                        _shadowTrace.Dispose();
                        _shadowTrace = null;
                    }
                }
                _collector?.Dispose();
                StopCapture();
                _diagnostics?.Dispose();
                _diagnostics = null;
                StopCapture();
                try
                {
                    _worldState?.Dispose();
                }
                catch (Exception exception)
                {
                    Saved = false;
                    Debug.LogError("Benchmark weather restore failed: " + exception);
                }
                _worldState = null;
                if (_cameraSaved && _camera != null)
                {
                    _camera.transform.localPosition = _savedLocalPosition;
                    _camera.transform.localRotation = _savedLocalRotation;
                }
                _cameraSaved = false;
                _finishing = false;
                Debug.Log("RVM Benchmark " + (Saved ? "saved" : "failed") + ": " + LastDirectory + "; " + _error);
                Completed?.Invoke(this);
            }
        }

        private void StartCapture()
        {
            if (!_scenario.ProfilerCapture)
            {
                return;
            }
            if (!Profiler.supported || Profiler.enableBinaryLog)
            {
                throw new InvalidOperationException("Profiler capture unavailable or another binary capture is active.");
            }
            _savedProfilerEnabled = Profiler.enabled;
            _savedBinaryLog = Profiler.enableBinaryLog;
            _savedProfilerFile = Profiler.logFile;
            _ownsCapture = true;
            Profiler.logFile = System.IO.Path.Combine(LastDirectory, "profiler.raw");
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;
            _metadata.warnings.Add("Diagnostic binary capture changes overhead and is not comparable with Standard runs. No Deep Profiling is enabled by benchmark.");
        }

        private void StopCapture()
        {
            if (!_ownsCapture)
            {
                return;
            }
            _ownsCapture = false;
            if (_metadata != null && _metadata.captureEndUnityFrame < 0)
            {
                _metadata.captureEndUnityFrame = Time.frameCount;
            }
            Profiler.enableBinaryLog = _savedBinaryLog;
            Profiler.logFile = _savedProfilerFile;
            Profiler.enabled = _savedProfilerEnabled;
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _runtimeErrors++;
                if (_firstRuntimeError == null)
                {
                    _firstRuntimeError = condition;
                }
            }
        }

        private static double Seconds(long ticks)
        {
            return (double)ticks / BenchmarkClock.Frequency;
        }
    }
}
