using RaidIntoDarkness.Inventory.Infrastructure;
using RaidIntoDarkness.Inventory.UI;

namespace RaidIntoDarkness.Inventory.Tests
{
    internal static class InventoryRuntimeTestAccess
    {
        public static PlayerInventoryRuntime Runtime(this InventoryWindowController controller) => (PlayerInventoryRuntime)controller.PresentationSource;
    }
}
