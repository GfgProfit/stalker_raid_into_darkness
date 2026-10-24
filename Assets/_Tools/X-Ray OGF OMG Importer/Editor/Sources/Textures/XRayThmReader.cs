using System.Collections.Generic;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Sources.Textures
{
    public static class XRayThmReader
    {
        public const uint CHUNK_VERSION = 0x0810;
        public const uint CHUNK_DATA = 0x0811;
        public const uint CHUNK_TEXTURE_PARAM = 0x0812;
        public const uint CHUNK_TYPE = 0x0813;
        public const uint CHUNK_TEXTURE_TYPE = 0x0814;
        public const uint CHUNK_DETAIL_EXT = 0x0815;
        public const uint CHUNK_MATERIAL = 0x0816;
        public const uint CHUNK_BUMP = 0x0817;
        public const int PARSER_VERSION = 2;
        public const uint THM_CHUNK_MASK = 0x7FFFFFF0;
        public const uint THM_CHUNK_BASE = 0x0810;

        public static XRayTextureDescription Read(byte[] data, string context)
        {
            var description = new XRayTextureDescription { Origin = context };
            Dictionary<uint, XRayChunk> chunks = ReadChunks(data, context, description.CorrectedChunks);
            if (chunks.TryGetValue(CHUNK_TEXTURE_PARAM, out XRayChunk parameters))
            {
                XRayReader reader = parameters.CreateReader(context + " TEXTUREPARAM");
                description.PixelFormat = reader.ReadUInt32();
                description.Flags = reader.ReadUInt32();
                reader.Skip(4 * 4);
                if (reader.Remaining >= 8)
                {
                    description.Width = reader.ReadInt32();
                    description.Height = reader.ReadInt32();
                }
            }

            if (chunks.TryGetValue(CHUNK_TEXTURE_TYPE, out XRayChunk type))
            {
                description.TextureType = (XRayThmTextureType)type.CreateReader(context + " TEXTURE_TYPE").ReadUInt32();
            }

            if (chunks.TryGetValue(CHUNK_DETAIL_EXT, out XRayChunk detail))
            {
                XRayReader reader = detail.CreateReader(context + " DETAIL_EXT");
                description.DetailName = reader.ReadStringZ();
                description.DetailScale = reader.Remaining >= 4 ? reader.ReadSingle() : 1.0;
            }

            if (chunks.TryGetValue(CHUNK_MATERIAL, out XRayChunk material))
            {
                XRayReader reader = material.CreateReader(context + " MATERIAL");
                uint kind = reader.ReadUInt32();
                double weight = reader.Remaining >= 4 ? reader.ReadSingle() : 0.0;
                description.Material = kind + weight;
                description.MaterialId = kind;
                description.MaterialWeight = weight;
                description.HasMaterial = true;
            }

            if (chunks.TryGetValue(CHUNK_BUMP, out XRayChunk bump))
            {
                XRayReader reader = bump.CreateReader(context + " BUMP");
                description.VirtualHeight = reader.ReadSingle();
                description.BumpMode = (XRayBumpMode)reader.ReadUInt32();
                description.BumpName = reader.ReadStringZ();
            }

            if (string.IsNullOrEmpty(description.DetailName) || (description.Flags & (XRayTextureDescription.FLAG_DIFFUSE_DETAIL | XRayTextureDescription.FLAG_BUMP_DETAIL)) == 0)
            {
                description.DetailName = null;
            }

            return description;
        }

        public static Dictionary<uint, XRayChunk> ReadChunks(byte[] data, string context, List<string> corrections)
        {
            var chunks = new Dictionary<uint, XRayChunk>();
            int position = 0;
            while (position + 8 <= data.Length)
            {
                uint id = ReadUInt32(data, position);
                uint declared = ReadUInt32(data, position + 4);
                int start = position + 8;
                long length = declared;
                if (start + length != data.Length && !IsThmChunkAt(data, start + length))
                {
                    length = 0;
                    while (start + length < data.Length && !IsThmChunkAt(data, start + length))
                    {
                        length++;
                    }

                    corrections?.Add($"chunk 0x{id:X} declares {declared} bytes, corrected to {length} like the engine THM reader (IReader::find_chunk)");
                }

                if ((id & XRayChunks.COMPRESSED_MASK) != 0 && (id & ~XRayChunks.COMPRESSED_MASK) != CHUNK_DATA)
                {
                    throw new XRayFormatException($"{context}: chunk 0x{id & ~XRayChunks.COMPRESSED_MASK:X} is compressed; compressed THM parameter chunks are not supported.");
                }

                if ((id & XRayChunks.COMPRESSED_MASK) == 0 && !chunks.ContainsKey(id))
                {
                    chunks.Add(id, new XRayChunk(id, data, start, (int)length));
                }

                position = start + (int)length;
            }

            return chunks;
        }

        private static bool IsThmChunkAt(byte[] data, long position)
        {
            return position >= 0 && position <= data.Length - 8 && (ReadUInt32(data, (int)position) & THM_CHUNK_MASK) == THM_CHUNK_BASE;
        }

        private static uint ReadUInt32(byte[] data, int position)
        {
            return (uint)(data[position] | (data[position + 1] << 8) | (data[position + 2] << 16) | (data[position + 3] << 24));
        }

        public static bool IsUsedByEngine(XRayTextureDescription description)
        {
            return description.TextureType == XRayThmTextureType.Image || description.TextureType == XRayThmTextureType.Terrain || description.TextureType == XRayThmTextureType.NormalMap;
        }
    }
}
