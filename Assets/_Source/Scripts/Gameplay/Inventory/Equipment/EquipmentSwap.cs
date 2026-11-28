namespace RaidIntoDarkness.Inventory
{
    internal readonly struct EquipmentSwap
    {
        public EquipmentSwap(ItemInstance displacedItem, InventoryContainer displacedDestination, ItemPlacement displacedPlacement, ItemPlacement incomingPlacement)
        {
            DisplacedItem = displacedItem;
            DisplacedDestination = displacedDestination;
            DisplacedPlacement = displacedPlacement;
            IncomingPlacement = incomingPlacement;
        }

        public ItemInstance DisplacedItem { get; }
        public InventoryContainer DisplacedDestination { get; }
        public ItemPlacement DisplacedPlacement { get; }
        public ItemPlacement IncomingPlacement { get; }
    }
}
