using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;

namespace Rvm.Benchmark.Tests
{
    public sealed class BenchmarkShadowLightIndexTests
    {
        private const string RESOLVER_TYPE = "UnityEngine.Rendering.HighDefinition.HDShadowLightIndexResolver, Unity.RenderPipelines.HighDefinition.Runtime";

        [Test]
        public void ParticleTemplateRequestKeepsTheNativeCasterWithPreparedSplits()
        {
            using var entities = new NativeArray<int>(new[] { -1, -1, -1, -1, -1, -1, -1, 53, 53 }, Allocator.Temp);
            var map = new NativeHashMap<int, int>(entities.Length, Allocator.Temp);
            try
            {
                BuildMap(entities, ref map);
                int resolved = Resolve(53, 7, map[53], entities);
                int[] preparedSplitCounts = { 0, 0, 0, 0, 0, 0, 0, 6, 0 };
                Assert.That(resolved, Is.EqualTo(7));
                Assert.That(preparedSplitCounts[resolved], Is.EqualTo(6));
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void UniqueEntityStillRemapsAnOlderNativeIndex()
        {
            using var entities = new NativeArray<int>(new[] { 99, -1, -1, 53 }, Allocator.Temp);
            var map = new NativeHashMap<int, int>(entities.Length, Allocator.Temp);
            try
            {
                BuildMap(entities, ref map);
                Assert.That(Resolve(53, 0, map[53], entities), Is.EqualTo(3));
            }
            finally
            {
                map.Dispose();
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EveryRequestOfASharedTemplateRetainsItsOwnNativeIndex(int requestLightIndex)
        {
            using var entities = new NativeArray<int>(new[] { 53, 53, 53, 99 }, Allocator.Temp);
            var map = new NativeHashMap<int, int>(entities.Length, Allocator.Temp);
            try
            {
                BuildMap(entities, ref map);
                Assert.That(Resolve(53, requestLightIndex, map[53], entities), Is.EqualTo(requestLightIndex));
                Assert.That(Resolve(99, 0, map[99], entities), Is.EqualTo(3));
            }
            finally
            {
                map.Dispose();
            }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(3)]
        public void AmbiguousMappingCannotSelectAnOutOfRangeOrDifferentOwner(int requestLightIndex)
        {
            using var entities = new NativeArray<int>(new[] { 99, 53, 53 }, Allocator.Temp);
            var map = new NativeHashMap<int, int>(entities.Length, Allocator.Temp);
            try
            {
                BuildMap(entities, ref map);
                var error = Assert.Throws<TargetInvocationException>(() => Resolve(53, requestLightIndex, map[53], entities));
                Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void RebuildingForAnotherCameraReplacesThePreviousCameraMapping()
        {
            using var first = new NativeArray<int>(new[] { 53, 53, 99 }, Allocator.Temp);
            using var second = new NativeArray<int>(new[] { -1, 99, 53 }, Allocator.Temp);
            var map = new NativeHashMap<int, int>(first.Length, Allocator.Temp);
            try
            {
                BuildMap(first, ref map);
                BuildMap(second, ref map);
                Assert.That(Resolve(53, 0, map[53], second), Is.EqualTo(2));
                Assert.That(Resolve(99, 2, map[99], second), Is.EqualTo(1));
                Assert.That(map.ContainsKey(-1), Is.False);
            }
            finally
            {
                map.Dispose();
            }
        }

        private static void BuildMap(NativeArray<int> entities, ref NativeHashMap<int, int> map)
        {
            var arguments = new object[] { entities, entities.Length, map };
            ResolverMethod("BuildMap").Invoke(null, arguments);
            map = (NativeHashMap<int, int>)arguments[2];
        }

        private static int Resolve(int dataIndex, int requestLightIndex, int mappedLightIndex, NativeArray<int> entities)
        {
            return (int)ResolverMethod("Resolve").Invoke(null, new object[] { dataIndex, requestLightIndex, mappedLightIndex, entities, entities.Length });
        }

        private static MethodInfo ResolverMethod(string name)
        {
            return Type.GetType(RESOLVER_TYPE, true).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        }
    }
}
