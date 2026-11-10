using System;
using System.Collections.Generic;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Core.Spawn
{
    public static class XRayLevelSpawnReader
    {
        private const uint GRAPH_VERSION = 10;
        private const int GRAPH_VERTEX_SIZE = 42;
        private const ushort SPAWN_VERSION_FLAG = 32;
        private const ushort MIN_OBJECT_VERSION = 118;
        private const ushort MAX_OBJECT_VERSION = 128;

        public static XRayLevelSpawnData Read(byte[] data, string context, ISet<string> anomalySections)
        {
            if (anomalySections == null)
            {
                throw new ArgumentNullException(nameof(anomalySections));
            }

            Dictionary<uint, XRayChunk> chunks = Chunks(new XRayReader(data, context));
            XRayLevelSpawnData result = ReadHeader(Require(chunks, 0, context), context);
            byte[] vertexLevels = ReadGraph(Require(chunks, 4, context), result, context);
            var levels = new Dictionary<byte, XRaySpawnLevel>();
            foreach (XRaySpawnLevel level in result.Levels)
            {
                levels.Add(level.Id, level);
            }

            Dictionary<uint, XRayChunk> vertices = ReadVertices(Require(chunks, 1, context), result.ObjectCount, context);
            var spawnIds = new HashSet<ushort>();
            foreach (XRayChunk vertex in vertices.Values)
            {
                Dictionary<uint, XRayChunk> vertexChunks = Chunks(vertex.CreateReader(context + " spawn vertex " + vertex.Id));
                XRayReader idReader = Require(vertexChunks, 0, context).CreateReader(context + " spawn ID");
                ushort spawnId = idReader.ReadUInt16();
                End(idReader);
                if (!spawnIds.Add(spawnId))
                {
                    throw new XRayFormatException($"{context}: duplicate spawn ID {spawnId}.");
                }

                XRayReader packet = Packet(vertexChunks, context + " spawn " + spawnId);
                string section = ReadSection(packet);
                if (!anomalySections.Contains(section))
                {
                    continue;
                }

                XRaySpawnAnomaly anomaly = ReadAnomaly(packet, spawnId, section);
                if (anomaly.GameVertexId >= vertexLevels.Length)
                {
                    throw new XRayFormatException($"{packet.Context}: game vertex {anomaly.GameVertexId} is outside the embedded game graph ({vertexLevels.Length} vertices).");
                }

                anomaly.LevelId = vertexLevels[anomaly.GameVertexId];
                anomaly.LevelName = levels[anomaly.LevelId].Name;
                result.Anomalies.Add(anomaly);
            }

            return result;
        }

        public static XRayLevelSpawnData ReadMetadata(byte[] data, string context)
        {
            Dictionary<uint, XRayChunk> chunks = Chunks(new XRayReader(data, context));
            XRayLevelSpawnData result = ReadHeader(Require(chunks, 0, context), context);
            ReadGraph(Require(chunks, 4, context), result, context);
            return result;
        }

        public static HashSet<string> ReadSections(byte[] data, string context)
        {
            Dictionary<uint, XRayChunk> chunks = Chunks(new XRayReader(data, context));
            XRayLevelSpawnData header = ReadHeader(Require(chunks, 0, context), context);
            Dictionary<uint, XRayChunk> vertices = ReadVertices(Require(chunks, 1, context), header.ObjectCount, context);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XRayChunk vertex in vertices.Values)
            {
                Dictionary<uint, XRayChunk> vertexChunks = Chunks(vertex.CreateReader(context + " spawn vertex " + vertex.Id));
                result.Add(ReadSection(Packet(vertexChunks, context + " spawn vertex " + vertex.Id)));
            }

            return result;
        }

        private static XRayLevelSpawnData ReadHeader(XRayChunk chunk, string context)
        {
            XRayReader reader = chunk.CreateReader(context + " spawn header");
            var result = new XRayLevelSpawnData { Version = reader.ReadUInt32() };
            if (result.Version != GRAPH_VERSION)
            {
                throw new XRayFormatException($"{context}: all.spawn version {result.Version} is unsupported (expected {GRAPH_VERSION}).");
            }

            result.Guid = ReadGuid(reader);
            result.GraphGuid = ReadGuid(reader);
            result.ObjectCount = reader.ReadUInt32();
            result.LevelCount = reader.ReadUInt32();
            if (result.ObjectCount > ushort.MaxValue || result.LevelCount > byte.MaxValue)
            {
                throw new XRayFormatException($"{context}: invalid spawn counts ({result.ObjectCount} objects, {result.LevelCount} levels).");
            }

            End(reader);
            return result;
        }

        private static byte[] ReadGraph(XRayChunk chunk, XRayLevelSpawnData result, string context)
        {
            XRayReader reader = chunk.CreateReader(context + " embedded game graph");
            result.GraphVersion = reader.ReadByte();
            if (result.GraphVersion != GRAPH_VERSION)
            {
                throw new XRayFormatException($"{context}: embedded game graph version {result.GraphVersion} is unsupported (expected {GRAPH_VERSION}).");
            }

            ushort vertexCount = reader.ReadUInt16();
            uint edgeCount = reader.ReadUInt32();
            uint deathPointCount = reader.ReadUInt32();
            Guid graphGuid = ReadGuid(reader);
            if (graphGuid != result.GraphGuid)
            {
                throw new XRayFormatException($"{context}: all.spawn header and embedded game graph GUIDs differ.");
            }

            byte levelCount = reader.ReadByte();
            if (levelCount != result.LevelCount)
            {
                throw new XRayFormatException($"{context}: spawn header and embedded graph level counts differ ({result.LevelCount} and {levelCount}).");
            }

            var ids = new HashSet<byte>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < levelCount; i++)
            {
                var level = new XRaySpawnLevel
                {
                    Name = StringZ(reader),
                    Offset = Vector(reader),
                    Id = reader.ReadByte(),
                    Section = StringZ(reader),
                    Guid = ReadGuid(reader)
                };
                if (string.IsNullOrWhiteSpace(level.Name) || !ids.Add(level.Id) || !names.Add(level.Name))
                {
                    throw new XRayFormatException($"{context}: duplicate or invalid embedded graph level '{level.Name}' ({level.Id}).");
                }

                result.Levels.Add(level);
            }

            long payloadSize = (long)vertexCount * GRAPH_VERTEX_SIZE + (long)edgeCount * 6 + (long)deathPointCount * 20;
            if (payloadSize > reader.Remaining)
            {
                throw new XRayFormatException($"{context}: embedded game graph data is truncated ({payloadSize} bytes required, {reader.Remaining} available).");
            }

            var vertexLevels = new byte[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                reader.Skip(24);
                byte levelId = reader.ReadByte();
                reader.Skip(GRAPH_VERTEX_SIZE - 25);
                if (!ids.Contains(levelId))
                {
                    throw new XRayFormatException($"{context}: game vertex {i} references unknown level ID {levelId}.");
                }

                vertexLevels[i] = levelId;
            }

            return vertexLevels;
        }

        private static Dictionary<uint, XRayChunk> ReadVertices(XRayChunk chunk, uint expectedCount, string context)
        {
            Dictionary<uint, XRayChunk> graph = Chunks(chunk.CreateReader(context + " spawn graph"));
            XRayReader countReader = Require(graph, 0, context).CreateReader(context + " spawn graph count");
            uint count = countReader.ReadUInt32();
            End(countReader);
            Dictionary<uint, XRayChunk> vertices = Chunks(Require(graph, 1, context).CreateReader(context + " spawn vertices"));
            if (count != expectedCount || count != vertices.Count)
            {
                throw new XRayFormatException($"{context}: spawn object counts differ (header {expectedCount}, graph {count}, vertices {vertices.Count}).");
            }

            return vertices;
        }

        private static XRayReader Packet(Dictionary<uint, XRayChunk> vertex, string context)
        {
            Dictionary<uint, XRayChunk> data = Chunks(Require(vertex, 1, context).CreateReader(context + " data"));
            XRayReader reader = Require(data, 0, context).CreateReader(context + " packet");
            ushort length = reader.ReadUInt16();
            if (length != reader.Remaining)
            {
                throw new XRayFormatException($"{context}: spawn packet length {length} differs from its chunk payload ({reader.Remaining}).");
            }

            return reader;
        }

        private static string ReadSection(XRayReader reader)
        {
            ushort command = reader.ReadUInt16();
            if (command != 1)
            {
                throw new XRayFormatException($"{reader.Context}: expected M_SPAWN (1), found packet command {command}.");
            }

            string section = StringZ(reader);
            if (string.IsNullOrWhiteSpace(section))
            {
                throw new XRayFormatException($"{reader.Context}: spawn section is empty.");
            }

            return section;
        }

        private static XRaySpawnAnomaly ReadAnomaly(XRayReader reader, ushort spawnId, string section)
        {
            var result = new XRaySpawnAnomaly { SpawnId = spawnId, Section = section, Name = StringZ(reader) };
            reader.Skip(2);
            result.Position = Vector(reader);
            result.Angles = Vector(reader);
            reader.Skip(8);
            ushort flags = reader.ReadUInt16();
            if ((flags & SPAWN_VERSION_FLAG) == 0)
            {
                throw new XRayFormatException($"{reader.Context}: anomaly '{section}' has no serialized spawn version.");
            }

            result.Version = reader.ReadUInt16();
            if (result.Version < MIN_OBJECT_VERSION || result.Version > MAX_OBJECT_VERSION)
            {
                throw new XRayFormatException($"{reader.Context}: anomaly '{section}' spawn version {result.Version} is unsupported (expected {MIN_OBJECT_VERSION}..{MAX_OBJECT_VERSION}).");
            }

            if (result.Version > 120)
            {
                reader.Skip(2);
            }

            reader.Skip(2);
            ushort clientDataLength = reader.ReadUInt16();
            reader.Skip(clientDataLength);
            reader.Skip(2);
            ushort stateSize = reader.ReadUInt16();
            if (stateSize < 2 || stateSize - 2 != reader.Remaining)
            {
                throw new XRayFormatException($"{reader.Context}: anomaly '{section}' state size {stateSize} differs from the remaining payload ({reader.Remaining} plus size field).");
            }

            XRayReader state = reader.Slice(stateSize - 2, reader.Context + " state " + section);
            result.GameVertexId = state.ReadUInt16();
            Number(state);
            state.Skip(4);
            result.LevelVertexId = state.ReadUInt32();
            state.Skip(4);
            result.IniString = StringZ(state);
            state.Skip(8);
            ReadShapes(state, result);
            state.Skip(1);
            Number(state);
            state.Skip(4);
            result.EnabledTime = state.ReadUInt32();
            result.DisabledTime = state.ReadUInt32();
            result.StartTimeShift = state.ReadUInt32();
            if (IsMovingAnomaly(section))
            {
                result.MotionName = StringZ(state);
                if (state.Remaining > 0 && IsScriptedMovingAnomaly(section))
                {
                    byte legacyRespawnTime = state.ReadByte();
                    if (legacyRespawnTime != 0)
                    {
                        throw new XRayFormatException($"{state.Context}: unsupported legacy anomaly respawn timestamp flag {legacyRespawnTime}.");
                    }
                }
                End(state);
            }
            return result;
        }

        private static bool IsMovingAnomaly(string section)
        {
            return IsScriptedMovingAnomaly(section) || string.Equals(section, "generator_torrid", StringComparison.OrdinalIgnoreCase) || string.Equals(section, "generator_dust", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsScriptedMovingAnomaly(string section)
        {
            return string.Equals(section, "fireball_zone", StringComparison.OrdinalIgnoreCase) || string.Equals(section, "fireball_electric_zone", StringComparison.OrdinalIgnoreCase) || string.Equals(section, "fireball_acidic_zone", StringComparison.OrdinalIgnoreCase);
        }

        private static void ReadShapes(XRayReader reader, XRaySpawnAnomaly anomaly)
        {
            byte count = reader.ReadByte();
            for (int i = 0; i < count; i++)
            {
                byte type = reader.ReadByte();
                var shape = new XRaySpawnShape { Type = (XRaySpawnShapeType)type };
                if (shape.Type == XRaySpawnShapeType.Sphere)
                {
                    shape.Center = Vector(reader);
                    shape.Radius = Number(reader);
                    if (shape.Radius < 0.0)
                    {
                        throw new XRayFormatException($"{reader.Context}: anomaly '{anomaly.Section}' has a negative sphere radius.");
                    }
                }
                else if (shape.Type == XRaySpawnShapeType.Box)
                {
                    XVector3 x = Vector(reader);
                    XVector3 y = Vector(reader);
                    XVector3 z = Vector(reader);
                    shape.Center = Vector(reader);
                    shape.Box = XMatrix4.FromRows(x.X, y.X, z.X, shape.Center.X, x.Y, y.Y, z.Y, shape.Center.Y, x.Z, y.Z, z.Z, shape.Center.Z, 0.0, 0.0, 0.0, 1.0);
                }
                else
                {
                    throw new XRayFormatException($"{reader.Context}: anomaly '{anomaly.Section}' shape type {type} is unsupported (expected sphere 0 or box 1).");
                }

                anomaly.Shapes.Add(shape);
            }
        }

        private static Dictionary<uint, XRayChunk> Chunks(XRayReader reader)
        {
            var result = new Dictionary<uint, XRayChunk>();
            foreach (XRayChunk chunk in XRayChunks.ReadAll(reader))
            {
                if (result.ContainsKey(chunk.Id))
                {
                    throw new XRayFormatException($"{reader.Context}: duplicate chunk {chunk.Id}.");
                }

                result.Add(chunk.Id, chunk);
            }

            End(reader);
            return result;
        }

        private static XRayChunk Require(Dictionary<uint, XRayChunk> chunks, uint id, string context)
        {
            if (!chunks.TryGetValue(id, out XRayChunk chunk))
            {
                throw new XRayFormatException($"{context}: required spawn chunk {id} is missing.");
            }

            return chunk;
        }

        private static string StringZ(XRayReader reader)
        {
            int start = reader.AbsolutePosition;
            int length = Array.IndexOf(reader.Buffer, (byte)0, start, reader.Remaining) - start;
            if (length < 0)
            {
                throw new XRayFormatException($"{reader.Context}: unterminated string at offset {reader.Position}.");
            }

            return reader.ReadStringZ();
        }

        private static double Number(XRayReader reader)
        {
            double value = reader.ReadSingle();
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new XRayFormatException($"{reader.Context}: nonfinite value at offset {reader.Position - 4}.");
            }

            return value;
        }

        private static XVector3 Vector(XRayReader reader)
        {
            return new XVector3(Number(reader), Number(reader), Number(reader));
        }

        private static Guid ReadGuid(XRayReader reader)
        {
            var bytes = new byte[16];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = reader.ReadByte();
            }

            return new Guid(bytes);
        }

        private static void End(XRayReader reader)
        {
            if (!reader.IsEnd)
            {
                throw new XRayFormatException($"{reader.Context}: {reader.Remaining} unexpected trailing bytes at offset {reader.Position}.");
            }
        }
    }
}
