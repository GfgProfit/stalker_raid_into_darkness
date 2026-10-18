namespace XRay.Importer.Core.Anm
{
    public enum XRayAnmShape
    {
        Tcb = 0,
        Hermite = 1,
        Bezier1D = 2,
        Linear = 3,
        Stepped = 4,
        Bezier2D = 5
    }

    public enum XRayAnmBehavior
    {
        Reset = 0,
        Constant = 1,
        Repeat = 2,
        Oscillate = 3,
        OffsetRepeat = 4,
        Linear = 5
    }

    public enum XRayAnmChannel
    {
        PositionX = 0,
        PositionY = 1,
        PositionZ = 2,
        RotationH = 3,
        RotationP = 4,
        RotationB = 5
    }

    public enum XRayAnmSemantics
    {
        Engine,
        BlenderXRay
    }

    public static class XRayAnmFormat
    {
        public const uint CHUNK_MAIN = 0x1100;
        public const ushort VERSION_3 = 3;
        public const ushort VERSION_4 = 4;
        public const ushort VERSION_5 = 5;
        public const int CHANNEL_COUNT = 6;
        public const float QUANTIZATION_MIN = -32.0f;
        public const float QUANTIZATION_MAX = 32.0f;
        public const int MAX_KEYS_PER_CHANNEL = 1 << 20;

        public static readonly XRayAnmChannel[] FILE_ORDER_V3_V5 = { XRayAnmChannel.PositionX, XRayAnmChannel.PositionY, XRayAnmChannel.PositionZ, XRayAnmChannel.RotationH, XRayAnmChannel.RotationP, XRayAnmChannel.RotationB };
        public static readonly XRayAnmChannel[] FILE_ORDER_V4 = { XRayAnmChannel.PositionX, XRayAnmChannel.PositionY, XRayAnmChannel.PositionZ, XRayAnmChannel.RotationP, XRayAnmChannel.RotationH, XRayAnmChannel.RotationB };

        public static bool IsSupported(ushort version)
        {
            return version == VERSION_3 || version == VERSION_4 || version == VERSION_5;
        }

        public static XRayAnmChannel[] GetFileOrder(ushort version, XRayAnmSemantics semantics)
        {
            return version == VERSION_4 && semantics == XRayAnmSemantics.Engine ? FILE_ORDER_V4 : FILE_ORDER_V3_V5;
        }
    }
}
