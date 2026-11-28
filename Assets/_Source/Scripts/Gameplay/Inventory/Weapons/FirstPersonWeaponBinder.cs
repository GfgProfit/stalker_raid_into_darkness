using System;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.IconGeneration;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Weapons;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    public sealed class FirstPersonWeaponBinder : IInventoryPlacementValidator, IDisposable
    {
        private readonly InventoryModel _model;
        private readonly string _equipmentContainerId;
        private readonly string _primaryContainerId;
        private readonly FirstPersonWeaponController _controller;
        private readonly List<string> _configuredVariant = new();
        private readonly HashSet<ItemDefinition> _grenadeTypes = new();

        private GameObject _configuredModel;
        private ItemDefinition _configuredDefinition;
        private ItemInstance _handGrenade;

        public FirstPersonWeaponBinder(InventoryModel model, string equipmentContainerId, string primaryContainerId, FirstPersonWeaponController controller, InventoryWeaponAmmoSource ammo)
        {
            _model = model;
            _equipmentContainerId = equipmentContainerId;
            _primaryContainerId = primaryContainerId;
            _controller = controller;
            _controller.SetAmmoSource(ammo);
            _controller.VisualCreated += OnVisualCreated;
            _controller.HandGrenadeSelectionRequested += SelectHandGrenade;
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

            SlottedContainer equipment = _model.GetContainer(_equipmentContainerId) as SlottedContainer;
            Bind(equipment, SlotId.Primary, FirstPersonWeaponController.PRIMARY_SLOT);
            Bind(equipment, SlotId.Secondary, FirstPersonWeaponController.SECONDARY_SLOT);
            Bind(equipment, SlotId.Knife, FirstPersonWeaponController.KNIFE_SLOT);
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
                _controller.SetAmmoSource(null);
            }
        }

        public bool IsReloading(ItemInstance item)
        {
            if (_controller == null || !_controller.IsReloading || item == null || item.ContainerId != _equipmentContainerId)
            {
                return false;
            }

            return item.Location.SlotId == GetEquipmentSlotId(_controller.ActiveSlot);
        }

        bool IInventoryPlacementValidator.CanPlace(ItemInstance item, InventoryContainer container, ItemPlacement placement)
        {
            return !IsReloading(item) || (container.Id == item.ContainerId && placement.SlotId == item.Location.SlotId);
        }

        internal static SlotId GetEquipmentSlotId(int slot)
        {
            return slot switch
            {
                FirstPersonWeaponController.PRIMARY_SLOT => SlotId.Primary,
                FirstPersonWeaponController.SECONDARY_SLOT => SlotId.Secondary,
                FirstPersonWeaponController.KNIFE_SLOT => SlotId.Knife,
                _ => SlotId.None
            };
        }

        private void Bind(SlottedContainer equipment, SlotId slotId, int slot)
        {
            ItemInstance item = equipment != null ? equipment.GetSlotOccupant(slotId) : null;
            WeaponHudComponent hud = item != null ? item.Definition.GetComponent<WeaponHudComponent>() : null;
            FirstPersonWeaponAsset weapon = hud != null ? hud.FirstPersonWeapon : null;
            WeaponStatsComponent stats = item?.Definition.GetComponent<WeaponStatsComponent>();
            bool isPistol = string.Equals(stats?.WeaponClass, "pistol", StringComparison.OrdinalIgnoreCase);
            _controller.SetSlotWeapon(slot, weapon, weapon != null ? item.InstanceId : null, isPistol);
            WeaponAddonComponent silencer = stats?.GetSilencer(item)?.GetComponent<WeaponAddonComponent>();
            WeaponAddonComponent launcher = stats?.GetGrenadeLauncher(item)?.GetComponent<WeaponAddonComponent>();
            var (recoil, snappiness) = WeaponAddonComponent.GetEquippedRecoilMultipliers(silencer, launcher);
            _controller.SetSlotRecoilMultipliers(slot, recoil, snappiness);
            _controller.SetSlotSilencer(slot, stats?.SilencerStatus == WeaponAddonStatus.Permanent || silencer != null);
            _controller.SetSlotGrenadeLauncher(slot, launcher != null);
            ItemDefinition scope = stats?.GetScope(item);
            _controller.SetSlotScope(slot, scope != null);
            float depthOffset = 0f;
            Vector3? aimOffset = hud?.GetScopeAimOffset(scope, out depthOffset);
            _controller.SetSlotAimOffset(slot, aimOffset, depthOffset);
        }

        private void OnVisualCreated(GameObject visual) => ApplyActiveVisual();

        private void RefreshHandGrenade()
        {
            if (_handGrenade == null || _handGrenade.ContainerId != _primaryContainerId || _model.GetItem(_handGrenade.InstanceId) != _handGrenade)
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
            InventoryContainer backpack = _model.GetContainer(_primaryContainerId);
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
            if (model == null || _model.GetContainer(_equipmentContainerId) is not SlottedContainer equipment)
            {
                return;
            }

            SlotId slotId = GetEquipmentSlotId(_controller.ActiveSlot);
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
            if (_controller != null && (change.Affects(_primaryContainerId) || change.Kind == InventoryChangeKind.Cleared))
            {
                RefreshHandGrenade();
            }

            if (change.Kind == InventoryChangeKind.ItemStateChanged && change.ContainerId == _equipmentContainerId)
            {
                Refresh();
            }
        }

        private void OnEquipmentChanged(ItemInstance item, SlottedContainer container, SlotDefinition slot)
        {
            if (container.Id == _equipmentContainerId && slot != null && slot.Id is SlotId.Primary or SlotId.Secondary or SlotId.Knife)
            {
                Refresh();
            }
        }
    }
}
