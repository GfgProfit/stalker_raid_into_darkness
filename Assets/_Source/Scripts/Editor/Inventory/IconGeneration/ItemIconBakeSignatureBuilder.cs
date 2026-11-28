using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RaidIntoDarkness.Inventory.Infrastructure.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal static class ItemIconBakeSignatureBuilder
    {
        private const int RENDER_VERSION = 2;

        public static string Build(ItemIconBakeVariant variant, ItemIconGeneratorSettings settings)
        {
            var source = new StringBuilder(512);
            BakedItemIconEntry keyEntry = BakedItemIconEntry.Create(variant.Definition, variant.VariantIds, variant.Width, variant.Height, variant.ProfileType, settings, string.Empty, string.Empty, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(variant.Definition)), variant.EstimatedTextureBytes);
            source.Append(RENDER_VERSION).Append('|').Append(BuildIdentity(keyEntry));
            AppendDependency(source, variant.Definition);
            AppendDependency(source, variant.Definition.VisualPrefab);
            AppendDependency(source, settings);
            Dictionary<string, ItemDefinition> definitions = ItemIconVariantEnumerator.GetDefinitionsById();
            var parts = new List<string>(variant.VariantIds);

            foreach (ItemDefinition defaultPart in variant.Definition.DefaultIconVariant)
            {
                if (defaultPart != null)
                {
                    parts.Add(defaultPart.DefinitionId);
                }
            }

            parts.Sort(StringComparer.Ordinal);

            foreach (string id in parts)
            {
                if (definitions.TryGetValue(id, out ItemDefinition part))
                {
                    AppendDependency(source, part);
                    AppendDependency(source, part.VisualPrefab);
                }
            }

            return Hash128.Compute(source.ToString()).ToString();
        }

        public static string BuildIdentity(BakedItemIconEntry entry)
        {
            var identity = new StringBuilder(192);
            identity.Append(entry.ItemId).Append('|');

            foreach (string id in entry.VariantIds)
            {
                identity.Append(id).Append(',');
            }

            identity.Append('|').Append(entry.Width).Append('x').Append(entry.Height).Append('|').Append((int)entry.ProfileType).Append('|').Append(entry.VisualSignature);
            return identity.ToString();
        }

        private static void AppendDependency(StringBuilder source, UnityEngine.Object asset)
        {
            if (asset == null)
            {
                source.Append("null;");
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            source.Append(assetPath).Append(':');
            source.Append(string.IsNullOrEmpty(assetPath) ? "unsaved" : AssetDatabase.GetAssetDependencyHash(assetPath).ToString()).Append(';');
        }
    }
}
