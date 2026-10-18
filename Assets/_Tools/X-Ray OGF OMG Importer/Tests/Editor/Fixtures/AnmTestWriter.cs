using System.Collections.Generic;
using System.Text;
using XRay.Importer.Core.Anm;

namespace XRay.Importer.Tests
{
    public sealed class AnmTestEnvelope
    {
        public XRayAnmBehavior Pre { get; set; } = XRayAnmBehavior.Constant;
        public XRayAnmBehavior Post { get; set; } = XRayAnmBehavior.Constant;
        public List<XRayAnmKey> Keys { get; } = new List<XRayAnmKey>();

        public AnmTestEnvelope Key(double time, double value, XRayAnmShape shape)
        {
            Keys.Add(new XRayAnmKey { Time = time, Value = value, Shape = shape });
            return this;
        }

        public AnmTestEnvelope Key(XRayAnmKey key)
        {
            Keys.Add(key);
            return this;
        }

        public static AnmTestEnvelope Constant(double value)
        {
            return new AnmTestEnvelope().Key(0.0, value, XRayAnmShape.Tcb);
        }
    }

    public static class AnmTestWriter
    {
        public static byte[] Write(string name, int frameStart, int frameEnd, float fps, ushort version, IList<AnmTestEnvelope> fileOrderEnvelopes)
        {
            var body = new BinaryBuffer();
            body.Writer.Write(Encoding.ASCII.GetBytes(name));
            body.Writer.Write((byte)0);
            body.Writer.Write(frameStart);
            body.Writer.Write(frameEnd);
            body.Writer.Write(fps);
            body.Writer.Write(version);
            foreach (AnmTestEnvelope envelope in fileOrderEnvelopes)
            {
                WriteEnvelope(body, version, envelope);
            }

            var file = new BinaryBuffer();
            file.WriteChunk(XRayAnmFormat.CHUNK_MAIN, body);
            return file.ToArray();
        }

        public static ushort Quantize(double value)
        {
            double normalized = (value - XRayAnmFormat.QUANTIZATION_MIN) / (XRayAnmFormat.QUANTIZATION_MAX - XRayAnmFormat.QUANTIZATION_MIN);
            return (ushort)System.Math.Max(0, System.Math.Min(65535, System.Math.Round(normalized * 65535.0)));
        }

        public static double Dequantize(double value)
        {
            return (float)(Quantize(value) * (XRayAnmFormat.QUANTIZATION_MAX - XRayAnmFormat.QUANTIZATION_MIN) / 65535.0f + XRayAnmFormat.QUANTIZATION_MIN);
        }

        private static void WriteEnvelope(BinaryBuffer body, ushort version, AnmTestEnvelope envelope)
        {
            if (version > XRayAnmFormat.VERSION_3)
            {
                body.Writer.Write((byte)envelope.Pre);
                body.Writer.Write((byte)envelope.Post);
                body.Writer.Write((ushort)envelope.Keys.Count);
            }
            else
            {
                body.Writer.Write((int)envelope.Pre);
                body.Writer.Write((int)envelope.Post);
                body.Writer.Write((uint)envelope.Keys.Count);
            }

            foreach (XRayAnmKey key in envelope.Keys)
            {
                body.Writer.Write((float)key.Value);
                body.Writer.Write((float)key.Time);
                if (version > XRayAnmFormat.VERSION_3)
                {
                    body.Writer.Write((byte)key.Shape);
                    if (key.Shape != XRayAnmShape.Stepped)
                    {
                        body.Writer.Write(Quantize(key.Tension));
                        body.Writer.Write(Quantize(key.Continuity));
                        body.Writer.Write(Quantize(key.Bias));
                        body.Writer.Write(Quantize(key.Param0));
                        body.Writer.Write(Quantize(key.Param1));
                        body.Writer.Write(Quantize(key.Param2));
                        body.Writer.Write(Quantize(key.Param3));
                    }
                }
                else
                {
                    body.Writer.Write((uint)key.Shape);
                    body.Writer.Write((float)key.Tension);
                    body.Writer.Write((float)key.Continuity);
                    body.Writer.Write((float)key.Bias);
                    body.Writer.Write((float)key.Param0);
                    body.Writer.Write((float)key.Param1);
                    body.Writer.Write((float)key.Param2);
                    body.Writer.Write((float)key.Param3);
                }
            }
        }
    }
}
