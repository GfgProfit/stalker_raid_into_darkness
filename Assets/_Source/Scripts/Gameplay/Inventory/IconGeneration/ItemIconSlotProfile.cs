using RaidIntoDarkness.Inventory.Domain;
using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
    [Serializable]
    public struct ItemIconSlotProfile : IEquatable<ItemIconSlotProfile>
    {
        [SerializeField] private bool _restrictCategory;
        [SerializeField] private string _acceptedCategory;
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private InventorySlotLayout _equipmentLayout;
        [SerializeField] private SlotId[] _slotIds;

        public ItemIconSlotProfile(bool restrictCategory, string acceptedCategory, int width, int height)
        {
            _restrictCategory = restrictCategory;
            _acceptedCategory = restrictCategory ? acceptedCategory : string.Empty;
            _width = Mathf.Max(1, width);
            _height = Mathf.Max(1, height);
            _equipmentLayout = null;
            _slotIds = Array.Empty<SlotId>();
        }

        public int Width => Mathf.Max(1, _width);
        public int Height => Mathf.Max(1, _height);
        public bool RestrictsCategory => _restrictCategory;
        public string AcceptedCategory => _acceptedCategory ?? string.Empty;

        public bool Accepts(ItemDefinition definition)
        {
            if (definition == null || (_restrictCategory && !definition.IsCategory(AcceptedCategory)))
            {
                return false;
            }

            if (_equipmentLayout == null)
            {
                return true;
            }

            var bindings = _equipmentLayout.Bindings;
            var slotIds = _slotIds ?? Array.Empty<SlotId>();
            for (int i = 0; i < bindings.Count; i++)
            {
                SlotLayoutBinding binding = bindings[i];
                if (binding != null && binding.Mode == SlotMode.Single && Array.IndexOf(slotIds, binding.SlotId) >= 0 && _equipmentLayout.SlotMask.IndexOf(binding.Key) >= 0 && binding.Rule.AcceptsDefinition(definition))
                {
                    return true;
                }
            }

            return false;
        }

        public bool Equals(ItemIconSlotProfile other)
        {
            if (_restrictCategory != other._restrictCategory || AcceptedCategory != other.AcceptedCategory || Width != other.Width || Height != other.Height || _equipmentLayout != other._equipmentLayout)
            {
                return false;
            }

            ReadOnlySpan<SlotId> slots = _slotIds.AsSpan();
            ReadOnlySpan<SlotId> otherSlots = other._slotIds.AsSpan();

            if (slots.Length != otherSlots.Length)
            {
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != otherSlots[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => obj is ItemIconSlotProfile other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_restrictCategory);
            hash.Add(AcceptedCategory);
            hash.Add(Width);
            hash.Add(Height);
            hash.Add(_equipmentLayout);
            foreach (SlotId slotId in _slotIds ?? Array.Empty<SlotId>())
            {
                hash.Add(slotId);
            }

            return hash.ToHashCode();
        }
    }
}
