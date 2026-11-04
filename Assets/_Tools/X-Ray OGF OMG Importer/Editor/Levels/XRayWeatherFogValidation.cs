using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayWeatherFogValidation
    {
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\WeatherCompletion_20261104_024544";

        private static string _preparedScene;
        private static string _preparedSource;
        private static string _buildDirectory;
        private static string _buildStatus;
        private static string _buildOutcome;
        private static string _executableName;

        public static string RunEditor()
        {
            string output = NewOutput("Editor");
            File.WriteAllText(Path.Combine(output, "Before.json"), ReadState());
            var checks = new List<string>();
            Scene fixture = default;
            var owned = new List<UnityEngine.Object>();
            bool passed = false;
            string rendering = "not_started";
            try
            {
                RunNumeric(checks);
                if (XRayWeatherController.Active != null)
                {
                    rendering = "blocked_by_existing_weather_owner_preserved";
                }
                else
                {
                    fixture = EditorSceneManager.NewPreviewScene();
                    GameObject root = CreateFixture(fixture, null, owned, out Camera camera, out Volume volume);
                    var fogObject = new GameObject("Separate Weather Fog Volume");
                    fogObject.layer = 30;
                    fogObject.transform.SetParent(root.transform, false);
                    Volume fogVolume = fogObject.AddComponent<Volume>();
                    fogVolume.isGlobal = true;
                    fogVolume.priority = float.MaxValue;
                    var fogProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    fogProfile.Add<Fog>(false);
                    Persist(fogProfile, null, "Fog", owned);
                    fogVolume.sharedProfile = fogProfile;
                    XRayWeatherFogRenderer fog = root.AddComponent<XRayWeatherFogRenderer>();
                    fog.Configure(fogVolume);
                    XRayWeatherFogValidationProbe.RunSweep(camera, fog, root, volume, output);
                    fog.ReleaseWeather(root);
                    rendering = "passed";
                }
                passed = true;
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(output, "Failure.txt"), exception.ToString());
            }
            finally
            {
                if (fixture.IsValid() && fixture.isLoaded) EditorSceneManager.ClosePreviewScene(fixture);
                for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
                File.WriteAllText(Path.Combine(output, "After.json"), ReadState());
                File.WriteAllText(Path.Combine(output, "Result.json"), JsonUtility.ToJson(new EditorResult { passed = passed, rendering = rendering, checks = checks.ToArray(), api = SystemInfo.graphicsDeviceType.ToString(), unity = Application.unityVersion }, true));
            }
            return output;
        }

        public static string PreparePlayer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Fog Player preparation requires Edit Mode.");
            RequireCleanScenes();
            Scene previous = SceneManager.GetActiveScene();
            bool dirty = previous.isDirty;
            XRayWeatherController host = null;
            foreach (GameObject sceneRoot in previous.GetRootGameObjects())
            {
                foreach (XRayWeatherController candidate in sceneRoot.GetComponentsInChildren<XRayWeatherController>(true))
                {
                    if (host != null) throw new InvalidOperationException("Preserve the host with multiple weather controllers before preparing the validation scene.");
                    host = candidate;
                }
            }
            if (host == null) throw new InvalidOperationException("The current military weather controller is required to identify its imported profiles.");
            var hostSerialized = new SerializedObject(host);
            var hostProfile = hostSerialized.FindProperty("_profile").objectReferenceValue as XRayWeatherProfile;
            string profileFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(hostProfile))?.Replace('\\', '/');
            XRayWeatherProfile clear = AssetDatabase.LoadAssetAtPath<XRayWeatherProfile>(profileFolder + "/w_clear1.asset");
            XRayWeatherProfile rain = AssetDatabase.LoadAssetAtPath<XRayWeatherProfile>(profileFolder + "/w_rain1.asset");
            if (clear == null || rain == null || clear.SourceIdentity != rain.SourceIdentity || !XRayWeatherImporter.HasSerializedResources(clear) || !XRayWeatherImporter.HasSerializedResources(rain)) throw new InvalidDataException("The current imported clear/rain profiles with serialized resources are required for Player validation.");
            string folder = "Assets/_Source/Levels/WeatherFogValidation/NativeRun_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                GameObject root = CreateFixture(scene, folder, new List<UnityEngine.Object>(), out Camera camera, out Volume volume);
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, clear);
                controller.gameObject.layer = 0;
                if (!XRayLevelSceneBuilder.ConfigureWeatherSky(root, controller, clear, folder + "/Sky.asset")) throw new InvalidDataException("The fixture must connect native weather Sky through the production builder.");
                var fogVolume = new SerializedObject(controller.GetComponent<XRayWeatherFogRenderer>()).FindProperty("_volume").objectReferenceValue as Volume;
                if (fogVolume == null) throw new InvalidDataException("The native fog fixture requires the builder's dedicated Volume.");
                fogVolume.gameObject.layer = 30;
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("_paused").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                controller.ApplySettings();
                var probeObject = new GameObject("Weather Fog Validation Probe");
                SceneManager.MoveGameObjectToScene(probeObject, scene);
                _preparedSource = SourceHash();
                var controllerSerialized = new SerializedObject(controller);
                var sun = controllerSerialized.FindProperty("_sun").objectReferenceValue as Light;
                var lighting = controllerSerialized.FindProperty("_sourceLighting").objectReferenceValue as MonolithSourceLighting;
                lighting.BoundRadianceScale = 1.0f;
                Volume skyVolume = controller.transform.Find("Source Weather Sky").GetComponent<Volume>();
                XRayWeatherFogValidationProbe probe = probeObject.AddComponent<XRayWeatherFogValidationProbe>();
                probe.Configure(camera, controller, clear, rain, volume, _preparedSource);
                probe.ConfigureEnvironment(sun, lighting, skyVolume);
                _preparedScene = folder + "/WeatherFogValidation.unity";
                if (!EditorSceneManager.SaveScene(scene, _preparedScene)) throw new IOException("Unable to save the dedicated synthetic fog fixture.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            if (previous.isDirty != dirty) throw new InvalidOperationException("Fog preparation changed the host dirty state.");
            return _preparedScene;
        }

        public static string BuildPlayer()
        {
            RequireCleanScenes();
            if (_buildStatus == "queued" || _buildStatus == "running" || _buildStatus == "awaiting_restore") throw new InvalidOperationException("A fog validation build or HDRP restoration is already active.");
            if (string.IsNullOrEmpty(_preparedScene) || !_preparedScene.StartsWith("Assets/_Source/Levels/WeatherFogValidation/", StringComparison.Ordinal) || !File.Exists(_preparedScene) || _preparedSource != SourceHash()) PreparePlayer();
            return QueueBuild(NewOutput("PlayerBuild"), "WeatherFogValidation.exe");
        }

        internal static string BuildCelestialFixture(string scenePath, string archive)
        {
            RequireCleanScenes();
            if (_buildStatus == "queued" || _buildStatus == "running" || _buildStatus == "awaiting_restore") throw new InvalidOperationException("A validation build or HDRP restoration is already active.");
            if (!Path.GetFullPath(archive).StartsWith(@"E:\Projects\stalker_raid_into_darkness_Archive\", StringComparison.OrdinalIgnoreCase) || !scenePath.StartsWith("Assets/_Source/Levels/WeatherCelestialValidation/", StringComparison.Ordinal) || !File.Exists(scenePath)) throw new InvalidOperationException("Select the saved celestial validation fixture and its Archive output.");
            _preparedScene = scenePath;
            _preparedSource = SourceHash();
            string directory = Path.Combine(archive, "PlayerBuild_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(directory);
            return QueueBuild(directory, "WeatherCelestialValidation.exe");
        }

        private static string QueueBuild(string directory, string executableName)
        {
            _buildDirectory = directory;
            _executableName = executableName;
            _buildStatus = "queued";
            File.WriteAllText(Path.Combine(_buildDirectory, "Status.txt"), _buildStatus);
            EditorApplication.update += BuildPreparedPlayer;
            return _buildDirectory;
        }

        public static string ReadBuildStatus()
        {
            return _buildStatus + "\n" + _buildDirectory + "\n" + _preparedScene;
        }

        public static string RestoreCurrentBuild(string output = null)
        {
            if (_buildStatus == "queued" || _buildStatus == "running") throw new InvalidOperationException("Wait for the current Player build before restoring its HDRP state.");
            if (!string.IsNullOrEmpty(output)) _buildDirectory = Path.GetFullPath(output);
            if (string.IsNullOrEmpty(_buildDirectory) || !_buildDirectory.StartsWith(@"E:\Projects\stalker_raid_into_darkness_Archive\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Select the existing build evidence inside the project Archive.");
            string resultPath = Path.Combine(_buildDirectory, "Result.json");
            _buildOutcome = File.Exists(resultPath) ? JsonUtility.FromJson<BuildRecord>(File.ReadAllText(resultPath)).result : "failed";
            QueueBuildRestoration();
            return ReadBuildStatus();
        }

        private static void BuildPreparedPlayer()
        {
            EditorApplication.update -= BuildPreparedPlayer;
            EditorApplication.delayCall -= BuildPreparedPlayer;
            _buildStatus = "running";
            _buildOutcome = null;
            File.WriteAllText(Path.Combine(_buildDirectory, "Status.txt"), _buildStatus);
            try
            {
                RequireCleanScenes();
                if (_preparedSource != SourceHash()) throw new InvalidOperationException("Production runtime sources changed after fixture preparation; prepare a fresh fog Player fixture.");
                string executable = Path.Combine(_buildDirectory, "Build", _executableName);
                var hashes = new List<FileRecord>();
                var sources = new List<FileRecord>();
                foreach (string path in Directory.GetFiles("Assets/_Tools/X-Ray OGF OMG Importer/Runtime", "*", SearchOption.AllDirectories)) sources.Add(Hash(path));
                File.WriteAllText(Path.Combine(_buildDirectory, "Sources.json"), JsonUtility.ToJson(new FileRecords { files = sources.ToArray() }, true));
                foreach (string dependency in AssetDatabase.GetDependencies(_preparedScene, true))
                {
                    if (File.Exists(dependency)) hashes.Add(Hash(dependency));
                    if (File.Exists(dependency + ".meta")) hashes.Add(Hash(dependency + ".meta"));
                }
                File.WriteAllText(Path.Combine(_buildDirectory, "Dependencies.json"), JsonUtility.ToJson(new FileRecords { files = hashes.ToArray() }, true));
                File.WriteAllText(Path.Combine(_buildDirectory, "Before.json"), ReadState());
                Directory.CreateDirectory(Path.GetDirectoryName(executable));
                XRayWeatherCompletionBuildState.Capture(_buildDirectory);
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { _preparedScene }, locationPathName = executable, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
                _buildOutcome = report.summary.result.ToString();
                if (_preparedSource != SourceHash()) throw new InvalidOperationException("Production runtime sources changed during Player build; build evidence is not current.");
                File.WriteAllText(Path.Combine(_buildDirectory, "Result.json"), JsonUtility.ToJson(new BuildRecord { result = _buildOutcome, errors = report.summary.totalErrors, warnings = report.summary.totalWarnings, executable = executable, executableSha256 = File.Exists(executable) ? Hash(executable).sha256 : null, sourceSha256 = SourceHash(), scene = _preparedScene, graphicsApis = Array.ConvertAll(PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64), value => value.ToString()) }, true));
                File.WriteAllText(Path.Combine(_buildDirectory, "After.json"), ReadState());
            }
            catch (Exception exception)
            {
                _buildStatus = "failed";
                _buildOutcome = "failed";
                File.WriteAllText(Path.Combine(_buildDirectory, "Failure.txt"), exception.ToString());
            }
            finally
            {
                if (File.Exists(Path.Combine(_buildDirectory, "DefaultAssetState.json"))) QueueBuildRestoration();
            }
            File.WriteAllText(Path.Combine(_buildDirectory, "Status.txt"), _buildStatus);
        }

        private static void QueueBuildRestoration()
        {
            _buildStatus = "awaiting_restore";
            File.WriteAllText(Path.Combine(_buildDirectory, "Status.txt"), _buildStatus);
            EditorApplication.update -= RestorePreparedBuild;
            EditorApplication.update += RestorePreparedBuild;
        }

        private static void RestorePreparedBuild()
        {
            if (BuildPipeline.isBuildingPlayer) return;
            EditorApplication.update -= RestorePreparedBuild;
            string run = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                string proof = XRayWeatherCompletionBuildState.Restore(_buildDirectory);
                _buildStatus = string.IsNullOrEmpty(_buildOutcome) ? "failed" : _buildOutcome;
                File.WriteAllText(Path.Combine(_buildDirectory, "RestorationCompletion_" + run + ".json"), JsonUtility.ToJson(new BuildRestorationRecord { status = _buildStatus, proof = proof }, true));
            }
            catch (Exception exception)
            {
                _buildStatus = "failed";
                File.WriteAllText(Path.Combine(_buildDirectory, "RestorationFailure_" + run + ".txt"), exception.ToString());
            }
            File.WriteAllText(Path.Combine(_buildDirectory, "Status.txt"), _buildStatus);
        }

        private static void RequireCleanScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty) throw new InvalidOperationException("BLOCKED: preserve the unsaved scene manually before Test Runner or Player build: " + scene.path);
            }
        }

        internal static GameObject CreateFixture(Scene scene, string folder, List<UnityEngine.Object> owned, out Camera camera, out Volume volume)
        {
            var root = new GameObject("Weather Fog Validation Geometry");
            root.layer = 30;
            SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = new GameObject("Weather Fog Validation Camera");
            cameraObject.transform.SetParent(root.transform, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.cullingMask = 1 << 30;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 4.0f;
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000.0f;
            camera.useOcclusionCulling = false;
            camera.allowHDR = true;
            HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = new Color(0.015f, 0.025f, 0.04f, 1.0f);
            data.volumeLayerMask = 1 << 30;
            data.customRenderingSettings = true;
            foreach (FrameSettingsField field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.ContactShadows, FrameSettingsField.Volumetrics })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            foreach (FrameSettingsField field in new[] { FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl, FrameSettingsField.AtmosphericScattering })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            var environmentObject = new GameObject("Weather Fog Validation Environment");
            environmentObject.transform.SetParent(root.transform, false);
            environmentObject.layer = 30;
            volume = environmentObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10000.0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<VisualEnvironment>(true).skyType.Override(SkySettings.GetUniqueID<GradientSky>());
            GradientSky sky = profile.Add<GradientSky>(true);
            sky.top.Override(new Color(0.25f, 0.35f, 0.45f, 1.0f));
            sky.middle.Override(sky.top.value);
            sky.bottom.Override(sky.top.value);
            sky.skyIntensityMode.Override(SkyIntensityMode.Multiplier);
            sky.multiplier.Override(1.0f);
            profile.Add<Fog>(true).enabled.Override(false);
            Exposure exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(0.0f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
            profile.Add<Bloom>(true).intensity.Override(0.0f);
            volume.sharedProfile = profile;
            Persist(profile, folder, "Environment.asset", owned);
            var material = new Material(Shader.Find("HDRP/Unlit"));
            material.SetColor("_UnlitColor", new Color(0.12f, 0.08f, 0.04f, 1.0f));
            HDMaterial.ValidateMaterial(material);
            Persist(material, folder, "Opaque.mat", owned);
            Vector3[] positions = { new Vector3(-5.2f, 1.7f, 30.0f), new Vector3(-1.7f, 1.7f, 180.0f), new Vector3(1.7f, 1.7f, 280.0f), new Vector3(5.2f, 1.7f, 500.0f) };
            for (int i = 0; i < positions.Length; i++) Quad(root, "Known Distance " + i, positions[i], material);
            var checker = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            checker.filterMode = FilterMode.Point;
            checker.SetPixels(new[] { Color.white, Color.clear, Color.clear, Color.white });
            checker.Apply();
            Persist(checker, folder, "AlphaChecker.asset", owned);
            var cutout = new Material(material);
            cutout.SetTexture("_UnlitColorMap", checker);
            cutout.SetFloat("_AlphaCutoffEnable", 1.0f);
            cutout.SetFloat("_AlphaCutoff", 0.5f);
            HDMaterial.ValidateMaterial(cutout);
            Persist(cutout, folder, "Cutout.mat", owned);
            Quad(root, "Alpha-tested surface", new Vector3(-3.4f, -1.8f, 280.0f), cutout);
            var transparent = new Material(Shader.Find("HDRP/MonolithLit"));
            transparent.SetColor("_BaseColor", new Color(0.3f, 0.1f, 0.05f, 0.5f));
            transparent.SetFloat("_SurfaceType", 1.0f);
            transparent.SetFloat("_BlendMode", 0.0f);
            transparent.SetFloat("_EnableFogOnTransparent", 1.0f);
            HDMaterial.ValidateMaterial(transparent);
            Persist(transparent, folder, "Transparent.mat", owned);
            Quad(root, "Monolith transparent surface", new Vector3(3.4f, -1.8f, 280.0f), transparent);
            return root;
        }

        private static void Quad(GameObject root, string name, Vector3 position, Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.layer = 30;
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = position;
            quad.transform.localScale = new Vector3(2.8f, 2.7f, 1.0f);
            quad.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
        }

        private static void Persist(UnityEngine.Object asset, string folder, string name, List<UnityEngine.Object> owned)
        {
            if (folder == null)
            {
                owned.Add(asset);
                if (asset is VolumeProfile transient) foreach (VolumeComponent component in transient.components) owned.Add(component);
                return;
            }
            AssetDatabase.CreateAsset(asset, folder + "/" + name);
            if (asset is VolumeProfile profile) foreach (VolumeComponent component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        private static void RunNumeric(List<string> checks)
        {
            XRayWeatherSnapshot snapshot = XRayWeatherFogValidationProbe.Snapshot(0.5f, 400.0f, Color.gray);
            Require(XRayWeatherFogMath.TryGetParameters(snapshot, out Vector4 parameters), "Finite snapshot accepted", checks);
            Require(Mathf.Abs(snapshot.FogNear - 170.0f) < 0.0001f && Mathf.Abs(snapshot.FogFar - 396.0f) < 0.0001f, "Source near/far formulas", checks);
            Require(XRayWeatherFogMath.EvaluateFactor(0.0f, parameters) == 0.0f && XRayWeatherFogMath.EvaluateFactor(1000.0f, parameters) == 1.0f, "Near/far saturation", checks);
            Require(Mathf.Abs(XRayWeatherFogMath.EvaluateFactor(283.0f, parameters) - 0.5f) < 0.0001f, "Midpoint factor", checks);
            Require(XRayWeatherFogMath.TryGetMeanFreePath(snapshot, out float meanFreePath) && Mathf.Abs(meanFreePath - (170.0f + (1.0f - Mathf.Exp(-1.0f)) * 226.0f)) < 0.0001f, "Native exponential attenuation matches the source anchor", checks);
            Color tint = XRayWeatherFogMath.ToSkyTint(new Color(0.25f, 0.35f, 0.45f, 1.0f));
            Require(Mathf.Abs(tint.r - Mathf.Lerp(1.0f, Mathf.Pow(0.25f / 0.45f, 2.2f), 0.45f)) < 0.00001f && tint.b == 1.0f, "Native fog uses a bounded weather tint over sky radiance", checks);
            Require(XRayWeatherFogMath.TryGetParameters(XRayWeatherFogValidationProbe.Snapshot(1.2f, 400.0f, Color.gray), out _), "Above-one density remains valid", checks);
            Require(!XRayWeatherFogMath.TryGetParameters(XRayWeatherFogValidationProbe.Snapshot(-1.0f, 400.0f, Color.gray), out _), "Inverted interval rejected", checks);
            Require(!XRayWeatherFogMath.TryGetParameters(XRayWeatherFogValidationProbe.Snapshot(float.NaN, 400.0f, Color.gray), out _), "NaN rejected", checks);
            Require(!XRayWeatherFogMath.TryGetParameters(XRayWeatherFogValidationProbe.Snapshot(0.5f, float.PositiveInfinity, Color.gray), out _), "Infinity rejected", checks);
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            try
            {
                XRayWeatherSnapshot first = new XRayWeatherSnapshot("00:00:00", 0.0, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.0f, 0.0f, Color.gray, Color.gray, 0.2f, 800.0f, 1000.0f, Color.gray, Color.clear, 0.0f, default, default);
                XRayWeatherSnapshot second = new XRayWeatherSnapshot("12:00:00", 43200.0, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.0f, 0.0f, Color.gray, Color.gray, 1.2f, 800.0f, 1000.0f, Color.gray, Color.clear, 0.0f, default, default);
                profile.Initialize("fog_fixture", "synthetic", "numeric fog regression", new[] { first, second }, new Vector2[24], visibilityScale: 0.5f);
                Require(profile.TryEvaluate(21600.0, out XRayWeatherSnapshot sampled, out _), "Weather sampling succeeds", checks);
                Require(Mathf.Abs(sampled.FogDensity - 0.7f) < 0.0001f && sampled.FarPlane == 500.0f && sampled.FogDistance == 490.0f, "Interpolate then visibility once then distance clamp", checks);
                Require(profile.TryEvaluate(108000.0, out XRayWeatherSnapshot wrapped, out _) && Mathf.Abs(wrapped.FogNear - sampled.FogNear) < 0.0001f, "Midnight sampling wraps", checks);
                XRayWeatherSnapshot blended = XRayWeatherSnapshot.Blend(sampled, wrapped, 0.5f);
                Require(blended.FarPlane == 500.0f && blended.FogDistance == 490.0f, "Crossfade does not rescale visibility", checks);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        private static void Require(bool condition, string message, List<string> checks)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
        }

        private static string NewOutput(string category)
        {
            string path = Path.Combine(ARCHIVE, category + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            if (Directory.Exists(path)) throw new IOException("Select a new fog evidence directory.");
            Directory.CreateDirectory(path);
            return path;
        }

        internal static string SourceHash()
        {
            var paths = new List<string>(Directory.GetFiles("Assets/_Tools/X-Ray OGF OMG Importer/Runtime", "*", SearchOption.AllDirectories));
            paths.Sort(StringComparer.Ordinal);
            var manifest = new StringBuilder();
            foreach (string path in paths) manifest.Append(path.Replace('\\', '/')).Append(':').Append(Hash(path).sha256).Append('\n');
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(manifest.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        private static FileRecord Hash(string path)
        {
            using SHA256 hash = SHA256.Create();
            return new FileRecord { path = path, sha256 = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() };
        }

        public static string ReadState()
        {
            var result = new StateRecord();
            result.project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            result.unity = Application.unityVersion;
            result.api = SystemInfo.graphicsDeviceType.ToString();
            result.playing = EditorApplication.isPlaying;
            result.compiling = EditorApplication.isCompiling;
            result.weatherOwner = XRayWeatherController.Active == null ? null : XRayWeatherController.Active.gameObject.scene.path + ":" + XRayWeatherController.Active.name;
            result.graphicsApis = Array.ConvertAll(PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64), value => value.ToString());
            result.globalDefault = Profile(VolumeManager.instance.globalDefaultProfile);
            result.qualityDefault = Profile(VolumeManager.instance.qualityDefaultProfile);
            result.lastRenderedFog = FogState(VolumeManager.instance.stack.GetComponent<Fog>());
            var scenes = new List<SceneRecord>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                var record = new SceneRecord { path = scene.path, dirty = scene.isDirty, active = scene == SceneManager.GetActiveScene(), roots = scene.rootCount };
                var volumes = new List<VolumeRecord>();
                var cameras = new List<CameraRecord>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Volume volume in root.GetComponentsInChildren<Volume>(true))
                    {
                        volumes.Add(new VolumeRecord { name = volume.name, enabled = volume.enabled, active = volume.gameObject.activeInHierarchy, global = volume.isGlobal, priority = volume.priority, weight = volume.weight, layer = volume.gameObject.layer, shared = Profile(volume.sharedProfile), instantiated = volume.HasInstantiatedProfile(), current = volume.HasInstantiatedProfile() ? Profile(volume.profile) : null });
                    }
                    foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                    {
                        HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
                        cameras.Add(new CameraRecord { name = camera.name, enabled = camera.enabled, near = camera.nearClipPlane, far = camera.farClipPlane, mask = data != null ? data.volumeLayerMask.value : 0 });
                    }
                }
                record.volumes = volumes.ToArray();
                record.cameras = cameras.ToArray();
                scenes.Add(record);
            }
            result.scenes = scenes.ToArray();
            return JsonUtility.ToJson(result, true);
        }

        private static ProfileRecord Profile(VolumeProfile profile)
        {
            if (profile == null) return null;
            var components = new List<string>();
            foreach (VolumeComponent component in profile.components) if (component != null) components.Add(component.GetType().FullName);
            profile.TryGet(out Fog fog);
            return new ProfileRecord { name = profile.name, path = AssetDatabase.GetAssetPath(profile), json = EditorJsonUtility.ToJson(profile), components = components.ToArray(), fog = FogState(fog) };
        }

        private static string FogState(Fog fog)
        {
            return fog == null ? null : EditorJsonUtility.ToJson(fog);
        }

        [Serializable]
        private sealed class StateRecord
        {
            public string project;
            public string unity;
            public string api;
            public bool playing;
            public bool compiling;
            public string weatherOwner;
            public string[] graphicsApis;
            public ProfileRecord globalDefault;
            public ProfileRecord qualityDefault;
            public string lastRenderedFog;
            public SceneRecord[] scenes;
        }

        [Serializable]
        private sealed class SceneRecord
        {
            public string path;
            public bool dirty;
            public bool active;
            public int roots;
            public VolumeRecord[] volumes;
            public CameraRecord[] cameras;
        }

        [Serializable]
        private sealed class ProfileRecord
        {
            public string name;
            public string path;
            public string json;
            public string[] components;
            public string fog;
        }

        [Serializable]
        private sealed class VolumeRecord
        {
            public string name;
            public bool enabled;
            public bool active;
            public bool global;
            public float priority;
            public float weight;
            public int layer;
            public ProfileRecord shared;
            public bool instantiated;
            public ProfileRecord current;
        }

        [Serializable]
        private sealed class CameraRecord
        {
            public string name;
            public bool enabled;
            public float near;
            public float far;
            public int mask;
        }

        [Serializable]
        private sealed class EditorResult
        {
            public bool passed;
            public string rendering;
            public string[] checks;
            public string api;
            public string unity;
        }

        [Serializable]
        private sealed class FileRecord
        {
            public string path;
            public string sha256;
        }

        [Serializable]
        private sealed class FileRecords
        {
            public FileRecord[] files;
        }

        [Serializable]
        private sealed class BuildRecord
        {
            public string result;
            public int errors;
            public int warnings;
            public string executable;
            public string executableSha256;
            public string sourceSha256;
            public string scene;
            public string[] graphicsApis;
        }

        [Serializable]
        private sealed class BuildRestorationRecord
        {
            public string status;
            public string proof;
        }
    }
}
