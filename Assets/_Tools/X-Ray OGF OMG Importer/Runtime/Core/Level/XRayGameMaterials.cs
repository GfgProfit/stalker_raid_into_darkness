using System.Collections.Generic;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Core.Level
{
    public sealed class XRayGameMaterial
    {
        public int Index { get; internal set; }
        public uint Id { get; internal set; }
        public string Name { get; internal set; }
        public uint Flags { get; internal set; }
        public bool IsPassable => (Flags & XRayGameMaterials.FLAG_PASSABLE) != 0;
        public bool IsLiquid => (Flags & XRayGameMaterials.FLAG_LIQUID) != 0;
    }

    public sealed class XRayGameMaterials
    {
        public const uint CHUNK_VERSION = 0x1000;
        public const uint CHUNK_MATERIALS = 0x1002;
        public const uint MATERIAL_CHUNK_MAIN = 0x1000;
        public const uint MATERIAL_CHUNK_FLAGS = 0x1001;
        public const uint FLAG_PASSABLE = 1u << 7;
        public const uint FLAG_LIQUID = 1u << 9;
        public const ushort VERSION = 1;

        public List<XRayGameMaterial> Materials { get; } = new List<XRayGameMaterial>();

        public static XRayGameMaterials Read(byte[] data, string context)
        {
            Dictionary<uint, XRayChunk> chunks = XRayChunks.ReadMap(new XRayReader(data, context));
            if (!chunks.TryGetValue(CHUNK_VERSION, out XRayChunk version) || version.CreateReader(context).ReadUInt16() != VERSION)
            {
                throw new XRayFormatException($"{context}: missing or unsupported game material library version.");
            }

            var library = new XRayGameMaterials();
            if (!chunks.TryGetValue(CHUNK_MATERIALS, out XRayChunk materials))
            {
                return library;
            }

            foreach (XRayChunk chunk in XRayChunks.ReadAll(materials.CreateReader(context + " MATERIALS")))
            {
                Dictionary<uint, XRayChunk> parts = XRayChunks.ReadMap(chunk.CreateReader(context + " MATERIAL"));
                var material = new XRayGameMaterial { Index = library.Materials.Count };
                if (parts.TryGetValue(MATERIAL_CHUNK_MAIN, out XRayChunk main))
                {
                    XRayReader reader = main.CreateReader(context + " MAIN");
                    material.Id = reader.ReadUInt32();
                    material.Name = reader.ReadStringZ();
                }

                if (parts.TryGetValue(MATERIAL_CHUNK_FLAGS, out XRayChunk flags))
                {
                    material.Flags = flags.CreateReader(context + " FLAGS").ReadUInt32();
                }

                library.Materials.Add(material);
            }

            return library;
        }

        public bool[] CreatePassableMask()
        {
            var mask = new bool[Materials.Count];
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = Materials[i].IsPassable;
            }

            return mask;
        }
    }
}
