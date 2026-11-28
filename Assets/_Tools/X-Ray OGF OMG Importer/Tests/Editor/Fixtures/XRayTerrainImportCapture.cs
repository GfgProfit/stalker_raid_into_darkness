using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer;
using XRay.Importer.Editor.Levels;

namespace XRay.Importer.Tests
{
    public static class XRayTerrainImportCapture
    {
        public static IEnumerator Run(XRayTerrainLevelFixture fixture, bool trueStalker)
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE05C_OUTPUT");
            Assert.IsNotEmpty(output);
            output = Path.Combine(output, "CapturedImport_" + (trueStalker ? "TS" : "Anomaly"));
            Directory.CreateDirectory(output);
            Scene scene = EditorSceneManager.OpenScene(XRayLevelImportJob.LastRecord.ScenePath, OpenSceneMode.Additive);
            var temporary = new List<UnityEngine.Object>();
            var metrics = new StringBuilder("Mode,Debug,NonFinite,MaxNormalDelta\n");
            var timings = new StringBuilder("Mode,Frame,GpuNanoseconds,GpuBlocks\n");
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Light light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
                    foreach (Camera existing in root.GetComponentsInChildren<Camera>(true)) existing.enabled = false;
                }
                MeshRenderer renderer = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    renderer = root.GetComponentInChildren<MeshRenderer>(true);
                    if (renderer != null) break;
                }
                Assert.IsNotNull(renderer);
                Material stored = renderer.sharedMaterial;
                var material = new Material(stored);
                temporary.Add(material);
                renderer.sharedMaterial = material;
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
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
                    Color weights = mask.GetPixelBilinear(tc.x, !trueStalker && material.GetFloat("_MonolithTerrainMaskFlipV") > 0.5f ? 1.0f - tc.y : tc.y);
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
                sun.gameObject.AddComponent<HDAdditionalLightData>();
                sun.intensity = 3;
                sun.lightUnit = LightUnit.Lux;
                rig.AddComponent<MonolithWeatherInputs>().SetInputs(0.71864f, Vector3.one, new Vector4(0.5f, 1, 0, 0), new Vector4(1.5f, 1, 0, 0), 0);
                rig.AddComponent<MonolithSourceLighting>().Mode = MonolithSourceLighting.LightingMode.Native;
                File.WriteAllText(Path.Combine(output, "Settings.txt"), "Source=" + fixture.SourceIdentity + "\nShader=" + fixture.SourceShader + "\nRenderer=" + renderer.name + "\nMesh=" + UnityEditor.AssetDatabase.GetAssetPath(mesh) + "\nMaterial=" + UnityEditor.AssetDatabase.GetAssetPath(stored) + "\nActiveLayers=" + activeLayers + "\nAPI=" + SystemInfo.graphicsDeviceType + "\nDevice=" + SystemInfo.graphicsDeviceName + "\nDriver=" + SystemInfo.graphicsDeviceVersion + "\nResolution=640x360\nCamera=" + camera.transform.position.ToString("F6") + "\nRotation=" + camera.transform.eulerAngles.ToString("F6") + "\nFOV=55\nSun=3 lux\nExposure=0 fixed; exposure control disabled\nSSAO/SSR/SSGI/Volumetrics/Postprocess/MotionVectors/ContactShadows=disabled\nWarmup=6 renders per mode; GPU=48 frames with first 12 discarded\n");
                File.WriteAllText(Path.Combine(output, "ImportedMaterial.json"), UnityEditor.EditorJsonUtility.ToJson(stored, true));
                using (var target = new XRayTerrainImportCaptureTarget(camera))
                {
                    for (int mode = 0; mode < (trueStalker ? 1 : 6); mode++)
                    {
                        material.SetFloat("_MonolithTerrainQuality", mode >= 4 ? mode - 3 : 0);
                        material.SetFloat("_MonolithTerrainParallax", mode == 1 || mode == 3 ? 1 : 0);
                        material.SetFloat("_MonolithTerrainOcclusion", mode == 2 || mode == 3 ? 1 : 0);
                        File.WriteAllText(Path.Combine(output, "M" + mode + "_Properties.json"), UnityEditor.EditorJsonUtility.ToJson(material, true));
                        if (trueStalker)
                        {
                            Assert.AreEqual(0, material.GetFloat("_MonolithSourceProfile"));
                            Assert.AreEqual(2, material.GetFloat("_MonolithTerrainSource"));
                        }
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
                                    if (float.IsNaN(pixels[pixel][channel]) || float.IsInfinity(pixels[pixel][channel])) nonFinite++;
                                    if (debug == 68 && forward != null) delta = Mathf.Max(delta, Mathf.Abs(pixels[pixel][channel] - forward[pixel][channel]));
                                }
                            }
                            if (debug == 12) forward = pixels;
                            metrics.Append(mode).Append(',').Append(debug).Append(',').Append(nonFinite).Append(',').Append(delta.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                            Assert.AreEqual(0, nonFinite);
                            if (debug == 68) Assert.Less(delta, 0.02f);
                            target.Save(Path.Combine(output, "M" + mode + "_D" + debug));
                        }
                        if (mode > 3 || trueStalker) continue;
                        material.SetFloat("_MonolithDebug", 0);
                        CustomSampler sampler = CustomSampler.Create("Phase05C_ImportedTerrain_" + mode, true);
                        Recorder recorder = sampler.GetRecorder();
                        bool previousProfiler = Profiler.enabled;
                        try
                        {
                            Profiler.enabled = true;
                            recorder.enabled = true;
                            for (int frame = 0; frame < 48; frame++)
                            {
                                sampler.Begin();
                                camera.Render();
                                sampler.End();
                                yield return null;
                                if (frame >= 12) timings.Append(mode).Append(',').Append(frame).Append(',').Append(recorder.gpuElapsedNanoseconds).Append(',').Append(recorder.gpuSampleBlockCount).AppendLine();
                            }
                        }
                        finally
                        {
                            recorder.enabled = false;
                            Profiler.enabled = previousProfiler;
                        }
                    }
                }
            }
            finally
            {
                File.WriteAllText(Path.Combine(output, "Metrics.csv"), metrics.ToString());
                File.WriteAllText(Path.Combine(output, "GPU_Timings.csv"), timings.ToString());
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                EditorSceneManager.CloseScene(scene, true);
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
    }
}
