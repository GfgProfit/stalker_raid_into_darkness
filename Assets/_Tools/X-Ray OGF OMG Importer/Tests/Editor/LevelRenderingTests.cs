using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class LevelRenderingTests
    {
        [Test]
        public void DetailCoordinatesUsePackingOriginInsteadOfExpandedBounds()
        {
            var bounds = new Bounds(new Vector3(117.5f, 7.5f, 217.5f), new Vector3(45.0f, 5.0f, 45.0f));
            var chunk = new XRayDetailChunk(bounds, new Vector3(100.0f, 0.0f, 200.0f), 0, 1, 0, 0);
            chunk = JsonUtility.FromJson<XRayDetailChunk>(JsonUtility.ToJson(chunk));
            var bytes = new byte[XRayDetailDatabase.INSTANCE_STRIDE];
            BitConverter.GetBytes((ushort)32768).CopyTo(bytes, 0);
            BitConverter.GetBytes((ushort)16384).CopyTo(bytes, 2);
            BitConverter.GetBytes(7.25f).CopyTo(bytes, 4);
            BitConverter.GetBytes((ushort)4096).CopyTo(bytes, 10);
            var database = ScriptableObject.CreateInstance<XRayDetailDatabase>();
            var go = new GameObject("Detail coordinate test");
            go.SetActive(false);
            try
            {
                database.Initialize("test", 32.0f, 0.6f, 1.0f, 2.0f, new List<XRayDetailModelEntry>(), new List<XRayDetailChunk> { chunk }, null, null, default, default, 1, 0);
                var vegetation = go.AddComponent<XRayDetailVegetation>();
                vegetation.Database = database;
                typeof(XRayDetailVegetation).GetField("_instanceBytes", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(vegetation, bytes);
                object decoded = typeof(XRayDetailVegetation).GetMethod("Decode", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(vegetation, new object[] { chunk });
                Type type = decoded.GetType();
                float x = ((float[])type.GetProperty("X").GetValue(decoded))[0];
                float y = ((float[])type.GetProperty("Y").GetValue(decoded))[0];
                float z = ((float[])type.GetProperty("Z").GetValue(decoded))[0];
                Assert.AreEqual(116.00024f, x, 0.0005f);
                Assert.AreEqual(208.00012f, z, 0.0005f);
                Assert.AreEqual(7.25f, y);
                Assert.AreNotEqual(bounds.min.x + 16.00024f, x);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void AlphaTestConfigurationDisablesBlendingAndUsesMatchingThresholds()
        {
            Material material = CreateCutoutMaterial();
            try
            {
                Assert.IsTrue(material.IsKeywordEnabled("_ALPHATEST_ON"));
                Assert.IsFalse(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
                Assert.AreEqual(0.0f, material.GetFloat("_SurfaceType"));
                Assert.AreEqual(1.0f, material.GetFloat("_ZWrite"));
                Assert.AreEqual(1.0f, material.GetFloat("_SrcBlend"));
                Assert.AreEqual(0.0f, material.GetFloat("_DstBlend"));
                Assert.AreEqual(material.GetFloat("_AlphaCutoff"), material.GetFloat("_AlphaCutoffShadow"));
                Assert.AreEqual(material.GetFloat("_AlphaCutoff"), material.GetFloat("_AlphaCutoffPrepass"));
                Assert.AreEqual(material.GetFloat("_AlphaCutoff"), material.GetFloat("_AlphaCutoffPostpass"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TransparentFallbackDoesNotPreserveSpecularAtZeroOpacity()
        {
            var material = new Material(Shader.Find("HDRP/Lit"));
            try
            {
                var library = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Update);
                var request = new XRayLevelMaterialRequest { Surface = new XRayLevelSurface { Kind = XRayLevelSurfaceKind.Transparent, IsFallback = true } };
                typeof(XRayLevelMaterialLibrary).GetMethod("Configure", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(library, new object[] { material, request });
                Assert.AreEqual(0.0f, material.GetFloat("_EnableBlendModePreserveSpecularLighting"));
                Assert.AreEqual(1.0f, material.GetFloat("_SurfaceType"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void OffscreenShadowCasterRemainsInSubmissionSet()
        {
            var go = new GameObject("Offscreen shadow test");
            go.SetActive(false);
            var cameraObject = new GameObject("Offscreen shadow test camera");
            var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            var mesh = new Mesh();
            Material material = CreateCutoutMaterial();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                var bounds = new Bounds(new Vector3(50.0f, 0.0f, 10.0f), Vector3.one);
                var batch = new XRayInstanceBatch("offscreen", mesh, new[] { material }, bounds, new[] { Matrix4x4.Translate(bounds.center) }, null, true);
                set.Initialize("test", 32.0f, new List<XRayInstanceBatch> { batch });
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                var renderer = go.AddComponent<XRayLevelInstancedRenderer>();
                renderer.BatchSet = set;
                Assert.IsFalse(GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bounds));
                typeof(XRayLevelInstancedRenderer).GetMethod("OnBeginCameraRendering", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(renderer, new object[] { default(UnityEngine.Rendering.ScriptableRenderContext), camera });
                Assert.AreEqual(0, renderer.LastVisibleBatches);
                Assert.AreEqual(1, renderer.LastShadowOnlyBatches);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(set);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [UnityTest]
        [Category("Graphics")]
        public IEnumerator AlphaClippingRejectsTransparentTexelsInCameraRender()
        {
            Assume.That(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null);
            Scene scene = SceneManager.GetActiveScene();
            Material material = CreateCutoutMaterial();
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            GameObject quad = null;
            GameObject cameraObject = null;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                texture.SetPixels(new[] { new Color(1.0f, 1.0f, 1.0f, 0.5f), Color.white });
                texture.Apply();
                material.SetTexture("_BaseColorMap", texture);
                material.SetFloat("_MonolithDebug", 8.0f);
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                SceneManager.MoveGameObjectToScene(quad, scene);
                quad.layer = 31;
                quad.GetComponent<MeshRenderer>().sharedMaterial = material;
                cameraObject = new GameObject("Alpha clipping test camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.cullingMask = 1 << 31;
                camera.orthographic = true;
                camera.orthographicSize = 0.6f;
                camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                camera.targetTexture = target;
                var hdCamera = cameraObject.AddComponent<HDAdditionalCameraData>();
                hdCamera.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                hdCamera.backgroundColorHDR = Color.magenta;
                hdCamera.volumeLayerMask = 0;
                hdCamera.customRenderingSettings = true;
                hdCamera.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);
                hdCamera.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
                yield return null;
                for (int i = 0; i < 3; i++)
                {
                    camera.Render();
                    yield return null;
                }

                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                readback.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "XRayAlphaClippingProbe.png"), readback.EncodeToPNG());
                Color clipped = readback.GetPixel(20, 32);
                Color background = readback.GetPixel(2, 32);
                Color opaque = readback.GetPixel(44, 32);
                Assert.Less(Vector3.Distance(new Vector3(clipped.r, clipped.g, clipped.b), new Vector3(background.r, background.g, background.b)), 0.05f, $"background={background} clipped={clipped} opaque={opaque}");
                Assert.Greater(opaque.g, clipped.g + 0.2f, $"background={background} clipped={clipped} opaque={opaque}");
            }
            finally
            {
                RenderTexture.active = previous;
                ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(quad);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static Material CreateCutoutMaterial()
        {
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.IsNotNull(shader);
            var material = new Material(shader);
            var library = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Update);
            var request = new XRayLevelMaterialRequest { Surface = new XRayLevelSurface { Kind = XRayLevelSurfaceKind.AlphaTest, AlphaCutoff = XRayLevelSurfaceRules.R2_AREF, DoubleSided = true } };
            typeof(XRayLevelMaterialLibrary).GetMethod("Configure", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(library, new object[] { material, request });
            return material;
        }
    }
}
