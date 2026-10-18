using System;
using System.Collections.Generic;

namespace XRay.Importer.Core.Anm
{
    public sealed class XRayAnmEnvelope
    {
        private const double BEZIER_TIME_TOLERANCE = 1e-9;
        private const int BEZIER_MAX_ITERATIONS = 64;

        public XRayAnmBehavior PreBehavior { get; set; } = XRayAnmBehavior.Constant;
        public XRayAnmBehavior PostBehavior { get; set; } = XRayAnmBehavior.Constant;
        public List<XRayAnmKey> Keys { get; } = new List<XRayAnmKey>();
        public double StartTime => Keys.Count > 0 ? Keys[0].Time : 0.0;
        public double EndTime => Keys.Count > 0 ? Keys[Keys.Count - 1].Time : 0.0;

        public double Evaluate(double time, XRayAnmSemantics semantics, double fps)
        {
            int count = Keys.Count;
            if (count == 0)
            {
                return 0.0;
            }

            if (count == 1)
            {
                return Keys[0].Value;
            }

            XRayAnmKey first = Keys[0];
            XRayAnmKey last = Keys[count - 1];
            double offset = 0.0;
            if (time < first.Time)
            {
                if (!ApplyBehavior(semantics == XRayAnmSemantics.Engine ? PreBehavior : ToBlenderBehavior(PreBehavior), true, ref time, ref offset, out double value, semantics, fps))
                {
                    return value;
                }
            }
            else if (time > last.Time)
            {
                if (!ApplyBehavior(semantics == XRayAnmSemantics.Engine ? PostBehavior : ToBlenderBehavior(PostBehavior), false, ref time, ref offset, out double value, semantics, fps))
                {
                    return value;
                }
            }

            int segment = FindSegment(time);
            XRayAnmKey key0 = Keys[segment];
            XRayAnmKey key1 = Keys[segment + 1];
            if (time == key0.Time)
            {
                return key0.Value + offset;
            }

            if (time == key1.Time)
            {
                return key1.Value + offset;
            }

            return EvaluateSegment(segment, time, semantics, fps) + offset;
        }

        public int FindSegment(double time)
        {
            int segment = 0;
            while (segment + 2 < Keys.Count && time > Keys[segment + 1].Time)
            {
                segment++;
            }

            return segment;
        }

        public double EvaluateSegment(int segment, double time, XRayAnmSemantics semantics, double fps)
        {
            XRayAnmKey key0 = Keys[segment];
            XRayAnmKey key1 = Keys[segment + 1];
            double length = key1.Time - key0.Time;
            double t = length > 0.0 ? (time - key0.Time) / length : 0.0;
            switch (key1.Shape)
            {
                case XRayAnmShape.Tcb:
                case XRayAnmShape.Hermite:
                case XRayAnmShape.Bezier1D:
                    GetHermiteTangents(segment, semantics, fps, out double outgoing, out double incoming);
                    Hermite(t, out double h1, out double h2, out double h3, out double h4);
                    return h1 * key0.Value + h2 * key1.Value + h3 * outgoing + h4 * incoming;
                case XRayAnmShape.Bezier2D:
                    return Bezier2D(segment, time, semantics, fps);
                case XRayAnmShape.Linear:
                    return key0.Value + t * (key1.Value - key0.Value);
                case XRayAnmShape.Stepped:
                    return key0.Value;
                default:
                    return 0.0;
            }
        }

        public void GetHermiteTangents(int segment, XRayAnmSemantics semantics, double fps, out double outgoing, out double incoming)
        {
            XRayAnmKey previous = segment > 0 ? Keys[segment - 1] : null;
            XRayAnmKey next = segment + 2 < Keys.Count ? Keys[segment + 2] : null;
            if (semantics == XRayAnmSemantics.Engine)
            {
                outgoing = OutgoingEngine(previous, Keys[segment], Keys[segment + 1]);
                incoming = IncomingEngine(Keys[segment], Keys[segment + 1], next);
            }
            else
            {
                outgoing = OutgoingBlender(previous, Keys[segment], Keys[segment + 1]);
                incoming = IncomingBlender(Keys[segment], Keys[segment + 1], next, fps);
            }
        }

        public void GetBezier2DControlPoints(int segment, XRayAnmSemantics semantics, double fps, out double x1, out double y1, out double x2, out double y2)
        {
            XRayAnmKey key0 = Keys[segment];
            XRayAnmKey key1 = Keys[segment + 1];
            double timeScale = semantics == XRayAnmSemantics.Engine ? 1.0 : 1.0 / fps;
            x1 = key0.Shape == XRayAnmShape.Bezier2D ? key0.Time + key0.Param2 * timeScale : key0.Time + (key1.Time - key0.Time) / 3.0;
            x2 = key1.Time + key1.Param0 * timeScale;
            if (key0.Shape == XRayAnmShape.Bezier2D)
            {
                y1 = key0.Value + key0.Param3;
            }
            else if (semantics == XRayAnmSemantics.Engine)
            {
                y1 = key0.Value + key0.Param1 / 3.0;
            }
            else
            {
                y1 = key0.Value + OutgoingBlender(segment > 0 ? Keys[segment - 1] : null, key0, key1) / 3.0;
            }

            y2 = key1.Value + key1.Param1;
        }

