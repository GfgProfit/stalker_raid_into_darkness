using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Core;
using XRay.Importer.Core.Anm;
using XRay.Importer.Editor;

namespace XRay.Importer.Tests
{
    [Category("RealData")]
    public sealed class AnmValidationTests
    {
        private const string REFERENCE_VARIABLE = "XRAY_ANM_REFERENCE_DIR";
        private const string FILES_VARIABLE = "XRAY_ANM_FILES_DIR";
        private const double BLENDER_POSITION_TOLERANCE = 5e-5;
        private const double BLENDER_ROTATION_TOLERANCE_DEGREES = 1e-3;
        private const double CLIP_POSITION_TOLERANCE = 2e-4;
        private const double CLIP_ROTATION_TOLERANCE_DEGREES = 0.02;

        [Test]
        public void CoreMatchesBlenderXRayAtBakedFrames()
        {
            var log = new StringBuilder("[X-Ray ANM] Core (BlenderXRay semantics) vs Blender 3.6 + blender-xray, integer frames:\n");
            var failures = new List<string>();
            foreach (string referencePath in GetReferences())
            {
                Dictionary<string, object> reference = LoadReference(referencePath, out XRayAnmMotion motion);
                var world = new AnmErrorStats();
                var camera = new AnmErrorStats();
                var channels = new AnmErrorStats();
                var engine = new AnmErrorStats();
                foreach (Dictionary<string, object> sample in Samples(reference))
                {
                    double frame = Convert.ToDouble(sample["frame"]);
                    if (Math.Abs(frame - Math.Round(frame)) > 1e-9)
                    {
                        continue;
                    }

                    double time = frame / motion.Fps;
                    XMatrix4 blender = AnmComparison.BlenderToNative(TestJson.Doubles(sample["world"]));
                    XMatrix4 ours = motion.EvaluateMatrix(time, XRayAnmSemantics.BlenderXRay);
                    world.Add(time, (blender.GetTranslation() - ours.GetTranslation()).Length, AnmComparison.AngleDegrees(blender.GetRotation(), ours.GetRotation()));
                    XMatrix4 blenderCamera = AnmComparison.BlenderToNative(TestJson.Doubles(sample["camera_world"]));
                    double forward = AnmComparison.VectorAngleDegrees(blenderCamera.TransformVector(new XVector3(0, -1, 0)), ours.TransformVector(new XVector3(0, 0, 1)));
                    double up = AnmComparison.VectorAngleDegrees(blenderCamera.TransformVector(new XVector3(0, 0, 1)), ours.TransformVector(new XVector3(0, 1, 0)));
                    camera.Add(time, (blenderCamera.GetTranslation() - ours.GetTranslation()).Length, Math.Max(forward, up));
                    double[] fcurves = TestJson.Doubles(sample["fcurves"]);
                    double[] expected = ChannelValuesAsBlenderFcurves(motion, time, XRayAnmSemantics.BlenderXRay);
                    double channelError = 0.0;
                    for (int i = 0; i < 6; i++)
                    {
                        channelError = Math.Max(channelError, Math.Abs(fcurves[i] - expected[i]));
                    }

                    channels.Add(time, channelError, 0.0);
                    XMatrix4 enginePose = motion.EvaluateMatrix(time, XRayAnmSemantics.Engine);
                    engine.Add(time, (blender.GetTranslation() - enginePose.GetTranslation()).Length, AnmComparison.AngleDegrees(blender.GetRotation(), enginePose.GetRotation()));
                }

                string file = Path.GetFileName(referencePath);
                log.AppendLine($"{file} (v{motion.Version}): world {world}; camera {camera}; raw fcurves max {channels.MaxPosition:E2}; Engine semantics vs Blender: {engine}");
                if (world.MaxPosition > BLENDER_POSITION_TOLERANCE || world.MaxRotationDegrees > BLENDER_ROTATION_TOLERANCE_DEGREES || camera.MaxRotationDegrees > BLENDER_ROTATION_TOLERANCE_DEGREES)
                {
                    failures.Add($"{file}: {world}; camera {camera}");
                }
            }

            Debug.Log(log.ToString());
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void ClipMatchesCoreAtFrameAndSubFrameTimes()
        {
            var log = new StringBuilder("[X-Ray ANM] AnimationClip vs Core, 8 samples per frame:\n");
            var failures = new List<string>();
            var target = new GameObject("anm_clip_probe");
            try
            {
                foreach (string file in GetFiles())
                {
                    XRayAnmMotion motion = XRayAnmReader.ReadFile(File.ReadAllBytes(file), Path.GetFileName(file));
                    foreach (XRayAnmSamplingMode mode in new[] { XRayAnmSamplingMode.Exact, XRayAnmSamplingMode.Adaptive, XRayAnmSamplingMode.FixedFps })
                    {
                        var warnings = new List<string>();
                        AnimationClip clip = XRayAnmClipBuilder.Build(motion, XRayAnmSemantics.Engine, 1.0f, mode, false, false, warnings);
                        AnmErrorStats stats = CompareClip(clip, motion, XRayAnmSemantics.Engine, target, 8);
                        int keys = CountKeys(clip);
                        log.AppendLine($"{Path.GetFileName(file)} {mode}: {stats}; Unity keys {keys}");
                        if (mode != XRayAnmSamplingMode.FixedFps && (stats.MaxPosition > CLIP_POSITION_TOLERANCE || stats.MaxRotationDegrees > CLIP_ROTATION_TOLERANCE_DEGREES))
                        {
                            failures.Add($"{Path.GetFileName(file)} {mode}: {stats}");
                        }

                        UnityEngine.Object.DestroyImmediate(clip);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }

            Debug.Log(log.ToString());
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void ClipAndPlayerMatchBlenderXRay()
        {
            var log = new StringBuilder("[X-Ray ANM] Unity (BlenderXRay semantics, Exact clip and XRayAnmPlayer) vs Blender:\n");
            var failures = new List<string>();
            var target = new GameObject("anm_blender_probe");
            try
            {
                foreach (string referencePath in GetReferences())
                {
                    Dictionary<string, object> reference = LoadReference(referencePath, out XRayAnmMotion motion);
                    var warnings = new List<string>();
                    AnimationClip clip = XRayAnmClipBuilder.Build(motion, XRayAnmSemantics.BlenderXRay, 1.0f, XRayAnmSamplingMode.Exact, false, false, warnings);
                    var asset = ScriptableObject.CreateInstance<XRayAnmAsset>();
                    asset.SetImportData("validation", motion, 1.0f, XRayAnmSemantics.BlenderXRay, true, clip);
                    var integer = new AnmErrorStats();
                    var subframes = new AnmErrorStats();
                    var player = new AnmErrorStats();
                    foreach (Dictionary<string, object> sample in Samples(reference))
                    {
                        double frame = Convert.ToDouble(sample["frame"]);
                        double clipTime = frame / motion.Fps - motion.StartTime;
                        if (clipTime < -1e-6 || clipTime > motion.Duration + 1e-6)
                        {
                            continue;
                        }

                        XMatrix4 blender = AnmComparison.BlenderToNative(TestJson.Doubles(sample["world"]));
                        AnmComparison.ClipPose(clip, target, (float)clipTime, out Vector3 clipPosition, out Quaternion clipRotation);
                        double position = (ToUnity(blender.GetTranslation()) - clipPosition).magnitude;
                        double rotation = AnmComparison.AngleDegrees(ToUnity(blender.GetRotation()), clipRotation);
                        (Math.Abs(frame - Math.Round(frame)) < 1e-9 ? integer : subframes).Add(clipTime, position, rotation);
                        asset.Evaluate((float)clipTime, out Vector3 playerPosition, out Quaternion playerRotation);
                        player.Add(clipTime, (ToUnity(blender.GetTranslation()) - playerPosition).magnitude, AnmComparison.AngleDegrees(ToUnity(blender.GetRotation()), playerRotation));
                    }

                    string file = Path.GetFileName(referencePath);
                    log.AppendLine($"{file}: clip @frames {integer}; clip @sub-frames (Blender interpolates its baked keys) {subframes}; player @all {player}");
                    if (integer.MaxPosition > CLIP_POSITION_TOLERANCE || integer.MaxRotationDegrees > CLIP_ROTATION_TOLERANCE_DEGREES)
                    {
                        failures.Add($"{file}: {integer}");
                    }

                    UnityEngine.Object.DestroyImmediate(clip);
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }

            Debug.Log(log.ToString());
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        public static AnmErrorStats CompareClip(AnimationClip clip, XRayAnmMotion motion, XRayAnmSemantics semantics, GameObject target, int samplesPerFrame)
        {
            var stats = new AnmErrorStats();
            int count = Math.Max(1, (int)Math.Round(motion.Duration * motion.Fps * samplesPerFrame));
            for (int i = 0; i <= count; i++)
            {
                double clipTime = motion.Duration * i / count;
                AnmComparison.ClipPose(clip, target, (float)clipTime, out Vector3 clipPosition, out Quaternion clipRotation);
                AnmComparison.CorePose(motion, motion.StartTime + clipTime, semantics, out Vector3 corePosition, out Quaternion coreRotation);
                stats.Add(clipTime, (clipPosition - corePosition).magnitude, AnmComparison.AngleDegrees(clipRotation, coreRotation));
            }

            return stats;
        }

        public static int CountKeys(AnimationClip clip)
        {
            int keys = 0;
            foreach (UnityEditor.EditorCurveBinding binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
            {
                keys += UnityEditor.AnimationUtility.GetEditorCurve(clip, binding).length;
            }

            return keys;
        }

        private static double[] ChannelValuesAsBlenderFcurves(XRayAnmMotion motion, double time, XRayAnmSemantics semantics)
        {
            return new[] { motion.EvaluateChannel(XRayAnmChannel.PositionX, time, semantics), motion.EvaluateChannel(XRayAnmChannel.PositionZ, time, semantics), motion.EvaluateChannel(XRayAnmChannel.PositionY, time, semantics), -motion.EvaluateChannel(XRayAnmChannel.RotationP, time, semantics), -motion.EvaluateChannel(XRayAnmChannel.RotationB, time, semantics), -motion.EvaluateChannel(XRayAnmChannel.RotationH, time, semantics) };
        }

        private static Vector3 ToUnity(XVector3 value)
        {
            return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
        }

        private static Quaternion ToUnity(XQuaternion value)
        {
            return new Quaternion((float)value.X, (float)value.Y, (float)value.Z, (float)value.W);
        }

        private static IEnumerable<Dictionary<string, object>> Samples(Dictionary<string, object> reference)
        {
            foreach (object sample in (List<object>)reference["samples"])
            {
                yield return (Dictionary<string, object>)sample;
            }
        }

        private static Dictionary<string, object> LoadReference(string referencePath, out XRayAnmMotion motion)
        {
            var reference = (Dictionary<string, object>)TestJson.Parse(File.ReadAllText(referencePath));
            string anm = Path.Combine(Environment.GetEnvironmentVariable(FILES_VARIABLE), (string)reference["file"]);
            motion = XRayAnmReader.ReadFile(File.ReadAllBytes(anm), Path.GetFileName(anm));
            return reference;
        }

        private static string[] GetReferences()
        {
            string folder = Environment.GetEnvironmentVariable(REFERENCE_VARIABLE);
            string files = Environment.GetEnvironmentVariable(FILES_VARIABLE);
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(files) || !Directory.Exists(folder) || !Directory.Exists(files))
            {
                Assert.Ignore($"Set {REFERENCE_VARIABLE} (Blender JSON from Tools~/blender_export_anm_reference.py) and {FILES_VARIABLE} (the *.anm files).");
            }

            string[] references = Directory.GetFiles(folder, "*.anm.json");
            Array.Sort(references, StringComparer.Ordinal);
            return references;
        }

        private static string[] GetFiles()
        {
            string files = Environment.GetEnvironmentVariable(FILES_VARIABLE);
            if (string.IsNullOrEmpty(files) || !Directory.Exists(files))
            {
                Assert.Ignore($"Set {FILES_VARIABLE} to a folder with *.anm files.");
            }

            string[] result = Directory.GetFiles(files, "*.anm");
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }
    }
}
