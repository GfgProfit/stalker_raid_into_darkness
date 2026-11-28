using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    public static class SlotMaskParser
    {
        public static bool IsEmptyCell(char cell) => cell == ' ' || cell == '.' || cell == '_' || cell == '-';

        public static bool TryParse(string mask, IReadOnlyList<SlotLayoutBinding> bindings, out List<SlotDefinition> slots, out int width, out int height, out string error)
        {
            slots = new List<SlotDefinition>();
            width = 0;
            height = 0;
            error = string.Empty;
            string[] rows = (mask ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            height = rows.Length;
            var boundsByKey = new Dictionary<char, SlotBounds>();
            var order = new List<char>();

            for (int y = 0; y < rows.Length; y++)
            {
                width = Mathf.Max(width, rows[y].Length);

                for (int x = 0; x < rows[y].Length; x++)
                {
                    char key = rows[y][x];

                    if (IsEmptyCell(key))
                    {
                        continue;
                    }

                    if (boundsByKey.TryGetValue(key, out SlotBounds bounds))
                    {
                        bounds.Add(x, y);
                        boundsByKey[key] = bounds;
                    }
                    else
                    {
                        boundsByKey.Add(key, new SlotBounds(x, y));
                        order.Add(key);
                    }
                }
            }

            if (boundsByKey.Count == 0)
            {
                error = "Slot layout has no slots.";
                return false;
            }

            var bindingsByKey = new Dictionary<char, SlotLayoutBinding>();

            if (bindings != null)
            {
                foreach (SlotLayoutBinding binding in bindings)
                {
                    if (binding == null || boundsByKey.ContainsKey(binding.Key) == false)
                    {
                        error = "Slot binding must have one key present in the mask.";
                        return false;
                    }

                    if (bindingsByKey.TryAdd(binding.Key, binding) == false)
                    {
                        error = $"Mask key '{binding.Key}' has more than one slot binding.";
                        return false;
                    }
                }
            }

            foreach (char key in order)
            {
                SlotBounds bounds = boundsByKey[key];
                int slotWidth = bounds.MaxX - bounds.MinX + 1;
                int slotHeight = bounds.MaxY - bounds.MinY + 1;

                if (slotWidth * slotHeight != bounds.CellCount)
                {
                    error = $"Slot '{key}' must be a filled rectangle.";
                    return false;
                }

                if (bindingsByKey.TryGetValue(key, out SlotLayoutBinding binding) == false)
                {
                    error = $"Mask key '{key}' does not have an explicit slot binding.";
                    return false;
                }

                slots.Add(new SlotDefinition(binding.SlotId, binding.DisplayName, new RectInt(bounds.MinX, bounds.MinY, slotWidth, slotHeight), binding.Mode, binding.Rule, binding.Group));
            }

            if (SlotDefinition.TryCreateIndex(slots, out _, out error) == false)
            {
                return false;
            }

            slots.Sort((left, right) =>
            {
                int yCompare = left.Y.CompareTo(right.Y);
                return yCompare != 0 ? yCompare : left.X.CompareTo(right.X);
            });

            return true;
        }
    }
}
