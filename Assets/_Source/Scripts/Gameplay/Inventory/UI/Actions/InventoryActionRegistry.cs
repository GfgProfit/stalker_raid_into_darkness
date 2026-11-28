using System;
using System.Collections.Generic;

namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class InventoryActionRegistry
    {
        private readonly List<IInventoryItemAction> _actions = new();
        private readonly List<IInventoryItemAction> _visibleActions = new();

        public IReadOnlyList<IInventoryItemAction> Actions => _actions;

        public void Register(IInventoryItemAction action)
        {
            if (action == null)
            {
                return;
            }

            Unregister(action.Id);
            _actions.Add(action);
            _actions.Sort((left, right) => left.Order.CompareTo(right.Order));
        }

        public bool Unregister(string actionId) => _actions.RemoveAll(action => action.Id == actionId) > 0;

        public IInventoryItemAction Find(string actionId) => _actions.Find(action => action.Id == actionId);

        internal void BuildMenu(InventoryActionContext context, Func<InventoryActionContext, IInventoryItemAction, InventoryActionStyle> styleProvider, List<InventoryContextMenuEntry> entries)
        {
            CollectVisible(context, _visibleActions);
            entries.Clear();

            foreach (IInventoryItemAction action in _visibleActions)
            {
                InventoryActionStyle style = styleProvider != null ? styleProvider(context, action) : default;
                entries.Add(new InventoryContextMenuEntry(action.GetLabel(context), action.IsEnabled(context), () =>
                {
                    if (context.Model?.ContainsItem(context.Item) == true)
                    {
                        action.Execute(context);
                    }
                }, style.Icon, style.Group));
            }
        }

        public void CollectVisible(InventoryActionContext context, List<IInventoryItemAction> result)
        {
            result.Clear();

            foreach (IInventoryItemAction action in _actions)
            {
                if (action.IsVisible(context))
                {
                    result.Add(action);
                }
            }
        }
    }
}
