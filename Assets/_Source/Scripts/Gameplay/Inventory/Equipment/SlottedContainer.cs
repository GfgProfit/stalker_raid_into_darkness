using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    public sealed class SlottedContainer : InventoryContainer
    {
        private readonly ReadOnlyCollection<SlotDefinition> _slots;
        private readonly SlotDefinition[,] _slotByCell;
        private readonly HashSet<SlotId> _lockedSlots = new();
        private readonly Dictionary<SlotId, SlotDefinition> _slotById;

        public SlottedContainer(string id, IReadOnlyList<SlotDefinition> slots, int width, int height, bool isEquipment, string ownerId = null, string displayName = null) : base(id, ownerId, displayName, width, height)
        {
            _slots = new List<SlotDefinition>(slots).AsReadOnly();

            if (SlotDefinition.TryCreateIndex(_slots, out Dictionary<SlotId, SlotDefinition> byId, out string error) == false)
            {
                throw new ArgumentException(error, nameof(slots));
            }

            _slotById = byId;
            _slotByCell = new SlotDefinition[Width, Height];
            IsEquipment = isEquipment;

            foreach (SlotDefinition slot in _slots)
            {
                if (slot.Width <= 0 || slot.Height <= 0 || slot.Bounds.xMin < 0 || slot.Bounds.yMin < 0 || slot.Bounds.xMax > Width || slot.Bounds.yMax > Height)
                {
                    throw new ArgumentException($"Slot '{slot.Id}' is outside the container bounds.", nameof(slots));
                }

                if (slot.Rule.StartsLocked)
                {
                    _lockedSlots.Add(slot.Id);
                }

                for (int x = slot.Bounds.xMin; x < slot.Bounds.xMax; x++)
                {
                    for (int y = slot.Bounds.yMin; y < slot.Bounds.yMax; y++)
                    {
                        if (_slotByCell[x, y] != null)
                        {
                            throw new ArgumentException($"Slots '{_slotByCell[x, y].Id}' and '{slot.Id}' overlap.", nameof(slots));
                        }

                        _slotByCell[x, y] = slot;
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

        public SlotDefinition GetSlot(SlotId slotId) => _slotById.TryGetValue(slotId, out SlotDefinition slot) ? slot : null;
        public SlotDefinition GetSlotAt(Vector2Int cell) => IsInside(cell) ? _slotByCell[cell.x, cell.y] : null;
        public bool IsSlotLocked(SlotId slotId) => _lockedSlots.Contains(slotId);

        public ItemInstance GetSlotOccupant(SlotId slotId) => GetSlotOccupant(slotId, null, default);

        private ItemInstance GetSlotOccupant(SlotId slotId, ItemInstance ignoredItem, in InventoryPlacementPreview preview)
        {
            SlotDefinition slot = GetSlot(slotId);

            if (slot == null)
            {
                return null;
            }

            ItemInstance reservedOccupant = preview.GetReservedOccupant(slot.Id, ignoredItem);

            if (reservedOccupant != null)
            {
                return reservedOccupant;
            }

            for (int x = slot.Bounds.xMin; x < slot.Bounds.xMax; x++)
            {
                for (int y = slot.Bounds.yMin; y < slot.Bounds.yMax; y++)
                {
                    ItemInstance occupant = GetItemAt(new Vector2Int(x, y));

                    if (occupant != null && occupant != ignoredItem && preview.Excludes(occupant) == false)
                    {
                        return occupant;
                    }
                }
            }

            return null;
        }

        public bool IsSlotOccupied(SlotId slotId) => GetSlotOccupant(slotId) != null;

        public bool IsSlotAvailable(SlotId slotId) => IsSlotAvailable(GetSlot(slotId), null);

        public bool IsSlotAvailable(SlotDefinition slot, ItemInstance ignoredOccupant) => IsSlotAvailable(slot, ignoredOccupant, default);

        private bool IsSlotAvailable(SlotDefinition slot, ItemInstance ignoredOccupant, in InventoryPlacementPreview preview)
        {
            if (slot == null || _lockedSlots.Contains(slot.Id))
            {
                return false;
            }

            IReadOnlyList<SlotId> required = slot.Rule.RequiresOccupiedSlots;

            for (int i = 0; i < required.Count; i++)
            {
                ItemInstance occupant = GetSlotOccupant(required[i], ignoredOccupant, preview);

                if (occupant == null)
                {
                    return false;
                }
            }

            IReadOnlyList<SlotId> blocking = slot.Rule.BlockedByOccupiedSlots;

            for (int i = 0; i < blocking.Count; i++)
            {
                ItemInstance occupant = GetSlotOccupant(blocking[i], ignoredOccupant, preview);

                if (occupant != null)
                {
                    return false;
                }
            }

            return true;
        }

        public bool AcceptsDefinition(SlotDefinition slot, ItemDefinition definition) => slot != null && slot.Rule.AcceptsDefinition(definition);

        public bool CanSetSlotLocked(SlotId slotId, bool locked) => GetSlot(slotId) != null && (locked == false || IsSlotOccupied(slotId) == false);

        internal bool SetSlotLocked(SlotId slotId, bool locked)
        {
            if (CanSetSlotLocked(slotId, locked) == false)
            {
                return false;
            }

            return locked ? _lockedSlots.Add(slotId) : _lockedSlots.Remove(slotId);
        }

        internal override bool TryResolvePlacement(ItemInstance item, Vector2Int position, bool rotated, out ItemPlacement placement, out InventoryFailure failure, in InventoryPlacementPreview preview)
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

            if (IsSlotAvailable(slot, item, preview) == false)
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

        protected override IEnumerable<Vector2Int> EnumerateCandidatePositions(ItemInstance item, bool rotated, int searchHeight)
        {
            Vector2Int size = item.GetSize(rotated);

            for (int i = 0; i < _slots.Count; i++)
            {
                SlotDefinition slot = _slots[i];

                if (slot.Mode == SlotMode.Single)
                {
                    if (rotated == false)
                    {
                        yield return slot.Bounds.position;
                    }

                    continue;
                }

                for (int y = slot.Bounds.yMin; y <= slot.Bounds.yMax - size.y; y++)
                {
                    for (int x = slot.Bounds.xMin; x <= slot.Bounds.xMax - size.x; x++)
                    {
                        yield return new Vector2Int(x, y);
                    }
                }
            }
        }

        internal override bool CanRelease(ItemInstance item, out InventoryFailure failure, in InventoryPlacementPreview preview = default)
        {
            failure = InventoryFailure.None;

            if (Contains(item) == false)
            {
                return true;
            }

            SlotId releasedSlotId = item.Location.SlotId;

            for (int i = 0; i < _slots.Count; i++)
            {
                SlotDefinition slot = _slots[i];

                if (slot.Id == releasedSlotId || GetSlotOccupant(slot.Id, item, preview) == null)
                {
                    continue;
                }

                if (IsSlotAvailable(slot, item, preview) == false)
                {
                    failure = InventoryFailure.DependentSlotOccupied;
                    return false;
                }
            }

            return true;
        }
    }
}
