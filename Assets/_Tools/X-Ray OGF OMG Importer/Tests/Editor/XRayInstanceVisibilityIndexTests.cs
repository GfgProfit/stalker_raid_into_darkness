using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayInstanceVisibilityIndexTests
    {
        private readonly List<XRayInstanceBatchSet> _sets = new List<XRayInstanceBatchSet>();

        [TearDown]
        public void TearDown()
        {
            foreach (XRayInstanceBatchSet set in _sets) UnityEngine.Object.DestroyImmediate(set);
            _sets.Clear();
        }

        [Test]
        public void PlaneCountUsesOnlyTheRequestedNativeSplitAndEmptySelectionKeepsAll()
        {
            var index = new XRayInstanceVisibilityIndex();
            index.Ensure(Set(Batch(Vector3.left * 100f), Batch(Vector3.right * 100f)));
            Plane[] planes = { new Plane(Vector3.right, Vector3.zero), new Plane(Vector3.left, -200f), new Plane(Vector3.up, float.NaN) };
            index.Select(planes, 1, null);
            Assert.That(index.IsCandidate(0), Is.False);
            Assert.That(index.IsCandidate(1), Is.True);
            index.Select(planes, 0, null);
            Assert.That(index.IsCandidate(0), Is.True);
            Assert.That(index.IsCandidate(1), Is.True);
            index.Select(Array.Empty<Plane>(), 0, null);
            Assert.That(index.IsCandidate(0), Is.True);
            Assert.That(index.IsCandidate(1), Is.True);
        }

        [TestCase(-1)]
        [TestCase(2)]
        public void InvalidPlaneCountPreservesCandidates(int count)
        {
            var index = new XRayInstanceVisibilityIndex();
            index.Ensure(Set(Batch(Vector3.left * 1000f)));
            index.Select(new[] { new Plane(Vector3.right, Vector3.zero) }, count, null);
            Assert.That(index.IsCandidate(0), Is.True);
            index.Select(null, 0, null);
            Assert.That(index.IsCandidate(0), Is.True);
        }

        [Test]
        public void ReinitializeReorderReplaceAndNewOwnerInvalidateSelection()
        {
            XRayInstanceBatch visible = Batch(Vector3.zero);
            XRayInstanceBatch outside = Batch(Vector3.right * 100f);
            XRayInstanceBatchSet set = Set(visible, outside);
            var index = new XRayInstanceVisibilityIndex();
            Plane[] planes = BoxPlanes(Vector3.zero, Vector3.one * 10f, Quaternion.identity);
            index.Ensure(set);
            index.Select(planes, 6, null);
            Assert.That(index.IsCandidate(0), Is.True);
            Assert.That(index.IsCandidate(1), Is.False);
            set.Initialize("reordered", 128f, new List<XRayInstanceBatch> { outside, visible });
            index.Ensure(set);
            index.Select(planes, 6, null);
            Assert.That(index.IsCandidate(0), Is.False);
            Assert.That(index.IsCandidate(1), Is.True);
            set.Initialize("replaced", 128f, new List<XRayInstanceBatch> { Batch(Vector3.up * 200f), outside, visible });
            index.Ensure(set);
            index.Select(planes, 6, null);
            Assert.That(index.IsCandidate(0), Is.False);
            Assert.That(index.IsCandidate(1), Is.False);
            Assert.That(index.IsCandidate(2), Is.True);
            index.Ensure(Set(visible));
            index.Select(planes, 6, null);
            Assert.That(index.IsCandidate(0), Is.True);
            Assert.That(index.MemoryBytes, Is.LessThan(200));
            index.Ensure(null);
            Assert.That(index.MemoryBytes, Is.Zero);
            Assert.That(index.VisitedNodes, Is.Zero);
            Assert.That(index.IsCandidate(0), Is.True);
        }

        [Test]
        public void CameraQueriesDoNotRetainPreviousCameraSelection()
        {
            var index = new XRayInstanceVisibilityIndex();
            index.Ensure(Set(Batch(Vector3.left * 100f), Batch(Vector3.right * 100f)));
            index.Select(BoxPlanes(Vector3.left * 100f, Vector3.one * 10f, Quaternion.identity), 6, null);
            Assert.That(index.IsCandidate(0), Is.True);
            Assert.That(index.IsCandidate(1), Is.False);
            index.Select(BoxPlanes(Vector3.right * 100f, Vector3.one * 10f, Quaternion.identity), 6, null);
            Assert.That(index.IsCandidate(0), Is.False);
            Assert.That(index.IsCandidate(1), Is.True);
        }

        [Test]
        public void ConservativeOverrideKeepsGeometryOutsideAuthoredBounds()
        {
            XRayInstanceBatchSet set = Set(Batch(Vector3.right * 100f));
            var index = new XRayInstanceVisibilityIndex();
            Plane[] planes = BoxPlanes(Vector3.zero, Vector3.one * 10f, Quaternion.identity);
            index.Ensure(set);
            index.Select(planes, planes.Length, null);
            Assert.That(index.IsCandidate(0), Is.False);
            index.Ensure(set, new[] { new Bounds(Vector3.zero, Vector3.one * 4f) }, 1);
            index.Select(planes, planes.Length, null);
            Assert.That(index.IsCandidate(0), Is.True);
            index.Select(BoxPlanes(Vector3.right * 100f, Vector3.one * 10f, Quaternion.identity), 6, null);
            Assert.That(index.IsCandidate(0), Is.True);
            index.Ensure(set);
            index.Select(planes, planes.Length, null);
            Assert.That(index.IsCandidate(0), Is.False);
        }

        [Test]
        public void ConservativeBoundsRevisionAndArrayIdentityInvalidateTheSameOwner()
        {
            XRayInstanceBatchSet set = Set(Batch(Vector3.right * 100f));
            var index = new XRayInstanceVisibilityIndex();
            Plane[] planes = BoxPlanes(Vector3.zero, Vector3.one * 10f, Quaternion.identity);
            Bounds[] bounds = { new Bounds(Vector3.right * 120f, Vector3.one) };
            index.Ensure(set, bounds, 1);
            index.Select(planes, planes.Length, null);
            Assert.That(index.IsCandidate(0), Is.False);
            bounds[0] = new Bounds(Vector3.zero, Vector3.one);
            index.Ensure(set, bounds, 2);
            index.Select(planes, planes.Length, null);
            Assert.That(index.IsCandidate(0), Is.True);
            index.Ensure(set, new[] { new Bounds(Vector3.right * 150f, Vector3.one) }, 2);
            index.Select(planes, planes.Length, null);
            Assert.That(index.IsCandidate(0), Is.False);
        }

        [Test]
        public void ConservativeBoundsLengthMustMatchSourceCount()
        {
            XRayInstanceBatchSet set = Set(Batch(Vector3.zero));
            var index = new XRayInstanceVisibilityIndex();
            Assert.Throws<ArgumentException>(() => index.Ensure(set, Array.Empty<Bounds>(), 0));
            Assert.Throws<ArgumentException>(() => index.Ensure(set, new Bounds[2], 0));
            Assert.DoesNotThrow(() => index.Ensure(set, new Bounds[1], 0));
        }

        [Test]
        public void NonfiniteConservativeOverrideKeepsTheSource()
        {
            var index = new XRayInstanceVisibilityIndex();
            index.Ensure(Set(Batch(Vector3.right * 100f)), new[] { new Bounds(new Vector3(float.NaN, 0f, 0f), Vector3.one) }, 1);
            index.Select(BoxPlanes(Vector3.zero, Vector3.one * 10f, Quaternion.identity), 6, null);
            Assert.That(index.IsCandidate(0), Is.True);
            Assert.That(index.SelectedCount, Is.EqualTo(1));
        }

        [Test]
        public void SelectedEnumerationContainsExactlyCurrentCandidatesWithoutDuplicates()
        {
            XRayInstanceBatchSet set = Set(Batch(Vector3.zero), Batch(Vector3.right * 100f), Batch(Vector3.right * 2f), Batch(Vector3.up * 5f));
            var index = new XRayInstanceVisibilityIndex();
            index.Ensure(set);
            index.Select(BoxPlanes(Vector3.zero, Vector3.one * 10f, Quaternion.identity), 6, null);
            Assert.That(index.SelectedCount, Is.EqualTo(3));
            AssertSelectedEnumeration(index, set.Batches.Count);
            index.Select(BoxPlanes(Vector3.right * 100f, Vector3.one * 10f, Quaternion.identity), 6, null);
            Assert.That(index.SelectedCount, Is.EqualTo(1));
            Assert.That(index.SelectedSource(0), Is.EqualTo(1));
            AssertSelectedEnumeration(index, set.Batches.Count);
            index.Select(Array.Empty<Plane>(), 0, null);
            Assert.That(index.SelectedCount, Is.EqualTo(set.Batches.Count));
            AssertSelectedEnumeration(index, set.Batches.Count);
            index.Ensure(null);
            Assert.That(index.SelectedCount, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => index.SelectedSource(0));
        }

        [Test]
        public void RandomizedHierarchyContainsEveryBruteForcePlaneCandidate()
        {
            var random = new System.Random(17421);
            var batches = new List<XRayInstanceBatch>();
            for (int source = 0; source < 193; source++) batches.Add(Batch(RandomVector(random, 400f), source % 3 != 0, RandomVector(random, 40f, true) + Vector3.one));
            XRayInstanceBatchSet set = Set(batches.ToArray());
            var index = new XRayInstanceVisibilityIndex();
            index.Ensure(set);
            var wind = new XRayFloraWindState { Profile = XRayFloraWindProfile.AnomalySsfx, TreesSetup = new Vector4(11f, 0.15f, 0.8f, 0.9f) };
            wind.Reset(new XRayFloraWindWeather(0.4f, 900f, 0.8f));
            for (int camera = 0; camera < 24; camera++)
            {
                Plane[] planes = BoxPlanes(RandomVector(random, 180f), RandomVector(random, 130f, true) + Vector3.one * 5f, Quaternion.Euler(RandomVector(random, 180f)));
                for (int count = 1; count <= planes.Length; count++)
                {
                    var activePlanes = new Plane[count];
                    Array.Copy(planes, activePlanes, count);
                    foreach (XRayFloraWindState currentWind in new[] { null, wind })
                    {
                        index.Select(planes, count, currentWind);
                        AssertSelectedEnumeration(index, batches.Count);
                        for (int source = 0; source < batches.Count; source++)
                        {
                            Bounds bounds = XRayFloraWindBounds.Expand(batches[source].Bounds, currentWind);
                            if (GeometryUtility.TestPlanesAABB(activePlanes, bounds)) Assert.That(index.IsCandidate(source), Is.True, "camera " + camera + " plane count " + count + " source " + source);
                        }
                    }
                }
            }
        }

        [Test]
        public void LargeLeafAndCurrentWindReachAcrossSpatialBoundary()
        {
            XRayInstanceBatchSet set = Set(Batch(Vector3.right * 100f, false, new Vector3(220f, 2f, 2f)), Batch(Vector3.right * 16f, false, new Vector3(2f, 30f, 2f)));
            var index = new XRayInstanceVisibilityIndex();
            index.Ensure(set);
            Plane[] planes = BoxPlanes(Vector3.zero, Vector3.one * 10f, Quaternion.identity);
            index.Select(planes, 6, null);
            Assert.That(index.IsCandidate(0), Is.True);
            Assert.That(index.IsCandidate(1), Is.False);
            var wind = new XRayFloraWindState { Profile = XRayFloraWindProfile.AnomalySsfx, TreesSetup = new Vector4(11f, 0.15f, 1f, 1f) };
            wind.Reset(new XRayFloraWindWeather(0f, 1000f, 1f));
            Assert.That(GeometryUtility.TestPlanesAABB(planes, XRayFloraWindBounds.Expand(set.Batches[1].Bounds, wind)), Is.True);
            index.Select(planes, 6, wind);
            Assert.That(index.IsCandidate(0), Is.True);
            Assert.That(index.IsCandidate(1), Is.True);
        }

        [TestCase(1000f)]
        [TestCase(1000000f)]
        [TestCase(100000000f)]
        public void LargeCoordinatesDoNotLoseBruteForceBoundaryCandidates(float origin)
        {
            var random = new System.Random(7912);
            var batches = new List<XRayInstanceBatch>();
            Vector3 center = new Vector3(origin, -origin, origin);
            for (int source = 0; source < 71; source++) batches.Add(Batch(center + RandomVector(random, 24f), true, RandomVector(random, 4f, true) + Vector3.one * 0.05f));
            var index = new XRayInstanceVisibilityIndex();
            index.Ensure(Set(batches.ToArray()));
            for (int query = 0; query < 16; query++)
            {
                Plane[] planes = BoxPlanes(center + RandomVector(random, 16f), RandomVector(random, 8f, true) + Vector3.one * 0.1f, Quaternion.Euler(RandomVector(random, 180f)));
                index.Select(planes, planes.Length, null);
                for (int source = 0; source < batches.Count; source++)
                {
                    if (GeometryUtility.TestPlanesAABB(planes, batches[source].Bounds)) Assert.That(index.IsCandidate(source), Is.True, "origin " + origin + " query " + query + " source " + source);
                }
            }
        }

        [Test]
        public void InvalidBoundsPlanesAndWindPreserveCandidates()
        {
            var index = new XRayInstanceVisibilityIndex();
            index.Ensure(Set(Batch(new Vector3(float.NaN, 0f, 0f)), Batch(Vector3.one * 100f)));
            Plane[] planes = BoxPlanes(Vector3.zero, Vector3.one, Quaternion.identity);
            index.Select(planes, 6, null);
            Assert.That(index.IsCandidate(0), Is.True);
            index.Ensure(Set(Batch(Vector3.one * 100f)));
            planes[0] = new Plane(Vector3.right, float.NaN);
            index.Select(planes, 6, null);
            Assert.That(index.IsCandidate(0), Is.True);
            var wind = new XRayFloraWindState { Profile = XRayFloraWindProfile.AnomalySsfx, TreesSetup = new Vector4(11f, 0.15f, float.NaN, 1f) };
            index.Select(BoxPlanes(Vector3.zero, Vector3.one, Quaternion.identity), 6, wind);
            Assert.That(index.IsCandidate(0), Is.True);
        }

        [Test]
        public void DistantClustersAreRejectedWithoutVisitingEveryLeaf()
        {
            var batches = new List<XRayInstanceBatch>();
            for (int index = 0; index < 512; index++) batches.Add(Batch(new Vector3(index < 256 ? -1000f : 1000f, index % 16, index / 16), false));
            var selection = new XRayInstanceVisibilityIndex();
            selection.Ensure(Set(batches.ToArray()));
            selection.Select(BoxPlanes(Vector3.zero, Vector3.one * 10f, Quaternion.identity), 6, null);
            for (int index = 0; index < batches.Count; index++) Assert.That(selection.IsCandidate(index), Is.False);
            Assert.That(selection.VisitedNodes, Is.LessThan(batches.Count / 4));
        }

        private XRayInstanceBatchSet Set(params XRayInstanceBatch[] batches)
        {
            var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            set.Initialize("visibility", 128f, new List<XRayInstanceBatch>(batches));
            _sets.Add(set);
            return set;
        }

        private static void AssertSelectedEnumeration(XRayInstanceVisibilityIndex index, int sourceCount)
        {
            var selected = new HashSet<int>();
            for (int ordinal = 0; ordinal < index.SelectedCount; ordinal++)
            {
                int source = index.SelectedSource(ordinal);
                Assert.That(source, Is.InRange(0, sourceCount - 1));
                Assert.That(selected.Add(source), Is.True);
            }
            for (int source = 0; source < sourceCount; source++) Assert.That(index.IsCandidate(source), Is.EqualTo(selected.Contains(source)));
        }

        private static XRayInstanceBatch Batch(Vector3 center, bool shadows = true, Vector3 size = default)
        {
            if (size == Vector3.zero) size = Vector3.one;
            return new XRayInstanceBatch("visibility", null, Array.Empty<Material>(), new Bounds(center, size), new[] { Matrix4x4.Translate(center) }, null, shadows);
        }

        private static Vector3 RandomVector(System.Random random, float range, bool positive = false)
        {
            float shift = positive ? 0f : -0.5f;
            return new Vector3(((float)random.NextDouble() + shift) * range, ((float)random.NextDouble() + shift) * range, ((float)random.NextDouble() + shift) * range);
        }

        private static Plane[] BoxPlanes(Vector3 center, Vector3 extent, Quaternion rotation)
        {
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            return new[] { new Plane(right, center - right * extent.x), new Plane(-right, center + right * extent.x), new Plane(up, center - up * extent.y), new Plane(-up, center + up * extent.y), new Plane(forward, center - forward * extent.z), new Plane(-forward, center + forward * extent.z) };
        }
    }
}
