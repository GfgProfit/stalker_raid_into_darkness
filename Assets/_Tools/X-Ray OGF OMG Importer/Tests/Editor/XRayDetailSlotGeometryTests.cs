using System.Collections.Generic;
using NUnit.Framework;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Level;

namespace XRay.Importer.Tests
{
    public sealed class XRayDetailSlotGeometryTests
    {
        [TestCase(0.2f, false)]
        [TestCase(0.42f, true)]
        [TestCase(0.45f, true)]
        public void PreparedCandidatesMatchSourcePlacement(float density, bool monolith)
        {
            XRayDetails details = CreateDetails();
            byte[] collisionBytes = CreateCollision();
            XRayCollisionModel collision = XRayCollisionModel.Read(collisionBytes, "Slot geometry test");
            collision.BuildGrid(XRayDetails.SLOT_SIZE);
            byte[] bytes = XRayDetailSlotGeometry.Bake(details, collision);
            XRayCollisionModel preparedCollision = XRayCollisionModel.Read(collisionBytes, "Prepared slot geometry test");
            XRayDetailSlotGeometry geometry = XRayDetailSlotGeometry.Read(bytes, details, preparedCollision);
            var settings = new XRayDetailPlacementSettings { Density = density, MonolithRelease = monolith, TerrainSlope = 1.0f };
            var passable = new[] { false, true };
            var baseline = new XRayDetailPlacement(details, collision, passable, settings);
            var prepared = new XRayDetailPlacement(details, preparedCollision, passable, settings, geometry);
            var expected = new List<XRayDetailInstance>();
            var actual = new List<XRayDetailInstance>();
            for (int x = 0; x < details.SizeX; x++)
            {
                baseline.PlaceSlot(x, 0, expected);
                prepared.PlaceSlot(x, 0, actual);
            }

            Assert.Greater(expected.Count, 0);
            Assert.AreEqual(2, baseline.Statistics.SlotsProcessed);
            Assert.AreEqual(1, baseline.Statistics.SlotsWithoutGround);
            Assert.AreEqual(baseline.Statistics.SlotsWithoutGround, prepared.Statistics.SlotsWithoutGround);
            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void IncompleteCandidatePayloadIsRejected()
        {
            XRayDetails details = CreateDetails();
            XRayCollisionModel collision = XRayCollisionModel.Read(CreateCollision(), "Slot geometry test");
            collision.BuildGrid(XRayDetails.SLOT_SIZE);
            byte[] bytes = XRayDetailSlotGeometry.Bake(details, collision);
            Assert.Throws<XRayFormatException>(() => XRayDetailSlotGeometry.Read(bytes[..^1], details, collision));
        }

        private static XRayDetails CreateDetails()
        {
            var header = new BinaryBuffer();
            header.Writer.Write(XRayDetails.VERSION);
            header.Writer.Write(1u);
            header.Writer.Write(0);
            header.Writer.Write(0);
            header.Writer.Write(3);
            header.Writer.Write(1);
            var model = new BinaryBuffer();
            model.Writer.Write((byte)0);
            model.Writer.Write((byte)0);
            model.Writer.Write(0u);
            model.Writer.Write(1.0f);
            model.Writer.Write(1.0f);
            model.Writer.Write(0u);
            model.Writer.Write(0u);
            var models = new BinaryBuffer();
            models.WriteChunk(0, model);
            var slots = new BinaryBuffer();
            for (int index = 0; index < 3; index++)
            {
                uint id = index == 1 ? 63u : 0u;
                slots.Writer.Write(1000u | 20u << 12 | id << 20 | 63u << 26);
                slots.Writer.Write(0xfffu);
                slots.Writer.Write(ushort.MaxValue);
                slots.Writer.Write((ushort)0);
                slots.Writer.Write((ushort)0);
                slots.Writer.Write((ushort)0);
            }

            var details = new BinaryBuffer();
            details.WriteChunk(0, header);
            details.WriteChunk(1, models);
            details.WriteChunk(2, slots);
            return XRayDetails.Read(details.ToArray(), "Slot geometry test");
        }

        private static byte[] CreateCollision()
        {
            var collision = new BinaryBuffer();
            collision.Writer.Write(XRayCollisionModel.VERSION);
            collision.Writer.Write(9);
            collision.Writer.Write(3);
            foreach (float coordinate in new[] { -1.0f, 0.0f, -1.0f, 3.0f, 1.0f, 3.0f }) collision.Writer.Write(coordinate);
            foreach (float coordinate in new[] { 1.0f, 0.5f, -1.0f, 1.0f, 0.8f, 3.0f, 3.0f, 0.5f, -1.0f, -1.0f, 0.0f, -1.0f, -1.0f, 0.0f, 3.0f, 3.0f, 0.0f, -1.0f, -1.0f, 1.0f, -1.0f, -1.0f, 1.0f, 3.0f, 3.0f, 1.0f, -1.0f }) collision.Writer.Write(coordinate);
            for (int face = 0; face < 3; face++)
            {
                for (int vertex = 0; vertex < 3; vertex++) collision.Writer.Write(face * 3 + vertex);
                collision.Writer.Write(face == 2 ? 1u : 0u);
            }

            return collision.ToArray();
        }
    }
}
