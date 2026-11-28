using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventoryContextMenuLayoutRow
    {
        public InventoryContextMenuLayoutRow(RectTransform root, Image icon, TextMeshProUGUI label, Image divider, InventoryContextMenuRow handler)
        {
            Root = root;
            Icon = icon;
            Label = label;
            Divider = divider;
            Handler = handler;
        }

        public RectTransform Root { get; }
        public Image Icon { get; }
        public TextMeshProUGUI Label { get; }
        public Image Divider { get; }
        public InventoryContextMenuRow Handler { get; }
        public bool IsDivider { get; set; }
        public int EntryIndex { get; set; } = -1;
        public float Top { get; set; }
        public float Height { get; set; }
    }
}
