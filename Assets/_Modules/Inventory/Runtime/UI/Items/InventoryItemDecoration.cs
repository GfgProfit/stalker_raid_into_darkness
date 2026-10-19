using UnityEngine;

namespace Inventory.UI
{
    public readonly struct InventoryItemDecoration
    {
        public InventoryItemDecoration(Color tint, Sprite badge)
        {
            Tint = tint;
            Badge = badge;
        }

        public Color Tint { get; }
        public Sprite Badge { get; }
        public bool HasTint => Tint.a > 0f;

        public static InventoryItemDecoration None => default;
    }
}
