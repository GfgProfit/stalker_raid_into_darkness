using UnityEngine;

namespace Inventory.UI
{
    public interface IInventoryEquipmentGate
    {
        bool HandleEquip(ItemInstance item, SlotDefinition slot);
        bool HandleUnequip(ItemInstance item, InventoryUnequipTarget target);
        bool HandleDrop(ItemInstance item);
    }

    public readonly struct InventoryUnequipTarget
    {
        public InventoryUnequipTarget(string containerId, Vector2Int origin, bool rotated, bool autoPlace)
        {
            ContainerId = containerId;
            Origin = origin;
            Rotated = rotated;
            AutoPlace = autoPlace;
        }

        public string ContainerId { get; }
        public Vector2Int Origin { get; }
        public bool Rotated { get; }
        public bool AutoPlace { get; }

        public static InventoryUnequipTarget Auto(string containerId) => new(containerId, Vector2Int.zero, false, true);
    }
}
