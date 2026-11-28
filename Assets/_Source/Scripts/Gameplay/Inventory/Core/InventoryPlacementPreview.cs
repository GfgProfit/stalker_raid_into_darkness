using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    internal readonly struct InventoryPlacementPreview
    {
        private readonly ItemInstance _firstExcluded;
        private readonly ItemInstance _secondExcluded;
        private readonly ItemInstance _firstReservedItem;
        private readonly ItemInstance _secondReservedItem;
        private readonly ItemPlacement _firstReservation;
        private readonly ItemPlacement _secondReservation;
        private readonly IReadOnlyList<KeyValuePair<ItemInstance, ItemPlacement>> _gridReservations;
        private readonly int _gridReservedRow;

        public InventoryPlacementPreview(ItemInstance firstExcluded, ItemInstance secondExcluded = null) : this(firstExcluded, secondExcluded, null, default, null, default)
        {
        }

        private InventoryPlacementPreview(ItemInstance firstExcluded, ItemInstance secondExcluded, ItemInstance firstReservedItem, ItemPlacement firstReservation, ItemInstance secondReservedItem, ItemPlacement secondReservation, IReadOnlyList<KeyValuePair<ItemInstance, ItemPlacement>> gridReservations = null, int gridReservedRow = 0)
        {
            _firstExcluded = firstExcluded;
            _secondExcluded = secondExcluded;
            _firstReservedItem = firstReservedItem;
            _firstReservation = firstReservation;
            _secondReservedItem = secondReservedItem;
            _secondReservation = secondReservation;
            _gridReservations = gridReservations;
            _gridReservedRow = gridReservedRow;
        }

        public int GridReservedRow => _gridReservedRow;
        public int LastReservedRow => Mathf.Max(Mathf.Max(_firstReservation.Area.yMax, _secondReservation.Area.yMax), _gridReservedRow);

        public InventoryPlacementPreview Reserve(ItemInstance item, ItemPlacement placement)
        {
            if (_firstReservedItem == null)
            {
                return new InventoryPlacementPreview(_firstExcluded, _secondExcluded, item, placement, null, default, _gridReservations, _gridReservedRow);
            }

            if (_secondReservedItem != null)
            {
                throw new InvalidOperationException("The inventory placement preview already has two reservations.");
            }

            return new InventoryPlacementPreview(_firstExcluded, _secondExcluded, _firstReservedItem, _firstReservation, item, placement, _gridReservations, _gridReservedRow);
        }

        public bool Excludes(ItemInstance item) => item == _firstExcluded || item == _secondExcluded;

        public InventoryPlacementPreview WithGridReservations(IReadOnlyList<KeyValuePair<ItemInstance, ItemPlacement>> reservations, int lastRow)
        {
            return new InventoryPlacementPreview(_firstExcluded, _secondExcluded, _firstReservedItem, _firstReservation, _secondReservedItem, _secondReservation, reservations, lastRow);
        }

        public bool Overlaps(RectInt area)
        {
            if ((_firstReservedItem != null && _firstReservation.Area.Overlaps(area)) || (_secondReservedItem != null && _secondReservation.Area.Overlaps(area)))
            {
                return true;
            }

            if (_gridReservations != null)
            {
                for (int i = 0; i < _gridReservations.Count; i++)
                {
                    if (_gridReservations[i].Value.Area.Overlaps(area))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public ItemInstance GetReservedOccupant(SlotId slotId, ItemInstance ignoredItem)
        {
            if (_firstReservedItem != ignoredItem && _firstReservation.SlotId == slotId)
            {
                return _firstReservedItem;
            }

            return _secondReservedItem != ignoredItem && _secondReservation.SlotId == slotId ? _secondReservedItem : null;
        }
    }
}
