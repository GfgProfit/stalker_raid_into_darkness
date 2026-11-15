using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayInstancePreparationTests
    {
        private static readonly MethodInfo HISTORY = typeof(XRayLevelInstancedRenderer).GetMethod("History", BindingFlags.Instance | BindingFlags.NonPublic);

        private GameObject _root;
        private XRayInstanceBatchSet _set;
        private XRayLevelInstancedRenderer _renderer;
        private XRayInstanceBatch _batch;
        private Matrix4x4[] _input;

        [SetUp]
        public void SetUp()
        {
            _input = new[] { Matrix4x4.TRS(new Vector3(17f, 3f, -4f), Quaternion.Euler(0f, 30f, 0f), Vector3.one), Matrix4x4.identity };
            _batch = new XRayInstanceBatch("fixture", null, Array.Empty<Material>(), new Bounds(Vector3.zero, Vector3.one * 1000f), _input, Array.Empty<Vector4>(), true);
            _set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            _set.Initialize("fixture", 128f, new List<XRayInstanceBatch> { _batch });
            _root = new GameObject("Matrix preparation fixture");
            _renderer = _root.AddComponent<XRayLevelInstancedRenderer>();
            _renderer.BatchSet = _set;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            UnityEngine.Object.DestroyImmediate(_set);
        }

        [Test]
        public void PreparationOwnsMatricesAndAnUnchangedSecondPassDoesNotSerializeChanges()
        {
            Matrix4x4 expected = _input[0];
            Assert.That(XRayInstancePreparation.Ensure(_set), Is.True);
            string serialized = JsonUtility.ToJson(_set);
            Assert.That(XRayInstancePreparation.Ensure(_set), Is.False);
            Assert.That(JsonUtility.ToJson(_set), Is.EqualTo(serialized));
            _input[0] = Matrix4x4.Translate(Vector3.one * 900f);
            Assert.That(_batch.GetMatrix(0), Is.EqualTo(expected));
            Assert.That(_set.MatrixPreparationVersion, Is.EqualTo(XRayInstanceBatchSet.MATRIX_PREPARATION_VERSION));
        }

        [Test]
        public void PreparedStaticHistoryPreservesPayloadAcrossCamerasAndReappearance()
        {
            XRayInstancePreparation.Ensure(_set);
            object history = Observe();
            var payload = Payload(history);
            Advance(history);
            Assert.That(Observe(), Is.SameAs(history));
            Assert.That(Payload(history), Is.SameAs(payload));
            for (int index = 0; index < _batch.Count; index++)
            {
                Assert.That(Matrix(history, index, "objectToWorld"), Is.EqualTo(_batch.GetMatrix(index)));
                Assert.That(Matrix(history, index, "prevObjectToWorld"), Is.EqualTo(_batch.GetMatrix(index)));
            }
            Assert.That(Moving(history), Is.False);
            _root.transform.SetPositionAndRotation(new Vector3(200f, 100f, -300f), Quaternion.Euler(0f, 90f, 0f));
            Assert.That(Matrix(Observe(), 0, "objectToWorld"), Is.EqualTo(_batch.GetMatrix(0)));
        }

        [Test]
        public void MutableArrayFallsBackAndPreservesPreviousMatricesWithinOneFrame()
        {
            XRayInstancePreparation.Ensure(_set);
            object history = Observe();
            Matrix4x4 original = Matrix(history, 0, "objectToWorld");
            Matrix4x4[] mutable = _batch.Matrices;
            Matrix4x4 first = Matrix4x4.Translate(new Vector3(50f, 20f, 3f));
            mutable[0] = first;
            Observe();
            Assert.That(Matrix(history, 0, "prevObjectToWorld"), Is.EqualTo(original));
            Assert.That(Matrix(history, 0, "objectToWorld"), Is.EqualTo(first));
            Matrix4x4 second = Matrix4x4.Translate(new Vector3(-50f, 20f, 3f));
            mutable[0] = second;
            Observe();
            Assert.That(Matrix(history, 0, "prevObjectToWorld"), Is.EqualTo(original));
            Assert.That(Matrix(history, 0, "objectToWorld"), Is.EqualTo(second));
            Assert.That(Moving(history), Is.True);
            Advance(history);
            Observe();
            Assert.That(Matrix(history, 0, "prevObjectToWorld"), Is.EqualTo(second));
            Assert.That(Moving(history), Is.False);
        }

        [Test]
        public void SharedBatchRepreparationKeepsBothConsumersCurrent()
        {
            _set.PrepareMatrices("first");
            object first = Observe();
            Matrix4x4 original = _batch.GetMatrix(0);
            var secondSet = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            var secondRoot = new GameObject("Shared matrix consumer");
            try
            {
                secondSet.Initialize("second", 128f, new List<XRayInstanceBatch> { _batch });
                secondSet.PrepareMatrices("second");
                var secondRenderer = secondRoot.AddComponent<XRayLevelInstancedRenderer>();
                secondRenderer.BatchSet = secondSet;
                HISTORY.Invoke(secondRenderer, new object[] { 0, _batch });
                Matrix4x4 changed = Matrix4x4.Translate(Vector3.right * 70f);
                _batch.Matrices[0] = changed;
                secondSet.PrepareMatrices("changed");
                first = Observe();
                object second = HISTORY.Invoke(secondRenderer, new object[] { 0, _batch });
                Assert.That(Matrix(first, 0, "objectToWorld"), Is.EqualTo(changed));
                Assert.That(Matrix(second, 0, "objectToWorld"), Is.EqualTo(changed));
                Assert.That(Matrix(first, 0, "prevObjectToWorld"), Is.EqualTo(original));
                Observe();
                Assert.That(Matrix(first, 0, "prevObjectToWorld"), Is.EqualTo(original));
                Advance(first);
                Observe();
                Assert.That(Matrix(first, 0, "prevObjectToWorld"), Is.EqualTo(changed));
                Assert.That(Moving(first), Is.False);
                Matrix4x4[] retained = _batch.Matrices;
                retained[0] = Matrix4x4.Translate(Vector3.up * 90f);
                Observe();
                second = HISTORY.Invoke(secondRenderer, new object[] { 0, _batch });
                Assert.That(Matrix(first, 0, "objectToWorld"), Is.EqualTo(retained[0]));
                Assert.That(Matrix(second, 0, "objectToWorld"), Is.EqualTo(retained[0]));
                Matrix4x4 previous = retained[0];
                Advance(first);
                Advance(second);
                retained[0] = Matrix4x4.Translate(Vector3.forward * 110f);
                Observe();
                HISTORY.Invoke(secondRenderer, new object[] { 0, _batch });
                Assert.That(Matrix(first, 0, "prevObjectToWorld"), Is.EqualTo(previous));
                Assert.That(Matrix(second, 0, "prevObjectToWorld"), Is.EqualTo(previous));
                Assert.That(Matrix(first, 0, "objectToWorld"), Is.EqualTo(retained[0]));
                Assert.That(Matrix(second, 0, "objectToWorld"), Is.EqualTo(retained[0]));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(secondRoot);
                UnityEngine.Object.DestroyImmediate(secondSet);
            }
        }

        [Test]
        public void UnpreparedConstructorRetainsMutableInputCompatibility()
        {
            object history = Observe();
            Matrix4x4 changed = Matrix4x4.Translate(Vector3.up * 70f);
            _input[0] = changed;
            Observe();
            Assert.That(Matrix(history, 0, "objectToWorld"), Is.EqualTo(changed));
            Assert.That(Moving(history), Is.True);
        }

        [Test]
        public void MatrixEditsInvalidateSignatureAndRepreparationResetsHistory()
        {
            XRayInstancePreparation.Ensure(_set);
            object history = Observe();
            string signature = _set.MatrixInputSignature;
            _batch.Matrices[0] = Matrix4x4.Translate(Vector3.forward * 200f);
            Assert.That(XRayInstancePreparation.InputSignature(_set), Is.Not.EqualTo(signature));
            Assert.That(XRayInstancePreparation.Ensure(_set), Is.True);
            Assert.That(Observe(), Is.Not.SameAs(history));
            Assert.That(Matrix(Observe(), 0, "objectToWorld"), Is.EqualTo(_batch.GetMatrix(0)));
        }

        [Test]
        public void UnknownPreparationVersionUsesTheFullHistoryPath()
        {
            XRayInstancePreparation.Ensure(_set);
            object history = Observe();
            typeof(XRayInstanceBatchSet).GetField("_matrixPreparationVersion", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_set, XRayInstanceBatchSet.MATRIX_PREPARATION_VERSION + 1);
            var matrices = (Matrix4x4[])typeof(XRayInstanceBatch).GetField("_matrices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_batch);
            matrices[0] = Matrix4x4.Translate(Vector3.right * 50f);
            Observe();
            Assert.That(Moving(history), Is.True);
            Assert.That(XRayInstancePreparation.Ensure(_set), Is.True);
        }

        [Test]
        public void FailedPreparationDoesNotMarkCompletion()
        {
            _set.Initialize("invalid", 128f, new List<XRayInstanceBatch> { null });
            Assert.Throws<System.IO.InvalidDataException>(() => XRayInstancePreparation.Ensure(_set));
            Assert.That(_set.MatrixPreparationVersion, Is.Zero);
        }

        private object Observe()
        {
            return HISTORY.Invoke(_renderer, new object[] { 0, _batch });
        }

        private static Array Payload(object history)
        {
            return (Array)history.GetType().GetProperty("Instances").GetValue(history);
        }

        private static Matrix4x4 Matrix(object history, int index, string name)
        {
            object item = Payload(history).GetValue(index);
            return (Matrix4x4)item.GetType().GetField(name).GetValue(item);
        }

        private static bool Moving(object history)
        {
            return (bool)history.GetType().GetProperty("Moving").GetValue(history);
        }

        private static void Advance(object history)
        {
            history.GetType().GetProperty("Frame").SetValue(history, Time.frameCount - 1);
        }
    }
}
