using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal sealed class InventoryPlacementPlanner
    {
        private readonly List<ItemInstance> _overlaps = new();

        public InventoryResult Move(ItemInstance item, InventoryContainer source, InventoryContainer target, Vector2Int position, bool rotated, bool allowMerge, out ItemPlacement placement, out ItemInstance mergeTarget)
        {
            placement = default;
            mergeTarget = null;

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (target.TryResolvePlacement(item, position, rotated, out placement, out InventoryFailure failure) == false)
            {
                if (allowMerge && InventoryStackPlanning.TryFindTargetAt(item, target, position, out mergeTarget))
                {
                    return InventoryStackPlanning.PlanMerge(item, source, mergeTarget, target, int.MaxValue);
                }

                return InventoryResult.Fail(failure);
            }

            target.CollectOverlaps(placement.Area, item, _overlaps);

            if (_overlaps.Count > 0)
            {
                if (allowMerge && _overlaps.Count == 1 && _overlaps[0].CanStackWith(item) && _overlaps[0].IsFullStack == false)
                {
                    mergeTarget = _overlaps[0];
                    return InventoryStackPlanning.PlanMerge(item, source, mergeTarget, target, int.MaxValue);
                }

                return InventoryResult.Fail(InventoryFailure.Occupied);
            }

            if (target.CanPlaceResolved(item, placement, out failure) == false)
            {
                return InventoryResult.Fail(failure);
            }

            bool sameSlot = source == target && placement.SlotId != SlotId.None && placement.SlotId == item.Location.SlotId;

            if (source != null && sameSlot == false && source.CanRelease(item, out InventoryFailure releaseFailure) == false)
            {
                return InventoryResult.Fail(releaseFailure);
            }

            if (source != target && target.WeightAllows(item.TotalWeight) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            return InventoryResult.Ok(item, item.Amount);
        }

        public InventoryResult Split(ItemInstance item, InventoryContainer source, InventoryContainer target, int amount, Vector2Int position, bool rotated, out ItemPlacement placement, out ItemInstance mergeTarget, out int moved)
        {
            placement = default;
            mergeTarget = null;
            moved = 0;

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (item.IsStackable == false)
            {
                return InventoryResult.Fail(InventoryFailure.NotStackable);
            }

            if (amount <= 0 || amount >= item.Amount)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidAmount);
            }

            ItemInstance probe = InventoryStackPlanning.CreatePiece(item, amount);
            InventoryFailure failure;

            if (target.TryResolvePlacement(probe, position, rotated, out placement, out failure))
            {
                target.CollectOverlaps(placement.Area, probe, _overlaps);
            }
            else
            {
                _overlaps.Clear();

                if (InventoryStackPlanning.TryFindTargetAt(probe, target, position, out ItemInstance stack))
                {
                    _overlaps.Add(stack);
                }
                else
                {
                    return InventoryResult.Fail(failure);
                }
            }

            if (_overlaps.Count > 0)
            {
                ItemInstance candidate = _overlaps[0];

                if (_overlaps.Count == 1 && candidate != item && candidate.CanStackWith(item) && candidate.IsFullStack == false)
                {
                    mergeTarget = candidate;
                    moved = Mathf.Min(amount, candidate.FreeStackSpace);
                    return target != source && target.WeightAllows(item.UnitWeight * moved) == false ? InventoryResult.Fail(InventoryFailure.NoSpace) : InventoryResult.Ok(candidate, moved);
                }

                return InventoryResult.Fail(InventoryFailure.Occupied);
            }

            if (target.CanPlaceResolved(probe, placement, out failure) == false)
            {
                return InventoryResult.Fail(failure);
            }

            if (target != source && target.WeightAllows(probe.TotalWeight) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            moved = amount;
            return InventoryResult.Ok(item, amount);
        }
    }
}
