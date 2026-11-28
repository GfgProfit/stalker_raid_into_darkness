using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    public sealed class SlotDefinition
    {
        public SlotDefinition(SlotId id, string displayName, RectInt bounds, SlotMode mode, SlotRule rule, string group = null)
        {
            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? Id.ToString() : displayName;
            Bounds = bounds;
            Mode = mode;
            Rule = rule ?? new SlotRule();
            Group = group ?? string.Empty;
        }

        public SlotId Id { get; }
        public string DisplayName { get; }
        public RectInt Bounds { get; }
        public SlotMode Mode { get; }
        public SlotRule Rule { get; }
        public string Group { get; }
        public int X => Bounds.x;
        public int Y => Bounds.y;
        public int Width => Bounds.width;
        public int Height => Bounds.height;

        public bool Contains(Vector2Int cell) => cell.x >= Bounds.xMin && cell.y >= Bounds.yMin && cell.x < Bounds.xMax && cell.y < Bounds.yMax;

        internal static bool TryCreateIndex(IReadOnlyList<SlotDefinition> slots, out Dictionary<SlotId, SlotDefinition> index, out string error)
        {
            index = new Dictionary<SlotId, SlotDefinition>(slots.Count);

            foreach (SlotDefinition slot in slots)
            {
                if (slot == null)
                {
                    error = "Slot definition is missing.";
                    return false;
                }

                if (slot.Id == SlotId.None || Enum.IsDefined(typeof(SlotId), slot.Id) == false)
                {
                    error = $"Slot id '{slot.Id}' is invalid.";
                    return false;
                }

                if (index.TryAdd(slot.Id, slot) == false)
                {
                    error = $"Slot id '{slot.Id}' is used by more than one slot.";
                    return false;
                }
            }

            foreach (SlotDefinition slot in slots)
            {
                if (ValidateDependencies(slot, slot.Rule.RequiresOccupiedSlots, index, out error) == false || ValidateDependencies(slot, slot.Rule.BlockedByOccupiedSlots, index, out error) == false)
                {
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool ValidateDependencies(SlotDefinition slot, IReadOnlyList<SlotId> dependencies, Dictionary<SlotId, SlotDefinition> index, out string error)
        {
            for (int i = 0; i < dependencies.Count; i++)
            {
                SlotId dependency = dependencies[i];

                if (dependency == slot.Id || index.ContainsKey(dependency) == false)
                {
                    error = $"Slot '{slot.Id}' depends on invalid slot '{dependency}'.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
    }
}
