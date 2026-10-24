using System.Collections.Generic;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Core.Level
{
    public static class XRayLevelReader
    {
        public static bool IsLevelSignature(byte[] head)
        {
            if (head == null || head.Length < 16)
            {
                return false;
            }

            uint id = (uint)(head[0] | (head[1] << 8) | (head[2] << 16) | (head[3] << 24));
            uint size = (uint)(head[4] | (head[5] << 8) | (head[6] << 16) | (head[7] << 24));
            ushort version = (ushort)(head[8] | (head[9] << 8));
            uint next = (uint)(head[12] | (head[13] << 8) | (head[14] << 16) | (head[15] << 24));
            return id == XRayLevelFormat.CHUNK_HEADER && size == XRayLevelFormat.HEADER_SIZE && version >= 4 && version <= 20 && next >= XRayLevelFormat.CHUNK_SHADERS && next <= XRayLevelFormat.CHUNK_SECTORS;
        }

        public static ushort ReadVersion(byte[] head)
        {
            return (ushort)(head[8] | (head[9] << 8));
        }

        public static XRayLevelData Read(byte[] data, string context)
        {
            var reader = new XRayReader(data, context);
            Dictionary<uint, XRayChunk> chunks = XRayChunks.ReadMap(reader);
            var level = new XRayLevelData();
            if (!chunks.TryGetValue(XRayLevelFormat.CHUNK_HEADER, out XRayChunk header))
            {
                throw new XRayFormatException($"{context}: header chunk 0x1 is missing; the file is not an X-Ray game level.");
            }

            XRayReader headerReader = header.CreateReader(context + " HEADER");
            level.Version = headerReader.ReadUInt16();
            level.Quality = headerReader.ReadUInt16();
            if (level.Version != XRayLevelFormat.VERSION_13 && level.Version != XRayLevelFormat.VERSION_14)
            {
                throw new XRayFormatException($"{context}: xrLC level version {level.Version} is not supported (supported: 13, 14). Older builds keep geometry inside the level file with a different chunk layout.");
            }

            foreach (uint id in chunks.Keys)
            {
                if (id < XRayLevelFormat.CHUNK_HEADER || id > XRayLevelFormat.CHUNK_SECTORS || id == 5)
                {
                    level.UnknownChunks.Add(id);
                }
            }

            ReadShaders(Require(chunks, XRayLevelFormat.CHUNK_SHADERS, context, "SHADERS"), level, context);
            ReadVisuals(Require(chunks, XRayLevelFormat.CHUNK_VISUALS, context, "VISUALS"), level, context);
            if (chunks.TryGetValue(XRayLevelFormat.CHUNK_PORTALS, out XRayChunk portals))
            {
                ReadPortals(portals, level, context);
            }

            if (chunks.TryGetValue(XRayLevelFormat.CHUNK_LIGHT_DYNAMIC, out XRayChunk lights))
            {
                ReadLights(lights, level, context);
            }

            if (chunks.TryGetValue(XRayLevelFormat.CHUNK_GLOWS, out XRayChunk glows))
            {
                ReadGlows(glows, level, context);
            }

            ReadSectors(Require(chunks, XRayLevelFormat.CHUNK_SECTORS, context, "SECTORS"), level, context);
            LinkHierarchy(level);
            return level;
        }

        private static XRayChunk Require(Dictionary<uint, XRayChunk> chunks, uint id, string context, string name)
        {
            if (!chunks.TryGetValue(id, out XRayChunk chunk))
            {
                throw new XRayFormatException($"{context}: required chunk {name} (0x{id:X}) is missing.");
            }

            return chunk;
        }

        private static void ReadShaders(XRayChunk chunk, XRayLevelData level, string context)
        {
            XRayReader reader = chunk.CreateReader(context + " SHADERS");
            uint count = reader.ReadUInt32();
            if (count > (uint)reader.Remaining)
            {
                throw new XRayFormatException($"{context} SHADERS: {count} entries declared in {reader.Remaining} bytes.");
            }

            for (int i = 0; i < count; i++)
            {
                level.Shaders.Add(new XRayLevelShader(i, reader.ReadStringZ()));
            }
        }

        private static void ReadVisuals(XRayChunk chunk, XRayLevelData level, string context)
        {
            List<XRayChunk> visuals = XRayChunks.ReadAll(chunk.CreateReader(context + " VISUALS"));
            for (int i = 0; i < visuals.Count; i++)
            {
                if (visuals[i].Id != (uint)i)
                {
                    throw new XRayFormatException($"{context} VISUALS: visual chunk {i} has id {visuals[i].Id}; visual ids must be sequential.");
                }

                level.Visuals.Add(ReadVisual(visuals[i], i, level, context + $" VISUAL[{i}]"));
            }
        }

        private static XRayLevelVisual ReadVisual(XRayChunk chunk, int index, XRayLevelData level, string context)
        {
            Dictionary<uint, XRayChunk> chunks = XRayChunks.ReadMap(chunk.CreateReader(context));
            if (!chunks.TryGetValue(XRayLevelFormat.OGF_CHUNK_HEADER, out XRayChunk headerChunk))
            {
                throw new XRayFormatException($"{context}: OGF header is missing.");
            }

            XRayReader header = headerChunk.CreateReader(context + " HEADER");
            byte format = header.ReadByte();
            if (format != XRayLevelFormat.OGF_VERSION_4)
            {
                throw new XRayFormatException($"{context}: OGF format {format} is not supported in level visuals (expected 4).");
            }

            var visual = new XRayLevelVisual { Index = index, ModelType = header.ReadByte(), ShaderId = header.ReadUInt16() };
            for (int i = 0; i < 3; i++)
            {
                visual.BoundsMin[i] = header.ReadSingle();
            }

            for (int i = 0; i < 3; i++)
            {
                visual.BoundsMax[i] = header.ReadSingle();
            }

            for (int i = 0; i < 3; i++)
            {
                visual.SphereCenter[i] = header.ReadSingle();
            }

            visual.SphereRadius = header.ReadSingle();
            if (visual.ShaderId >= level.Shaders.Count && visual.ModelType != XRayLevelFormat.MT_HIERRARHY)
            {
                throw new XRayFormatException($"{context}: shader id {visual.ShaderId} is outside the shader table ({level.Shaders.Count}).");
            }

            switch (visual.ModelType)
            {
                case XRayLevelFormat.MT_NORMAL:
                    ReadGeometry(chunks, visual, context);
                    break;
                case XRayLevelFormat.MT_PROGRESSIVE:
                    ReadGeometry(chunks, visual, context);
                    ReadSwiData(chunks, visual, context);
                    break;
                case XRayLevelFormat.MT_TREE_ST:
                    ReadGeometry(chunks, visual, context);
                    ReadTree(chunks, visual, context);
                    break;
                case XRayLevelFormat.MT_TREE_PM:
                    ReadGeometry(chunks, visual, context);
                    ReadTree(chunks, visual, context);
                    if (!chunks.TryGetValue(XRayLevelFormat.OGF_CHUNK_SWICONTAINER, out XRayChunk swi))
                    {
                        throw new XRayFormatException($"{context}: TreePM visual has no SWICONTAINER chunk.");
                    }

                    visual.SwiContainer = (int)swi.CreateReader(context + " SWICONTAINER").ReadUInt32();
                    break;
                case XRayLevelFormat.MT_HIERRARHY:
                    ReadChildren(chunks, visual, context);
                    break;
                case XRayLevelFormat.MT_LOD:
                    ReadChildren(chunks, visual, context);
                    if (chunks.TryGetValue(XRayLevelFormat.OGF_CHUNK_LODDEF2, out XRayChunk lod))
                    {
                        visual.LodFaceCount = lod.Length == XRayLevelFormat.LOD_FACES * 4 * XRayLevelFormat.LOD_VERTEX_SIZE ? XRayLevelFormat.LOD_FACES : 0;
                        if (visual.LodFaceCount == 0)
                        {
                            level.Warnings.Add($"{context}: LODDEF2 has {lod.Length} bytes; expected {XRayLevelFormat.LOD_FACES * 4 * XRayLevelFormat.LOD_VERTEX_SIZE}. The LOD billboard is ignored.");
                        }
                        else
                        {
                            XRayReader reader = lod.CreateReader(context + " LODDEF2");
                            visual.LodVertices = new XRayLodVertex[XRayLevelFormat.LOD_FACES * 4];
                            for (int vertex = 0; vertex < visual.LodVertices.Length; vertex++)
                            {
                                visual.LodVertices[vertex] = new XRayLodVertex(reader.ReadVector3(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadUInt32(), reader.ReadByte());
                                reader.Skip(3);
                            }
                        }
                    }

                    break;
                default:
                    level.Warnings.Add($"{context}: model type {visual.ModelType} ({XRayLevelFormat.GetModelTypeName(visual.ModelType)}) is not supported and is skipped.");
                    break;
            }

            return visual;
        }

        private static void ReadGeometry(Dictionary<uint, XRayChunk> chunks, XRayLevelVisual visual, string context)
        {
            if (!chunks.TryGetValue(XRayLevelFormat.OGF_CHUNK_GCONTAINER, out XRayChunk gcontainer))
            {
                throw new XRayFormatException($"{context}: {XRayLevelFormat.GetModelTypeName(visual.ModelType)} visual has no GCONTAINER chunk (VCONTAINER/ICONTAINER layouts of older builds are not supported).");
            }

            XRayReader reader = gcontainer.CreateReader(context + " GCONTAINER");
            int vb = reader.ReadInt32();
            int vbOffset = reader.ReadInt32();
            int vbCount = reader.ReadInt32();
            int ib = reader.ReadInt32();
            int ibOffset = reader.ReadInt32();
            int ibCount = reader.ReadInt32();
            if (vb < 0 || vbOffset < 0 || vbCount <= 0 || ib < 0 || ibOffset < 0 || ibCount < 0 || ibCount % 3 != 0)
            {
                throw new XRayFormatException($"{context} GCONTAINER: invalid range vb={vb}:{vbOffset}+{vbCount}, ib={ib}:{ibOffset}+{ibCount}.");
            }

            visual.Geometry = new XRayGeometryRange(vb, vbOffset, vbCount, ib, ibOffset, ibCount);
            visual.HasGeometry = true;
            visual.HasFastPath = chunks.ContainsKey(XRayLevelFormat.OGF_CHUNK_FASTPATH);
        }

        private static void ReadSwiData(Dictionary<uint, XRayChunk> chunks, XRayLevelVisual visual, string context)
        {
            if (!chunks.TryGetValue(XRayLevelFormat.OGF_CHUNK_SWIDATA, out XRayChunk chunk))
            {
                throw new XRayFormatException($"{context}: Progressive visual has no SWIDATA chunk.");
            }

            XRayReader reader = chunk.CreateReader(context + " SWIDATA");
            reader.Skip(16);
            uint count = reader.ReadUInt32();
            if (count == 0 || count * 8 > reader.Remaining)
            {
                throw new XRayFormatException($"{context} SWIDATA: {count} slide windows in {reader.Remaining} bytes.");
            }

            visual.SwiOffset = reader.ReadInt32();
            visual.SwiTriangles = reader.ReadUInt16();
            visual.SwiVertices = reader.ReadUInt16();
        }

        private static void ReadTree(Dictionary<uint, XRayChunk> chunks, XRayLevelVisual visual, string context)
        {
            if (!chunks.TryGetValue(XRayLevelFormat.OGF_CHUNK_TREEDEF2, out XRayChunk chunk))
            {
                throw new XRayFormatException($"{context}: tree visual has no TREEDEF2 chunk.");
            }

            XRayReader reader = chunk.CreateReader(context + " TREEDEF2");
            var tree = new XRayTreeDefinition();
            for (int i = 0; i < 16; i++)
            {
                tree.Matrix[i] = reader.ReadSingle();
            }

            for (int i = 0; i < 3; i++)
            {
                tree.ScaleRgb[i] = reader.ReadSingle();
            }

            tree.ScaleHemi = reader.ReadSingle();
            tree.ScaleSun = reader.ReadSingle();
            for (int i = 0; i < 3; i++)
            {
                tree.BiasRgb[i] = reader.ReadSingle();
            }

            tree.BiasHemi = reader.ReadSingle();
            tree.BiasSun = reader.ReadSingle();
            visual.Tree = tree;
        }

        private static void ReadChildren(Dictionary<uint, XRayChunk> chunks, XRayLevelVisual visual, string context)
        {
            if (!chunks.TryGetValue(XRayLevelFormat.OGF_CHUNK_CHILDREN_L, out XRayChunk chunk))
            {
                throw new XRayFormatException($"{context}: hierarchy visual has no CHILDREN_L chunk.");
            }

            XRayReader reader = chunk.CreateReader(context + " CHILDREN_L");
            uint count = reader.ReadUInt32();
            bool wide = reader.Remaining == count * 4;
            if (!wide && reader.Remaining != count * 2)
            {
                throw new XRayFormatException($"{context} CHILDREN_L: {count} children do not match {reader.Remaining} bytes.");
            }

            for (int i = 0; i < count; i++)
            {
                visual.Children.Add(wide ? reader.ReadInt32() : reader.ReadUInt16());
            }
        }

        private static void ReadPortals(XRayChunk chunk, XRayLevelData level, string context)
        {
            if (chunk.Length % XRayLevelFormat.PORTAL_SIZE != 0)
            {
                level.Warnings.Add($"{context} PORTALS: size {chunk.Length} is not a multiple of {XRayLevelFormat.PORTAL_SIZE}; trailing bytes ignored.");
            }

            XRayReader reader = chunk.CreateReader(context + " PORTALS");
            int count = chunk.Length / XRayLevelFormat.PORTAL_SIZE;
            for (int i = 0; i < count; i++)
            {
                var portal = new XRayLevelPortal { Index = i, SectorFront = reader.ReadUInt16(), SectorBack = reader.ReadUInt16() };
                var vertices = new XVector3[XRayLevelFormat.PORTAL_MAX_VERTICES];
                for (int v = 0; v < vertices.Length; v++)
                {
                    vertices[v] = reader.ReadVector3();
                }

                uint used = reader.ReadUInt32();
                for (int v = 0; v < used && v < vertices.Length; v++)
                {
                    portal.Vertices.Add(vertices[v]);
                }

                level.Portals.Add(portal);
            }
        }

        private static void ReadLights(XRayChunk chunk, XRayLevelData level, string context)
        {
            XRayReader reader = chunk.CreateReader(context + " LIGHT_DYNAMIC");
            int count = chunk.Length / XRayLevelFormat.LIGHT_SIZE;
            for (int i = 0; i < count; i++)
            {
                var light = new XRayLevelLight { Index = i, Controller = reader.ReadInt32(), Type = reader.ReadInt32() };
                for (int c = 0; c < 4; c++)
                {
                    light.Diffuse[c] = reader.ReadSingle();
                }

                reader.Skip(32);
                for (int c = 0; c < 3; c++)
                {
                    light.Position[c] = reader.ReadSingle();
                }

                for (int c = 0; c < 3; c++)
                {
                    light.Direction[c] = reader.ReadSingle();
                }

                light.Range = reader.ReadSingle();
                light.Falloff = reader.ReadSingle();
                light.Attenuation0 = reader.ReadSingle();
                light.Attenuation1 = reader.ReadSingle();
                light.Attenuation2 = reader.ReadSingle();
                light.Theta = reader.ReadSingle();
                light.Phi = reader.ReadSingle();
                level.Lights.Add(light);
            }
        }

        private static void ReadGlows(XRayChunk chunk, XRayLevelData level, string context)
        {
            XRayReader reader = chunk.CreateReader(context + " GLOWS");
            int count = chunk.Length / XRayLevelFormat.GLOW_SIZE;
            for (int i = 0; i < count; i++)
            {
                var glow = new XRayLevelGlow();
                for (int c = 0; c < 3; c++)
                {
                    glow.Position[c] = reader.ReadSingle();
                }

                glow.Radius = reader.ReadSingle();
                glow.Shader = reader.ReadUInt16();
                level.Glows.Add(glow);
            }
        }

        private static void ReadSectors(XRayChunk chunk, XRayLevelData level, string context)
        {
            List<XRayChunk> sectors = XRayChunks.ReadAll(chunk.CreateReader(context + " SECTORS"));
            foreach (XRayChunk sectorChunk in sectors)
            {
                var sector = new XRayLevelSector { Index = (int)sectorChunk.Id };
                Dictionary<uint, XRayChunk> parts = XRayChunks.ReadMap(sectorChunk.CreateReader(context + $" SECTOR[{sectorChunk.Id}]"));
                if (parts.TryGetValue(XRayLevelFormat.SECTOR_CHUNK_ROOT, out XRayChunk root))
                {
                    sector.RootVisual = (int)root.CreateReader(context + " SECTOR ROOT").ReadUInt32();
                    if (sector.RootVisual >= level.Visuals.Count)
                    {
                        throw new XRayFormatException($"{context} SECTOR[{sectorChunk.Id}]: root visual {sector.RootVisual} is outside the visual table ({level.Visuals.Count}).");
                    }
                }

                if (parts.TryGetValue(XRayLevelFormat.SECTOR_CHUNK_PORTALS, out XRayChunk portals))
                {
                    XRayReader reader = portals.CreateReader(context + " SECTOR PORTALS");
                    while (reader.Remaining >= 2)
                    {
                        sector.Portals.Add(reader.ReadUInt16());
                    }
                }

                level.Sectors.Add(sector);
            }
        }

        private static void LinkHierarchy(XRayLevelData level)
        {
            foreach (XRayLevelVisual visual in level.Visuals)
            {
                foreach (int child in visual.Children)
                {
                    if (child < 0 || child >= level.Visuals.Count)
                    {
                        throw new XRayFormatException($"Visual {visual.Index} references child {child} outside the visual table ({level.Visuals.Count}).");
                    }

                    level.Visuals[child].Parent = visual.Index;
                }
            }

            var stack = new Stack<int>();
            foreach (XRayLevelSector sector in level.Sectors)
            {
                if (sector.RootVisual < 0)
                {
                    continue;
                }

                stack.Push(sector.RootVisual);
                while (stack.Count > 0)
                {
                    XRayLevelVisual visual = level.Visuals[stack.Pop()];
                    if (visual.Sector >= 0)
                    {
                        continue;
                    }

                    visual.Sector = sector.Index;
                    foreach (int child in visual.Children)
                    {
                        stack.Push(child);
                    }
                }
            }
        }
    }
}
