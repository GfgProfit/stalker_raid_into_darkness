using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    public enum InventoryFailure
    {
        None,
        InvalidItem,
        ItemNotFound,
        ContainerNotFound,
        OutOfBounds,
        Occupied,
        NoSlot,
        SlotUnavailable,
        SlotRejected,
        ValidatorRejected,
        RotationNotAllowed,
        NotStackable,
        StackFull,
        InvalidAmount,
        NoSpace,
        AlreadyInInventory,
        DependentSlotOccupied,
        Duplicate
    }

    public readonly struct ItemPlacement
    {
        public ItemPlacement(Vector2Int position, RectInt area, bool rotated, string slotId)
        {
            Position = position;
            Area = area;
            Rotated = rotated;
            SlotId = slotId ?? string.Empty;
        }

        public Vector2Int Position { get; }
        public RectInt Area { get; }
        public bool Rotated { get; }
        public string SlotId { get; }
    }

    public abstract class InventoryContainer
    {
        private readonly ItemInstance[,] _cells;
        private readonly List<ItemInstance> _items = new();
        private readonly Dictionary<string, RectInt> _areas = new();

        protected InventoryContainer(string id, string ownerId, string displayName, int width, int height)
        {
            Id = id;
            OwnerId = ownerId ?? string.Empty;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            _cells = new ItemInstance[Width, Height];
        }

        public string Id { get; }
        public string OwnerId { get; }
        public string DisplayName { get; }
        public int Width { get; }
        public int Height { get; }
        public float MaxWeight { get; set; } = -1f;
        public IReadOnlyList<ItemInstance> Items => _items;
        public InventoryModel Model { get; internal set; }

        public float TotalWeight
        {
            get
            {
                float weight = 0f;

                for (int i = 0; i < _items.Count; i++)
                {
                    weight += _items[i].TotalWeight;
                }

                return weight;
            }
        }

        public bool Contains(ItemInstance item) => item != null && _areas.ContainsKey(item.InstanceId);

        public ItemInstance GetItemAt(Vector2Int cell) => IsInside(cell) ? _cells[cell.x, cell.y] : null;

        public RectInt GetItemArea(ItemInstance item) => item != null && _areas.TryGetValue(item.InstanceId, out RectInt area) ? area : default;

        public bool IsInside(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;

        public abstract bool TryResolvePlacement(ItemInstance item, Vector2Int position, bool rotated, out ItemPlacement placement, out InventoryFailure failure);

        public bool CanPlace(ItemInstance item, Vector2Int position, bool rotated, out ItemPlacement placement, out InventoryFailure failure, ICollection<ItemInstance> ignored = null)
        {
            if (TryResolvePlacement(item, position, rotated, out placement, out failure) == false)
            {
                return false;
            }

            if (IsAreaFree(placement.Area, item, ignored) == false)
            {
                failure = InventoryFailure.Occupied;
                return false;
            }

            if (Model != null && Model.ValidatePlacement(item, this, placement, out failure) == false)
            {
                return false;
            }

            failure = InventoryFailure.None;
            return true;
        }

        public void CollectOverlaps(RectInt area, ItemInstance self, List<ItemInstance> overlaps)
        {
            overlaps.Clear();

            for (int x = area.xMin; x < area.xMax; x++)
            {
                for (int y = area.yMin; y < area.yMax; y++)
                {
                    ItemInstance occupant = GetItemAt(new Vector2Int(x, y));

                    if (occupant != null && occupant != self && overlaps.Contains(occupant) == false)
                    {
                        overlaps.Add(occupant);
                    }
                }
            }
        }

        public bool IsAreaFree(RectInt area, ItemInstance self, ICollection<ItemInstance> ignored = null)
        {
            if (area.xMin < 0 || area.yMin < 0 || area.xMax > Width || area.yMax > Height)
            {
                return false;
            }

            for (int x = area.xMin; x < area.xMax; x++)
            {
                for (int y = area.yMin; y < area.yMax; y++)
                {
                    ItemInstance occupant = _cells[x, y];

                    if (occupant != null && occupant != self && (ignored == null || ignored.Contains(occupant) == false))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public virtual bool TryFindSpace(ItemInstance item, bool allowRotation, out ItemPlacement placement, ICollection<ItemInstance> ignored = null)
        {
            for (int pass = 0; pass < (allowRotation ? 2 : 1); pass++)
            {
                bool rotated = pass == 1;

                if (rotated && item.Definition.AllowRotation == false)
                {
                    continue;
                }

                foreach (Vector2Int candidate in EnumerateCandidatePositions(item, rotated))
                {
                    if (CanPlace(item, candidate, rotated, out placement, out _, ignored))
                    {
                        return true;
                    }
                }
            }

            placement = default;
            return false;
        }

        protected virtual IEnumerable<Vector2Int> EnumerateCandidatePositions(ItemInstance item, bool rotated)
        {
            Vector2Int size = item.GetSize(rotated);

            for (int y = 0; y <= Height - size.y; y++)
            {
                for (int x = 0; x <= Width - size.x; x++)
                {
                    yield return new Vector2Int(x, y);
                }
            }
        }

        internal virtual void Occupy(ItemInstance item, ItemPlacement placement)
        {
            for (int x = placement.Area.xMin; x < placement.Area.xMax; x++)
            {
                for (int y = placement.Area.yMin; y < placement.Area.yMax; y++)
                {
                    _cells[x, y] = item;
                }
            }

            _areas[item.InstanceId] = placement.Area;

            if (_items.Contains(item) == false)
            {
                _items.Add(item);
            }

            item.Location = new ItemLocation(Id, placement.Position, placement.Rotated, placement.SlotId);
        }

        internal virtual void Release(ItemInstance item)
        {
            if (_areas.TryGetValue(item.InstanceId, out RectInt area))
            {
                for (int x = area.xMin; x < area.xMax; x++)
                {
                    for (int y = area.yMin; y < area.yMax; y++)
                    {
                        if (_cells[x, y] == item)
                        {
                            _cells[x, y] = null;
                        }
                    }
                }

                _areas.Remove(item.InstanceId);
            }

            _items.Remove(item);

            if (item.Location.ContainerId == Id)
            {
                item.Location = ItemLocation.None;
            }
        }

        internal virtual bool CanRelease(ItemInstance item, out InventoryFailure failure)
        {
            failure = InventoryFailure.None;
            return true;
        }
    }

    public sealed class GridContainer : InventoryContainer
    {
        public GridContainer(string id, int width, int height, string ownerId = null, string displayName = null) : base(id, ownerId, displayName, width, height)
        {
        }

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
            var area = new RectInt(position, size);

            if (area.xMin < 0 || area.yMin < 0 || area.xMax > Width || area.yMax > Height)
            {
                failure = InventoryFailure.OutOfBounds;
                return false;
            }

            placement = new ItemPlacement(position, area, rotated, string.Empty);
            failure = InventoryFailure.None;
            return true;
        }
    }
}
