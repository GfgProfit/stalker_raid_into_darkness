using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public static class MonolithTemporalCapture
    {
        private static Action _stop;
        private static string _status = "Idle";

        public static string Status()
        {
            return _status;
        }

        public static void Cancel()
        {
            _stop?.Invoke();
        }

        public static void Start(string directory, int diagnostic, float x, float y, float z, float pitch, float yaw, float roll)
        {
            if (_stop != null) throw new InvalidOperationException("A temporal capture is already running.");
            string output = Path.GetFullPath(directory);
            if (!output.StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Capture output must be inside Archive.");
            if (Directory.Exists(output)) throw new IOException("Use a unique capture directory.");
            Camera camera = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            if (camera == null) throw new InvalidOperationException("The loaded level requires a main camera.");
            Volume volume = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).Single(v => v.name == "Monolith Lighting (Phase 02)");
            volume.sharedProfile.TryGet(out GlobalIllumination gi);
            volume.sharedProfile.TryGet(out ScreenSpaceAmbientOcclusion ao);
            bool indirect = gi.enable.value;
            float ambientOcclusion = ao.intensity.value;
            int aoQuality = ao.quality.value;
            bool aoTemporal = ao.temporalAccumulation.value;
            float aoRadius = ao.radius.value;
            HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
            HDAdditionalCameraData.AntialiasingMode aa = data.antialiasing;
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            LightShadows[] shadows = lights.Select(l => l.shadows).ToArray();
            HDAdditionalLightData[] suns = lights.Where(l => l.type == LightType.Directional).Select(l => l.GetComponent<HDAdditionalLightData>()).Where(l => l != null).ToArray();
            float[] normalBias = suns.Select(l => l.normalBias).ToArray();
            float[] slopeBias = suns.Select(l => l.slopeBias).ToArray();
            MonolithSourceLighting sourceLighting = volume.GetComponent<MonolithSourceLighting>();
            Light[] boundLights = null;
            Vector3[] sourceColors = null;
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            Material[] materials = roots.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Concat(roots.SelectMany(g => g.GetComponentsInChildren<XRayLevelInstancedRenderer>(true)).Where(r => r.BatchSet != null).SelectMany(r => r.BatchSet.Batches).SelectMany(b => b.Materials)).Where(m => m != null && m.HasProperty("_MonolithFloraSubsurface")).Distinct().ToArray();
            float[] masks = materials.Select(m => m.GetFloat("_MonolithFloraSubsurface")).ToArray();
            float[] debug = materials.Select(m => m.GetFloat("_MonolithDebug")).ToArray();
            Vector4[] options = materials.Select(m => m.GetVector("_MonolithDiagnosticOptions")).ToArray();
            float[] gloss = materials.Select(m => m.GetFloat("_MonolithGlossScale")).ToArray();
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            float originalFov = camera.fieldOfView;
            RenderTexture previousTarget = camera.targetTexture;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf);
            var image = new Texture2D(1280, 720, TextureFormat.RGBAFloat, false, true);
            var display = new Texture2D(1280, 720, TextureFormat.RGB24, false, true);
            var poses = new List<string> { "frame,time,unityFrame,x,y,z,pitch,yaw,roll" };
            int frame = 0;
            double lastUpdate = 0;
            EditorApplication.CallbackFunction update = null;
            AssemblyReloadEvents.AssemblyReloadCallback cleanup = null;
            Directory.CreateDirectory(output);
            camera.targetTexture = target;
            camera.fieldOfView = 60;
            if (diagnostic == 3) gi.enable.value = false;
            if (diagnostic == 6) ao.intensity.value = 0;
            if (diagnostic == 7) foreach (Light light in lights) light.shadows = LightShadows.None;
            if (diagnostic == 12) data.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            if (diagnostic == 13 || diagnostic == 16) ao.temporalAccumulation.value = false;
            if (diagnostic == 14 || diagnostic == 16) ao.quality.value = (int)ScalableSettingLevelParameter.Level.High;
            if (diagnostic == 15) ao.radius.value = 0.5f;
            if (diagnostic == 78) foreach (Material material in materials) material.SetFloat("_MonolithFloraSubsurface", 0);
            if (diagnostic == 80) foreach (Material material in materials) material.SetVector("_MonolithDiagnosticOptions", new Vector4(0, 0, 1, 0));
            if (diagnostic == 81) foreach (Material material in materials) material.SetVector("_MonolithDiagnosticOptions", new Vector4(0, 0, 0, 1));
            if (diagnostic == 82) foreach (Material material in materials) material.SetFloat("_MonolithGlossScale", 0);
            if (diagnostic == 83) foreach (Material material in materials) material.SetVector("_MonolithDiagnosticOptions", new Vector4(0, 1, 0, 0));
            if (diagnostic == 63 || diagnostic == 69 || diagnostic == 85)
            {
                foreach (Material material in materials) material.SetFloat("_MonolithDebug", diagnostic == 85 ? 69 : diagnostic);
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            }
            if (diagnostic == 86 || diagnostic == 87)
            {
                foreach (HDAdditionalLightData sun in suns)
                {
                    sun.normalBias = diagnostic == 86 ? 0.05f : 0;
                    sun.slopeBias = diagnostic == 86 ? 0.1f : 0;
                }
            }
            if (diagnostic == 88)
            {
                var serialized = new SerializedObject(sourceLighting);
                SerializedProperty lightArray = serialized.FindProperty("_lights");
                SerializedProperty colorArray = serialized.FindProperty("_sourceColors");
                boundLights = new Light[lightArray.arraySize];
                sourceColors = new Vector3[colorArray.arraySize];
                var referenceColors = new Vector3[colorArray.arraySize];
                for (int i = 0; i < boundLights.Length; i++)
                {
                    boundLights[i] = lightArray.GetArrayElementAtIndex(i).objectReferenceValue as Light;
                    sourceColors[i] = colorArray.GetArrayElementAtIndex(i).vector3Value;
                    referenceColors[i] = boundLights[i].type == LightType.Directional ? new Vector3(boundLights[i].color.r, boundLights[i].color.g, boundLights[i].color.b) : sourceColors[i];
                }
                sourceLighting.SetBindings(boundLights, referenceColors);
            }
            if (diagnostic == 17)
            {
                ao.quality.value = (int)ScalableSettingLevelParameter.Level.High;
                ao.radius.value = 0.5f;
                ao.temporalAccumulation.value = true;
            }
            File.WriteAllText(Path.Combine(output, "Settings.txt"), FormattableString.Invariant($"AO quality={ao.quality.value}; fullResolution={ao.fullResolution}; steps={ao.stepCount}; directions={ao.directionCount}; temporal={ao.temporalAccumulation.value}; radius={ao.radius.value}; AA={data.antialiasing}; GI={gi.enable.value}"));
            cleanup = () =>
            {
                EditorApplication.update -= update;
                AssemblyReloadEvents.beforeAssemblyReload -= cleanup;
                if (camera != null)
                {
                    camera.targetTexture = previousTarget;
                    camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                    camera.fieldOfView = originalFov;
                }
                if (data != null) data.antialiasing = aa;
                if (gi != null) gi.enable.value = indirect;
                if (ao != null) ao.intensity.value = ambientOcclusion;
                if (ao != null) ao.quality.value = aoQuality;
                if (ao != null) ao.temporalAccumulation.value = aoTemporal;
                if (ao != null) ao.radius.value = aoRadius;
                for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].shadows = shadows[i];
                for (int i = 0; i < suns.Length; i++)
                {
                    if (suns[i] == null) continue;
                    suns[i].normalBias = normalBias[i];
                    suns[i].slopeBias = slopeBias[i];
                }
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    materials[i].SetFloat("_MonolithFloraSubsurface", masks[i]);
                    materials[i].SetFloat("_MonolithDebug", debug[i]);
                    materials[i].SetVector("_MonolithDiagnosticOptions", options[i]);
                    materials[i].SetFloat("_MonolithGlossScale", gloss[i]);
                }
                if (boundLights != null && sourceLighting != null) sourceLighting.SetBindings(boundLights, sourceColors);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(display);
                File.WriteAllLines(Path.Combine(output, "Camera_Motion.csv"), poses);
                _stop = null;
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            };
            update = () =>
            {
                if (EditorApplication.timeSinceStartup - lastUpdate < 1.0 / 30.0) return;
                lastUpdate = EditorApplication.timeSinceStartup;
                try
                {
                    float angle = frame < 8 ? 0 : (frame - 8) / 32.0f * Mathf.PI * 2;
                    Vector3 position = new Vector3(x, y, z) + new Vector3(Mathf.Sin(angle) * 0.12f, 0, (1 - Mathf.Cos(angle)) * 0.12f);
                    Vector3 rotation = new Vector3(pitch + Mathf.Sin(angle * 2) * 4, yaw + Mathf.Sin(angle) * (diagnostic == 84 || diagnostic == 85 ? 150 : 14), roll);
                    camera.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
                    camera.Render();
                    poses.Add(FormattableString.Invariant($"{frame},{lastUpdate:R},{Time.frameCount},{position.x:R},{position.y:R},{position.z:R},{rotation.x:R},{rotation.y:R},{rotation.z:R}"));
                    if (frame >= 8 && frame % 4 == 0) WriteFrame(target, image, display, Path.Combine(output, "Frame_" + frame.ToString("D3")));
                    frame++;
                    _status = "Running " + frame + "/40";
                    if (frame >= 40)
                    {
                        _status = "Completed " + output;
                        cleanup();
                    }
                    else
                    {
                        EditorApplication.QueuePlayerLoopUpdate();
                    }
                }
                catch (Exception exception)
                {
                    _status = "Failed: " + exception;
                    cleanup();
                    Debug.LogException(exception);
                }
            };
            _status = "Running 0/40";
            _stop = cleanup.Invoke;
            AssemblyReloadEvents.beforeAssemblyReload += cleanup;
            EditorApplication.update += update;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void WriteFrame(RenderTexture target, Texture2D image, Texture2D display, string path)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path + ".exr", image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP));
                Color[] colors = image.GetPixels();
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(colors[i].r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(colors[i].g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(colors[i].b)), 1);
                display.SetPixels(colors);
                display.Apply();
                File.WriteAllBytes(path + ".png", display.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }
}
