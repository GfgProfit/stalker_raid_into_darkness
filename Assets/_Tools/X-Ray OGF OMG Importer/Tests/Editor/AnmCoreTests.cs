using System;
using System.Collections.Generic;
using NUnit.Framework;
using XRay.Importer.Core;
using XRay.Importer.Core.Anm;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Tests
{
    public sealed class AnmCoreTests
    {
        private const double EPSILON = 1e-6;

        [Test]
        public void Version5HeaderAndChannelsAreRead()
        {
            byte[] data = AnmTestWriter.Write("cam", 3, 63, 30.0f, XRayAnmFormat.VERSION_5, DistinctChannels());
            XRayAnmMotion motion = XRayAnmReader.ReadFile(data, "test.anm");
            Assert.AreEqual("cam", motion.Name);
            Assert.AreEqual(3, motion.FrameStart);
            Assert.AreEqual(63, motion.FrameEnd);
            Assert.AreEqual(30.0, motion.Fps, EPSILON);
            Assert.AreEqual(5, motion.Version);
            Assert.AreEqual(2.0, motion.Duration, EPSILON);
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(i + 1.0, motion.EvaluateChannel(XRayAnmFormat.FILE_ORDER_V3_V5[i], 0.0, XRayAnmSemantics.Engine), 1e-6);
            }

            Assert.IsEmpty(motion.Warnings);
        }

        [Test]
        public void Version3StoresFullPrecisionParameters()
        {
            List<AnmTestEnvelope> channels = DistinctChannels();
            channels[0] = new AnmTestEnvelope().Key(new XRayAnmKey { Time = 0.0, Value = 0.0, Shape = XRayAnmShape.Hermite, Param1 = 0.123456 }).Key(new XRayAnmKey { Time = 1.0, Value = 1.0, Shape = XRayAnmShape.Hermite, Param0 = 0.654321 });
            XRayAnmMotion motion = XRayAnmReader.ReadFile(AnmTestWriter.Write("v3", 0, 30, 30.0f, XRayAnmFormat.VERSION_3, channels), "v3.anm");
            XRayAnmEnvelope envelope = motion.GetEnvelope(XRayAnmChannel.PositionX, XRayAnmSemantics.Engine);
            Assert.AreEqual(0.123456, envelope.Keys[0].Param1, 1e-7);
            Assert.AreEqual(0.654321, envelope.Keys[1].Param0, 1e-7);
        }

        [Test]
        public void Version5ParametersAreQuantizedLikeTheEngine()
        {
            List<AnmTestEnvelope> channels = DistinctChannels();
            channels[0] = new AnmTestEnvelope().Key(new XRayAnmKey { Time = 0.0, Value = 0.0, Shape = XRayAnmShape.Tcb, Tension = 0.3, Continuity = -0.2, Bias = 0.1 }).Key(1.0, 2.0, XRayAnmShape.Tcb);
            XRayAnmMotion motion = XRayAnmReader.ReadFile(AnmTestWriter.Write("q", 0, 30, 30.0f, XRayAnmFormat.VERSION_5, channels), "q.anm");
            XRayAnmKey key = motion.GetEnvelope(XRayAnmChannel.PositionX, XRayAnmSemantics.Engine).Keys[0];
            Assert.AreEqual(AnmTestWriter.Dequantize(0.3), key.Tension, 1e-9);
            Assert.AreEqual(AnmTestWriter.Dequantize(-0.2), key.Continuity, 1e-9);
            Assert.AreEqual(AnmTestWriter.Dequantize(0.1), key.Bias, 1e-9);
            Assert.AreEqual(0.0, motion.GetEnvelope(XRayAnmChannel.PositionX, XRayAnmSemantics.Engine).Keys[1].Tension, 0.001);
        }

        [Test]
        public void Version4UsesEngineChannelOrder()
        {
            XRayAnmMotion motion = XRayAnmReader.ReadFile(AnmTestWriter.Write("v4", 0, 30, 30.0f, XRayAnmFormat.VERSION_4, DistinctChannels()), "v4.anm");
            Assert.AreEqual(4.0, motion.EvaluateChannel(XRayAnmChannel.RotationP, 0.0, XRayAnmSemantics.Engine), EPSILON);
            Assert.AreEqual(5.0, motion.EvaluateChannel(XRayAnmChannel.RotationH, 0.0, XRayAnmSemantics.Engine), EPSILON);
            Assert.AreEqual(4.0, motion.EvaluateChannel(XRayAnmChannel.RotationH, 0.0, XRayAnmSemantics.BlenderXRay), EPSILON);
            Assert.AreEqual(5.0, motion.EvaluateChannel(XRayAnmChannel.RotationP, 0.0, XRayAnmSemantics.BlenderXRay), EPSILON);
        }

        [Test]
        public void NegativeAndShiftedFrameRangesAreSigned()
        {
            XRayAnmMotion motion = XRayAnmReader.ReadFile(AnmTestWriter.Write("neg", -1, 59, 30.0f, XRayAnmFormat.VERSION_5, DistinctChannels()), "neg.anm");
            Assert.AreEqual(-1, motion.FrameStart);
            Assert.AreEqual(-1.0 / 30.0, motion.StartTime, EPSILON);
            Assert.AreEqual(2.0, motion.Duration, EPSILON);
        }

        [Test]
        public void LinearAndSteppedSpans()
        {
            var envelope = new XRayAnmEnvelope();
            envelope.Keys.Add(new XRayAnmKey { Time = 0.0, Value = 1.0, Shape = XRayAnmShape.Linear });
            envelope.Keys.Add(new XRayAnmKey { Time = 1.0, Value = 3.0, Shape = XRayAnmShape.Linear });
            envelope.Keys.Add(new XRayAnmKey { Time = 2.0, Value = -1.0, Shape = XRayAnmShape.Stepped });
            Assert.AreEqual(2.0, envelope.Evaluate(0.5, XRayAnmSemantics.Engine, 30.0), EPSILON);
            Assert.AreEqual(3.0, envelope.Evaluate(1.7, XRayAnmSemantics.Engine, 30.0), EPSILON);
            Assert.AreEqual(-1.0, envelope.Evaluate(2.0, XRayAnmSemantics.Engine, 30.0), EPSILON);
            Assert.AreEqual(-1.0, envelope.Evaluate(5.0, XRayAnmSemantics.Engine, 30.0), EPSILON);
            Assert.AreEqual(1.0, envelope.Evaluate(-5.0, XRayAnmSemantics.Engine, 30.0), EPSILON);
        }

        [Test]
        public void TcbMatchesKochanekBartelsReference()
        {
            var envelope = new XRayAnmEnvelope();
            envelope.Keys.Add(new XRayAnmKey { Time = 0.0, Value = 0.0, Shape = XRayAnmShape.Tcb });
            envelope.Keys.Add(new XRayAnmKey { Time = 1.0, Value = 1.0, Shape = XRayAnmShape.Tcb });
            envelope.Keys.Add(new XRayAnmKey { Time = 3.0, Value = 0.0, Shape = XRayAnmShape.Tcb });
            double outgoing = 1.0;
            double incomingEngine = 1.0 / 3.0 * (0.0 - 1.0) + 1.0 / 3.0 * 1.0;
            double expectedEngine = Reference(0.0, 1.0, outgoing, incomingEngine, 0.5);
            Assert.AreEqual(expectedEngine, envelope.Evaluate(0.5, XRayAnmSemantics.Engine, 30.0), 1e-9);
            double blenderOutgoing = 0.5 * 2.0 * 1.0;
            Assert.AreEqual(Reference(0.0, 1.0, blenderOutgoing, incomingEngine, 0.5), envelope.Evaluate(0.5, XRayAnmSemantics.BlenderXRay, 30.0), 1e-9);
            envelope.Keys[0].Tension = 0.5;
            envelope.Keys[0].Continuity = 0.25;
            envelope.Keys[0].Bias = -0.25;
            double b = (1.0 - 0.5) * (1.0 - 0.25) * (1.0 + 0.25);
            Assert.AreEqual(Reference(0.0, 1.0, b * 1.0, incomingEngine, 0.25), envelope.Evaluate(0.25, XRayAnmSemantics.Engine, 30.0), 1e-9);
        }

        [Test]
        public void HermiteUsesStoredTangents()
        {
            var envelope = new XRayAnmEnvelope();
            envelope.Keys.Add(new XRayAnmKey { Time = 0.0, Value = 0.0, Shape = XRayAnmShape.Hermite, Param1 = 2.0 });
            envelope.Keys.Add(new XRayAnmKey { Time = 2.0, Value = 1.0, Shape = XRayAnmShape.Hermite, Param0 = -1.0 });
            Assert.AreEqual(Reference(0.0, 1.0, 2.0, -1.0, 0.3), envelope.Evaluate(0.6, XRayAnmSemantics.Engine, 30.0), 1e-9);
        }

        [Test]
        public void Bezier2DFollowsControlPoints()
        {
            var envelope = new XRayAnmEnvelope();
            envelope.Keys.Add(new XRayAnmKey { Time = 0.0, Value = 0.0, Shape = XRayAnmShape.Bezier2D, Param2 = 0.5, Param3 = 1.0 });
            envelope.Keys.Add(new XRayAnmKey { Time = 1.5, Value = 1.0, Shape = XRayAnmShape.Bezier2D, Param0 = -0.5, Param1 = 0.0 });
            for (double u = 0.05; u < 1.0; u += 0.1)
            {
                double time = XRayAnmEnvelope.Bezier(0.0, 0.5, 1.0, 1.5, u);
                double expected = XRayAnmEnvelope.Bezier(0.0, 1.0, 1.0, 1.0, u);
                Assert.AreEqual(expected, envelope.Evaluate(time, XRayAnmSemantics.Engine, 30.0), 2e-4);
            }

            double blenderTime = 0.3;
            double engine = envelope.Evaluate(blenderTime, XRayAnmSemantics.Engine, 30.0);
            double blender = envelope.Evaluate(blenderTime, XRayAnmSemantics.BlenderXRay, 30.0);
            Assert.Greater(Math.Abs(engine - blender), 1e-3, "blender-xray adds Bezier 2D time handles in seconds to frame times.");
        }

        [Test]
        public void BehaviorsOutsideTheKeyRange()
        {
            var envelope = new XRayAnmEnvelope();
            envelope.Keys.Add(new XRayAnmKey { Time = 1.0, Value = 1.0, Shape = XRayAnmShape.Linear });
            envelope.Keys.Add(new XRayAnmKey { Time = 2.0, Value = 3.0, Shape = XRayAnmShape.Linear });
            envelope.PostBehavior = XRayAnmBehavior.Linear;
            envelope.PreBehavior = XRayAnmBehavior.Reset;
            Assert.AreEqual(0.0, envelope.Evaluate(0.5, XRayAnmSemantics.Engine, 30.0), EPSILON);
            Assert.AreEqual(5.0, envelope.Evaluate(3.0, XRayAnmSemantics.Engine, 30.0), EPSILON);
            envelope.PostBehavior = XRayAnmBehavior.Repeat;
            Assert.AreEqual(envelope.Evaluate(1.25, XRayAnmSemantics.Engine, 30.0), envelope.Evaluate(2.25, XRayAnmSemantics.Engine, 30.0), EPSILON);
            envelope.PostBehavior = XRayAnmBehavior.OffsetRepeat;
            Assert.AreEqual(envelope.Evaluate(1.25, XRayAnmSemantics.Engine, 30.0) + 2.0, envelope.Evaluate(2.25, XRayAnmSemantics.Engine, 30.0), EPSILON);
            envelope.PostBehavior = XRayAnmBehavior.Oscillate;
            Assert.AreEqual(-2.5, envelope.Evaluate(2.75, XRayAnmSemantics.Engine, 30.0), EPSILON, "OpenXRay evalEnvelope mirrors odd cycles as (end - start - time).");
            Assert.AreEqual(3.0, envelope.Evaluate(4.0, XRayAnmSemantics.BlenderXRay, 30.0), EPSILON, "blender-xray replaces periodic behaviors with CONSTANT.");
        }

        [Test]
        public void RotationMatchesEngineHpbMatrix()
        {
            var random = new Random(7);
            for (int i = 0; i < 200; i++)
            {
                double h = (random.NextDouble() - 0.5) * 8.0;
                double p = (random.NextDouble() - 0.5) * 8.0;
                double b = (random.NextDouble() - 0.5) * 8.0;
                XMatrix4 ours = XMatrix4.Rotation(XRayAnmTransform.ToRotation(new XVector3(h, p, b)));
                XMatrix4 engine = XRayAnmTransform.EngineHpbMatrix(-h, -p, -b);
                Assert.Less(XMatrix4.MaxAbsDifference(ours, engine), 1e-9);
            }
        }

        [Test]
        public void CorruptedFilesReportActionableErrors()
        {
            byte[] valid = AnmTestWriter.Write("x", 0, 30, 30.0f, XRayAnmFormat.VERSION_5, DistinctChannels());
            var truncated = new byte[valid.Length - 5];
            Array.Copy(valid, truncated, truncated.Length);
            Assert.Throws<XRayFormatException>(() => XRayAnmReader.ReadFile(truncated, "truncated.anm"));
            Assert.Throws<XRayFormatException>(() => XRayAnmReader.ReadFile(AnmTestWriter.Write("x", 0, 30, 30.0f, 7, DistinctChannels()), "version.anm"));
            Assert.Throws<XRayFormatException>(() => XRayAnmReader.ReadFile(AnmTestWriter.Write("x", 0, 30, 0.0f, XRayAnmFormat.VERSION_5, DistinctChannels()), "fps.anm"));
            Assert.Throws<XRayFormatException>(() => XRayAnmReader.ReadFile(new byte[] { 1, 2, 3, 4, 0, 0, 0, 0 }, "chunk.anm"));
            byte[] hugeCount = (byte[])valid.Clone();
            int countOffset = 8 + 2 + 14 + 2;
            hugeCount[countOffset] = 0xFF;
            hugeCount[countOffset + 1] = 0xFF;
            var exception = Assert.Throws<XRayFormatException>(() => XRayAnmReader.ReadFile(hugeCount, "count.anm"));
            StringAssert.Contains("65535 keys", exception.Message);
        }

        public static List<AnmTestEnvelope> DistinctChannels()
        {
            var channels = new List<AnmTestEnvelope>();
            for (int i = 0; i < 6; i++)
            {
                channels.Add(AnmTestEnvelope.Constant(i + 1.0));
            }

            return channels;
        }

        private static double Reference(double v0, double v1, double outgoing, double incoming, double t)
        {
            XRayAnmEnvelope.Hermite(t, out double h1, out double h2, out double h3, out double h4);
            return h1 * v0 + h2 * v1 + h3 * outgoing + h4 * incoming;
        }
    }
}
