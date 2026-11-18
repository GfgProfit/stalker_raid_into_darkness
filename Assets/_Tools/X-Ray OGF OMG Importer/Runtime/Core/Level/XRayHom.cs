using System;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Core.Level
{
    public readonly struct XRayHomTriangle
    {
        public XRayHomTriangle(XVector3 a, XVector3 b, XVector3 c, uint flags)
        {
            A = a;
            B = b;
            C = c;
            Flags = flags;
        }

        public XVector3 A { get; }
        public XVector3 B { get; }
        public XVector3 C { get; }
        public uint Flags { get; }
        public bool DoubleSided => Flags != 0;
    }

    public static class XRayHom
    {
        private const uint TRIANGLES_CHUNK = 1;
        private const int TRIANGLE_SIZE = 40;

        public static XRayHomTriangle[] Read(byte[] data, string context)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var reader = new XRayReader(data, context);
            XRayChunk? triangles = null;
            foreach (XRayChunk chunk in XRayChunks.ReadAll(reader))
            {
                if (chunk.Id != TRIANGLES_CHUNK)
                {
                    continue;
                }

                if (triangles.HasValue)
                {
                    throw new XRayFormatException($"{context}: duplicate HOM triangle chunk 0x1.");
                }

                triangles = chunk;
            }

            if (!reader.IsEnd)
            {
                throw new XRayFormatException($"{context}: incomplete HOM chunk header.");
            }

            if (!triangles.HasValue)
            {
                throw new XRayFormatException($"{context}: missing HOM triangle chunk 0x1.");
            }

            XRayReader payload = triangles.Value.CreateReader(context + " HOM triangles");
            if (payload.Length % TRIANGLE_SIZE != 0)
            {
                throw new XRayFormatException($"{context}: HOM triangle payload is not a multiple of {TRIANGLE_SIZE} bytes.");
            }

            var result = new XRayHomTriangle[payload.Length / TRIANGLE_SIZE];
            for (int index = 0; index < result.Length; index++)
            {
                XVector3 a = payload.ReadVector3();
                XVector3 b = payload.ReadVector3();
                XVector3 c = payload.ReadVector3();
                uint flags = payload.ReadUInt32();
                if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
                {
                    throw new XRayFormatException($"{context}: HOM triangle {index} has non-finite coordinates.");
                }

                result[index] = new XRayHomTriangle(a, b, c, flags);
            }

            return result;
        }

        private static bool IsFinite(XVector3 value)
        {
            return double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
        }
    }
}
