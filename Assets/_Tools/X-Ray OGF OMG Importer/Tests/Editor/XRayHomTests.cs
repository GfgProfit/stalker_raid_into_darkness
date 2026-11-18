using System.IO;
using NUnit.Framework;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Level;

namespace XRay.Importer.Tests
{
    public sealed class XRayHomTests
    {
        [Test]
        public void ReadsTriangleOrderCoordinatesAndSidedness()
        {
            XRayHomTriangle[] triangles = XRayHom.Read(Build(1u, 0.5f), "fixture");

            Assert.That(triangles, Has.Length.EqualTo(2));
            Assert.That(triangles[0].A.X, Is.EqualTo(-2.5));
            Assert.That(triangles[0].A.Y, Is.EqualTo(4.0));
            Assert.That(triangles[0].A.Z, Is.EqualTo(3.0));
            Assert.That(triangles[0].B.Z, Is.EqualTo(0.5));
            Assert.That(triangles[0].C.X, Is.EqualTo(7.0));
            Assert.That(triangles[0].Flags, Is.EqualTo(0u));
            Assert.That(triangles[0].DoubleSided, Is.False);
            Assert.That(triangles[1].Flags, Is.EqualTo(0x80000000u));
            Assert.That(triangles[1].DoubleSided, Is.True);
        }

        [Test]
        public void RejectsMissingOrDuplicateTriangleChunk()
        {
            Assert.Throws<XRayFormatException>(() => XRayHom.Read(Build(2u, 0.5f), "fixture"));
            Assert.Throws<XRayFormatException>(() => XRayHom.Read(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0 }, "fixture"));
        }

        [Test]
        public void RejectsIncompleteAndMisalignedTrianglePayload()
        {
            Assert.Throws<XRayFormatException>(() => XRayHom.Read(new byte[] { 1, 0, 0, 0, 1, 0, 0, 0 }, "fixture"));
            Assert.Throws<XRayFormatException>(() => XRayHom.Read(new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 0 }, "fixture"));
            Assert.Throws<XRayFormatException>(() => XRayHom.Read(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }, "fixture"));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void RejectsNonFiniteCoordinates(float coordinate)
        {
            Assert.Throws<XRayFormatException>(() => XRayHom.Read(Build(1u, coordinate), "fixture"));
        }

        private static byte[] Build(uint dataChunk, float coordinate)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(0u);
            writer.Write(4u);
            writer.Write(0u);
            writer.Write(dataChunk);
            writer.Write(80u);
            foreach (uint flags in new[] { 0u, 0x80000000u })
            {
                writer.Write(-2.5f);
                writer.Write(4f);
                writer.Write(3f);
                writer.Write(5f);
                writer.Write(6f);
                writer.Write(coordinate);
                writer.Write(7f);
                writer.Write(8f);
                writer.Write(9f);
                writer.Write(flags);
            }

            return stream.ToArray();
        }
    }
}
