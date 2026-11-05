using System;
using System.Collections.Generic;
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
    public static class XRayCordonWeatherSetup
    {
        public const string SCENE_PATH = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/l01_escape/Scenes/l01_escape.unity";
        public const string SKY_PROFILE_PATH = "Assets/_Source/Levels/WeatherProfiles/Cordon/CordonWeatherSky.asset";

        private const string SKY_VOLUME_NAME = "Source Weather Sky";

        [MenuItem("Tools/X-Ray/Weather/Connect Cordon Weather")]
        public static void ConfigureActiveScene()
        {
            Scene scene = RequireScene(true);
            List<XRayWeatherController> controllers = Components<XRayWeatherController>(scene);
            if (controllers.Count != 1) throw new InvalidOperationException("The Cordon scene must already contain exactly one weather controller.");
            XRayWeatherController controller = controllers[0];
            var serialized = new SerializedObject(controller);
            XRayWeatherProfile original = serialized.FindProperty("_profile").objectReferenceValue as XRayWeatherProfile;
            XRayWeatherProfile resourceProfile = ResourceProfile(original);
            float initialTime = serialized.FindProperty("_timeOfDayHours").floatValue;
            float timeScale = serialized.FindProperty("_timeScale").floatValue;
            bool paused = serialized.FindProperty("_paused").boolValue;
            UnityEngine.Object weather = serialized.FindProperty("_weatherInputs").objectReferenceValue;
            UnityEngine.Object lighting = serialized.FindProperty("_sourceLighting").objectReferenceValue;
            var wind = serialized.FindProperty("_windProvider").objectReferenceValue as XRayFloraWindProvider;
            UnityEngine.Object sun = serialized.FindProperty("_sun").objectReferenceValue;
            if (weather == null || lighting == null || wind == null || sun == null || !wind.isActiveAndEnabled) throw new InvalidOperationException("Preserve and connect the scene's existing weather consumers before attaching its sky.");
            foreach (XRayFloraWindProvider provider in Components<XRayFloraWindProvider>(scene))
            {
                if (provider == wind || !provider.isActiveAndEnabled) continue;
                if (provider.gameObject.name != "Source Flora Wind" || !PrefabUtility.IsPartOfPrefabInstance(provider)) throw new InvalidOperationException("An unrelated active wind provider must be reviewed before connecting Cordon weather.");
            }
            Volume authoredVolume = AuthoredVolume(scene);
            authoredVolume.sharedProfile.TryGet(out GradientSky authoredSky);
            Shader shader = Shader.Find("XRay/Monolith Weather Sky");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("The serialized X-Ray weather sky shader must compile before scene integration.");
            List<XRayWeatherSkyRenderer> renderers = Components<XRayWeatherSkyRenderer>(scene);
            if (renderers.Count > 1 || renderers.Count == 1 && renderers[0].gameObject != controller.gameObject) throw new InvalidOperationException("The Cordon sky must have one renderer on its existing weather controller.");
            Volume volume = null;
            foreach (Volume candidate in Components<Volume>(scene))
            {
                if (candidate.gameObject.name != SKY_VOLUME_NAME) continue;
                if (volume != null || candidate.transform.parent != controller.transform || candidate.sharedProfile == null || AssetDatabase.GetAssetPath(candidate.sharedProfile) != SKY_PROFILE_PATH) throw new InvalidOperationException("The existing Cordon sky Volume has incompatible ownership.");
                volume = candidate;
            }
            XRayWeatherPreview.Stop();
            if (controller.PreviewActive) controller.EndPreview();
            VolumeProfile profile = SkyProfile(resourceProfile, controller.ConfiguredTimeOfDay, shader, authoredSky, authoredVolume.sharedProfile);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Connect Cordon Weather");
            if (volume == null)
            {
                var volumeObject = new GameObject(SKY_VOLUME_NAME);
                SceneManager.MoveGameObjectToScene(volumeObject, scene);
                Undo.RegisterCreatedObjectUndo(volumeObject, "Create Cordon Weather Sky Volume");
                Undo.SetTransformParent(volumeObject.transform, controller.transform, "Parent Cordon Weather Sky Volume");
                volume = Undo.AddComponent<Volume>(volumeObject);
            }
            Undo.RecordObject(volume, "Configure Cordon Weather Sky Volume");
            Undo.RecordObject(volume.gameObject, "Configure Cordon Weather Sky Layer");
            volume.gameObject.layer = 0;
            volume.isGlobal = true;
            volume.priority = authoredVolume.priority + 1.0f;
            volume.weight = 1.0f;
            volume.sharedProfile = profile;
            volume.enabled = true;
            XRayWeatherSkyRenderer renderer = controller.GetComponent<XRayWeatherSkyRenderer>();
            if (renderer == null) renderer = Undo.AddComponent<XRayWeatherSkyRenderer>(controller.gameObject);
            Undo.RecordObject(renderer, "Connect Cordon Weather Sky Renderer");
            renderer.Configure(volume, shader);
            renderer.enabled = true;
            foreach (XRayFloraWindProvider provider in Components<XRayFloraWindProvider>(scene))
            {
                if (provider == wind || !provider.isActiveAndEnabled) continue;
                Undo.RecordObject(provider, "Disable Duplicate Imported Flora Wind");
                provider.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(provider);
            }
            Undo.RecordObject(controller, "Connect Cordon Resource Weather Profile");
            serialized.Update();
            serialized.FindProperty("_profile").objectReferenceValue = resourceProfile;
            serialized.FindProperty("_levelOverrides").intValue |= (int)XRayWeatherLevelOverrides.WeatherProfile;
            serialized.ApplyModifiedProperties();
            controller.ApplySettings();
            controller.ConfigureSky(renderer);
            Record(controller);
            Record(renderer);
            Record(volume);
            serialized.Update();
            if (serialized.FindProperty("_timeOfDayHours").floatValue != initialTime || serialized.FindProperty("_timeScale").floatValue != timeScale || serialized.FindProperty("_paused").boolValue != paused || serialized.FindProperty("_weatherInputs").objectReferenceValue != weather || serialized.FindProperty("_sourceLighting").objectReferenceValue != lighting || serialized.FindProperty("_windProvider").objectReferenceValue != wind || serialized.FindProperty("_sun").objectReferenceValue != sun) throw new InvalidOperationException("Cordon weather integration must preserve authored simulation settings and consumer references.");
            ValidateActiveScene();
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unable to save the authorized Cordon weather scene.");
        }

        public static string ValidateActiveScene()
        {
            Scene scene = RequireScene(false);
            List<XRayWeatherController> controllers = Components<XRayWeatherController>(scene);
            List<XRayWeatherSkyRenderer> renderers = Components<XRayWeatherSkyRenderer>(scene);
            if (controllers.Count != 1 || renderers.Count != 1 || controllers[0].gameObject != renderers[0].gameObject) throw new InvalidDataException("Cordon requires one existing controller and one attached sky renderer.");
            var controller = new SerializedObject(controllers[0]);
            var boundWind = controller.FindProperty("_windProvider").objectReferenceValue as XRayFloraWindProvider;
            int activeWind = 0;
            foreach (XRayFloraWindProvider wind in Components<XRayFloraWindProvider>(scene))
            {
                if (!wind.isActiveAndEnabled) continue;
                if (wind != boundWind) throw new InvalidDataException("A second Cordon wind writer is active.");
                activeWind++;
            }
            if (activeWind != 1) throw new InvalidDataException("The authored controller wind provider must remain enabled.");
            XRayWeatherProfile profile = controllers[0].AuthoredProfile;
            if (profile == null || AssetDatabase.GetAssetPath(profile) != AssetDatabase.GetAssetPath(ResourceProfile(profile))) throw new InvalidDataException("The Cordon controller must reference its same-cycle resource-ready profile.");
            var renderer = new SerializedObject(renderers[0]);
            var volume = renderer.FindProperty("_volume").objectReferenceValue as Volume;
            if (volume == null || !volume.isActiveAndEnabled || !volume.isGlobal || volume.gameObject.layer != 0 || volume.sharedProfile == null || AssetDatabase.GetAssetPath(volume.sharedProfile) != SKY_PROFILE_PATH || volume.sharedProfile.components.Count != 2) throw new InvalidDataException("The dedicated Cordon sky Volume is incomplete.");
            if (!volume.sharedProfile.TryGet(out XRayWeatherSkySettings sky) || !volume.sharedProfile.TryGet(out VisualEnvironment visual) || visual.skyType.value != XRayWeatherSkySettings.SKY_ID) throw new InvalidDataException("The Cordon sky profile must contain only its custom sky and Visual Environment.");
            foreach (VolumeComponent component in volume.sharedProfile.components)
            {
                if (component == null || !AssetDatabase.Contains(component) || AssetDatabase.GetAssetPath(component) != SKY_PROFILE_PATH) throw new InvalidDataException("Cordon sky Volume components must be persistent subassets.");
            }
            return "Cordon weather controller=1; sky renderer=1; active wind=1; persistent sky-only Volume components=2; sky exposure=" + sky.exposure.value + "; HDRP intensity=" + sky.UseHdrpIntensity + "; night ambient=" + sky.NightAmbient.ToString("F6") + ".";
        }

        private static Scene RequireScene(bool clean)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Cordon weather setup and inspection require Edit Mode.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.path != SCENE_PATH) throw new InvalidOperationException("Activate the exact authorized Production l01_escape scene before connecting Cordon weather.");
            if (clean && scene.isDirty) throw new InvalidOperationException("Preserve unsaved Cordon scene changes before weather integration.");
            return scene;
        }

        private static XRayWeatherProfile ResourceProfile(XRayWeatherProfile original)
        {
            if (original == null) throw new InvalidDataException("The Cordon controller requires its existing weather profile.");
            string path = original.CycleName == "w_clear1" ? XRayWeatherSkySetup.CLEAR_PATH : original.CycleName == "w_rain1" ? XRayWeatherSkySetup.RAIN_PATH : null;
            XRayWeatherProfile result = path == null ? null : AssetDatabase.LoadAssetAtPath<XRayWeatherProfile>(path);
            if (result == null || result.CycleName != original.CycleName || result.KeyframeCount != original.KeyframeCount || !result.Validate(out string diagnostic)) throw new InvalidDataException("The matching same-cycle Phase07B weather profile is unavailable.");
            for (int frame = 0; frame < result.KeyframeCount; frame++)
            {
                XRayWeatherSnapshot snapshot = result.GetKeyframe(frame);
                for (int resource = 0; resource < 4; resource++)
                {
                    if (snapshot.SkyResources.Weights[resource] > 0.0f && !(snapshot.SkyResources.Texture(resource) is Cubemap)) throw new InvalidDataException("The Phase07B profile contains a missing serialized sky cubemap.");
                    if (snapshot.EnvironmentResources.Weights[resource] > 0.0f && !(snapshot.EnvironmentResources.Texture(resource) is Cubemap)) throw new InvalidDataException("The Phase07B profile contains a missing serialized environment cubemap.");
                }
            }
            return result;
        }

        private static Volume AuthoredVolume(Scene scene)
        {
            Volume selected = null;
            foreach (Volume volume in Components<Volume>(scene))
            {
                if (!volume.isActiveAndEnabled || !volume.isGlobal || volume.sharedProfile == null || !volume.sharedProfile.TryGet(out GradientSky sky) || !sky.active) continue;
                if (selected == null || volume.priority > selected.priority) selected = volume;
            }
            if (selected == null) throw new InvalidDataException("The authored Cordon global Gradient Sky is required to preserve its HDRP intensity settings.");
            return selected;
        }

        private static VolumeProfile SkyProfile(XRayWeatherProfile weather, float hour, Shader shader, GradientSky authored, VolumeProfile authoredProfile)
        {
            VolumeProfile profile = XRayWeatherSkySetup.GetOrCreateSkyProfile(weather, hour, shader, authored, authoredProfile, SKY_PROFILE_PATH);
            profile.TryGet(out XRayWeatherSkySettings settings);
            if (settings.skyIntensityMode.value != authored.skyIntensityMode.value || settings.exposure.value != authored.exposure.value || settings.multiplier.value != authored.multiplier.value) throw new InvalidDataException("Preserve the authored Cordon sky intensity settings before updating its setup.");
            Color night = XRayWeatherSkySetup.DEFAULT_NIGHT_AMBIENT;
            if (!settings.UseHdrpIntensity || settings.NightAmbient != night)
            {
                settings.SetHdrpIntensity(true);
                settings.SetNightAmbient(night);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
            return profile;
        }

        private static List<T> Components<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }

        private static void Record(UnityEngine.Object value)
        {
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }

    }
}
