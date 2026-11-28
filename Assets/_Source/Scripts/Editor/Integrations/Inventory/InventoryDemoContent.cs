using System.Collections.Generic;
using Inventory;
using Inventory.IconGeneration;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public sealed class InventoryDemoContent
    {
        public Dictionary<string, ItemDefinition> Items { get; } = new();
        public ItemDatabase Database { get; set; }
        public InventorySeed Seed { get; set; }
        public InventorySlotLayout EquipmentLayout { get; set; }
        public ItemIconGeneratorSettings Settings { get; set; }
        public BakedItemIconCatalog Catalog { get; set; }
    }
}
