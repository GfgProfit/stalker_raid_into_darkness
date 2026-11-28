using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RaidIntoDarkness.Inventory.Infrastructure.IconGeneration;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal static class ItemIconCatalogValidator
    {
        public static ItemIconCatalogValidationResult Validate(bool includeMissingVariants)
        {
            var result = new ItemIconCatalogValidationResult();
            List<ItemDefinition> definitions = ItemDefinitionIdValidator.LoadAllDefinitions();
            ItemDefinitionIdValidationResult idValidation = ItemDefinitionIdValidator.Validate(definitions);
            result.Errors.AddRange(idValidation.Errors);
            var byId = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

            foreach (ItemDefinition definition in definitions)
            {
                if (ItemDefinitionIdValidator.IsValidId(definition.DefinitionId) && byId.ContainsKey(definition.DefinitionId) == false)
                {
                    byId.Add(definition.DefinitionId, definition);
                }
            }

            var catalog = AssetDatabase.LoadAssetAtPath<BakedItemIconCatalog>(ItemIconGeneratedAssetWriter.CatalogAssetPath);

            if (catalog == null)
            {
                result.Errors.Add($"Baked catalog is missing: {ItemIconGeneratedAssetWriter.CatalogAssetPath}");
                return result;
            }

            ItemIconGeneratorSettings settings = ItemIconGeneratedAssetWriter.LoadOrCreateSettings();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var identitiesByHash = new Dictionary<ulong, string>();

            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                BakedItemIconEntry entry = catalog.Entries[i];

                if (entry == null)
                {
                    result.Errors.Add($"Catalog entry {i} is null.");
                    continue;
                }

                string identity = ItemIconBakeSignatureBuilder.BuildIdentity(entry);

                if (identities.Add(identity) == false)
                {
                    result.Errors.Add($"Duplicate catalog entry: {identity}");
                }

                if (identitiesByHash.TryGetValue(entry.StableKeyHash, out string existingIdentity) && existingIdentity != identity)
                {
                    result.Errors.Add($"Stable key hash collision: {entry.StableKeyHash:x16} maps to both '{existingIdentity}' and '{identity}'.");
                }
                else
                {
                    identitiesByHash[entry.StableKeyHash] = identity;
                }

                if (byId.TryGetValue(entry.ItemId, out ItemDefinition definition) == false)
                {
                    result.Errors.Add($"Catalog entry references unknown DefinitionId '{entry.ItemId}'.");
                    continue;
                }

                string assetPath = ItemIconGeneratedAssetWriter.ResourcePathToAssetPath(entry.SpriteResourcePath);

                if (AssetDatabase.LoadAssetAtPath<Sprite>(assetPath) == null)
                {
                    result.Errors.Add($"Generated Sprite is missing for '{identity}': {assetPath}");
                }

                string[] variantIds = new List<string>(entry.VariantIds).ToArray();
                var variant = new ItemIconBakeVariant(definition, variantIds, entry.Width, entry.Height, entry.ProfileType, settings.IconRenderScale);
                BakedItemIconEntry expected = BakedItemIconEntry.Create(definition, variantIds, entry.Width, entry.Height, entry.ProfileType, settings, entry.SpriteResourcePath, string.Empty, string.Empty, variant.EstimatedTextureBytes);

                if (expected.VisualSignature != entry.VisualSignature)
                {
                    result.Errors.Add($"Catalog entry has a stale runtime signature: {identity}");
                }

                if (ItemIconBakeSignatureBuilder.Build(variant, settings) != entry.BakeSignature)
                {
                    result.Errors.Add($"Catalog entry has stale asset dependencies: {identity}");
                }
            }

            if (includeMissingVariants && idValidation.IsValid)
            {
                ItemIconVariantAnalysis analysis = ItemIconVariantEnumerator.Enumerate(definitions, settings);

                foreach (ItemIconBakeVariant variant in analysis.Variants)
                {
                    BakedItemIconEntry expected = BakedItemIconEntry.Create(variant.Definition, variant.VariantIds, variant.Width, variant.Height, variant.ProfileType, settings, string.Empty, string.Empty, string.Empty, variant.EstimatedTextureBytes);

                    if (identities.Contains(ItemIconBakeSignatureBuilder.BuildIdentity(expected)) == false)
                    {
                        result.MissingVariantCount++;
                        result.Errors.Add($"Missing baked icon variant: {variant.DisplayName}");
                    }
                }
            }

            result.EntryCount = catalog.Entries.Count;
            return result;
        }

        [MenuItem("Tools/Inventory/Item Icons/Validate Catalog")]
        private static void ValidateMenu()
        {
            ItemIconCatalogValidationResult result = Validate(true);

            if (result.IsValid)
            {
                Debug.Log($"Baked item icon catalog validation passed: {result.EntryCount} entries.");
            }
            else
            {
                Debug.LogWarning($"Baked item icon catalog validation reported {result.Errors.Count} issue(s); runtime generation covers missing variants.\n{string.Join("\n", result.Errors)}");
            }
        }
    }
}
