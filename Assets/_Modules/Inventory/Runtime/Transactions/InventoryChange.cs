namespace Inventory
{
    public enum InventoryChangeKind
    {
        ItemAdded,
        ItemRemoved,
        ItemMoved,
        AmountChanged,
        SlotStateChanged,
        ContainerAdded,
        ContainerRemoved,
        Cleared,
        ItemStateChanged
    }

    public readonly struct InventoryChange
    {
        public InventoryChange(InventoryChangeKind kind, ItemInstance item, string containerId, ItemLocation previousLocation, string slotId = null)
        {
            Kind = kind;
            Item = item;
            ContainerId = containerId ?? string.Empty;
            PreviousLocation = previousLocation;
            SlotId = slotId ?? string.Empty;
        }

        public InventoryChangeKind Kind { get; }
        public ItemInstance Item { get; }
        public string ContainerId { get; }
        public ItemLocation PreviousLocation { get; }
        public string SlotId { get; }
        public string PreviousContainerId => PreviousLocation.ContainerId;

        public bool Affects(string containerId) => ContainerId == containerId || PreviousLocation.ContainerId == containerId;
    }

    public readonly struct InventoryResult
    {
        private InventoryResult(bool success, InventoryFailure failure, ItemInstance item, int amount)
        {
            Success = success;
            Failure = failure;
            Item = item;
            Amount = amount;
        }

        public bool Success { get; }
        public InventoryFailure Failure { get; }
        public ItemInstance Item { get; }
        public int Amount { get; }

        public static InventoryResult Ok(ItemInstance item, int amount) => new(true, InventoryFailure.None, item, amount);
        public static InventoryResult Fail(InventoryFailure failure) => new(false, failure, null, 0);

        public static implicit operator bool(InventoryResult result) => result.Success;
        public override string ToString() => Success ? $"Ok({Item}, {Amount})" : $"Fail({Failure})";
    }

    public interface ISlotValidator
    {
        bool CanPlace(ItemInstance item, SlottedContainer container, SlotDefinition slot);
    }

    public interface IInventoryPlacementValidator
    {
        bool CanPlace(ItemInstance item, InventoryContainer container, ItemPlacement placement);
    }
}
