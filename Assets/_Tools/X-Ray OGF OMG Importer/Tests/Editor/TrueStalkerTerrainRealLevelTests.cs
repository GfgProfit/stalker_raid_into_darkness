using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer;
using XRay.Importer.Editor.Levels;

namespace XRay.Importer.Tests
{
    public sealed class TrueStalkerTerrainRealLevelTests
    {
        [UnityEngine.TestTools.UnityTest]
        [Timeout(600000)]
        public IEnumerator SavedTrueStalkerEscapeRemainsReadOnly()
        {
            const string ROOT = "Assets/_Source/Levels";
            const string LEVEL = "escape";
            const string MESH_PATH = ROOT + "/escape/Meshes/Static/S14_C0886.asset";
            const string MATERIAL_PATH = ROOT + "/Materials/levels/escape/terrain/levels_escape_terrain_terrain_escape_terrain.mat";
            if (!File.Exists(MESH_PATH) || !File.Exists(MATERIAL_PATH))
            {
                Assert.Ignore("This integration test requires the saved True Stalker escape terrain.");
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_PATH);
            Material stored = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            Assert.IsNotNull(mesh);
            Assert.IsNotNull(stored);
            string storedBytes = File.ReadAllText(MATERIAL_PATH);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            XRayTerrainDiagnosticBinding bindings = null;
            try
            {
                var geometry = new GameObject("Read-only imported mesh capture");
                temporary.Add(geometry);
                SceneManager.MoveGameObjectToScene(geometry, scene);
                geometry.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = geometry.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var material = new Material(stored);
                temporary.Add(material);
                bindings = XRayTerrainDiagnosticBinding.Open(ROOT, LEVEL);
                bindings.Bind(stored, material, trueStalker: true);
                renderer.sharedMaterial = material;
                Texture2D mask = ReadTexture(material.GetTexture("_MonolithTerrainMask"));
                temporary.Add(mask);
                Vector3 center = mesh.bounds.center;
                Vector3 normal = Vector3.up;
                int activeLayers = 0;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector2[] uv = mesh.uv;
                int[] indices = mesh.triangles;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i];
                    int b = indices[i + 1];
                    int c = indices[i + 2];
                    Vector3 n = (normals[a] + normals[b] + normals[c]).normalized;
                    if (n.y < 0.5f) continue;
                    Vector2 tc = (uv[a] + uv[b] + uv[c]) / 3.0f;
                    Color weights = mask.GetPixelBilinear(tc.x, material.GetFloat("_MonolithTerrainMaskFlipV") > 0.5f ? 1.0f - tc.y : tc.y);
                    float sum = weights.r + weights.g + weights.b + weights.a;
                    if (sum <= 0) continue;
                    int active = 0;
                    for (int channel = 0; channel < 4; channel++) if (weights[channel] / sum > 0.05f) active++;
                    if (active <= activeLayers) continue;
                    activeLayers = active;
                    center = (vertices[a] + vertices[b] + vertices[c]) / 3.0f;
                    normal = n;
                    if (active >= 3) break;
                }
                Assert.GreaterOrEqual(activeLayers, 2);
                var rig = new GameObject("Import acceptance capture");
                temporary.Add(rig);
                SceneManager.MoveGameObjectToScene(rig, scene);
                Camera camera = new GameObject("Capture Camera").AddComponent<Camera>();
                camera.transform.SetParent(rig.transform);
                camera.scene = scene;
                camera.enabled = false;
                camera.fieldOfView = 55;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 300;
                camera.aspect = 640.0f / 360;
                camera.allowHDR = true;
                camera.useOcclusionCulling = false;
                Vector3 tangent = Vector3.Cross(normal, Vector3.right).normalized;
                camera.transform.position = center + normal * 3 - tangent * 1.5f;
                camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, Vector3.up);
                HDAdditionalCameraData data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                data.customRenderingSettings = true;
                data.volumeLayerMask = ~0;
                foreach (FrameSettingsField field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.MotionVectors, FrameSettingsField.ContactShadows, FrameSettingsField.Volumetrics, FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl })
                {
                    data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                    data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                temporary.Add(profile);
                profile.Add<Bloom>(true).intensity.Override(0);
                profile.Add<Fog>(true).enabled.Override(false);
                profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
                Exposure exposure = profile.Add<Exposure>(true);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(0);
                VisualEnvironment environment = profile.Add<VisualEnvironment>(true);
                environment.skyType.Override(SkySettings.GetUniqueID<GradientSky>());
                GradientSky sky = profile.Add<GradientSky>(true);
                sky.top.Override(new Color(0.1f, 0.12f, 0.16f));
                sky.middle.Override(new Color(0.06f, 0.07f, 0.09f));
                sky.bottom.Override(new Color(0.03f, 0.025f, 0.02f));
                Volume volume = rig.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000;
                volume.sharedProfile = profile;
                Light sun = new GameObject("Capture Sun").AddComponent<Light>();
                sun.transform.SetParent(rig.transform);
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(55, -30, 0);
                sun.shadows = LightShadows.None;
                sun.gameObject.AddComponent<HDAdditionalLightData>().SetIntensity(3, LightUnit.Lux);
                rig.AddComponent<MonolithWeatherInputs>().SetInputs(0.71864f, Vector3.one, new Vector4(0.5f, 1, 0, 0), new Vector4(1.5f, 1, 0, 0), 0);
                rig.AddComponent<MonolithSourceLighting>().Mode = MonolithSourceLighting.LightingMode.Native;
                using (var target = new Target(camera))
                {
                    foreach (int mode in new[] { 0, 1 })
                    {
                        material.SetFloat("_MonolithTrueStalkerSourceTerrain", mode);
                        Assert.AreEqual(0, material.GetFloat("_MonolithSourceProfile"));
                        Assert.AreEqual(2, material.GetFloat("_MonolithTerrainSource"));
                        for (int warm = 0; warm < 6; warm++)
                        {
                            camera.Render();
                            yield return null;
                        }

                        Color[] forward = null;
                        foreach (int debug in new[] { 0, 12, 68, 67 })
                        {
                            material.SetFloat("_MonolithDebug", debug);
                            camera.Render();
                            camera.Render();
                            Color[] pixels = target.Pixels();
                            int nonFinite = 0;
                            float delta = 0;
                            for (int pixel = 0; pixel < pixels.Length; pixel++)
                            {
                                for (int channel = 0; channel < 3; channel++)
                                {
                                    if (float.IsNaN(pixels[pixel][channel]) || float.IsInfinity(pixels[pixel][channel]))
                                    {
                                        nonFinite++;
                                    }
                                    if (debug == 68 && forward != null)
                                    {
                                        delta = Mathf.Max(delta, Mathf.Abs(pixels[pixel][channel] - forward[pixel][channel]));
                                    }
                                }
                            }

                            Assert.AreEqual(0, nonFinite);
                            if (debug == 12)
                            {
                                forward = pixels;
                            }
                            if (debug == 68)
                            {
                                Assert.Less(delta, 0.02f);
                            }
                        }
                    }
                }
            }
            finally
            {
                bindings?.Dispose();
                for (int i = temporary.Count - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(temporary[i]);
                }
                EditorSceneManager.ClosePreviewScene(scene);
                Assert.AreEqual(storedBytes, File.ReadAllText(MATERIAL_PATH));
                Assert.IsFalse(EditorUtility.IsDirty(stored));
            }
        }

        private static Texture2D ReadTexture(Texture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return image;
        }

        private sealed class Target : IDisposable
        {
            private readonly Camera _camera;
            private readonly RenderTexture _target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            private readonly Texture2D _pixels = new Texture2D(640, 360, TextureFormat.RGBAFloat, false, true);

            public Target(Camera camera)
            {
                _camera = camera;
                _target.Create();
                camera.targetTexture = _target;
            }

            public Color[] Pixels()
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _target;
                _pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
                _pixels.Apply();
                RenderTexture.active = previous;
                return _pixels.GetPixels();
            }

            public void Dispose()
            {
                _camera.targetTexture = null;
                _target.Release();
                UnityEngine.Object.DestroyImmediate(_pixels);
                UnityEngine.Object.DestroyImmediate(_target);
            }
        }
    }
}
