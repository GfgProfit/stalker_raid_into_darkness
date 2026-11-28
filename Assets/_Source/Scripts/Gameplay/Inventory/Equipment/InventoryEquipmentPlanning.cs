using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal static class InventoryEquipmentPlanning
    {
        public static SlotDefinition FindSwapCandidate(ItemInstance item, SlottedContainer target, InventoryContainer parking)
        {
            foreach (SlotDefinition slot in EnumerateEquipCandidates(item, target))
            {
                if (GetSwappableOccupant(item, target, slot, parking) != null)
                {
                    return slot;
                }
            }

            return null;
        }

        public static ItemInstance GetSwappableOccupant(ItemInstance item, SlottedContainer target, SlotDefinition slot, InventoryContainer parking)
        {
            if (parking == null || slot == null || slot.Mode != SlotMode.Single || slot.Rule.AcceptsDefinition(item.Definition) == false)
            {
                return null;
            }

            ItemInstance occupant = target.GetSlotOccupant(slot.Id);
            return occupant != null && occupant != item && target.IsSlotAvailable(slot, occupant) ? occupant : null;
        }

        public static InventoryResult PlanSwap(ItemInstance item, InventoryContainer source, SlottedContainer target, SlotDefinition slot, InventoryContainer parking, SlottedContainer outfitEquipment, out EquipmentSwap swap)
        {
            swap = default;

            if (parking == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            ItemInstance displaced = GetSwappableOccupant(item, target, slot, parking);

            if (displaced == null)
            {
                return InventoryResult.Fail(InventoryFailure.SlotUnavailable);
            }

            if (parking == target)
            {
                return InventoryResult.Fail(InventoryFailure.Duplicate);
            }

            if (target.CanRelease(displaced, out InventoryFailure failure) == false)
            {
                return InventoryResult.Fail(failure);
            }

            if (parking.WeightAllows(displaced.TotalWeight) == false || parking.TryFindSpace(displaced, true, out ItemPlacement parked) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            var afterParking = new InventoryPlacementPreview(displaced);

            if (target.CanPlace(item, slot.Bounds.position, false, out ItemPlacement incoming, out failure, afterParking) == false)
            {
                return InventoryResult.Fail(failure);
            }

            InventoryPlacementPreview sourcePreview = source == target ? afterParking : default;

            if (source.CanRelease(item, out failure, sourcePreview) == false)
            {
                return InventoryResult.Fail(failure);
            }

            if (source != target && target.WeightAllows(item.TotalWeight - displaced.TotalWeight) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            InventoryContainer destination = parking;
            ItemPlacement displacedPlacement = parked;

            if (source == target && (target != outfitEquipment || incoming.SlotId != SlotId.Outfit || OutfitSlotRules.ShouldLock(item, item.Location.SlotId) == false))
            {
                InventoryPlacementPreview afterMove = new InventoryPlacementPreview(displaced, item).Reserve(item, incoming);

                if (target.CanPlace(displaced, item.Location.Position, false, out ItemPlacement returned, out _, afterMove))
                {
                    destination = target;
                    displacedPlacement = returned;
                }
            }

            swap = new EquipmentSwap(displaced, destination, displacedPlacement, incoming);
            return InventoryResult.Ok(item, item.Amount);
        }

        public static IEnumerable<SlotDefinition> EnumerateEquipCandidates(ItemInstance item, SlottedContainer equipment)
        {
            HashSet<SlotId> yielded = null;
            IReadOnlyList<SlotId> preferred = item.Definition.GetComponent<EquippableItemComponent>()?.PreferredSlotIds;

            if (preferred != null)
            {
                for (int i = 0; i < preferred.Count; i++)
                {
                    SlotDefinition slot = equipment.GetSlot(preferred[i]);

                    if (slot != null && slot.Mode == SlotMode.Single && (yielded ??= new HashSet<SlotId>()).Add(slot.Id))
                    {
                        yield return slot;
                    }
                }
            }

            IReadOnlyList<SlotDefinition> slots = equipment.Slots;

            for (int i = 0; i < slots.Count; i++)
            {
                SlotDefinition slot = slots[i];

                if (slot.Mode == SlotMode.Single && slot.Rule.AcceptsDefinition(item.Definition) && (yielded == null || yielded.Add(slot.Id)))
                {
                    yield return slot;
                }
            }
        }
    }
}
