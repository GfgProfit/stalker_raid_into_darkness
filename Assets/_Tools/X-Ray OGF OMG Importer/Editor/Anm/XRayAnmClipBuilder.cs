using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.Anm;

namespace XRay.Importer.Editor
{
    public static class XRayAnmClipBuilder
    {
        public const double POSITION_TOLERANCE = 1e-5;
        public const double ROTATION_TOLERANCE_DEGREES = 1e-3;

        private static readonly string[] POSITION_PROPERTIES = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
        private static readonly string[] ROTATION_PROPERTIES = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        private static readonly XRayAnmChannel[] POSITION_CHANNELS = { XRayAnmChannel.PositionX, XRayAnmChannel.PositionY, XRayAnmChannel.PositionZ };

        public static AnimationClip Build(XRayAnmMotion motion, XRayAnmSemantics semantics, float scale, XRayAnmSamplingMode mode, bool loop, bool loopPose, List<string> warnings)
        {
            double duration = motion.Duration;
            if (duration <= 0.0)
            {
                warnings.Add($"Frame range {motion.FrameStart}..{motion.FrameEnd} is empty; the clip contains a single pose.");
                duration = 0.0;
            }

            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            int sampled = 0;
            for (int i = 0; i < POSITION_CHANNELS.Length; i++)
            {
                var sampler = new XRayAnmCurveSampler(motion.GetEnvelope(POSITION_CHANNELS[i], semantics), semantics, motion.Fps, motion.StartTime, duration, scale, POSITION_TOLERANCE * Mathf.Max(scale, 1e-6f));
                curves.Add(CreateCurve(sampler.Build(mode)));
                bindings.Add(EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), POSITION_PROPERTIES[i]));
                sampled += sampler.SampledIntervals;
            }

            if (mode == XRayAnmSamplingMode.Exact && sampled > 0)
            {
                warnings.Add($"{sampled} position curve intervals (cropped Bezier 2D spans or periodic behaviors) have no exact Unity key representation and were sampled adaptively.");
            }

            var rotation = new XRayAnmRotationSampler(motion, semantics, duration, ROTATION_TOLERANCE_DEGREES);
            List<XRayAnmRotationKey> rotationKeys = rotation.Build(mode);
            for (int component = 0; component < ROTATION_PROPERTIES.Length; component++)
            {
                curves.Add(CreateRotationCurve(rotationKeys, component));
                bindings.Add(EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), ROTATION_PROPERTIES[component]));
            }

            var clip = new AnimationClip { name = motion.Name, frameRate = (float)motion.Fps, legacy = false };
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlend = loopPose;
            settings.startTime = 0.0f;
            settings.stopTime = (float)duration;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static AnimationCurve CreateCurve(List<XRayAnmCurveKey> source)
        {
            var keys = new Keyframe[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                XRayAnmCurveKey key = source[i];
                float inSlope = key.SteppedIn ? float.PositiveInfinity : (float)key.InSlope;
                float outSlope = key.SteppedOut ? float.PositiveInfinity : (float)key.OutSlope;
                var keyframe = new Keyframe((float)key.Time, (float)key.Value, inSlope, outSlope, (float)key.InWeight, (float)key.OutWeight);
                keyframe.weightedMode = key.WeightedIn && key.WeightedOut ? WeightedMode.Both : key.WeightedIn ? WeightedMode.In : key.WeightedOut ? WeightedMode.Out : WeightedMode.None;
                keys[i] = keyframe;
            }

            return Finish(keys, index => source[index].SteppedIn, index => source[index].SteppedOut);
        }

        private static AnimationCurve CreateRotationCurve(List<XRayAnmRotationKey> source, int component)
        {
            var keys = new Keyframe[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                XRayAnmRotationKey key = source[i];
                bool steppedIn = i > 0 && source[i - 1].SteppedOut;
                float inSlope = steppedIn ? float.PositiveInfinity : (float)key.InSlope[component];
                float outSlope = key.SteppedOut ? float.PositiveInfinity : (float)key.OutSlope[component];
                keys[i] = new Keyframe((float)key.Time, (float)key.Value[component], inSlope, outSlope);
            }

            return Finish(keys, index => index > 0 && source[index - 1].SteppedOut, index => source[index].SteppedOut);
        }

        private static AnimationCurve Finish(Keyframe[] keys, System.Func<int, bool> steppedIn, System.Func<int, bool> steppedOut)
        {
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyBroken(curve, i, true);
                AnimationUtility.SetKeyLeftTangentMode(curve, i, steppedIn(i) ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Free);
                AnimationUtility.SetKeyRightTangentMode(curve, i, steppedOut(i) ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Free);
            }

            return curve;
        }
    }
}
