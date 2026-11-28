using System.Collections.Generic;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal readonly struct OutfitSlotRelocation
    {
        public OutfitSlotRelocation(ItemInstance item, ItemPlacement placement, List<KeyValuePair<ItemInstance, int>> merges, int remainder)
        {
            Item = item;
            Placement = placement;
            Merges = merges;
            Remainder = remainder;
            Drop = null;
        }

        public OutfitSlotRelocation(ItemInstance item, IPreparedOutfitDrop drop)
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
        public IPreparedOutfitDrop Drop { get; }
    }
}
