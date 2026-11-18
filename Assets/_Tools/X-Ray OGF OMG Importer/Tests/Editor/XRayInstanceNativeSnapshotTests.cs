using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayInstanceNativeSnapshotTests
    {
        [TestCase("MatrixMutation")]
        [TestCase("MatrixExposure")]
        [TestCase("PayloadMutation")]
        [TestCase("OwnedMatrixRevision")]
        [TestCase("SetReplacement")]
        [TestCase("SharedBatchCapture")]
        [TestCase("MaterialReplacement")]
        [TestCase("MaterialKeyword")]
        [TestCase("MaterialWind")]
        public void StaleReflectionRegistrationPreservesThePendingNativeSnapshot(string mutation)
        {
            if (!SystemInfo.supportsInstancing || BatchRendererGroup.BufferTarget != BatchBufferTarget.RawBuffer) Assert.Ignore("Native instance buffers require a supported graphics device.");
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.That(shader, Is.Not.Null);
            var owned = new List<UnityEngine.Object>();
            var native = new XRayInstanceNativeRenderer();
            var globalStates = new bool[XRayInstanceNativeRenderer.UnsupportedShaderKeywords.Count];
            bool capturedGlobalStates = false;
            try
            {
                for (int index = 0; index < globalStates.Length; index++) globalStates[index] = Shader.IsKeywordEnabled(XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index]);
                capturedGlobalStates = true;
                for (int index = 0; index < globalStates.Length; index++)
                {
                    string name = XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index];
                    LocalKeyword keyword = shader.keywordSpace.FindKeyword(name);
                    if (keyword.isValid && keyword.isOverridable) Shader.DisableKeyword(name);
                }
                var root = new GameObject("Native snapshot camera");
                owned.Add(root);
                Camera camera = root.AddComponent<Camera>();
                camera.enabled = false;
                var reflectionObject = new GameObject("Native snapshot reflection camera");
                owned.Add(reflectionObject);
                Camera reflection = reflectionObject.AddComponent<Camera>();
                reflection.enabled = false;
                reflection.farClipPlane = 1000f;
                typeof(Camera).GetProperty(nameof(Camera.cameraType)).SetValue(reflection, CameraType.Reflection);
                Assert.That(reflection.cameraType, Is.EqualTo(CameraType.Reflection));
                var mesh = new Mesh();
                owned.Add(mesh);
                mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(0f, 1f, 0f) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();
                Material material = CreateMaterial(shader, owned);
                Matrix4x4 originalMatrix = Matrix4x4.Translate(new Vector3(0f, 0f, 4f));
                Matrix4x4 expectedMatrix = originalMatrix;
                Vector4 originalPayload = new Vector4(0.2f, 0.4f, 0.7f, 0f);
                var batch = new XRayInstanceBatch("native snapshot", mesh, new[] { material }, new Bounds(new Vector3(0f, 0f, 4f), Vector3.one * 2f), new[] { originalMatrix }, new[] { originalPayload }, true);
                XRayInstanceBatch currentBatch = batch;
                var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
                owned.Add(set);
                set.Initialize("native snapshot", 16f, new List<XRayInstanceBatch> { batch });
                set.PrepareMatrices("native snapshot");
                Assert.That(native.Prepare(set, camera, 0, true, null), Is.True, native.FallbackReason);
                Assert.That(native.TryRegisterReflection(reflection), Is.True);
                Assert.That(native.RegisteredReflectionViews, Is.EqualTo(1));
                GraphicsBuffer buffer = Field<GraphicsBuffer>(native, "_buffer");
                object group = Field<object>(native, "_group");
                int payloadOffset = Field<int>(native, "_dataOffset");
                float[] initialPayload = ReadPayload(buffer, payloadOffset);
                Assert.That(initialPayload, Is.EqualTo(new[] { originalPayload.x, originalPayload.y, originalPayload.z, originalPayload.w }));
                bool canRestoreWithoutPreparation = false;
                switch (mutation)
                {
                    case "MatrixMutation":
                        expectedMatrix = Matrix4x4.Translate(new Vector3(9f, 0f, 4f));
                        batch.Matrices[0] = expectedMatrix;
                        break;
                    case "MatrixExposure":
                        Assert.That(batch.Matrices.Length, Is.EqualTo(1));
                        break;
                    case "PayloadMutation":
                        batch.Data[0] = new Vector4(originalPayload.x + 0.0000001f, originalPayload.y, originalPayload.z, originalPayload.w);
                        Assert.That(batch.Data[0].Equals(originalPayload), Is.False);
                        canRestoreWithoutPreparation = true;
                        break;
                    case "OwnedMatrixRevision":
                        set.PrepareMatrices("native snapshot new owner revision");
                        break;
                    case "SetReplacement":
                        expectedMatrix = Matrix4x4.Translate(new Vector3(3f, 0f, 4f));
                        currentBatch = new XRayInstanceBatch("native snapshot replacement", mesh, new[] { material }, new Bounds(new Vector3(3f, 0f, 4f), Vector3.one * 2f), new[] { expectedMatrix }, new[] { originalPayload }, true);
                        set.Initialize("native snapshot replacement", 16f, new List<XRayInstanceBatch> { currentBatch });
                        set.PrepareMatrices("native snapshot replacement");
                        break;
                    case "SharedBatchCapture":
                        var sharedSet = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
                        owned.Add(sharedSet);
                        sharedSet.Initialize("native shared snapshot", 16f, new List<XRayInstanceBatch> { batch });
                        expectedMatrix = Matrix4x4.Translate(new Vector3(7f, 0f, 4f));
                        batch.Matrices[0] = expectedMatrix;
                        sharedSet.PrepareMatrices("native shared snapshot capture");
                        Assert.That(set.MatrixInputSignature, Is.EqualTo("native snapshot"));
                        break;
                    case "MaterialReplacement":
                        batch.Materials[0] = CreateMaterial(shader, owned);
                        canRestoreWithoutPreparation = true;
                        break;
                    case "MaterialKeyword":
                        Assert.That(shader.keywordSpace.FindKeyword("_MONOLITH_LEVEL_DETAIL").isValid, Is.True);
                        material.EnableKeyword("_MONOLITH_LEVEL_DETAIL");
                        Assert.That(material.IsKeywordEnabled("_MONOLITH_LEVEL_DETAIL"), Is.True);
                        canRestoreWithoutPreparation = true;
                        break;
                    case "MaterialWind":
                        material.SetFloat("_MonolithFloraWind", 1f);
                        Assert.That(material.GetFloat("_MonolithFloraWind"), Is.EqualTo(1f));
                        canRestoreWithoutPreparation = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
                Assert.That(native.TryRegisterReflection(reflection), Is.False);
                Assert.That(native.Active, Is.True);
                Assert.That(native.RegisteredReflectionViews, Is.Zero);
                Assert.That(Field<GraphicsBuffer>(native, "_buffer"), Is.SameAs(buffer));
                Assert.That(Field<object>(native, "_group"), Is.SameAs(group));
                Assert.That(Field<int>(native, "_dataOffset"), Is.EqualTo(payloadOffset));
                Assert.That(ReadPayload(buffer, payloadOffset), Is.EqualTo(initialPayload));
                if (canRestoreWithoutPreparation)
                {
                    batch.Data[0] = originalPayload;
                    batch.Materials[0] = material;
                    material.DisableKeyword("_MONOLITH_LEVEL_DETAIL");
                    material.SetFloat("_MonolithFloraWind", 0f);
                    Assert.That(native.TryRegisterReflection(reflection), Is.True);
                    Assert.That(native.Active, Is.True);
                    Assert.That(native.RegisteredReflectionViews, Is.EqualTo(1));
                    Assert.That(Field<GraphicsBuffer>(native, "_buffer"), Is.SameAs(buffer));
                    Assert.That(Field<object>(native, "_group"), Is.SameAs(group));
                    Assert.That(ReadPayload(buffer, payloadOffset), Is.EqualTo(initialPayload));
                }
                else
                {
                    Assert.That(native.TryRegisterReflection(reflection), Is.False);
                    Assert.That(native.RegisteredReflectionViews, Is.Zero);
                    if (mutation == "MatrixMutation" || mutation == "MatrixExposure") set.PrepareMatrices("native snapshot recovery");
                }
                Assert.That(native.Prepare(set, camera, 0, true, null), Is.True, native.FallbackReason);
                Assert.That(native.TryRegisterReflection(reflection), Is.True);
                Assert.That(native.Active, Is.True);
                Assert.That(native.RegisteredReflectionViews, Is.EqualTo(1));
                Assert.That(currentBatch.GetMatrix(0), Is.EqualTo(expectedMatrix));
                Assert.That(currentBatch.Data[0], Is.EqualTo(originalPayload));
            }
            finally
            {
                if (capturedGlobalStates)
                {
                    for (int index = 0; index < globalStates.Length; index++)
                    {
                        string name = XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index];
                        LocalKeyword keyword = shader.keywordSpace.FindKeyword(name);
                        if (!keyword.isValid || !keyword.isOverridable) continue;
                        if (globalStates[index]) Shader.EnableKeyword(name);
                        else Shader.DisableKeyword(name);
                    }
                }
                native.Dispose();
                for (int index = owned.Count - 1; index >= 0; index--) UnityEngine.Object.DestroyImmediate(owned[index]);
            }
        }

        [Test]
        public void PreparedShadowMaterialLookupReusesTheAcceptedResolverResult()
        {
            if (!SystemInfo.supportsInstancing || BatchRendererGroup.BufferTarget != BatchBufferTarget.RawBuffer) Assert.Ignore("Native instance buffers require a supported graphics device.");
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.That(shader, Is.Not.Null);
            var owned = new List<UnityEngine.Object>();
            var native = new XRayInstanceNativeRenderer();
            var globalStates = new bool[XRayInstanceNativeRenderer.UnsupportedShaderKeywords.Count];
            for (int index = 0; index < globalStates.Length; index++) globalStates[index] = Shader.IsKeywordEnabled(XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index]);
            try
            {
                for (int index = 0; index < globalStates.Length; index++)
                {
                    string name = XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index];
                    LocalKeyword keyword = shader.keywordSpace.FindKeyword(name);
                    if (keyword.isValid && keyword.isOverridable) Shader.DisableKeyword(name);
                }
                var root = new GameObject("Native cached shadow material camera", typeof(Camera));
                owned.Add(root);
                Camera camera = root.GetComponent<Camera>();
                camera.enabled = false;
                var mesh = new Mesh();
                owned.Add(mesh);
                mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(0f, 1f, 0f) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();
                Material source = CreateMaterial(shader, owned);
                Material resolved = CreateMaterial(shader, owned);
                Material missing = CreateMaterial(shader, owned);
                var batch = new XRayInstanceBatch("native cached material", mesh, new[] { source }, new Bounds(Vector3.zero, Vector3.one * 2f), new[] { Matrix4x4.identity }, new[] { Vector4.one }, true);
                var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
                owned.Add(set);
                set.Initialize("native cached material", 16f, new List<XRayInstanceBatch> { batch });
                set.PrepareMatrices("native cached material");
                int calls = 0;
                Func<Material, Material> resolver = material =>
                {
                    calls++;
                    Assert.That(material, Is.SameAs(source));
                    return resolved;
                };
                Assert.That(native.Prepare(set, camera, 0, true, null, resolver), Is.True, native.FallbackReason);
                Assert.That(calls, Is.EqualTo(1));
                for (int index = 0; index < 16; index++)
                {
                    Material actual;
                    Assert.That(native.TryGetResolvedMaterial(source, out actual), Is.True);
                    Assert.That(actual, Is.SameAs(resolved));
                }
                Material unavailable;
                Assert.That(native.TryGetResolvedMaterial(missing, out unavailable), Is.False);
                Assert.That(unavailable, Is.Null);
                Assert.That(native.TryGetResolvedMaterial(null, out unavailable), Is.False);
                Assert.That(unavailable, Is.Null);
                Assert.That(calls, Is.EqualTo(1));
                native.Disable();
                Assert.That(native.TryGetResolvedMaterial(source, out unavailable), Is.False);
                Assert.That(unavailable, Is.Null);
                Assert.That(calls, Is.EqualTo(1));
            }
            finally
            {
                for (int index = 0; index < globalStates.Length; index++)
                {
                    string name = XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index];
                    LocalKeyword keyword = shader.keywordSpace.FindKeyword(name);
                    if (!keyword.isValid || !keyword.isOverridable) continue;
                    if (globalStates[index]) Shader.EnableKeyword(name);
                    else Shader.DisableKeyword(name);
                }
                native.Dispose();
                for (int index = owned.Count - 1; index >= 0; index--) UnityEngine.Object.DestroyImmediate(owned[index]);
            }
        }

        private static Material CreateMaterial(Shader shader, List<UnityEngine.Object> owned)
        {
            var material = new Material(shader) { enableInstancing = true, renderQueue = 2000 };
            owned.Add(material);
            material.SetFloat("_SurfaceType", 0f);
            material.SetFloat("_MonolithLevelWind", 0f);
            material.SetFloat("_MonolithFloraWind", 0f);
            material.SetFloat("_MonolithLevelDetail", 0f);
            for (int index = 0; index < XRayInstanceNativeRenderer.UnsupportedShaderKeywords.Count; index++) material.DisableKeyword(XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index]);
            return material;
        }

        private static T Field<T>(XRayInstanceNativeRenderer native, string name)
        {
            FieldInfo field = typeof(XRayInstanceNativeRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(native);
        }

        private static float[] ReadPayload(GraphicsBuffer buffer, int offset)
        {
            var values = new float[4];
            buffer.GetData(values, 0, offset / sizeof(float), values.Length);
            return values;
        }
    }
}
