using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayVegetationOcclusionTests
    {
        private static readonly MethodInfo _reset = typeof(XRayVegetationOcclusion).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo _addTriangle = typeof(XRayVegetationOcclusion).GetMethod("AddTriangle", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo _addHomTriangle = typeof(XRayVegetationOcclusion).GetMethod("AddHomTriangle", BindingFlags.Instance | BindingFlags.NonPublic);

        [TestCase(false)]
        [TestCase(true)]
        public void OnlyFullyCoveredBoundsBehindTheTriangleAreRejected(bool orthographic)
        {
            XRayVegetationOcclusion query = Create(orthographic);
            AddTriangle(query, new Vector3(-4f, -4f, -5f), new Vector3(0f, 4f, -5f), new Vector3(4f, -4f, -5f));

            Assert.That(query.IsOccluded(new Bounds(new Vector3(0f, 0f, -10f), Vector3.one * 0.2f)), Is.True);
            Assert.That(query.IsOccluded(new Bounds(new Vector3(0f, 0f, -4f), Vector3.one * 0.2f)), Is.False);
            Assert.That(query.IsOccluded(new Bounds(new Vector3(0f, 0f, -5f), Vector3.one * 0.2f)), Is.False);
            Assert.That(query.IsOccluded(new Bounds(new Vector3(0f, 2.3f, -10f), new Vector3(0.3f, 4.5f, 0.2f))), Is.False);
        }

        [Test]
        public void BackFacesAndNearPlaneIntersectionsDoNotContributeCoverage()
        {
            XRayVegetationOcclusion query = Create(false);
            var hidden = new Bounds(new Vector3(0f, 0f, -10f), Vector3.one * 0.2f);
            var left = new Vector3(-4f, -4f, -5f);
            var top = new Vector3(0f, 4f, -5f);
            var right = new Vector3(4f, -4f, -5f);

            AddTriangle(query, right, top, left);
            AddTriangle(query, left, new Vector3(0f, 4f, -0.1f), right);
            Assert.That(query.IsOccluded(hidden), Is.False);

            AddTriangle(query, right, top, left, true);
            Assert.That(query.IsOccluded(hidden), Is.True);
        }

        [Test]
        public void ANewViewStartsWithoutCoverage()
        {
            XRayVegetationOcclusion query = Create(false);
            AddTriangle(query, new Vector3(-4f, -4f, -5f), new Vector3(0f, 4f, -5f), new Vector3(4f, -4f, -5f));
            var hidden = new Bounds(new Vector3(0f, 0f, -10f), Vector3.one * 0.2f);
            Assert.That(query.IsOccluded(hidden), Is.True);

            _reset.Invoke(query, new object[] { Matrix4x4.identity, Matrix4x4.Perspective(60f, 1f, 0.3f, 100f), 0.3f, 100f });
            Assert.That(query.IsOccluded(hidden), Is.False);
        }

        [Test]
        public void HomAndRenderMeshCanCompleteOneOcclusionQuery()
        {
            XRayVegetationOcclusion query = Create(true);
            var hidden = new Bounds(new Vector3(0f, 0f, -10f), new Vector3(0.9f, 0.2f, 0.2f));
            _addHomTriangle.Invoke(query, new object[] { new Vector3(-4f, 0f, -5f), new Vector3(0.25f, 4f, -5f), new Vector3(0.25f, -4f, -5f), false });
            Assert.That(query.IsOccluded(hidden), Is.False);

            AddTriangle(query, new Vector3(4f, 0f, -6f), new Vector3(-0.25f, -4f, -6f), new Vector3(-0.25f, 4f, -6f));
            Assert.That(query.IsOccluded(hidden), Is.True);
        }

        [TestCase(0.15f, -6f)]
        [TestCase(-0.25f, -11f)]
        public void HomDoesNotRejectBoundsAcrossAnUncoveredOrDeeperTile(float rightEdge, float rightDepth)
        {
            XRayVegetationOcclusion query = Create(true);
            var hidden = new Bounds(new Vector3(0f, 0f, -10f), new Vector3(0.9f, 0.2f, 0.2f));
            _addHomTriangle.Invoke(query, new object[] { new Vector3(-4f, 0f, -5f), new Vector3(0.03f, 4f, -5f), new Vector3(0.03f, -4f, -5f), false });
            AddTriangle(query, new Vector3(4f, 0f, rightDepth), new Vector3(rightEdge, -4f, rightDepth), new Vector3(rightEdge, 4f, rightDepth));
            Assert.That(query.IsOccluded(hidden), Is.False);
        }

        private static XRayVegetationOcclusion Create(bool orthographic)
        {
            var query = new XRayVegetationOcclusion();
            Matrix4x4 projection = orthographic ? Matrix4x4.Ortho(-5f, 5f, -5f, 5f, 0.3f, 100f) : Matrix4x4.Perspective(60f, 1f, 0.3f, 100f);
            _reset.Invoke(query, new object[] { Matrix4x4.identity, projection, 0.3f, 100f });
            return query;
        }

        private static void AddTriangle(XRayVegetationOcclusion query, Vector3 a, Vector3 b, Vector3 c, bool doubleSided = false)
        {
            _addTriangle.Invoke(query, new object[] { a, b, c, doubleSided });
        }
    }
}
