using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayPrecipitationValidation
    {
        private const string PRODUCTION = "E:/Projects/stalker_raid_into_darkness/Assets";
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";

        private static string _queuedOutput;

        public static string BeginMilitary(string archive)
        {
            Guard(archive);
            if (!string.IsNullOrEmpty(_queuedOutput)) throw new InvalidOperationException("A precipitation capture is already queued.");
            string output = NewOutput(archive, "MilitaryRain");
            File.WriteAllText(Path.Combine(output, "Status.txt"), "queued");
            _queuedOutput = output;
            EditorApplication.update += RunQueuedMilitary;
            return output;
        }

        private static void RunQueuedMilitary()
        {
            EditorApplication.update -= RunQueuedMilitary;
            string output = _queuedOutput;
            _queuedOutput = null;
            try
            {
                RunMilitary(output);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(output, "QueueFailure.txt"), exception.ToString());
                File.WriteAllText(Path.Combine(output, "Status.txt"), "failed");
            }
        }

        public static string InspectResources(string archive)
        {
            Guard(archive);
            string output = NewOutput(archive, "ResourceValidation");
            PrecipitationConfig config = GameConfig.Load().Precipitation;
            var errors = new List<string>();
            if (!config.TryValidate(out string diagnostic)) errors.Add(diagnostic);
            foreach (Shader shader in new[] { config.RainShader, config.HeightShader, Shader.Find("HDRP/MonolithLit") })
            {
                if (shader == null || !shader.isSupported) errors.Add("Missing or unsupported precipitation shader.");
                if (shader == null) continue;
                foreach (var message in ShaderUtil.GetShaderMessages(shader)) if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add(shader.name + ":" + message.message);
            }
            if (config.RainCompute == null || !config.RainCompute.HasKernel("InitializeDrops") || !config.RainCompute.HasKernel("SimulateDrops")) errors.Add("Missing rain simulation kernels.");
            if (config.RainCompute != null) foreach (var message in ShaderUtil.GetComputeShaderMessages(config.RainCompute)) if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add(config.RainCompute.name + ":" + message.message);
            var result = new ResourceRecord { passed = errors.Count == 0, errors = errors.ToArray(), root = AssetDatabase.GetAssetPath(GameConfig.Load()), configuration = AssetDatabase.GetAssetPath(config), rainTexture = AssetDatabase.GetAssetPath(config.RainTexture), splashTexture = AssetDatabase.GetAssetPath(config.SplashTexture), rainShaderSupported = config.RainShader != null && config.RainShader.isSupported, heightShaderSupported = config.HeightShader != null && config.HeightShader.isSupported };
            string path = Path.Combine(output, "Result.json");
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
            return path;
        }

        public static string SourceParity(string archive)
        {
            Guard(archive);
            string output = NewOutput(archive, "SourceParity");
            string[] rows = File.ReadAllLines(Path.Combine(archive, "Source", "WeatherNumericKeys.csv"));
            var actual = new Dictionary<string, XRayWeatherSnapshot>(StringComparer.Ordinal);
            GameConfig config = GameConfig.Load();
            foreach (XRayWeatherProfile profile in config.OrdinaryWeatherCatalog.Profiles)
            {
                for (int index = 0; index < profile.KeyframeCount; index++)
                {
                    XRayWeatherSnapshot frame = profile.GetKeyframe(index);
                    actual.Add("Ordinary|" + profile.CycleName + "|" + frame.Key0, frame);
                }
            }
            foreach (XRayWeatherFxProfile profile in config.WeatherFxCatalog.Effects)
            {
                for (int index = 0; index < profile.KeyframeCount; index++)
                {
                    XRayWeatherSnapshot frame = profile.GetKeyframe(index);
                    actual.Add("WeatherFX|" + profile.EffectName + "|" + frame.Key0, frame);
                }
            }
            int ordinary = 0;
            int fx = 0;
            int raining = 0;
            int rawClamped = 0;
            var failures = new List<string>();
            for (int index = 1; index < rows.Length; index++)
            {
                MatchCollection matches = Regex.Matches(rows[index], "\"((?:[^\"]|\"\")*)\"");
                if (matches.Count != 11) throw new InvalidDataException("Unexpected source CSV row " + index);
                var cells = new string[matches.Count];
                for (int cell = 0; cell < cells.Length; cell++) cells[cell] = matches[cell].Groups[1].Value.Replace("\"\"", "\"");
                string key = cells[0] + "|" + cells[1] + "|" + cells[2];
                if (!actual.TryGetValue(key, out XRayWeatherSnapshot frame))
                {
                    failures.Add("Missing " + key);
                    continue;
                }
                float rawDensity = Number(cells[3]);
                string[] color = cells[4].Split(',');
                bool passed = Near(frame.RainDensity, Mathf.Clamp01(rawDensity)) && Near(frame.RainColor.r, Number(color[0])) && Near(frame.RainColor.g, Number(color[1])) && Near(frame.RainColor.b, Number(color[2])) && Near(frame.WindVelocity, Number(cells[5])) && Near(frame.WindDirectionRadians, Number(cells[6]) * Mathf.Deg2Rad) && Near(frame.WaterIntensity, Number(cells[7])) && string.Equals(frame.ThunderboltCollection, cells[8], StringComparison.Ordinal) && Near(frame.ThunderboltPeriod, Number(cells[9]));
                if (!passed) failures.Add("Value mismatch " + key);
                if (cells[0] == "Ordinary") ordinary++;
                else fx++;
                if (frame.RainDensity > 0.0f) raining++;
                if (rawDensity > 1.0f) rawClamped++;
            }
            string path = Path.Combine(output, "Result.json");
            File.WriteAllText(path, JsonUtility.ToJson(new ParityRecord { passed = failures.Count == 0 && ordinary == 600 && fx == 112 && actual.Count == 712, ordinary = ordinary, fx = fx, raining = raining, rawClamped = rawClamped, failures = failures.ToArray() }, true));
            return path;
        }

        public static string PreparePlayer(string archive)
        {
            Guard(archive);
            var host = new XRayWeatherCelestialValidation.HostScope();
            string before = XRayWeatherCelestialValidation.InspectHost();
            Scene source = SceneManager.GetActiveScene();
            string folder = "Assets/_Source/Levels/WeatherCelestialValidation/Phase08_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string path = folder + "/PrecipitationMilitary.unity";
            Scene fixture = default;
            try
            {
                host.Isolate();
                Directory.CreateDirectory(folder);
                AssetDatabase.ImportAsset(folder);
                fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                foreach (GameObject root in source.GetRootGameObjects())
                {
                    GameObject original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(root);
                    GameObject clone = original != null ? (GameObject)PrefabUtility.InstantiatePrefab(original, fixture) : Object.Instantiate(root);
                    if (clone.scene != fixture) SceneManager.MoveGameObjectToScene(clone, fixture);
                    clone.name = root.name;
                }
                XRayWeatherController controller = Find<XRayWeatherController>(fixture);
                Camera camera = Find<Camera>(fixture);
                PrepareCamera(camera);
                camera.enabled = true;
                var probeObject = new GameObject("Phase08 Precipitation Player Validation");
                SceneManager.MoveGameObjectToScene(probeObject, fixture);
                probeObject.AddComponent<XRayPrecipitationValidationProbe>().Configure(camera, controller, XRayWeatherFogValidation.SourceHash());
                if (!EditorSceneManager.SaveScene(fixture, path)) throw new IOException("Cannot save the task-owned Player fixture.");
                File.WriteAllText(Path.Combine(archive, "PlayerFixture_" + Path.GetFileName(folder) + ".txt"), path);
            }
            finally
            {
                if (fixture.IsValid() && fixture.isLoaded) EditorSceneManager.CloseScene(fixture, true);
                host.Restore();
                string after = XRayWeatherCelestialValidation.InspectHost();
                File.WriteAllText(Path.Combine(archive, "PreparePlayerHost_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".json"), after);
                if (before != after) throw new InvalidOperationException("The Player fixture changed the military host state.");
            }
            return path;
        }

        public static string Build(string scenePath, string archive)
        {
            Guard(archive);
            return XRayWeatherFogValidation.BuildCelestialFixture(scenePath, archive);
        }

        private static void RunMilitary(string output)
        {
            var host = new XRayWeatherCelestialValidation.HostScope();
            string before = XRayWeatherCelestialValidation.InspectHost();
            File.WriteAllText(Path.Combine(output, "HostBefore.json"), before);
            GameObject temporary = null;
            bool passed = false;
            string failure = null;
            try
            {
                host.BeginHostPreview();
                Scene scene = SceneManager.GetActiveScene();
                Camera authored = Find<Camera>(scene);
                temporary = new GameObject("Phase08 Military Evidence Camera");
                temporary.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(temporary, scene);
                Camera camera = temporary.AddComponent<Camera>();
                camera.CopyFrom(authored);
                camera.transform.SetPositionAndRotation(authored.transform.position, authored.transform.rotation);
                authored.GetComponent<HDAdditionalCameraData>().CopyTo(camera.GetComponent<HDAdditionalCameraData>() ?? temporary.AddComponent<HDAdditionalCameraData>());
                PrepareCamera(camera);
                camera.scene = scene;
                camera.enabled = false;
                var stack = VolumeManager.instance.CreateStack();
                try
                {
                    VolumeManager.instance.Update(stack, camera.transform, camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask);
                    Exposure exposure = stack.GetComponent<Exposure>();
                    File.WriteAllText(Path.Combine(output, "Exposure.json"), JsonUtility.ToJson(new ExposureRecord { fixedExposure = exposure.fixedExposure.value, mode = exposure.mode.value.ToString(), scene = scene.path, cameraMask = camera.GetComponent<HDAdditionalCameraData>().volumeLayerMask.value }, true));
                    if (exposure.mode.value != ExposureMode.Fixed || !Near(exposure.fixedExposure.value, 9.0f)) throw new InvalidOperationException("Use the authored military fixed EV9.");
                }
                finally
                {
                    VolumeManager.instance.DestroyStack(stack);
                }
                string path = XRayPrecipitationValidationProbe.RunRainCases(camera, host.Controller, output);
                passed = JsonUtility.FromJson<XRayPrecipitationValidationProbe.RainResult>(File.ReadAllText(path)).passed;
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
                File.WriteAllText(Path.Combine(output, "HostFailure.txt"), failure);
            }
            finally
            {
                if (temporary != null) Object.DestroyImmediate(temporary);
                host.Restore();
                string after = XRayWeatherCelestialValidation.InspectHost();
                File.WriteAllText(Path.Combine(output, "HostAfter.json"), after);
                File.WriteAllText(Path.Combine(output, "Completion.json"), JsonUtility.ToJson(new CompletionRecord { passed = passed && before == after, renderPassed = passed, hostRestored = before == after, error = failure }, true));
                File.WriteAllText(Path.Combine(output, "Status.txt"), "completed");
            }
        }

        private static void PrepareCamera(Camera camera)
        {
            camera.targetTexture = null;
            camera.allowDynamicResolution = false;
            camera.aspect = 1920.0f / 1080.0f;
            HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>() ?? camera.gameObject.AddComponent<HDAdditionalCameraData>();
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            data.dithering = false;
            data.customRenderingSettings = true;
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.CustomPass, true);
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.CustomPass] = true;
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            T found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T candidate in root.GetComponentsInChildren<T>(true))
                {
                    if (found != null) throw new InvalidOperationException("Expected one " + typeof(T).Name);
                    found = candidate;
                }
            }
            if (found == null) throw new InvalidOperationException("Missing " + typeof(T).Name);
            return found;
        }

        private static void Guard(string archive)
        {
            if (!Path.GetFullPath(archive).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(archive)) throw new InvalidOperationException("Use the existing task Archive.");
            if (!string.Equals(Application.dataPath, PRODUCTION, StringComparison.OrdinalIgnoreCase) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("Use the idle Production Editor.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.path.EndsWith("/l07_military/Scenes/l07_military.unity", StringComparison.Ordinal)) throw new InvalidOperationException("Keep the military scene active.");
            for (int index = 0; index < SceneManager.sceneCount; index++) if (SceneManager.GetSceneAt(index).isDirty) throw new InvalidOperationException("Preserve the dirty scene before validation.");
        }

        private static string NewOutput(string archive, string name)
        {
            string path = Path.Combine(archive, name + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(path);
            return path;
        }

        private static float Number(string value)
        {
            return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static bool Near(float a, float b)
        {
            return Mathf.Abs(a - b) <= 0.00001f;
        }

        [Serializable]
        private sealed class ResourceRecord
        {
            public bool passed;
            public string[] errors;
            public string root;
            public string configuration;
            public string rainTexture;
            public string splashTexture;
            public bool rainShaderSupported;
            public bool heightShaderSupported;
        }

        [Serializable]
        private sealed class ParityRecord
        {
            public bool passed;
            public int ordinary;
            public int fx;
            public int raining;
            public int rawClamped;
            public string[] failures;
        }

        [Serializable]
        private sealed class ExposureRecord
        {
            public float fixedExposure;
            public string mode;
            public string scene;
            public int cameraMask;
        }

        [Serializable]
        private sealed class CompletionRecord
        {
            public bool passed;
            public bool renderPassed;
            public bool hostRestored;
            public string error;
        }
    }
}
