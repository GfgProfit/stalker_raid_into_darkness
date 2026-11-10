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
        public float Friction { get; internal set; } = 1.0f;
        public float Flotation { get; internal set; } = 1.0f;
        public bool IsPassable => (Flags & XRayGameMaterials.FLAG_PASSABLE) != 0;
        public bool IsLiquid => (Flags & XRayGameMaterials.FLAG_LIQUID) != 0;
        public bool IsActorObstacle => (Flags & XRayGameMaterials.FLAG_ACTOR_OBSTACLE) != 0;
        public bool IsSlowDown => (Flags & XRayGameMaterials.FLAG_SLOW_DOWN) != 0;
        public bool IsInjurious => (Flags & XRayGameMaterials.FLAG_INJURIOUS) != 0;
    }

    public sealed class XRayGameMaterials
    {
        public const uint CHUNK_VERSION = 0x1000;
        public const uint CHUNK_MATERIALS = 0x1002;
        public const uint MATERIAL_CHUNK_MAIN = 0x1000;
        public const uint MATERIAL_CHUNK_FLAGS = 0x1001;
        public const uint MATERIAL_CHUNK_PHYSICS = 0x1002;
        public const uint MATERIAL_CHUNK_FLOTATION = 0x1004;
        public const uint FLAG_PASSABLE = 1u << 7;
        public const uint FLAG_LIQUID = 1u << 9;
        public const uint FLAG_ACTOR_OBSTACLE = 1u << 12;
        public const uint FLAG_SLOW_DOWN = 1u << 31;
        public const uint FLAG_INJURIOUS = 1u << 28;
        public const ushort VERSION = 1;

        private Dictionary<uint, XRayGameMaterial> _byId;

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

                if (parts.TryGetValue(MATERIAL_CHUNK_PHYSICS, out XRayChunk physics))
                {
                    material.Friction = physics.CreateReader(context + " PHYSICS").ReadSingle();
                }

                if (parts.TryGetValue(MATERIAL_CHUNK_FLOTATION, out XRayChunk flotation))
                {
                    material.Flotation = flotation.CreateReader(context + " FLOTATION").ReadSingle();
                }

                library.Materials.Add(material);
            }

            return library;
        }

        public bool[] CreatePassableMask()
        {
            int length = 0;
            foreach (XRayGameMaterial material in Materials)
            {
                if (material.Id <= 0x3FFF)
                {
                    length = System.Math.Max(length, (int)material.Id + 1);
                }
            }

            var mask = new bool[length];
            foreach (XRayGameMaterial material in Materials)
            {
                if (material.Id < mask.Length)
                {
                    mask[material.Id] = material.IsPassable;
                }
            }

            return mask;
        }

        public XRayGameMaterial GetById(uint id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<uint, XRayGameMaterial>();
                foreach (XRayGameMaterial material in Materials)
                {
                    _byId[material.Id] = material;
                }
            }

            return _byId.TryGetValue(id, out XRayGameMaterial found) ? found : null;
        }

        public bool[] CreateActorPassableMask()
        {
            bool[] mask = CreatePassableMask();
            foreach (XRayGameMaterial material in Materials)
            {
                if (material.Id < mask.Length && material.IsActorObstacle)
                {
                    mask[material.Id] = false;
                }
            }

            return mask;
        }
    }
}
