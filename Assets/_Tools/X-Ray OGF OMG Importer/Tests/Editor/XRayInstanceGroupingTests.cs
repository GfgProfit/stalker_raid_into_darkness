using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayInstanceGroupingTests
    {
        private static readonly MethodInfo DRAW = typeof(XRayLevelInstancedRenderer).GetMethod("OnBeginCameraRendering", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private Mesh _mesh;
        private Material _material;
        private XRayInstanceBatchSet _set;
        private XRayLevelInstancedRenderer _renderer;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            _mesh = Own(new Mesh { name = "fixture", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } });
            _material = Own(new Material(Shader.Find("HDRP/Unlit")) { enableInstancing = true });
            _set = Own(ScriptableObject.CreateInstance<XRayInstanceBatchSet>());
            _renderer = Own(new GameObject("Grouping renderer")).AddComponent<XRayLevelInstancedRenderer>();
            _camera = Own(new GameObject("Grouping camera")).AddComponent<Camera>();
            _camera.enabled = false;
            _camera.farClipPlane = 2000f;
            _renderer.BatchSet = _set;
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = _owned.Count - 1; index >= 0; index--) UnityEngine.Object.DestroyImmediate(_owned[index]);
            _owned.Clear();
        }

        [Test]
        public void GroupingReducesActualSubmissionsAndPreservesSourceIdentityAndPayload()
        {
            var first = Batch(1f, 2, new Vector4(-0.0f, 0.25f, 3f, 4f));
            var second = Batch(129f, 3, new Vector4(9f, -2f, 7f, 8f));
            Prepare(first, second);
            Assert.That(_set.SubmissionGroups.Count, Is.EqualTo(1));
            Assert.That(_set.Batches[0], Is.SameAs(first));
            Assert.That(_set.Batches[1], Is.SameAs(second));
            _camera.orthographic = true;
            _camera.orthographicSize = 200f;
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(1));
            Assert.That(_renderer.LastSubmittedInstances, Is.EqualTo(5));
            object group = ((Array)Field(_renderer, "_groups")).GetValue(0);
            var data = (Vector4[])Property(group, "Data");
            var instances = (Array)Property(group, "Instances");
            int offset = 0;
            foreach (var batch in _set.Batches)
            {
                for (int index = 0; index < batch.Count; index++)
                {
                    for (int component = 0; component < 4; component++) Assert.That(BitConverter.SingleToInt32Bits(data[offset][component]), Is.EqualTo(BitConverter.SingleToInt32Bits(batch.Data[index][component])));
                    Assert.That((Matrix4x4)instances.GetValue(offset).GetType().GetField("objectToWorld").GetValue(instances.GetValue(offset)), Is.EqualTo(batch.GetMatrix(index)));
                    offset++;
                }
            }
            var legacy = Own(ScriptableObject.CreateInstance<XRayInstanceBatchSet>());
            legacy.Initialize("legacy", 128f, new List<XRayInstanceBatch> { first, second });
            legacy.PrepareMatrices("legacy");
            _renderer.BatchSet = legacy;
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(2));
            Assert.That(_renderer.LastSubmittedInstances, Is.EqualTo(5));
        }

        [Test]
        public void EqualLookingMeshesAndDifferentMaterialOrderRemainSeparate()
        {
            var first = Batch(1f);
            Mesh otherMesh = Own(UnityEngine.Object.Instantiate(_mesh));
            var second = new XRayInstanceBatch(first.Name, otherMesh, first.Materials, first.Bounds, new[] { first.GetMatrix(0) }, first.Data, true);
            Prepare(first, second);
            Assert.That(_set.SubmissionGroups, Is.Empty);
            var secondMaterial = Own(new Material(_material));
            first.Materials[0] = secondMaterial;
            Prepare(first, Batch(2f));
            Assert.That(_set.SubmissionGroups, Is.Empty);
        }

        [Test]
        public void SubmeshMaterialOrderAndLodIndicesArePreserved()
        {
            _mesh.subMeshCount = 2;
            _mesh.SetTriangles(new[] { 0, 1, 2 }, 1);
            Material other = Own(new Material(_material));
            var first = new XRayInstanceBatch("first", _mesh, new[] { _material, other }, new Bounds(new Vector3(1f, 0f, 20f), Vector3.one), new[] { Matrix4x4.Translate(new Vector3(1f, 0f, 20f)) }, new[] { Vector4.one }, true, new[] { 17 });
            var second = new XRayInstanceBatch("second", _mesh, new[] { _material, other }, new Bounds(new Vector3(2f, 0f, 20f), Vector3.one), new[] { Matrix4x4.Translate(new Vector3(2f, 0f, 20f)) }, new[] { Vector4.zero }, true, new[] { 31 });
            Prepare(first, second);
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(2));
            Assert.That(first.LodIndices, Is.EqualTo(new[] { 17 }));
            Assert.That(second.LodIndices, Is.EqualTo(new[] { 31 }));
            second.Materials[0] = other;
            second.Materials[1] = _material;
            Assert.That(XRayInstancePreparation.Ensure(_set), Is.True);
            Assert.That(_set.SubmissionGroups, Is.Empty);
        }

        [TestCase(255f, 256f, 0)]
        [TestCase(-129f, -1f, 1)]
        [TestCase(-1f, 1f, 0)]
        [TestCase(1f, 1025f, 0)]
        public void SpatialCellsUseFloorAndKeepDistantRegionsSeparate(float first, float second, int expected)
        {
            Prepare(Batch(first), Batch(second));
            Assert.That(_set.SubmissionGroups.Count, Is.EqualTo(expected));
        }

        [TestCase(255, 256, 1)]
        [TestCase(256, 256, 0)]
        [TestCase(511, 1, 0)]
        [TestCase(512, 1, 0)]
        public void WholeBatchesNeverIncreaseSliceCount(int first, int second, int groups)
        {
            Prepare(Batch(1f, first), Batch(2f, second));
            Assert.That(_set.SubmissionGroups.Count, Is.EqualTo(groups));
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(groups == 1 ? 1 : (first + 510) / 511 + (second + 510) / 511));
            Assert.That(_renderer.LastSubmittedInstances, Is.EqualTo(first + second));
        }

        [Test]
        public void TransparentAndOversizedBoundsKeepTheirOriginalPath()
        {
            _material.renderQueue = 3000;
            Prepare(Batch(1f), Batch(2f));
            Assert.That(_set.SubmissionGroups, Is.Empty);
            _material.renderQueue = 2450;
            Prepare(Batch(1f), Batch(2f));
            Assert.That(_set.SubmissionGroups.Count, Is.EqualTo(1));
            var large = new XRayInstanceBatch("large", _mesh, new[] { _material }, new Bounds(Vector3.zero, Vector3.one * 1000f), new[] { Matrix4x4.identity }, new[] { Vector4.one }, true);
            Prepare(large, Batch(1f));
            Assert.That(_set.SubmissionGroups, Is.Empty);
        }

        [Test]
        public void EmptyAndMalformedPayloadRemainSafeAndPreparationIsIdempotent()
        {
            Prepare();
            Assert.That(_set.SubmissionGroups, Is.Empty);
            Assert.That(XRayInstancePreparation.Ensure(_set), Is.False);
            var invalid = new XRayInstanceBatch("invalid", _mesh, new[] { _material }, new Bounds(Vector3.zero, Vector3.one), new[] { Matrix4x4.identity }, Array.Empty<Vector4>(), true);
            Prepare(invalid, Batch(1f));
            Assert.That(_set.SubmissionGroups, Is.Empty);
            Prepare(Batch(1f), Batch(2f));
            string serialized = JsonUtility.ToJson(_set);
            Assert.That(XRayInstancePreparation.Ensure(_set), Is.False);
            Assert.That(JsonUtility.ToJson(_set), Is.EqualTo(serialized));
        }

        [Test]
        public void MutableReferenceAndSharedRepreparationRefreshBothHistoriesAndGroupPayload()
        {
            XRayInstanceBatch first = Batch(1f);
            Prepare(first, Batch(2f));
            Draw();
            Matrix4x4 original = first.GetMatrix(0);
            Matrix4x4[] retained = first.Matrices;
            retained[0] = Matrix4x4.Translate(new Vector3(3f, 0f, 20f));
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(2));
            var other = Own(ScriptableObject.CreateInstance<XRayInstanceBatchSet>());
            other.Initialize("shared", 128f, new List<XRayInstanceBatch> { first });
            other.PrepareMatrices("shared");
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(1));
            object group = ((Array)Field(_renderer, "_groups")).GetValue(0);
            object instance = ((Array)Property(group, "Instances")).GetValue(0);
            Assert.That((Matrix4x4)instance.GetType().GetField("objectToWorld").GetValue(instance), Is.EqualTo(first.GetMatrix(0)));
            Assert.That((Matrix4x4)instance.GetType().GetField("prevObjectToWorld").GetValue(instance), Is.EqualTo(original));
            var camera = Own(new GameObject("Second camera")).AddComponent<Camera>();
            camera.enabled = false;
            DRAW.Invoke(_renderer, new object[] { default(ScriptableRenderContext), camera });
            Assert.That(_renderer.LastSubmittedInstances, Is.EqualTo(2));
            foreach (object history in (IList)Field(_renderer, "_histories")) history.GetType().GetProperty("Frame").SetValue(history, Time.frameCount - 1);
            Draw();
            group = ((Array)Field(_renderer, "_groups")).GetValue(0);
            instance = ((Array)Property(group, "Instances")).GetValue(0);
            Assert.That((Matrix4x4)instance.GetType().GetField("prevObjectToWorld").GetValue(instance), Is.EqualTo(first.GetMatrix(0)));
            Assert.That(Property(group, "Moving"), Is.False);
        }

        [Test]
        public void ResolverResultsAreCheckedPerSourceAndFallbackAvoidsDoubleResolution()
        {
            Prepare(Batch(1f), Batch(2f));
            Material other = Own(new Material(_material));
            int calls = 0;
            _renderer.MaterialResolver = material => ++calls == 1 ? material : other;
            Draw();
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(2));
            _renderer.MaterialResolver = material => other;
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(1));
            _renderer.LodImpostors = true;
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(2));
        }

        [Test]
        public void MirroredAndNonuniformTransformsKeepTheirPayloadAndOrientationBoundary()
        {
            Matrix4x4 mirrored = Matrix4x4.TRS(new Vector3(1f, 0f, 20f), Quaternion.Euler(0f, 35f, 0f), new Vector3(-1f, 2f, 3f));
            var negative = new XRayInstanceBatch("mirrored", _mesh, new[] { _material }, new Bounds(new Vector3(1f, 0f, 20f), Vector3.one * 10f), new[] { mirrored }, new[] { Vector4.one }, true);
            Prepare(negative, Batch(2f));
            Assert.That(_set.SubmissionGroups, Is.Empty);
            var second = new XRayInstanceBatch("mirrored second", _mesh, new[] { _material }, new Bounds(new Vector3(1f, 0f, 20f), Vector3.one * 10f), new[] { mirrored }, new[] { Vector4.zero }, true);
            Prepare(negative, second);
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(1));
            Assert.That(negative.GetMatrix(0), Is.EqualTo(mirrored));
            second.Matrices[0] = Matrix4x4.identity;
            _set.PrepareMatrices("orientation changed");
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(2));
        }

        [Test]
        public void MixedVisibilityKeepsOriginalColorWorkAndFalsePositiveUnionStaysShadowOnly()
        {
            Prepare(Batch(1f), Batch(129f));
            Draw();
            Assert.That(_renderer.LastVisibleInstances, Is.EqualTo(1));
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(2));
            _camera.transform.position = new Vector3(65f, 0f, 0f);
            Draw();
            Assert.That(_renderer.LastVisibleInstances, Is.Zero);
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(1));
            Assert.That(_renderer.LastShadowOnlyBatches, Is.EqualTo(1));
        }

        [Test]
        public void PayloadChangesInvalidateThePlanAndRefreshPropertyBlocks()
        {
            var first = Batch(1f);
            Prepare(first, Batch(2f));
            Draw();
            string signature = _set.GroupingSignature;
            first.Data[0] = new Vector4(7f, 9f, 11f, 13f);
            Assert.That(XRayInstancePreparation.Ensure(_set), Is.True);
            Assert.That(_set.GroupingSignature, Is.Not.EqualTo(signature));
            Draw();
            object group = ((Array)Field(_renderer, "_groups")).GetValue(0);
            Assert.That(((Vector4[])Property(group, "Data"))[0], Is.EqualTo(first.Data[0]));
        }

        [TestCase(XRayFloraWindProfile.AnomalySsfx)]
        [TestCase(XRayFloraWindProfile.TrueStalkerXRay)]
        public void MergedWindBoundsContainEveryExpandedSourceWithoutUsingTerrainHeight(XRayFloraWindProfile profile)
        {
            var state = new XRayFloraWindState { Profile = profile };
            state.Reset(new XRayFloraWindWeather(1f, 100f, 1f));
            Bounds first = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(5f, 7f, 9f));
            Bounds second = new Bounds(new Vector3(100f, 80f, 0f), new Vector3(8f, 11f, 10f));
            Bounds merged = first;
            merged.Encapsulate(second);
            merged = XRayFloraWindBounds.Expand(merged, state, Mathf.Max(first.size.y, second.size.y));
            foreach (Bounds source in new[] { first, second })
            {
                Bounds expanded = XRayFloraWindBounds.Expand(source, state);
                Assert.That(merged.Contains(expanded.min), Is.True);
                Assert.That(merged.Contains(expanded.max), Is.True);
            }
        }

        [Test]
        public void DistanceAndReflectionKeepSourceGranularityAndReflectionDoesNotAdvanceHistory()
        {
            Prepare(Batch(1f), Batch(2f));
            Draw();
            IList histories = (IList)Field(_renderer, "_histories");
            object history = histories[0];
            history.GetType().GetProperty("Frame").SetValue(history, -71);
            typeof(XRayLevelInstancedRenderer).GetMethod("RenderReflection", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_renderer, new object[] { _camera });
            Assert.That(Property(history, "Frame"), Is.EqualTo(-71));
            Assert.That(_renderer.ReflectionSubmittedBatches, Is.EqualTo(2));
            typeof(XRayLevelInstancedRenderer).GetField("_maxDistance", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_renderer, 100f);
            Draw();
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(2));
        }

        private void Prepare(params XRayInstanceBatch[] batches)
        {
            _set.Initialize("fixture", 128f, new List<XRayInstanceBatch>(batches));
            XRayInstancePreparation.Ensure(_set);
        }

        private XRayInstanceBatch Batch(float x, int count = 1, Vector4? payload = null)
        {
            var matrices = new Matrix4x4[count];
            var data = new Vector4[count];
            for (int index = 0; index < count; index++)
            {
                matrices[index] = Matrix4x4.Translate(new Vector3(x, 0f, 20f));
                data[index] = payload ?? Vector4.one;
            }
            return new XRayInstanceBatch("source", _mesh, new[] { _material }, new Bounds(new Vector3(x, 0f, 20f), Vector3.one), matrices, data, true);
        }

        private void Draw()
        {
            DRAW.Invoke(_renderer, new object[] { default(ScriptableRenderContext), _camera });
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            _owned.Add(value);
            return value;
        }

        private static object Field(object value, string name)
        {
            return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        }

        private static object Property(object value, string name)
        {
            return value.GetType().GetProperty(name).GetValue(value);
        }
    }
}
