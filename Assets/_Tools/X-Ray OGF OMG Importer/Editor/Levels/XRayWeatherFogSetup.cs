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
    public static class XRayWeatherFogSetup
    {
        public const string PROFILE_PATH = "Assets/_Source/Levels/WeatherProfiles/NativeFog/WeatherFogVolume.asset";

        private const string PRODUCTION_ASSETS = "E:/Projects/stalker_raid_into_darkness/Assets";
        private const string VOLUME_NAME = "Source Weather Fog";

        internal static bool IsProductionProject => string.Equals(Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/'), PRODUCTION_ASSETS, StringComparison.OrdinalIgnoreCase);

        [MenuItem("Tools/X-Ray/Weather/Connect Active Scene Weather Fog")]
        public static void ConfigureActiveScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Weather fog setup requires Edit Mode.");
            if (!IsProductionProject) throw new InvalidOperationException("Weather fog setup requires the authorized Production project.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) throw new InvalidOperationException("Activate the existing weather scene before connecting fog.");
            Configure(scene);
        }

        public static int Configure(Scene scene)
        {
            if (!IsProductionProject || EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) return 0;
            int changed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (XRayWeatherController controller in root.GetComponentsInChildren<XRayWeatherController>(true))
                {
                    ConfigureController(controller, true, out bool configured);
                    if (configured) changed++;
                }
            }
            if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);
            return changed;
        }

        public static XRayWeatherFogRenderer ConfigureController(XRayWeatherController controller, bool recordUndo = false)
        {
            return ConfigureController(controller, recordUndo, out _);
        }

        public static VolumeProfile GetOrCreateProfile()
        {
            VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PROFILE_PATH);
            if (existing != null)
            {
                if (existing.components.Count != 1 || !existing.TryGet(out Fog fog)) throw new InvalidDataException("The native weather fog template must contain only HDRP Fog: " + PROFILE_PATH);
                return existing;
            }
            if (File.Exists(PROFILE_PATH)) throw new IOException("The native weather fog profile destination is already occupied: " + PROFILE_PATH);
            XRayLevelPaths.EnsureFolder(Path.GetDirectoryName(PROFILE_PATH).Replace('\\', '/'));
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "WeatherFogVolume";
            Fog settings = profile.Add<Fog>(false);
            settings.enabled.value = false;
            AssetDatabase.CreateAsset(profile, PROFILE_PATH);
            AssetDatabase.AddObjectToAsset(settings, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        private static XRayWeatherFogRenderer ConfigureController(XRayWeatherController controller, bool recordUndo, out bool changed)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            VolumeProfile profile = GetOrCreateProfile();
            XRayWeatherFogRenderer renderer = controller.GetComponent<XRayWeatherFogRenderer>();
            Transform child = controller.transform.Find(VOLUME_NAME);
            Volume volume = child != null ? child.GetComponent<Volume>() : null;
            int layer = XRayWeatherFogRenderer.GetVolumeLayer(controller.gameObject);
            var serialized = new SerializedObject(controller);
            changed = renderer == null || volume == null || !volume.isGlobal || volume.priority != float.MaxValue || volume.weight != 1.0f || volume.sharedProfile != profile || child.gameObject.layer != layer;
            changed |= renderer != null && (serialized.FindProperty("_fogRenderer").objectReferenceValue != renderer || new SerializedObject(renderer).FindProperty("_volume").objectReferenceValue != volume);
            if (!changed) return renderer;
            int group = 0;
            if (recordUndo)
            {
                Undo.IncrementCurrentGroup();
                group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Connect Weather Fog");
            }
            if (child == null)
            {
                var volumeObject = new GameObject(VOLUME_NAME);
                SceneManager.MoveGameObjectToScene(volumeObject, controller.gameObject.scene);
                if (recordUndo)
                {
                    Undo.RegisterCreatedObjectUndo(volumeObject, "Create Weather Fog Volume");
                    Undo.SetTransformParent(volumeObject.transform, controller.transform, "Parent Weather Fog Volume");
                }
                else volumeObject.transform.SetParent(controller.transform, false);
                child = volumeObject.transform;
            }
            if (recordUndo) Undo.RecordObject(child.gameObject, "Configure Weather Fog Layer");
            child.gameObject.layer = layer;
            if (volume == null) volume = recordUndo ? Undo.AddComponent<Volume>(child.gameObject) : child.gameObject.AddComponent<Volume>();
            if (renderer == null) renderer = recordUndo ? Undo.AddComponent<XRayWeatherFogRenderer>(controller.gameObject) : controller.gameObject.AddComponent<XRayWeatherFogRenderer>();
            if (recordUndo)
            {
                Undo.RecordObject(volume, "Configure Native Weather Fog Volume");
                Undo.RecordObject(child.gameObject, "Configure Weather Fog Layer");
                Undo.RecordObject(renderer, "Connect Native Weather Fog Volume");
                Undo.RecordObject(controller, "Connect Weather Fog Consumer");
            }
            volume.isGlobal = true;
            volume.priority = float.MaxValue;
            volume.weight = 1.0f;
            volume.sharedProfile = profile;
            renderer.Configure(volume);
            controller.ConfigureFog(renderer);
            if (recordUndo)
            {
                Record(volume);
                Record(child.gameObject);
                Record(renderer);
                Record(controller);
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
            }
            return renderer;
        }

        private static void Record(UnityEngine.Object value)
        {
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }
    }
}
