namespace Inventory.UI
{
    public interface IInventoryItemDropHandler
    {
        bool Handles(ItemInstance source, ItemInstance target);
        InventoryResult Preview(ItemInstance source, ItemInstance target);
        InventoryResult Apply(ItemInstance source, ItemInstance target);
    }
}
