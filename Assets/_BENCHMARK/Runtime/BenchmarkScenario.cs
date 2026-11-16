using System;
using UnityEngine;

namespace Rvm.Benchmark
{
    public enum BenchmarkMode
    {
        Standard,
        Diagnostic
    }

    [CreateAssetMenu(menuName = "RVM/Benchmark Scenario")]
    public sealed class BenchmarkScenario : ScriptableObject
    {
        [SerializeField] private string _scenarioId = "l07_military";
        [SerializeField] private string _scenePath;
        [SerializeField] private string[] _positionKeys = { "Pos 1", "Pos 2", "Pos 3", "Pos 4" };
        [SerializeField] private double _positionSeconds = 5;
        [SerializeField] private double _warmupSeconds = 8;
        [SerializeField] private double _stabilizationSeconds = 0.5;
        [SerializeField] private BenchmarkMode _mode;
        [SerializeField] private bool _profilerCapture;
        [SerializeField] private double _captureSeconds = 2;
        [SerializeField] private int _frameCapacity = 65536;
        [SerializeField] private string[] _tags = { "Baseline" };
        [SerializeField] private bool _diagnosticCpu = true;
        [SerializeField] private bool _diagnosticGpu = true;
        [SerializeField] private bool _diagnosticRendering = true;
        [SerializeField] private bool _diagnosticMemory = true;
        [SerializeField] private double _snapshotSeconds = 1;
        [SerializeField] private bool _fixedWeatherTime;
        [SerializeField] private double _fixedTimeHours = 12;
        [SerializeField] private string _expectedWeatherProfile;
        [SerializeField] private string _displayName;
        [SerializeField] private string _sceneGuid;
        [SerializeField] private string _authoringGuid;
        [SerializeField] private Vector3[] _worldPositions;
        [SerializeField] private Quaternion[] _worldRotations;
        [SerializeField] private BenchmarkCameraSettings _cameraSettings;
        [SerializeField] private string _cameraProvenance;

        public string ScenarioId => _scenarioId;
        public string ScenePath => _scenePath;
        public string[] PositionKeys => (string[])_positionKeys.Clone();
        public double PositionSeconds => _positionSeconds;
        public double WarmupSeconds => _warmupSeconds;
        public double StabilizationSeconds => _stabilizationSeconds;
        public BenchmarkMode Mode => _mode;
        public bool ProfilerCapture => _profilerCapture;
        public double CaptureSeconds => _captureSeconds;
        public int FrameCapacity => _frameCapacity;
        public string[] Tags => (string[])_tags.Clone();
        public bool DiagnosticCpu => _diagnosticCpu;
        public bool DiagnosticGpu => _diagnosticGpu;
        public bool DiagnosticRendering => _diagnosticRendering;
        public bool DiagnosticMemory => _diagnosticMemory;
        public double SnapshotSeconds => _snapshotSeconds;
        public bool FixedWeatherTime => _fixedWeatherTime;
        public double FixedTimeHours => _fixedTimeHours;
        public string ExpectedWeatherProfile => _expectedWeatherProfile;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? _scenarioId : _displayName;
        public string SceneGuid => _sceneGuid;
        public string AuthoringGuid => _authoringGuid;
        public bool HasRoute => !string.IsNullOrEmpty(_sceneGuid);
        public BenchmarkCameraSettings CameraSettings => _cameraSettings;
        public string CameraProvenance => _cameraProvenance;

        public void ConfigureRoute(string id, string displayName, string scenePath, string sceneGuid, string authoringGuid, Vector3[] positions, Quaternion[] rotations, BenchmarkCameraSettings cameraSettings, string cameraProvenance)
        {
            string error = ValidatePoses(positions, rotations);
            if (error != null || string.IsNullOrEmpty(sceneGuid) || cameraSettings == null || cameraSettings.Validate() != null)
            {
                throw new ArgumentException(error ?? "A stable scene GUID and valid camera settings are required.");
            }
            _scenarioId = id;
            _displayName = displayName;
            _scenePath = scenePath;
            _sceneGuid = sceneGuid;
            _authoringGuid = authoringGuid;
            _worldPositions = (Vector3[])positions.Clone();
            _worldRotations = (Quaternion[])rotations.Clone();
            _cameraSettings = cameraSettings;
            _cameraProvenance = cameraProvenance;
        }

        public Vector3 Position(int index) => _worldPositions[index];
        public Quaternion Rotation(int index) => _worldRotations[index];

