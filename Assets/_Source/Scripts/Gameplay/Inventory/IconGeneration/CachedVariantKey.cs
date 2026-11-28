using System;
using System.Collections.Generic;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
    internal sealed class CachedVariantKey
    {
        private readonly string[] _normalizedIds;

        public CachedVariantKey(string[] normalizedIds)
        {
            _normalizedIds = normalizedIds;
            CanonicalKey = ItemIconVariantKeyCache.BuildCanonicalKey(normalizedIds);
        }

        public string CanonicalKey { get; }

        public bool Matches(IReadOnlyList<string> variantIds)
        {
            if (variantIds == null)
            {
                return _normalizedIds.Length == 0;
            }

            int uniqueCount = 0;

            for (int i = 0; i < variantIds.Count; i++)
            {
                string id = variantIds[i];

                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                bool duplicate = false;

                for (int previousIndex = 0; previousIndex < i; previousIndex++)
                {
                    if (string.Equals(variantIds[previousIndex], id, StringComparison.Ordinal))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    continue;
                }

                uniqueCount++;

                if (Array.BinarySearch(_normalizedIds, id, StringComparer.Ordinal) < 0)
                {
                    return false;
                }
            }

            return uniqueCount == _normalizedIds.Length;
        }
    }
}
