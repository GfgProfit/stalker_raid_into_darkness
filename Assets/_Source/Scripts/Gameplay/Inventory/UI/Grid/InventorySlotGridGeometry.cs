using System.Collections.Generic;
using RaidIntoDarkness.Inventory.Domain;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventorySlotGridGeometry
    {
        private IReadOnlyList<SlotDefinition> _slots = System.Array.Empty<SlotDefinition>();
        private readonly Dictionary<SlotId, Rect> _slotRects = new();
        private SlotDefinition[,] _slotByCell = new SlotDefinition[0, 0];
        private int _gridWidth;
        private int _gridHeight;
        private Vector2 _contentSize;
        private Vector2 _cellSize;
        private float _slotSpacing;

        public int Columns => _gridWidth;
        public int Rows => _gridHeight;
        public Vector2 ContentSize => _contentSize;

        public Rect GetSlotRect(SlotId slotId) => _slotRects.TryGetValue(slotId, out Rect rect) ? rect : default;

        public void Reset(Vector2 cellSize)
        {
            _slots = System.Array.Empty<SlotDefinition>();
            _slotRects.Clear();
            _gridWidth = 1;
            _gridHeight = 1;
            _slotByCell = new SlotDefinition[1, 1];
            _contentSize = cellSize;
            _cellSize = cellSize;
        }

        public void Build(IReadOnlyList<SlotDefinition> slots, IReadOnlyDictionary<SlotId, InventorySlotVisual> visuals, int width, int height, Vector2 cellSize, float spacing, bool centerRows)
        {
            _slots = slots;
            _slotRects.Clear();
            _gridWidth = width;
            _gridHeight = height;
            _cellSize = cellSize;
            _slotSpacing = spacing;
            _slotByCell = new SlotDefinition[_gridWidth, _gridHeight];

            foreach (SlotDefinition slot in _slots)
            {
                for (int x = slot.Bounds.xMin; x < slot.Bounds.xMax; x++)
                {
                    for (int y = slot.Bounds.yMin; y < slot.Bounds.yMax; y++)
                    {
                        _slotByCell[x, y] = slot;
                    }
                }
            }

            ComputeGeometry(visuals, centerRows);
        }

        public bool TryGetCellAtLocal(Vector2 topLeftLocal, out Vector2Int cell)
        {
            SlotDefinition slot = GetSlotAtVisualPosition(topLeftLocal);

            if (slot == null)
            {
                cell = new Vector2Int(-1, -1);
                return false;
            }

            cell = CellInSlot(slot, topLeftLocal);
            return true;
        }

        public Vector2Int GetPlacementOrigin(Vector2 topLeftPointer, Vector2Int itemSize)
        {
            SlotDefinition hover = GetSlotAtVisualPosition(topLeftPointer);

            if (hover != null && hover.Mode == SlotMode.Single)
            {
                return hover.Bounds.position;
            }

            Vector2 cell = hover != null ? SlotCellSize(hover) : _cellSize;
            Vector2 shifted = topLeftPointer - new Vector2((itemSize.x - 1) * cell.x * 0.5f, (itemSize.y - 1) * cell.y * 0.5f);
            SlotDefinition slot = GetSlotAtVisualPosition(shifted) ?? hover;
            return slot == null ? new Vector2Int(-1, -1) : CellInSlot(slot, shifted);
        }

        public Rect GetAreaRect(RectInt area)
        {
            SlotDefinition slot = GetSlotAtCell(area.position);

            if (slot == null || _slotRects.TryGetValue(slot.Id, out Rect slotRect) == false)
            {
                Vector2 size = _cellSize;
                return new Rect(area.x * size.x, area.y * size.y, area.width * size.x, area.height * size.y);
            }

            if (slot.Mode == SlotMode.Single)
            {
                return slotRect;
            }

            Vector2 cell = SlotCellSize(slot);
            return new Rect(slotRect.x + (area.x - slot.X) * cell.x, slotRect.y + (area.y - slot.Y) * cell.y, area.width * cell.x, area.height * cell.y);
        }

        public Vector2 SlotCellSize(SlotDefinition slot)
        {
            Rect rect = _slotRects[slot.Id];
            return new Vector2(rect.width / Mathf.Max(1, slot.Width), rect.height / Mathf.Max(1, slot.Height));
        }

        private Vector2Int CellInSlot(SlotDefinition slot, Vector2 topLeftLocal)
        {
            Rect rect = _slotRects[slot.Id];
            Vector2 cell = SlotCellSize(slot);
            int x = Mathf.Clamp(Mathf.FloorToInt((topLeftLocal.x - rect.x) / cell.x), 0, slot.Width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((topLeftLocal.y - rect.y) / cell.y), 0, slot.Height - 1);
            return new Vector2Int(slot.X + x, slot.Y + y);
        }

        public SlotDefinition GetSlotAtCell(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= _gridWidth || cell.y >= _gridHeight)
            {
                return null;
            }

            return _slotByCell[cell.x, cell.y];
        }

        private SlotDefinition GetSlotAtVisualPosition(Vector2 visualPosition)
        {
            foreach (SlotDefinition slot in _slots)
            {
                if (_slotRects.TryGetValue(slot.Id, out Rect rect) && rect.Contains(visualPosition))
                {
                    return slot;
                }
            }

            return null;
        }

        private void ComputeGeometry(IReadOnlyDictionary<SlotId, InventorySlotVisual> visuals, bool centerRows)
        {
            Vector2 cell = _cellSize;
            var rowPositions = new float[_gridHeight];
            var rowWidths = new float[_gridHeight];
            float maxRowWidth = 0f;

            for (int y = 0; y < _gridHeight; y++)
            {
                rowWidths[y] = CalculateRowLocalWidth(y, cell.x);
                maxRowWidth = Mathf.Max(maxRowWidth, rowWidths[y]);
            }

            for (int y = 1; y < _gridHeight; y++)
            {
                float gap = HasRowBoundary(y - 1) ? _slotSpacing : 0f;
                rowPositions[y] = rowPositions[y - 1] + cell.y + gap;
            }

            _contentSize = Vector2.zero;

            foreach (SlotDefinition slot in _slots)
            {
                Rect rect;

                if (visuals.TryGetValue(slot.Id, out InventorySlotVisual visual) && visual.OverrideRect)
                {
                    rect = visual.Rect;
                }
                else
                {
                    float x = 0f;

                    for (int y = slot.Y; y < slot.Y + slot.Height; y++)
                    {
                        float offset = centerRows ? (maxRowWidth - rowWidths[y]) * 0.5f : 0f;
                        x = Mathf.Max(x, CalculateSlotLocalX(slot.X, y, cell.x) + offset);
                    }

                    rect = new Rect(x, rowPositions[slot.Y], slot.Width * cell.x, slot.Height * cell.y);
                }

                _slotRects[slot.Id] = rect;
                _contentSize.x = Mathf.Max(_contentSize.x, rect.xMax);
                _contentSize.y = Mathf.Max(_contentSize.y, rect.yMax);
            }
        }

        private float CalculateRowLocalWidth(int y, float cellWidth)
        {
            float width = 0f;
            bool hasVisibleRun = false;
            SlotDefinition previousRun = null;

            for (int x = 0; x < _gridWidth; x++)
            {
                SlotDefinition current = GetSlotAtCell(new Vector2Int(x, y));

                if (current == null)
                {
                    previousRun = null;
                    continue;
                }

                if (current == previousRun)
                {
                    continue;
                }

                if (hasVisibleRun)
                {
                    width += _slotSpacing;
                }

                width += CountRunWidthInRow(x, y, current) * cellWidth;
                hasVisibleRun = true;
                previousRun = current;
            }

            return width;
        }

        private float CalculateSlotLocalX(int targetX, int y, float cellWidth)
        {
            float xPosition = 0f;
            bool hasVisibleRun = false;
            SlotDefinition previousRun = null;

            for (int x = 0; x < _gridWidth; x++)
            {
                SlotDefinition current = GetSlotAtCell(new Vector2Int(x, y));

                if (current == null)
                {
                    previousRun = null;
                    continue;
                }

                if (current == previousRun)
                {
                    continue;
                }

                if (hasVisibleRun)
                {
                    xPosition += _slotSpacing;
                }

                if (x == targetX)
                {
                    return xPosition;
                }

                xPosition += CountRunWidthInRow(x, y, current) * cellWidth;
                hasVisibleRun = true;
                previousRun = current;
            }

            return targetX * cellWidth;
        }

        private int CountRunWidthInRow(int startX, int y, SlotDefinition slot)
        {
            int width = 0;

            for (int x = startX; x < _gridWidth; x++)
            {
                if (GetSlotAtCell(new Vector2Int(x, y)) != slot)
                {
                    break;
                }

                width++;
            }

            return width;
        }

        private bool HasRowBoundary(int topRow)
        {
            for (int x = 0; x < _gridWidth; x++)
            {
                SlotDefinition first = GetSlotAtCell(new Vector2Int(x, topRow));
                SlotDefinition second = GetSlotAtCell(new Vector2Int(x, topRow + 1));

                if (first != second && (first != null || second != null))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
