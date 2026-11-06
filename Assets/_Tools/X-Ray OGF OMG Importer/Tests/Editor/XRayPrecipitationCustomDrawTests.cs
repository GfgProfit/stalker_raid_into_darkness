using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayPrecipitationCustomDrawTests
    {
        private const float TOLERANCE = 0.0001f;

        [Test]
        public void CustomResolversChainShareClonesAndSurvivePruneWithoutMeshRenderers()
        {
            using var fixture = new Fixture();
            using var registry = new XRayWetMaterialBindings();
            int calls = 0;
            Func<Material, Material> previous = material =>
            {
                Assert.IsNotNull(material);
                Assert.IsTrue(material == fixture.Source || material == fixture.Skipped);
                calls++;
                if (material == fixture.Source) return fixture.Resolved;
                return material == fixture.Skipped ? null : material;
            };
            fixture.Instances.MaterialResolver = previous;
            fixture.Details.MaterialResolver = previous;
            Assert.AreEqual(0, fixture.Root.GetComponentsInChildren<MeshRenderer>(true).Length);
            registry.Bind(fixture.Scene, fixture.Configuration);
            Assert.AreEqual(1, registry.CloneCount);
            Assert.AreEqual(0, registry.BindingCount);
            Assert.AreEqual(2, registry.CustomDrawBindingCount);
            Material clone = fixture.Instances.MaterialResolver(fixture.Source);
            Assert.AreNotSame(fixture.Source, clone);
            Assert.AreNotSame(fixture.Resolved, clone);
            Assert.AreSame(clone, fixture.Details.MaterialResolver(fixture.Source));
            Assert.AreEqual(fixture.Resolved.enableInstancing, clone.enableInstancing);
            Assert.AreEqual(fixture.Configuration.GetWetResponse(XRayWetSurfaceType.Concrete).Response, clone.GetVector("_MonolithWetResponse"));
            Assert.IsTrue(clone.IsKeywordEnabled("_MONOLITH_PRECIPITATION"));
            Assert.IsFalse(fixture.Resolved.IsKeywordEnabled("_MONOLITH_PRECIPITATION"));
            Assert.IsNull(fixture.Instances.MaterialResolver(fixture.Skipped));
            Assert.IsNull(fixture.Details.MaterialResolver(fixture.Skipped));
            int beforeNull = calls;
            Assert.IsNull(fixture.Instances.MaterialResolver(null));
            Assert.IsNull(fixture.Details.MaterialResolver(null));
            Assert.AreEqual(beforeNull, calls);
            registry.Bind(fixture.Scene, fixture.Configuration);
            Assert.AreEqual(1, registry.CloneCount);
            Assert.AreEqual(2, registry.CustomDrawBindingCount);
            Assert.AreSame(clone, fixture.Instances.MaterialResolver(fixture.Source));
            Assert.AreSame(clone, fixture.Details.MaterialResolver(fixture.Source));
            Assert.AreSame(fixture.Source, fixture.Batch.Materials[1]);
            Assert.AreSame(fixture.Source, fixture.Database.Models[0].Material);
            registry.Dispose();
            Assert.AreSame(previous, fixture.Instances.MaterialResolver);
            Assert.AreSame(previous, fixture.Details.MaterialResolver);
            Assert.AreEqual(0, registry.CloneCount);
            Assert.AreEqual(0, registry.CustomDrawBindingCount);
            Assert.IsTrue(clone == null);
            Assert.DoesNotThrow(registry.Dispose);
            fixture.AssertReadOnly();
        }

        [Test]
        public void ExternalResolverReplacementIsPreservedWhileOwnedDetailResolverRestores()
        {
            using var fixture = new Fixture();
            using var registry = new XRayWetMaterialBindings();
            Func<Material, Material> previous = material => material == fixture.Source ? fixture.Resolved : material;
            Func<Material, Material> external = material => material;
            fixture.Instances.MaterialResolver = previous;
            fixture.Details.MaterialResolver = previous;
            registry.Bind(fixture.Scene, fixture.Configuration);
            fixture.Instances.MaterialResolver = external;
            registry.Dispose();
            Assert.AreSame(external, fixture.Instances.MaterialResolver);
            Assert.AreSame(previous, fixture.Details.MaterialResolver);
            Assert.AreEqual(0, registry.CloneCount);
            Assert.AreEqual(0, registry.CustomDrawBindingCount);
            Assert.DoesNotThrow(registry.Dispose);
            fixture.AssertReadOnly();
        }

        [Test]
        public void SparseMaterialsAndNullResolverResultsSkipInstancedAndBothDetailSubmissionPaths()
        {
            using var fixture = new Fixture();
            int calls = 0;
            Func<Material, Material> skip = material =>
            {
                Assert.AreSame(fixture.Source, material);
                calls++;
                return null;
            };
            fixture.Instances.MaterialResolver = skip;
            fixture.Details.MaterialResolver = skip;
            Type instanceData = typeof(XRayLevelInstancedRenderer).GetNestedType("InstanceData", BindingFlags.NonPublic);
            Assert.IsNotNull(instanceData);
            Array matrices = Array.CreateInstance(instanceData, 2);
            Invoke(fixture.Instances, "Submit", fixture.Camera, fixture.Batch, fixture.Batch.Bounds, default(XRayFloraWindState), new MaterialPropertyBlock(), matrices, fixture.Batch.Data, 2, ShadowCastingMode.Off, false);
            Assert.AreEqual(1, calls);
            Set(fixture.Details, "_counts", new[] { 1, 0, 0 });
            Assert.AreEqual(0, Invoke(fixture.Details, "Flush", fixture.Camera, 0));
            Assert.AreEqual(2, calls);
            Set(fixture.Details, "_counts", new[] { 1, 0, 0 });
            Assert.IsNull(Invoke(fixture.Details, "FlushSource", fixture.Camera, 0, false));
            Assert.AreEqual(3, calls);
            Set(fixture.Details, "_counts", new[] { 0, 0, 1 });
            Assert.AreEqual(0, Invoke(fixture.Details, "Flush", fixture.Camera, 2));
            Set(fixture.Details, "_counts", new[] { 0, 0, 1 });
            Assert.IsNull(Invoke(fixture.Details, "FlushSource", fixture.Camera, 2, false));
            Assert.AreEqual(3, calls);
            fixture.AssertReadOnly();
        }

        [Test]
        public void InstancedHeightAtlasUsesWorldMatricesAndTracksComponentRootAndDataChanges()
        {
            using var fixture = new Fixture();
            using var geometry = new XRayPrecipitationGeometry(fixture.Scene);
            fixture.Instances.enabled = true;
            fixture.Root.SetActive(true);
            geometry.Refresh(fixture.Configuration, 0.0f, true);
            CheckHeight(geometry, -2.0f, 3.5f, 2.5f);
            CheckHeight(geometry, 2.0f, 6.5f, 5.5f);
            Assert.AreEqual(1, geometry.InstancedGeometryCount);
            Assert.AreEqual(2, geometry.InstancedInstanceCount);
            int captures = geometry.CaptureCount;
            geometry.Refresh(fixture.Configuration, 0.2f);
            Assert.AreEqual(captures, geometry.CaptureCount);
            fixture.Instances.enabled = false;
            geometry.Refresh(fixture.Configuration, 0.2f);
            CheckEmpty(geometry, -2.0f);
            Assert.Greater(geometry.CaptureCount, captures);
            Assert.AreEqual(0, geometry.GeometryCount);
            fixture.Instances.enabled = true;
            geometry.Refresh(fixture.Configuration, 0.2f);
            CheckHeight(geometry, -2.0f, 3.5f, 2.5f);
            fixture.Root.SetActive(false);
            geometry.Refresh(fixture.Configuration, 0.2f);
            CheckEmpty(geometry, 2.0f);
            Assert.AreEqual(0, geometry.GeometryCount);
            fixture.Root.SetActive(true);
            geometry.Refresh(fixture.Configuration, 0.2f);
            CheckHeight(geometry, 2.0f, 6.5f, 5.5f);
            captures = geometry.CaptureCount;
            fixture.Batch.Matrices[0] = Matrix4x4.TRS(new Vector3(-2.0f, 4.0f, 0.0f), Quaternion.identity, Vector3.one);
            geometry.Refresh(fixture.Configuration, 0.2f);
            CheckHeight(geometry, -2.0f, 4.5f, 3.5f);
            Assert.Greater(geometry.CaptureCount, captures);
            fixture.Root.transform.position += new Vector3(10.0f, 20.0f, 30.0f);
            geometry.Refresh(fixture.Configuration, 0.2f);
            CheckHeight(geometry, -2.0f, 4.5f, 3.5f);
            fixture.Batch.Matrices[0] = fixture.FirstMatrix;
            fixture.AssertReadOnly();
        }

        [Test]
        public void InstancedHeightAtlasPreservesSourceAlphaClipWithoutMaterialAssetChanges()
        {
            using var fixture = new Fixture();
            using var geometry = new XRayPrecipitationGeometry(fixture.Scene);
            var transparent = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var source = new Material(fixture.Source);
            try
            {
                transparent.SetPixel(0, 0, Color.clear);
                transparent.Apply(false, false);
                source.SetTexture("_BaseColorMap", transparent);
                source.EnableKeyword("_ALPHATEST_ON");
                source.SetFloat("_AlphaCutoff", 0.5f);
                source.SetFloat("_AlphaCutoffEnable", 1.0f);
                var first = new XRayInstanceBatch("cutout", fixture.Mesh, new Material[] { null, source }, fixture.Batch.Bounds, new[] { fixture.FirstMatrix }, new[] { Vector4.one }, true);
                var second = new XRayInstanceBatch("solid", fixture.Mesh, new Material[] { null, fixture.Source }, fixture.Batch.Bounds, new[] { fixture.Batch.Matrices[1] }, new[] { Vector4.one }, true);
                fixture.BatchSet.Initialize("owned_cutout", 128.0f, new List<XRayInstanceBatch> { first, second });
                string batchBefore = JsonUtility.ToJson(fixture.BatchSet);
                string sourceBefore = EditorJsonUtility.ToJson(source);
                fixture.Instances.enabled = true;
                fixture.Root.SetActive(true);
                geometry.Refresh(fixture.Configuration, 0.0f, true);
                CheckEmpty(geometry, -2.0f);
                CheckHeight(geometry, 2.0f, 6.5f, 5.5f);
                Assert.AreEqual(batchBefore, JsonUtility.ToJson(fixture.BatchSet));
                Assert.AreEqual(sourceBefore, EditorJsonUtility.ToJson(source));
                fixture.AssertSharedReadOnly();
            }
            finally
            {
                geometry.Dispose();
                fixture.Instances.enabled = false;
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(transparent);
            }
        }

        [Test]
        public void NativeReflectionCameraIncludesInstancedGeometryWithoutAdvancingMainViewHistoryOrCounters()
        {
            using var fixture = new Fixture();
            fixture.Root.SetActive(true);
            fixture.Instances.enabled = true;
            fixture.Camera.transform.SetPositionAndRotation(new Vector3(0.0f, 4.5f, -10.0f), Quaternion.identity);
            fixture.Camera.nearClipPlane = 0.1f;
            fixture.Camera.farClipPlane = 30.0f;
            Invoke(fixture.Instances, "OnBeginCameraRendering", default(ScriptableRenderContext), fixture.Camera);
            int visible = fixture.Instances.LastVisibleInstances;
            int batches = fixture.Instances.LastVisibleBatches;
            Assert.AreEqual(2, visible);
            var histories = (System.Collections.IList)typeof(XRayLevelInstancedRenderer).GetField("_histories", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture.Instances);
            object history = histories[0];
            int historyFrame = (int)history.GetType().GetProperty("Frame").GetValue(history);
            fixture.Camera.cameraType = CameraType.Reflection;
            Invoke(fixture.Instances, "OnBeginCameraRendering", default(ScriptableRenderContext), fixture.Camera);
            Assert.AreEqual(1L, fixture.Instances.ReflectionSubmittedBatches);
            Assert.AreEqual(2L, fixture.Instances.ReflectionSubmittedInstances);
            Assert.AreEqual(visible, fixture.Instances.LastVisibleInstances);
            Assert.AreEqual(batches, fixture.Instances.LastVisibleBatches);
            Assert.AreSame(history, histories[0]);
            Assert.AreEqual(historyFrame, history.GetType().GetProperty("Frame").GetValue(history));
            fixture.Camera.farClipPlane = 5.0f;
            Invoke(fixture.Instances, "OnBeginCameraRendering", default(ScriptableRenderContext), fixture.Camera);
            Assert.AreEqual(1L, fixture.Instances.ReflectionSubmittedBatches);
            Assert.AreEqual(2L, fixture.Instances.ReflectionSubmittedInstances);
            fixture.AssertReadOnly();
        }

        private static object Invoke(object target, string method, params object[] arguments)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, method);
            return info.Invoke(target, arguments);
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }

        private static void CheckHeight(XRayPrecipitationGeometry geometry, float worldX, float top, float bottom)
        {
            Color value = ReadHeight(geometry, worldX);
            Assert.That(value.r, Is.EqualTo(top).Within(TOLERANCE));
            Assert.That(value.g, Is.EqualTo(bottom).Within(TOLERANCE));
        }

        private static void CheckEmpty(XRayPrecipitationGeometry geometry, float worldX)
        {
            Color value = ReadHeight(geometry, worldX);
            Assert.Less(value.r, value.g);
        }

        private static Color ReadHeight(XRayPrecipitationGeometry geometry, float worldX)
        {
            RenderTexture target = geometry.Heights;
            Vector4 mapping = geometry.WorldMapping;
            int x = Mathf.Clamp(Mathf.FloorToInt((worldX - mapping.x) * mapping.z * target.width), 0, target.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(-mapping.y * mapping.w * target.height), 0, target.height - 1);
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
                readback.Apply(false, false);
                return readback.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(readback);
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string _sharedBefore;
            private readonly string _configurationBefore;
            private readonly string _batchBefore;
            private readonly string _databaseBefore;
            private readonly string _sourceBefore;
            private readonly string _resolvedBefore;
            private readonly string _skippedBefore;

            public Scene Scene { get; }
            public GameObject Root { get; }
            public Camera Camera { get; }
            public Mesh Mesh { get; }
            public Material Source { get; }
            public Material Resolved { get; }
            public Material Skipped { get; }
            public PrecipitationConfig Configuration { get; }
            public XRayInstanceBatchSet BatchSet { get; }
            public XRayInstanceBatch Batch { get; }
            public XRayDetailDatabase Database { get; }
            public XRayLevelInstancedRenderer Instances { get; }
            public XRayDetailVegetation Details { get; }
            public Matrix4x4 FirstMatrix { get; }

            public Fixture()
            {
                try
                {
                    _sharedBefore = SharedJson();
                    Configuration = Object.Instantiate(GameConfig.Load().Precipitation);
                    var serialized = new SerializedObject(Configuration);
                    serialized.FindProperty("_quality").enumValueIndex = 0;
                    serialized.FindProperty("_geometryRefreshSeconds").floatValue = 0.1f;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    Scene = EditorSceneManager.NewPreviewScene();
                    Root = new GameObject("Owned custom precipitation fixture");
                    SceneManager.MoveGameObjectToScene(Root, Scene);
                    Root.SetActive(false);
                    Root.transform.SetPositionAndRotation(new Vector3(100.0f, 50.0f, -100.0f), Quaternion.Euler(0.0f, 90.0f, 0.0f));
                    Root.transform.localScale = Vector3.one * 2.0f;
                    Source = new Material(Shader.Find("HDRP/MonolithLit")) { name = "asphalt custom regression", enableInstancing = true };
                    Resolved = new Material(Source) { name = "concrete previous resolver", enableInstancing = true };
                    Skipped = new Material(Source) { name = "wood skipped resolver", enableInstancing = true };
                    Mesh = Cube();
                    FirstMatrix = Matrix4x4.TRS(new Vector3(-2.0f, 3.0f, 0.0f), Quaternion.identity, Vector3.one);
                    Batch = new XRayInstanceBatch("owned world cubes", Mesh, new Material[] { null, Source, Skipped }, new Bounds(new Vector3(0.0f, 4.5f, 0.0f), new Vector3(5.0f, 4.0f, 1.0f)), new[] { FirstMatrix, Matrix4x4.TRS(new Vector3(2.0f, 6.0f, 0.0f), Quaternion.identity, Vector3.one) }, new[] { Vector4.one, Vector4.one }, true);
                    BatchSet = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
                    BatchSet.Initialize("owned_custom", 128.0f, new List<XRayInstanceBatch> { Batch });
                    Database = ScriptableObject.CreateInstance<XRayDetailDatabase>();
                    var models = new List<XRayDetailModelEntry> { Model(Source), Model(Skipped), Model(null) };
                    Database.Initialize("owned_custom", 32.0f, 1.0f, 1.0f, 2.0f, models, new List<XRayDetailChunk>(), null, null, default, default, 0, 0);
                    Instances = Root.AddComponent<XRayLevelInstancedRenderer>();
                    Instances.enabled = false;
                    Instances.BatchSet = BatchSet;
                    Details = Root.AddComponent<XRayDetailVegetation>();
                    Details.enabled = false;
                    Details.Database = Database;
                    Camera = Root.AddComponent<Camera>();
                    Camera.enabled = false;
                    _configurationBefore = JsonUtility.ToJson(Configuration);
                    _batchBefore = JsonUtility.ToJson(BatchSet);
                    _databaseBefore = JsonUtility.ToJson(Database);
                    _sourceBefore = EditorJsonUtility.ToJson(Source);
                    _resolvedBefore = EditorJsonUtility.ToJson(Resolved);
                    _skippedBefore = EditorJsonUtility.ToJson(Skipped);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void AssertReadOnly()
            {
                AssertSharedReadOnly();
                Assert.AreEqual(_batchBefore, JsonUtility.ToJson(BatchSet));
                Assert.AreEqual(_databaseBefore, JsonUtility.ToJson(Database));
            }

            public void AssertSharedReadOnly()
            {
                Assert.AreEqual(_sharedBefore, SharedJson());
                Assert.AreEqual(_configurationBefore, JsonUtility.ToJson(Configuration));
                Assert.AreEqual(_sourceBefore, EditorJsonUtility.ToJson(Source));
                Assert.AreEqual(_resolvedBefore, EditorJsonUtility.ToJson(Resolved));
                Assert.AreEqual(_skippedBefore, EditorJsonUtility.ToJson(Skipped));
            }

            public void Dispose()
            {
                if (Instances != null) Instances.enabled = false;
                if (Details != null) Details.enabled = false;
                if (Scene.IsValid() && Scene.isLoaded) EditorSceneManager.ClosePreviewScene(Scene);
                Object.DestroyImmediate(BatchSet);
                Object.DestroyImmediate(Database);
                Object.DestroyImmediate(Mesh);
                Object.DestroyImmediate(Source);
                Object.DestroyImmediate(Resolved);
                Object.DestroyImmediate(Skipped);
                Object.DestroyImmediate(Configuration);
            }

            private XRayDetailModelEntry Model(Material material)
            {
                return new XRayDetailModelEntry("details\\blend", "owned", Mesh, material, 1.0f, true, 1.0f, 1.0f, 0);
            }

            private static Mesh Cube()
            {
                var mesh = new Mesh { name = "Owned custom height cube" };
                mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f) };
                mesh.uv = new Vector2[8];
                int[] triangles = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
                mesh.subMeshCount = 2;
                mesh.SetTriangles(triangles, 0);
                mesh.SetTriangles(triangles, 1);
                mesh.RecalculateBounds();
                return mesh;
            }

            private static string SharedJson()
            {
                GameConfig config = GameConfig.Load();
                return JsonUtility.ToJson(config) + JsonUtility.ToJson(config.Precipitation);
            }
        }
    }
}
