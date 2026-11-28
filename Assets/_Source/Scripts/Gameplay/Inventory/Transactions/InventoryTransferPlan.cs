using System.Collections.Generic;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal readonly struct InventoryTransferPlan
    {
        public InventoryTransferPlan(ItemInstance item, InventoryContainer source, InventoryContainer target, int moving, int remainder, bool whole, ItemInstance carrier, List<KeyValuePair<ItemInstance, int>> merges, ItemPlacement placement)
        {
            Item = item;
            Source = source;
            Target = target;
            Moving = moving;
            Remainder = remainder;
            Whole = whole;
            Carrier = carrier;
            Merges = merges;
            Placement = placement;
        }

        public ItemInstance Item { get; }
        public InventoryContainer Source { get; }
        public InventoryContainer Target { get; }
        public int Moving { get; }
        public int Remainder { get; }
        public bool Whole { get; }
        public ItemInstance Carrier { get; }
        public List<KeyValuePair<ItemInstance, int>> Merges { get; }
        public ItemPlacement Placement { get; }
    }
}
