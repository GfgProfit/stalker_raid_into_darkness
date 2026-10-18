using System;
using UnityEngine;
using XRay.Importer.Core;
using XRay.Importer.Core.Anm;
using XRay.Importer.Editor;

namespace XRay.Importer.Tests
{
    public sealed class AnmErrorStats
    {
        public double MaxPosition { get; private set; }
        public double MaxRotationDegrees { get; private set; }
        public double MaxPositionTime { get; private set; }
        public double MaxRotationTime { get; private set; }
        public int Samples { get; private set; }

        public void Add(double time, double position, double rotationDegrees)
        {
            Samples++;
            if (position > MaxPosition)
            {
                MaxPosition = position;
                MaxPositionTime = time;
            }

            if (rotationDegrees > MaxRotationDegrees)
            {
                MaxRotationDegrees = rotationDegrees;
                MaxRotationTime = time;
            }
        }

        public override string ToString()
        {
            return $"samples {Samples}, max position {MaxPosition:E2} m (t={MaxPositionTime:0.###}), max rotation {MaxRotationDegrees:E2} deg (t={MaxRotationTime:0.###})";
        }
    }

    public static class AnmComparison
    {
        public static void CorePose(XRayAnmMotion motion, double time, XRayAnmSemantics semantics, out Vector3 position, out Quaternion rotation)
        {
            motion.Evaluate(time, semantics, out XVector3 nativePosition, out XVector3 hpb);
            XQuaternion q = XRayAnmTransform.ToRotation(hpb);
            position = new Vector3((float)nativePosition.X, (float)nativePosition.Y, (float)nativePosition.Z);
            rotation = new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
        }

        public static void ClipPose(AnimationClip clip, GameObject target, float time, out Vector3 position, out Quaternion rotation)
        {
            clip.SampleAnimation(target, time);
            position = target.transform.localPosition;
            rotation = target.transform.localRotation;
        }

        public static double AngleDegrees(Quaternion a, Quaternion b)
        {
            return AngleDegrees(new XQuaternion(a.x, a.y, a.z, a.w), new XQuaternion(b.x, b.y, b.z, b.w));
        }

        public static double AngleDegrees(XQuaternion a, XQuaternion b)
        {
            return XRayAnmRotationSampler.Angle(a.Normalized(), b.Normalized());
        }

        public static XMatrix4 BlenderToNative(double[] rowMajor)
        {
            int[] map = { 0, 2, 1, 3 };
            var native = new double[16];
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    native[row * 4 + column] = rowMajor[map[row] * 4 + map[column]];
                }
            }

            return XMatrix4.FromArray(native);
        }

        public static double VectorAngleDegrees(XVector3 a, XVector3 b)
        {
            double dot = XVector3.Dot(a.Normalized(), b.Normalized());
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot))) * 180.0 / Math.PI;
        }
    }
}
