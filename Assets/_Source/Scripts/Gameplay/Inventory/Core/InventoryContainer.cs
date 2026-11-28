using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    public abstract class InventoryContainer
    {
        private readonly List<ItemInstance> _items = new();
        private readonly Dictionary<string, RectInt> _areas = new();

        private ItemInstance[,] _cells;

        protected InventoryContainer(string id, string ownerId, string displayName, int width, int height)
        {
            Id = id;
            OwnerId = ownerId ?? string.Empty;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            _cells = new ItemInstance[Height, Width];
        }

        public string Id { get; }
        public string OwnerId { get; }
        public string DisplayName { get; }
        public int Width { get; }
        public int Height { get; protected set; }
        public float MaxWeight { get; set; } = -1f;
        public IReadOnlyList<ItemInstance> Items => _items;
        public InventoryModel Model { get; internal set; }

        protected int MaximumStorageHeight => int.MaxValue / Width;
        protected virtual int PlacementHeight => Height;

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

        public bool Contains(ItemInstance item) => item != null && _areas.TryGetValue(item.InstanceId, out RectInt area) && GetItemAt(area.position) == item;

        public ItemInstance GetItemAt(Vector2Int cell) => IsInside(cell) && cell.y < _cells.GetLength(0) ? _cells[cell.y, cell.x] : null;

        public RectInt GetItemArea(ItemInstance item) => item != null && _areas.TryGetValue(item.InstanceId, out RectInt area) ? area : default;

        public bool IsInside(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;

        public bool TryResolvePlacement(ItemInstance item, Vector2Int position, bool rotated, out ItemPlacement placement, out InventoryFailure failure) => TryResolvePlacement(item, position, rotated, out placement, out failure, default);

        internal abstract bool TryResolvePlacement(ItemInstance item, Vector2Int position, bool rotated, out ItemPlacement placement, out InventoryFailure failure, in InventoryPlacementPreview preview);

        public bool CanPlace(ItemInstance item, Vector2Int position, bool rotated, out ItemPlacement placement, out InventoryFailure failure) => CanPlace(item, position, rotated, out placement, out failure, default);

        internal bool CanPlace(ItemInstance item, Vector2Int position, bool rotated, out ItemPlacement placement, out InventoryFailure failure, in InventoryPlacementPreview preview)
        {
            return TryResolvePlacement(item, position, rotated, out placement, out failure, preview) && CanPlaceResolved(item, placement, out failure, preview);
        }

        internal bool CanPlaceResolved(ItemInstance item, ItemPlacement placement, out InventoryFailure failure, in InventoryPlacementPreview preview = default)
        {
            if (IsAreaFree(placement.Area, item, preview) == false)
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

        private bool IsAreaFree(RectInt area, ItemInstance self, in InventoryPlacementPreview preview)
        {
            if (area.xMin < 0 || area.yMin < 0 || area.xMax > Width || area.yMax > PlacementHeight || preview.Overlaps(area))
            {
                return false;
            }

            int occupiedBottom = Mathf.Min(area.yMax, _cells.GetLength(0));
            for (int x = area.xMin; x < area.xMax; x++)
            {
                for (int y = area.yMin; y < occupiedBottom; y++)
                {
                    ItemInstance occupant = _cells[y, x];

                    if (occupant != null && occupant != self && preview.Excludes(occupant) == false)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public bool TryFindSpace(ItemInstance item, bool allowRotation, out ItemPlacement placement) => TryFindSpace(item, allowRotation, out placement, default);

        internal virtual bool TryFindSpace(ItemInstance item, bool allowRotation, out ItemPlacement placement, in InventoryPlacementPreview preview)
        {
            int searchHeight = GetSearchHeight(preview);

            for (int pass = 0; pass < (allowRotation ? 2 : 1); pass++)
            {
                bool rotated = pass == 1;

                if (rotated && item.Definition.AllowRotation == false)
                {
                    continue;
                }

                foreach (Vector2Int candidate in EnumerateCandidatePositions(item, rotated, searchHeight))
                {
                    if (CanPlace(item, candidate, rotated, out placement, out _, preview))
                    {
                        return true;
                    }
                }
            }

            placement = default;
            return false;
        }

        internal virtual int GetSearchHeight(in InventoryPlacementPreview preview) => Height;

        protected virtual IEnumerable<Vector2Int> EnumerateCandidatePositions(ItemInstance item, bool rotated, int searchHeight)
        {
            Vector2Int size = item.GetSize(rotated);

            for (int y = 0; y <= searchHeight - size.y; y++)
            {
                for (int x = 0; x <= Width - size.x; x++)
                {
                    yield return new Vector2Int(x, y);
                }
            }
        }

        internal virtual void CommitPlacement(ItemInstance item, ItemPlacement placement) => Occupy(item, placement);

        internal virtual void Occupy(ItemInstance item, ItemPlacement placement)
        {
            int capacity = _cells.GetLength(0);
            if (placement.Area.yMax > capacity)
            {
                int rows = (int)Math.Min(MaximumStorageHeight, Math.Max(placement.Area.yMax, 2L * capacity));
                var cells = new ItemInstance[rows, Width];
                Array.Copy(_cells, cells, _cells.Length);
                _cells = cells;
            }

            for (int x = placement.Area.xMin; x < placement.Area.xMax; x++)
            {
                for (int y = placement.Area.yMin; y < placement.Area.yMax; y++)
                {
                    _cells[y, x] = item;
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
                        if (_cells[y, x] == item)
                        {
                            _cells[y, x] = null;
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

        internal virtual bool CanRelease(ItemInstance item, out InventoryFailure failure, in InventoryPlacementPreview preview = default)
        {
            failure = InventoryFailure.None;
            return true;
        }
    }
}
