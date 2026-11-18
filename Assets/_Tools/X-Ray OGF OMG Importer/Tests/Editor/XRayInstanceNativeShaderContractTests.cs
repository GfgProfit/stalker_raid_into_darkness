using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayInstanceNativeShaderContractTests
    {
        [Test]
        public void UnsupportedShaderKeywordContractIsReadOnly()
        {
            var keywords = XRayInstanceNativeRenderer.UnsupportedShaderKeywords as IList<string>;
            Assert.That(keywords, Is.Not.Null);
            Assert.That(keywords.IsReadOnly, Is.True);
            Assert.That(keywords.Count, Is.EqualTo(10));
            string first = keywords[0];
            Assert.Throws<NotSupportedException>(() => keywords[0] = "changed");
            Assert.That(keywords[0], Is.EqualTo(first));
        }

        [TestCaseSource(nameof(DeclaredShaderKeywordCases))]
        public void UnsupportedShaderKeywordUsesFallbackAndRecoversAfterRemoval(string keyword, bool global)
        {
            if (!SystemInfo.supportsInstancing || BatchRendererGroup.BufferTarget != BatchBufferTarget.RawBuffer) Assert.Ignore("Native instance buffers require a supported graphics device.");
            Assert.That(XRayInstanceNativeRenderer.UnsupportedShaderKeywords, Does.Contain(keyword));
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.keywordSpace.FindKeyword(keyword).isValid, Is.True);
            var mesh = new Mesh();
            var material = new Material(shader);
            var cameraObject = new GameObject("Native shader contract camera", typeof(Camera));
            var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            var native = new XRayInstanceNativeRenderer();
            var globalStates = new bool[XRayInstanceNativeRenderer.UnsupportedShaderKeywords.Count];
            bool capturedGlobalStates = false;
            try
            {
                for (int index = 0; index < globalStates.Length; index++)
                {
                    globalStates[index] = Shader.IsKeywordEnabled(XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index]);
                }
                capturedGlobalStates = true;
                for (int index = 0; index < globalStates.Length; index++)
                {
                    LocalKeyword descriptor = shader.keywordSpace.FindKeyword(XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index]);
                    if (descriptor.isValid && descriptor.isOverridable) Shader.DisableKeyword(XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index]);
                }
                material.enableInstancing = true;
                material.renderQueue = 2000;
                material.SetFloat("_SurfaceType", 0f);
                material.SetFloat("_MonolithFloraWind", 0f);
                material.SetFloat("_MonolithLevelDetail", 0f);
                for (int index = 0; index < XRayInstanceNativeRenderer.UnsupportedShaderKeywords.Count; index++)
                {
                    material.DisableKeyword(XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index]);
                }
                mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(0f, 1f, 0f) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                Matrix4x4 matrix = Matrix4x4.Translate(new Vector3(0f, 0f, 4f));
                var batch = new XRayInstanceBatch("native shader contract", mesh, new[] { material }, new Bounds(new Vector3(0f, 0f, 4f), Vector3.one * 2f), new[] { matrix }, new[] { new Vector4(0.2f, 0.4f, 0.7f, 0f) }, true);
                set.Initialize("native shader contract", 1f, new List<XRayInstanceBatch> { batch });
                set.PrepareMatrices("native shader contract");
                Assert.That(native.Prepare(set, camera, 0, true, null), Is.True, native.FallbackReason);
                Assert.That(native.Active, Is.True);
                if (global)
                {
                    Assert.That(shader.keywordSpace.FindKeyword(keyword).isOverridable, Is.True);
                    Shader.EnableKeyword(keyword);
                    Assert.That(Shader.IsKeywordEnabled(keyword), Is.True);
                    Assert.That(material.IsKeywordEnabled(keyword), Is.False);
                }
                else
                {
                    material.EnableKeyword(keyword);
                    Assert.That(material.IsKeywordEnabled(keyword), Is.True);
                }
                Assert.That(native.Prepare(set, camera, 0, true, null), Is.False);
                Assert.That(native.Active, Is.False);
                Assert.That(native.FallbackReason, Does.Contain(global ? "global keyword" : "material"));
                Assert.That(batch.GetMatrix(0), Is.EqualTo(matrix));
                Assert.That(batch.Data[0], Is.EqualTo(new Vector4(0.2f, 0.4f, 0.7f, 0f)));
                if (global) Shader.DisableKeyword(keyword);
                else material.DisableKeyword(keyword);
                Assert.That(native.Prepare(set, camera, 0, true, null), Is.True, native.FallbackReason);
                Assert.That(native.Active, Is.True);
                Assert.That(native.FallbackReason, Is.Null);
            }
            finally
            {
                if (capturedGlobalStates)
                {
                    for (int index = 0; index < globalStates.Length; index++)
                    {
                        LocalKeyword descriptor = shader.keywordSpace.FindKeyword(XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index]);
                        if (!descriptor.isValid || !descriptor.isOverridable) continue;
                        if (globalStates[index]) Shader.EnableKeyword(XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index]);
                        else Shader.DisableKeyword(XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index]);
                    }
                }
                native.Dispose();
                UnityEngine.Object.DestroyImmediate(set);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static IEnumerable<TestCaseData> DeclaredShaderKeywordCases()
        {
            Shader shader = Shader.Find("HDRP/MonolithLit");
            if (shader == null) throw new InvalidOperationException("The native instance shader is required.");
            for (int index = 0; index < XRayInstanceNativeRenderer.UnsupportedShaderKeywords.Count; index++)
            {
                string name = XRayInstanceNativeRenderer.UnsupportedShaderKeywords[index];
                LocalKeyword keyword = shader.keywordSpace.FindKeyword(name);
                if (!keyword.isValid) continue;
                yield return new TestCaseData(name, false);
                if (keyword.isOverridable) yield return new TestCaseData(name, true);
            }
        }
    }
}
