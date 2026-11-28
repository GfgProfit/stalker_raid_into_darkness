using System;
using RaidIntoDarkness.Inventory;

namespace RaidIntoDarkness.Inventory
{
    public sealed class OutfitSlotRules : IDisposable
    {
        private readonly InventoryModel _model;
        private readonly PlayerInventoryRuntime _inventory;

        internal OutfitSlotRules(PlayerInventoryRuntime inventory)
        {
            _inventory = inventory;
            _model = inventory.Model;
            _model.ItemEquipped += OnEquipmentChanged;
            _model.ItemUnequipped += OnEquipmentChanged;
        }

        public void Refresh()
        {
            if (_model.TryGetContainer(_inventory.EquipmentContainerId, out SlottedContainer equipment) == false)
            {
                return;
            }

            ItemInstance outfit = FindEquipped(equipment, AnomalyEquipmentProtection.OUTFIT_CATEGORY);
            EquipmentStatsComponent stats = outfit != null ? outfit.Definition.GetComponent<EquipmentStatsComponent>() : null;
            RefreshSlot(equipment, SlotId.Head, stats != null && stats.HelmetAvailable == false);
            RefreshSlot(equipment, SlotId.Backpack, stats != null && stats.RepairType == "outfit_exo");
        }

        internal static ItemInstance FindEquipped(InventoryContainer equipment, string category)
        {
            if (equipment != null)
            {
                foreach (ItemInstance item in equipment.Items)
                {
                    if (item.Definition.IsCategory(category))
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        private void RefreshSlot(SlottedContainer equipment, SlotId slotId, bool locked)
        {
            if (equipment.GetSlot(slotId) == null || equipment.IsSlotLocked(slotId) == locked)
            {
                return;
            }

            ItemInstance item = locked ? equipment.GetSlotOccupant(slotId) : null;

            if (item != null && _model.TryTransfer(item.InstanceId, _inventory.PrimaryContainerId).Success == false)
            {
                _inventory.DropBlockedEquipment(item);
            }

            _model.TrySetSlotLocked(equipment.Id, slotId, locked);
        }

        public void Dispose()
        {
            _model.ItemEquipped -= OnEquipmentChanged;
            _model.ItemUnequipped -= OnEquipmentChanged;
        }

        private void OnEquipmentChanged(ItemInstance item, SlottedContainer container, SlotDefinition slot)
        {
            if (container.Id == _inventory.EquipmentContainerId && item != null && item.Definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY))
            {
                Refresh();
            }
        }
    }
}
