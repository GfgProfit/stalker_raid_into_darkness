using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal static class InventoryStackPlanning
    {
        public static InventoryResult PlanTransfer(ItemInstance item, InventoryContainer source, InventoryContainer target, int amount, bool mergeStacks, out InventoryTransferPlan plan)
        {
            plan = default;

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }


            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (target.Id == item.ContainerId)
            {
                return InventoryResult.Fail(InventoryFailure.Duplicate);
            }

            int moving = Mathf.Min(amount, item.Amount);

            if (moving <= 0)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidAmount);
            }

            bool whole = moving == item.Amount;

            if (whole && source != null && source.CanRelease(item, out InventoryFailure releaseFailure) == false)
            {
                return InventoryResult.Fail(releaseFailure);
            }

            if (target.WeightAllows(item.UnitWeight * moving) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            List<KeyValuePair<ItemInstance, int>> merges = mergeStacks && item.IsStackable ? Plan(item, moving, target, item) : null;
            int merged = merges != null ? Sum(merges) : 0;
            int remainder = moving - merged;
            ItemPlacement placement = default;
            ItemInstance carrier = whole ? item : remainder > 0 ? CreatePiece(item, remainder) : null;

            if (remainder > 0)
            {
                if (target.TryFindSpaceForAmount(carrier, remainder, true, out placement) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }
            }

            plan = new InventoryTransferPlan(item, source, target, moving, remainder, whole, carrier, merges, placement);
            return InventoryResult.Ok(item, moving);
        }

        public static InventoryResult PlanMerge(ItemInstance source, InventoryContainer sourceContainer, ItemInstance target, InventoryContainer targetContainer, int amount)
        {
            if (source == null || target == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (source.CanStackWith(target) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NotStackable);
            }

            int moved = Mathf.Min(amount, source.Amount, target.FreeStackSpace);

            if (moved <= 0)
            {
                return InventoryResult.Fail(amount <= 0 ? InventoryFailure.InvalidAmount : InventoryFailure.StackFull);
            }

            if (sourceContainer != targetContainer && targetContainer.WeightAllows(source.UnitWeight * moved) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            if (moved == source.Amount && sourceContainer != null && sourceContainer.CanRelease(source, out InventoryFailure releaseFailure) == false)
            {
                return InventoryResult.Fail(releaseFailure);
            }

            return InventoryResult.Ok(target, moved);
        }

        public static ItemInstance CreatePiece(ItemInstance source, int amount)
        {
            var piece = new ItemInstance(source.Definition, amount);
            piece.CopyStateFrom(source);
            return piece;
        }

        public static bool TryFindTargetAt(ItemInstance item, InventoryContainer container, Vector2Int cell, out ItemInstance stack)
        {
            stack = container.GetItemAt(cell);

            if (stack != null && stack != item && stack.CanStackWith(item) && stack.IsFullStack == false)
            {
                return true;
            }

            stack = null;
            return false;
        }

        public static List<KeyValuePair<ItemInstance, int>> Plan(ItemInstance item, int amount, InventoryContainer container, ItemInstance exclude, ItemInstance consumed = null, List<KeyValuePair<ItemInstance, int>> reserved = null)
        {
            var merges = new List<KeyValuePair<ItemInstance, int>>();
            int remaining = amount;

            if (item.IsStackable == false)
            {
                return merges;
            }

            foreach (ItemInstance candidate in container.Items)
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (candidate == exclude || candidate.CanStackWith(item) == false)
                {
                    continue;
                }

                int free = candidate.FreeStackSpace;
                if (candidate == consumed)
                {
                    free = candidate.Amount > 1 ? free + 1 : 0;
                }

                if (reserved != null)
                {
                    foreach (KeyValuePair<ItemInstance, int> merge in reserved)
                    {
                        if (merge.Key == candidate)
                        {
                            free -= merge.Value;
                            break;
                        }
                    }
                }

                if (free <= 0)
                {
                    continue;
                }

                int moved = Mathf.Min(remaining, free);
                merges.Add(new KeyValuePair<ItemInstance, int>(candidate, moved));
                remaining -= moved;
            }

            return merges;
        }

        public static int Sum(List<KeyValuePair<ItemInstance, int>> merges)
        {
            int sum = 0;

            foreach (KeyValuePair<ItemInstance, int> merge in merges)
            {
                sum += merge.Value;
            }

            return sum;
        }
    }
}
