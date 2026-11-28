using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.UI;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    internal sealed class EquipmentAnimationRequest
    {
        public EquipmentAnimationRequestKind Kind { get; set; }
        public ItemInstance Item { get; set; }
        public SlotId SlotId { get; set; }
        public InventoryUnequipTarget Target { get; set; }
    }
}
