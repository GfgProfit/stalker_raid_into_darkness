using System.Collections.Generic;

namespace XRay.Importer.Core.Anm
{
    public sealed class XRayAnmMotion
    {
        public string Name { get; set; }
        public int FrameStart { get; set; }
        public int FrameEnd { get; set; }
        public double Fps { get; set; }
        public ushort Version { get; set; }
        public XRayAnmEnvelope[] FileEnvelopes { get; } = new XRayAnmEnvelope[XRayAnmFormat.CHANNEL_COUNT];
        public List<string> Warnings { get; } = new List<string>();
        public double StartTime => Fps > 0.0 ? FrameStart / Fps : 0.0;
        public double EndTime => Fps > 0.0 ? FrameEnd / Fps : 0.0;
        public double Duration => EndTime - StartTime;

        public XRayAnmEnvelope GetEnvelope(XRayAnmChannel channel, XRayAnmSemantics semantics)
        {
            XRayAnmChannel[] order = XRayAnmFormat.GetFileOrder(Version, semantics);
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == channel)
                {
                    return FileEnvelopes[i];
                }
            }

            return null;
        }

        public double EvaluateChannel(XRayAnmChannel channel, double time, XRayAnmSemantics semantics)
        {
            XRayAnmEnvelope envelope = GetEnvelope(channel, semantics);
            return envelope != null ? envelope.Evaluate(time, semantics, Fps) : 0.0;
        }

        public void Evaluate(double time, XRayAnmSemantics semantics, out XVector3 position, out XVector3 rotationHpb)
        {
            position = new XVector3(EvaluateChannel(XRayAnmChannel.PositionX, time, semantics), EvaluateChannel(XRayAnmChannel.PositionY, time, semantics), EvaluateChannel(XRayAnmChannel.PositionZ, time, semantics));
            rotationHpb = new XVector3(EvaluateChannel(XRayAnmChannel.RotationH, time, semantics), EvaluateChannel(XRayAnmChannel.RotationP, time, semantics), EvaluateChannel(XRayAnmChannel.RotationB, time, semantics));
        }

        public XMatrix4 EvaluateMatrix(double time, XRayAnmSemantics semantics)
        {
            Evaluate(time, semantics, out XVector3 position, out XVector3 rotationHpb);
            return XMatrix4.TranslationRotation(position, XRayAnmTransform.ToRotation(rotationHpb));
        }
    }
}
