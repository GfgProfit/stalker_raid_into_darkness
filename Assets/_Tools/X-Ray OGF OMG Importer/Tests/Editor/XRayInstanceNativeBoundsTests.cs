using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayInstanceNativeBoundsTests
    {
        private const string ACTUAL_LEVEL_ROOT = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d";

        [TestCase("l07_military")]
        [TestCase("l01_escape")]
        public void ActualImportedLocationsPrepareNativeWithoutChangingSourceAssets(string level)
        {
            if (!SystemInfo.supportsInstancing || BatchRendererGroup.BufferTarget != BatchBufferTarget.RawBuffer) Assert.Ignore("Native instance buffers require a supported graphics device.");
            string assetPath = ACTUAL_LEVEL_ROOT + "/" + level + "/Meshes/" + level + "_StaticProps.asset";
            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            byte[] assetHash = HashFile(absolutePath);
            byte[] metaHash = HashFile(absolutePath + ".meta");
            var source = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(assetPath);
            Assert.That(source, Is.Not.Null, assetPath);
            Assert.That(source.MatrixPreparationVersion, Is.EqualTo(XRayInstanceBatchSet.MATRIX_PREPARATION_VERSION));
            Assert.That(source.MatrixInputSignature, Is.Not.Null.And.Not.Empty);
            int batches = source.Batches.Count;
            int instances = source.InstanceCount;
            Assert.That(batches, Is.GreaterThan(0));
            Assert.That(instances, Is.GreaterThan(0));
            var requiredWindMaterials = new HashSet<Material>();
            var requiredWindProfiles = new List<float>();
            string firstRequiredWindMaterial = string.Empty;
            foreach (XRayInstanceBatch batch in source.Batches)
            {
                foreach (Material material in batch.Materials)
                {
                    if (material == null || !material.HasProperty(XRayFloraWindBounds.MATERIAL_PROPERTY)) continue;
                    float profile = material.GetFloat(XRayFloraWindBounds.MATERIAL_PROPERTY);
                    if (profile <= 0.5f || !requiredWindMaterials.Add(material)) continue;
                    if (string.IsNullOrEmpty(firstRequiredWindMaterial)) firstRequiredWindMaterial = AssetDatabase.GetAssetPath(material);
                    if (!requiredWindProfiles.Contains(profile)) requiredWindProfiles.Add(profile);
                }
            }
            requiredWindProfiles.Sort();
            bool dirty = EditorUtility.IsDirty(source);
            var defaultsAsset = Resources.Load<TextAsset>("XRayRvmGrassDefaults");
            Assert.That(defaultsAsset, Is.Not.Null);
            string defaultsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", AssetDatabase.GetAssetPath(defaultsAsset)));
            byte[] defaultsHash = HashFile(defaultsPath);
            byte[] defaultsMetaHash = HashFile(defaultsPath + ".meta");
            XRayGrassDefaults defaults = XRayGrassDefaults.Load(defaultsAsset.text);
            bool[] sourceExposure = MatrixExposureFlags(source);
            var snapshot = Object.Instantiate(source);
            snapshot.hideFlags = HideFlags.HideAndDontSave;
            var cameraObject = new GameObject("Actual native location preparation camera", typeof(Camera)) { hideFlags = HideFlags.HideAndDontSave };
            var native = new XRayInstanceNativeRenderer();
            try
            {
                Assert.That(MatrixExposureFlags(snapshot), Is.EqualTo(new bool[batches]), "The serialized snapshot must match fresh Player matrix ownership.");
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                var wind = new XRayFloraWindState { Profile = XRayFloraWindProfile.AnomalySsfx, TreesSetup = defaults.WindTrees };
                wind.Reset(new XRayFloraWindWeather(0f, 1000f, 1f));
                foreach (XRayFloraWindState state in new XRayFloraWindState[] { null, wind })
                {
                    bool prepared = native.Prepare(snapshot, camera, 0, true, state);
                    string reason = native.FallbackReason ?? string.Empty;
                    TestContext.WriteLine("Actual native preparation: level=" + level + ";source=" + assetPath + ";batches=" + batches + ";instances=" + instances + ";matrixSignature=" + source.MatrixInputSignature + ";requiredWindMaterials=" + requiredWindMaterials.Count + ";requiredWindProfiles=" + string.Join(",", requiredWindProfiles) + ";firstRequiredWindMaterial=" + firstRequiredWindMaterial + ";wind=" + (state == null ? "None" : "AnomalySsfx") + ";prepared=" + prepared + ";memoryBytes=" + native.MemoryBytes + ";fallback=" + reason);
                    if (state == null && requiredWindMaterials.Count > 0)
                    {
                        Assert.That(prepared, Is.False, "Authored wind materials require their initialized source wind context.");
                        Assert.That(reason, Does.Contain("material does not support"));
                        Assert.That(native.Active, Is.False);
                        continue;
                    }
                    Assert.That(prepared, Is.True, reason);
                    Assert.That(native.Active, Is.True);
                    Assert.That(native.MemoryBytes, Is.GreaterThan(0));
                    Assert.That(typeof(XRayInstanceNativeRenderer).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(native), Is.Not.Null);
                    Assert.That(typeof(XRayInstanceNativeRenderer).GetField("_group", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(native), Is.Not.Null);
                    Assert.That(PhysicalBounds(native), Has.Length.EqualTo(batches));
                    Assert.That(SourceBounds(native), Has.Length.EqualTo(batches));
                    Assert.That(snapshot.Batches.Count, Is.EqualTo(batches));
                    Assert.That(snapshot.InstanceCount, Is.EqualTo(instances));
                    if (state != null) AssertActualConfigurationInheritance(snapshot, camera, state);
                }
            }
            finally
            {
                native.Dispose();
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(snapshot);
                Assert.That(HashFile(absolutePath), Is.EqualTo(assetHash), "The imported batch asset changed during read-only native preparation.");
                Assert.That(HashFile(absolutePath + ".meta"), Is.EqualTo(metaHash), "The imported batch GUID metadata changed.");
                Assert.That(HashFile(defaultsPath), Is.EqualTo(defaultsHash));
                Assert.That(HashFile(defaultsPath + ".meta"), Is.EqualTo(defaultsMetaHash));
                Assert.That(EditorUtility.IsDirty(source), Is.EqualTo(dirty));
                Assert.That(MatrixExposureFlags(source), Is.EqualTo(sourceExposure), "Read-only native preparation must preserve every original matrix exposure flag.");
                Assert.That(source.Batches.Count, Is.EqualTo(batches));
                Assert.That(source.InstanceCount, Is.EqualTo(instances));
            }
        }

        [TestCase(32f, 0.000003814697265625f)]
        [TestCase(1048576f, 0.125f)]
        public void ThinPhysicalBoundsAtLargeCentersRemainRepresentable(float coordinate, float width)
        {
            MethodInfo enclose = typeof(XRayInstanceNativeRenderer).GetMethod("TryEnclosePhysicalBounds", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(enclose, Is.Not.Null);
            Vector3 minimum = new Vector3(coordinate, 0f, 0f);
            Vector3 maximum = new Vector3(coordinate + width, 0.000001f, 0.000001f);
            object[] arguments = { new Bounds(minimum, Vector3.zero), minimum, maximum };
            Assert.That((bool)enclose.Invoke(null, arguments), Is.True);
            var bounds = (Bounds)arguments[0];
            Assert.That(bounds.Contains(minimum), Is.True);
            Assert.That(bounds.Contains(maximum), Is.True);
            AssertContains(bounds, minimum);
            AssertContains(bounds, maximum);
        }

        [Test]
        public void NativeBoundsCoverAuthoredAndTransformedMeshAfterAnInPlaceMeshBoundsChange()
        {
            if (!SystemInfo.supportsInstancing || BatchRendererGroup.BufferTarget != BatchBufferTarget.RawBuffer) Assert.Ignore("Native instance buffers require a supported graphics device.");
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.That(shader, Is.Not.Null);
            var mesh = new Mesh();
            var material = new Material(shader);
            var cameraObject = new GameObject("Native conservative bounds camera", typeof(Camera));
            var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            var native = new XRayInstanceNativeRenderer();
            try
            {
                material.enableInstancing = true;
                material.renderQueue = 2000;
                material.SetFloat("_SurfaceType", 0f);
                material.SetFloat("_MonolithFloraWind", 0f);
                material.SetFloat("_MonolithLevelDetail", 0f);
                material.DisableKeyword("_MONOLITH_LEVEL_DETAIL");
                material.DisableKeyword("_MONOLITH_SKINNED_TBN");
                mesh.vertices = new[] { Vector3.one * -1f, Vector3.one, new Vector3(1f, -1f, 0f) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(50f, 2f, 10f), Quaternion.Euler(0f, 45f, 0f), new Vector3(2f, 3f, 4f));
                Bounds authored = new Bounds(new Vector3(50f, 2f, 10f), Vector3.one);
                var batch = new XRayInstanceBatch("native bounds", mesh, new[] { material }, authored, new[] { matrix }, new[] { Vector4.one }, true);
                set.Initialize("native bounds", 1f, new List<XRayInstanceBatch> { batch });
                set.PrepareMatrices("native bounds");
                Assert.That(native.Prepare(set, camera, 0, true, null), Is.True, native.FallbackReason);
                Bounds[] firstBounds = SourceBounds(native);
                Bounds[] firstPhysical = PhysicalBounds(native);
                AssertContains(firstBounds[0], authored.min);
                AssertContains(firstBounds[0], authored.max);
                AssertMeshCorners(firstBounds[0], mesh.bounds, matrix);
                AssertContains(firstPhysical[0], authored.min);
                AssertContains(firstPhysical[0], authored.max);
                AssertMeshCorners(firstPhysical[0], mesh.bounds, matrix);
                var index = (XRayInstanceVisibilityIndex)typeof(XRayInstanceNativeRenderer).GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(native);
                index.Select(new[] { new Plane(Vector3.right, -52f) }, 1, null);
                Assert.That(index.IsCandidate(0), Is.True);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20f);
                Assert.That(native.Prepare(set, camera, 0, true, null), Is.True, native.FallbackReason);
                Bounds[] secondBounds = SourceBounds(native);
                Bounds[] secondPhysical = PhysicalBounds(native);
                Assert.That(secondBounds, Is.Not.SameAs(firstBounds));
                Assert.That(secondPhysical, Is.Not.SameAs(firstPhysical));
                Assert.That(secondBounds[0].size.x, Is.GreaterThan(firstBounds[0].size.x));
                AssertMeshCorners(secondBounds[0], mesh.bounds, matrix);
                AssertMeshCorners(secondPhysical[0], mesh.bounds, matrix);
                index.Select(new[] { new Plane(Vector3.right, -70f) }, 1, null);
                Assert.That(index.IsCandidate(0), Is.True);
                Matrix4x4 moved = Matrix4x4.TRS(new Vector3(75f, 2f, 10f), Quaternion.Euler(0f, 45f, 0f), new Vector3(2f, 3f, 4f));
                batch.Matrices[0] = moved;
                set.PrepareMatrices("native bounds moved ownership");
                Assert.That(native.Prepare(set, camera, 0, true, null), Is.True, native.FallbackReason);
                Bounds[] movedPhysical = PhysicalBounds(native);
                Assert.That(movedPhysical, Is.Not.SameAs(secondPhysical));
                AssertContains(movedPhysical[0], authored.min);
                AssertContains(movedPhysical[0], authored.max);
                AssertMeshCorners(movedPhysical[0], mesh.bounds, moved);
            }
            finally
            {
                native.Dispose();
                Object.DestroyImmediate(set);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(32f, true)]
        [TestCase(1048576f, true)]
        [TestCase(32f, false)]
        [TestCase(1048576f, false)]
        public void PhysicalSourceBoundsPreserveAuthoredCoverageAndIndependentIndexPadding(float coordinate, bool oversized)
        {
            if (!SystemInfo.supportsInstancing || BatchRendererGroup.BufferTarget != BatchBufferTarget.RawBuffer) Assert.Ignore("Native instance buffers require a supported graphics device.");
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.That(shader, Is.Not.Null);
            var mesh = new Mesh();
            var material = new Material(shader) { enableInstancing = true, renderQueue = 2000 };
            var cameraObject = new GameObject("Native physical bounds camera", typeof(Camera));
            var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            var native = new XRayInstanceNativeRenderer();
            try
            {
                material.SetFloat("_SurfaceType", 0f);
                material.SetFloat("_MonolithFloraWind", 0f);
                material.SetFloat("_MonolithLevelDetail", 0f);
                material.DisableKeyword("_MONOLITH_LEVEL_DETAIL");
                material.DisableKeyword("_MONOLITH_SKINNED_TBN");
                mesh.vertices = new[] { Vector3.one * -1f, Vector3.one, new Vector3(1f, -1f, 0f) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                Vector3 center = new Vector3(coordinate, 2f, 10f);
                Matrix4x4 matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, 90f, 0f), new Vector3(2f, 3f, 4f));
                Bounds authored = new Bounds(center, Vector3.one * (oversized ? 64f : 0.25f));
                var batch = new XRayInstanceBatch("native physical bounds", mesh, new[] { material }, authored, new[] { matrix }, new[] { Vector4.one }, true);
                set.Initialize("native physical bounds", 1f, new List<XRayInstanceBatch> { batch });
                set.PrepareMatrices("native physical bounds ownership");
                Assert.That(native.Prepare(set, camera, 0, true, null), Is.True, native.FallbackReason);
                Bounds physical = PhysicalBounds(native)[0];
                AssertContains(physical, authored.min);
                AssertContains(physical, authored.max);
                AssertMeshCorners(physical, mesh.bounds, matrix);
                Bounds indexed = SourceBounds(native)[0];
                AssertContains(indexed, physical.min);
                AssertContains(indexed, physical.max);
                if (oversized) AssertBoundsBits(physical, authored);
                else Assert.That(indexed.size.x, Is.GreaterThan(physical.size.x));
                var currentBounds = (Bounds)typeof(XRayInstanceNativeRenderer).GetMethod("CurrentGlobalBounds", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(native, null);
                AssertBoundsBits(currentBounds, physical);
                native.Dispose();
                Assert.That(typeof(XRayInstanceNativeRenderer).GetField("_physicalSourceBounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(native), Is.Null);
                Assert.That(typeof(XRayInstanceNativeRenderer).GetField("_windBounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(native), Is.Null);
                Assert.That(typeof(XRayInstanceNativeRenderer).GetField("_windBoundsStamps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(native), Is.Null);
            }
            finally
            {
                native.Dispose();
                Object.DestroyImmediate(set);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        private static Bounds[] SourceBounds(XRayInstanceNativeRenderer native)
        {
            return (Bounds[])typeof(XRayInstanceNativeRenderer).GetField("_sourceBounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(native);
        }

        private static byte[] HashFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                using (SHA256 hash = SHA256.Create())
                {
                    return hash.ComputeHash(stream);
                }
            }
        }

        private static void AssertActualConfigurationInheritance(XRayInstanceBatchSet set, Camera camera, XRayFloraWindState wind)
        {
            GameConfig source = GameConfig.Load();
            Assert.That(source, Is.Not.Null);
            string assetPath = AssetDatabase.GetAssetPath(source);
            Assert.That(assetPath, Is.Not.Null.And.Not.Empty);
            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            byte[] assetHash = HashFile(absolutePath);
            byte[] metaHash = HashFile(absolutePath + ".meta");
            bool dirty = EditorUtility.IsDirty(source);
            bool sourceEnabled = source.InstanceNativeSubmissionEnabled;
            GameConfig configuration = Object.Instantiate(source);
            configuration.hideFlags = HideFlags.HideAndDontSave;
            typeof(GameConfig).GetField("_instanceNativeSubmissionEnabled", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(configuration, true);
            var root = new GameObject("Actual inherited native configuration fixture") { hideFlags = HideFlags.HideAndDontSave };
            var renderer = root.AddComponent<XRayLevelInstancedRenderer>();
            MethodInfo begin = typeof(XRayLevelInstancedRenderer).GetMethod("OnBeginContextRendering", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo end = typeof(XRayLevelInstancedRenderer).GetMethod("OnEndContextRendering", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prepare = typeof(XRayLevelInstancedRenderer).GetMethod("TryNativeSubmission", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                renderer.BatchSet = set;
                renderer.ApplyConfiguration(configuration);
                renderer.NativeSubmissionOverride = null;
                Assert.That(renderer.Configuration, Is.SameAs(configuration));
                Assert.That(configuration.InstanceNativeSubmissionEnabled, Is.True);
                Assert.That(renderer.NativeSubmissionOverride, Is.Null);
                begin.Invoke(renderer, new object[] { default(ScriptableRenderContext), new List<Camera> { camera } });
                object[] arguments = { camera, wind, false };
                Assert.That((bool)prepare.Invoke(renderer, arguments), Is.True, renderer.NativeSubmissionFallbackReason);
                Assert.That((bool)arguments[2], Is.True);
                Assert.That(renderer.NativeSubmissionActive, Is.True);
                Assert.That(renderer.NativeSubmissionFallbackReason, Is.Null.Or.Empty);
                Assert.That(renderer.NativeSubmissionMemoryBytes, Is.GreaterThan(0));
                Assert.That(MatrixExposureFlags(set), Is.EqualTo(new bool[set.Batches.Count]));
                TestContext.WriteLine("Actual inherited native configuration: level=" + set.LevelName + ";batches=" + set.Batches.Count + ";instances=" + set.InstanceCount + ";configurationEnabled=true;sessionOverride=null;prepared=true;fallback=" + renderer.NativeSubmissionFallbackReason);
            }
            finally
            {
                end.Invoke(renderer, new object[] { default(ScriptableRenderContext), new List<Camera> { camera } });
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(configuration);
                Assert.That(HashFile(absolutePath), Is.EqualTo(assetHash));
                Assert.That(HashFile(absolutePath + ".meta"), Is.EqualTo(metaHash));
                Assert.That(EditorUtility.IsDirty(source), Is.EqualTo(dirty));
                Assert.That(source.InstanceNativeSubmissionEnabled, Is.EqualTo(sourceEnabled));
            }
        }

        private static bool[] MatrixExposureFlags(XRayInstanceBatchSet set)
        {
            FieldInfo exposed = typeof(XRayInstanceBatch).GetField("_matricesExposed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(exposed, Is.Not.Null);
            var flags = new bool[set.Batches.Count];
            for (int index = 0; index < flags.Length; index++) flags[index] = (bool)exposed.GetValue(set.Batches[index]);
            return flags;
        }

        private static Bounds[] PhysicalBounds(XRayInstanceNativeRenderer native)
        {
            return (Bounds[])typeof(XRayInstanceNativeRenderer).GetField("_physicalSourceBounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(native);
        }

        private static void AssertBoundsBits(Bounds actual, Bounds expected)
        {
            float[] actualValues = { actual.center.x, actual.center.y, actual.center.z, actual.extents.x, actual.extents.y, actual.extents.z };
            float[] expectedValues = { expected.center.x, expected.center.y, expected.center.z, expected.extents.x, expected.extents.y, expected.extents.z };
            for (int index = 0; index < actualValues.Length; index++) Assert.That(System.BitConverter.SingleToInt32Bits(actualValues[index]), Is.EqualTo(System.BitConverter.SingleToInt32Bits(expectedValues[index])));
        }

        private static void AssertMeshCorners(Bounds worldBounds, Bounds meshBounds, Matrix4x4 matrix)
        {
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 local = new Vector3(x == 0 ? meshBounds.min.x : meshBounds.max.x, y == 0 ? meshBounds.min.y : meshBounds.max.y, z == 0 ? meshBounds.min.z : meshBounds.max.z);
                        AssertContains(worldBounds, matrix.MultiplyPoint3x4(local));
                    }
                }
            }
        }

        private static void AssertContains(Bounds bounds, Vector3 position)
        {
            Assert.That(position.x, Is.InRange(bounds.min.x, bounds.max.x));
            Assert.That(position.y, Is.InRange(bounds.min.y, bounds.max.y));
            Assert.That(position.z, Is.InRange(bounds.min.z, bounds.max.z));
        }
    }
}
