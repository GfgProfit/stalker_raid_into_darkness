namespace RaidIntoDarkness.Inventory.UI
{
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
            registry.Register(new InventoryDelegateAction(USE, 0, context => context.Window.Label(USE, "Use"), context => context.Window.UseItem(context.Item), context => context.Window.CanUseItem(context.Item)));
            registry.Register(new InventoryDelegateAction(EQUIP, 10, context => context.Window.Label(EQUIP, "Equip"), context => context.Window.EquipItem(context.Item), context => context.Window.IsEquipped(context.Item) == false && context.Window.HasEquipmentSlotFor(context.Item), context => context.Window.CanEquipItem(context.Item)));
            registry.Register(new InventoryDelegateAction(UNEQUIP, 11, context => context.Window.Label(UNEQUIP, "Unequip"), context => context.Window.UnequipItem(context.Item), context => context.Window.IsEquipped(context.Item)));
            registry.Register(new InventoryDelegateAction(SPLIT, 20, context => context.Window.Label(SPLIT, "Split"), context => context.Window.OpenSplitDialog(context.Item), context => context.Item.IsStackable, context => context.Item.Amount > 1));
            registry.Register(new InventoryDelegateAction(MOVE, 30, context => context.Window.FormatLabel(MOVE, "Move to {0}", context.Window.GetQuickTransferTargetName(context.Item)), context => context.Window.QuickTransfer(context.Item), context => context.Window.GetQuickTransferTarget(context.Item) != null));
            registry.Register(new InventoryDelegateAction(INSPECT, 40, context => context.Window.Label(INSPECT, "Inspect"), context => context.Window.InspectItem(context.Item)));
            registry.Register(new InventoryDelegateAction(DROP, 50, context => context.Window.Label(DROP, "Drop"), context => context.Window.DropItem(context.Item)));
        }
    }
}
