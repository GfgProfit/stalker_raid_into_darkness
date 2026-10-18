using System;
using System.Collections.Generic;
using XRay.Importer.Core;
using XRay.Importer.Core.Anm;

namespace XRay.Importer.Editor
{
    public sealed class XRayAnmRotationKey
    {
        public double Time { get; set; }
        public double[] Value { get; } = new double[4];
        public double[] InSlope { get; } = new double[4];
        public double[] OutSlope { get; } = new double[4];
        public bool SteppedOut { get; set; }
    }

    public sealed class XRayAnmRotationSampler
    {
        private const int MAX_DEPTH = 12;
        private const double TIME_EPSILON = 1e-7;
        private const double DISCONTINUITY_OFFSET = 1e-5;
        private const double DISCONTINUITY_DEGREES = 0.01;

        private static readonly XRayAnmChannel[] ROTATION_CHANNELS = { XRayAnmChannel.RotationH, XRayAnmChannel.RotationP, XRayAnmChannel.RotationB };

        private readonly XRayAnmMotion _motion;
        private readonly XRayAnmSemantics _semantics;
        private readonly double _duration;
        private readonly double _toleranceDegrees;
        private readonly SortedDictionary<double, XRayAnmRotationKey> _keys = new SortedDictionary<double, XRayAnmRotationKey>();
        private XQuaternion _reference = XQuaternion.Identity;

        public XRayAnmRotationSampler(XRayAnmMotion motion, XRayAnmSemantics semantics, double duration, double toleranceDegrees)
        {
            _motion = motion;
            _semantics = semantics;
            _duration = duration;
            _toleranceDegrees = toleranceDegrees;
        }

        public List<XRayAnmRotationKey> Build(XRayAnmSamplingMode mode)
        {
            _keys.Clear();
            _reference = Rotation(0.0);
            if (mode == XRayAnmSamplingMode.FixedFps)
            {
                BuildFixed();
                return new List<XRayAnmRotationKey>(_keys.Values);
            }

            List<double> breakpoints = GetBreakpoints();
            if (breakpoints.Count == 1)
            {
                AddKey(0.0, Rotation(0.0), new double[4], new double[4]);
            }

            for (int i = 0; i + 1 < breakpoints.Count; i++)
            {
                double a = breakpoints[i];
                double b = breakpoints[i + 1];
                XQuaternion right = Rotation(b);
                XQuaternion left = Rotation(b - DISCONTINUITY_OFFSET);
                if (b - a > 4.0 * DISCONTINUITY_OFFSET && Angle(left, right) > DISCONTINUITY_DEGREES)
                {
                    Refine(a, b - DISCONTINUITY_OFFSET, 0);
                    _keys[b - DISCONTINUITY_OFFSET].SteppedOut = true;
                    AddKey(b, Align(right), new double[4], new double[4]);
                }
                else
                {
                    Refine(a, b, 0);
                }
            }

            return new List<XRayAnmRotationKey>(_keys.Values);
        }

        public XQuaternion Rotation(double clipTime)
        {
            double time = _motion.StartTime + clipTime;
            var hpb = new XVector3(_motion.EvaluateChannel(XRayAnmChannel.RotationH, time, _semantics), _motion.EvaluateChannel(XRayAnmChannel.RotationP, time, _semantics), _motion.EvaluateChannel(XRayAnmChannel.RotationB, time, _semantics));
            return XRayAnmTransform.ToRotation(hpb);
        }

        public static double Angle(XQuaternion a, XQuaternion b)
        {
            XQuaternion aligned = XQuaternion.Dot(a, b) < 0.0 ? b.Negated() : b;
            double dx = a.X - aligned.X;
            double dy = a.Y - aligned.Y;
            double dz = a.Z - aligned.Z;
            double dw = a.W - aligned.W;
            double sx = a.X + aligned.X;
            double sy = a.Y + aligned.Y;
            double sz = a.Z + aligned.Z;
            double sw = a.W + aligned.W;
            return 4.0 * Math.Atan2(Math.Sqrt(dx * dx + dy * dy + dz * dz + dw * dw), Math.Sqrt(sx * sx + sy * sy + sz * sz + sw * sw)) * 180.0 / Math.PI;
        }

