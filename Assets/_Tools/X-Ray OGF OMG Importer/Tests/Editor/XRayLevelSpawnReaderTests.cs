using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Spawn;

namespace XRay.Importer.Tests
{
    public sealed class XRayLevelSpawnReaderTests
    {
        [Test]
        public void ReadsSpheresBoxesAndEmbeddedLevelMappingWithoutApplyingGraphOffsets()
        {
            XRayLevelSpawnData data = Read(Fixture());
            Assert.AreEqual(10u, data.Version);
            Assert.AreEqual(2u, data.LevelCount);
            Assert.AreEqual(1u, data.ObjectCount);
            Assert.AreEqual(2, data.Levels.Count);
            Assert.AreEqual(1, data.Anomalies.Count);
            XRaySpawnAnomaly anomaly = data.Anomalies[0];
            Assert.AreEqual(97, anomaly.SpawnId);
            Assert.AreEqual("zone_electro", anomaly.Section);
            Assert.AreEqual("source_zone", anomaly.Name);
            Assert.AreEqual(42, anomaly.LevelId);
            Assert.AreEqual("l02_target", anomaly.LevelName);
            Assert.AreEqual(17.0, anomaly.Position.X);
            Assert.AreEqual(5.0, anomaly.Position.Y);
            Assert.AreEqual(-9.0, anomaly.Position.Z);
            Assert.AreEqual(12u, anomaly.EnabledTime);
            Assert.AreEqual(34u, anomaly.DisabledTime);
            Assert.AreEqual(56u, anomaly.StartTimeShift);
            Assert.AreEqual("[logic]\nactive = sr_idle", anomaly.IniString);
            Assert.AreEqual(2, anomaly.Shapes.Count);
            XRaySpawnShape sphere = anomaly.Shapes[0];
            Assert.AreEqual(XRaySpawnShapeType.Sphere, sphere.Type);
            Assert.AreEqual(2.5, sphere.Radius);
            Assert.AreEqual(3.0, sphere.Center.Z);
            XRaySpawnShape box = anomaly.Shapes[1];
            Assert.AreEqual(XRaySpawnShapeType.Box, box.Type);
            Assert.AreEqual(4.0, box.Center.X);
            Assert.AreEqual(2.0, box.Box[2, 0]);
            Assert.AreEqual(-4.0, box.Box[0, 2]);
            Assert.AreEqual(3.0, box.Box[1, 1]);
        }

        [Test]
        public void EnumeratesUnknownSectionsWithoutTreatingThemAsAnomalies()
        {
            byte[] bytes = Fixture(section: "future_zone");
            CollectionAssert.AreEquivalent(new[] { "future_zone" }, XRayLevelSpawnReader.ReadSections(bytes, "fixture"));
            Assert.AreEqual(0, Read(bytes).Anomalies.Count);
            Assert.AreEqual(1, XRayLevelSpawnReader.Read(bytes, "fixture", new HashSet<string> { "future_zone" }).Anomalies.Count);
        }

        [Test]
        public void ReadsMetadataWithoutParsingObjectPackets()
        {
            XRayLevelSpawnData data = XRayLevelSpawnReader.ReadMetadata(Fixture(version: 129), "fixture");
            Assert.AreEqual(2, data.Levels.Count);
            Assert.AreEqual("l02_target", data.Levels[1].Name);
            Assert.AreEqual(0, data.Anomalies.Count);
        }

        [TestCase(117)]
        [TestCase(129)]
        public void RejectsUnsupportedObjectVersions(int version)
        {
            StringAssert.Contains("unsupported", Assert.Throws<XRayFormatException>(() => Read(Fixture(version: (ushort)version))).Message);
        }

        [TestCase(118)]
        [TestCase(120)]
        [TestCase(128)]
        public void SupportsModernHeaderLayouts(int version)
        {
            Assert.AreEqual(version, Read(Fixture(version: (ushort)version)).Anomalies[0].Version);
        }

        [Test]
        public void RejectsMismatchedEmbeddedGraphGuid()
        {
            StringAssert.Contains("GUID", Assert.Throws<XRayFormatException>(() => Read(Fixture(graphMismatch: true))).Message);
        }

        [Test]
        public void RejectsUnknownGraphLevelId()
        {
            StringAssert.Contains("unknown level ID", Assert.Throws<XRayFormatException>(() => Read(Fixture(vertexLevel: 99))).Message);
        }

        [Test]
        public void RejectsGameVertexOutsideEmbeddedGraph()
        {
            StringAssert.Contains("outside", Assert.Throws<XRayFormatException>(() => Read(Fixture(gameVertex: 5))).Message);
        }

        [Test]
        public void RejectsUnsupportedShapeType()
        {
            StringAssert.Contains("shape type", Assert.Throws<XRayFormatException>(() => Read(Fixture(shapeType: 9))).Message);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1.0f)]
        public void RejectsInvalidSphereRadii(float radius)
        {
            Assert.Throws<XRayFormatException>(() => Read(Fixture(radius: radius)));
        }

        [Test]
        public void RejectsTruncatedChunksAndTrailingBytes()
        {
            byte[] complete = Fixture();
            var truncated = new byte[complete.Length - 1];
            Buffer.BlockCopy(complete, 0, truncated, 0, truncated.Length);
            Assert.Throws<XRayFormatException>(() => Read(truncated));
            Array.Resize(ref complete, complete.Length + 1);
            StringAssert.Contains("trailing", Assert.Throws<XRayFormatException>(() => Read(complete)).Message);
        }

