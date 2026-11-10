using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayLevelContactTests
    {
        [Test]
        public void CapsuleDetectsTriangleInteriorAtMiddleOfBody()
        {
            float distance = XRayLevelContactSource.CapsuleDistanceSquared(new Vector3(0.0f, -2.0f, 0.0f), new Vector3(0.0f, 2.0f, 0.0f), new Vector3(-2.0f, 0.0f, -2.0f), new Vector3(0.0f, 0.0f, 2.0f), new Vector3(2.0f, 0.0f, -2.0f), out Vector3 point);
            Assert.That(distance, Is.EqualTo(0.0f).Within(0.000001f));
            Assert.That(point.sqrMagnitude, Is.LessThan(0.000001f));
        }

        [Test]
        public void TriangleBoundsDoNotCauseFalseVegetationContact()
        {
            float distance = XRayLevelContactSource.CapsuleDistanceSquared(new Vector3(0.9f, -1.0f, 0.9f), new Vector3(0.9f, 1.0f, 0.9f), Vector3.zero, Vector3.forward, Vector3.right, out _);
            Assert.That(distance, Is.EqualTo(0.32f).Within(0.000001f));
        }

        [Test]
        public void CapsuleTouchesTriangleEdgeWithoutAxisIntersection()
        {
            float distance = XRayLevelContactSource.CapsuleDistanceSquared(new Vector3(-0.2f, -1.0f, 0.5f), new Vector3(-0.2f, 1.0f, 0.5f), Vector3.zero, Vector3.forward, Vector3.right, out Vector3 point);
            Assert.That(distance, Is.EqualTo(0.04f).Within(0.000001f));
            Assert.That(point, Is.EqualTo(new Vector3(0.0f, 0.0f, 0.5f)));
        }

        [Test]
        public void DegenerateTriangleProducesFiniteDistance()
        {
            float distance = XRayLevelContactSource.CapsuleDistanceSquared(Vector3.up, Vector3.up, Vector3.zero, Vector3.right, Vector3.right * 2.0f, out Vector3 point);
            Assert.That(distance, Is.EqualTo(1.0f).Within(0.000001f));
            Assert.That(point, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void LargeCoordinatesRetainInteriorContactDespiteFloatingPointRounding()
        {
            float distance = XRayLevelContactSource.CapsuleDistanceSquared(new Vector3(1000.37f, 4.1f, 1000.51f), new Vector3(1000.37f, 5.9f, 1000.51f), new Vector3(999.4f, 4.7f, 999.1f), new Vector3(1001.2f, 5.2f, 999.3f), new Vector3(1000.5f, 4.8f, 1002.4f), out _);
            Assert.That(distance, Is.LessThan(0.000001f));
        }

        [Test]
        public void SerializedMaterialPhysicsAndFlagsRetainSourceSemantics()
        {
            XRayGameMaterials library = XRayGameMaterials.Read(Materials(XRayGameMaterials.FLAG_PASSABLE | XRayGameMaterials.FLAG_INJURIOUS | XRayGameMaterials.FLAG_SLOW_DOWN | XRayGameMaterials.FLAG_ACTOR_OBSTACLE, true), "contact test");
            XRayGameMaterial material = library.Materials[0];
            Assert.That(material.Id, Is.EqualTo(37));
            Assert.That(material.Index, Is.Zero);
            Assert.That(material.IsPassable, Is.True);
            Assert.That(material.IsSlowDown, Is.True);
            Assert.That(material.IsInjurious, Is.True);
            Assert.That(material.Friction, Is.EqualTo(0.25f));
            Assert.That(material.Flotation, Is.EqualTo(0.85f));
            Assert.That(library.GetById(37), Is.SameAs(material));
            Assert.That(library.GetById(0), Is.Null);
            Assert.That(library.CreatePassableMask()[37], Is.True);
            Assert.That(library.CreatePassableMask()[0], Is.False);
            Assert.That(library.CreateActorPassableMask()[37], Is.False);
            XRayGameMaterial defaults = XRayGameMaterials.Read(Materials(0, false), "contact defaults").Materials[0];
            Assert.That(defaults.Flotation, Is.EqualTo(1.0f));
        }

        private static byte[] Materials(uint flags, bool physics)
        {
            return Chunks(writer =>
            {
                Chunk(writer, XRayGameMaterials.CHUNK_VERSION, part => part.Write(XRayGameMaterials.VERSION));
                Chunk(writer, XRayGameMaterials.CHUNK_MATERIALS, materialList => Chunk(materialList, 0, material =>
                {
                    Chunk(material, XRayGameMaterials.MATERIAL_CHUNK_MAIN, part =>
                    {
                        part.Write(37u);
                        part.Write(System.Text.Encoding.ASCII.GetBytes("materials\\bush\0"));
                    });
                    Chunk(material, XRayGameMaterials.MATERIAL_CHUNK_FLAGS, part => part.Write(flags));
                    if (physics)
                    {
                        Chunk(material, XRayGameMaterials.MATERIAL_CHUNK_PHYSICS, part => part.Write(0.25f));
                        Chunk(material, XRayGameMaterials.MATERIAL_CHUNK_FLOTATION, part => part.Write(0.85f));
                    }
                }));
            });
        }

        private static byte[] Chunks(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    write(writer);
                    return stream.ToArray();
                }
            }
        }

        private static void Chunk(BinaryWriter writer, uint id, Action<BinaryWriter> write)
        {
            byte[] payload = Chunks(write);
            writer.Write(id);
            writer.Write(payload.Length);
            writer.Write(payload);
        }
    }
}
