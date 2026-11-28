using System.Collections.Generic;

namespace RaidIntoDarkness.Inventory
{
    internal readonly struct OutfitSlotRelocation
    {
        public OutfitSlotRelocation(ItemInstance item, ItemPlacement placement, List<KeyValuePair<ItemInstance, int>> merges, int remainder)
        {
            Item = item;
            Placement = placement;
            Merges = merges;
            Remainder = remainder;
            Drop = default;
        }

        public OutfitSlotRelocation(ItemInstance item, PreparedWorldItemDrop drop)
        {
            Item = item;
            Placement = default;
            Merges = null;
            Remainder = 0;
            Drop = drop;
        }

        public ItemInstance Item { get; }
        public ItemPlacement Placement { get; }
        public List<KeyValuePair<ItemInstance, int>> Merges { get; }
        public int Remainder { get; }
        public PreparedWorldItemDrop Drop { get; }
    }
}
