using Inventory;
using Inventory.UI;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class ContextMenuOverhaulStyle
    {
        private const int DEFAULT_GROUP = 2;
        private const int SPLIT_GROUP = 5;
        private const int MARK_GROUP = 6;
        private const int DROP_GROUP = 7;
        private const string ICON_HANDS = "ui_cmo_hands";
        private const string ICON_WATER = "ui_cmo_water";
        private const string ICON_UTENSILS = "ui_cmo_utensils";
        private const string ICON_HEALTH = "ui_cmo_health";
        private const string ICON_CIGARETTE = "ui_cmo_cigarette";
        private const string ICON_VEST = "ui_cmo_vest";
        private const string ICON_HELMET = "ui_cmo_helmet";
        private const string ICON_BACKPACK = "ui_cmo_backpack";
        private const string ICON_CELL = "ui_cmo_cell";
        private const string ICON_DIVIDE = "ui_cmo_divide";
        private const string ICON_ARROWS = "ui_cmo_arrows";
        private const string ICON_ARROW_DOWN = "ui_cmo_arrow_down";
        private const string ICON_STAR = "ui_cmo_star";
        private const string ICON_UNSTAR = "ui_cmo_unstar";
        private const string ICON_TRASHBIN = "ui_cmo_trashbin";
        private const string ICON_UNTRASHBIN = "ui_cmo_untrashbin";
        private const string ICON_SCOPE = "ui_cmo_scope";
        private const string ICON_SILENCER = "ui_cmo_silencer";
        private const string ICON_PATCH = "ui_cmo_patch";
        private const string TAG_DRINK = "drink";
        private const string TAG_FOOD = "food";
        private const string TAG_MEDKIT = "medkit";
        private const string TAG_SMOKE = "smoke";

        private readonly InventoryWindow _window;
        private readonly InventorySortingPlus _sorting;

        public ContextMenuOverhaulStyle(InventoryWindow window, InventorySortingPlus sorting)
        {
            _window = window;
            _sorting = sorting;
        }

        public InventoryActionStyle Resolve(InventoryActionContext context, IInventoryItemAction action)
        {
            ItemDefinition definition = context.Item.Definition;

            switch (action.Id)
            {
                case InventoryBuiltInActions.USE:
                    return Style(UseIcon(definition), DEFAULT_GROUP);
                case InventoryBuiltInActions.EQUIP:
                    return Style(SlotIcon(definition, ICON_CELL), DEFAULT_GROUP);
                case InventoryBuiltInActions.UNEQUIP:
                    return Style(SlotIcon(definition, ICON_BACKPACK), DEFAULT_GROUP);
                case InventoryWeaponAttachments.DETACH_ACTION:
                    return Style(ICON_SILENCER, DEFAULT_GROUP);
                case InventoryWeaponAttachments.DETACH_SCOPE_ACTION:
                    return Style(ICON_SCOPE, DEFAULT_GROUP);
                case InventoryWindowController.OUTFIT_PATCH_ACTION:
                    return Style(ICON_PATCH, DEFAULT_GROUP);
                case InventoryBuiltInActions.MOVE:
                    return Style(ICON_ARROWS, DEFAULT_GROUP);
                case InventoryBuiltInActions.SPLIT:
                    return Style(ICON_DIVIDE, SPLIT_GROUP);
                case InventoryBuiltInActions.DROP:
                    return Style(ICON_ARROW_DOWN, DROP_GROUP);
                case InventoryWindowController.FAVORITE_ACTION:
                    return Style(_sorting != null && _sorting.IsFavorite(definition) ? ICON_UNSTAR : ICON_STAR, MARK_GROUP);
                case InventoryWindowController.JUNK_ACTION:
                    return Style(_sorting != null && _sorting.IsJunk(definition) ? ICON_UNTRASHBIN : ICON_TRASHBIN, MARK_GROUP);
                default:
                    return Style(action.Id.StartsWith(InventoryWeaponAttachments.ATTACH_ACTION, System.StringComparison.Ordinal) ? definition.GetComponent<WeaponAddonComponent>()?.Kind == WeaponAddonKind.Scope ? ICON_SCOPE : ICON_SILENCER : null, DEFAULT_GROUP);
            }
        }

        private static string UseIcon(ItemDefinition definition)
        {
            if (definition.HasTag(TAG_SMOKE))
            {
                return ICON_CIGARETTE;
            }

            if (definition.HasTag(TAG_DRINK))
            {
                return ICON_WATER;
            }

            if (definition.HasTag(TAG_FOOD))
            {
                return ICON_UTENSILS;
            }

            return definition.HasTag(TAG_MEDKIT) ? ICON_HEALTH : ICON_HANDS;
        }

        private static string SlotIcon(ItemDefinition definition, string fallback)
        {
            if (definition.IsCategory(EquipmentStatsFormatter.OUTFIT_CATEGORY))
            {
                return ICON_VEST;
            }

            if (definition.IsCategory(EquipmentStatsFormatter.HELMET_CATEGORY))
            {
                return ICON_HELMET;
            }

            return definition.IsCategory(EquipmentStatsFormatter.BACKPACK_CATEGORY) ? ICON_BACKPACK : fallback;
        }

        private InventoryActionStyle Style(string iconId, int group) => new(iconId != null && _window.Theme != null ? _window.Theme.Icon(iconId) : null, group);
    }
}
