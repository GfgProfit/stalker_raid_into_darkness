namespace RaidIntoDarkness.Inventory
{
    public interface IInventoryPlacementValidator
    {
        bool CanPlace(ItemInstance item, InventoryContainer container, ItemPlacement placement);
    }
}
