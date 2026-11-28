using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.Application
{
    internal interface IInventoryPresentationSource
    {
        event Action<ItemInstance, InventoryResult> ForcedDropCompleted;

        bool TryGetPresentation(out InventoryPresentationBindings bindings);
        UniTask PrewarmIconsAsync(CancellationToken cancellationToken);
    }
}
