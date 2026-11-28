using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.Application;
using System;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory.Infrastructure.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal sealed class ItemIconBakeVariant : IEquatable<ItemIconBakeVariant>
    {
        public ItemIconBakeVariant(ItemDefinition definition, string[] variantIds, int width, int height, ItemIconProfileType profileType, int iconRenderScale)
        {
            Definition = definition;
            VariantIds = variantIds ?? Array.Empty<string>();
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            ProfileType = profileType;
            IconRenderScale = Mathf.Clamp(iconRenderScale, 1, 4);
        }

        public ItemDefinition Definition { get; }
        public string[] VariantIds { get; }
        public int Width { get; }
        public int Height { get; }
        public ItemIconProfileType ProfileType { get; }
        public int IconRenderScale { get; }
        public long EstimatedTextureBytes => (long)Width * Definition.Icon.PixelsPerCell * IconRenderScale * Height * Definition.Icon.PixelsPerCell * IconRenderScale * 4L;
        public string DisplayName => $"{Definition.DisplayName} | {ProfileType} {Width}x{Height} | {(VariantIds.Length == 0 ? "base" : string.Join(" + ", ResolveNames()))}";

        public bool Equals(ItemIconBakeVariant other)
        {
            if (other == null || Definition != other.Definition || Width != other.Width || Height != other.Height || ProfileType != other.ProfileType || VariantIds.Length != other.VariantIds.Length)
            {
                return false;
            }

            for (int i = 0; i < VariantIds.Length; i++)
            {
                if (string.Equals(VariantIds[i], other.VariantIds[i], StringComparison.Ordinal) == false)
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as ItemIconBakeVariant);

        public override int GetHashCode()
        {
            BakedItemIconEntry entry = BakedItemIconEntry.Create(Definition, VariantIds, Width, Height, ProfileType, ItemIconGeneratorSettings.LoadDefault(), string.Empty, string.Empty, string.Empty, 0L);
            return unchecked((int)(entry.StableKeyHash ^ (entry.StableKeyHash >> 32)));
        }

        public static int Compare(ItemIconBakeVariant left, ItemIconBakeVariant right)
        {
            int itemComparison = string.CompareOrdinal(AssetDatabase.GetAssetPath(left.Definition), AssetDatabase.GetAssetPath(right.Definition));

            if (itemComparison != 0)
            {
                return itemComparison;
            }

            int profileComparison = left.ProfileType.CompareTo(right.ProfileType);

            if (profileComparison != 0)
            {
                return profileComparison;
            }

            int variantComparison = left.VariantIds.Length.CompareTo(right.VariantIds.Length);

            if (variantComparison != 0)
            {
                return variantComparison;
            }

            int widthComparison = left.Width.CompareTo(right.Width);
            return widthComparison != 0 ? widthComparison : left.Height.CompareTo(right.Height);
        }

        private IEnumerable<string> ResolveNames()
        {
            Dictionary<string, ItemDefinition> definitions = ItemIconVariantEnumerator.GetDefinitionsById();

            foreach (string id in VariantIds)
            {
                yield return definitions.TryGetValue(id, out ItemDefinition definition) ? (definition.ShortName.Length > 0 ? definition.ShortName : definition.DisplayName) : id;
            }
        }
    }
}
