using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventoryTooltipStatRow
    {
        public InventoryTooltipStatRow(RectTransform root, Image icon, TextMeshProUGUI label, Image back, Image fill, TextMeshProUGUI value, TextMeshProUGUI comparison)
        {
            Root = root;
            Icon = icon;
            Label = label;
            Back = back;
            Fill = fill;
            Value = value;
            Comparison = comparison;
        }

        public RectTransform Root { get; }
        public Image Icon { get; }
        public TextMeshProUGUI Label { get; }
        public Image Back { get; }
        public Image Fill { get; }
        public TextMeshProUGUI Value { get; }
        public TextMeshProUGUI Comparison { get; }
        public Image DeltaFill { get; set; }
    }
}
