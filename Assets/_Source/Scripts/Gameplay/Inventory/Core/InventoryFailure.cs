namespace RaidIntoDarkness.Inventory.Domain
{
    public enum InventoryFailure
    {
        None,
        InvalidItem,
        ItemNotFound,
        ContainerNotFound,
        OutOfBounds,
        Occupied,
        NoSlot,
        SlotUnavailable,
        SlotRejected,
        ValidatorRejected,
        RotationNotAllowed,
        NotStackable,
        StackFull,
        InvalidAmount,
        NoSpace,
        AlreadyInInventory,
        DependentSlotOccupied,
        Duplicate
    }
}
