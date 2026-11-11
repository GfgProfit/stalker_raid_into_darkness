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
        [SerializeField] private int _frameCapacity = 65536;
        [SerializeField] private string[] _tags = { "Baseline" };

        public string ScenarioId => _scenarioId;
        public string ScenePath => _scenePath;
        public string[] PositionKeys => (string[])_positionKeys.Clone();
        public double PositionSeconds => _positionSeconds;
        public double WarmupSeconds => _warmupSeconds;
        public double StabilizationSeconds => _stabilizationSeconds;
        public BenchmarkMode Mode => _mode;
        public bool ProfilerCapture => _profilerCapture;
        public int FrameCapacity => _frameCapacity;
        public string[] Tags => (string[])_tags.Clone();

        public string Validate()
        {
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
