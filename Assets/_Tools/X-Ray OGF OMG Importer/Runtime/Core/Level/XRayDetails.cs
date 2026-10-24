using System.Collections.Generic;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Core.Level
{
    public sealed class XRayDetailModel
    {
        public int Index { get; internal set; }
        public string Shader { get; internal set; }
        public string Texture { get; internal set; }
        public uint Flags { get; internal set; }
        public float MinScale { get; internal set; }
        public float MaxScale { get; internal set; }
        public float[] Positions { get; internal set; }
        public float[] Uv { get; internal set; }
        public ushort[] Indices { get; internal set; }
        public float[] BoundsMin { get; } = new float[3];
        public float[] BoundsMax { get; } = new float[3];
        public int VertexCount => Positions.Length / 3;
        public bool NoWaving => (Flags & XRayDetails.DO_NO_WAVING) != 0;
    }

    public readonly struct XRayDetailSlot
    {
        public XRayDetailSlot(uint low, uint high, ushort palette0, ushort palette1, ushort palette2, ushort palette3)
        {
            Low = low;
            High = high;
            Palette0 = palette0;
            Palette1 = palette1;
            Palette2 = palette2;
            Palette3 = palette3;
        }

        public uint Low { get; }
        public uint High { get; }
        public ushort Palette0 { get; }
        public ushort Palette1 { get; }
        public ushort Palette2 { get; }
        public ushort Palette3 { get; }
        public float YBase => (Low & 0xFFF) * 0.2f - 200.0f;
        public float YHeight => ((Low >> 12) & 0xFF) * 0.1f;
        public int Hemi => (int)((High >> 16) & 0xF);
        public int Sun => (int)((High >> 12) & 0xF);
        public bool IsEmpty => GetId(0) == XRayDetails.ID_EMPTY && GetId(1) == XRayDetails.ID_EMPTY && GetId(2) == XRayDetails.ID_EMPTY && GetId(3) == XRayDetails.ID_EMPTY;

        public int GetId(int index)
        {
            switch (index)
            {
                case 0:
                    return (int)((Low >> 20) & 0x3F);
                case 1:
                    return (int)((Low >> 26) & 0x3F);
                case 2:
                    return (int)(High & 0x3F);
                default:
                    return (int)((High >> 6) & 0x3F);
            }
        }

        public ushort GetPalette(int index)
        {
            switch (index)
            {
                case 0:
                    return Palette0;
                case 1:
                    return Palette1;
                case 2:
                    return Palette2;
                default:
                    return Palette3;
            }
        }
    }

    public sealed class XRayDetails
    {
        public const uint VERSION = 3;
        public const uint DO_NO_WAVING = 1;
        public const int ID_EMPTY = 0x3F;
        public const float SLOT_SIZE = 2.0f;
        public const int SLOT_BYTES = 16;
        public const int VERTEX_BYTES = 20;

        public uint Version { get; private set; }
        public int OffsetX { get; private set; }
        public int OffsetZ { get; private set; }
        public int SizeX { get; private set; }
        public int SizeZ { get; private set; }
        public List<XRayDetailModel> Models { get; } = new List<XRayDetailModel>();
        public XRayDetailSlot[] Slots { get; private set; }

        public XRayDetailSlot GetSlot(int dbX, int dbZ)
        {
            return Slots[dbZ * SizeX + dbX];
        }

        public static XRayDetails Read(byte[] data, string context)
        {
            Dictionary<uint, XRayChunk> chunks = XRayChunks.ReadMap(new XRayReader(data, context));
            if (!chunks.TryGetValue(0, out XRayChunk headerChunk))
            {
                throw new XRayFormatException($"{context}: header chunk 0 is missing.");
            }

            XRayReader header = headerChunk.CreateReader(context + " HEADER");
            var details = new XRayDetails { Version = header.ReadUInt32() };
            if (details.Version != VERSION)
            {
                throw new XRayFormatException($"{context}: level.details version {details.Version} is not supported (expected {VERSION}).");
            }

            uint objectCount = header.ReadUInt32();
            details.OffsetX = header.ReadInt32();
            details.OffsetZ = header.ReadInt32();
            details.SizeX = header.ReadInt32();
            details.SizeZ = header.ReadInt32();
            if (objectCount > ID_EMPTY || details.SizeX < 0 || details.SizeZ < 0)
            {
                throw new XRayFormatException($"{context}: invalid header (objects {objectCount}, size {details.SizeX}x{details.SizeZ}).");
            }

            if (!chunks.TryGetValue(1, out XRayChunk modelsChunk))
            {
                throw new XRayFormatException($"{context}: detail models chunk 1 is missing.");
            }

            List<XRayChunk> models = XRayChunks.ReadAll(modelsChunk.CreateReader(context + " MODELS"));
            if (models.Count != objectCount)
            {
                throw new XRayFormatException($"{context}: header declares {objectCount} detail models, chunk 1 contains {models.Count}.");
            }

            foreach (XRayChunk modelChunk in models)
            {
                details.Models.Add(ReadModel(modelChunk, context + $" MODEL[{modelChunk.Id}]"));
            }

            if (!chunks.TryGetValue(2, out XRayChunk slotsChunk))
            {
                throw new XRayFormatException($"{context}: slots chunk 2 is missing.");
            }

            long expected = (long)details.SizeX * details.SizeZ * SLOT_BYTES;
            if (slotsChunk.Length != expected)
            {
                throw new XRayFormatException($"{context}: slots chunk has {slotsChunk.Length} bytes, expected {expected} for {details.SizeX}x{details.SizeZ} slots.");
            }

            XRayReader slots = slotsChunk.CreateReader(context + " SLOTS");
            details.Slots = new XRayDetailSlot[details.SizeX * details.SizeZ];
            for (int i = 0; i < details.Slots.Length; i++)
            {
                uint low = slots.ReadUInt32();
                uint high = slots.ReadUInt32();
                details.Slots[i] = new XRayDetailSlot(low, high, slots.ReadUInt16(), slots.ReadUInt16(), slots.ReadUInt16(), slots.ReadUInt16());
            }

            return details;
        }

        private static XRayDetailModel ReadModel(XRayChunk chunk, string context)
        {
            XRayReader reader = chunk.CreateReader(context);
            var model = new XRayDetailModel { Index = (int)chunk.Id, Shader = reader.ReadStringZ(), Texture = reader.ReadStringZ(), Flags = reader.ReadUInt32(), MinScale = reader.ReadSingle(), MaxScale = reader.ReadSingle() };
            uint vertices = reader.ReadUInt32();
            uint indices = reader.ReadUInt32();
            if (indices % 3 != 0 || vertices * VERTEX_BYTES + indices * 2 != reader.Remaining)
            {
                throw new XRayFormatException($"{context}: {vertices} vertices and {indices} indices do not match {reader.Remaining} bytes.");
            }

            model.Positions = new float[vertices * 3];
            model.Uv = new float[vertices * 2];
            for (int v = 0; v < vertices; v++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float value = reader.ReadSingle();
                    model.Positions[v * 3 + c] = value;
                    if (v == 0 || value < model.BoundsMin[c])
                    {
                        model.BoundsMin[c] = value;
                    }

                    if (v == 0 || value > model.BoundsMax[c])
                    {
                        model.BoundsMax[c] = value;
                    }
                }

                model.Uv[v * 2] = reader.ReadSingle();
                model.Uv[v * 2 + 1] = reader.ReadSingle();
            }

            model.Indices = new ushort[indices];
            for (int i = 0; i < indices; i++)
            {
                model.Indices[i] = reader.ReadUInt16();
                if (model.Indices[i] >= vertices)
                {
                    throw new XRayFormatException($"{context}: index {model.Indices[i]} at {i} is outside {vertices} vertices.");
                }
            }

            return model;
        }
    }
}
