using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    public sealed class SlottedContainer : InventoryContainer
    {
        private readonly List<SlotDefinition> _slots;
        private readonly SlotDefinition[,] _slotByCell;
        private readonly HashSet<string> _lockedSlots = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SlotDefinition> _slotById = new(StringComparer.Ordinal);

        public SlottedContainer(string id, IReadOnlyList<SlotDefinition> slots, int width, int height, bool isEquipment, string ownerId = null, string displayName = null) : base(id, ownerId, displayName, width, height)
        {
            _slots = new List<SlotDefinition>(slots);
            _slotByCell = new SlotDefinition[Width, Height];
            IsEquipment = isEquipment;

            foreach (SlotDefinition slot in _slots)
            {
                _slotById[slot.Id] = slot;

                if (slot.Rule.StartsLocked)
                {
                    _lockedSlots.Add(slot.Id);
                }

                for (int x = slot.Bounds.xMin; x < slot.Bounds.xMax; x++)
                {
                    for (int y = slot.Bounds.yMin; y < slot.Bounds.yMax; y++)
                    {
                        if (x >= 0 && y >= 0 && x < Width && y < Height)
                        {
                            _slotByCell[x, y] = slot;
                        }
                    }
                }
            }
        }

        public static SlottedContainer FromLayout(string id, InventorySlotLayout layout, bool isEquipment, string ownerId = null, string displayName = null)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (layout.TryBuildSlots(out List<SlotDefinition> slots, out int width, out int height, out string error) == false)
            {
                throw new ArgumentException(error);
            }

            return new SlottedContainer(id, slots, width, height, isEquipment, ownerId, displayName);
        }

        public bool IsEquipment { get; }
        public IReadOnlyList<SlotDefinition> Slots => _slots;

        public SlotDefinition GetSlot(string slotId) => slotId != null && _slotById.TryGetValue(slotId, out SlotDefinition slot) ? slot : null;
        public SlotDefinition GetSlotAt(Vector2Int cell) => IsInside(cell) ? _slotByCell[cell.x, cell.y] : null;
        public bool IsSlotLocked(string slotId) => _lockedSlots.Contains(slotId);

        public ItemInstance GetSlotOccupant(string slotId)
        {
            SlotDefinition slot = GetSlot(slotId);

            if (slot == null)
            {
                return null;
            }

            for (int x = slot.Bounds.xMin; x < slot.Bounds.xMax; x++)
            {
                for (int y = slot.Bounds.yMin; y < slot.Bounds.yMax; y++)
                {
                    ItemInstance occupant = GetItemAt(new Vector2Int(x, y));

                    if (occupant != null)
                    {
                        return occupant;
                    }
                }
            }

            return null;
        }

        public bool IsSlotOccupied(string slotId) => GetSlotOccupant(slotId) != null;

        public bool IsSlotAvailable(string slotId) => IsSlotAvailable(GetSlot(slotId), null);

        public bool IsSlotAvailable(SlotDefinition slot, ItemInstance ignoredOccupant)
        {
            if (slot == null || _lockedSlots.Contains(slot.Id))
            {
                return false;
            }

            IReadOnlyList<string> required = slot.Rule.RequiresOccupiedSlots;

            for (int i = 0; i < required.Count; i++)
            {
                ItemInstance occupant = GetSlotOccupant(required[i]);

                if (occupant == null || occupant == ignoredOccupant)
                {
                    return false;
                }
            }

            IReadOnlyList<string> blocking = slot.Rule.BlockedByOccupiedSlots;

            for (int i = 0; i < blocking.Count; i++)
            {
                ItemInstance occupant = GetSlotOccupant(blocking[i]);

                if (occupant != null && occupant != ignoredOccupant)
                {
                    return false;
                }
            }

            return true;
        }

        public bool AcceptsDefinition(SlotDefinition slot, ItemDefinition definition) => slot != null && slot.Rule.AcceptsDefinition(definition);

        public bool CanSetSlotLocked(string slotId, bool locked) => GetSlot(slotId) != null && (locked == false || IsSlotOccupied(slotId) == false);

        internal bool SetSlotLocked(string slotId, bool locked)
        {
            if (CanSetSlotLocked(slotId, locked) == false)
            {
                return false;
            }

            return locked ? _lockedSlots.Add(slotId) : _lockedSlots.Remove(slotId);
        }

        public override bool TryResolvePlacement(ItemInstance item, Vector2Int position, bool rotated, out ItemPlacement placement, out InventoryFailure failure)
        {
            placement = default;

            if (item == null || item.Definition == null)
            {
                failure = InventoryFailure.InvalidItem;
                return false;
            }

            SlotDefinition slot = GetSlotAt(position);

            if (slot == null)
            {
                failure = InventoryFailure.NoSlot;
                return false;
            }

            if (IsSlotAvailable(slot, item) == false)
            {
                failure = InventoryFailure.SlotUnavailable;
                return false;
            }

            if (slot.Rule.AcceptsDefinition(item.Definition) == false)
            {
                failure = InventoryFailure.SlotRejected;
                return false;
            }

            if (Model != null && Model.ValidateSlot(item, this, slot) == false)
            {
                failure = InventoryFailure.ValidatorRejected;
                return false;
            }

            if (slot.Mode == SlotMode.Single)
            {
                placement = new ItemPlacement(slot.Bounds.position, slot.Bounds, false, slot.Id);
                failure = InventoryFailure.None;
                return true;
            }

            if (rotated && item.Definition.AllowRotation == false && item.Definition.Width != item.Definition.Height)
            {
                failure = InventoryFailure.RotationNotAllowed;
                return false;
            }

            var area = new RectInt(position, item.GetSize(rotated));

            if (area.xMin < slot.Bounds.xMin || area.yMin < slot.Bounds.yMin || area.xMax > slot.Bounds.xMax || area.yMax > slot.Bounds.yMax)
            {
                failure = InventoryFailure.OutOfBounds;
                return false;
            }

            placement = new ItemPlacement(position, area, rotated, slot.Id);
            failure = InventoryFailure.None;
            return true;
        }

        protected override IEnumerable<Vector2Int> EnumerateCandidatePositions(ItemInstance item, bool rotated)
        {
            foreach (SlotDefinition slot in _slots)
            {
                if (slot.Mode == SlotMode.Single)
                {
                    if (rotated == false)
                    {
                        yield return slot.Bounds.position;
                    }

                    continue;
                }

                Vector2Int size = item.GetSize(rotated);

                for (int y = slot.Bounds.yMin; y <= slot.Bounds.yMax - size.y; y++)
                {
                    for (int x = slot.Bounds.xMin; x <= slot.Bounds.xMax - size.x; x++)
                    {
                        yield return new Vector2Int(x, y);
                    }
                }
            }
        }

        internal override bool CanRelease(ItemInstance item, out InventoryFailure failure)
        {
            failure = InventoryFailure.None;

            if (Contains(item) == false)
            {
                return true;
            }

            string releasedSlotId = item.Location.SlotId;

            foreach (SlotDefinition slot in _slots)
            {
                if (slot.Id == releasedSlotId || IsSlotOccupied(slot.Id) == false)
                {
                    continue;
                }

                ItemInstance occupant = GetSlotOccupant(slot.Id);

                if (occupant == item)
                {
                    continue;
                }

                if (IsSlotAvailable(slot, item) == false)
                {
                    failure = InventoryFailure.DependentSlotOccupied;
                    return false;
                }
            }

            return true;
        }
    }
}
