using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace Rvm.Benchmark.Tests
{
    public sealed class InstancedRendererEquivalenceTests
    {
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private BenchmarkRendererReference _old;
        private BenchmarkRendererCandidate _current;
        private XRayInstanceBatchSet _set;
        private Camera _camera;
        private Mesh _mesh;
        private Material _material;

        [SetUp]
        public void SetUp()
        {
            _old = Own(new GameObject("old")).AddComponent<BenchmarkRendererReference>();
            _current = Own(new GameObject("new")).AddComponent<BenchmarkRendererCandidate>();
            _set = Own(ScriptableObject.CreateInstance<XRayInstanceBatchSet>());
            _camera = Own(new GameObject("test camera")).AddComponent<Camera>();
            _camera.enabled = false;
            _camera.transform.position = new Vector3(0, 0, -10);
            _mesh = Own(new Mesh());
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.subMeshCount = 3;
            for (int submesh = 0; submesh < 3; submesh++) _mesh.SetTriangles(new[] { 0, 1, 2 }, submesh);
            _mesh.RecalculateBounds();
            _material = Own(new Material(Shader.Find("HDRP/Lit")) { enableInstancing = true });
            _old.BatchSet = _set;
            _current.BatchSet = _set;
            RendererSubmissionTrace.Enabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            RendererSubmissionTrace.Enabled = false;
            RendererSubmissionTrace.Entries.Clear();
            for (int i = _owned.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(511)]
        [TestCase(512)]
        [TestCase(1023)]
        public void HistoryAndSubmissionsRemainBitExactAcrossTemporalSequence(int count)
        {
            var batch = Batch(count);
            _set.Initialize("test", 32, new List<XRayInstanceBatch> { batch });
            foreach (int frame in new[] { 0, 1, 2, 2, 3, 4, 5, 19, 20, 21, 22, 23 })
            {
                if (count > 0 && (frame == 1 || frame == 2 || frame == 5 || frame == 19 || frame == 22)) batch.Matrices[count - 1].m03 += 0.25f;
                if (frame == 3) _camera.transform.position = new Vector3(10000, 0, 0);
                if (frame == 20) _camera.transform.position = new Vector3(0, 0, -10);
                if (frame == 21)
                {
                    _old.ResetMotionHistory();
                    _current.ResetMotionHistory();
                }
                CompareCamera(frame);
                AssertPayload(0);
            }
            _old.enabled = false;
            _current.enabled = false;
            _old.enabled = true;
            _current.enabled = true;
            Assert.AreEqual(0, _old.HistoryCount);
            Assert.AreEqual(0, _current.HistoryCount);
            CompareCamera(23);
            AssertPayload(0);
        }

        [Test]
        public void MatrixEqualityMatchesInstalledUnityForSpecialValuesAndAllComponents()
        {
            float[] values = { 0f, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), 1f, -1f, float.Epsilon, float.PositiveInfinity, float.NegativeInfinity, float.NaN, BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00017)) };
            var batch = Batch(1);
            foreach (int component in Enumerable.Range(0, 16))
            {
                foreach (float before in values)
                {
                    foreach (float now in values)
                    {
                        _old.ResetMotionHistory();
                        _current.ResetMotionHistory();
                        batch.Matrices[0] = Matrix4x4.identity;
                        batch.Matrices[0][component] = before;
                        _old.Observe(0, batch, 0);
                        _current.Observe(0, batch, 0);
                        batch.Matrices[0][component] = now;
                        _old.Observe(0, batch, 1);
                        _current.Observe(0, batch, 1);
                        AssertPayload(0);
                    }
                }
            }
        }

        [Test]
        public void ReinitializeReorderReplacementAndMutableArraysKeepExistingContract()
        {
            var first = Batch(512);
            var second = Batch(1);
            _set.Initialize("test", 32, new List<XRayInstanceBatch> { first, second });
            CompareCamera(0);
            foreach (var sequence in new[] { new[] { second, first }, new[] { Batch(1023), second }, new[] { first }, new[] { second, first } })
            {
                _set.Initialize("same object", 32, sequence.ToList());
                first.Matrices[0].m03 += 0.5f;
                first.Data[0] = new Vector4(7, 8, 9, 10);
                first.Materials[1] = _material;
                CompareCamera(1);
                for (int index = 0; index < sequence.Length; index++) AssertPayload(index);
            }
            var replacement = Own(ScriptableObject.CreateInstance<XRayInstanceBatchSet>());
            replacement.Initialize("replacement", 32, new List<XRayInstanceBatch> { second });
            _old.BatchSet = replacement;
            _current.BatchSet = replacement;
            CompareCamera(1);
            AssertPayload(0);
        }

        [Test]
        public void ResolverOrderNullMaterialsAndLateDataMutationAreIdentical()
        {
            var batch = Batch(1023);
            _set.Initialize("test", 32, new List<XRayInstanceBatch> { batch });
            int calls = 0;
            Func<Material, Material> resolver = material =>
            {
                calls++;
                batch.Data[0] = new Vector4(calls, 2, 3, 4);
                return calls % 2 == 0 ? null : material;
            };
            _old.MaterialResolver = resolver;
            _current.MaterialResolver = resolver;
            calls = 0;
            batch.Data[0] = Vector4.one;
            string[] expected = Trace(() => _old.CameraCallback(_camera, 1));
            int oldCalls = calls;
            calls = 0;
            batch.Data[0] = Vector4.one;
            string[] actual = Trace(() => _current.CameraCallback(_camera, 1));
            Assert.AreEqual(oldCalls, calls);
            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void LodSplitPreservesFullBothImpostorDiscardFallbackAndPayloadOrder()
        {
            var matrices = Enumerable.Repeat(Matrix4x4.identity, 1023).ToArray();
            var batch = new XRayInstanceBatch("LOD", _mesh, new[] { _material, null, _material }, new Bounds(Vector3.zero, Vector3.one * 100), matrices, new[] { Vector4.one }, true, Enumerable.Range(0, 1023).Select(i => i % 6 - 1).ToArray());
            _set.Initialize("LOD", 32, new List<XRayInstanceBatch> { batch });
            Invoke(_old, "EnsureProperties");
            Invoke(_current, "EnsureProperties");
            _old.Observe(0, batch, 0);
            _current.Observe(0, batch, 0);
            batch.Matrices[511].m03 = 1;
            _old.Observe(0, batch, 1);
            _current.Observe(0, batch, 1);
            foreach (byte[] states in new[] { new byte[] { 0, 1, 2, 3 }, new byte[] { 2, 2, 3, 3 }, new byte[] { 0, 0, 0, 0 } })
            {
                foreach (bool shadows in new[] { false, true })
                {
                    var expected = InvokeLod(_old, batch, states, shadows);
                    var actual = InvokeLod(_current, batch, states, shadows);
                    Assert.AreEqual(expected.result, actual.result);
                    Assert.AreEqual(expected.shadow, actual.shadow);
                    Assert.AreEqual(expected.discard, actual.discard);
                    CollectionAssert.AreEqual(expected.trace, actual.trace);
                }
            }
        }

        [Test]
        public void ReflectionUsesRawMatricesAndNeverAdvancesMainHistory()
        {
            var batch = Batch(512);
            _set.Initialize("test", 32, new List<XRayInstanceBatch> { batch });
            CompareCamera(0);
            string[] before = RendererSubmissionTrace.Payload(_current.Payload(0));
            batch.Matrices[0].m03 += 0.25f;
            _camera.farClipPlane = 100;
            string[] expected = Trace(() => Invoke(_old, "RenderReflection", _camera));
            string[] actual = Trace(() => Invoke(_current, "RenderReflection", _camera));
            CollectionAssert.AreEqual(expected, actual);
            Assert.IsNotEmpty(actual);
            Assert.IsTrue(actual.All(row => row.Contains("ForceNoMotion|Off|True")));
            CollectionAssert.AreEqual(before, RendererSubmissionTrace.Payload(_current.Payload(0)));
            CompareCamera(1);
            AssertPayload(0);
        }

        [Test]
        public void CompiledCandidateHistoryAgreesWithActualProductionRenderer()
        {
            var production = Own(new GameObject("production")).AddComponent<XRayLevelInstancedRenderer>();
            production.enabled = false;
            var batch = Batch(512);
            foreach (int observation in Enumerable.Range(0, 20))
            {
                batch.Matrices[observation].m03 += observation;
                if (observation == 10)
                {
                    production.ResetMotionHistory();
                    _current.ResetMotionHistory();
                }
                object actual = Invoke(production, "History", 0, batch);
                _current.Observe(0, batch, Time.frameCount);
                Array instances = (Array)actual.GetType().GetProperty("Instances").GetValue(actual);
                CollectionAssert.AreEqual(RendererSubmissionTrace.Payload(instances), RendererSubmissionTrace.Payload(_current.Payload(0)));
                Assert.AreEqual(actual.GetType().GetProperty("Moving").GetValue(actual), _current.Moving(0));
            }
        }

        [Test]
        public void ProvenanceGitDrainsOutputLargerThanPipeCapacity()
        {
            var method = typeof(Rvm.Benchmark.Editor.BenchmarkEditorSession).GetMethod("Git", BindingFlags.NonPublic | BindingFlags.Static);
            string output = (string)method.Invoke(null, new object[] { "help -a" });
            Assert.AreNotEqual("unavailable", output);
            Assert.Greater(output.Length, 4096);
        }

        private (object result, int shadow, int discard, string[] trace) InvokeLod(Component renderer, XRayInstanceBatch batch, byte[] states, bool shadows)
        {
            var histories = (IList)renderer.GetType().GetField("_histories", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(renderer);
            object[] args = { _camera, 0, batch, histories[0], batch.Bounds, XRayFloraWindBounds.ActiveState(), states, shadows, 0, 0 };
            object result = null;
            string[] trace = Trace(() => result = Invoke(renderer, "RenderLodBatch", args));
            return (result, (int)args[8], (int)args[9], trace);
        }

        private void CompareCamera(int frame)
        {
            CollectionAssert.AreEqual(Trace(() => _old.CameraCallback(_camera, frame)), Trace(() => _current.CameraCallback(_camera, frame)));
            Assert.AreEqual(_old.LastVisibleInstances, _current.LastVisibleInstances);
            Assert.AreEqual(_old.LastShadowOnlyBatches, _current.LastShadowOnlyBatches);
            Assert.AreEqual(_old.LastRejectedShadowBatches, _current.LastRejectedShadowBatches);
        }

        private void AssertPayload(int index)
        {
            CollectionAssert.AreEqual(RendererSubmissionTrace.Payload(_old.Payload(index)), RendererSubmissionTrace.Payload(_current.Payload(index)));
            Assert.AreEqual(_old.Moving(index), _current.Moving(index));
        }

        private XRayInstanceBatch Batch(int count)
        {
            var matrices = Enumerable.Range(0, count).Select(i => Matrix4x4.Translate(new Vector3(i % 3, i % 5, 0))).ToArray();
            return new XRayInstanceBatch("batch", _mesh, new[] { _material, null, _material }, new Bounds(Vector3.zero, Vector3.one * 20), matrices, new[] { Vector4.one }, true);
        }

        private static string[] Trace(Action action)
        {
            RendererSubmissionTrace.Entries.Clear();
            action();
            return RendererSubmissionTrace.Entries.ToArray();
        }

        private static object Invoke(Component component, string method, params object[] args) => component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, args);

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            _owned.Add(value);
            return value;
        }
    }
}
