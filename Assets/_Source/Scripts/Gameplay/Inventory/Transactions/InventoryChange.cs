namespace RaidIntoDarkness.Inventory
{
    public readonly struct InventoryChange
    {
        public InventoryChange(InventoryChangeKind kind, ItemInstance item, string containerId, ItemLocation previousLocation, SlotId slotId = SlotId.None)
        {
            Kind = kind;
            Item = item;
            ContainerId = containerId ?? string.Empty;
            PreviousLocation = previousLocation;
            SlotId = slotId;
        }

        public InventoryChangeKind Kind { get; }
        public ItemInstance Item { get; }
        public string ContainerId { get; }
        public ItemLocation PreviousLocation { get; }
        public SlotId SlotId { get; }
        public string PreviousContainerId => PreviousLocation.ContainerId;

        public bool Affects(string containerId) => ContainerId == containerId || PreviousLocation.ContainerId == containerId;
    }
}
