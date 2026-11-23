using System;
using System.Collections.Generic;
using Inventory;
using Inventory.UI;
using Inventory.IconGeneration;
using RaidIntoDarkness.Weapons;
using UnityEngine;

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
        private readonly List<string> _configuredVariant = new();

        private GameObject _configuredModel;
        private ItemDefinition _configuredDefinition;

        public FirstPersonWeaponBinder(InventoryWindow window, FirstPersonWeaponController controller)
        {
            _window = window;
            _controller = controller;
            _model = window.Model;
            _ammo = new InventoryWeaponAmmoSource(_model, window.PrimaryContainerId);
            _controller.SetAmmoSource(_ammo);
            _controller.VisualCreated += OnVisualCreated;
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
            ApplyActiveVisual();
        }

        public void Dispose()
        {
            _model.ItemEquipped -= OnEquipmentChanged;
            _model.ItemUnequipped -= OnEquipmentChanged;
            _model.Changed -= OnItemChanged;
            if (_controller != null)
            {
                _controller.VisualCreated -= OnVisualCreated;
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
            _controller.SetSlotGrenadeLauncher(slot, stats?.GetGrenadeLauncher(item, _model.Resolver) != null);
            WeaponAddonComponent scope = stats?.GetScope(item, _model.Resolver)?.GetComponent<WeaponAddonComponent>();
            _controller.SetSlotAimOffset(slot, scope != null && scope.OverrideAimOffsetPosition ? scope.AimOffsetPosition : null);
        }

        private void OnVisualCreated(GameObject visual) => ApplyActiveVisual();

        private void ApplyActiveVisual()
        {
            GameObject model = _controller.Model;
            if (model == null || _model.GetContainer(_window.EquipmentContainerId) is not SlottedContainer equipment)
            {
                return;
            }

            string slotId = _controller.ActiveSlot == FirstPersonWeaponController.PRIMARY_SLOT ? PRIMARY_SLOT : SECONDARY_SLOT;
            ItemInstance item = equipment.GetSlotOccupant(slotId);

            if (item == null || model == _configuredModel && item.Definition == _configuredDefinition && HasConfiguredVariant(item.VisualVariant))
            {
                return;
            }

            ItemIconVisualConfigurators.Apply(model, item.Definition, item.VisualVariant);
            _configuredModel = model;
            _configuredDefinition = item.Definition;
            _configuredVariant.Clear();
            _configuredVariant.AddRange(item.VisualVariant);
        }

        private bool HasConfiguredVariant(IReadOnlyList<string> variant)
        {
            if (variant.Count != _configuredVariant.Count)
            {
                return false;
            }

            for (int i = 0; i < variant.Count; i++)
            {
                if (variant[i] != _configuredVariant[i])
                {
                    return false;
                }
            }

            return true;
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
