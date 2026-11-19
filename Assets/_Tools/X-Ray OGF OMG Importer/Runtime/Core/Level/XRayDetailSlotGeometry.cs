using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Core.Level
{
    public sealed class XRayDetailSlotGeometry
    {
        public const int VERSION = 1;

        private const uint MAGIC = 0x53475258;
        private const int HEADER_BYTES = 7 * sizeof(int);
        private const int MAX_DATA_BYTES = 64 * 1024 * 1024;

        private readonly byte[] _bytes;
        private readonly int _dataStart;
        private readonly int _triangleCount;

        private XRayDetailSlotGeometry(byte[] bytes, int dataStart, int triangleCount)
        {
            _bytes = bytes;
            _dataStart = dataStart;
            _triangleCount = triangleCount;
        }

        public static byte[] Bake(XRayDetails details, XRayCollisionModel collision)
        {
            int dataStart = GetDataStart(details);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(MAGIC);
            writer.Write(VERSION);
            writer.Write(details.SizeX);
            writer.Write(details.SizeZ);
            writer.Write(details.OffsetX);
            writer.Write(details.OffsetZ);
            writer.Write(collision.TriangleCount);
            var offsets = new int[details.Slots.Length + 1];
            stream.SetLength(dataStart);
            stream.Position = dataStart;
            var triangles = new List<int>();
            var seen = new HashSet<int>();
            for (int index = 0; index < details.Slots.Length; index++)
            {
                offsets[index] = checked((int)(stream.Position - dataStart));
                XRayDetailSlot slot = details.Slots[index];
                if (slot.IsEmpty) continue;
                var bounds = new XRayDetailSlotBounds(slot, index % details.SizeX - details.OffsetX, index / details.SizeX - details.OffsetZ);
                bounds.Query(collision, triangles, seen);
                int previous = 0;
                foreach (int face in triangles)
                {
                    int delta = face - previous;
                    uint encoded = unchecked((uint)((delta << 1) ^ (delta >> 31)));
                    while (encoded >= 0x80)
                    {
                        writer.Write((byte)(0x80 | (encoded & 0x7f)));
                        encoded >>= 7;
                    }

                    writer.Write((byte)encoded);
                    previous = face;
                }

                if (stream.Position > MAX_DATA_BYTES) throw new XRayFormatException("Grass slot geometry exceeds the asset size limit.");
            }

            offsets[offsets.Length - 1] = checked((int)(stream.Position - dataStart));
            stream.Position = HEADER_BYTES;
            foreach (int offset in offsets) writer.Write(offset);
            return stream.ToArray();
        }

        public static XRayDetailSlotGeometry Read(byte[] bytes, XRayDetails details, XRayCollisionModel collision)
        {
            int dataStart = GetDataStart(details);
            if (bytes == null || dataStart > bytes.Length) throw new XRayFormatException("Grass slot geometry index is truncated.");
            if (bytes.Length > MAX_DATA_BYTES) throw new XRayFormatException("Grass slot geometry exceeds the asset size limit.");
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != MAGIC || reader.ReadInt32() != VERSION) throw new XRayFormatException("Grass slot geometry header or version is invalid.");
            if (reader.ReadInt32() != details.SizeX || reader.ReadInt32() != details.SizeZ || reader.ReadInt32() != details.OffsetX || reader.ReadInt32() != details.OffsetZ || reader.ReadInt32() != collision.TriangleCount) throw new XRayFormatException("Grass slot geometry does not match the source dimensions or collision face count.");

            int dataLength = bytes.Length - dataStart;
            var geometry = new XRayDetailSlotGeometry(bytes, dataStart, collision.TriangleCount);
            int start = geometry.ReadOffset(0);
            if (start != 0) throw new XRayFormatException("Grass slot geometry must begin at the start of the face data.");
            for (int index = 0; index < details.Slots.Length; index++)
            {
                int end = geometry.ReadOffset(index + 1);
                if (end < start || end > dataLength || end != start && details.Slots[index].IsEmpty) throw new XRayFormatException($"Grass slot geometry range is invalid at slot {index}.");
                int position = dataStart + start;
                int limit = dataStart + end;
                int face = 0;
                while (position < limit) face = geometry.ReadFace(ref position, limit, face);
                start = end;
            }

            if (start != dataLength) throw new XRayFormatException("Grass slot geometry contains trailing face data.");
            return geometry;
        }

        internal void GetFaces(int slotIndex, List<int> faces)
        {
            faces.Clear();
            int position = _dataStart + ReadOffset(slotIndex);
            int end = _dataStart + ReadOffset(slotIndex + 1);
            int face = 0;
            while (position < end)
            {
                face = ReadFace(ref position, end, face);
                faces.Add(face);
            }
        }

        private int ReadOffset(int slotIndex)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(HEADER_BYTES + slotIndex * sizeof(int), sizeof(int)));
        }

        private static int GetDataStart(XRayDetails details)
        {
            long indexEnd = HEADER_BYTES + (details.Slots.LongLength + 1) * sizeof(int);
            if (indexEnd > MAX_DATA_BYTES) throw new XRayFormatException("Grass slot geometry index exceeds the asset size limit.");
            return (int)indexEnd;
        }

        private int ReadFace(ref int position, int end, int previous)
        {
            uint encoded = 0;
            for (int shift = 0; shift <= 28; shift += 7)
            {
                if (position >= end) break;
                byte item = _bytes[position++];
                if (shift == 28 && item > 0x0f) break;
                encoded |= (uint)(item & 0x7f) << shift;
                if (item >= 0x80) continue;
                int delta = (int)(encoded >> 1) ^ -((int)encoded & 1);
                long face = (long)previous + delta;
                if (face >= 0 && face < _triangleCount) return (int)face;
                break;
            }

            throw new XRayFormatException("Grass slot geometry contains an invalid face index.");
        }
    }

    internal readonly struct XRayDetailSlotBounds
    {
        private readonly float _maxX;
        private readonly float _maxZ;

        public XRayDetailSlotBounds(XRayDetailSlot slot, int x, int z)
        {
            MinX = x * XRayDetails.SLOT_SIZE - XRayDetailPlacement.EPS_L;
            MinY = slot.YBase - XRayDetailPlacement.EPS_L;
            MinZ = z * XRayDetails.SLOT_SIZE - XRayDetailPlacement.EPS_L;
            _maxX = x * XRayDetails.SLOT_SIZE + XRayDetails.SLOT_SIZE + XRayDetailPlacement.EPS_L;
            MaxY = slot.YBase + slot.YHeight + XRayDetailPlacement.EPS_L;
            _maxZ = z * XRayDetails.SLOT_SIZE + XRayDetails.SLOT_SIZE + XRayDetailPlacement.EPS_L;
        }

        public float MinX { get; }
        public float MinY { get; }
        public float MinZ { get; }
        public float MaxY { get; }

        public void Query(XRayCollisionModel collision, List<int> faces, HashSet<int> seen)
        {
            collision.QueryBox(MinX, MinY, MinZ, _maxX, MaxY, _maxZ, faces, seen);
        }
    }
}
