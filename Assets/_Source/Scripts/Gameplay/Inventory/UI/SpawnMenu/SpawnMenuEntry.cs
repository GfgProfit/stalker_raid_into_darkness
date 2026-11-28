using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class SpawnMenuEntry
    {
        public SpawnMenuEntry(ItemDefinition definition, SpawnMenuCategory category, int? price, bool supportsCondition, int packSize)
        {
            Definition = definition;
            Category = category;
            Group = (InventoryCategoryOrder.Get(definition), category.Order);
            Price = price;
            SupportsCondition = supportsCondition;
            PackSize = packSize;
        }

        public ItemDefinition Definition { get; }
        public SpawnMenuCategory Category { get; }
        public (int Kind, int Category) Group { get; }
        public int? Price { get; }
        public bool SupportsCondition { get; }
        public int PackSize { get; }
        public int MaxQuantity => int.MaxValue / PackSize;
        public int GetUnitCount(int quantity) => quantity * PackSize;
        public int Quantity { get; set; } = 1;
        public int Condition { get; set; } = 100;
    }
}
