using System;
using System.Collections.Generic;
using XRay.Importer.Core.Anm;

namespace XRay.Importer.Editor
{
    public enum XRayAnmSamplingMode
    {
        Exact,
        Adaptive,
        FixedFps
    }

    public sealed class XRayAnmCurveKey
    {
        public double Time { get; set; }
        public double Value { get; set; }
        public double InSlope { get; set; }
        public double OutSlope { get; set; }
        public double InWeight { get; set; } = 1.0 / 3.0;
        public double OutWeight { get; set; } = 1.0 / 3.0;
        public bool WeightedIn { get; set; }
        public bool WeightedOut { get; set; }
        public bool SteppedIn { get; set; }
        public bool SteppedOut { get; set; }
    }

    public sealed class XRayAnmCurveSampler
    {
        private const int MAX_DEPTH = 10;
        private const double MIN_INTERVAL = 1e-3;
        private const double TIME_EPSILON = 1e-7;

        private readonly XRayAnmEnvelope _envelope;
        private readonly XRayAnmSemantics _semantics;
        private readonly double _fps;
        private readonly double _startTime;
        private readonly double _duration;
        private readonly double _factor;
        private readonly double _tolerance;
        private readonly SortedDictionary<double, XRayAnmCurveKey> _keys = new SortedDictionary<double, XRayAnmCurveKey>();

        public XRayAnmCurveSampler(XRayAnmEnvelope envelope, XRayAnmSemantics semantics, double fps, double startTime, double duration, double factor, double tolerance)
        {
            _envelope = envelope;
            _semantics = semantics;
            _fps = fps;
            _startTime = startTime;
            _duration = duration;
            _factor = factor;
            _tolerance = tolerance;
        }

        public int SampledIntervals { get; private set; }

        public List<XRayAnmCurveKey> Build(XRayAnmSamplingMode mode)
        {
            _keys.Clear();
            if (mode == XRayAnmSamplingMode.FixedFps)
            {
                BuildFixed();
            }
            else
            {
                List<double> breakpoints = GetBreakpoints();
                for (int i = 0; i + 1 < breakpoints.Count; i++)
                {
                    double a = breakpoints[i];
                    double b = breakpoints[i + 1];
                    if (mode == XRayAnmSamplingMode.Adaptive || !TryAnalytic(a, b))
                    {
                        SampledIntervals++;
                        Adaptive(a, b, 0);
                    }
                }

                if (breakpoints.Count == 1)
                {
                    GetKey(0.0).Value = Value(0.0);
                }
            }

            return new List<XRayAnmCurveKey>(_keys.Values);
        }

        public double Value(double clipTime)
        {
            return _envelope.Evaluate(_startTime + clipTime, _semantics, _fps) * _factor;
        }

        public static double EvaluateHermite(double a, double b, double va, double vb, double ma, double mb, double time)
        {
            double length = b - a;
            if (length <= 0.0)
            {
                return va;
            }

            double s = (time - a) / length;
            XRayAnmEnvelope.Hermite(s, out double h1, out double h2, out double h3, out double h4);
            return h1 * va + h2 * vb + h3 * ma * length + h4 * mb * length;
        }

        private List<double> GetBreakpoints()
        {
            var points = new List<double> { 0.0 };
            foreach (XRayAnmKey key in _envelope.Keys)
            {
                double time = key.Time - _startTime;
                if (time > TIME_EPSILON && time < _duration - TIME_EPSILON)
                {
                    points.Add(time);
                }
            }

            if (_duration > TIME_EPSILON)
            {
                points.Add(_duration);
            }

            points.Sort();
            var unique = new List<double>();
            foreach (double point in points)
            {
                if (unique.Count == 0 || point - unique[unique.Count - 1] > TIME_EPSILON)
                {
                    unique.Add(point);
                }
            }

            return unique;
        }

        private void BuildFixed()
        {
            double step = 1.0 / _fps;
            int count = Math.Max(1, (int)Math.Ceiling(_duration * _fps - TIME_EPSILON));
            for (int i = 0; i <= count; i++)
            {
                double time = Math.Min(i * step, _duration);
                XRayAnmCurveKey key = GetKey(time);
                key.Value = Value(time);
                double h = Math.Min(step * 0.01, 1e-4);
                double slope = (Value(time + h) - Value(time - h)) / (2.0 * h);
                key.InSlope = slope;
                key.OutSlope = slope;
            }
        }

