namespace RaidIntoDarkness.Inventory.Domain
{
    internal interface IPreparedOutfitDrop
    {
        void Complete(ItemInstance item);
        void Cancel();
    }
}
