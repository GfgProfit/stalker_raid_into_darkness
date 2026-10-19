using System;
using Inventory;
using Inventory.UI;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class OutfitHelmetRule : IDisposable
    {
        public const string HELMET_SLOT = "Head";

        private readonly InventoryWindow _window;
        private readonly InventoryModel _model;

        public OutfitHelmetRule(InventoryWindow window)
        {
            _window = window;
            _model = window.Model;
            _model.ItemEquipped += OnEquipmentChanged;
            _model.ItemUnequipped += OnEquipmentChanged;
        }

        public void Refresh()
        {
            if (_model.TryGetContainer(_window.EquipmentContainerId, out SlottedContainer equipment) == false || equipment.GetSlot(HELMET_SLOT) == null)
            {
                return;
            }

            ItemInstance outfit = EquipmentStatsFormatter.FindEquipped(_window, EquipmentStatsFormatter.OUTFIT_CATEGORY);
            EquipmentStatsComponent stats = outfit != null ? outfit.Definition.GetComponent<EquipmentStatsComponent>() : null;
            bool locked = stats != null && stats.HelmetAvailable == false;

            if (equipment.IsSlotLocked(HELMET_SLOT) == locked)
            {
                return;
            }

            ItemInstance helmet = locked ? equipment.GetSlotOccupant(HELMET_SLOT) : null;

            if (helmet != null && _model.TryTransfer(helmet.InstanceId, _window.PrimaryContainerId).Success == false)
            {
                _window.DropItem(helmet);
            }

            _model.TrySetSlotLocked(_window.EquipmentContainerId, HELMET_SLOT, locked);
        }

        public void Dispose()
        {
            _model.ItemEquipped -= OnEquipmentChanged;
            _model.ItemUnequipped -= OnEquipmentChanged;
        }

        private void OnEquipmentChanged(ItemInstance item, SlottedContainer container, SlotDefinition slot)
        {
            if (item != null && item.Definition.IsCategory(EquipmentStatsFormatter.OUTFIT_CATEGORY))
            {
                Refresh();
            }
        }
    }
}
