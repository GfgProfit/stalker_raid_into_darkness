using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
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
}
