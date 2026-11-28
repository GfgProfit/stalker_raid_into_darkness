using System;
using System.Collections.Generic;
using Inventory;
using Inventory.UI;
using Inventory.IconGeneration;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Weapons;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class FirstPersonWeaponBinder : IInventoryPlacementValidator, IDisposable
    {
        public const string PRIMARY_SLOT = "Primary";
        public const string SECONDARY_SLOT = "Secondary";
        public const string KNIFE_SLOT = "Knife";

        private readonly InventoryWindow _window;
        private readonly InventoryModel _model;
        private readonly FirstPersonWeaponController _controller;
        private readonly List<string> _configuredVariant = new();
        private readonly HashSet<ItemDefinition> _grenadeTypes = new();

        private GameObject _configuredModel;
        private ItemDefinition _configuredDefinition;
        private ItemInstance _handGrenade;

        public FirstPersonWeaponBinder(InventoryWindow window, FirstPersonWeaponController controller, InventoryWeaponAmmoSource ammo)
        {
            _window = window;
            _controller = controller;
            _model = window.Model;
            _controller.SetAmmoSource(ammo);
            _controller.VisualCreated += OnVisualCreated;
            _controller.HandGrenadeSelectionRequested += SelectHandGrenade;
            _controller.FireModeChanged += _window.RefreshTooltip;
            _model.ItemEquipped += OnEquipmentChanged;
            _model.ItemUnequipped += OnEquipmentChanged;
            _model.Changed += OnItemChanged;
            _model.AddPlacementValidator(this);
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
            Bind(equipment, KNIFE_SLOT, FirstPersonWeaponController.KNIFE_SLOT);
            RefreshHandGrenade();
            ApplyActiveVisual();
        }

        public void Dispose()
        {
            _model.RemovePlacementValidator(this);
            _model.ItemEquipped -= OnEquipmentChanged;
            _model.ItemUnequipped -= OnEquipmentChanged;
            _model.Changed -= OnItemChanged;
            if (_controller != null)
            {
                _controller.VisualCreated -= OnVisualCreated;
                _controller.HandGrenadeSelectionRequested -= SelectHandGrenade;
                _controller.FireModeChanged -= _window.RefreshTooltip;
                _controller.SetAmmoSource(null);
            }
        }

        public bool IsReloading(ItemInstance item)
        {
            if (_controller == null || !_controller.IsReloading || _window.IsEquipped(item) == false)
            {
                return false;
            }

            return item.Location.SlotId == GetEquipmentSlotId(_controller.ActiveSlot);
        }

        bool IInventoryPlacementValidator.CanPlace(ItemInstance item, InventoryContainer container, ItemPlacement placement)
        {
            return !IsReloading(item) || (container.Id == item.ContainerId && placement.SlotId == item.Location.SlotId);
        }

        internal static string GetEquipmentSlotId(int slot)
        {
            return slot switch
            {
                FirstPersonWeaponController.PRIMARY_SLOT => PRIMARY_SLOT,
                FirstPersonWeaponController.SECONDARY_SLOT => SECONDARY_SLOT,
                FirstPersonWeaponController.KNIFE_SLOT => KNIFE_SLOT,
                _ => string.Empty
            };
        }

        private void Bind(SlottedContainer equipment, string slotId, int slot)
        {
            ItemInstance item = equipment != null ? equipment.GetSlotOccupant(slotId) : null;
            WeaponHudComponent hud = item != null ? item.Definition.GetComponent<WeaponHudComponent>() : null;
            FirstPersonWeaponAsset weapon = hud != null ? hud.FirstPersonWeapon : null;
            WeaponStatsComponent stats = item?.Definition.GetComponent<WeaponStatsComponent>();
            bool isPistol = string.Equals(stats?.WeaponClass, "pistol", StringComparison.OrdinalIgnoreCase);
            _controller.SetSlotWeapon(slot, weapon, weapon != null ? item.InstanceId : null, isPistol);
            WeaponAddonComponent silencer = stats?.GetSilencer(item, _model.Resolver)?.GetComponent<WeaponAddonComponent>();
            WeaponAddonComponent launcher = stats?.GetGrenadeLauncher(item, _model.Resolver)?.GetComponent<WeaponAddonComponent>();
            var (recoil, snappiness) = WeaponAddonComponent.GetEquippedRecoilMultipliers(silencer, launcher);
            _controller.SetSlotRecoilMultipliers(slot, recoil, snappiness);
            _controller.SetSlotSilencer(slot, stats?.SilencerStatus == WeaponAddonStatus.Permanent || silencer != null);
            _controller.SetSlotGrenadeLauncher(slot, launcher != null);
            ItemDefinition scope = stats?.GetScope(item, _model.Resolver);
            _controller.SetSlotScope(slot, scope != null);
            float depthOffset = 0f;
            Vector3? aimOffset = hud?.GetScopeAimOffset(scope, out depthOffset);
            _controller.SetSlotAimOffset(slot, aimOffset, depthOffset);
        }

        private void OnVisualCreated(GameObject visual) => ApplyActiveVisual();

        private void RefreshHandGrenade()
        {
            if (_handGrenade == null || _handGrenade.ContainerId != _window.PrimaryContainerId || _model.GetItem(_handGrenade.InstanceId) != _handGrenade)
            {
                SetHandGrenade(FindHandGrenade(_handGrenade?.Definition, false));
            }
        }

        private void SelectHandGrenade()
        {
            RefreshHandGrenade();
            if (_controller.ActiveSlot != FirstPersonWeaponController.HAND_GRENADE_SELECTION)
            {
                _controller.Select(FirstPersonWeaponController.HAND_GRENADE_SELECTION);
                return;
            }

            if (_controller.State is FirstPersonWeaponState.Ready or FirstPersonWeaponState.Drawing or FirstPersonWeaponState.Holstering)
            {
                ItemInstance next = FindHandGrenade(_handGrenade?.Definition, true);
                if (next != null && next.Definition != _handGrenade?.Definition)
                {
                    SetHandGrenade(next);
                }
            }
        }

        private void SetHandGrenade(ItemInstance item)
        {
            _handGrenade = item;
            FirstPersonWeaponAsset weapon = item?.Definition.GetComponent<WeaponHudComponent>()?.FirstPersonWeapon;
            _controller.SetSlotWeapon(FirstPersonWeaponController.HAND_GRENADE_SELECTION, weapon, item?.InstanceId);
        }

        private ItemInstance FindHandGrenade(ItemDefinition selected, bool cycle)
        {
            InventoryContainer backpack = _model.GetContainer(_window.PrimaryContainerId);
            _grenadeTypes.Clear();
            ItemInstance first = null;
            bool found = selected == null;
            if (backpack != null)
            {
                foreach (ItemInstance item in backpack.Items)
                {
                    if (!item.Definition.IsCategory("Grenade") || item.Definition.GetComponent<WeaponHudComponent>()?.FirstPersonWeapon?.IsHandGrenade != true || !_grenadeTypes.Add(item.Definition))
                    {
                        continue;
                    }

                    first ??= item;
                    if (item.Definition == selected)
                    {
                        if (!cycle)
                        {
                            return item;
                        }

                        found = true;
                    }
                    else if (cycle && found)
                    {
                        return item;
                    }
                }
            }

            return first;
        }

        private void ApplyActiveVisual()
        {
            GameObject model = _controller.Model;
            if (model == null || _model.GetContainer(_window.EquipmentContainerId) is not SlottedContainer equipment)
            {
                return;
            }

            string slotId = GetEquipmentSlotId(_controller.ActiveSlot);
            ItemInstance item = _controller.ActiveSlot == FirstPersonWeaponController.HAND_GRENADE_SELECTION ? _handGrenade : equipment.GetSlotOccupant(slotId);

            if (item == null || model == _configuredModel && item.Definition == _configuredDefinition && HasConfiguredVariant(item.VisualVariant))
            {
                return;
            }

            ItemIconVisualConfigurators.Apply(model, item.Definition, item.VisualVariant);
            FirstPersonRig rig = model.GetComponentInParent<FirstPersonRig>();
            if (rig != null)
            {
                rig.InvalidateWeaponObstructionGeometry();
            }

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
            if (_controller != null && (change.Affects(_window.PrimaryContainerId) || change.Kind == InventoryChangeKind.Cleared))
            {
                RefreshHandGrenade();
            }

            if (change.Kind == InventoryChangeKind.ItemStateChanged && change.ContainerId == _window.EquipmentContainerId)
            {
                Refresh();
            }
        }

        private void OnEquipmentChanged(ItemInstance item, SlottedContainer container, SlotDefinition slot)
        {
            if (slot != null && (slot.Id == PRIMARY_SLOT || slot.Id == SECONDARY_SLOT || slot.Id == KNIFE_SLOT))
            {
                Refresh();
            }
        }
    }
}
