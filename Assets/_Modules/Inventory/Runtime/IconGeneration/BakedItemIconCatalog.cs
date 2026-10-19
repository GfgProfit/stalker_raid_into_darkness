using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory.IconGeneration
{
    [CreateAssetMenu(fileName = DEFAULT_RESOURCE_PATH, menuName = "Inventory/Baked Item Icon Catalog")]
    public sealed class BakedItemIconCatalog : ScriptableObject
    {
        public const string DEFAULT_RESOURCE_PATH = "BakedItemIconCatalog";

        private static BakedItemIconCatalog _defaultCatalog;
        private static bool _defaultCatalogLoaded;

        [SerializeField] private List<BakedItemIconEntry> _entries = new();

        private Dictionary<IconCacheKey, BakedItemIconEntry> _lookup;

        public IReadOnlyList<BakedItemIconEntry> Entries => _entries;

        internal bool TryGetSprite(IconCacheKey key, out Sprite sprite)
        {
            EnsureLookup();

            if (_lookup.TryGetValue(key, out BakedItemIconEntry entry) == false)
            {
                sprite = null;
                return false;
            }

            return entry.TryLoadSprite(out sprite);
        }

        public void ReplaceEntries(IReadOnlyList<BakedItemIconEntry> entries)
        {
            _entries.Clear();

            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null)
                    {
                        _entries.Add(entries[i]);
                    }
                }
            }

            _lookup = null;
        }

        public void UpsertEntries(IReadOnlyList<BakedItemIconEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            var existingIndices = new Dictionary<IconCacheKey, int>();

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i] != null && _entries[i].TryBuildKey(out IconCacheKey key))
                {
                    existingIndices[key] = i;
                }
            }

            for (int i = 0; i < entries.Count; i++)
            {
                BakedItemIconEntry entry = entries[i];

                if (entry == null || entry.TryBuildKey(out IconCacheKey key) == false)
                {
                    continue;
                }

                if (existingIndices.TryGetValue(key, out int index))
                {
                    _entries[index] = entry;
                }
                else
                {
                    existingIndices.Add(key, _entries.Count);
                    _entries.Add(entry);
                }
            }

            _lookup = null;
        }

        public void ClearEntries()
        {
            _entries.Clear();
            _lookup = null;
        }

        public static BakedItemIconCatalog LoadDefault()
        {
            if (_defaultCatalogLoaded)
            {
                return _defaultCatalog;
            }

            _defaultCatalogLoaded = true;
            _defaultCatalog = Resources.Load<BakedItemIconCatalog>(DEFAULT_RESOURCE_PATH);
            return _defaultCatalog;
        }

        public static void ResetDefaultCache()
        {
            _defaultCatalogLoaded = false;
            _defaultCatalog = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => ResetDefaultCache();

        private void OnEnable() => _lookup = null;
        private void OnValidate() => _lookup = null;

        private void EnsureLookup()
        {
            if (_lookup != null)
            {
                return;
            }

            _lookup = new Dictionary<IconCacheKey, BakedItemIconEntry>(_entries.Count);

            for (int i = 0; i < _entries.Count; i++)
            {
                BakedItemIconEntry entry = _entries[i];

                if (entry != null && entry.TryBuildKey(out IconCacheKey key) && _lookup.ContainsKey(key) == false)
                {
                    _lookup.Add(key, entry);
                }
            }
        }
    }

    [Serializable]
    public sealed class BakedItemIconEntry
    {
        [SerializeField] private string _itemId;
        [SerializeField] private List<string> _variantIds = new();
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private ItemIconProfileType _profileType;
        [SerializeField] private string _visualSignature;
        [SerializeField] private string _spriteResourcePath;
        [SerializeField] private string _bakeSignature;
        [SerializeField] private string _sourceItemAssetGuid;
        [SerializeField] private long _estimatedTextureBytes;

        [NonSerialized] private string _canonicalVariantIds;
        [NonSerialized] private Sprite _loadedSprite;

        public BakedItemIconEntry(string itemId, IReadOnlyList<string> variantIds, int width, int height, ItemIconProfileType profileType, ulong visualSignature, string spriteResourcePath, string bakeSignature, string sourceItemAssetGuid, long estimatedTextureBytes)
        {
            _itemId = itemId ?? string.Empty;
            _variantIds = variantIds == null ? new List<string>() : new List<string>(variantIds);
            _variantIds.Sort(StringComparer.Ordinal);
            _width = Mathf.Max(1, width);
            _height = Mathf.Max(1, height);
            _profileType = profileType;
            _visualSignature = ItemIconStableHash.ToHex(visualSignature);
            _spriteResourcePath = spriteResourcePath ?? string.Empty;
            _bakeSignature = bakeSignature ?? string.Empty;
            _sourceItemAssetGuid = sourceItemAssetGuid ?? string.Empty;
            _estimatedTextureBytes = Math.Max(0L, estimatedTextureBytes);
        }

        public string ItemId => _itemId ?? string.Empty;
        public IReadOnlyList<string> VariantIds => _variantIds;
        public int Width => Mathf.Max(1, _width);
        public int Height => Mathf.Max(1, _height);
        public ItemIconProfileType ProfileType => _profileType;
        public string VisualSignature => _visualSignature ?? string.Empty;
        public string SpriteResourcePath => _spriteResourcePath ?? string.Empty;
        public string BakeSignature => _bakeSignature ?? string.Empty;
        public string SourceItemAssetGuid => _sourceItemAssetGuid ?? string.Empty;
        public long EstimatedTextureBytes => Math.Max(0L, _estimatedTextureBytes);
        public ulong StableKeyHash => TryBuildKey(out IconCacheKey key) ? key.StableHash : 0UL;

        public static BakedItemIconEntry Create(ItemDefinition definition, IReadOnlyList<string> variantIds, int width, int height, ItemIconProfileType profileType, ItemIconGeneratorSettings settings, string spriteResourcePath, string bakeSignature, string sourceItemAssetGuid, long estimatedTextureBytes)
        {
            IconRenderProfile renderProfile = IconRenderProfile.Create(definition, width, height, profileType, settings);
            IconCacheKey key = ItemIconStableKeyBuilder.Build(definition, variantIds, settings, renderProfile);
            return new BakedItemIconEntry(key.ItemId, ItemIconVariantKeyCache.BuildNormalizedIds(variantIds), key.Width, key.Height, key.ProfileType, key.VisualSignature, spriteResourcePath, bakeSignature, sourceItemAssetGuid, estimatedTextureBytes);
        }

        internal bool TryBuildKey(out IconCacheKey key)
        {
            if (string.IsNullOrEmpty(ItemId) || ItemIconStableHash.TryParseHex(VisualSignature, out ulong visualSignature) == false)
            {
                key = default;
                return false;
            }

            _canonicalVariantIds ??= ItemIconVariantKeyCache.BuildCanonicalKey(_variantIds);
            key = new IconCacheKey(ItemId, _canonicalVariantIds, Width, Height, ProfileType, visualSignature);
            return true;
        }

        internal bool TryLoadSprite(out Sprite sprite)
        {
            if (_loadedSprite == null && string.IsNullOrEmpty(SpriteResourcePath) == false)
            {
                _loadedSprite = Resources.Load<Sprite>(SpriteResourcePath);
            }

            sprite = _loadedSprite;
            return sprite != null;
        }
    }

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
