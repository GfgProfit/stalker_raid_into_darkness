using System;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    public static class WeaponEmptyAnimationSetup
    {
        public static string ConfigureAug()
        {
            FirstPersonWeaponAsset asset = AssetDatabase.LoadAssetAtPath<FirstPersonWeaponAsset>(IwpFirstPersonWeaponSetup.ASSET_PATH);
            AnimationClip shot = asset.Find(FirstPersonWeaponMotionKind.Shoot, 0).Weapon;
            AnimationClip empty = asset.Find(FirstPersonWeaponMotionKind.IdleEmpty, 0).Weapon;
            GameObject reference = UnityEngine.Object.Instantiate(asset.HudModel);
            reference.SetActive(false);

            try
            {
                Transform bolt = Array.Find(reference.GetComponentsInChildren<Transform>(true), child => child.name == "bolt");

                if (bolt == null || shot == null || empty == null)
                {
                    throw new InvalidOperationException("AUG requires a bolt bone and imported shot/idle_empty weapon clips.");
                }

                empty.SampleAnimation(reference, 0f);
                Vector3 target = bolt.localPosition;
                float nearest = float.PositiveInfinity;
                int holdFrame = 0;
                int frames = Mathf.CeilToInt(shot.length * shot.frameRate);

                for (int i = 0; i <= frames; i++)
                {
                    float time = Mathf.Min(i / shot.frameRate, shot.length);
                    shot.SampleAnimation(reference, time);
                    float distance = (bolt.localPosition - target).sqrMagnitude;

                    if (distance < nearest)
                    {
                        nearest = distance;
                        holdFrame = i;
                    }
                }

                if (holdFrame <= 0)
                {
                    throw new InvalidOperationException("AUG shot clip has no retracting bolt motion.");
                }

                var serialized = new SerializedObject(asset);
                serialized.FindProperty("_lastShotHoldFrame").intValue = holdFrame;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                return $"AUG last-shot hold: {holdFrame} frames ({holdFrame / shot.frameRate:F4}s); weapon shot: {shot.length:F4}s at {shot.frameRate:F0}fps; hands shot: {asset.Find(FirstPersonWeaponMotionKind.Shoot, 0).Duration:F4}s; bolt distance: {Mathf.Sqrt(nearest):F6}m.";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(reference);
            }
        }
    }
}
