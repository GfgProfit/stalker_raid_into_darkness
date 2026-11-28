using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    [CreateAssetMenu(menuName = "Inventory/Icon Set", fileName = "InventoryIconSet")]
    public sealed class InventoryIconSet : ScriptableObject
    {
        [SerializeField] private List<InventoryIconSetEntry> _entries = new();

        public IReadOnlyList<InventoryIconSetEntry> Entries => _entries;

        public Sprite Get(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].Id, id, StringComparison.Ordinal))
                {
                    return _entries[i].Sprite;
                }
            }

            return null;
        }

        public void SetEntries(IEnumerable<InventoryIconSetEntry> entries) => _entries = new List<InventoryIconSetEntry>(entries);
    }
}
