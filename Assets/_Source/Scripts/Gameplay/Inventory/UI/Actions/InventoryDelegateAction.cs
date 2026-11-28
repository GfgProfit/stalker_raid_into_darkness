using System;

namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class InventoryDelegateAction : IInventoryItemAction
    {
        private readonly Func<InventoryActionContext, string> _label;
        private readonly Func<InventoryActionContext, bool> _visible;
        private readonly Func<InventoryActionContext, bool> _enabled;
        private readonly Action<InventoryActionContext> _execute;

        public InventoryDelegateAction(string id, int order, Func<InventoryActionContext, string> label, Action<InventoryActionContext> execute, Func<InventoryActionContext, bool> visible = null, Func<InventoryActionContext, bool> enabled = null)
        {
            Id = id;
            Order = order;
            _label = label;
            _execute = execute;
            _visible = visible;
            _enabled = enabled;
        }

        public string Id { get; }
        public int Order { get; }

        public string GetLabel(InventoryActionContext context) => _label?.Invoke(context) ?? Id;
        public bool IsVisible(InventoryActionContext context) => _visible == null || _visible(context);
        public bool IsEnabled(InventoryActionContext context) => _enabled == null || _enabled(context);
        public void Execute(InventoryActionContext context) => _execute?.Invoke(context);
    }
}
