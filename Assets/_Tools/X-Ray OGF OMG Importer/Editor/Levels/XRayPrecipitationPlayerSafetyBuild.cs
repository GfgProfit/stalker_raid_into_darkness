using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayPrecipitationPlayerSafetyBuild
    {
        private const string PRODUCTION = "E:/Projects/stalker_raid_into_darkness/Assets";
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";

        public static string Prepare(string archive)
        {
            if (!Path.GetFullPath(archive).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(archive)) throw new InvalidOperationException("Use the existing task Archive.");
            if (!string.Equals(Application.dataPath, PRODUCTION, StringComparison.OrdinalIgnoreCase) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("Use the idle Production Editor.");
            Scene source = SceneManager.GetActiveScene();
            if (!source.path.EndsWith("/l07_military/Scenes/l07_military.unity", StringComparison.Ordinal)) throw new InvalidOperationException("The existing military scene identifies the real saved asset references.");
            for (int index = 0; index < SceneManager.sceneCount; index++) if (SceneManager.GetSceneAt(index).isDirty) throw new InvalidOperationException("Preserve the dirty scene before preparing a standalone fixture.");
            var host = new XRayWeatherCelestialValidation.HostScope();
            string before = XRayWeatherCelestialValidation.InspectHost();
            GameObject[] originals = source.GetRootGameObjects();
            var activeBefore = new bool[originals.Length];
            var enabledBefore = new bool[originals.Length][];
            for (int index = 0; index < originals.Length; index++)
            {
                activeBefore[index] = originals[index].activeSelf;
                Behaviour[] behaviours = originals[index].GetComponentsInChildren<Behaviour>(true);
                enabledBefore[index] = Array.ConvertAll(behaviours, item => item != null && item.enabled);
            }
            string folder = "Assets/_Source/Levels/WeatherCelestialValidation/Phase08Safety_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string scenePath = folder + "/PrecipitationMilitarySafety.unity";
            Scene fixture = default;
            try
            {
                host.Isolate();
                Directory.CreateDirectory(folder);
                AssetDatabase.ImportAsset(folder);
                fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var staging = new GameObject("Inactive safety fixture staging");
                SceneManager.MoveGameObjectToScene(staging, fixture);
                staging.SetActive(false);
                var roots = new List<GameObject>();
                var active = new List<bool>();
                for (int index = 0; index < originals.Length; index++)
                {
                    GameObject original = originals[index];
                    GameObject clone = Object.Instantiate(original, staging.transform, true);
                    clone.name = original.name;
                    roots.Add(clone);
                    active.Add(activeBefore[index]);
                    clone.SetActive(false);
                    Behaviour[] behaviours = clone.GetComponentsInChildren<Behaviour>(true);
                    if (behaviours.Length != enabledBefore[index].Length) throw new InvalidOperationException("The inactive clone must preserve the authored behaviour layout.");
                    for (int behaviour = 0; behaviour < behaviours.Length; behaviour++) if (behaviours[behaviour] != null) behaviours[behaviour].enabled = enabledBefore[index][behaviour];
                    clone.transform.SetParent(null, true);
                }
                Object.DestroyImmediate(staging);
                Camera camera = Find<Camera>(fixture);
                XRayWeatherController controller = Find<XRayWeatherController>(fixture);
                camera.scene = fixture;
                camera.targetTexture = null;
                camera.enabled = true;
                camera.allowDynamicResolution = false;
                camera.aspect = 1920.0f / 1080.0f;
                HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
                if (data == null) throw new InvalidOperationException("The authored camera requires HDRP camera data.");
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                data.dithering = false;
                data.customRenderingSettings = true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.CustomPass, true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.CustomPass] = true;
                var probeObject = new GameObject("Phase08 Standalone Native Safety Probe");
                SceneManager.MoveGameObjectToScene(probeObject, fixture);
                var probe = probeObject.AddComponent<XRayPrecipitationPlayerSafetyProbe>();
                probe.Configure(camera, controller, XRayWeatherFogValidation.SourceHash());
                probe.ConfigureRoots(roots.ToArray(), active.ToArray());
                if (!EditorSceneManager.SaveScene(fixture, scenePath)) throw new IOException("Cannot save the task-owned safety fixture.");
                File.WriteAllText(Path.Combine(archive, "SafetyFixture_" + Path.GetFileName(folder) + ".txt"), scenePath);
            }
            finally
            {
                if (fixture.IsValid() && fixture.isLoaded) EditorSceneManager.CloseScene(fixture, true);
                host.Restore();
                string after = XRayWeatherCelestialValidation.InspectHost();
                string proof = Path.Combine(archive, "SafetyPrepareHost_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".json");
                File.WriteAllText(proof, after);
                if (before != after) throw new InvalidOperationException("Preparing the standalone fixture changed the user host.");
            }
            return scenePath;
        }

        public static string Build(string scenePath, string archive)
        {
            return XRayWeatherFogValidation.BuildCelestialFixture(scenePath, archive);
        }

        public static string InspectHeight(string archive)
        {
            if (!Path.GetFullPath(archive).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(archive)) throw new InvalidOperationException("Use the existing task Archive.");
            Shader shader = GameConfig.Load().Precipitation.HeightShader;
            if (shader == null) throw new InvalidOperationException("The shared height shader is required.");
            var report = new HeightReport();
            ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            foreach (int compiler in new[] { 4, 28 })
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (bool instancing in new[] { false, true })
                    {
                        foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            ShaderData.VariantCompileInfo variant = subshader.GetPass(pass).CompileVariant(stage, instancing ? new[] { "INSTANCING_ON" } : Array.Empty<string>(), (ShaderCompilerPlatform)compiler, BuildTarget.StandaloneWindows64);
                            var messages = new List<string>();
                            if (variant.Messages != null) foreach (ShaderMessage message in variant.Messages) messages.Add(message.severity + ": " + message.message);
                            report.variants.Add(new HeightVariant { compiler = compiler, pass = pass, instancing = instancing, stage = stage.ToString(), success = variant.Success, bytecodeBytes = variant.ShaderData != null ? variant.ShaderData.Length : 0, messages = messages.ToArray() });
                            report.passed &= variant.Success;
                        }
                    }
                }
            }
            string path = Path.Combine(archive, "HeightCpuCompile_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return path;
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            T result = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T item in root.GetComponentsInChildren<T>(true))
                {
                    if (result != null) throw new InvalidOperationException("Expected one " + typeof(T).Name + " in the safety scene.");
                    result = item;
                }
            }
            if (result == null) throw new InvalidOperationException("Missing " + typeof(T).Name + " in the safety scene.");
            return result;
        }

        [Serializable]
        private sealed class HeightReport
        {
            public bool passed = true;
            public List<HeightVariant> variants = new List<HeightVariant>();
        }

        [Serializable]
        private sealed class HeightVariant
        {
            public int compiler;
            public int pass;
            public bool instancing;
            public string stage;
            public bool success;
            public int bytecodeBytes;
            public string[] messages;
        }
    }
}