        public static void Hermite(double t, out double h1, out double h2, out double h3, out double h4)
        {
            double t2 = t * t;
            double t3 = t * t2;
            h2 = 3.0 * t2 - t3 - t3;
            h1 = 1.0 - h2;
            h4 = t3 - t2;
            h3 = h4 - t2 + t;
        }

        public static double Bezier(double x0, double x1, double x2, double x3, double t)
        {
            double t2 = t * t;
            double t3 = t2 * t;
            double c = 3.0 * (x1 - x0);
            double b = 3.0 * (x2 - x1) - c;
            double a = x3 - x0 - c - b;
            return a * t3 + b * t2 + c * t + x0;
        }

        private double Bezier2D(int segment, double time, XRayAnmSemantics semantics, double fps)
        {
            XRayAnmKey key0 = Keys[segment];
            XRayAnmKey key1 = Keys[segment + 1];
            GetBezier2DControlPoints(segment, semantics, fps, out double x1, out double y1, out double x2, out double y2);
            double tolerance = BEZIER_TIME_TOLERANCE;
            double t0 = 0.0;
            double t1 = 1.0;
            double t = 0.5;
            for (int i = 0; i < BEZIER_MAX_ITERATIONS; i++)
            {
                t = t0 + (t1 - t0) * 0.5;
                double v = Bezier(key0.Time, x1, x2, key1.Time, t);
                if (Math.Abs(time - v) <= tolerance)
                {
                    break;
                }

                if (v > time)
                {
                    t1 = t;
                }
                else
                {
                    t0 = t;
                }
            }

            return Bezier(key0.Value, y1, y2, key1.Value, t);
        }

        private bool ApplyBehavior(XRayAnmBehavior behavior, bool before, ref double time, ref double offset, out double value, XRayAnmSemantics semantics, double fps)
        {
            XRayAnmKey first = Keys[0];
            XRayAnmKey last = Keys[Keys.Count - 1];
            value = 0.0;
            switch (behavior)
            {
                case XRayAnmBehavior.Reset:
                    return false;
                case XRayAnmBehavior.Constant:
                    value = before ? first.Value : last.Value;
                    return false;
                case XRayAnmBehavior.Repeat:
                    time = Range(time, first.Time, last.Time, out int _);
                    return true;
                case XRayAnmBehavior.Oscillate:
                    time = Range(time, first.Time, last.Time, out int oscillations);
                    if (oscillations % 2 != 0)
                    {
                        time = last.Time - first.Time - time;
                    }

                    return true;
                case XRayAnmBehavior.OffsetRepeat:
                    time = Range(time, first.Time, last.Time, out int repeats);
                    offset = repeats * (last.Value - first.Value);
                    return true;
                case XRayAnmBehavior.Linear:
                    if (before)
                    {
                        double slope = OutgoingEngine(null, first, Keys[1]) / (Keys[1].Time - first.Time);
                        value = slope * (time - first.Time) + first.Value;
                    }
                    else
                    {
                        XRayAnmKey previous = Keys[Keys.Count - 2];
                        double slope = IncomingEngine(previous, last, null) / (last.Time - previous.Time);
                        value = slope * (time - last.Time) + last.Value;
                    }

                    return false;
                default:
                    value = before ? first.Value : last.Value;
                    return false;
            }
        }

        private static XRayAnmBehavior ToBlenderBehavior(XRayAnmBehavior behavior)
        {
            return behavior == XRayAnmBehavior.Linear ? XRayAnmBehavior.Linear : XRayAnmBehavior.Constant;
        }

        private static double Range(double value, double low, double high, out int cycles)
        {
            double range = high - low;
            if (range == 0.0)
            {
                cycles = 0;
                return low;
            }

            double result = low + value - range * Math.Floor(value / range);
            cycles = -(int)((result - value) / range + (result > value ? 0.5 : -0.5));
            return result;
        }

