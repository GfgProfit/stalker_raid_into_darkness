using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XRay.Importer.Core.Level;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayDetailBackgroundTests
    {
        private static readonly Type SOURCE_TYPE = typeof(XRayDetailPopulation).GetNestedType("Source", BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo STREAM_BACKGROUND = typeof(XRayDetailPopulation).GetMethod("StreamBackground", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo PREPARE_NEAR = typeof(XRayDetailPopulation).GetMethod("PrepareNear", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly Vector3 EYE = new Vector3(1.0f, 1.0f, 1.0f);
        private static readonly XRayGrassValues INITIAL = new XRayGrassValues(0.45f, 110, 1.0f, false);
        private static readonly XRayGrassValues LESS_DENSE = new XRayGrassValues(0.8f, 110, 1.0f, false);

        [Test]
        public void StartupPreparesNearbyCellsBeforeTheirFirstPublication()
        {
            using var lifetime = new CancellationTokenSource();
            XRayDetailPopulation population = Population();
            PREPARE_NEAR.Invoke(population, new object[] { EYE, INITIAL, lifetime.Token });
            Assert.That(population.ResidentCells, Is.Zero);

            try
            {
                for (int step = 0; step < 16 && population.ResidentCells == 0; step++) BackgroundStep(population, lifetime.Token);
                Assert.That(CellsByKey(population).ContainsKey(new Vector2Int(1, 1)), Is.True);
            }
            finally
            {
                lifetime.Cancel();
            }
        }

        [UnityTest]
        public IEnumerator BackgroundStreamingStartsAtTheViewerAndMatchesSynchronousPlacement()
        {
            using (var lifetime = new CancellationTokenSource())
            {
                try
                {
                    XRayDetailPopulation expected = Reference(INITIAL);
                    XRayDetailPopulation population = Population();
                    population.Apply(INITIAL);

                    bool checkedFirstPublication = false;
                    double deadline = Time.realtimeSinceStartupAsDouble + 5.0;
                    while (population.NeedsStreaming && Time.realtimeSinceStartupAsDouble < deadline)
                    {
                        BackgroundStep(population, lifetime.Token);
                        if (!checkedFirstPublication && population.ResidentCells != 0)
                        {
                            Assert.That(CellsByKey(population).ContainsKey(new Vector2Int(1, 1)), Is.True);
                            checkedFirstPublication = true;
                        }
                        yield return null;
                    }

                    Assert.That(checkedFirstPublication, Is.True);
                    Assert.That(population.NeedsStreaming, Is.False);
                    AssertEquivalent(expected, population);
                }
                finally
                {
                    lifetime.Cancel();
                }
            }
        }

        [UnityTest]
        public IEnumerator DensityChangeDiscardsPreviouslyScheduledResults()
        {
            using (var lifetime = new CancellationTokenSource())
            {
                try
                {
                    XRayDetailPopulation expected = Reference(LESS_DENSE);
                    Assert.That(expected.ResidentInstances, Is.LessThan(Reference(INITIAL).ResidentInstances));

                    XRayDetailPopulation population = Population();
                    population.Apply(INITIAL);
                    BackgroundStep(population, lifetime.Token);
                    population.Apply(LESS_DENSE);
                    double deadline = Time.realtimeSinceStartupAsDouble + 5.0;
                    while (population.NeedsStreaming && Time.realtimeSinceStartupAsDouble < deadline)
                    {
                        BackgroundStep(population, lifetime.Token);
                        yield return null;
                    }

                    Assert.That(population.NeedsStreaming, Is.False);
                    AssertEquivalent(expected, population);
                }
                finally
                {
                    lifetime.Cancel();
                }
            }
        }

        private static XRayDetailPopulation Reference(XRayGrassValues settings)
        {
            XRayDetailPopulation population = Population();
            population.Apply(settings);
            for (int step = 0; step < 256 && population.NeedsStreaming; step++)
            {
                population.Stream(EYE, 0.5f);
            }

            Assert.That(population.NeedsStreaming, Is.False);
            Assert.That(population.ResidentCells, Is.EqualTo(9));
            Assert.That(population.ResidentInstances, Is.GreaterThan(0));
            return population;
        }

        private static void BackgroundStep(XRayDetailPopulation population, CancellationToken token)
        {
            STREAM_BACKGROUND.Invoke(population, new object[] { EYE, 0.5f, token });
        }

        private static void AssertEquivalent(XRayDetailPopulation expected, XRayDetailPopulation actual)
        {
            Assert.That(actual.ResidentInstances, Is.EqualTo(expected.ResidentInstances));
            Dictionary<Vector2Int, XRayDetailCell> expectedCells = CellsByKey(expected);
            Dictionary<Vector2Int, XRayDetailCell> actualCells = CellsByKey(actual);
            CollectionAssert.AreEquivalent(expectedCells.Keys, actualCells.Keys);
            foreach (KeyValuePair<Vector2Int, XRayDetailCell> entry in expectedCells)
            {
                XRayDetailCell cell = actualCells[entry.Key];
                Assert.That(cell.Bounds, Is.EqualTo(entry.Value.Bounds));
                Assert.That(cell.FadeCenter, Is.EqualTo(entry.Value.FadeCenter));
                CollectionAssert.AreEqual(entry.Value.Instances, cell.Instances);
            }
        }

        private static Dictionary<Vector2Int, XRayDetailCell> CellsByKey(XRayDetailPopulation population)
        {
            var cells = new Dictionary<Vector2Int, XRayDetailCell>();
            foreach (XRayDetailCell cell in population.Cells)
            {
                if (population.IsCurrent(cell))
                {
                    cells.Add(new Vector2Int(cell.X, cell.Z), cell);
                }
            }
            return cells;
        }

        private static XRayDetailPopulation Population()
        {
            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object source = Activator.CreateInstance(SOURCE_TYPE, FLAGS, null, new object[] { Details(), Collision(), null, null, "Grass background fixture", new[] { false }, new Bounds?[1], true, true, 0.6f }, null);
            return (XRayDetailPopulation)Activator.CreateInstance(typeof(XRayDetailPopulation), FLAGS, null, new object[] { source, CancellationToken.None }, null);
        }

        private static byte[] Details()
        {
            const int SIZE = 3;
            var header = new BinaryBuffer();
            header.Writer.Write(XRayDetails.VERSION);
            header.Writer.Write(1u);
            header.Writer.Write(1);
            header.Writer.Write(1);
            header.Writer.Write(SIZE);
            header.Writer.Write(SIZE);
            var model = new BinaryBuffer();
            model.Writer.Write(new byte[] { 0, 0 });
            model.Writer.Write(0u);
            model.Writer.Write(1.0f);
            model.Writer.Write(1.0f);
            model.Writer.Write(3u);
            model.Writer.Write(3u);
            foreach (Vector3 vertex in new[] { new Vector3(-0.15f, 0.0f, 0.0f), Vector3.up, new Vector3(0.15f, 0.0f, 0.0f) })
            {
                model.Writer.Write(vertex.x);
                model.Writer.Write(vertex.y);
                model.Writer.Write(vertex.z);
                model.Writer.Write(0.0f);
                model.Writer.Write(0.0f);
            }
            model.Writer.Write((ushort)0);
            model.Writer.Write((ushort)1);
            model.Writer.Write((ushort)2);
            var models = new BinaryBuffer();
            models.WriteChunk(0, model);
            var slots = new BinaryBuffer();
            for (int index = 0; index < SIZE * SIZE; index++)
            {
                slots.Writer.Write(995u | (20u << 12) | (63u << 26));
                slots.Writer.Write(63u | (63u << 6) | (10u << 12) | (15u << 16));
                slots.Writer.Write(ushort.MaxValue);
                slots.Writer.Write((ushort)0);
                slots.Writer.Write((ushort)0);
                slots.Writer.Write((ushort)0);
            }
            var result = new BinaryBuffer();
            result.WriteChunk(0, header);
            result.WriteChunk(1, models);
            result.WriteChunk(2, slots);
            return result.ToArray();
        }

        private static byte[] Collision()
        {
            var result = new BinaryBuffer();
            result.Writer.Write(XRayCollisionModel.VERSION);
            result.Writer.Write(4);
            result.Writer.Write(2);
            foreach (float value in new[] { -8.0f, 0.0f, -8.0f, 8.0f, 0.25f, 8.0f, -8.0f, 0.0f, -8.0f, -8.0f, 0.0f, 8.0f, 8.0f, 0.25f, 8.0f, 8.0f, 0.25f, -8.0f })
            {
                result.Writer.Write(value);
            }
            foreach (int value in new[] { 0, 1, 2, 0, 0, 2, 3, 0 })
            {
                result.Writer.Write(value);
            }
            return result.ToArray();
        }
    }
}
