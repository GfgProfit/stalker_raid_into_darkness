using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayWeatherSkySetup
    {
        public const string ASSET_FOLDER = "Assets/_Source/Levels/WeatherProfiles/Phase07B";
        public const string CLEAR_PATH = ASSET_FOLDER + "/w_clear1.asset";
        public const string RAIN_PATH = ASSET_FOLDER + "/w_rain1.asset";
        public const string SCENE_PATH = ASSET_FOLDER + "/WeatherSkyPreview.unity";
        public const string VOLUME_PATH = ASSET_FOLDER + "/WeatherSkyVolume.asset";
        public const float DEFAULT_NIGHT_DIFFUSE_MULTIPLIER = 19.2f;

        public static readonly Color DEFAULT_NIGHT_AMBIENT = new Color(0.003f, 0.009f, 0.025f, 1.0f);

        public static VolumeProfile GetOrCreateSkyProfile(XRayWeatherProfile weather, float hour, Shader shader, GradientSky authored, VolumeProfile authoredProfile, string assetPath)
        {
            if (!XRayWeatherImporter.HasSerializedResources(weather)) throw new InvalidDataException("Weather sky setup requires serialized sky and environment cubemaps.");
            if (authored == null || shader == null || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Weather sky setup requires an authored Gradient Sky, shader and explicit asset destination.");
            VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(assetPath);
            if (existing != null)
            {
                if (existing.components.Count != 2 || !existing.TryGet(out XRayWeatherSkySettings sky) || !existing.TryGet(out VisualEnvironment visual) || sky.Shader != shader || visual.skyType.value != XRayWeatherSkySettings.SKY_ID) throw new InvalidDataException("Preserve the incompatible weather sky profile before connecting it: " + assetPath);
                return existing;
            }
            if (File.Exists(assetPath)) throw new IOException("The weather sky profile destination is already occupied: " + assetPath);
            if (!weather.TryEvaluate(hour * 3600.0, out XRayWeatherSnapshot snapshot, out string diagnostic)) throw new InvalidDataException(diagnostic);
            XRayLevelPaths.EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = Path.GetFileNameWithoutExtension(assetPath);
            XRayWeatherSkySettings settings = profile.Add<XRayWeatherSkySettings>(false);
            settings.SetWeather(snapshot, shader);
            settings.skyIntensityMode.Override(authored.skyIntensityMode.value);
            settings.exposure.Override(authored.exposure.value);
            settings.multiplier.Override(authored.multiplier.value);
            settings.upperHemisphereLuxValue.Override(authored.upperHemisphereLuxValue.value);
            settings.desiredLuxValue.Override(authored.desiredLuxValue.value);
            settings.SetHdrpIntensity(true);
            settings.SetNightAmbient(DEFAULT_NIGHT_AMBIENT);
            settings.SetNightDiffuseMultiplier(DEFAULT_NIGHT_DIFFUSE_MULTIPLIER);
            VisualEnvironment environment = profile.Add<VisualEnvironment>(false);
            environment.skyType.Override(XRayWeatherSkySettings.SKY_ID);
            if (authoredProfile != null && authoredProfile.TryGet(out VisualEnvironment sourceEnvironment))
            {
                environment.skyAmbientMode.value = sourceEnvironment.skyAmbientMode.value;
                environment.skyAmbientMode.overrideState = sourceEnvironment.skyAmbientMode.overrideState;
            }
            AssetDatabase.CreateAsset(profile, assetPath);
            foreach (VolumeComponent component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        [MenuItem("Tools/X-Ray/Weather/Create Phase07B Weather Sky Preview")]
        public static void CreateDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Create the weather preview outside Play Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty) throw new InvalidOperationException("Preserve unsaved scene changes before creating the dedicated weather preview: " + scene.path);
            }
            if (Directory.Exists(ASSET_FOLDER) || AssetDatabase.IsValidFolder(ASSET_FOLDER)) throw new IOException("Weather sky setup preserves existing assets; the dedicated destination already exists: " + ASSET_FOLDER);
            Shader shader = Shader.Find("XRay/Monolith Weather Sky");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("The X-Ray weather sky shader must compile before creating the preview.");
            if (UnityEngine.Object.FindFirstObjectByType<MonolithWeatherInputs>() != null || UnityEngine.Object.FindFirstObjectByType<MonolithSourceLighting>() != null) throw new InvalidOperationException("Create the weather preview from a clean empty host scene so active authored environment providers remain untouched.");
            XRayWeatherProfile clear = XRayWeatherImporter.ImportCurrent("w_clear1", CLEAR_PATH, ASSET_FOLDER + "/Textures");
            XRayWeatherImporter.ImportCurrent("w_rain1", RAIN_PATH, ASSET_FOLDER + "/Textures");
            var volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volumeProfile.Add<Fog>(true).enabled.Override(false);
            Exposure exposure = volumeProfile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(0.0f);
            volumeProfile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
            AssetDatabase.CreateAsset(volumeProfile, VOLUME_PATH);
            foreach (VolumeComponent component in volumeProfile.components) AssetDatabase.AddObjectToAsset(component, volumeProfile);
            EditorUtility.SetDirty(volumeProfile);
            AssetDatabase.SaveAssetIfDirty(volumeProfile);
            Scene originalActive = SceneManager.GetActiveScene();
            Scene demo = default;
            try
            {
                demo = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(demo);
                var root = new GameObject("Phase07B Weather Sky");
                var cameraObject = new GameObject("Weather Sky Camera");
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetPositionAndRotation(new Vector3(0.0f, 1.8f, 0.0f), Quaternion.Euler(-12.0f, 0.0f, 0.0f));
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 65.0f;
                camera.aspect = 16.0f / 9.0f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1000.0f;
                camera.allowHDR = true;
                camera.useOcclusionCulling = false;
                HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
                data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                data.volumeLayerMask = ~0;
                data.customRenderingSettings = true;
                foreach (FrameSettingsField field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.ContactShadows, FrameSettingsField.Volumetrics })
                {
                    data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                    data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                foreach (FrameSettingsField field in new[] { FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl })
                {
                    data.renderingPathCustomFrameSettings.SetEnabled(field, true);
                    data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                Volume volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000.0f;
                volume.sharedProfile = volumeProfile;
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, clear);
                XRayWeatherSkyRenderer sky = controller.gameObject.AddComponent<XRayWeatherSkyRenderer>();
                sky.Configure(volume, shader);
                controller.ConfigureSky(sky);
                if (!EditorSceneManager.SaveScene(demo, SCENE_PATH)) throw new IOException("The dedicated weather sky scene could not be saved.");
            }
            finally
            {
                if (demo.IsValid() && demo.isLoaded) EditorSceneManager.CloseScene(demo, true);
                if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
            }
            Debug.Log("[XRayWeatherSkySetup] Created " + SCENE_PATH + " with serialized effective clear/rain sky and environment resources.");
        }
    }
}
