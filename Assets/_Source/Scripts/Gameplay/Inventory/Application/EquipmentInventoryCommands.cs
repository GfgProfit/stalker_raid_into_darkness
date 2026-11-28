using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;

namespace RaidIntoDarkness.Inventory.Application
{
    internal sealed class EquipmentInventoryCommands
    {
        private const string PATCH_REMOVED_STATE = "outfit_patch_removed";
        private const string PATCH_CATEGORY = "Patch";
        private const string PATCH_SECTION_SUFFIX = "_patch";

        private readonly InventoryModel _model;
        private readonly string _equipmentContainerId;
        private readonly string _primaryContainerId;
        private readonly Func<ItemInstance, InventoryResult> _drop;
        private readonly Dictionary<string, ItemDefinition> _patchDefinitions = new(StringComparer.OrdinalIgnoreCase);

        public EquipmentInventoryCommands(InventoryModel model, string equipmentContainerId, string primaryContainerId, Func<ItemInstance, InventoryResult> drop)
        {
            _model = model;
            _equipmentContainerId = equipmentContainerId;
            _primaryContainerId = primaryContainerId;
            _drop = drop;

            if (_model.Resolver is ItemDatabase database)
            {
                foreach (ItemDefinition definition in database.Definitions)
                {
                    if (definition != null && definition.IsCategory(PATCH_CATEGORY))
                    {
                        _patchDefinitions[definition.name] = definition;
                    }
                }
            }
        }

        public static bool IsPatchRemoved(ItemInstance item) => item?.GetState(PATCH_REMOVED_STATE) == "1";

        public bool CanEquip(ItemInstance item) => _model.CanEquipItem(item, _equipmentContainerId, _primaryContainerId);

        public InventoryResult Equip(ItemInstance item, SlotId slotId) => _model.TryEquipItem(item, _equipmentContainerId, slotId, _primaryContainerId);

        public InventoryResult Unequip(ItemInstance item, InventoryUnequipTarget target) => _model.TryUnequipItem(item, target.ContainerId, target.Origin, target.Rotated, target.AutoPlace);

        public InventoryResult Drop(ItemInstance item) => _drop(item);

        public InventoryResult RemovePatch(ItemInstance item, bool apply)
        {
            if (IsPatchRemoved(item) || TryFindPatch(item, out _, out ItemDefinition patchDefinition) == false)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemInstance updated = item.CreateDraft();

            updated.SetState(PATCH_REMOVED_STATE, "1");
            ItemInstance patch = _model.CreateItem(patchDefinition);
            return _model.TryReconfigureItem(item, updated, item.Location.Position, returned: patch, returnContainerId: _primaryContainerId, apply: apply, mergeReturned: true);
        }

        public bool TryFindPatch(ItemInstance item, out string community, out ItemDefinition patchDefinition)
        {
            community = item.Definition.GetComponent<EquipmentStatsComponent>()?.Community ?? string.Empty;
            return _patchDefinitions.TryGetValue(community + PATCH_SECTION_SUFFIX, out patchDefinition);
        }
    }
}
