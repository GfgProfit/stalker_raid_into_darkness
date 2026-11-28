using System;
using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.Application
{
    internal static class InventoryItemOperations
    {
        public static bool CanUse(ItemInstance item) => item?.Definition.GetComponent<ConsumableItemComponent>() != null;

        public static InventoryResult Use(InventoryModel model, ItemInstance item)
        {
            return item.Definition.TryGetComponent(out ConsumableItemComponent consumable) ? model.TryRemove(item.InstanceId, consumable.AmountPerUse) : InventoryResult.Ok(item, 0);
        }

        public static bool CanSplit(InventoryModel model, ItemInstance item) => model.ContainsItem(item) && item.Amount >= 2;

        public static InventoryResult Split(InventoryModel model, ItemInstance item, int amount) => model.TrySplitAuto(item.InstanceId, amount);

        public static (InventoryResult Result, bool Queued, bool WasEquipped) Transfer(InventoryModel model, ItemInstance item, InventoryContainer target, string equipmentId, IEquipmentAnimationRequests equipment)
        {
            bool equipped = EquipmentInventoryCommands.IsEquipped(model, item, equipmentId);
            if (equipped && equipment?.HandleUnequip(item, InventoryUnequipTarget.Auto(target.Id)) == true)
            {
                return (InventoryResult.Ok(item, item.Amount), true, true);
            }

            return (model.TryTransfer(item.InstanceId, target.Id), false, equipped);
        }

        public static (InventoryResult Result, bool Queued, bool Dispatched) RequestDrop(InventoryModel model, ItemInstance item, string equipmentId, IEquipmentAnimationRequests equipment, Func<ItemInstance, InventoryResult> worldDrop)
        {
            if (EquipmentInventoryCommands.IsEquipped(model, item, equipmentId) && equipment?.HandleDrop(item) == true)
            {
                return (InventoryResult.Ok(item, item.Amount), true, true);
            }

            if (model.ContainsItem(item) == false)
            {
                return (InventoryResult.Fail(InventoryFailure.ItemNotFound), false, false);
            }

            return (ApplyDrop(model, item, worldDrop), false, true);
        }

        public static InventoryResult Drop(InventoryModel model, ItemInstance item, Func<ItemInstance, InventoryResult> worldDrop)
        {
            if (model.ContainsItem(item) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            return ApplyDrop(model, item, worldDrop);
        }

        private static InventoryResult ApplyDrop(InventoryModel model, ItemInstance item, Func<ItemInstance, InventoryResult> worldDrop)
        {
            return worldDrop != null ? worldDrop(item) : model.TryRemove(item.InstanceId);
        }
    }
}
