using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.Application
{
    public interface IEquipmentAnimationRequests
    {
        bool HandleEquip(ItemInstance item, SlotDefinition slot);
        bool HandleUnequip(ItemInstance item, InventoryUnequipTarget target);
        bool HandleDrop(ItemInstance item);
    }
}
