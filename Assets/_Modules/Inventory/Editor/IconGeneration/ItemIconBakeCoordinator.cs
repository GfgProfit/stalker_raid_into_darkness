using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Inventory.IconGeneration;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Inventory.Editor
{
    internal static class ItemIconBakeCoordinator
    {
        public static async UniTask<int> BakeAsync(IReadOnlyList<ItemIconBakeVariant> variants, ItemIconGeneratorSettings settings, bool replaceCatalog, bool skipUpToDate, Action<int, int, ItemIconBakeVariant> progress, CancellationToken cancellationToken)
        {
            if (variants == null || variants.Count == 0)
            {
                return 0;
            }

            BakedItemIconCatalog catalog = ItemIconGeneratedAssetWriter.LoadOrCreateCatalog();
            var bakedEntries = new List<BakedItemIconEntry>(variants.Count);
            Dictionary<string, BakedItemIconEntry> existing = BuildExistingIndex(catalog);
            bool completed = false;
            int rendered = 0;

            try
            {
                using var renderSession = new ItemIconBatchRenderSession(settings);

                for (int i = 0; i < variants.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ItemIconBakeVariant variant = variants[i];
                    progress?.Invoke(i, variants.Count, variant);
                    string sourceGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(variant.Definition));
                    string bakeSignature = ItemIconBakeSignatureBuilder.Build(variant, settings);
                    BakedItemIconEntry keyEntry = BakedItemIconEntry.Create(variant.Definition, variant.VariantIds, variant.Width, variant.Height, variant.ProfileType, settings, string.Empty, bakeSignature, sourceGuid, variant.EstimatedTextureBytes);
                    string identity = ItemIconBakeSignatureBuilder.BuildIdentity(keyEntry);

                    if (skipUpToDate && existing.TryGetValue(identity, out BakedItemIconEntry current) && current.BakeSignature == bakeSignature && AssetDatabase.LoadAssetAtPath<Sprite>(ItemIconGeneratedAssetWriter.ResourcePathToAssetPath(current.SpriteResourcePath)) != null)
                    {
                        bakedEntries.Add(current);
                        continue;
                    }

                    Texture2D texture = await renderSession.RenderAsync(variant.Definition, variant.VariantIds, variant.Width, variant.Height, variant.ProfileType, cancellationToken);

                    if (texture == null)
                    {
                        throw new InvalidOperationException($"Icon renderer returned no texture for {variant.DisplayName}.");
                    }

                    try
                    {
                        string resourcePath = ItemIconGeneratedAssetWriter.WriteTexture(texture, keyEntry, variant.Definition, settings);
                        bakedEntries.Add(BakedItemIconEntry.Create(variant.Definition, variant.VariantIds, variant.Width, variant.Height, variant.ProfileType, settings, resourcePath, bakeSignature, sourceGuid, variant.EstimatedTextureBytes));
                        rendered++;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                    }
                }

                completed = true;
                return rendered;
            }
            finally
            {
                if (bakedEntries.Count > 0)
                {
                    Undo.RecordObject(catalog, replaceCatalog && completed ? "Bake All Item Icons" : "Bake Item Icon Variants");

                    if (replaceCatalog && completed)
                    {
                        catalog.ReplaceEntries(bakedEntries);
                        ItemIconGeneratedAssetWriter.DeleteGeneratedAssetsExcept(bakedEntries);
                    }
                    else
                    {
                        catalog.UpsertEntries(bakedEntries);
                    }

                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssets();
                    BakedItemIconCatalog.ResetDefaultCache();
                    ItemIconCache.Clear();
                }

                progress?.Invoke(variants.Count, variants.Count, null);
            }
        }

        private static Dictionary<string, BakedItemIconEntry> BuildExistingIndex(BakedItemIconCatalog catalog)
        {
            var index = new Dictionary<string, BakedItemIconEntry>(StringComparer.Ordinal);

            foreach (BakedItemIconEntry entry in catalog.Entries)
            {
                if (entry != null)
                {
                    index[ItemIconBakeSignatureBuilder.BuildIdentity(entry)] = entry;
                }
            }

            return index;
        }
    }

    public static class ItemIconBatchBake
    {
        public static string CatalogAssetPath => ItemIconGeneratedAssetWriter.CatalogAssetPath;

        public static int AssignMissingDefinitionIds() => ItemDefinitionIdValidator.AssignMissing();

        public static async UniTask<int> BakeAllAsync(bool incremental, CancellationToken cancellationToken)
        {
            ItemIconGeneratorSettings settings = ItemIconGeneratedAssetWriter.LoadOrCreateSettings();
            ItemIconVariantAnalysis analysis = ItemIconVariantEnumerator.Enumerate(ItemDefinitionIdValidator.LoadAllDefinitions(), settings);
            return await ItemIconBakeCoordinator.BakeAsync(analysis.Variants, settings, true, incremental, null, cancellationToken);
        }

        public static async UniTask<int> BakeAsync(IReadOnlyList<ItemDefinition> definitions, bool incremental, CancellationToken cancellationToken)
        {
            ItemIconGeneratorSettings settings = ItemIconGeneratedAssetWriter.LoadOrCreateSettings();
            ItemIconVariantAnalysis analysis = ItemIconVariantEnumerator.Enumerate(definitions, settings);
            return await ItemIconBakeCoordinator.BakeAsync(analysis.Variants, settings, false, incremental, null, cancellationToken);
        }

        [MenuItem("Tools/Inventory/Item Icons/Bake All (Incremental)")]
        private static void BakeAllMenu() => RunMenu(true);

        [MenuItem("Tools/Inventory/Item Icons/Rebake All")]
        private static void RebakeAllMenu() => RunMenu(false);

        public static void BakeAllBatch()
        {
            BakeBatchAsync().Forget();
        }

        private static void RunMenu(bool incremental)
        {
            RunMenuAsync(incremental).Forget(Debug.LogException);
        }

        private static async UniTask RunMenuAsync(bool incremental)
        {
            int baked = await BakeAllAsync(incremental, CancellationToken.None);
            Debug.Log($"Item icon bake finished: {baked} variant(s) rendered into {CatalogAssetPath}.");
        }

        private static async UniTaskVoid BakeBatchAsync()
        {
            bool success = false;

            try
            {
                int baked = await BakeAllAsync(true, CancellationToken.None);
                Debug.Log($"Item icon batch bake rendered {baked} variant(s).");
                success = true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }
    }

    internal sealed class ItemIconCatalogValidationResult
    {
        public readonly List<string> Errors = new();
        public int EntryCount;
        public int MissingVariantCount;
        public bool IsValid => Errors.Count == 0;
    }

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

    internal sealed class ItemIconBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            ItemIconCatalogValidationResult validation = ItemIconCatalogValidator.Validate(false);

            if (validation.IsValid == false)
            {
                Debug.LogWarning($"Baked item icon catalog has {validation.Errors.Count} issue(s); stale icons are regenerated at runtime. Open Tools/Inventory/Item Icon Generator to rebake.\n{string.Join("\n", validation.Errors)}");
            }
        }
    }
}