        [Test]
        public void RejectsCompressedChunksWithAnActionableError()
        {
            byte[] complete = Fixture();
            complete[3] = 128;
            StringAssert.Contains("LZHUF", Assert.Throws<XRayFormatException>(() => Read(complete)).Message);
        }

        [Test]
        public void RejectsMissingRequiredGraphChunk()
        {
            byte[] complete = Fixture();
            complete[52] = 9;
            StringAssert.Contains("required spawn chunk 1", Assert.Throws<XRayFormatException>(() => Read(complete)).Message);
        }

        [Test]
        public void RejectsMismatchedObjectCounts()
        {
            StringAssert.Contains("counts differ", Assert.Throws<XRayFormatException>(() => Read(Fixture(objectCount: 2))).Message);
        }

        private static XRayLevelSpawnData Read(byte[] bytes)
        {
            return XRayLevelSpawnReader.Read(bytes, "fixture", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zone_electro" });
        }

        internal static byte[] Fixture(string section = "zone_electro", ushort version = 128, bool graphMismatch = false, byte vertexLevel = 42, ushort gameVertex = 1, byte shapeType = 0, float radius = 2.5f, uint objectCount = 1, string levelName = "l02_target")
        {
            byte[] guid = new Guid("e6584793-5ae4-40bd-9ef0-b86f7796084d").ToByteArray();
            byte[] header = Bytes(writer =>
            {
                writer.Write(10u);
                writer.Write(new byte[16]);
                writer.Write(guid);
                writer.Write(objectCount);
                writer.Write(2u);
            });
            byte[] graph = Bytes(writer =>
            {
                writer.Write((byte)10);
                writer.Write((ushort)2);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(graphMismatch ? new byte[16] : guid);
                writer.Write((byte)2);
                Level(writer, "l01_other", 7);
                Level(writer, levelName, 42);
                writer.Write(new byte[24]);
                writer.Write((byte)7);
                writer.Write(new byte[17]);
                writer.Write(new byte[24]);
                writer.Write(vertexLevel);
                writer.Write(new byte[17]);
            });
            byte[] state = Bytes(writer =>
            {
                writer.Write(gameVertex);
                writer.Write(0.0f);
                writer.Write(0u);
                writer.Write(123u);
                writer.Write(0u);
                StringZ(writer, "[logic]\nactive = sr_idle");
                writer.Write(uint.MaxValue);
                writer.Write(uint.MaxValue);
                writer.Write((byte)2);
                writer.Write(shapeType);
                Vector(writer, 1.0f, 2.0f, 3.0f);
                writer.Write(radius);
                writer.Write((byte)1);
                Vector(writer, 0.0f, 0.0f, 2.0f);
                Vector(writer, 0.0f, 3.0f, 0.0f);
                Vector(writer, -4.0f, 0.0f, 0.0f);
                Vector(writer, 4.0f, 5.0f, 6.0f);
                writer.Write((byte)0);
                writer.Write(0.0f);
                writer.Write(uint.MaxValue);
                writer.Write(12u);
                writer.Write(34u);
                writer.Write(56u);
                writer.Write(new byte[11]);
            });
            byte[] packet = Bytes(writer =>
            {
                writer.Write((ushort)1);
                StringZ(writer, section);
                StringZ(writer, "source_zone");
                writer.Write((byte)0);
                writer.Write((byte)254);
                Vector(writer, 17.0f, 5.0f, -9.0f);
                Vector(writer, 0.1f, 0.2f, 0.3f);
                writer.Write(new byte[8]);
                writer.Write((ushort)33);
                writer.Write(version);
                if (version > 120)
                {
                    writer.Write((ushort)0);
                }

                writer.Write((ushort)1);
                writer.Write((ushort)3);
                writer.Write(new byte[3]);
                writer.Write((ushort)97);
                writer.Write((ushort)(state.Length + 2));
                writer.Write(state);
            });
            byte[] data = Chunk(0, Bytes(writer =>
            {
                writer.Write((ushort)packet.Length);
                writer.Write(packet);
            }));
            byte[] vertex = Concat(Chunk(0, BitConverter.GetBytes((ushort)97)), Chunk(1, data));
            byte[] spawnGraph = Concat(Chunk(0, BitConverter.GetBytes(1u)), Chunk(1, Chunk(0, vertex)));
            return Concat(Chunk(0, header), Chunk(1, spawnGraph), Chunk(4, graph));
        }

        private static void Level(BinaryWriter writer, string name, byte id)
        {
            StringZ(writer, name);
            Vector(writer, 10000.0f, 20000.0f, 30000.0f);
            writer.Write(id);
            StringZ(writer, name);
            writer.Write(new byte[16]);
        }

        private static void Vector(BinaryWriter writer, float x, float y, float z)
        {
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);
        }

        private static void StringZ(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.ASCII.GetBytes(value));
            writer.Write((byte)0);
        }

        private static byte[] Chunk(uint id, byte[] data)
        {
            return Bytes(writer =>
            {
                writer.Write(id);
                writer.Write((uint)data.Length);
                writer.Write(data);
            });
        }

        private static byte[] Concat(params byte[][] parts)
        {
            return Bytes(writer =>
            {
                foreach (byte[] part in parts)
                {
                    writer.Write(part);
                }
            });
        }

        private static byte[] Bytes(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    write(writer);
                    writer.Flush();
                    return stream.ToArray();
                }
            }
        }
    }
}
