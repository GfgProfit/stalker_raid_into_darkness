using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    public sealed class GridContainer : InventoryContainer
    {
        public GridContainer(string id, int width, int height, string ownerId = null, string displayName = null) : base(id, ownerId, displayName, width, height)
        {
        }

        public GridContainer(string id, int width, int height, string ownerId, string displayName, int rowGrowth) : this(id, width, height, ownerId, displayName)
        {
            RowGrowth = Mathf.Max(0, rowGrowth);
        }

        public int RowGrowth { get; }

        protected override int PlacementHeight => RowGrowth == 0 ? Height : Height + (MaximumStorageHeight - Height) / RowGrowth * RowGrowth;

        public override bool TryResolvePlacement(ItemInstance item, Vector2Int position, bool rotated, out ItemPlacement placement, out InventoryFailure failure)
        {
            placement = default;

            if (item == null || item.Definition == null)
            {
                failure = InventoryFailure.InvalidItem;
                return false;
            }

            if (rotated && item.Definition.AllowRotation == false && item.Definition.Width != item.Definition.Height)
            {
                failure = InventoryFailure.RotationNotAllowed;
                return false;
            }

            Vector2Int size = item.GetSize(rotated);
            if (position.x < 0 || position.y < 0 || size.x > Width - position.x || size.y > PlacementHeight - position.y)
            {
                failure = InventoryFailure.OutOfBounds;
                return false;
            }

            placement = new ItemPlacement(position, new RectInt(position, size), rotated, string.Empty);
            failure = InventoryFailure.None;
            return true;
        }

        public override bool TryFindSpace(ItemInstance item, bool allowRotation, out ItemPlacement placement, ICollection<ItemInstance> ignored = null)
        {
            if (base.TryFindSpace(item, allowRotation, out placement, ignored))
            {
                return true;
            }

            if (RowGrowth == 0)
            {
                return false;
            }

            int end = Height;
            foreach (ItemInstance placed in Items)
            {
                end = Mathf.Max(end, GetItemArea(placed).yMax);
            }

            bool found = false;
            int requiredHeight = PlacementHeight;
            for (int pass = 0; pass < (allowRotation && item.Definition.AllowRotation ? 2 : 1); pass++)
            {
                bool rotated = pass == 1;
                Vector2Int size = item.GetSize(rotated);
                if (TryFindExpansion(item, rotated, size, end, requiredHeight, ignored, out ItemPlacement candidate))
                {
                    placement = candidate;
                    requiredHeight = Height + (candidate.Area.yMax - Height - 1) / RowGrowth * RowGrowth;
                    found = true;
                }
            }

            return found;
        }

        private bool TryFindExpansion(ItemInstance item, bool rotated, Vector2Int size, int end, int requiredHeight, ICollection<ItemInstance> ignored, out ItemPlacement placement)
        {
            int lastRow = Mathf.Min(end, requiredHeight - size.y);
            for (int y = Mathf.Max(0, Height - size.y + 1); y <= lastRow; y++)
            {
                for (int x = 0; x <= Width - size.x; x++)
                {
                    if (CanPlace(item, new Vector2Int(x, y), rotated, out placement, out _, ignored))
                    {
                        return true;
                    }
                }
            }

            placement = default;
            return false;
        }

        internal override void CommitPlacement(ItemInstance item, ItemPlacement placement)
        {
            base.CommitPlacement(item, placement);
            int required = placement.Area.yMax - Height;
            if (RowGrowth > 0 && required > 0)
            {
                Height += ((required - 1) / RowGrowth + 1) * RowGrowth;
            }
        }
    }
}
