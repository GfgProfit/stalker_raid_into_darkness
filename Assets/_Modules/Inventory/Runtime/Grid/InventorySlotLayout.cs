using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    [Serializable]
    public sealed class SlotLayoutBinding
    {
        [SerializeField] private string _key;
        [SerializeField] private string _slotId;
        [SerializeField] private string _displayName;
        [SerializeField] private string _group;
        [SerializeField] private SlotMode _mode = SlotMode.Grid;
        [SerializeField] private SlotRule _rule = new();

        public SlotLayoutBinding()
        {
        }

        public SlotLayoutBinding(char key, string slotId, string displayName, SlotMode mode, SlotRule rule, string group = null)
        {
            _key = key.ToString();
            _slotId = slotId;
            _displayName = displayName;
            _mode = mode;
            _rule = rule ?? new SlotRule();
            _group = group;
        }

        public char Key => string.IsNullOrEmpty(_key) ? '\0' : _key[0];
        public string SlotId => _slotId ?? string.Empty;
        public string DisplayName => _displayName ?? string.Empty;
        public string Group => _group ?? string.Empty;
        public SlotMode Mode => _mode;
        public SlotRule Rule => _rule ?? new SlotRule();
    }

    [CreateAssetMenu(menuName = "Inventory/Slot Layout", fileName = "SlotLayout")]
    public sealed class InventorySlotLayout : ScriptableObject
    {
        [SerializeField] [TextArea(2, 16)] private string _slotMask = "AAB\nAAC";
        [SerializeField] private List<SlotLayoutBinding> _bindings = new();

        public string SlotMask => _slotMask ?? string.Empty;
        public IReadOnlyList<SlotLayoutBinding> Bindings => _bindings;

        public void SetData(string slotMask, IEnumerable<SlotLayoutBinding> bindings)
        {
            _slotMask = slotMask;
            _bindings = bindings == null ? new List<SlotLayoutBinding>() : new List<SlotLayoutBinding>(bindings);
        }

        public bool TryBuildSlots(out List<SlotDefinition> slots, out int width, out int height, out string error) => SlotMaskParser.TryParse(SlotMask, _bindings, out slots, out width, out height, out error);
    }

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
                        boundsByKey.Add(key, new SlotBounds(key, x, y));
                        order.Add(key);
                    }
                }
            }

            if (boundsByKey.Count == 0)
            {
                error = "Slot layout has no slots.";
                return false;
            }

            var usedIds = new HashSet<string>(StringComparer.Ordinal);

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

                for (int y = bounds.MinY; y <= bounds.MaxY; y++)
                {
                    for (int x = bounds.MinX; x <= bounds.MaxX; x++)
                    {
                        if (GetCell(rows, x, y) != key)
                        {
                            error = $"Slot '{key}' must not contain holes or other slot keys.";
                            return false;
                        }
                    }
                }

                SlotLayoutBinding binding = FindBinding(bindings, key);
                string slotId = binding != null && binding.SlotId.Length > 0 ? binding.SlotId : key.ToString();

                if (usedIds.Add(slotId) == false)
                {
                    error = $"Slot id '{slotId}' is used by more than one mask key.";
                    return false;
                }

                slots.Add(new SlotDefinition(slotId, binding?.DisplayName, new RectInt(bounds.MinX, bounds.MinY, slotWidth, slotHeight), binding?.Mode ?? SlotMode.Grid, binding?.Rule, binding?.Group));
            }

            slots.Sort((left, right) =>
            {
                int yCompare = left.Y.CompareTo(right.Y);
                return yCompare != 0 ? yCompare : left.X.CompareTo(right.X);
            });

            return true;
        }

        private static SlotLayoutBinding FindBinding(IReadOnlyList<SlotLayoutBinding> bindings, char key)
        {
            if (bindings == null)
            {
                return null;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i] != null && bindings[i].Key == key)
                {
                    return bindings[i];
                }
            }

            return null;
        }

        private static char GetCell(string[] rows, int x, int y)
        {
            if (y < 0 || y >= rows.Length || x < 0 || x >= rows[y].Length)
            {
                return '\0';
            }

            char cell = rows[y][x];
            return IsEmptyCell(cell) ? '\0' : cell;
        }

        private struct SlotBounds
        {
            public readonly char Key;

            public SlotBounds(char key, int x, int y)
            {
                Key = key;
                MinX = x;
                MaxX = x;
                MinY = y;
                MaxY = y;
                CellCount = 1;
            }

            public int MinX { get; private set; }
            public int MaxX { get; private set; }
            public int MinY { get; private set; }
            public int MaxY { get; private set; }
            public int CellCount { get; private set; }

            public void Add(int x, int y)
            {
                MinX = Mathf.Min(MinX, x);
                MaxX = Mathf.Max(MaxX, x);
                MinY = Mathf.Min(MinY, y);
                MaxY = Mathf.Max(MaxY, y);
                CellCount++;
            }
        }
    }
}
