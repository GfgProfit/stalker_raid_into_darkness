namespace RaidIntoDarkness.Inventory.Domain
{
    public interface IInventoryPlacementValidator
    {
        bool CanPlace(ItemInstance item, InventoryContainer container, ItemPlacement placement);
    }
}
