using RaidIntoDarkness.Inventory.Domain;
using System;
using System.IO;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Inventory.Editor
{
    public static class AmmoBallisticsSetup
    {
        public const string FOLDER = IwpWeaponSetup.WEAPONS_FOLDER + "/_Shared Ballistics";
        public const string MATERIAL_PATH = FOLDER + "/BallisticTracer.mat";
        public const string BULLET_PATH = FOLDER + "/Bullet.prefab";
        public const string PROFILES_PATH = "Assets/_Source/Scripts/Editor/Inventory/AmmoBallisticsProfiles.json";

        [MenuItem("Tools/Inventory/Setup Ammo Ballistics")]
        public static void SetupFromMenu()
        {
            Debug.Log(Configure());
        }

        public static string Configure()
        {
            AmmoBallisticsProfiles profiles = JsonUtility.FromJson<AmmoBallisticsProfiles>(File.ReadAllText(PROFILES_PATH));
            Shader shader = Shader.Find("RaidIntoDarkness/BallisticTracer");

            if (shader == null)
            {
                throw new InvalidOperationException("Import the BallisticTracer shader before configuring ammo.");
            }

            if (ShaderUtil.ShaderHasError(shader))
            {
                throw new InvalidOperationException("Fix BallisticTracer shader compilation errors before configuring ammo.");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);

            if (material == null)
            {
                material = new Material(shader) { name = "BallisticTracer", renderQueue = 3100, enableInstancing = true };
                AssetDatabase.CreateAsset(material, MATERIAL_PATH);
            }

            GameObject bullet = AssetDatabase.LoadAssetAtPath<GameObject>(BULLET_PATH);

            if (bullet == null)
            {
                Scene preview = EditorSceneManager.NewPreviewScene();

                try
                {
                    var root = new GameObject("Bullet");
                    SceneManager.MoveGameObjectToScene(root, preview);
                    root.AddComponent<BallisticProjectile>();
                    bullet = PrefabUtility.SaveAsPrefabAsset(root, BULLET_PATH);
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(preview);
                }
            }

            int count = 0;

            foreach (AmmoBallisticsProfile profile in profiles.Entries)
            {
                string path = IwpAmmoSetup.DefinitionPath(profile.Section);
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

                if (definition == null || !profile.Data.IsConfigured)
                {
                    throw new InvalidOperationException("Missing ammo definition or invalid ballistics: " + path);
                }

                var serialized = new SerializedObject(definition);
                SerializedProperty components = serialized.FindProperty("_components");
                SerializedProperty component = null;

                for (int i = 0; i < components.arraySize; i++)
                {
                    SerializedProperty candidate = components.GetArrayElementAtIndex(i);

                    if (candidate.managedReferenceValue is AmmoBallisticsComponent)
                    {
                        component = candidate;
                        break;
                    }
                }

                if (component == null)
                {
                    int index = components.arraySize++;
                    component = components.GetArrayElementAtIndex(index);
                }

                GrenadeAmmoData grenade = (component.managedReferenceValue as AmmoBallisticsComponent)?.Grenade;
                component.managedReferenceValue = new AmmoBallisticsComponent(profile.Data, grenade);
                component.FindPropertyRelative("_data").FindPropertyRelative("_ammoTracerMaterial").objectReferenceValue = material;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(definition);
                count++;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:FirstPersonWeaponAsset", new[] { IwpWeaponSetup.WEAPONS_FOLDER }))
            {
                FirstPersonWeaponAsset weapon = AssetDatabase.LoadAssetAtPath<FirstPersonWeaponAsset>(AssetDatabase.GUIDToAssetPath(guid));
                var serialized = new SerializedObject(weapon);
                serialized.FindProperty("_ballistics").FindPropertyRelative("_bulletPrefab").objectReferenceValue = bullet;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(weapon);
            }

            AssetDatabase.SaveAssets();
            return $"Configured {count} ammo ballistic profiles, shared HDRP tracer material and weapon bullet prefabs.";
        }
    }
}
