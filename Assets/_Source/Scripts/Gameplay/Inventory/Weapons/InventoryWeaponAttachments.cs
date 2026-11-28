using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Application
{
    public sealed class InventoryWeaponAttachments
    {
        public const string SILENCER_LEFT_STATE = "weapon.silencer.left";
        public const string DETACH_ACTION = "detach_silencer";
        public const string ATTACH_ACTION = "attach_silencer_";
        public const string DETACH_SCOPE_ACTION = "detach_scope";
        public const string DETACH_GRENADE_LAUNCHER_ACTION = "detach_grenade_launcher";

        private readonly InventoryModel _model;
        private readonly string _primaryContainerId;

        public InventoryWeaponAttachments(InventoryModel model, string primaryContainerId)
        {
            _model = model;
            _primaryContainerId = primaryContainerId;
        }

        internal event Action<AudioClip> Detached;

        public ItemDefinition GetSilencer(ItemInstance weapon) => weapon?.Definition.GetComponent<WeaponStatsComponent>()?.GetSilencer(weapon);
        public ItemDefinition GetScope(ItemInstance weapon) => weapon?.Definition.GetComponent<WeaponStatsComponent>()?.GetScope(weapon);
        public ItemDefinition GetGrenadeLauncher(ItemInstance weapon) => weapon?.Definition.GetComponent<WeaponStatsComponent>()?.GetGrenadeLauncher(weapon);

        public bool Handles(ItemInstance source, ItemInstance target) => source != null && target != null && source != target && target.Definition.GetComponent<WeaponStatsComponent>() is WeaponStatsComponent stats && (stats.SupportsSilencer(source.Definition) || stats.SupportsScope(source.Definition) || stats.SupportsGrenadeLauncher(source.Definition));

        public InventoryResult Preview(ItemInstance source, ItemInstance target) => Install(source, target, false);

        public InventoryResult Apply(ItemInstance source, ItemInstance target) => Install(source, target);

        public InventoryResult Install(ItemInstance silencer, ItemInstance weapon, bool apply = true)
        {
            if (silencer?.Definition.GetComponent<WeaponAddonComponent>()?.Kind == WeaponAddonKind.Scope)
            {
                return InstallScope(silencer, weapon, apply);
            }

            if (silencer?.Definition.GetComponent<WeaponAddonComponent>()?.Kind == WeaponAddonKind.GrenadeLauncher)
            {
                return InstallGrenadeLauncher(silencer, weapon, apply);
            }

            if (Handles(silencer, weapon) == false || _model.ContainsItem(silencer) == false || _model.ContainsItem(weapon) == false || GetSilencer(weapon) != null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            if (weapon.Definition.GetComponent<WeaponStatsComponent>().BlockSilencerWithGrenadeLauncher && GetGrenadeLauncher(weapon) != null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemInstance updated = weapon.CreateDraft();
            updated.SetMountedWeaponAttachment(WeaponAddonKind.Silencer, silencer);
            updated.SetState(SILENCER_LEFT_STATE, null);
            var variants = new List<string>(weapon.VisualVariant) { silencer.DefinitionId };
            updated.SetVisualVariant(variants);
            return InstallWithPlacement(weapon, updated, silencer, apply, allowLeftExpansion: true);
        }

        public InventoryResult Detach(ItemInstance weapon, bool apply = true)
        {
            ItemDefinition definition = GetSilencer(weapon);
            if (definition == null || _model.ContainsItem(weapon) == false)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemInstance silencer = weapon.CreateWeaponAttachmentDraft(WeaponAddonKind.Silencer);
            ItemInstance updated = weapon.CreateDraft();
            updated.SetMountedWeaponAttachment(WeaponAddonKind.Silencer, null);
            updated.SetState(SILENCER_LEFT_STATE, null);
            var variants = new List<string>(weapon.VisualVariant);
            variants.Remove(definition.DefinitionId);
            updated.SetVisualVariant(variants);
            bool equipped = _model.GetContainer(weapon.ContainerId) is SlottedContainer;
            Vector2Int position = weapon.Location.Position;

            if (equipped == false && weapon.GetState(SILENCER_LEFT_STATE) == "1")
            {
                position += weapon.Location.Rotated ? Vector2Int.up : Vector2Int.right;
            }

            InventoryResult result = _model.TryReconfigureItem(weapon, updated, position, returned: silencer, returnContainerId: equipped ? _primaryContainerId : weapon.ContainerId, apply: apply, mergeReturned: true);

            if (apply && result.Success)
            {
                Detached?.Invoke(definition.GetComponent<WeaponAddonComponent>().DetachSound);
            }

            return result;
        }

        public InventoryResult InstallScope(ItemInstance scope, ItemInstance weapon, bool apply = true)
        {
            if (scope == null || weapon == null || weapon.Definition.GetComponent<WeaponStatsComponent>()?.SupportsScope(scope.Definition) != true || _model.ContainsItem(scope) == false || _model.ContainsItem(weapon) == false)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemInstance returned = weapon.CreateWeaponAttachmentDraft(WeaponAddonKind.Scope);
            ItemInstance updated = weapon.CreateDraft();
            updated.SetMountedWeaponAttachment(WeaponAddonKind.Scope, scope);
            var variants = new List<string>(weapon.VisualVariant);

            if (returned != null)
            {
                variants.Remove(returned.DefinitionId);
            }

            variants.Add(scope.DefinitionId);
            updated.SetVisualVariant(variants);
            return InstallWithPlacement(weapon, updated, scope, apply, returned);
        }

        public InventoryResult DetachScope(ItemInstance weapon, bool apply = true)
        {
            ItemDefinition definition = GetScope(weapon);
            if (definition == null || _model.ContainsItem(weapon) == false)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemInstance scope = weapon.CreateWeaponAttachmentDraft(WeaponAddonKind.Scope);
            ItemInstance updated = weapon.CreateDraft();
            updated.SetMountedWeaponAttachment(WeaponAddonKind.Scope, null);
            var variants = new List<string>(weapon.VisualVariant);
            variants.Remove(definition.DefinitionId);
            updated.SetVisualVariant(variants);
            bool equipped = _model.GetContainer(weapon.ContainerId) is SlottedContainer;
            InventoryResult result = _model.TryReconfigureItem(weapon, updated, weapon.Location.Position, returned: scope, returnContainerId: equipped ? _primaryContainerId : weapon.ContainerId, apply: apply, mergeReturned: true);

            if (apply && result.Success)
            {
                Detached?.Invoke(definition.GetComponent<WeaponAddonComponent>().DetachSound);
            }

            return result;
        }

        public InventoryResult InstallGrenadeLauncher(ItemInstance launcher, ItemInstance weapon, bool apply = true)
        {
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();
            if (launcher == null || stats?.SupportsGrenadeLauncher(launcher.Definition) != true || _model.ContainsItem(launcher) == false || _model.ContainsItem(weapon) == false || GetGrenadeLauncher(weapon) != null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            if (stats.BlockSilencerWithGrenadeLauncher && GetSilencer(weapon) != null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemInstance launcherDraft = launcher.CreateDraft();
            launcherDraft.SetState(WeaponAddonComponent.GRENADE_ROUND_STATE, null);
            ItemInstance updated = weapon.CreateDraft();
            updated.SetState(WeaponAddonComponent.GRENADE_ROUND_STATE, launcher.Definition.GetComponent<WeaponAddonComponent>().GetLoadedGrenade(launcher)?.DefinitionId);
            updated.SetMountedWeaponAttachment(WeaponAddonKind.GrenadeLauncher, launcherDraft);
            updated.SetVisualVariant(new List<string>(weapon.VisualVariant) { launcher.DefinitionId });
            return InstallWithPlacement(weapon, updated, launcher, apply);
        }

        public InventoryResult DetachGrenadeLauncher(ItemInstance weapon, bool apply = true)
        {
            ItemDefinition definition = GetGrenadeLauncher(weapon);
            if (definition == null || _model.ContainsItem(weapon) == false)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            WeaponAddonComponent addon = definition.GetComponent<WeaponAddonComponent>();
            ItemInstance launcher = weapon.CreateWeaponAttachmentDraft(WeaponAddonKind.GrenadeLauncher);
            ItemDefinition loadedGrenade = addon.GetLoadedGrenade(weapon);
            ItemInstance returnedGrenade = loadedGrenade != null ? _model.CreateItem(loadedGrenade, 1) : null;
            ItemInstance updated = weapon.CreateDraft();
            updated.SetMountedWeaponAttachment(WeaponAddonKind.GrenadeLauncher, null);
            updated.SetState(WeaponAddonComponent.GRENADE_ROUND_STATE, null);
            var variants = new List<string>(weapon.VisualVariant);
            variants.Remove(definition.DefinitionId);
            updated.SetVisualVariant(variants);
            string destination = _model.GetContainer(weapon.ContainerId) is SlottedContainer ? _primaryContainerId : weapon.ContainerId;
            InventoryResult result = _model.TryReconfigureItem(weapon, updated, weapon.Location.Position, returned: launcher, returnContainerId: destination, apply: apply, mergeReturned: true, returnedStack: returnedGrenade);

            if (apply && result.Success)
            {
                Detached?.Invoke(addon.DetachSound);
            }

            return result;
        }

        private InventoryResult InstallWithPlacement(ItemInstance weapon, ItemInstance updated, ItemInstance addon, bool apply, ItemInstance returned = null, bool allowLeftExpansion = false)
        {
            InventoryResult TryPlaceAt(Vector2Int position) => _model.TryReconfigureItem(weapon, updated, position, consumed: addon, returned: returned, returnContainerId: addon.ContainerId, apply: apply, mergeReturned: true);

            InventoryResult result = TryPlaceAt(weapon.Location.Position);
            if (result.Failure is not (InventoryFailure.Occupied or InventoryFailure.OutOfBounds) || _model.GetContainer(weapon.ContainerId) is not GridContainer grid)
            {
                return result;
            }

            if (allowLeftExpansion)
            {
                updated.SetState(SILENCER_LEFT_STATE, "1");
                result = TryPlaceAt(weapon.Location.Position - (weapon.Location.Rotated ? Vector2Int.up : Vector2Int.right));
                if (result.Failure is not (InventoryFailure.Occupied or InventoryFailure.OutOfBounds))
                {
                    return result;
                }

                updated.SetState(SILENCER_LEFT_STATE, null);
            }

            int bottom = 0;
            IReadOnlyList<ItemInstance> items = grid.Items;
            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance placed = items[i];
                if (placed != weapon && (placed != addon || addon.Amount > 1))
                {
                    bottom = Mathf.Max(bottom, grid.GetItemArea(placed).yMax);
                }
            }

            return TryPlaceAt(new Vector2Int(0, bottom));
        }

        internal bool CanOfferInstall(ItemInstance addon, ItemInstance weapon)
        {
            if (Handles(addon, weapon) == false)
            {
                return false;
            }

            return addon.Definition.GetComponent<WeaponAddonComponent>().Kind switch
            {
                WeaponAddonKind.Scope => true,
                WeaponAddonKind.GrenadeLauncher => GetGrenadeLauncher(weapon) == null,
                _ => GetSilencer(weapon) == null
            };
        }
    }
}
