using System;
using Inventory;
using Inventory.UI;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class OutfitSlotRules : IDisposable
    {
        public const string HELMET_SLOT = "Head";
        public const string BACKPACK_SLOT = "Backpack";

        private readonly InventoryWindow _window;
        private readonly InventoryModel _model;
        private readonly PlayerInventoryRuntime _inventory;

        public OutfitSlotRules(InventoryWindow window, PlayerInventoryRuntime inventory)
        {
            _window = window;
            _model = window.Model;
            _inventory = inventory;
            _model.ItemEquipped += OnEquipmentChanged;
            _model.ItemUnequipped += OnEquipmentChanged;
            _inventory.InventoryRestored += Refresh;
        }

        public void Refresh()
        {
            if (_model.TryGetContainer(_window.EquipmentContainerId, out SlottedContainer equipment) == false)
            {
                return;
            }

            ItemInstance outfit = EquipmentStatsFormatter.FindEquipped(_window, EquipmentStatsFormatter.OUTFIT_CATEGORY);
            EquipmentStatsComponent stats = outfit != null ? outfit.Definition.GetComponent<EquipmentStatsComponent>() : null;
            RefreshSlot(equipment, HELMET_SLOT, stats != null && stats.HelmetAvailable == false);
            if (_inventory.IsRestoring == false)
            {
                RefreshSlot(equipment, BACKPACK_SLOT, stats != null && stats.RepairType == "outfit_exo");
            }
        }

        private void RefreshSlot(SlottedContainer equipment, string slotId, bool locked)
        {
            if (equipment.GetSlot(slotId) == null || equipment.IsSlotLocked(slotId) == locked)
            {
                return;
            }

            ItemInstance item = locked ? equipment.GetSlotOccupant(slotId) : null;

            if (item != null && _model.TryTransfer(item.InstanceId, _window.PrimaryContainerId).Success == false)
            {
                _window.DropItemNow(item);
            }

            _model.TrySetSlotLocked(equipment.Id, slotId, locked);
        }

        public void Dispose()
        {
            _model.ItemEquipped -= OnEquipmentChanged;
            _model.ItemUnequipped -= OnEquipmentChanged;
            _inventory.InventoryRestored -= Refresh;
        }

        private void OnEquipmentChanged(ItemInstance item, SlottedContainer container, SlotDefinition slot)
        {
            if (container.Id == _window.EquipmentContainerId && item != null && item.Definition.IsCategory(EquipmentStatsFormatter.OUTFIT_CATEGORY))
            {
                Refresh();
            }
        }
    }
}
