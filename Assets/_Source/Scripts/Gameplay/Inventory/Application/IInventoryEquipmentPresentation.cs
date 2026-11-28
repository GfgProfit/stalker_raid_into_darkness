using System;
using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.Application
{
    internal interface IInventoryEquipmentPresentation : IEquipmentAnimationRequests
    {
        event Action<ItemInstance> ItemUseChanged;
        event Action<EquipmentAnimationRequestKind, ItemInstance, InventoryResult, bool> OperationCompleted;

        bool IsBusy { get; }
        bool IsInUse(ItemInstance item);
        bool HasPatchMotion(ItemInstance item);
        bool TryQueuePatch(ItemInstance item);
    }
}
