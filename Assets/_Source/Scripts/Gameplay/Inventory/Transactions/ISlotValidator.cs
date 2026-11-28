namespace RaidIntoDarkness.Inventory.Domain
{
    public interface ISlotValidator
    {
        bool CanPlace(ItemInstance item, SlottedContainer container, SlotDefinition slot);
    }
}
