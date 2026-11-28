namespace RaidIntoDarkness.Inventory
{
    public interface ISlotValidator
    {
        bool CanPlace(ItemInstance item, SlottedContainer container, SlotDefinition slot);
    }
}
