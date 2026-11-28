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
                    InventoryRuntimeIconInvalidation.ClearActiveCaches();
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
}
