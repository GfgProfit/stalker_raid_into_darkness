using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.Application;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.Infrastructure.IconGeneration;

namespace RaidIntoDarkness.Inventory.Editor
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
