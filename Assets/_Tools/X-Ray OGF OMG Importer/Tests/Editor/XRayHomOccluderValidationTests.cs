using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Core;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayHomOccluderValidationTests
    {
        [Test]
        public void OccluderDoesNotFillTheGapBetweenBackingFaces()
        {
            XRayHomTriangle source = Hom(0.1f, 0);
            var validator = new XRayHomOccluderValidation(new[] { source });
            Assert.That(validator.BeginMesh(new Bounds(Vector3.zero, new Vector3(10f, 8f, 0f))), Is.True);

            validator.AddBackingTriangle(0, new Vector3(-4f, -2f, 0f), new Vector3(-1f, -2f, 0f), new Vector3(-1f, 2f, 0f));
            validator.AddBackingTriangle(1, new Vector3(1f, -2f, 0f), new Vector3(4f, -2f, 0f), new Vector3(1f, 2f, 0f));
            XRayVegetationOccluderData.Triangle[] patches = validator.FinishMesh();

            Assert.That(patches, Has.Length.GreaterThanOrEqualTo(2));
            int covered = 0;
            foreach (XRayVegetationOccluderData.Triangle patch in patches)
            {
                covered |= 1 << patch.SubMesh;
                foreach (Vector3 vertex in new[] { patch.A, patch.B, patch.C })
                {
                    Assert.That(vertex.z, Is.Zero);
                    if (patch.SubMesh == 0) Assert.That(vertex.x, Is.LessThanOrEqualTo(-1f));
                    else Assert.That(vertex.x, Is.GreaterThanOrEqualTo(1f));
                }
            }
            Assert.That(covered, Is.EqualTo(3));
        }

        [TestCase(0u)]
        [TestCase(1u)]
        public void ReversedBackingRequiresDoubleSidedHomAndKeepsBackingWinding(uint flags)
        {
            var validator = new XRayHomOccluderValidation(new[] { Hom(0f, flags) });
            Assert.That(validator.BeginMesh(new Bounds(Vector3.zero, Vector3.one * 8f)), Is.True);

            validator.AddBackingTriangle(2, Vector3.left, Vector3.up, Vector3.right);
            XRayVegetationOccluderData.Triangle[] patches = validator.FinishMesh();

            if (flags == 0u)
            {
                Assert.That(patches, Is.Empty);
                return;
            }
            Assert.That(patches, Has.Length.EqualTo(1));
            XRayVegetationOccluderData.Triangle patch = patches[0];
            Assert.That(patch.DoubleSided, Is.True);
            Assert.That(Vector3.Cross(patch.B - patch.A, patch.C - patch.A).z, Is.LessThan(0f));
        }

        [Test]
        public void BackingFromAnotherPlaneIsRejectedAndSourceTransformDoesNotLeakIntoPatch()
        {
            var validator = new XRayHomOccluderValidation(new[] { Hom(0f, 0) });
            Assert.That(validator.BeginMesh(new Bounds(Vector3.zero, Vector3.one * 4f), Matrix4x4.Translate(new Vector3(0.5f, 0f, 0.1f))), Is.True);
            validator.AddBackingTriangle(9, new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(0f, 1f, 1f));
            Assert.That(validator.FinishMesh(), Is.Empty);

            validator.AddBackingTriangle(9, Vector3.left, Vector3.right, Vector3.up);
            XRayVegetationOccluderData.Triangle[] patches = validator.FinishMesh();

            Assert.That(patches, Has.Length.EqualTo(1));
            Assert.That(patches[0].A.z, Is.Zero);
            Assert.That(patches[0].B.z, Is.Zero);
            Assert.That(patches[0].C.z, Is.Zero);
        }

        private static XRayHomTriangle Hom(float height, uint flags)
        {
            return new XRayHomTriangle(new XVector3(-5, -3, height), new XVector3(5, -3, height), new XVector3(0, 8, height), flags);
        }
    }
}
