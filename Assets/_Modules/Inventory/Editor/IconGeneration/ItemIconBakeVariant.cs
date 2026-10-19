using System;
using System.Collections.Generic;
using Inventory.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace Inventory.Editor
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

    internal sealed class ItemIconVariantAnalysis
    {
        public readonly List<ItemIconBakeVariant> Variants = new();
        public readonly Dictionary<ItemDefinition, int> VariantCountsByItem = new();
        public readonly List<ItemDefinition> ExcessiveVariantItems = new();

        public int DefaultVariantCount => Variants.FindAll(variant => variant.ProfileType == ItemIconProfileType.Default).Count;
        public int SlotVariantCount => Variants.Count - DefaultVariantCount;

        public long EstimatedTextureBytes
        {
            get
            {
                long bytes = 0L;

                foreach (ItemIconBakeVariant variant in Variants)
                {
                    bytes += variant.EstimatedTextureBytes;
                }

                return bytes;
            }
        }
    }

    internal static class ItemIconVariantEnumerator
    {
        private static Dictionary<string, ItemDefinition> _definitionsById;
        private static int _definitionsFrame = -1;

        public static ItemIconVariantAnalysis Enumerate(IReadOnlyList<ItemDefinition> definitions, ItemIconGeneratorSettings settings)
        {
            InvalidateDefinitions();
            var analysis = new ItemIconVariantAnalysis();
            var unique = new HashSet<ItemIconBakeVariant>();

            foreach (ItemDefinition definition in definitions)
            {
                if (definition == null || definition.CanGenerateIcon == false || ItemDefinitionIdValidator.IsValidId(definition.DefinitionId) == false)
                {
                    continue;
                }

                int before = analysis.Variants.Count;

                foreach (string[] variant in ItemIconVariantEnumeration.EnumerateVariants(definition))
                {
                    Vector2Int sizeDelta = ItemIconVariantEnumeration.CalculateSizeDelta(definition, variant);
                    AddUnique(analysis.Variants, unique, new ItemIconBakeVariant(definition, variant, Mathf.Max(1, definition.Width + sizeDelta.x), Mathf.Max(1, definition.Height + sizeDelta.y), ItemIconProfileType.Default, settings.IconRenderScale));

                    foreach (ItemIconSlotProfile slotProfile in settings.PrewarmSlotProfiles)
                    {
                        if (slotProfile.Accepts(definition))
                        {
                            AddUnique(analysis.Variants, unique, new ItemIconBakeVariant(definition, variant, slotProfile.Width, slotProfile.Height, ItemIconProfileType.Slot, settings.IconRenderScale));
                        }
                    }
                }

                int count = analysis.Variants.Count - before;
                analysis.VariantCountsByItem[definition] = count;

                if (count > ItemIconVariantEnumeration.LARGE_VARIANT_WARNING)
                {
                    analysis.ExcessiveVariantItems.Add(definition);
                }
            }

            analysis.Variants.Sort(ItemIconBakeVariant.Compare);
            return analysis;
        }

        public static ItemIconVariantAnalysis Enumerate(ItemDefinition definition, ItemIconGeneratorSettings settings) => Enumerate(definition == null ? Array.Empty<ItemDefinition>() : new[] { definition }, settings);

        public static void InvalidateDefinitions()
        {
            _definitionsById = null;
            _definitionsFrame = -1;
        }

        public static Dictionary<string, ItemDefinition> GetDefinitionsById()
        {
            if (_definitionsById != null && _definitionsFrame == Time.frameCount)
            {
                return _definitionsById;
            }

            _definitionsById = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

            foreach (ItemDefinition definition in ItemDefinitionIdValidator.LoadAllDefinitions())
            {
                if (string.IsNullOrEmpty(definition.DefinitionId) == false)
                {
                    _definitionsById[definition.DefinitionId] = definition;
                }
            }

            _definitionsFrame = Time.frameCount;
            return _definitionsById;
        }

        private static void AddUnique(List<ItemIconBakeVariant> variants, HashSet<ItemIconBakeVariant> unique, ItemIconBakeVariant variant)
        {
            if (unique.Add(variant))
            {
                variants.Add(variant);
            }
        }
    }
}
