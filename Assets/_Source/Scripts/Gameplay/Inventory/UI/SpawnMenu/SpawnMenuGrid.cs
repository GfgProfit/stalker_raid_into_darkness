using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class SpawnMenuGrid : MonoBehaviour
    {
        private const int CELLS_PER_FRAME = 16;
        private const int OVERSCAN_ROWS = 1;

        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private InventoryGridView _grid;
        [SerializeField] private SpawnMenuItemCell _cellTemplate;

        private readonly List<RectInt> _areas = new();
        private readonly Dictionary<int, SpawnMenuItemCell> _visible = new();
        private readonly List<int> _stale = new();
        private readonly Stack<SpawnMenuItemCell> _pool = new();
        private SpawnMenuView _owner;
        private IReadOnlyList<SpawnMenuEntry> _entries;
        private SpawnMenuEntry _selected;
        private InventoryWindow _inventoryWindow;
        private Vector2 _preferredCell;
        private Vector2 _viewportSize;
        private int _widest = 1;
        private int _maxHeight;
        private int _firstRow = -1;
        private int _lastRow = -1;
        private bool _pending;

        internal void Show(SpawnMenuView owner, IReadOnlyList<SpawnMenuEntry> entries, InventoryWindow inventoryWindow, Vector2 cellSize, int widest)
        {
            _owner = owner;
            _entries = entries;
            _inventoryWindow = inventoryWindow;
            _preferredCell = cellSize;
            _widest = widest;
            Rebuild(true);
        }

        internal void SetSelected(SpawnMenuEntry selected)
        {
            _selected = selected;
            foreach (SpawnMenuItemCell cell in _visible.Values)
            {
                cell.SetSelected(selected);
            }
        }

        private void LateUpdate()
        {
            if (_entries == null)
            {
                return;
            }
            if (_viewportSize != _scroll.viewport.rect.size)
            {
                Rebuild(false);
            }

            float cellHeight = _grid.CellSize.y;
            float top = Mathf.Max(0f, _grid.RectTransform.anchoredPosition.y);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(top / cellHeight) - OVERSCAN_ROWS);
            int lastRow = Mathf.CeilToInt((top + _viewportSize.y) / cellHeight) + OVERSCAN_ROWS;
            if (_pending || firstRow != _firstRow || lastRow != _lastRow)
            {
                _firstRow = firstRow;
                _lastRow = lastRow;
                RefreshVisible(firstRow, lastRow);
            }
        }

        private void OnDisable() => ReleaseVisible();

        private void Rebuild(bool resetScroll)
        {
            ReleaseVisible();
            _areas.Clear();
            _maxHeight = 1;
            _viewportSize = _scroll.viewport.rect.size;
            float scale = Mathf.Clamp(_viewportSize.x / (_widest * _preferredCell.x), 0.05f, 1f);
            Vector2 cell = Vector2.Max(Vector2.one * 4f, _preferredCell * scale);
            int columns = Mathf.Max(_widest, Mathf.FloorToInt(_viewportSize.x / cell.x + 0.001f));
            int left = 0;
            int top = 0;
            int shelfHeight = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                SpawnMenuEntry entry = _entries[i];
                Vector2Int size = entry.Definition.Size;
                if (left + size.x > columns || i > 0 && entry.Group != _entries[i - 1].Group)
                {
                    left = 0;
                    top += shelfHeight;
                    shelfHeight = 0;
                }
                _areas.Add(new RectInt(left, top, size.x, size.y));
                left += size.x;
                shelfHeight = Mathf.Max(shelfHeight, size.y);
                _maxHeight = Mathf.Max(_maxHeight, size.y);
            }

            int viewportRows = Mathf.FloorToInt(_viewportSize.y / cell.y);
            _grid.Configure(_grid.ContainerId, _grid.OwnerId, _grid.DisplayName, cell);
            _grid.SetGridSize(columns, Mathf.Max(top + shelfHeight, viewportRows));
            Vector2 position = _grid.RectTransform.anchoredPosition;
            if (resetScroll)
            {
                _scroll.StopMovement();
                position.y = 0f;
            }
            position.y = Mathf.Clamp(position.y, 0f, Mathf.Max(0f, _grid.ContentSize.y - _viewportSize.y));
            _grid.RectTransform.anchoredPosition = position;
            _pending = true;
        }

        private void RefreshVisible(int firstRow, int lastRow)
        {
            foreach (var pair in _visible)
            {
                RectInt area = _areas[pair.Key];
                if (area.yMax <= firstRow || area.y >= lastRow)
                {
                    _stale.Add(pair.Key);
                }
            }
            foreach (int index in _stale)
            {
                Release(_visible[index]);
                _visible.Remove(index);
            }
            _stale.Clear();

            int budget = CELLS_PER_FRAME;
            _pending = false;
            for (int index = FindFirstPossible(firstRow); index < _areas.Count && _areas[index].y < lastRow; index++)
            {
                if (_areas[index].yMax <= firstRow || _visible.ContainsKey(index))
                {
                    continue;
                }
                if (budget == 0)
                {
                    _pending = true;
                    break;
                }

                SpawnMenuItemCell cell = _pool.Count > 0 ? _pool.Pop() : Instantiate(_cellTemplate, _grid.ItemsRoot);
                SpawnMenuEntry entry = _entries[index];
                cell.gameObject.SetActive(true);
                cell.Bind(_owner, entry, _grid.GetAreaRect(_areas[index]), _inventoryWindow, entry == _selected);
                _visible.Add(index, cell);
                budget--;
            }
        }

        private int FindFirstPossible(int firstRow)
        {
            int low = 0;
            int high = _areas.Count;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (_areas[mid].y + _maxHeight <= firstRow)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }
            return low;
        }

        private void ReleaseVisible()
        {
            foreach (SpawnMenuItemCell cell in _visible.Values)
            {
                Release(cell);
            }
            _visible.Clear();
            _firstRow = -1;
            _lastRow = -1;
        }

        private void Release(SpawnMenuItemCell cell)
        {
            cell.Unbind();
            _pool.Push(cell);
        }
    }
}
