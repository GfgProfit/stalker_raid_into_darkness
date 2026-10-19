using System;
using System.Collections.Generic;

namespace Inventory.UI
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

    public interface IInventoryItemAction
    {
        string Id { get; }
        int Order { get; }
        string GetLabel(InventoryActionContext context);
        bool IsVisible(InventoryActionContext context);
        bool IsEnabled(InventoryActionContext context);
        void Execute(InventoryActionContext context);
    }

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

    public sealed class InventoryActionRegistry
    {
        private readonly List<IInventoryItemAction> _actions = new();

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

    public static class InventoryBuiltInActions
    {
        public const string USE = "use";
        public const string EQUIP = "equip";
        public const string UNEQUIP = "unequip";
        public const string SPLIT = "split";
        public const string MOVE = "move";
        public const string DROP = "drop";
        public const string INSPECT = "inspect";

        public static void RegisterDefaults(InventoryActionRegistry registry)
        {
            registry.Register(new InventoryDelegateAction(USE, 0, context => context.Window.Label(USE, "Use"), context => context.Window.UseItem(context.Item), enabled: context => context.Window.CanUseItem(context.Item)));
            registry.Register(new InventoryDelegateAction(EQUIP, 10, context => context.Window.Label(EQUIP, "Equip"), context => context.Window.EquipItem(context.Item), context => context.Window.IsEquipped(context.Item) == false && context.Window.HasEquipmentSlotFor(context.Item), context => context.Window.CanEquipItem(context.Item)));
            registry.Register(new InventoryDelegateAction(UNEQUIP, 11, context => context.Window.Label(UNEQUIP, "Unequip"), context => context.Window.UnequipItem(context.Item), context => context.Window.IsEquipped(context.Item)));
            registry.Register(new InventoryDelegateAction(SPLIT, 20, context => context.Window.Label(SPLIT, "Split"), context => context.Window.OpenSplitDialog(context.Item), context => context.Item.IsStackable, context => context.Item.Amount > 1));
            registry.Register(new InventoryDelegateAction(MOVE, 30, context => context.Window.FormatLabel(MOVE, "Move to {0}", context.Window.GetQuickTransferTargetName(context.Item)), context => context.Window.QuickTransfer(context.Item), context => context.Window.GetQuickTransferTarget(context.Item) != null));
            registry.Register(new InventoryDelegateAction(INSPECT, 40, context => context.Window.Label(INSPECT, "Inspect"), context => context.Window.InspectItem(context.Item)));
            registry.Register(new InventoryDelegateAction(DROP, 50, context => context.Window.Label(DROP, "Drop"), context => context.Window.DropItem(context.Item)));
        }
    }
}
