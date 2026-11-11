using System;
using System.Collections.Generic;
using Inventory;
using Inventory.UI;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class InventoryWeaponAttachments : IDisposable, IInventoryItemDropHandler
    {
        public const string SILENCER_STATE = "weapon.silencer";
        public const string SILENCER_ITEM_STATE = "weapon.silencer.item";
        public const string SILENCER_LEFT_STATE = "weapon.silencer.left";
        public const string DETACH_ACTION = "detach_silencer";
        public const string ATTACH_ACTION = "attach_silencer_";
        public const string SCOPE_STATE = "weapon.scope";
        public const string SCOPE_ITEM_STATE = "weapon.scope.item";
        public const string DETACH_SCOPE_ACTION = "detach_scope";

        private readonly InventoryWindow _window;
        private readonly InventoryModel _model;
        private readonly List<string> _actions = new();

        public InventoryWeaponAttachments(InventoryWindow window)
        {
            _window = window;
            _model = window.Model;
            _window.ItemDropHandler = this;
            _window.Actions.Register(new InventoryDelegateAction(DETACH_ACTION, 21, context => "Снять глушитель", context => Report(Detach(context.Item), null), context => GetSilencer(context.Item) != null, context => Detach(context.Item, false).Success));
            _actions.Add(DETACH_ACTION);
            _window.Actions.Register(new InventoryDelegateAction(DETACH_SCOPE_ACTION, 22, context => "Снять прицел", context => Report(DetachScope(context.Item), null), context => GetScope(context.Item) != null, context => DetachScope(context.Item, false).Success));
            _actions.Add(DETACH_SCOPE_ACTION);
            SlottedContainer equipment = _model.GetContainer(window.EquipmentContainerId) as SlottedContainer;

            if (equipment == null)
            {
                return;
            }

            foreach (SlotDefinition slot in equipment.Slots)
            {
                string slotId = slot.Id;
                string actionId = ATTACH_ACTION + slotId;
                _window.Actions.Register(new InventoryDelegateAction(actionId, 20, context => "Поставить на: " + equipment.GetSlotOccupant(slotId)?.Definition.ShortName, context => Report(Install(context.Item, equipment.GetSlotOccupant(slotId)), context.Item), context => Handles(context.Item, equipment.GetSlotOccupant(slotId)) && (context.Item.Definition.GetComponent<WeaponAddonComponent>().Kind == WeaponAddonKind.Scope || GetSilencer(equipment.GetSlotOccupant(slotId)) == null), context => Install(context.Item, equipment.GetSlotOccupant(slotId), false).Success));
                _actions.Add(actionId);
            }
        }

        public ItemDefinition GetSilencer(ItemInstance weapon) => weapon?.Definition.GetComponent<WeaponStatsComponent>()?.GetSilencer(weapon, _model.Resolver);
        public ItemDefinition GetScope(ItemInstance weapon) => weapon?.Definition.GetComponent<WeaponStatsComponent>()?.GetScope(weapon, _model.Resolver);

        public bool Handles(ItemInstance source, ItemInstance target) => source != null && target != null && source != target && target.Definition.GetComponent<WeaponStatsComponent>() is WeaponStatsComponent stats && (stats.SupportsSilencer(source.Definition) || stats.SupportsScope(source.Definition));

        public InventoryResult Preview(ItemInstance source, ItemInstance target) => Install(source, target, false);

        public InventoryResult Apply(ItemInstance source, ItemInstance target)
        {
            InventoryResult result = Install(source, target);
            Report(result, source);
            return result;
        }

        public InventoryResult Install(ItemInstance silencer, ItemInstance weapon, bool apply = true)
        {
            if (silencer?.Definition.GetComponent<WeaponAddonComponent>()?.Kind == WeaponAddonKind.Scope)
            {
                return InstallScope(silencer, weapon, apply);
            }

            if (Handles(silencer, weapon) == false || _model.ContainsItem(silencer) == false || _model.ContainsItem(weapon) == false || GetSilencer(weapon) != null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemInstance updated = Clone(weapon);
            InventoryItemData data = InventorySerializer.CaptureItem(silencer);
            data.Amount = 1;

            if (silencer.Amount > 1)
            {
                data.InstanceId = Guid.NewGuid().ToString("N");
            }

            updated.SetState(SILENCER_STATE, silencer.DefinitionId);
            updated.SetState(SILENCER_ITEM_STATE, JsonUtility.ToJson(data));
            updated.SetState(SILENCER_LEFT_STATE, null);
            var variants = new List<string>(weapon.VisualVariant) { silencer.DefinitionId };
            updated.SetVisualVariant(variants);
            Vector2Int position = weapon.Location.Position;
            InventoryResult result = _model.TryReconfigureItem(weapon, updated, position, silencer, apply: apply);

            if (result.Success || _model.GetContainer(weapon.ContainerId) is SlottedContainer)
            {
                return result;
            }

            updated.SetState(SILENCER_LEFT_STATE, "1");
            position -= weapon.Location.Rotated ? Vector2Int.up : Vector2Int.right;
            return _model.TryReconfigureItem(weapon, updated, position, silencer, apply: apply);
        }

        public InventoryResult Detach(ItemInstance weapon, bool apply = true)
        {
            ItemDefinition definition = GetSilencer(weapon);
            string json = weapon?.GetState(SILENCER_ITEM_STATE);

            if (definition == null || string.IsNullOrEmpty(json) || _model.ContainsItem(weapon) == false)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            InventoryItemData data = JsonUtility.FromJson<InventoryItemData>(json);
            ItemInstance silencer = Restore(definition, data);
            ItemInstance updated = Clone(weapon);
            updated.SetState(SILENCER_STATE, null);
            updated.SetState(SILENCER_ITEM_STATE, null);
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

            InventoryResult result = _model.TryReconfigureItem(weapon, updated, position, returned: silencer, returnContainerId: equipped ? _window.PrimaryContainerId : weapon.ContainerId, apply: apply);

            if (apply && result.Success)
            {
                _window.PlayClip(definition.GetComponent<WeaponAddonComponent>().DetachSound, 1f);
            }

            return result;
        }

        public void Dispose()
        {
            if (_window.ItemDropHandler == this)
            {
                _window.ItemDropHandler = null;
            }

            foreach (string action in _actions)
            {
                _window.Actions.Unregister(action);
            }
        }

        public InventoryResult InstallScope(ItemInstance scope, ItemInstance weapon, bool apply = true)
        {
            if (scope == null || weapon == null || weapon.Definition.GetComponent<WeaponStatsComponent>()?.SupportsScope(scope.Definition) != true || _model.ContainsItem(scope) == false || _model.ContainsItem(weapon) == false)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemDefinition previous = GetScope(weapon);
            ItemInstance returned = null;

            if (previous != null)
            {
                string json = weapon.GetState(SCOPE_ITEM_STATE);

                if (string.IsNullOrEmpty(json))
                {
                    return InventoryResult.Fail(InventoryFailure.InvalidItem);
                }

                returned = Restore(previous, JsonUtility.FromJson<InventoryItemData>(json));
            }

            ItemInstance updated = Clone(weapon);
            InventoryItemData data = InventorySerializer.CaptureItem(scope);
            data.Amount = 1;

            if (scope.Amount > 1)
            {
                data.InstanceId = Guid.NewGuid().ToString("N");
            }

            updated.SetState(SCOPE_STATE, scope.DefinitionId);
            updated.SetState(SCOPE_ITEM_STATE, JsonUtility.ToJson(data));
            var variants = new List<string>(weapon.VisualVariant);

            if (previous != null)
            {
                variants.Remove(previous.DefinitionId);
            }

            variants.Add(scope.DefinitionId);
            updated.SetVisualVariant(variants);
            return _model.TryReconfigureItem(weapon, updated, weapon.Location.Position, consumed: scope, returned: returned, returnContainerId: scope.ContainerId, apply: apply);
        }

        public InventoryResult DetachScope(ItemInstance weapon, bool apply = true)
        {
            ItemDefinition definition = GetScope(weapon);
            string json = weapon?.GetState(SCOPE_ITEM_STATE);

            if (definition == null || string.IsNullOrEmpty(json) || _model.ContainsItem(weapon) == false)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemInstance scope = Restore(definition, JsonUtility.FromJson<InventoryItemData>(json));
            ItemInstance updated = Clone(weapon);
            updated.SetState(SCOPE_STATE, null);
            updated.SetState(SCOPE_ITEM_STATE, null);
            var variants = new List<string>(weapon.VisualVariant);
            variants.Remove(definition.DefinitionId);
            updated.SetVisualVariant(variants);
            bool equipped = _model.GetContainer(weapon.ContainerId) is SlottedContainer;
            InventoryResult result = _model.TryReconfigureItem(weapon, updated, weapon.Location.Position, returned: scope, returnContainerId: equipped ? _window.PrimaryContainerId : weapon.ContainerId, apply: apply);

            if (apply && result.Success)
            {
                _window.PlayClip(definition.GetComponent<WeaponAddonComponent>().DetachSound, 1f);
            }

            return result;
        }

        private void Report(InventoryResult result, ItemInstance silencer)
        {
            if (result.Success == false)
            {
                _window.ShowStatus(_window.DescribeFailure(result.Failure));
            }
            else if (silencer != null)
            {
                _window.PlayClip(silencer.Definition.GetComponent<WeaponAddonComponent>().AttachSound, 1f);
            }
        }

        private static ItemInstance Clone(ItemInstance item) => Restore(item.Definition, InventorySerializer.CaptureItem(item));

        private static ItemInstance Restore(ItemDefinition definition, InventoryItemData data)
        {
            var item = new ItemInstance(definition, data.Amount, data.InstanceId);
            item.SetVisualVariant(data.VisualVariant);

            foreach (InventoryStateEntryData entry in data.State)
            {
                item.SetState(entry.Key, entry.Value);
            }

            return item;
        }
    }
}
