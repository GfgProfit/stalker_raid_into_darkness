namespace RaidIntoDarkness.Inventory.Domain
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
        ItemStateChanged,
        ContainerConstraintsChanged
    }
}
