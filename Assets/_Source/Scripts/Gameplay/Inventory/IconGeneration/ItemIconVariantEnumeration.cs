using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
    public static class ItemIconVariantEnumeration
    {
        public const int LARGE_VARIANT_WARNING = 256;

        public static List<string[]> EnumerateVariants(ItemDefinition definition)
        {
            var variants = new List<string[]> { Array.Empty<string>() };

            if (definition == null || definition.VisualPrefab == null)
            {
                return variants;
            }

            ItemIconVariantPart[] parts = definition.VisualPrefab.GetComponentsInChildren<ItemIconVariantPart>(true);
            var uniqueParts = new Dictionary<string, ItemIconVariantPart>(StringComparer.Ordinal);

            foreach (ItemIconVariantPart part in parts)
            {
                if (part != null && string.IsNullOrEmpty(part.PartId) == false && uniqueParts.ContainsKey(part.PartId) == false)
                {
                    uniqueParts.Add(part.PartId, part);
                }
            }

            if (uniqueParts.Count == 0)
            {
                return variants;
            }

            List<string> defaultIds = ItemIconVariantKeyCache.ToIds(definition.DefaultIconVariant);
            var occupiedGroups = new HashSet<string>(StringComparer.Ordinal);

            foreach (string defaultId in defaultIds)
            {
                if (uniqueParts.TryGetValue(defaultId, out ItemIconVariantPart part))
                {
                    occupiedGroups.Add(part.Group);
                }
            }

            var groups = new SortedDictionary<string, List<ItemIconVariantPart>>(StringComparer.Ordinal);

            foreach (ItemIconVariantPart part in uniqueParts.Values)
            {
                if (occupiedGroups.Contains(part.Group))
                {
                    continue;
                }

                if (groups.TryGetValue(part.Group, out List<ItemIconVariantPart> list) == false)
                {
                    list = new List<ItemIconVariantPart>();
                    groups.Add(part.Group, list);
                }

                list.Add(part);
            }

            var orderedGroups = new List<List<ItemIconVariantPart>>(groups.Values);

            foreach (List<ItemIconVariantPart> group in orderedGroups)
            {
                group.Sort((left, right) => string.CompareOrdinal(left.PartId, right.PartId));
            }

            var selected = new List<string>();
            var effective = new List<string>(defaultIds);
            Enumerate(0, orderedGroups, uniqueParts, selected, effective, variants);
            return variants;
        }

        public static Vector2Int CalculateSizeDelta(ItemDefinition definition, IReadOnlyList<string> variantIds)
        {
            var delta = Vector2Int.zero;

            if (definition == null || definition.VisualPrefab == null || variantIds == null || variantIds.Count == 0)
            {
                return delta;
            }

            var counted = new HashSet<string>(StringComparer.Ordinal);

            foreach (ItemIconVariantPart part in definition.VisualPrefab.GetComponentsInChildren<ItemIconVariantPart>(true))
            {
                if (part != null && counted.Add(part.PartId) && Contains(variantIds, part.PartId))
                {
                    delta += part.SizeDelta;
                }
            }

            return delta;
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Enumerate(int groupIndex, List<List<ItemIconVariantPart>> groups, Dictionary<string, ItemIconVariantPart> parts, List<string> selected, List<string> effective, List<string[]> variants)
        {
            if (groupIndex >= groups.Count)
            {
                if (selected.Count > 0 && IsSatisfied(effective, parts))
                {
                    variants.Add(ItemIconVariantKeyCache.BuildNormalizedIds(selected));
                }

                return;
            }

            Enumerate(groupIndex + 1, groups, parts, selected, effective, variants);

            foreach (ItemIconVariantPart part in groups[groupIndex])
            {
                selected.Add(part.PartId);
                effective.Add(part.PartId);
                Enumerate(groupIndex + 1, groups, parts, selected, effective, variants);
                effective.RemoveAt(effective.Count - 1);
                selected.RemoveAt(selected.Count - 1);
            }
        }

        private static bool IsSatisfied(List<string> effective, Dictionary<string, ItemIconVariantPart> parts)
        {
            foreach (string id in effective)
            {
                if (parts.TryGetValue(id, out ItemIconVariantPart part) && part.ConfigurationSatisfied(effective) == false)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
