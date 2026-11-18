using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayVegetationOccluderIndexTests
    {
        private const int RENDERER_BUDGET = 768;
        private const float DISTANCE_SQUARED = 96f * 96f;

        private static readonly Type _indexType = typeof(XRayVegetationOcclusion).Assembly.GetType("XRay.Importer.Levels.XRayVegetationOccluderIndex", true);
        private static readonly ConstructorInfo _constructor = _indexType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Bounds[]) }, null);
        private static readonly MethodInfo _select = _indexType.GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);

        [Test]
        public void ExceedingTheViewBudgetKeepsSelectionStableAndFavorsNearbyGeometry()
        {
            var sources = new Bounds[RENDERER_BUDGET * 2];
            for (int index = 0; index < sources.Length; index++)
            {
                float x = index < RENDERER_BUDGET ? -40f : 40f;
                sources[index] = new Bounds(new Vector3(x, 0f, 20f), Vector3.one);
            }

            object spatialIndex = _constructor.Invoke(new object[] { sources });
            Plane[] planes = CreatePlanes(96f);
            var selected = new int[RENDERER_BUDGET];
            var nextSelection = new int[RENDERER_BUDGET];
            Vector3 left = new Vector3(-32f, 0f, 0f);
            Vector3 right = new Vector3(32f, 0f, 0f);

            Assert.That(Select(spatialIndex, planes, left, selected), Is.EqualTo(RENDERER_BUDGET));
            Assert.That(selected, Is.All.LessThan(RENDERER_BUDGET));
            Assert.That(Select(spatialIndex, planes, right, nextSelection), Is.EqualTo(RENDERER_BUDGET));
            Assert.That(nextSelection, Is.All.GreaterThanOrEqualTo(RENDERER_BUDGET));
            Assert.That(Select(spatialIndex, planes, left, nextSelection), Is.EqualTo(RENDERER_BUDGET));
            CollectionAssert.AreEqual(selected, nextSelection);
        }

        [Test]
        public void EntirelyOutOfViewGroupsDoNotConsumeTheRendererBudget()
        {
            var sources = new Bounds[64];
            for (int index = 0; index < 16; index++)
            {
                sources[index] = new Bounds(new Vector3(0f, 0f, -24f), Vector3.one);
                sources[index + 16] = new Bounds(new Vector3(18f, 0f, 20f), new Vector3(30f, 1f, 1f));
                sources[index + 32] = new Bounds(new Vector3(60f, 0f, 40f), Vector3.one);
                sources[index + 48] = new Bounds(new Vector3(0f, 0f, 110f), Vector3.one);
            }

            object spatialIndex = _constructor.Invoke(new object[] { sources });
            var selected = new int[16];
            Assert.That(Select(spatialIndex, CreatePlanes(10f), Vector3.zero, selected), Is.EqualTo(16));
            Assert.That(selected, Is.All.InRange(16, 31));
        }

        private static int Select(object index, Plane[] planes, Vector3 eye, int[] selected)
        {
            return (int)_select.Invoke(index, new object[] { planes, eye, DISTANCE_SQUARED, selected });
        }

        private static Plane[] CreatePlanes(float horizontalExtent)
        {
            return new[]
            {
                new Plane(Vector3.right, horizontalExtent),
                new Plane(Vector3.left, horizontalExtent),
                new Plane(Vector3.up, 10f),
                new Plane(Vector3.down, 10f),
                new Plane(Vector3.forward, 0f),
                new Plane(Vector3.back, 120f)
            };
        }
    }
}
