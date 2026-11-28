namespace RaidIntoDarkness.Inventory.UI
{
    public interface IInventoryItemAction
    {
        string Id { get; }
        int Order { get; }
        string GetLabel(InventoryActionContext context);
        bool IsVisible(InventoryActionContext context);
        bool IsEnabled(InventoryActionContext context);
        void Execute(InventoryActionContext context);
    }
}
