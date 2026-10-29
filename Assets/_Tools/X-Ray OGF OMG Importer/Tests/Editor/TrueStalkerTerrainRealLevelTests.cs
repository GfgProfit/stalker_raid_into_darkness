using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Profiling;
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
            yield return Run(true);
        }

        [UnityEngine.TestTools.UnityTest]
        [Timeout(600000)]
        public IEnumerator AnomalyTerrainMatchesFrozenCheckpoint()
        {
            yield return Run(false);
        }

        private static IEnumerator Run(bool trueStalker)
        {
            string output = Path.Combine(TrueStalkerTerrainReference.Output(), trueStalker ? "Real_TS_Escape" : "Anomaly_AB");
            Directory.CreateDirectory(output);
            string root = trueStalker ? "Assets/_Source/Levels" : "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d";
            string level = trueStalker ? "escape" : "l01_escape";
            string meshPath = root + "/" + level + "/Meshes/Static/" + (trueStalker ? "S14_C0886.asset" : "S30_C0876.asset");
            string materialPath = root + "/Materials/levels/" + level + "/terrain/" + (trueStalker ? "levels_escape_terrain_terrain_escape_terrain.mat" : "terrain_escape_terrain.mat");
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            Material stored = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Assert.IsNotNull(mesh);
            Assert.IsNotNull(stored);
            string storedBytes = File.ReadAllText(materialPath);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            var metrics = new StringBuilder("Mode,Debug,NonFinite,MaxNormalDelta\n");
            var timings = new StringBuilder("Mode,Frame,GpuNanoseconds,GpuBlocks\n");
            var comparisons = new StringBuilder("Mode,Debug,MaxAbsoluteDelta,ComparedComponents\n");
            var baselinePixels = new Dictionary<int, Color[]>();
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
                bindings = XRayTerrainDiagnosticBinding.Open(root, level);
                bindings.Bind(stored, material, trueStalker);
                if (!trueStalker)
                {
                    material.SetFloat("_MonolithSourceProfile", 1);
                    material.SetFloat("_MonolithSourceTerrain", 1);
                    material.SetFloat("_MonolithTerrainParallax", 0);
                    material.SetFloat("_MonolithTerrainOcclusion", 0);
                }
                Shader currentShader = material.shader;
                Shader baselineShader = FrozenShader();
                temporary.Add(baselineShader);
                Assert.IsFalse(ShaderUtil.ShaderHasError(baselineShader));
                renderer.sharedMaterial = material;
                var inventory = new StringBuilder("Vertex,Px,Py,Pz,Nx,Ny,Nz,NormalLength,Tx,Ty,Tz,Bx,By,Bz,UVx,UVy,Hemisphere\n");
                var colors = new List<Color>();
                var binormal = new List<Vector2>();
                var hemi = new List<Vector2>();
                mesh.GetColors(colors);
                mesh.GetUVs(1, binormal);
                mesh.GetUVs(2, hemi);
                for (int vertex = 0; vertex < mesh.vertexCount; vertex++)
                {
                    Vector3 p = mesh.vertices[vertex];
                    Vector3 n = mesh.normals[vertex];
                    Vector3 t = new Vector3(colors[vertex].r, colors[vertex].g, colors[vertex].b) * 4 - Vector3.one * 2;
                    Vector3 b = new Vector3(binormal[vertex].x, binormal[vertex].y, hemi[vertex].x) * 4 - Vector3.one * 2;
                    Vector2 tc = mesh.uv[vertex];
                    inventory.AppendLine(string.Join(",", vertex, p.x.ToString("R", CultureInfo.InvariantCulture), p.y.ToString("R", CultureInfo.InvariantCulture), p.z.ToString("R", CultureInfo.InvariantCulture), n.x.ToString("R", CultureInfo.InvariantCulture), n.y.ToString("R", CultureInfo.InvariantCulture), n.z.ToString("R", CultureInfo.InvariantCulture), n.magnitude.ToString("R", CultureInfo.InvariantCulture), t.x.ToString("R", CultureInfo.InvariantCulture), t.y.ToString("R", CultureInfo.InvariantCulture), t.z.ToString("R", CultureInfo.InvariantCulture), b.x.ToString("R", CultureInfo.InvariantCulture), b.y.ToString("R", CultureInfo.InvariantCulture), b.z.ToString("R", CultureInfo.InvariantCulture), tc.x.ToString("R", CultureInfo.InvariantCulture), tc.y.ToString("R", CultureInfo.InvariantCulture), hemi[vertex].y.ToString("R", CultureInfo.InvariantCulture)));
                }
                File.WriteAllText(Path.Combine(output, "Mesh_Inputs.csv"), inventory.ToString());
                var layout = new StringBuilder("Attribute,Format,Dimension,Stream,Offset\n");
                foreach (VertexAttributeDescriptor attribute in mesh.GetVertexAttributes()) layout.AppendLine(attribute.attribute + "," + attribute.format + "," + attribute.dimension + "," + attribute.stream + "," + mesh.GetVertexAttributeOffset(attribute.attribute));
                File.WriteAllText(Path.Combine(output, "Mesh_Layout.csv"), layout.ToString());
                var channels = new StringBuilder();
                var channelValues = new List<Vector4>();
                for (int channel = 0; channel < 8; channel++)
                {
                    mesh.GetUVs(channel, channelValues);
                    channels.AppendLine("UV" + channel + ": " + channelValues.Count);
                }
                channels.AppendLine("Tangents: " + mesh.tangents.Length);
                channels.AppendLine("Vertices: " + mesh.vertexCount);
                channels.AppendLine("Submeshes: " + mesh.subMeshCount);
                File.WriteAllText(Path.Combine(output, "Mesh_Channels.txt"), channels.ToString());
                File.WriteAllText(Path.Combine(output, "Source_Manifest.json"), EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(XRayLevelPaths.ManifestPath(root, level)), true));
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
                File.WriteAllText(Path.Combine(output, "Settings.txt"), "Source=" + (trueStalker ? "True Stalker Patch 1.5 escape" : "Anomaly MO2 l01_escape") + "\nShader=" + "terrain" + "\nRenderer=" + renderer.name + "\nMesh=" + UnityEditor.AssetDatabase.GetAssetPath(mesh) + "\nMaterial=" + UnityEditor.AssetDatabase.GetAssetPath(stored) + "\nActiveLayers=" + activeLayers + "\nAPI=" + SystemInfo.graphicsDeviceType + "\nDevice=" + SystemInfo.graphicsDeviceName + "\nDriver=" + SystemInfo.graphicsDeviceVersion + "\nResolution=640x360\nCamera=" + camera.transform.position.ToString("F6") + "\nRotation=" + camera.transform.eulerAngles.ToString("F6") + "\nFOV=55\nSun=3 lux\nExposure=0 fixed; exposure control disabled\nSSAO/SSR/SSGI/Volumetrics/Postprocess/MotionVectors/ContactShadows=disabled\nWarmup=6 renders per mode; GPU=36 whole-camera frames with first 12 discarded; not isolated pass cost\n");
                File.WriteAllText(Path.Combine(output, "ImportedMaterial.json"), UnityEditor.EditorJsonUtility.ToJson(stored, true));
                using (var target = new Target(camera))
                {
                    for (int mode = 0; mode < (trueStalker ? 3 : 2); mode++)
                    {
                        material.shader = mode == 0 ? baselineShader : currentShader;
                        if (mode != 0) material.SetFloat("_MonolithTrueStalkerSourceTerrain", trueStalker && mode == 2 ? 1 : 0);
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
                        foreach (int debug in trueStalker && mode == 2 ? new[] { 0, 12, 68, 30, 31, 32, 33, 34, 67, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84 } : new[] { 0, 12, 68, 67 })
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
                            if (mode == 0) baselinePixels[debug] = pixels;
                            else if (mode == 1 && baselinePixels.TryGetValue(debug, out Color[] baseline))
                            {
                                float maximum = 0;
                                for (int pixel = 0; pixel < pixels.Length; pixel++)
                                {
                                    for (int channel = 0; channel < 3; channel++) maximum = Mathf.Max(maximum, Mathf.Abs(pixels[pixel][channel] - baseline[pixel][channel]));
                                }
                                comparisons.AppendLine(mode + "," + debug + "," + maximum.ToString("R", CultureInfo.InvariantCulture) + "," + pixels.Length * 3);
                                File.WriteAllText(Path.Combine(output, "Frozen_AB.csv"), comparisons.ToString());
                                Assert.LessOrEqual(maximum, 0.00002f, "Frozen checkpoint changed, debug " + debug);
                            }
                            if (debug == 12) forward = pixels;
                            metrics.Append(mode).Append(',').Append(debug).Append(',').Append(nonFinite).Append(',').Append(delta.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                            Assert.AreEqual(0, nonFinite);
                            if (debug == 68) Assert.Less(delta, 0.02f);
                            target.Save(Path.Combine(output, "M" + mode + "_D" + debug));
                        }

                        material.SetFloat("_MonolithDebug", 0);
                        CustomSampler sampler = CustomSampler.Create("Phase05D_RealTerrain_" + mode, true);
                        Recorder recorder = sampler.GetRecorder();
                        bool previousProfiler = Profiler.enabled;
                        try
                        {
                            Profiler.enabled = true;
                            recorder.enabled = true;
                            for (int frame = 0; frame < 36; frame++)
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
                File.WriteAllText(Path.Combine(output, "Frozen_AB.csv"), comparisons.ToString());
                bindings?.Dispose();
                Assert.AreEqual(storedBytes, File.ReadAllText(materialPath));
                Assert.IsFalse(EditorUtility.IsDirty(stored));
                File.WriteAllText(Path.Combine(output, "GPU_Timings.csv"), timings.ToString());
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Shader FrozenShader()
        {
            string folder = Environment.GetEnvironmentVariable("MONOLITH_PHASE05D_BASELINE");
            Assert.IsNotEmpty(folder);
            folder = Path.Combine(folder, "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders");
            string source = Expand(Path.Combine(folder, "MonolithLit.shader"));
            source = source.Replace("Shader \"HDRP/MonolithLit\"", "Shader \"Hidden/XRay/Phase05D_FrozenMonolithLit\"");
            Assert.IsFalse(Regex.IsMatch(source, "#include \\\"Monolith"));
            File.WriteAllText(Path.Combine(TrueStalkerTerrainReference.Output(), "Frozen_Checkpoint.shader"), source);
            return ShaderUtil.CreateShaderAsset(source);
        }

        private static string Expand(string file)
        {
            string source = File.ReadAllText(file);
            return Regex.Replace(source, "#include \\\"(Monolith[^\\\"]+)\\\"", match => Expand(Path.Combine(Path.GetDirectoryName(file), match.Groups[1].Value)));
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

            public void Save(string path)
            {
                using (var writer = new BinaryWriter(File.Open(path + ".rgba32f", FileMode.CreateNew)))
                {
                    foreach (Color pixel in _pixels.GetPixels())
                    {
                        for (int channel = 0; channel < 4; channel++) writer.Write(pixel[channel]);
                    }
                }
                File.WriteAllBytes(path + ".exr", _pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                var display = new Texture2D(640, 360, TextureFormat.RGBA32, false, true);
                try
                {
                    display.SetPixels(_pixels.GetPixels());
                    display.Apply();
                    File.WriteAllBytes(path + ".png", display.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(display);
                }
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
