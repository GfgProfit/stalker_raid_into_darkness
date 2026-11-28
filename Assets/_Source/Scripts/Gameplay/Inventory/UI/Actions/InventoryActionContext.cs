namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class InventoryActionContext
    {
        public InventoryActionContext(InventoryWindow window, ItemInstance item, InventoryGridViewBase sourceGrid)
        {
            Window = window;
            Item = item;
            SourceGrid = sourceGrid;
        }

        public InventoryWindow Window { get; }
        public InventoryModel Model => Window.Model;
        public ItemInstance Item { get; }
        public InventoryGridViewBase SourceGrid { get; }
        public InventoryContainer SourceContainer => Model.GetContainer(Item.ContainerId);
    }
}
