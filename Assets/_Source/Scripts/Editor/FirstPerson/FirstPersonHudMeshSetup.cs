using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.FirstPerson.Editor
{
    public static class FirstPersonHudMeshSetup
    {
        public static string Configure(int offset, int count)
        {
            var paths = new List<string>();
            paths.AddRange(Directory.GetFiles("Assets/_Source/FirstPerson/Hands/Models", "*.ogf", SearchOption.AllDirectories));
            paths.AddRange(Directory.GetFiles("Assets/_Source/FirstPerson/Body/Models", "*.ogf", SearchOption.AllDirectories));
            paths.AddRange(Directory.GetFiles("Assets/_Source/Items/Weapons/Models", "*_hud.ogf", SearchOption.AllDirectories));
            paths.Sort(StringComparer.Ordinal);
            int changed = 0;
            int end = Mathf.Min(paths.Count, offset + count);

            for (int i = offset; i < end; i++)
            {
                AssetImporter importer = AssetImporter.GetAtPath(paths[i].Replace('\\', '/'));
                var serialized = new SerializedObject(importer);
                SerializedProperty property = serialized.FindProperty("_updateWhenOffscreen");

                if (!property.boolValue)
                {
                    property.boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    importer.SaveAndReimport();
                    changed++;
                }

                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i].Replace('\\', '/'));

                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!renderer.updateWhenOffscreen)
                    {
                        throw new InvalidOperationException(paths[i] + ": Update When Offscreen is disabled.");
                    }
                }
            }

            return $"HUD models {offset}..{end}/{paths.Count}; updated {changed}.";
        }
    }
}
