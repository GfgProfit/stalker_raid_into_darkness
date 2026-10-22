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

        public static XRayTextureDescription Read(byte[] data, string context)
        {
            Dictionary<uint, XRayChunk> chunks = XRayChunks.ReadMap(new XRayReader(data, context));
            var description = new XRayTextureDescription { Origin = context };
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

        public static bool IsUsedByEngine(XRayTextureDescription description)
        {
            return description.TextureType == XRayThmTextureType.Image || description.TextureType == XRayThmTextureType.Terrain || description.TextureType == XRayThmTextureType.NormalMap;
        }
    }
}
