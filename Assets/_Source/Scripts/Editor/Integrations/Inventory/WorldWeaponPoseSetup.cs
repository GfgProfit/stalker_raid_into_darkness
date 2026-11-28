using System;
using System.Collections.Generic;
using Inventory;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class WorldWeaponPoseSetup
    {
        private const float POSITION_TOLERANCE_SQUARED = 0.00000009f;
        private const float ROTATION_TOLERANCE = 0.2f;

        [MenuItem("Tools/Inventory/Configure World Weapon Empty Poses")]
        public static void ConfigureCatalog()
        {
            var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(InventoryDemoContentBuilder.DATABASE_PATH);
            if (database == null)
            {
                Debug.LogError("[WorldWeaponPose] Item database is missing.");
                return;
            }

            int changed = 0;
            foreach (ItemDefinition definition in database.Definitions)
            {
                if (Configure(definition))
                {
                    changed++;
                }
            }

            Debug.Log($"[WorldWeaponPose] Updated {changed} world prefabs.");
        }

        public static bool Configure(ItemDefinition definition)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return false;
            }

            if (definition == null || !definition.IsCategory("Weapon"))
            {
                return false;
            }

            FirstPersonWeaponAsset weapon = definition.GetComponent<WeaponHudComponent>()?.FirstPersonWeapon;
            WorldItem prefab = definition.GetComponent<WorldItemComponent>()?.Prefab;
            if (weapon == null || prefab == null || weapon.IsMelee || weapon.IsStandaloneLauncher || weapon.IsHandGrenade)
            {
                return false;
            }

            FirstPersonWeaponMotion idle = weapon.Find(FirstPersonWeaponMotionKind.Idle, 0);
            AnimationClip loaded = idle?.Weapon;
            AnimationClip empty = idle?.EmptyWeapon;
            if (empty == null)
            {
                empty = weapon.Find(FirstPersonWeaponMotionKind.IdleEmpty, 0)?.Weapon;
            }

            if ((loaded == null || empty == null || loaded == empty) && new SerializedObject(prefab).FindProperty("_weaponPose").arraySize == 0)
            {
                return false;
            }

            if (EditorUtility.IsDirty(prefab))
            {
                throw new InvalidOperationException($"[WorldWeaponPose] Save the pending changes to {prefab.name} before configuring its pose.");
            }

            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                GameObject visual = WorldItemPrefabBuilder.GetWeaponVisual(root);
                var serialized = new SerializedObject(root.GetComponent<WorldItem>());
                SerializedProperty pose = serialized.FindProperty("_weaponPose");
                var paths = new SortedSet<string>(StringComparer.Ordinal);
                var loadedValues = ReadPose(loaded, paths);
                var emptyValues = ReadPose(empty, paths);
                int index = 0;
                if (loaded != null && empty != null && loaded != empty)
                {
                    foreach (string bonePath in paths)
                    {
                        Transform bone = visual.transform.Find(bonePath);
                        if (bone == null)
                        {
                            throw new InvalidOperationException($"[WorldWeaponPose] {path} has no bone '{bonePath}' from its idle clips.");
                        }

                        var loadedPose = GetPose(loadedValues, bonePath, bone);
                        var emptyPose = GetPose(emptyValues, bonePath, bone);
                        if ((loadedPose.Position - emptyPose.Position).sqrMagnitude <= POSITION_TOLERANCE_SQUARED && Quaternion.Angle(loadedPose.Rotation, emptyPose.Rotation) <= ROTATION_TOLERANCE)
                        {
                            continue;
                        }

                        if (index == pose.arraySize)
                        {
                            pose.arraySize++;
                        }

                        SerializedProperty part = pose.GetArrayElementAtIndex(index++);
                        part.FindPropertyRelative("_bone").objectReferenceValue = bone;
                        part.FindPropertyRelative("_loadedPosition").vector3Value = loadedPose.Position;
                        part.FindPropertyRelative("_loadedRotation").quaternionValue = loadedPose.Rotation;
                        part.FindPropertyRelative("_emptyPosition").vector3Value = emptyPose.Position;
                        part.FindPropertyRelative("_emptyRotation").quaternionValue = emptyPose.Rotation;
                    }
                }

                pose.arraySize = index;
                if (!serialized.ApplyModifiedProperties())
                {
                    return false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved)
                {
                    throw new InvalidOperationException($"[WorldWeaponPose] Cannot save {path}.");
                }

                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Tools/Inventory/Configure World Weapon Empty Poses", true)]
        private static bool CanConfigureCatalog() => !EditorApplication.isPlayingOrWillChangePlaymode;

        private static Dictionary<(string Path, string Property), float> ReadPose(AnimationClip clip, SortedSet<string> paths)
        {
            var values = new Dictionary<(string Path, string Property), float>();
            if (clip == null)
            {
                return values;
            }

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform) || !IsMechanismPath(binding.path) || !(binding.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal) || binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)))
                {
                    continue;
                }

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null)
                {
                    paths.Add(binding.path);
                    values[(binding.path, binding.propertyName)] = curve.Evaluate(0f);
                }
            }

            return values;
        }

        private static bool IsMechanismPath(string path)
        {
            int parent = path.LastIndexOf('/');
            if (parent < 0)
            {
                return false;
            }

            string name = path.Substring(parent + 1);
            return !name.StartsWith("bullet", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("ammo", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("shell", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("case", StringComparison.OrdinalIgnoreCase);
        }

        private static (Vector3 Position, Quaternion Rotation) GetPose(Dictionary<(string Path, string Property), float> values, string path, Transform bone)
        {
            Vector3 position = bone.localPosition;
            Quaternion rotation = bone.localRotation;
            position.x = GetValue(values, path, "m_LocalPosition.x", position.x);
            position.y = GetValue(values, path, "m_LocalPosition.y", position.y);
            position.z = GetValue(values, path, "m_LocalPosition.z", position.z);
            rotation.x = GetValue(values, path, "m_LocalRotation.x", rotation.x);
            rotation.y = GetValue(values, path, "m_LocalRotation.y", rotation.y);
            rotation.z = GetValue(values, path, "m_LocalRotation.z", rotation.z);
            rotation.w = GetValue(values, path, "m_LocalRotation.w", rotation.w);
            return (position, rotation.normalized);
        }

        private static float GetValue(Dictionary<(string Path, string Property), float> values, string path, string property, float fallback)
        {
            return values.TryGetValue((path, property), out float value) ? value : fallback;
        }
    }
}
