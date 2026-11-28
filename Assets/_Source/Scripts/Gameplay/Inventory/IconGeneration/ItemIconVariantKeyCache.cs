using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RaidIntoDarkness.Inventory.IconGeneration
{
    internal static class ItemIconVariantKeyCache
    {
        private static readonly ConditionalWeakTable<object, CachedVariantKey> _cache = new();

        public static string GetCanonicalKey(IReadOnlyList<string> variantIds)
        {
            if (variantIds == null || variantIds.Count == 0)
            {
                return string.Empty;
            }

            object owner = variantIds;

            if (_cache.TryGetValue(owner, out CachedVariantKey cached) && cached.Matches(variantIds))
            {
                return cached.CanonicalKey;
            }

            string[] normalizedIds = BuildNormalizedIds(variantIds);
            var replacement = new CachedVariantKey(normalizedIds);
            _cache.Remove(owner);
            _cache.Add(owner, replacement);
            return replacement.CanonicalKey;
        }

        public static string GetCanonicalKey(IReadOnlyList<ItemDefinition> definitions) => BuildCanonicalKey(BuildNormalizedIds(ToIds(definitions)));

        public static string[] BuildNormalizedIds(IReadOnlyList<string> variantIds)
        {
            if (variantIds == null || variantIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var ids = new List<string>(variantIds.Count);

            for (int i = 0; i < variantIds.Count; i++)
            {
                string id = variantIds[i];

                if (string.IsNullOrEmpty(id) == false && ids.Contains(id) == false)
                {
                    ids.Add(id);
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        public static string BuildCanonicalKey(IReadOnlyList<string> normalizedIds)
        {
            return normalizedIds == null || normalizedIds.Count == 0 ? string.Empty : string.Join("|", normalizedIds);
        }

        public static List<string> ToIds(IReadOnlyList<ItemDefinition> definitions)
        {
            var ids = new List<string>();

            if (definitions == null)
            {
                return ids;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null && string.IsNullOrEmpty(definitions[i].DefinitionId) == false)
                {
                    ids.Add(definitions[i].DefinitionId);
                }
            }

            return ids;
        }
    }
}
