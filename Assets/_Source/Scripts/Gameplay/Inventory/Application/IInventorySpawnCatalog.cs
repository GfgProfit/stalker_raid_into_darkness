using System.Collections.Generic;
using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.Application
{
    internal interface IInventorySpawnCatalog
    {
        IReadOnlyList<ItemDefinition> Definitions { get; }

        int Grant(ItemDefinition definition, int quantity, int condition, out InventoryFailure failure);
    }
}
