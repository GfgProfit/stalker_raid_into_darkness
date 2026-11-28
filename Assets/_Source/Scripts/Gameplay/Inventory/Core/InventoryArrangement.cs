using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal static class InventoryArrangement
    {
        public static InventoryResult Plan(InventoryContainer container, IReadOnlyList<ItemInstance> order, IReadOnlyList<int> groups, bool allowRotation, out ItemPlacement[] placements)
        {
            placements = null;
            if (container is SlottedContainer || order == null || groups == null || order.Count != groups.Count || order.Count != container.Items.Count)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            var occupied = new bool[container.Width, container.Height];
            placements = new ItemPlacement[order.Count];
            var items = new HashSet<ItemInstance>();
            int rowEnd = -1;
            int startRow = 0;

            for (int i = 0; i < order.Count; i++)
            {
                ItemInstance item = order[i];

                if (item == null || container.Contains(item) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.ItemNotFound);
                }

                if (items.Add(item) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.Duplicate);
                }

                if (i == 0 || groups[i] != groups[i - 1])
                {
                    startRow = rowEnd + 1;
                }

                if (TryFitArranged(occupied, item, startRow, allowRotation, out ItemPlacement placement) == false && TryFitArranged(occupied, item, 0, allowRotation, out placement) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }

                for (int x = placement.Area.xMin; x < placement.Area.xMax; x++)
                {
                    for (int y = placement.Area.yMin; y < placement.Area.yMax; y++)
                    {
                        occupied[x, y] = true;
                    }
                }

                rowEnd = Mathf.Max(rowEnd, placement.Area.yMax - 1);
                placements[i] = placement;
            }

            return InventoryResult.Ok(null, order.Count);
        }

        private static bool TryFitArranged(bool[,] occupied, ItemInstance item, int startRow, bool allowRotation, out ItemPlacement placement)
        {
            int width = occupied.GetLength(0);
            int height = occupied.GetLength(1);

            for (int pass = 0; pass < (allowRotation && item.Definition.AllowRotation ? 2 : 1); pass++)
            {
                bool rotated = pass == 1;
                Vector2Int size = item.GetSize(rotated);

                for (int y = Mathf.Max(0, startRow); y <= height - size.y; y++)
                {
                    for (int x = 0; x <= width - size.x; x++)
                    {
                        if (IsFree(occupied, x, y, size))
                        {
                            var position = new Vector2Int(x, y);
                            placement = new ItemPlacement(position, new RectInt(position, size), rotated, SlotId.None);
                            return true;
                        }
                    }
                }
            }

            placement = default;
            return false;
        }

        private static bool IsFree(bool[,] occupied, int startX, int startY, Vector2Int size)
        {
            for (int x = startX; x < startX + size.x; x++)
            {
                for (int y = startY; y < startY + size.y; y++)
                {
                    if (occupied[x, y])
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