        private static double OutgoingEngine(XRayAnmKey previous, XRayAnmKey key0, XRayAnmKey key1)
        {
            double d = key1.Value - key0.Value;
            switch (key0.Shape)
            {
                case XRayAnmShape.Tcb:
                    double a = (1.0 - key0.Tension) * (1.0 + key0.Continuity) * (1.0 + key0.Bias);
                    double b = (1.0 - key0.Tension) * (1.0 - key0.Continuity) * (1.0 - key0.Bias);
                    if (previous != null)
                    {
                        double t = (key1.Time - key0.Time) / (key1.Time - previous.Time);
                        return t * (a * (key0.Value - previous.Value) + b * d);
                    }

                    return b * d;
                case XRayAnmShape.Linear:
                    if (previous != null)
                    {
                        double t = (key1.Time - key0.Time) / (key1.Time - previous.Time);
                        return t * (key0.Value - previous.Value + d);
                    }

                    return d;
                case XRayAnmShape.Bezier1D:
                case XRayAnmShape.Hermite:
                    double outgoing = key0.Param1;
                    if (previous != null)
                    {
                        outgoing *= (key1.Time - key0.Time) / (key1.Time - previous.Time);
                    }

                    return outgoing;
                case XRayAnmShape.Bezier2D:
                    double bezier = key0.Param3 * (key1.Time - key0.Time);
                    return Math.Abs(key0.Param2) > 1e-5 ? bezier / key0.Param2 : bezier * 1e5;
                default:
                    return 0.0;
            }
        }

        private static double IncomingEngine(XRayAnmKey key0, XRayAnmKey key1, XRayAnmKey next)
        {
            double d = key1.Value - key0.Value;
            switch (key1.Shape)
            {
                case XRayAnmShape.Linear:
                    if (next != null)
                    {
                        double t = (key1.Time - key0.Time) / (next.Time - key0.Time);
                        return t * (next.Value - key1.Value + d);
                    }

                    return d;
                case XRayAnmShape.Tcb:
                    double a = (1.0 - key1.Tension) * (1.0 - key1.Continuity) * (1.0 + key1.Bias);
                    double b = (1.0 - key1.Tension) * (1.0 + key1.Continuity) * (1.0 - key1.Bias);
                    if (next != null)
                    {
                        double t = (key1.Time - key0.Time) / (next.Time - key0.Time);
                        return t * (b * (next.Value - key1.Value) + a * d);
                    }

                    return a * d;
                case XRayAnmShape.Bezier1D:
                case XRayAnmShape.Hermite:
                    double incoming = key1.Param0;
                    if (next != null)
                    {
                        incoming *= (key1.Time - key0.Time) / (next.Time - key0.Time);
                    }

                    return incoming;
                case XRayAnmShape.Bezier2D:
                    double bezier = key1.Param1 * (key1.Time - key0.Time);
                    return Math.Abs(key1.Param0) > 1e-5 ? bezier / key1.Param0 : bezier * 1e5;
                default:
                    return 0.0;
            }
        }

        private static double OutgoingBlender(XRayAnmKey previous, XRayAnmKey key0, XRayAnmKey key1)
        {
            switch (key0.Shape)
            {
                case XRayAnmShape.Tcb:
                    double a = (1.0 - key0.Tension) * (1.0 + key0.Continuity) * (1.0 + key0.Bias);
                    double b = (1.0 - key0.Tension) * (1.0 - key0.Continuity) * (1.0 - key0.Bias);
                    double d = key1.Value - key0.Value;
                    if (previous != null)
                    {
                        double t = (key1.Time - key0.Time) / (key1.Time - previous.Time);
                        return t * (a * (key0.Value - previous.Value) + b * d);
                    }

                    return 0.5 * (a + b) * d;
                case XRayAnmShape.Hermite:
                case XRayAnmShape.Bezier1D:
                    double outgoing = key0.Param1;
                    if (previous != null)
                    {
                        outgoing *= (key1.Time - key0.Time) / (key1.Time - previous.Time);
                    }

                    return outgoing;
                default:
                    return 0.0;
            }
        }

        private static double IncomingBlender(XRayAnmKey key0, XRayAnmKey key1, XRayAnmKey next, double fps)
        {
            switch (key1.Shape)
            {
                case XRayAnmShape.Tcb:
                    double a = (1.0 - key1.Tension) * (1.0 - key1.Continuity) * (1.0 + key1.Bias);
                    double b = (1.0 - key1.Tension) * (1.0 + key1.Continuity) * (1.0 - key1.Bias);
                    double d = key1.Value - key0.Value;
                    if (next != null)
                    {
                        double t = (key1.Time - key0.Time) / (next.Time - key0.Time);
                        return t * (b * (next.Value - key1.Value) + a * d);
                    }

                    return 0.5 * (a + b) * d;
                case XRayAnmShape.Linear:
                    return (key1.Value - key0.Value) / ((key1.Time - key0.Time) * fps);
                case XRayAnmShape.Hermite:
                case XRayAnmShape.Bezier1D:
                    double incoming = key1.Param0;
                    if (next != null)
                    {
                        incoming *= (key1.Time - key0.Time) / (next.Time - key0.Time);
                    }

                    return incoming;
                default:
                    return 0.0;
            }
        }
    }
}
