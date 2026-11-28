namespace RaidIntoDarkness.Inventory.Domain
{
    internal readonly struct EquipEvent
    {
        public EquipEvent(bool equipped, ItemInstance item, SlottedContainer container, SlotDefinition slot)
        {
            Equipped = equipped;
            Item = item;
            Container = container;
            Slot = slot;
        }

        public bool Equipped { get; }
        public ItemInstance Item { get; }
        public SlottedContainer Container { get; }
        public SlotDefinition Slot { get; }
    }
}
