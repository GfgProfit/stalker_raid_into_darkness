using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    public readonly struct InventoryContextMenuEntry
    {
        public InventoryContextMenuEntry(string label, bool enabled, Action callback, Sprite icon = null, int group = 0)
        {
            Label = label ?? string.Empty;
            Enabled = enabled;
            Callback = callback;
            Icon = icon;
            Group = group;
        }

        public string Label { get; }
        public bool Enabled { get; }
        public Action Callback { get; }
        public Sprite Icon { get; }
        public int Group { get; }
    }
}
