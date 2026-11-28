using System;
using RaidIntoDarkness.Player;

namespace RaidIntoDarkness.Inventory
{
    internal sealed class OutfitSlotRules
    {
        private readonly WorldItemInteractor _worldItems;
        private readonly StalkerPlayerController _player;
        private readonly Action<ItemInstance, InventoryResult> _dropCompleted;

        public OutfitSlotRules(SlottedContainer equipment, GridContainer backpack, WorldItemInteractor worldItems, StalkerPlayerController player, Action<ItemInstance, InventoryResult> dropCompleted)
        {
            Equipment = equipment;
            Backpack = backpack;
            _worldItems = worldItems;
            _player = player;
            _dropCompleted = dropCompleted;
        }

        public SlottedContainer Equipment { get; }
        public GridContainer Backpack { get; }

        public bool CanPrepareDrop(ItemInstance item) => _worldItems != null && _worldItems.isActiveAndEnabled && _worldItems.CanPrepareOutfitDrop(_player, item);

        public InventoryResult TryPrepareDrop(ItemInstance item, out PreparedWorldItemDrop drop) => _worldItems.TryPrepareOutfitDrop(_player, item, out drop);

        public void CompleteDrop(PreparedWorldItemDrop drop, ItemInstance item) => WorldItemInteractor.CompleteOutfitDrop(_player, drop, item);

        public void CancelDrop(PreparedWorldItemDrop drop) => WorldItemInteractor.CancelOutfitDrop(drop);

        public void ReportDrop(ItemInstance item, int amount) => _dropCompleted?.Invoke(item, InventoryResult.Ok(item, amount));

        public static bool ShouldLock(ItemInstance outfit, SlotId slotId)
        {
            EquipmentStatsComponent stats = outfit?.Definition.GetComponent<EquipmentStatsComponent>();
            return stats != null && (slotId == SlotId.Head && stats.HelmetAvailable == false || slotId == SlotId.Backpack && stats.RepairType == "outfit_exo");
        }

        internal static ItemInstance FindEquipped(InventoryContainer equipment, string category)
        {
            if (equipment != null)
            {
                foreach (ItemInstance item in equipment.Items)
                {
                    if (item.Definition.IsCategory(category))
                    {
                        return item;
                    }
                }
            }

            return null;
        }
    }
}
