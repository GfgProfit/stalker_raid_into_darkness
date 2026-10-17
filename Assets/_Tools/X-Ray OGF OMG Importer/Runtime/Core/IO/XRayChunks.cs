using System.Collections.Generic;

namespace XRay.Importer.Core.IO
{
    public readonly struct XRayChunk
    {
        public XRayChunk(uint id, byte[] buffer, int offset, int length)
        {
            Id = id;
            Buffer = buffer;
            Offset = offset;
            Length = length;
        }

        public uint Id { get; }
        public byte[] Buffer { get; }
        public int Offset { get; }
        public int Length { get; }

        public XRayReader CreateReader(string context)
        {
            return new XRayReader(Buffer, Offset, Length, context);
        }
    }

    public static class XRayChunks
    {
        public const uint COMPRESSED_MASK = 0x80000000u;

        public static List<XRayChunk> ReadAll(XRayReader reader)
        {
            var chunks = new List<XRayChunk>();
            while (!reader.IsEnd)
            {
                if (reader.Remaining < 8)
                {
                    break;
                }

                uint id = reader.ReadUInt32();
                uint size = reader.ReadUInt32();
                if (size > (uint)reader.Remaining)
                {
                    throw new XRayFormatException($"{reader.Context}: chunk 0x{id:X} declares {size} bytes, only {reader.Remaining} left.");
                }

                if ((id & COMPRESSED_MASK) != 0)
                {
                    throw new XRayFormatException($"{reader.Context}: chunk 0x{id & ~COMPRESSED_MASK:X} is LZHUF-compressed. Compressed chunks are not supported; re-save the file uncompressed.");
                }

                chunks.Add(new XRayChunk(id, reader.Buffer, reader.AbsolutePosition, (int)size));
                reader.Skip((int)size);
            }

            return chunks;
        }

        public static Dictionary<uint, XRayChunk> ReadMap(XRayReader reader)
        {
            var map = new Dictionary<uint, XRayChunk>();
            foreach (XRayChunk chunk in ReadAll(reader))
            {
                map[chunk.Id] = chunk;
            }

            return map;
        }
    }
}
