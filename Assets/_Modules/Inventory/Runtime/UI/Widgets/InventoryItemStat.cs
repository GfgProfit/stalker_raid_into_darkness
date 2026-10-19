using UnityEngine;

namespace Inventory.UI
{
    public enum InventoryStatTone
    {
        Normal = 0,
        Negative = 1,
        Better = 2,
        Worse = 3,
        Highlight = 4
    }

    public readonly struct InventoryItemStat
    {
        public InventoryItemStat(string label, string value, Sprite icon = null, InventoryStatTone tone = InventoryStatTone.Normal, string comparison = null, float fill = -1f)
        {
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
            Icon = icon;
            Tone = tone;
            Comparison = comparison ?? string.Empty;
            Fill = fill;
        }

        public string Label { get; }
        public string Value { get; }
        public Sprite Icon { get; }
        public InventoryStatTone Tone { get; }
        public string Comparison { get; }
        public float Fill { get; }
        public bool HasBar => Fill >= 0f;
    }
}
