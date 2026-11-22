using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Inventory;
using Inventory.Editor;
using Inventory.IconGeneration;
using RaidIntoDarkness.Player.Editor;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class IwpSilencerSetup
    {
        private const string SOUND_FOLDER = IwpWeaponSetup.AUDIO_FOLDER + "/";
        private const string ITEM_SOUND_FOLDER = "Assets/_Source/Audio/Inventory/";
        private const string SILENCER_COLLISION_NAME = "SilencerCollision";

        public static string LastResult { get; private set; }
        public static bool IsBaking { get; private set; }

        [MenuItem("Tools/Inventory/Setup AUG A3 Silencer")]
        public static void SetupFromMenu()
        {
            Debug.Log(Setup());
            BakeIcons();
        }

        public static string Setup()
        {
            string silencerPath = GraWeaponAddonSetup.AddonDefinitionPath(WeaponAddonKind.Silencer, GraWeaponAddonSetup.AUG_SILENCER_SECTION);
            ItemDefinition silencer = AssetDatabase.LoadAssetAtPath<ItemDefinition>(silencerPath);

            if (silencer == null)
            {
                throw new InvalidOperationException("Missing NATO silencer definition: " + silencerPath);
            }

            ConfigureVisual(IwpWeaponSetup.PREFAB_PATH, silencer);
            Directory.CreateDirectory(SOUND_FOLDER);
            var clips = new List<AudioClip>();

            for (int i = 0; i <= 6; i++)
            {
                string name = "aug_silencer_stereo" + (i == 0 ? string.Empty : i.ToString());
                string source = Path.Combine(IwpWeaponSetup.IWP_ROOT, "sounds/weapons/aug/" + name + ".ogg");
                string target = SOUND_FOLDER + name + ".ogg";
                File.Copy(source, target, true);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(target));
            }

            var weapon = new SerializedObject(AssetDatabase.LoadAssetAtPath<FirstPersonWeaponAsset>(IwpFirstPersonWeaponSetup.ASSET_PATH));
            SerializedProperty shots = weapon.FindProperty("_silencedShotSounds");
            shots.arraySize = clips.Count;

            for (int i = 0; i < clips.Count; i++)
            {
                shots.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }

            weapon.FindProperty("_silencedFlameParticles").objectReferenceValue = null;
            weapon.FindProperty("_silencedSmokeParticles").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Source/Effects/XRay/Particles/weapons/generic_shoot_00.prefab");
            weapon.ApplyModifiedPropertiesWithoutUndo();
            ConfigureItemSounds();
            AssetDatabase.SaveAssets();
            LastResult = "Configured AUG silencer visuals, world collider, seven IWP shots and suppressed smoke without muzzle flame.";
            return LastResult;
        }

        public static void BakeIcons()
        {
            BakeAsync().Forget(Debug.LogException);
        }

        private static async UniTask BakeAsync()
        {
            IsBaking = true;

            try
            {
                ItemDefinition weapon = AssetDatabase.LoadAssetAtPath<ItemDefinition>(IwpWeaponSetup.DEFINITION_PATH);
                int count = await ItemIconBatchBake.BakeAsync(new[] { weapon }, false, CancellationToken.None);
                LastResult = "Baked AUG icon profiles: " + count;
                Debug.Log(LastResult);
            }
            finally
            {
                IsBaking = false;
            }
        }

        private static void ConfigureVisual(string path, ItemDefinition silencer)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                GameObject weaponVisual = WorldItemPrefabBuilder.GetWeaponVisual(root);
                Transform visual = Array.Find(weaponVisual.GetComponentsInChildren<Transform>(true), child => child.name == "silencer");

                if (visual == null || visual.parent == null || visual.parent.name != WeaponHudComponent.SILENCER_BONE)
                {
                    throw new InvalidOperationException("Missing silencer visual under attachment bone in " + path);
                }

                if (PrefabUtility.IsAnyPrefabInstanceRoot(visual.gameObject))
                {
                    PrefabUtility.UnpackPrefabInstance(visual.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }

                foreach (MonoBehaviour component in visual.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }

                foreach (Component component in visual.GetComponentsInChildren<Component>(true))
                {
                    if (component is not Transform && component is not Renderer && component is not MeshFilter && component is not LODGroup)
                    {
                        UnityEngine.Object.DestroyImmediate(component);
                    }
                }

                ConfigureWorldCollider(root, visual);
                visual.gameObject.SetActive(false);
                ItemIconVariantPart part = Array.Find(weaponVisual.GetComponents<ItemIconVariantPart>(), variantPart => variantPart.Part == silencer);

                if (part == null)
                {
                    part = weaponVisual.AddComponent<ItemIconVariantPart>();
                }

                part.SetData(silencer, "silencer", new[] { visual.gameObject }, Array.Empty<GameObject>());
                var variant = new SerializedObject(part);
                variant.FindProperty("_sizeDelta").vector2IntValue = Vector2Int.right;
                variant.ApplyModifiedPropertiesWithoutUndo();

                FirstPersonWeaponView view = weaponVisual.GetComponent<FirstPersonWeaponView>();

                if (view == null)
                {
                    throw new InvalidOperationException("Missing FirstPersonWeaponView in " + path);
                }

                var serialized = new SerializedObject(view);
                serialized.FindProperty("_silencerVisual").objectReferenceValue = visual.gameObject;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureWorldCollider(GameObject root, Transform visual)
        {
            Transform existing = root.transform.Find(SILENCER_COLLISION_NAME);
            GameObject collision = existing != null ? existing.gameObject : new GameObject(SILENCER_COLLISION_NAME);
            collision.transform.SetParent(root.transform, false);
            collision.SetActive(false);
            BoxCollider collider = collision.GetComponent<BoxCollider>();

            if (collider == null)
            {
                collider = collision.AddComponent<BoxCollider>();
            }

            Bounds bounds = VisualBounds(visual);
            collider.center = bounds.center;
            collider.size = Vector3.Max(bounds.size, Vector3.one * 0.001f);
            var world = new SerializedObject(root.GetComponent<WorldItem>());
            world.FindProperty("_silencerVisual").objectReferenceValue = visual.gameObject;
            world.FindProperty("_silencerCollider").objectReferenceValue = collider;
            world.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Bounds VisualBounds(Transform visual)
        {
            Bounds bounds = default;
            bool found = false;

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                Bounds local = renderer.localBounds;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 point = visual.InverseTransformPoint(renderer.transform.TransformPoint(corner));

                    if (found)
                    {
                        bounds.Encapsulate(point);
                    }
                    else
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                }
            }

            return bounds;
        }

        private static void ConfigureItemSounds()
        {
            StalkerXRaySources sources = StalkerXRaySources.Open(new[] { "sounds/interface" }, out string error);

            if (sources == null)
            {
                throw new InvalidOperationException(error);
            }

            Directory.CreateDirectory(ITEM_SOUND_FOLDER);
            AudioClip attach = ImportSound(sources, "inv_attach_addon");
            AudioClip detach = ImportSound(sources, "inv_detach_addon");

            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { GraWeaponAddonSetup.SILENCERS_FOLDER }))
            {
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));

                if (definition.GetComponent<WeaponAddonComponent>()?.Kind != WeaponAddonKind.Silencer)
                {
                    continue;
                }

                var serialized = new SerializedObject(definition);
                SerializedProperty components = serialized.FindProperty("_components");

                for (int i = 0; i < components.arraySize; i++)
                {
                    SerializedProperty component = components.GetArrayElementAtIndex(i);

                    if (component.managedReferenceValue is WeaponAddonComponent)
                    {
                        component.FindPropertyRelative("_attachSound").objectReferenceValue = attach;
                        component.FindPropertyRelative("_detachSound").objectReferenceValue = detach;
                    }
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static AudioClip ImportSound(StalkerXRaySources sources, string name)
        {
            string target = ITEM_SOUND_FOLDER + name + ".ogg";
            File.WriteAllBytes(target, sources.ReadBytes("sounds/interface/" + name + ".ogg"));
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(target);
        }
    }
}
