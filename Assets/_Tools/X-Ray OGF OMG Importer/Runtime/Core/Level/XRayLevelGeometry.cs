using System;
using System.Collections.Generic;
using System.IO;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Core.Level
{
    public enum XRayLevelVertexKind
    {
        Lightmap,
        VertexColor,
        Tree,
        Other
    }

    public readonly struct XRayVertexElement
    {
        public XRayVertexElement(int offset, byte type, byte method, byte usage, byte usageIndex)
        {
            Offset = offset;
            Type = type;
            Method = method;
            Usage = usage;
            UsageIndex = usageIndex;
        }

        public int Offset { get; }
        public byte Type { get; }
        public byte Method { get; }
        public byte Usage { get; }
        public byte UsageIndex { get; }
    }

    public sealed class XRayVertexBufferInfo
    {
        public int Index { get; internal set; }
        public List<XRayVertexElement> Elements { get; } = new List<XRayVertexElement>();
        public int Stride { get; internal set; }
        public int Count { get; internal set; }
        public long DataOffset { get; internal set; }
        public XRayLevelVertexKind Kind { get; internal set; }

        public string Describe()
        {
            var parts = new List<string>();
            foreach (XRayVertexElement element in Elements)
            {
                parts.Add($"{element.Offset}:{UsageName(element.Usage)}{element.UsageIndex}/{TypeName(element.Type)}");
            }

            return string.Join(" ", parts);
        }

        private static string UsageName(byte usage)
        {
            switch (usage)
            {
                case XRayLevelFormat.USAGE_POSITION:
                    return "POSITION";
                case XRayLevelFormat.USAGE_NORMAL:
                    return "NORMAL";
                case XRayLevelFormat.USAGE_TEXCOORD:
                    return "TEXCOORD";
                case XRayLevelFormat.USAGE_TANGENT:
                    return "TANGENT";
                case XRayLevelFormat.USAGE_BINORMAL:
                    return "BINORMAL";
                case XRayLevelFormat.USAGE_COLOR:
                    return "COLOR";
                default:
                    return "USAGE" + usage;
            }
        }

        private static string TypeName(byte type)
        {
            switch (type)
            {
                case XRayLevelFormat.DECL_FLOAT2:
                    return "FLOAT2";
                case XRayLevelFormat.DECL_FLOAT3:
                    return "FLOAT3";
                case XRayLevelFormat.DECL_FLOAT4:
                    return "FLOAT4";
                case XRayLevelFormat.DECL_D3DCOLOR:
                    return "D3DCOLOR";
                case XRayLevelFormat.DECL_SHORT2:
                    return "SHORT2";
                case XRayLevelFormat.DECL_SHORT4:
                    return "SHORT4";
                default:
                    return "TYPE" + type;
            }
        }
    }

    public sealed class XRayIndexBufferInfo
    {
        public int Index { get; internal set; }
        public int Count { get; internal set; }
        public long DataOffset { get; internal set; }
    }

    public readonly struct XRaySlideWindow
    {
        public XRaySlideWindow(int offset, int triangles, int vertices)
        {
            Offset = offset;
            Triangles = triangles;
            Vertices = vertices;
        }

        public int Offset { get; }
        public int Triangles { get; }
        public int Vertices { get; }
    }

    public sealed class XRayLevelVertexBlock
    {
        public XRayLevelVertexBlock(int count, XRayLevelVertexKind kind)
        {
            Count = count;
            Kind = kind;
            Positions = new float[count * 3];
            Normals = new byte[count * 4];
            Tangents = new byte[count * 4];
            Binormals = new byte[count * 4];
            Uv = new float[count * 2];
            Lightmap = new float[count * 2];
            Colors = new byte[count * 4];
            WindFactor = new float[count];
        }

        public int Count { get; }
        public XRayLevelVertexKind Kind { get; }
        public float[] Positions { get; }
        public byte[] Normals { get; }
        public byte[] Tangents { get; }
        public byte[] Binormals { get; }
        public float[] Uv { get; }
        public float[] Lightmap { get; }
        public byte[] Colors { get; }
        public float[] WindFactor { get; }
        public bool HasNormal { get; internal set; }
        public bool HasTangent { get; internal set; }
        public bool HasBinormal { get; internal set; }
        public bool HasLightmap { get; internal set; }
        public bool HasColor { get; internal set; }
    }

    public sealed class XRayLevelGeometry : IDisposable
    {
        private readonly Stream _stream;
        private readonly BinaryReader _reader;
        private readonly string _context;

        private XRayLevelGeometry(string path, string context) : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16), context)
        {
        }

        private XRayLevelGeometry(Stream stream, string context)
        {
            _context = context;
            _stream = stream;
            _reader = new BinaryReader(_stream);
        }

        public ushort Version { get; private set; }
        public long Length => _stream.Length;
        public List<XRayVertexBufferInfo> VertexBuffers { get; } = new List<XRayVertexBufferInfo>();
        public List<XRayIndexBufferInfo> IndexBuffers { get; } = new List<XRayIndexBufferInfo>();
        public List<List<XRaySlideWindow>> SlideWindows { get; } = new List<List<XRaySlideWindow>>();

        public static XRayLevelGeometry Open(string path)
        {
            return Open(new XRayLevelGeometry(path, Path.GetFileName(path)));
        }

        public static XRayLevelGeometry Open(Stream stream, string context)
        {
            return Open(new XRayLevelGeometry(stream, context));
        }

        private static XRayLevelGeometry Open(XRayLevelGeometry geometry)
        {
            try
            {
                geometry.ReadDirectory();
            }
            catch
            {
                geometry.Dispose();
                throw;
            }

            return geometry;
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
        }

        public ushort[] ReadIndices(int buffer, int offset, int count)
        {
            if (buffer < 0 || buffer >= IndexBuffers.Count)
            {
                throw new XRayFormatException($"{_context}: index buffer {buffer} does not exist ({IndexBuffers.Count} buffers).");
            }

            XRayIndexBufferInfo info = IndexBuffers[buffer];
            if (offset < 0 || count < 0 || (long)offset + count > info.Count)
            {
                throw new XRayFormatException($"{_context}: index range {offset}+{count} is outside index buffer {buffer} ({info.Count} indices).");
            }

            byte[] bytes = ReadBytes(info.DataOffset + offset * 2L, count * 2);
            var indices = new ushort[count];
            Buffer.BlockCopy(bytes, 0, indices, 0, bytes.Length);
            return indices;
        }

        public XRayLevelVertexBlock ReadVertices(int buffer, int offset, int count)
        {
            if (buffer < 0 || buffer >= VertexBuffers.Count)
            {
                throw new XRayFormatException($"{_context}: vertex buffer {buffer} does not exist ({VertexBuffers.Count} buffers).");
            }

            XRayVertexBufferInfo info = VertexBuffers[buffer];
            if (offset < 0 || count < 0 || (long)offset + count > info.Count)
            {
                throw new XRayFormatException($"{_context}: vertex range {offset}+{count} is outside vertex buffer {buffer} ({info.Count} vertices).");
            }

            byte[] bytes = ReadBytes(info.DataOffset + (long)offset * info.Stride, count * info.Stride);
            return Decode(info, bytes, count);
        }

        private static XRayLevelVertexBlock Decode(XRayVertexBufferInfo info, byte[] bytes, int count)
        {
            var block = new XRayLevelVertexBlock(count, info.Kind);
            int tangentOffset = -1;
            int binormalOffset = -1;
            foreach (XRayVertexElement element in info.Elements)
            {
                if (element.Usage == XRayLevelFormat.USAGE_TANGENT)
                {
                    tangentOffset = element.Offset;
                }
                else if (element.Usage == XRayLevelFormat.USAGE_BINORMAL)
                {
                    binormalOffset = element.Offset;
                }
            }

            for (int v = 0; v < count; v++)
            {
                int baseOffset = v * info.Stride;
                foreach (XRayVertexElement element in info.Elements)
                {
                    int p = baseOffset + element.Offset;
                    switch (element.Usage)
                    {
                        case XRayLevelFormat.USAGE_POSITION:
                            block.Positions[v * 3] = BitConverter.ToSingle(bytes, p);
                            block.Positions[v * 3 + 1] = BitConverter.ToSingle(bytes, p + 4);
                            block.Positions[v * 3 + 2] = BitConverter.ToSingle(bytes, p + 8);
                            break;
                        case XRayLevelFormat.USAGE_NORMAL:
                            Buffer.BlockCopy(bytes, p, block.Normals, v * 4, 4);
                            block.HasNormal = true;
                            break;
                        case XRayLevelFormat.USAGE_TANGENT:
                            Buffer.BlockCopy(bytes, p, block.Tangents, v * 4, 4);
                            block.HasTangent = true;
                            break;
                        case XRayLevelFormat.USAGE_BINORMAL:
                            Buffer.BlockCopy(bytes, p, block.Binormals, v * 4, 4);
                            block.HasBinormal = true;
                            break;
                        case XRayLevelFormat.USAGE_COLOR:
                            Buffer.BlockCopy(bytes, p, block.Colors, v * 4, 4);
                            block.HasColor = true;
                            break;
                        case XRayLevelFormat.USAGE_TEXCOORD:
                            DecodeTexcoord(block, element, bytes, p, v, baseOffset, tangentOffset, binormalOffset);
                            break;
                    }
                }
            }

            return block;
        }

        private static void DecodeTexcoord(XRayLevelVertexBlock block, XRayVertexElement element, byte[] bytes, int p, int v, int baseOffset, int tangentOffset, int binormalOffset)
        {
            if (element.UsageIndex == 0)
            {
                switch (element.Type)
                {
                    case XRayLevelFormat.DECL_FLOAT2:
                        block.Uv[v * 2] = BitConverter.ToSingle(bytes, p);
                        block.Uv[v * 2 + 1] = BitConverter.ToSingle(bytes, p + 4);
                        break;
                    case XRayLevelFormat.DECL_SHORT2:
                        float du = tangentOffset >= 0 ? bytes[baseOffset + tangentOffset + 3] / 255.0f : 0.0f;
                        float dv = binormalOffset >= 0 ? bytes[baseOffset + binormalOffset + 3] / 255.0f : 0.0f;
                        block.Uv[v * 2] = (BitConverter.ToInt16(bytes, p) + du) * XRayLevelFormat.BASE_UV_SCALE;
                        block.Uv[v * 2 + 1] = (BitConverter.ToInt16(bytes, p + 2) + dv) * XRayLevelFormat.BASE_UV_SCALE;
                        break;
                    case XRayLevelFormat.DECL_SHORT4:
                        block.Uv[v * 2] = BitConverter.ToInt16(bytes, p) / XRayLevelFormat.TREE_QUANT;
                        block.Uv[v * 2 + 1] = BitConverter.ToInt16(bytes, p + 2) / XRayLevelFormat.TREE_QUANT;
                        block.WindFactor[v] = BitConverter.ToInt16(bytes, p + 4) / XRayLevelFormat.TREE_QUANT;
                        break;
                }

                return;
            }

            if (element.UsageIndex == 1)
            {
                if (element.Type == XRayLevelFormat.DECL_SHORT2)
                {
                    block.Lightmap[v * 2] = BitConverter.ToInt16(bytes, p) * XRayLevelFormat.LIGHTMAP_UV_SCALE;
                    block.Lightmap[v * 2 + 1] = BitConverter.ToInt16(bytes, p + 2) * XRayLevelFormat.LIGHTMAP_UV_SCALE;
                    block.HasLightmap = true;
                }
                else if (element.Type == XRayLevelFormat.DECL_FLOAT2)
                {
                    block.Lightmap[v * 2] = BitConverter.ToSingle(bytes, p);
                    block.Lightmap[v * 2 + 1] = BitConverter.ToSingle(bytes, p + 4);
                    block.HasLightmap = true;
                }
            }
        }

        private byte[] ReadBytes(long position, int count)
        {
            if (position < 0 || position + count > _stream.Length)
            {
                throw new XRayFormatException($"{_context}: read of {count} bytes at {position} exceeds the file ({_stream.Length} bytes).");
            }

            _stream.Position = position;
            byte[] bytes = _reader.ReadBytes(count);
            if (bytes.Length != count)
            {
                throw new XRayFormatException($"{_context}: unexpected end of file at {position + bytes.Length}.");
            }

            return bytes;
        }

        private void ReadDirectory()
        {
            long length = _stream.Length;
            long position = 0;
            var chunks = new Dictionary<uint, (long Offset, long Size)>();
            while (position + 8 <= length)
            {
                _stream.Position = position;
                uint id = _reader.ReadUInt32();
                uint size = _reader.ReadUInt32();
                if ((id & XRayChunks.COMPRESSED_MASK) != 0)
                {
                    throw new XRayFormatException($"{_context}: chunk 0x{id & ~XRayChunks.COMPRESSED_MASK:X} is compressed; compressed level.geom is not supported.");
                }

                if (position + 8 + size > length)
                {
                    throw new XRayFormatException($"{_context}: chunk 0x{id:X} at {position} declares {size} bytes beyond the end of the file.");
                }

                chunks[id] = (position + 8, size);
                position += 8 + size;
            }

            if (!chunks.TryGetValue(XRayLevelFormat.CHUNK_HEADER, out (long Offset, long Size) header) || header.Size < 2)
            {
                throw new XRayFormatException($"{_context}: header chunk is missing.");
            }

            _stream.Position = header.Offset;
            Version = _reader.ReadUInt16();
            if (!chunks.TryGetValue(XRayLevelFormat.CHUNK_VB, out (long Offset, long Size) vb))
            {
                throw new XRayFormatException($"{_context}: vertex buffer chunk 0x9 is missing.");
            }

            if (!chunks.TryGetValue(XRayLevelFormat.CHUNK_IB, out (long Offset, long Size) ib))
            {
                throw new XRayFormatException($"{_context}: index buffer chunk 0xA is missing.");
            }

            ReadVertexDirectory(vb.Offset, vb.Size);
            ReadIndexDirectory(ib.Offset, ib.Size);
            if (chunks.TryGetValue(XRayLevelFormat.CHUNK_SWIS, out (long Offset, long Size) swis))
            {
                ReadSwis(swis.Offset, swis.Size);
            }
        }

        private void ReadVertexDirectory(long offset, long size)
        {
            long end = offset + size;
            _stream.Position = offset;
            uint count = _reader.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                var info = new XRayVertexBufferInfo { Index = i };
                while (true)
                {
                    if (_stream.Position + 8 > end)
                    {
                        throw new XRayFormatException($"{_context}: vertex declaration {i} runs past the VB chunk.");
                    }

                    ushort stream = _reader.ReadUInt16();
                    ushort elementOffset = _reader.ReadUInt16();
                    byte type = _reader.ReadByte();
                    byte method = _reader.ReadByte();
                    byte usage = _reader.ReadByte();
                    byte usageIndex = _reader.ReadByte();
                    if (type == XRayLevelFormat.DECL_UNUSED || stream == 0xFF)
                    {
                        break;
                    }

                    int elementSize = XRayLevelFormat.GetDeclSize(type);
                    if (elementSize < 0 || stream != 0)
                    {
                        throw new XRayFormatException($"{_context}: vertex buffer {i} uses unsupported element type {type} (stream {stream}, usage {usage}).");
                    }

                    info.Elements.Add(new XRayVertexElement(elementOffset, type, method, usage, usageIndex));
                    info.Stride = Math.Max(info.Stride, elementOffset + elementSize);
                }

                info.Count = (int)_reader.ReadUInt32();
                info.DataOffset = _stream.Position;
                info.Kind = Classify(info);
                long next = info.DataOffset + (long)info.Count * info.Stride;
                if (info.Stride <= 0 || next > end)
                {
                    throw new XRayFormatException($"{_context}: vertex buffer {i} ({info.Count} x {info.Stride} bytes) exceeds the VB chunk.");
                }

                VertexBuffers.Add(info);
                _stream.Position = next;
            }

            if (_stream.Position != end)
            {
                throw new XRayFormatException($"{_context}: VB chunk has {end - _stream.Position} unread bytes after {count} buffers; unknown vertex layout.");
            }
        }

        private static XRayLevelVertexKind Classify(XRayVertexBufferInfo info)
        {
            bool lightmap = false;
            bool color = false;
            bool tree = false;
            foreach (XRayVertexElement element in info.Elements)
            {
                if (element.Usage == XRayLevelFormat.USAGE_TEXCOORD && element.UsageIndex == 1)
                {
                    lightmap = true;
                }
                else if (element.Usage == XRayLevelFormat.USAGE_COLOR)
                {
                    color = true;
                }
                else if (element.Usage == XRayLevelFormat.USAGE_TEXCOORD && element.Type == XRayLevelFormat.DECL_SHORT4)
                {
                    tree = true;
                }
            }

            if (lightmap)
            {
                return XRayLevelVertexKind.Lightmap;
            }

            if (color)
            {
                return XRayLevelVertexKind.VertexColor;
            }

            return tree ? XRayLevelVertexKind.Tree : XRayLevelVertexKind.Other;
        }

        private void ReadIndexDirectory(long offset, long size)
        {
            long end = offset + size;
            _stream.Position = offset;
            uint count = _reader.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                int indices = (int)_reader.ReadUInt32();
                var info = new XRayIndexBufferInfo { Index = i, Count = indices, DataOffset = _stream.Position };
                long next = info.DataOffset + indices * 2L;
                if (next > end)
                {
                    throw new XRayFormatException($"{_context}: index buffer {i} ({indices} indices) exceeds the IB chunk.");
                }

                IndexBuffers.Add(info);
                _stream.Position = next;
            }
        }

        private void ReadSwis(long offset, long size)
        {
            long end = offset + size;
            _stream.Position = offset;
            uint count = _reader.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                _stream.Position += 16;
                uint windows = _reader.ReadUInt32();
                if (_stream.Position + windows * 8L > end)
                {
                    throw new XRayFormatException($"{_context}: slide window buffer {i} exceeds the SWIS chunk.");
                }

                var list = new List<XRaySlideWindow>((int)windows);
                for (int w = 0; w < windows; w++)
                {
                    int windowOffset = _reader.ReadInt32();
                    ushort triangles = _reader.ReadUInt16();
                    ushort vertices = _reader.ReadUInt16();
                    list.Add(new XRaySlideWindow(windowOffset, triangles, vertices));
                }

                SlideWindows.Add(list);
            }
        }
    }
}
