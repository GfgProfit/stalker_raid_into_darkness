using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal sealed class OutfitSlotRules
    {
        private readonly IOutfitDropSource _dropSource;
        private readonly Action<ItemInstance, InventoryResult> _dropCompleted;

        public OutfitSlotRules(SlottedContainer equipment, GridContainer backpack, IOutfitDropSource dropSource, Action<ItemInstance, InventoryResult> dropCompleted)
        {
            Equipment = equipment;
            Backpack = backpack;
            _dropSource = dropSource;
            _dropCompleted = dropCompleted;
        }

        public SlottedContainer Equipment { get; }
        public GridContainer Backpack { get; }

        public InventoryResult Plan(ItemInstance incoming, InventoryContainer target, ItemPlacement placement, bool releaseSource, bool prepareDrops, bool backpackAvailable, out List<OutfitSlotRelocation> relocations, EquipmentSwap swap = default)
        {
            relocations = null;
            if (placement.SlotId != SlotId.Outfit || target != Equipment)
            {
                return InventoryResult.Ok(incoming, 0);
            }

            SlottedContainer equipment = Equipment;
            if (TryFindOutfitBlocker(incoming, equipment, SlotId.Head, out ItemInstance helmet) == false || TryFindOutfitBlocker(incoming, equipment, SlotId.Backpack, out ItemInstance bag) == false)
            {
                return InventoryResult.Fail(InventoryFailure.Occupied);
            }

            if (helmet == null && bag == null)
            {
                return InventoryResult.Ok(incoming, 0);
            }

            GridContainer backpack = Backpack;
            bool incomingLeavesBackpack = releaseSource && incoming.ContainerId == backpack.Id;
            var backpackPreview = new InventoryPlacementPreview(incomingLeavesBackpack ? incoming : null);
            var reservations = new List<KeyValuePair<ItemInstance, ItemPlacement>>(3);
            float weightDelta = incomingLeavesBackpack ? -incoming.TotalWeight : 0f;
            int reservedRow = 0;

            if (swap.DisplacedDestination == backpack)
            {
                reservations.Add(new KeyValuePair<ItemInstance, ItemPlacement>(swap.DisplacedItem, swap.DisplacedPlacement));
                reservedRow = swap.DisplacedPlacement.Area.yMax;
                weightDelta += swap.DisplacedItem.TotalWeight;
            }

            var planned = new List<OutfitSlotRelocation>(2);
            InventoryPlacementPreview equipmentPreview = new InventoryPlacementPreview(helmet, bag).Reserve(incoming, placement);
            bool ready = false;
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    ItemInstance incompatible = i == 0 ? helmet : bag;
                    if (incompatible == null)
                    {
                        continue;
                    }

                    if (equipment.CanRelease(incompatible, out InventoryFailure releaseFailure, equipmentPreview) == false)
                    {
                        return InventoryResult.Fail(releaseFailure);
                    }

                    if (backpackAvailable && backpack.WeightAllows(weightDelta + incompatible.TotalWeight))
                    {
                        OutfitSlotRelocation first = planned.Count > 0 ? planned[0] : default;
                        List<KeyValuePair<ItemInstance, int>> merges = InventoryStackPlanning.Plan(incompatible, incompatible.Amount, backpack, incomingLeavesBackpack ? incoming : null, reserved: first.Merges);
                        int remaining = incompatible.Amount - InventoryStackPlanning.Sum(merges);
                        if (remaining > 0 && first.Remainder > 0 && first.Item.CanStackWith(incompatible))
                        {
                            int merge = Mathf.Min(remaining, first.Item.MaxStack - first.Remainder);
                            if (merge > 0)
                            {
                                merges.Add(new KeyValuePair<ItemInstance, int>(first.Item, merge));
                                remaining -= merge;
                            }
                        }

                        ItemPlacement destination = default;
                        if (remaining == 0 || backpack.TryFindSpaceForAmount(incompatible, remaining, true, out destination, backpackPreview.WithGridReservations(reservations, reservedRow)))
                        {
                            planned.Add(new OutfitSlotRelocation(incompatible, destination, merges, remaining));
                            if (remaining > 0)
                            {
                                reservations.Add(new KeyValuePair<ItemInstance, ItemPlacement>(incompatible, destination));
                                reservedRow = Mathf.Max(reservedRow, destination.Area.yMax);
                            }

                            weightDelta += incompatible.TotalWeight;
                            continue;
                        }
                    }

                    if (_dropSource == null || _dropSource.CanPrepare(incompatible) == false)
                    {
                        return InventoryResult.Fail(InventoryFailure.NoSpace);
                    }

                    if (prepareDrops)
                    {
                        InventoryResult drop = _dropSource.TryPrepare(incompatible, out IPreparedOutfitDrop prepared);
                        if (drop.Success == false)
                        {
                            return drop;
                        }

                        planned.Add(new OutfitSlotRelocation(incompatible, prepared));
                    }
                }

                relocations = planned;
                ready = true;
                return InventoryResult.Ok(incoming, 0);
            }
            finally
            {
                if (ready == false)
                {
                    foreach (OutfitSlotRelocation relocation in planned)
                    {
                        relocation.Drop?.Cancel();
                    }
                }
            }
        }

        private static bool TryFindOutfitBlocker(ItemInstance incoming, SlottedContainer equipment, SlotId slotId, out ItemInstance blocker)
        {
            blocker = null;
            SlotDefinition slot = equipment.GetSlot(slotId);
            if (slot == null || ShouldLock(incoming, slotId) == false)
            {
                return true;
            }

            if (slot.Mode == SlotMode.Single)
            {
                ItemInstance occupant = equipment.GetSlotOccupant(slotId);
                blocker = occupant != incoming ? occupant : null;
                return true;
            }

            foreach (ItemInstance item in equipment.Items)
            {
                if (item.Location.SlotId != slotId || item == incoming)
                {
                    continue;
                }

                if (blocker != null)
                {
                    return false;
                }

                blocker = item;
            }

            return true;
        }

        public void ReportDrop(ItemInstance item, int amount) => _dropCompleted?.Invoke(item, InventoryResult.Ok(item, amount));

        public static bool ShouldLock(ItemInstance outfit, SlotId slotId)
        {
            EquipmentStatsComponent stats = outfit?.Definition.GetComponent<EquipmentStatsComponent>();
            return stats != null && (slotId == SlotId.Head && stats.HelmetAvailable == false || slotId == SlotId.Backpack && stats.RepairType == "outfit_exo");
        }
    }
}
