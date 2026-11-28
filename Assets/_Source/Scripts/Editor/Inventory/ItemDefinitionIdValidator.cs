using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RaidIntoDarkness.Inventory.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal static class ItemDefinitionIdValidator
    {
        private static readonly Regex VALID_ID_PATTERN = new("^[0-9a-zA-Z_\\-\\.]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static List<ItemDefinition> LoadAllDefinitions()
        {
            string[] assetGuids = AssetDatabase.FindAssets("t:" + nameof(ItemDefinition));
            var definitions = new List<ItemDefinition>(assetGuids.Length);

            foreach (string guid in assetGuids)
            {
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));

                if (definition != null)
                {
                    definitions.Add(definition);
                }
            }

            definitions.Sort((left, right) => string.CompareOrdinal(AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right)));
            return definitions;
        }

        public static bool IsValidId(string definitionId) => string.IsNullOrEmpty(definitionId) == false && VALID_ID_PATTERN.IsMatch(definitionId);

        public static ItemDefinitionIdValidationResult Validate(IReadOnlyList<ItemDefinition> definitions)
        {
            var result = new ItemDefinitionIdValidationResult();
            var byId = new Dictionary<string, List<ItemDefinition>>(StringComparer.Ordinal);

            foreach (ItemDefinition definition in definitions)
            {
                string path = AssetDatabase.GetAssetPath(definition);

                if (string.IsNullOrEmpty(definition.DefinitionId))
                {
                    result.MissingIds.Add(definition);
                    result.Errors.Add($"Missing DefinitionId: {path}");
                    continue;
                }

                if (IsValidId(definition.DefinitionId) == false)
                {
                    result.MalformedIds.Add(definition);
                    result.Errors.Add($"Malformed DefinitionId '{definition.DefinitionId}': {path}");
                    continue;
                }

                if (byId.TryGetValue(definition.DefinitionId, out List<ItemDefinition> list) == false)
                {
                    list = new List<ItemDefinition>();
                    byId.Add(definition.DefinitionId, list);
                }

                list.Add(definition);
            }

            foreach (KeyValuePair<string, List<ItemDefinition>> pair in byId)
            {
                if (pair.Value.Count > 1)
                {
                    result.Duplicates.Add(pair.Key, pair.Value);
                    result.Errors.Add($"Duplicate DefinitionId '{pair.Key}': {string.Join(", ", pair.Value.ConvertAll(AssetDatabase.GetAssetPath))}");
                }
            }

            result.ValidCount = definitions.Count - result.MissingIds.Count - result.MalformedIds.Count;
            return result;
        }

        public static void AssignId(ItemDefinition definition, string definitionId, string undoName)
        {
            if (definition == null || IsValidId(definitionId) == false)
            {
                return;
            }

            Undo.RecordObject(definition, undoName);
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_definitionId").stringValue = definitionId;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);
        }

        [MenuItem("Tools/Inventory/Item IDs/Validate Definition IDs")]
        private static void ValidateMenu()
        {
            List<ItemDefinition> definitions = LoadAllDefinitions();
            ItemDefinitionIdValidationResult result = Validate(definitions);

            if (result.IsValid)
            {
                Debug.Log($"ItemDefinition validation passed: {definitions.Count} assets, all DefinitionId values are unique and valid.");
            }
            else
            {
                Debug.LogError($"ItemDefinition validation failed with {result.Errors.Count} issue(s):\n{string.Join("\n", result.Errors)}");
            }
        }

        [MenuItem("Tools/Inventory/Item IDs/Assign Missing Definition IDs")]
        public static int AssignMissing()
        {
            ItemDefinitionIdValidationResult result = Validate(LoadAllDefinitions());

            foreach (ItemDefinition definition in result.MissingIds)
            {
                AssignId(definition, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition)), "Assign Missing Definition ID");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Assigned {result.MissingIds.Count} missing DefinitionId value(s). Existing IDs were not changed.");
            return result.MissingIds.Count;
        }

        [MenuItem("Tools/Inventory/Item IDs/Regenerate Selected Duplicate ID")]
        private static void RegenerateSelectedDuplicateId()
        {
            if (Selection.activeObject is not ItemDefinition selected)
            {
                return;
            }

            ItemDefinitionIdValidationResult validation = Validate(LoadAllDefinitions());

            if (validation.Duplicates.TryGetValue(selected.DefinitionId, out List<ItemDefinition> duplicates) == false || duplicates.Count < 2)
            {
                EditorUtility.DisplayDialog("Definition IDs", "Selected ItemDefinition does not have a duplicate DefinitionId.", "OK");
                return;
            }

            AssignId(selected, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selected)), "Regenerate Duplicate Definition ID");
            AssetDatabase.SaveAssets();
            BakedItemIconCatalog.ResetDefaultCache();
        }
    }
}
