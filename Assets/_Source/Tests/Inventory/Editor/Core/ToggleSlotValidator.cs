namespace RaidIntoDarkness.Inventory.Core.Tests
{
    internal sealed class ToggleSlotValidator : ISlotValidator
    {
        public bool Allow { get; set; }

        public bool CanPlace(ItemInstance item, SlottedContainer container, SlotDefinition slot) => Allow;
    }
}
