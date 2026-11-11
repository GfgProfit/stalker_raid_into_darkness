using System;

namespace Rvm.Benchmark
{
    public sealed class BenchmarkSequence
    {
        private readonly double _positionSeconds;
        private readonly double _warmupSeconds;
        private double _phaseStart;
        private double _lastNow;
        private bool _started;

        public int Position { get; private set; } = -1;
        public bool Complete { get; private set; }
        public bool Measuring => _started && Position >= 0 && !Complete;
        public double PhaseStart => _phaseStart;

        public BenchmarkSequence(double positionSeconds, double warmupSeconds)
        {
            if (double.IsNaN(positionSeconds) || double.IsInfinity(positionSeconds) || positionSeconds <= 0 || double.IsNaN(warmupSeconds) || double.IsInfinity(warmupSeconds) || warmupSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(positionSeconds));
            }
            _positionSeconds = positionSeconds;
            _warmupSeconds = warmupSeconds;
        }

        public void Start(double now)
        {
            if (_started || double.IsNaN(now) || double.IsInfinity(now))
            {
                throw new InvalidOperationException("Sequence cannot start.");
            }
            _started = true;
            _phaseStart = now;
            _lastNow = now;
            Position = _warmupSeconds > 0 ? -1 : 0;
        }

        public bool Advance(double now)
        {
            if (!_started || double.IsNaN(now) || double.IsInfinity(now) || now < _lastNow)
            {
                throw new InvalidOperationException("A monotonic started sequence is required.");
            }
            _lastNow = now;
            if (Complete || now - _phaseStart < (Position < 0 ? _warmupSeconds : _positionSeconds))
            {
                return false;
            }
            _phaseStart = now;
            Position++;
            Complete = Position == 4;
            return true;
        }

        public int WarmupPosition(double now)
        {
            return _warmupSeconds <= 0 ? 0 : Math.Min(3, Math.Max(0, (int)((now - _phaseStart) * 4 / _warmupSeconds)));
        }
    }
}
