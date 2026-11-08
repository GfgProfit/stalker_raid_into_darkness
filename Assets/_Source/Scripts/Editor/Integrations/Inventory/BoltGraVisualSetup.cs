using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RaidIntoDarkness.FirstPerson;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class BoltGraVisualSetup
    {
        private const string ROOT = "Assets/_Source/FirstPerson/Bolt";
        private const string DEFINITION = ROOT + "/BoltDefinition.asset";
        private const string OUTPUT = "E:/Projects/stalker_raid_into_darkness_Archive/BoltIwp_20261108_01/RollbackGra";

        private static readonly string[] HAND_FIELDS = { "_draw", "_drawAlternate", "_holster", "_idle", "_moving", "_sprint", "_bore", "_throwBegin", "_throwIdle", "_throwEnd" };
        private static readonly string[] HAND_MOTIONS = { "dev_bolt_draw", "dev_bolt_draw1", "dev_bolt_holster", "dev_bolt_idle", "dev_bolt_idle_moving", "dev_bolt_idle_sprint", "dev_bolt_idle_bore", "dev_bolt_shoot_start", "dev_bolt_shoot_idle", "dev_bolt_shoot_end" };
        private static readonly string[] ITEM_FIELDS = { "_itemIdle", "_itemFallback", "_itemDraw", "_itemDrawAlternate", "_itemBore" };
        private static readonly string[] ITEM_MOTIONS = { "dev_bolt_idle", "idle", "dev_bolt_draw", "dev_bolt_draw1", "dev_bolt_idle_bore" };

        public static bool Restore()
        {
            Guard();
            string scenes = SnapshotScenes();
            BoltDefinition definition = Load<BoltDefinition>(DEFINITION);
            XRayMotionLibrary hands = Load<XRayMotionLibrary>(ROOT + "/Animations/dev_hand_bolt_hud_animation.omf");
            XRayMotionLibrary item = Load<XRayMotionLibrary>(ROOT + "/Animations/dev_bolt_hud_animation.omf");
            GameObject hud = Load<GameObject>(ROOT + "/Models/dev_bolt_hud.ogf");
            GameObject world = Load<GameObject>(ROOT + "/Models/dev_bolt.ogf");
            var data = new SerializedObject(definition);
            Set(data, "_hudModel", hud);
            Set(data, "_handsMotions", hands);
            Set(data, "_itemMotions", item);
            for (int index = 0; index < HAND_FIELDS.Length; index++)
            {
                Set(data, HAND_FIELDS[index], Clip(hands, HAND_MOTIONS[index]));
            }
            for (int index = 0; index < ITEM_FIELDS.Length; index++)
            {
                Set(data, ITEM_FIELDS[index], Clip(item, ITEM_MOTIONS[index]));
            }
            Set(data, "_movingSlow", null);
            Set(data, "_movingCrouch", null);
            Set(data, "_drawCameraEffect", null);
            data.FindProperty("_handsPosition").vector3Value = new Vector3(0.033f, -0.155f, -0.15f);
            data.FindProperty("_handsRotation").quaternionValue = Quaternion.identity;
            data.FindProperty("_throwReleaseTime").floatValue = 0.2446666956f;
            ReplaceWorldVisual(definition.ProjectilePrefab, world);
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(definition);
            Require(scenes == SnapshotScenes(), "Restoring GRA bolt visuals changed a loaded scene.");
            Directory.CreateDirectory(OUTPUT);
            string report = OUTPUT + "/restore-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff") + ".txt";
            File.WriteAllText(report, "GRA HUD/world model and hand/item animations restored.\nOptional IWP clips and camera effect cleared.\nSource pose and Left release marker restored.\nExisting projectile root components preserved by exact serialized snapshot.\nAll other BoltDefinition fields preserved.\nLoaded scenes unchanged.\nNo import, physics simulation, Play Mode, rendering or audio playback.\n", new UTF8Encoding(false));
            return true;
        }

        public static string AuditIwpReferences()
        {
            var dependencies = new HashSet<string>();
            foreach (string path in AssetDatabase.GetDependencies(DEFINITION, true))
            {
                if (path.StartsWith(ROOT + "/IWP/", StringComparison.OrdinalIgnoreCase))
                {
                    dependencies.Add(path);
                }
            }
            var sceneObjects = new List<Object>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                foreach (GameObject root in SceneManager.GetSceneAt(index).GetRootGameObjects())
                {
                    sceneObjects.Add(root);
                }
            }
            foreach (Object value in EditorUtility.CollectDependencies(sceneObjects.ToArray()))
            {
                string path = AssetDatabase.GetAssetPath(value);
                if (path.StartsWith(ROOT + "/IWP/", StringComparison.OrdinalIgnoreCase))
                {
                    dependencies.Add(path);
                }
            }
            var result = new List<string>(dependencies);
            result.Sort(StringComparer.Ordinal);
            string text = string.Join("\n", result);
            Directory.CreateDirectory(OUTPUT);
            File.WriteAllText(OUTPUT + "/active-iwp-dependencies-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff") + ".txt", text, new UTF8Encoding(false));
            return "count=" + result.Count + "; paths=" + text;
        }

        private static void ReplaceWorldVisual(GameObject prefab, GameObject model)
        {
            Require(prefab != null, "Existing bolt projectile prefab is missing.");
            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                string before = SnapshotComponents(root);
                foreach (XRayModel visual in root.GetComponentsInChildren<XRayModel>(true))
                {
                    Require(visual.gameObject != root, "Bolt physics root cannot be replaced as a visual.");
                    Object.DestroyImmediate(visual.gameObject);
                }
                GameObject instance = Object.Instantiate(model, root.transform, false);
                instance.name = "BoltWorldModel";
                Require(instance.GetComponentsInChildren<Renderer>(true).Length > 0, "GRA bolt world model has no geometry.");
                Require(before == SnapshotComponents(root), "Restoring the GRA world model changed projectile physics or audio.");
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string SnapshotComponents(GameObject root)
        {
            var text = new StringBuilder();
            foreach (Component component in root.GetComponents<Component>())
            {
                if (component is Transform || component is XRayModel)
                {
                    continue;
                }
                text.AppendLine(component.GetType().FullName);
                text.AppendLine(EditorJsonUtility.ToJson(component));
            }
            return text.ToString();
        }

        private static string SnapshotScenes()
        {
            var text = new StringBuilder();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                text.AppendLine(scene.path + " dirty=" + scene.isDirty + " roots=" + scene.rootCount);
            }
            return text.ToString();
        }

        private static AnimationClip Clip(XRayMotionLibrary library, string name)
        {
            Require(library.TryGetClip(name, out AnimationClip clip) && clip != null, "Restored GRA animation is missing: " + name);
            return clip;
        }

        private static void Set(SerializedObject data, string field, Object value)
        {
            data.FindProperty(field).objectReferenceValue = value;
        }

        private static T Load<T>(string path) where T : Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            Require(value != null, "Restored GRA asset is missing: " + path);
            return value;
        }

        private static void Guard()
        {
            Require(Application.dataPath.Replace('\\', '/') == "E:/Projects/stalker_raid_into_darkness/Assets", "Unexpected Unity project.");
            Require(EditorApplication.isPlayingOrWillChangePlaymode == false && EditorApplication.isCompiling == false, "Bolt visual restore requires idle Edit Mode.");
            Require(PrefabStageUtility.GetCurrentPrefabStage() == null, "Close Prefab Stage before restoring bolt visuals.");
        }

        private static void Require(bool condition, string message)
        {
            if (condition == false)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
