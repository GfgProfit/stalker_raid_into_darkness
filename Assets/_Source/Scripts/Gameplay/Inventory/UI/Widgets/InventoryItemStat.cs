using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    public readonly struct InventoryItemStat
    {
        public InventoryItemStat(string label, string value, Sprite icon = null, InventoryStatTone tone = InventoryStatTone.Normal, string comparison = null, float fill = -1f, float currentFill = -1f)
        {
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
            Icon = icon;
            Tone = tone;
            Comparison = comparison ?? string.Empty;
            Fill = fill;
            CurrentFill = currentFill;
        }

        public string Label { get; }
        public string Value { get; }
        public Sprite Icon { get; }
        public InventoryStatTone Tone { get; }
        public string Comparison { get; }
        public float Fill { get; }
        public float CurrentFill { get; }
        public bool HasBar => Fill >= 0f;
        public bool HasCurrentBar => HasBar && CurrentFill >= 0f;
    }
}
