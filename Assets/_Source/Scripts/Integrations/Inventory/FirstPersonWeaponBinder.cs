using System;
using Inventory;
using Inventory.UI;
using RaidIntoDarkness.Weapons;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class FirstPersonWeaponBinder : IDisposable
    {
        public const string PRIMARY_SLOT = "Primary";
        public const string SECONDARY_SLOT = "Secondary";

        private readonly InventoryWindow _window;
        private readonly InventoryModel _model;
        private readonly FirstPersonWeaponController _controller;
        private readonly InventoryWeaponAmmoSource _ammo;

        public FirstPersonWeaponBinder(InventoryWindow window, FirstPersonWeaponController controller)
        {
            _window = window;
            _controller = controller;
            _model = window.Model;
            _ammo = new InventoryWeaponAmmoSource(_model, window.PrimaryContainerId);
            _controller.SetAmmoSource(_ammo);
            _model.ItemEquipped += OnEquipmentChanged;
            _model.ItemUnequipped += OnEquipmentChanged;
            _model.Changed += OnItemChanged;
        }

        public void Refresh()
        {
            if (_controller == null)
            {
                return;
            }

            SlottedContainer equipment = _model != null ? _model.GetContainer(_window.EquipmentContainerId) as SlottedContainer : null;
            Bind(equipment, PRIMARY_SLOT, FirstPersonWeaponController.PRIMARY_SLOT);
            Bind(equipment, SECONDARY_SLOT, FirstPersonWeaponController.SECONDARY_SLOT);
        }

        public void Dispose()
        {
            _model.ItemEquipped -= OnEquipmentChanged;
            _model.ItemUnequipped -= OnEquipmentChanged;
            _model.Changed -= OnItemChanged;
            if (_controller != null)
            {
                _controller.SetAmmoSource(null);
            }

            _ammo.Dispose();
        }

        private void Bind(SlottedContainer equipment, string slotId, int slot)
        {
            ItemInstance item = equipment != null ? equipment.GetSlotOccupant(slotId) : null;
            WeaponHudComponent hud = item != null ? item.Definition.GetComponent<WeaponHudComponent>() : null;
            FirstPersonWeaponAsset weapon = hud != null ? hud.FirstPersonWeapon : null;
            _controller.SetSlotWeapon(slot, weapon, weapon != null ? item.InstanceId : null);
            WeaponStatsComponent stats = item?.Definition.GetComponent<WeaponStatsComponent>();
            _controller.SetSlotSilencer(slot, stats != null && (stats.SilencerStatus == WeaponAddonStatus.Permanent || stats.GetSilencer(item, _model.Resolver) != null));
        }

        private void OnItemChanged(InventoryChange change)
        {
            if (change.Kind == InventoryChangeKind.ItemStateChanged && change.ContainerId == _window.EquipmentContainerId)
            {
                Refresh();
            }
        }

        private void OnEquipmentChanged(ItemInstance item, SlottedContainer container, SlotDefinition slot)
        {
            if (slot != null && (slot.Id == PRIMARY_SLOT || slot.Id == SECONDARY_SLOT))
            {
                Refresh();
            }
        }
    }
}
