using System;
using System.Collections.Generic;
using RaidIntoDarkness.FirstPerson;
using UnityEditor;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Player.Editor
{
    public static class FirstPersonPickupSetup
    {
        public const string MOTIONS_PATH = "Assets/_Source/FirstPerson/Animations/Hands/liz_fdda_redone_item_pickup.omf";
        public const string CATALOG_PATH = "Assets/_Source/FirstPerson/Outfits/OutfitCatalog.asset";

        private const string MENU = "Tools/X-Ray/Setup First Person Pickup";

        [MenuItem(MENU)]
        public static void SetupFromMenu()
        {
            Setup();
        }

        public static void SetupBatch()
        {
            bool success = false;
            try
            {
                success = Setup();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Pickup] Setup failed: " + exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        public static bool Setup()
        {
            AssetDatabase.ImportAsset(MOTIONS_PATH, ImportAssetOptions.ForceSynchronousImport);
            var library = AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(MOTIONS_PATH);
            if (library == null)
            {
                Debug.LogError($"[Pickup] '{MOTIONS_PATH}' was not imported as an X-Ray motion library.");
                return false;
            }

            AnimationClip left = FindClip(library, FirstPersonPickupAnimator.LEFT_HAND_MOTION);
            AnimationClip right = FindClip(library, FirstPersonPickupAnimator.RIGHT_HAND_MOTION);
            AnimationClip leftWithWeapon = FindClip(library, FirstPersonPickupAnimator.LEFT_HAND_WITH_WEAPON_MOTION);
            if (left == null || right == null)
            {
                return false;
            }

            if (!ValidateHands(new[] { left, right, leftWithWeapon }))
            {
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(StalkerPlayerSetup.PREFAB_PATH);
            try
            {
                var rig = root.GetComponent<FirstPersonRig>();
                var player = root.GetComponent<StalkerPlayerController>();
                if (rig == null || player == null)
                {
                    Debug.LogError("[Pickup] FPS_Player.prefab must contain FirstPersonRig and StalkerPlayerController.");
                    return false;
                }

                FirstPersonPickupAnimator pickup = root.GetComponent<FirstPersonPickupAnimator>();
                if (pickup == null)
                {
                    pickup = root.AddComponent<FirstPersonPickupAnimator>();
                }

                var serialized = new SerializedObject(pickup);
                serialized.FindProperty("_rig").objectReferenceValue = rig;
                serialized.FindProperty("_leftHand").objectReferenceValue = left;
                serialized.FindProperty("_rightHand").objectReferenceValue = right;
                serialized.FindProperty("_leftHandWithWeapon").objectReferenceValue = leftWithWeapon;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var playerObject = new SerializedObject(player);
                playerObject.FindProperty("_pickup").objectReferenceValue = pickup;
                playerObject.ApplyModifiedPropertiesWithoutUndo();
                var rigObject = new SerializedObject(rig);
                rigObject.FindProperty("_handsVisible").boolValue = false;
                rigObject.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, StalkerPlayerSetup.PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Debug.Log($"[Pickup] Configured {StalkerPlayerSetup.PREFAB_PATH}: {left.name} {left.length:0.000}s, {right.name} {right.length:0.000}s.");
            return true;
        }

        private static AnimationClip FindClip(XRayMotionLibrary library, string motion)
        {
            if (library.TryGetClip(motion, out AnimationClip clip))
            {
                return clip;
            }

            Debug.LogError($"[Pickup] Motion '{motion}' is missing in '{MOTIONS_PATH}'.");
            return null;
        }

        private static bool ValidateHands(IReadOnlyList<AnimationClip> clips)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FirstPersonOutfitCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                Debug.LogError($"[Pickup] Outfit catalog '{CATALOG_PATH}' is missing.");
                return false;
            }

            var checkedModels = new HashSet<GameObject>();
            int failures = 0;
            foreach (FirstPersonOutfitEntry entry in catalog.Entries)
            {
                if (entry.HandsModel == null || !checkedModels.Add(entry.HandsModel))
                {
                    continue;
                }

                foreach (AnimationClip clip in clips)
                {
                    if (clip == null)
                    {
                        continue;
                    }

                    foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.path.Length > 0 && entry.HandsModel.transform.Find(binding.path) == null)
                        {
                            Debug.LogError($"[Pickup] '{clip.name}' animates '{binding.path}', which is missing in hands '{entry.HandsModel.name}'.");
                            failures++;
                            break;
                        }
                    }
                }
            }

            Debug.Log($"[Pickup] Checked {checkedModels.Count} hands models, {failures} incompatible clip bindings.");
            return failures == 0;
        }
    }
}
