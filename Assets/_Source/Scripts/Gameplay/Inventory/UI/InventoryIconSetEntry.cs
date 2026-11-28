using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    [Serializable]
    public sealed class InventoryIconSetEntry
    {
        [SerializeField] private string _id;
        [SerializeField] private Sprite _sprite;

        public InventoryIconSetEntry(string id, Sprite sprite)
        {
            _id = id;
            _sprite = sprite;
        }

        public string Id => _id ?? string.Empty;
        public Sprite Sprite => _sprite;
    }
}
