using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    public readonly struct InventoryActionStyle
    {
        public InventoryActionStyle(Sprite icon, int group)
        {
            Icon = icon;
            Group = group;
        }

        public Sprite Icon { get; }
        public int Group { get; }
    }
}
