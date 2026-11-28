using RaidIntoDarkness.Inventory.Domain;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Application
{
    internal static class InventoryDragOperations
    {
        public static bool TryPreviewItemTarget(InventoryModel model, InventoryWeaponAttachments attachments, ItemInstance item, ItemInstance targetItem, bool split, int amount, out InventoryDropKind kind, out InventoryFailure failure)
        {
            bool attachment = split == false && attachments?.Handles(item, targetItem) == true;
            if (attachment == false && item.CanStackWith(targetItem) == false)
            {
                kind = InventoryDropKind.None;
                failure = InventoryFailure.None;
                return false;
            }

            InventoryResult preview = attachment ? attachments.Preview(item, targetItem) : model.CanMerge(item.InstanceId, targetItem.InstanceId, amount);
            failure = preview.Failure;
            kind = preview.Success == false ? InventoryDropKind.Invalid : attachment ? InventoryDropKind.Valid : InventoryDropKind.Merge;
            return true;
        }

        public static (InventoryDropKind Kind, InventoryFailure Failure, RectInt Area, bool AutoPlace) PreviewPlacement(InventoryModel model, ItemInstance item, InventoryContainer target, Vector2Int origin, bool rotated, bool split, int amount, string parkingId, RectInt area)
        {
            InventoryResult result = split ? model.CanSplit(item.InstanceId, amount, target.Id, origin, rotated) : model.CanMove(item.InstanceId, target.Id, origin, rotated);
            if (result.Success)
            {
                ItemInstance merge = result.Item != null && result.Item != item && result.Item.CanStackWith(item) ? result.Item : target.GetItemAt(origin);
                return merge != null && merge != item && merge.CanStackWith(item) ? (InventoryDropKind.Merge, InventoryFailure.None, target.GetItemArea(merge), false) : (InventoryDropKind.Valid, InventoryFailure.None, area, false);
            }

            if (split == false && target is SlottedContainer && model.CanSwapIntoSlot(item, target.Id, origin, parkingId))
            {
                return (InventoryDropKind.Valid, InventoryFailure.None, area, false);
            }

            if (split == false && target is not SlottedContainer)
            {
                result = model.CanTransfer(item.InstanceId, target.Id, out ItemPlacement placement, out ItemInstance firstMerge);
                if (result.Success)
                {
                    return firstMerge == null ? (InventoryDropKind.Valid, InventoryFailure.None, placement.Area, true) : (InventoryDropKind.Merge, InventoryFailure.None, target.GetItemArea(firstMerge), true);
                }

                return (InventoryDropKind.Invalid, result.Failure, area, true);
            }

            return (InventoryDropKind.Invalid, result.Failure, area, false);
        }

        public static (InventoryResult Result, bool Attachment, bool Queued) Apply(InventoryModel model, InventoryWeaponAttachments attachments, IEquipmentAnimationRequests equipment, ItemInstance item, InventoryContainer target, ItemInstance targetItem, Vector2Int origin, bool rotated, bool split, int amount, bool autoPlace, bool previewValid, string equipmentId, string parkingId)
        {
            if (targetItem != null && split == false && attachments?.Handles(item, targetItem) == true)
            {
                return (attachments.Apply(item, targetItem), true, false);
            }

            if ((autoPlace == false || previewValid) && TryQueueEquipment(equipment, item, target, origin, rotated, split, autoPlace, equipmentId))
            {
                return (InventoryResult.Ok(item, item.Amount), false, true);
            }

            if (targetItem != null)
            {
                return (model.TryMerge(item.InstanceId, targetItem.InstanceId, amount), false, false);
            }

            if (autoPlace)
            {
                return (model.TryTransfer(item.InstanceId, target.Id), false, false);
            }

            InventoryResult result = split ? model.TrySplit(item.InstanceId, amount, target.Id, origin, rotated) : model.TryMove(item.InstanceId, target.Id, origin, rotated);
            if (result.Success == false && split == false && target is SlottedContainer && model.CanSwapIntoSlot(item, target.Id, origin, parkingId))
            {
                result = model.TrySwapIntoSlot(item, target.Id, origin, parkingId);
            }

            return (result, false, false);
        }

        private static bool TryQueueEquipment(IEquipmentAnimationRequests equipment, ItemInstance item, InventoryContainer target, Vector2Int origin, bool rotated, bool split, bool autoPlace, string equipmentId)
        {
            if (equipment == null || split || item == null)
            {
                return false;
            }

            bool fromEquipment = item.ContainerId == equipmentId;
            bool toEquipment = target.Id == equipmentId;
            if (toEquipment && fromEquipment == false && target is SlottedContainer slotted)
            {
                SlotDefinition slot = slotted.GetSlotAt(origin);
                return slot != null && slot.Mode == SlotMode.Single && slot.Rule.AcceptsDefinition(item.Definition) && equipment.HandleEquip(item, slot);
            }

            return fromEquipment && toEquipment == false && equipment.HandleUnequip(item, new InventoryUnequipTarget(target.Id, origin, rotated, autoPlace));
        }
    }
}
