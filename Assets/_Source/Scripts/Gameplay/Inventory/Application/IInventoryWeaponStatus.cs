using System;
using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.Application
{
    internal interface IInventoryWeaponStatus
    {
        event Action FireModeChanged;

        int GetSelectedFireModeIndex(string instanceId);
        bool IsReloading(ItemInstance item);
    }
}
