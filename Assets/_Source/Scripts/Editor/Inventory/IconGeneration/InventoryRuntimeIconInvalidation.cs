using RaidIntoDarkness.Inventory.Infrastructure;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal static class InventoryRuntimeIconInvalidation
    {
        public static void ClearActiveCaches()
        {
            if (UnityEngine.Application.isPlaying == false)
            {
                return;
            }

            foreach (PlayerInventoryRuntime inventory in Object.FindObjectsByType<PlayerInventoryRuntime>(FindObjectsInactive.Include))
            {
                inventory.Icons?.Clear();
            }
        }
    }
}
