using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.UI;

namespace RaidIntoDarkness.Inventory
{
    internal sealed class EquipmentAnimationRequest
    {
        public EquipmentAnimationRequestKind Kind { get; set; }
        public ItemInstance Item { get; set; }
        public SlotId SlotId { get; set; }
        public InventoryUnequipTarget Target { get; set; }
    }
}
