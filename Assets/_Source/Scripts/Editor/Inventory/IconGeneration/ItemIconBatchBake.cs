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
}
