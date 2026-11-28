using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    internal readonly struct InventoryGridQuad
    {
        public InventoryGridQuad(Rect rect, Color color)
        {
            Rect = rect;
            Color = color;
        }

        public Rect Rect { get; }
        public Color Color { get; }
    }
}
