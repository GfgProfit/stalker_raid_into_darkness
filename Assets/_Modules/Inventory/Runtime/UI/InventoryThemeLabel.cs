using System;
using UnityEngine;

namespace Inventory.UI
{
    [Serializable]
    public sealed class InventoryThemeLabel
    {
        [SerializeField] private string _key;
        [SerializeField] private string _text;

        public InventoryThemeLabel()
        {
        }

        public InventoryThemeLabel(string key, string text)
        {
            _key = key;
            _text = text;
        }

        public string Key => _key ?? string.Empty;
        public string Text => _text ?? string.Empty;
    }
}
