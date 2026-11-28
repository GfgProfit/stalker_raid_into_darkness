namespace RaidIntoDarkness.Inventory.Domain
{
    internal interface IOutfitDropSource
    {
        bool CanPrepare(ItemInstance item);
        InventoryResult TryPrepare(ItemInstance item, out IPreparedOutfitDrop drop);
    }
}
