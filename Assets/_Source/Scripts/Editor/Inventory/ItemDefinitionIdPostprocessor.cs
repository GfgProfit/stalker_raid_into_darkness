using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RaidIntoDarkness.Inventory.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal sealed class ItemDefinitionIdPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets)
            {
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

                if (definition != null && string.IsNullOrEmpty(definition.DefinitionId))
                {
                    definition.SetDefinitionId(AssetDatabase.AssetPathToGUID(path));
                    EditorUtility.SetDirty(definition);
                }
            }
        }
    }
}
