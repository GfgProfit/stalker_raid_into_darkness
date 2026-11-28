using System;
using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.Application
{
    internal interface IWorldItemInteraction
    {
        bool CanDrop { get; }
        string ActivePickupBinding { get; }

        event Action<string> HintChanged;
        event Action<bool> DropAvailabilityChanged;
        event Action<(ItemInstance Item, InventoryResult Result)> PickupCompleted;
        event Action<ItemInstance> PickupSucceeded;
    }
}