        public string ValidateRoute(string sceneGuid)
        {
            string error = Validate();
            if (error != null) return error;
            if (string.IsNullOrEmpty(_sceneGuid) || _sceneGuid != sceneGuid) return "Route belongs to a different scene, or its stable scene GUID is missing.";
            if (_cameraSettings == null) return "Route camera settings are missing.";
            return ValidatePoses(_worldPositions, _worldRotations) ?? _cameraSettings.Validate();
        }

        public static BenchmarkScenario SelectRoute(BenchmarkScenario[] profiles, string id, string sceneGuid)
        {
            BenchmarkScenario found = null;
            foreach (var profile in profiles)
            {
                if (profile == null || profile.ScenarioId != id) continue;
                if (found != null) throw new InvalidOperationException("Duplicate route ID: " + id);
                found = profile;
            }
            if (found == null) throw new InvalidOperationException("No benchmark route is available: " + id);
            string error = found.ValidateRoute(sceneGuid);
            if (error != null) throw new InvalidOperationException(error);
            return found;
        }

        private static string ValidatePoses(Vector3[] positions, Quaternion[] rotations)
        {
            if (positions == null || rotations == null || positions.Length != 4 || rotations.Length != 4) return "Exactly four world poses are required.";
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = positions[i];
                Quaternion q = rotations[i];
                if (!IsFinite(p.x) || !IsFinite(p.y) || !IsFinite(p.z) || !IsFinite(q.x) || !IsFinite(q.y) || !IsFinite(q.z) || !IsFinite(q.w) || Math.Abs(Quaternion.Dot(q, q) - 1) > 0.001) return "World pose must be finite with a unit quaternion: " + i;
                for (int j = 0; j < i; j++)
                {
                    if (positions[j] == p && Quaternion.Angle(rotations[j], q) < 0.0001f) return "Duplicate world pose: " + i;
                }
            }
            return null;
        }

        public void ConfigureSession(BenchmarkMode mode, bool fixedWeatherTime, double fixedTimeHours, string expectedProfile, bool capture, double captureSeconds)
        {
            _mode = mode;
            _fixedWeatherTime = fixedWeatherTime;
            _fixedTimeHours = fixedTimeHours;
            _expectedWeatherProfile = expectedProfile;
            _profilerCapture = capture;
            _captureSeconds = captureSeconds;
        }

        public string Validate()
        {
            if (!IsFinite(_snapshotSeconds) || _snapshotSeconds < 0.5 || !IsFinite(_fixedTimeHours) || _fixedTimeHours < 0 || _fixedTimeHours >= 24)
            {
                return "Diagnostic snapshot interval must be >= 0.5 seconds; fixed time must be in [0, 24).";
            }
            if (string.IsNullOrWhiteSpace(_scenarioId) || string.IsNullOrWhiteSpace(_scenePath))
            {
                return "Scenario ID and scene path are required.";
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(_scenarioId, "^[A-Za-z0-9_.-]+$"))
            {
                return "Scenario ID must contain only ASCII letters, digits, underscores, dots or hyphens.";
            }
            if (_positionKeys == null || _positionKeys.Length != 4)
            {
                return "Exactly four position keys are required.";
            }
            for (int i = 0; i < 4; i++)
            {
                if (string.IsNullOrWhiteSpace(_positionKeys[i]))
                {
                    return "Position key is empty.";
                }
                for (int j = 0; j < i; j++)
                {
                    if (_positionKeys[i] == _positionKeys[j])
                    {
                        return "Position keys must be unique.";
                    }
                }
            }
            if (!IsFinite(_positionSeconds) || _positionSeconds <= 0 || !IsFinite(_warmupSeconds) || _warmupSeconds < 0 || !IsFinite(_stabilizationSeconds) || _stabilizationSeconds < 0 || _stabilizationSeconds >= _positionSeconds)
            {
                return "Durations must be finite; position > 0 and 0 <= stabilization < position.";
            }
            if (_frameCapacity < 1024 || _frameCapacity > 262144)
            {
                return "Frame capacity must be between 1024 and 262144.";
            }
            if (!IsFinite(_captureSeconds) || _captureSeconds <= 0 || _captureSeconds > 4)
            {
                return "Capture duration must be in (0, 4] seconds.";
            }
            if (_profilerCapture && _mode != BenchmarkMode.Diagnostic)
            {
                return "Profiler capture requires Diagnostic mode.";
            }
            return null;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