        private List<double> GetBreakpoints()
        {
            var points = new List<double> { 0.0 };
            foreach (XRayAnmChannel channel in ROTATION_CHANNELS)
            {
                foreach (XRayAnmKey key in _motion.GetEnvelope(channel, _semantics).Keys)
                {
                    double time = key.Time - _motion.StartTime;
                    if (time > TIME_EPSILON && time < _duration - TIME_EPSILON)
                    {
                        points.Add(time);
                    }
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
                if (unique.Count == 0 || point - unique[unique.Count - 1] > 8.0 * DISCONTINUITY_OFFSET)
                {
                    unique.Add(point);
                }
            }

            return unique;
        }

        private void BuildFixed()
        {
            double step = 1.0 / _motion.Fps;
            int count = Math.Max(1, (int)Math.Ceiling(_duration * _motion.Fps - TIME_EPSILON));
            for (int i = 0; i <= count; i++)
            {
                double time = Math.Min(i * step, _duration);
                double h = Math.Min(step * 0.01, 1e-4);
                XQuaternion value = Align(Rotation(time));
                double[] slope = Difference(Align(Rotation(time - h), value), Align(Rotation(time + h), value), 2.0 * h);
                AddKey(time, value, slope, slope);
            }
        }

        private void Refine(double a, double b, int depth)
        {
            XQuaternion qa = Align(Rotation(a));
            XQuaternion qb = Align(Rotation(b), qa);
            double h = Math.Min((b - a) * 1e-3, 1e-4);
            double[] ma = OneSided(a, h, qa, true);
            double[] mb = OneSided(b, h, qb, false);
            double error = 0.0;
            for (int i = 1; i < 8; i++)
            {
                double time = a + (b - a) * i / 8.0;
                XQuaternion expected = Rotation(time);
                XQuaternion fitted = Hermite(a, b, qa, qb, ma, mb, time);
                error = Math.Max(error, Angle(expected, fitted));
            }

            if (error <= _toleranceDegrees || depth >= MAX_DEPTH)
            {
                SetOut(a, qa, ma);
                SetIn(b, qb, mb);
                return;
            }

            double middle = (a + b) * 0.5;
            Refine(a, middle, depth + 1);
            Refine(middle, b, depth + 1);
        }

        private double[] OneSided(double time, double h, XQuaternion value, bool forward)
        {
            double direction = forward ? 1.0 : -1.0;
            XQuaternion first = Align(Rotation(time + direction * h), value);
            XQuaternion second = Align(Rotation(time + direction * 2.0 * h), value);
            var slope = new double[4];
            double[] v0 = Components(value);
            double[] v1 = Components(first);
            double[] v2 = Components(second);
            for (int i = 0; i < 4; i++)
            {
                slope[i] = direction * (-3.0 * v0[i] + 4.0 * v1[i] - v2[i]) / (2.0 * h);
            }

            return slope;
        }

        private static XQuaternion Hermite(double a, double b, XQuaternion qa, XQuaternion qb, double[] ma, double[] mb, double time)
        {
            double[] va = Components(qa);
            double[] vb = Components(qb);
            var result = new double[4];
            for (int i = 0; i < 4; i++)
            {
                result[i] = XRayAnmCurveSampler.EvaluateHermite(a, b, va[i], vb[i], ma[i], mb[i], time);
            }

            return new XQuaternion(result[0], result[1], result[2], result[3]).Normalized();
        }

        private void SetOut(double time, XQuaternion value, double[] slope)
        {
            XRayAnmRotationKey key = GetOrAdd(time, value);
            Array.Copy(slope, key.OutSlope, 4);
        }

        private void SetIn(double time, XQuaternion value, double[] slope)
        {
            XRayAnmRotationKey key = GetOrAdd(time, value);
            Array.Copy(slope, key.InSlope, 4);
        }

        private XRayAnmRotationKey AddKey(double time, XQuaternion value, double[] inSlope, double[] outSlope)
        {
            XRayAnmRotationKey key = GetOrAdd(time, value);
            Array.Copy(inSlope, key.InSlope, 4);
            Array.Copy(outSlope, key.OutSlope, 4);
            return key;
        }

        private XRayAnmRotationKey GetOrAdd(double time, XQuaternion value)
        {
            if (_keys.TryGetValue(time, out XRayAnmRotationKey existing))
            {
                return existing;
            }

            var key = new XRayAnmRotationKey { Time = time };
            Array.Copy(Components(value), key.Value, 4);
            _keys.Add(time, key);
            _reference = value;
            return key;
        }

        private XQuaternion Align(XQuaternion value)
        {
            return Align(value, _reference);
        }

        private static XQuaternion Align(XQuaternion value, XQuaternion reference)
        {
            return XQuaternion.Dot(value, reference) < 0.0 ? value.Negated() : value;
        }

        private static double[] Difference(XQuaternion before, XQuaternion after, double length)
        {
            double[] a = Components(before);
            double[] b = Components(after);
            var result = new double[4];
            for (int i = 0; i < 4; i++)
            {
                result[i] = (b[i] - a[i]) / length;
            }

            return result;
        }

        private static double[] Components(XQuaternion value)
        {
            return new[] { value.X, value.Y, value.Z, value.W };
        }
    }
}
