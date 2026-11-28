using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    [AddComponentMenu("Inventory/Inventory Grid View")]
    public sealed class InventoryGridView : InventoryGridViewBase
    {
        private const float SIZE_EPSILON = 0.01f;

        [SerializeField] private InventoryGridSizeMode _sizeMode = InventoryGridSizeMode.AutoSize;
        [SerializeField] [Min(1)] private int _columns = 10;
        [SerializeField] [Min(1)] private int _rows = 8;

        private int _resolvedColumns = 1;
        private int _resolvedRows = 1;
        private int _contentRows;
        private string _layoutWarning = string.Empty;

        public InventoryGridSizeMode SizeMode => _sizeMode;
        public override int Columns => _resolvedColumns;
        public override int Rows => _resolvedRows;
        public override Vector2 ContentSize => new(_resolvedColumns * CellSize.x, _resolvedRows * CellSize.y);
        public override string LayoutWarning => _layoutWarning;

        public void SetGridSize(int columns, int rows)
        {
            _columns = Mathf.Max(1, columns);
            _rows = Mathf.Max(1, rows);
            Rebuild();
        }

        public void SetSizeMode(InventoryGridSizeMode sizeMode)
        {
            _sizeMode = sizeMode;
            Rebuild();
        }

        internal Vector2Int GetConfiguredSize()
        {
            ComputeLayout();
            int rows = _sizeMode == InventoryGridSizeMode.AutoSize ? Mathf.Max(1, _rows) : _resolvedRows;
            return new Vector2Int(_resolvedColumns, rows);
        }

        public void SetContentRows(int rows)
        {
            if (_contentRows == rows)
            {
                return;
            }

            _contentRows = rows;
            Rebuild();
        }

        public override bool TryGetCellAtLocal(Vector2 topLeftLocal, out Vector2Int cell)
        {
            Vector2 size = CellSize;
            cell = new Vector2Int(Mathf.FloorToInt(topLeftLocal.x / size.x), Mathf.FloorToInt(topLeftLocal.y / size.y));
            return cell.x >= 0 && cell.y >= 0 && cell.x < _resolvedColumns && cell.y < _resolvedRows;
        }

        public override Rect GetAreaRect(RectInt area)
        {
            Vector2 size = CellSize;
            return new Rect(area.x * size.x, area.y * size.y, area.width * size.x, area.height * size.y);
        }

        protected override void ComputeLayout()
        {
            _layoutWarning = string.Empty;

            if (_sizeMode == InventoryGridSizeMode.AutoSize)
            {
                _resolvedColumns = Mathf.Max(1, _columns);
                _resolvedRows = Mathf.Max(1, _contentRows > 0 ? _contentRows : _rows);
                return;
            }

            Vector2 rectSize = RectTransform.rect.size;
            Vector2 cell = CellSize;
            float columns = rectSize.x / cell.x;
            float rows = rectSize.y / cell.y;
            _resolvedColumns = Mathf.Max(1, Mathf.FloorToInt(columns + SIZE_EPSILON));
            _resolvedRows = Mathf.Max(1, Mathf.FloorToInt(rows + SIZE_EPSILON));

            if (Mathf.Abs(columns - Mathf.Round(columns)) > SIZE_EPSILON || Mathf.Abs(rows - Mathf.Round(rows)) > SIZE_EPSILON)
            {
                _layoutWarning = $"RectTransform size {rectSize.x:0.##}x{rectSize.y:0.##} is not a multiple of the cell size {cell.x:0.##}x{cell.y:0.##}; the grid uses {_resolvedColumns}x{_resolvedRows} cells and leaves unused space.";
            }

            _columns = _resolvedColumns;
            _rows = _resolvedRows;
        }

        protected override void ApplySize() => ApplyContentSize(_sizeMode);

        protected override void DrawGrid(InventoryGridGraphic graphic)
        {
            DrawCellRegion(graphic, new Rect(Vector2.zero, ContentSize), _resolvedColumns, _resolvedRows);
        }
    }
}