        private bool TryAnalytic(double a, double b)
        {
            List<XRayAnmKey> keys = _envelope.Keys;
            double ta = _startTime + a;
            double tb = _startTime + b;
            XRayAnmCurveKey start = GetKey(a);
            XRayAnmCurveKey end = GetKey(b);
            start.Value = Value(a);
            end.Value = Value(b);
            if (keys.Count <= 1)
            {
                start.OutSlope = 0.0;
                end.InSlope = 0.0;
                return true;
            }

            if (tb <= keys[0].Time + TIME_EPSILON || ta >= keys[keys.Count - 1].Time - TIME_EPSILON)
            {
                XRayAnmBehavior behavior = tb <= keys[0].Time + TIME_EPSILON ? _envelope.PreBehavior : _envelope.PostBehavior;
                if (_semantics == XRayAnmSemantics.BlenderXRay && behavior != XRayAnmBehavior.Linear)
                {
                    behavior = XRayAnmBehavior.Constant;
                }

                if (behavior == XRayAnmBehavior.Constant)
                {
                    start.OutSlope = 0.0;
                    end.InSlope = 0.0;
                    return true;
                }

                if (behavior == XRayAnmBehavior.Linear)
                {
                    double slope = (end.Value - start.Value) / (b - a);
                    start.OutSlope = slope;
                    end.InSlope = slope;
                    return true;
                }

                return false;
            }

            int segment = _envelope.FindSegment((ta + tb) * 0.5);
            XRayAnmKey key0 = keys[segment];
            XRayAnmKey key1 = keys[segment + 1];
            double length = key1.Time - key0.Time;
            if (length <= TIME_EPSILON)
            {
                return false;
            }

            switch (key1.Shape)
            {
                case XRayAnmShape.Tcb:
                case XRayAnmShape.Hermite:
                case XRayAnmShape.Bezier1D:
                    _envelope.GetHermiteTangents(segment, _semantics, _fps, out double outgoing, out double incoming);
                    start.OutSlope = HermiteDerivative(key0, key1, outgoing, incoming, (ta - key0.Time) / length) / length * _factor;
                    end.InSlope = HermiteDerivative(key0, key1, outgoing, incoming, (tb - key0.Time) / length) / length * _factor;
                    return true;
                case XRayAnmShape.Linear:
                    double linear = (key1.Value - key0.Value) / length * _factor;
                    start.OutSlope = linear;
                    end.InSlope = linear;
                    return true;
                case XRayAnmShape.Stepped:
                    start.SteppedOut = true;
                    end.SteppedIn = true;
                    return true;
                case XRayAnmShape.Bezier2D:
                    return TryBezier2D(segment, ta, tb, start, end);
                default:
                    return false;
            }
        }

        private bool TryBezier2D(int segment, double ta, double tb, XRayAnmCurveKey start, XRayAnmCurveKey end)
        {
            XRayAnmKey key0 = _envelope.Keys[segment];
            XRayAnmKey key1 = _envelope.Keys[segment + 1];
            if (Math.Abs(ta - key0.Time) > TIME_EPSILON || Math.Abs(tb - key1.Time) > TIME_EPSILON)
            {
                return false;
            }

            _envelope.GetBezier2DControlPoints(segment, _semantics, _fps, out double x1, out double y1, out double x2, out double y2);
            double length = key1.Time - key0.Time;
            double outWeight = (x1 - key0.Time) / length;
            double inWeight = (key1.Time - x2) / length;
            if (outWeight <= 1e-6 || outWeight > 1.0 || inWeight <= 1e-6 || inWeight > 1.0)
            {
                return false;
            }

            start.WeightedOut = true;
            start.OutWeight = outWeight;
            start.OutSlope = (y1 - key0.Value) / (x1 - key0.Time) * _factor;
            end.WeightedIn = true;
            end.InWeight = inWeight;
            end.InSlope = (key1.Value - y2) / (key1.Time - x2) * _factor;
            return true;
        }

        private static double HermiteDerivative(XRayAnmKey key0, XRayAnmKey key1, double outgoing, double incoming, double s)
        {
            double h2 = 6.0 * s - 6.0 * s * s;
            double h1 = -h2;
            double h3 = 3.0 * s * s - 4.0 * s + 1.0;
            double h4 = 3.0 * s * s - 2.0 * s;
            return h1 * key0.Value + h2 * key1.Value + h3 * outgoing + h4 * incoming;
        }

        private void Adaptive(double a, double b, int depth)
        {
            double va = Value(a);
            double vb = Value(b);
            double h = Math.Min((b - a) * 1e-3, 1e-4);
            double ma = (-3.0 * va + 4.0 * Value(a + h) - Value(a + 2.0 * h)) / (2.0 * h);
            double mb = (3.0 * vb - 4.0 * Value(b - h) + Value(b - 2.0 * h)) / (2.0 * h);
            double error = 0.0;
            for (int i = 1; i < 8; i++)
            {
                double time = a + (b - a) * i / 8.0;
                error = Math.Max(error, Math.Abs(EvaluateHermite(a, b, va, vb, ma, mb, time) - Value(time)));
            }

            if (error <= _tolerance || depth >= MAX_DEPTH || b - a < MIN_INTERVAL)
            {
                XRayAnmCurveKey start = GetKey(a);
                XRayAnmCurveKey end = GetKey(b);
                start.Value = va;
                start.OutSlope = ma;
                end.Value = vb;
                end.InSlope = mb;
                return;
            }

            double middle = (a + b) * 0.5;
            Adaptive(a, middle, depth + 1);
            Adaptive(middle, b, depth + 1);
        }

        private XRayAnmCurveKey GetKey(double time)
        {
            if (_keys.TryGetValue(time, out XRayAnmCurveKey existing))
            {
                return existing;
            }

            var key = new XRayAnmCurveKey { Time = time };
            _keys.Add(time, key);
            return key;
        }
    }
}
