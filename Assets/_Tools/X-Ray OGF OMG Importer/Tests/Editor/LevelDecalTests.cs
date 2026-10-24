using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class LevelDecalTests
    {
        private static Action _pendingCleanup;

        [TearDown]
        public void ReleaseInterruptedRender()
        {
            Action cleanup = _pendingCleanup;
            _pendingCleanup = null;
            cleanup?.Invoke();
        }

        [Test]
        public void OnlyConfirmedWallmarkBlendUsesSpecializedShader()
        {
            var shader = new XRayLevelShader(1, "effects\\wallmarkblend/decal\\test");
            XRayLevelSurface surface = XRayLevelSurfaceRules.Classify(shader, null, false);
            Assert.IsTrue(surface.IsDecal);
            Assert.IsFalse(surface.UsesMonolith);
            Assert.IsFalse(surface.IsFallback);
            Assert.IsFalse(surface.CastShadows);
        }

        [Test]
        public void MultiplicativeWallmarkAndWaterAreNotClassifiedAsBlendDecals()
        {
            foreach (string name in new[] { "effects\\wallmark", "effects\\wallmarkmult", "effects\\water_underground" })
            {
                var shader = new XRayLevelShader(1, name + "/decal\\test");
                XRayLevelSurface surface = XRayLevelSurfaceRules.Classify(shader, null, false);
                Assert.IsFalse(surface.IsDecal, name);
                Assert.IsTrue(surface.IsFallback, name);
            }
        }

        [Test]
        public void DecalMigrationClearsLegacyPbrProperties()
        {
            var material = new Material(Shader.Find("HDRP/Lit"));
            try
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                ConfigureDecal(material);
                Assert.AreEqual("HDRP/MonolithDecal", material.shader.name);
                Assert.AreEqual(2000, material.renderQueue);
                Assert.AreEqual(0.007f, material.GetFloat("_MonolithDecalNormalShift"));
                Assert.AreEqual(0.003f, material.GetFloat("_MonolithDecalMinShift"));
                Assert.AreEqual(0.011f, material.GetFloat("_MonolithDecalMaxShift"));
                Assert.IsFalse(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
                Assert.AreEqual(-1, material.FindPass("ShadowCaster"));
                Assert.AreEqual(-1, material.FindPass("DepthOnly"));
                Assert.GreaterOrEqual(material.FindPass("DBufferMesh"), 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [UnityTest]
        [Category("Graphics")]
        public IEnumerator DBufferChangesAlbedoAndPreservesNormalGlossHemisphere()
        {
            return RenderDBuffer(false);
        }

        [UnityTest]
        [Category("Graphics")]
        public IEnumerator OrthographicDBufferPreservesOcclusionAndOverlayOrder()
        {
            return RenderDBuffer(true);
        }

        private static IEnumerator RenderDBuffer(bool orthographic)
        {
            var receiver = new Material(Shader.Find("HDRP/MonolithLit"));
            var decal = new Material(Shader.Find("HDRP/MonolithDecal"));
            var baseMap = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var decalMap = new Texture2D(3, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var secondMap = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var secondMaterial = new Material(Shader.Find("HDRP/MonolithDecal"));
            var target = new RenderTexture(96, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(96, 64, TextureFormat.RGBAFloat, false, true);
            var aov = new RenderTexture(96, 64, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            RTHandle buffer = RTHandles.Alloc(96, 64, colorFormat: GraphicsFormat.R32G32B32A32_SFloat);
            GameObject baseObject = null;
            GameObject decalObject = null;
            GameObject cameraObject = null;
            GameObject secondDecal = null;
            RenderTexture previous = RenderTexture.active;
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            _pendingCleanup = () =>
            {
                RenderTexture.active = previous;
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                if (cameraObject != null)
                {
                    cameraObject.GetComponent<HDAdditionalCameraData>().SetAOVRequests(null);
                }

                buffer.Release();
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(secondDecal);
                UnityEngine.Object.DestroyImmediate(decalObject);
                UnityEngine.Object.DestroyImmediate(baseObject);
                UnityEngine.Object.DestroyImmediate(receiver);
                UnityEngine.Object.DestroyImmediate(decal);
                UnityEngine.Object.DestroyImmediate(baseMap);
                UnityEngine.Object.DestroyImmediate(decalMap);
                UnityEngine.Object.DestroyImmediate(secondMap);
                UnityEngine.Object.DestroyImmediate(secondMaterial);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                aov.Release();
                UnityEngine.Object.DestroyImmediate(aov);
            };
            try
            {
                HdrpTestWarmup.Ensure();
                ShaderUtil.allowAsyncCompilation = false;
                baseMap.SetPixel(0, 0, new Color(0.2f, 0.4f, 0.6f, 1.0f));
                baseMap.Apply();
                decalMap.SetPixels(new[] { new Color(0.8f, 0.2f, 0.1f, 0.0f), new Color(0.8f, 0.2f, 0.1f, 0.5f), new Color(0.8f, 0.2f, 0.1f, 1.0f) });
                decalMap.Apply();
                MonolithMaterialConfigurator.Configure(receiver, null, baseMap, null, null, false);
                receiver.EnableKeyword("_MONOLITH_LEVEL");
                receiver.SetFloat("_MonolithLevel", 1.0f);
                receiver.SetFloat("_MonolithLevelHemiMode", 0.0f);
                receiver.SetFloat("_MonolithEnvHemisphere", 0.4f);
                receiver.SetFloat("_SupportDecals", 1.0f);
                receiver.DisableKeyword("_DISABLE_DECALS");
                ConfigureDecal(decal);
                decal.SetTexture("_BaseColorMap", decalMap);
                decal.SetFloat("_MonolithAlbedoSrgb", 0.0f);
                Scene scene = SceneManager.GetActiveScene();
                baseObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                decalObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cameraObject = new GameObject("Decal regression camera");
                foreach (GameObject go in new[] { baseObject, decalObject, cameraObject })
                {
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.layer = 31;
                }

                baseObject.GetComponent<MeshRenderer>().sharedMaterial = receiver;
                baseObject.GetComponent<MeshRenderer>().motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                MeshRenderer decalRenderer = decalObject.GetComponent<MeshRenderer>();
                decalRenderer.sharedMaterial = decal;
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.cullingMask = 1 << 31;
                camera.fieldOfView = 30.75f;
                camera.orthographic = orthographic;
                camera.orthographicSize = 0.55f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100.0f;
                camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                camera.targetTexture = target;
                HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
                data.volumeLayerMask = 0;
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                data.backgroundColorHDR = Color.magenta;
                data.customRenderingSettings = true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
                AOVRequest request = AOVRequest.NewDefault();
                request.SetOverrideRenderFormat(true);
                data.SetAOVRequests(new AOVRequestBuilder().Add(request, id => buffer, null, new[] { AOVBuffers.Color }, (cmd, textures, properties) => cmd.Blit(textures[0], aov)).Build());
                yield return null;
                foreach (float diagnostic in new[] { 9.0f, 22.0f, 20.0f, 36.0f })
                {
                    receiver.SetFloat("_MonolithDebug", diagnostic);
                    var values = new Color[2][];
                    for (int mode = 0; mode < 2; mode++)
                    {
                        decalRenderer.enabled = mode == 1;
                        for (int frame = 0; frame < 4; frame++)
                        {
                            yield return null;
                            camera.Render();
                        }

                        RenderTexture.active = aov;
                        readback.ReadPixels(new Rect(0, 0, 96, 64), 0, 0);
                        readback.Apply();
                        values[mode] = new[] { readback.GetPixel(28, 32), readback.GetPixel(48, 32), readback.GetPixel(68, 32) };
                        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MonolithDecalProbe" + diagnostic + "_" + mode + ".txt"), string.Join(";", Array.ConvertAll(values[mode], value => value.ToString("F6"))));
                    }

                    AssertColor(values[0][0], values[1][0], 0.025f, "zero alpha " + diagnostic);
                    if (diagnostic == 9.0f)
                    {
                        AssertColor(new Color(0.5f, 0.3f, 0.35f), values[1][1], 0.035f, "half alpha");
                        AssertColor(new Color(0.8f, 0.2f, 0.1f), values[1][2], 0.035f, "opaque alpha");
                    }
                    else if (diagnostic == 20.0f)
                    {
                        AssertColor(new Color(values[0][1].r, values[0][1].g, 0.0f), new Color(values[1][1].r, values[1][1].g, 0.0f), 0.005f, "roughness/fresnel");
                        AssertColor(new Color(values[0][2].r, values[0][2].g, 0.0f), new Color(values[1][2].r, values[1][2].g, 0.0f), 0.005f, "roughness/fresnel");
                    }
                    else
                    {
                        AssertColor(values[0][1], values[1][1], 0.005f, "unchanged " + diagnostic);
                        AssertColor(values[0][2], values[1][2], 0.005f, "unchanged " + diagnostic);
                    }
                }

                receiver.SetFloat("_MonolithDebug", 9.0f);
                decalObject.transform.position = Vector3.forward * 0.5f;
                for (int frame = 0; frame < 4; frame++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                    camera.Render();
                }

                RenderTexture.active = aov;
                readback.ReadPixels(new Rect(0, 0, 96, 64), 0, 0);
                readback.Apply();
                AssertColor(new Color(0.2f, 0.4f, 0.6f), readback.GetPixel(68, 32), 0.01f, "occluded decal must not pierce receiver");
                decalObject.transform.position = Vector3.zero;
                secondDecal = GameObject.CreatePrimitive(PrimitiveType.Quad);
                SceneManager.MoveGameObjectToScene(secondDecal, scene);
                secondDecal.layer = 31;
                secondDecal.GetComponent<MeshRenderer>().sharedMaterial = decal;
                secondDecal.GetComponent<MeshRenderer>().rendererPriority = 10;
                for (int frame = 0; frame < 4; frame++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                    camera.Render();
                }

                RenderTexture.active = aov;
                readback.ReadPixels(new Rect(0, 0, 96, 64), 0, 0);
                readback.Apply();
                AssertColor(new Color(0.65f, 0.25f, 0.225f), readback.GetPixel(48, 32), 0.035f, "two overlapping half-alpha decals");
                secondMap.SetPixel(0, 0, new Color(0.1f, 0.8f, 0.2f, 0.5f));
                secondMap.Apply();
                secondMaterial.CopyPropertiesFromMaterial(decal);
                secondMaterial.SetTexture("_BaseColorMap", secondMap);
                secondDecal.GetComponent<MeshRenderer>().sharedMaterial = secondMaterial;
                for (int frame = 0; frame < 4; frame++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                    camera.Render();
                }

                RenderTexture.active = aov;
                readback.ReadPixels(new Rect(0, 0, 96, 64), 0, 0);
                readback.Apply();
                Color mixed = (new Color(0.8f, 0.2f, 0.1f).linear * 0.5f + new Color(0.1f, 0.8f, 0.2f).linear * 0.5f).gamma;
                AssertColor(mixed, readback.GetPixel(68, 32), 0.035f, "higher renderer priority must draw later");
                decalRenderer.rendererPriority = 20;
                for (int frame = 0; frame < 4; frame++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                    camera.Render();
                }

                RenderTexture.active = aov;
                readback.ReadPixels(new Rect(0, 0, 96, 64), 0, 0);
                readback.Apply();
                AssertColor(new Color(0.8f, 0.2f, 0.1f), readback.GetPixel(68, 32), 0.035f, "reversing priority must reverse the overlay");

                Assert.IsFalse(ShaderUtil.ShaderHasError(decal.shader), XRayLevelDiagnostics.ShaderMessages(decal.shader.name));
            }
            finally
            {
                Action cleanup = _pendingCleanup;
                _pendingCleanup = null;
                cleanup?.Invoke();
            }
        }

        [Test]
        public void DecalOrderingFollowsCameraAndRestoresRendererPriorities()
        {
            var root = new GameObject("Decal sorting test");
            var cameraObject = new GameObject("Decal sorting camera");
            GameObject near = GameObject.CreatePrimitive(PrimitiveType.Quad);
            GameObject far = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                near.transform.SetParent(root.transform);
                far.transform.SetParent(root.transform);
                near.transform.position = Vector3.forward * 2.0f;
                far.transform.position = Vector3.forward * 10.0f;
                MeshRenderer nearRenderer = near.GetComponent<MeshRenderer>();
                MeshRenderer farRenderer = far.GetComponent<MeshRenderer>();
                nearRenderer.rendererPriority = 15;
                farRenderer.rendererPriority = 25;
                var ordering = root.AddComponent<XRayLevelDecalOrdering>();
                ordering.Initialize(new[] { nearRenderer, farRenderer });
                Camera camera = cameraObject.AddComponent<Camera>();
                ordering.Order(camera);
                Assert.Less(farRenderer.rendererPriority, nearRenderer.rendererPriority);
                camera.transform.position = Vector3.forward * 12.0f;
                ordering.Order(camera);
                Assert.Less(nearRenderer.rendererPriority, farRenderer.rendererPriority);
                ordering.enabled = false;
                Assert.AreEqual(15, nearRenderer.rendererPriority);
                Assert.AreEqual(25, farRenderer.rendererPriority);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        [Category("ImportedLevelIntegration")]
        public void GeneratedEscapeKeepsDecalVisualsSeparateFromOtherSurfaces()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Source/Levels/escape/Prefabs/escape.prefab");
            if (!System.IO.File.Exists("Assets/_Source/Levels/escape/Prefabs/escape.prefab"))
            {
                Assert.Ignore("Optional fixture missing: import True Stalker escape to Assets/_Source/Levels/escape before running this integration test.");
            }

            Assert.IsNotNull(prefab);
            int count = 0;
            var materials = new HashSet<Material>();
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.AreEqual(MotionVectorGenerationMode.Camera, renderer.motionVectorGenerationMode, renderer.name);
                Material[] assigned = renderer.sharedMaterials;
                if (!Array.Exists(assigned, material => material != null && material.shader.name == "HDRP/MonolithDecal"))
                {
                    continue;
                }

                Assert.AreEqual(1, assigned.Length, renderer.name);
                Assert.AreEqual(1, renderer.GetComponent<MeshFilter>().sharedMesh.subMeshCount, renderer.name);
                Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
                materials.Add(assigned[0]);
                count++;
            }

            Assert.AreEqual(111, count);
            Assert.AreEqual(69, materials.Count);
            Assert.IsNotNull(prefab.GetComponentInChildren<XRayLevelDecalOrdering>(true));
        }

        private static void ConfigureDecal(Material material)
        {
            typeof(XRayLevelMaterialLibrary).GetMethod("ConfigureDecal", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { material, null });
        }

        private static void AssertColor(Color expected, Color actual, float tolerance, string message)
        {
            Assert.Less(Vector3.Distance(new Vector3(expected.r, expected.g, expected.b), new Vector3(actual.r, actual.g, actual.b)), tolerance, $"{message}: expected={expected} actual={actual}");
        }
    }
}
