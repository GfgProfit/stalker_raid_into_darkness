using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public abstract class InventoryGridViewBase : MonoBehaviour
    {
        private const string GRID_NAME = "__InventoryGrid";
        private const string HIGHLIGHT_NAME = "__InventoryHighlight";
        private const string ITEMS_NAME = "__InventoryItems";
        private const string LABELS_NAME = "__InventoryLabels";
        private const HideFlags GENERATED_FLAGS = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable;

        [SerializeField] private string _containerId = "Main";
        [SerializeField] private string _ownerId = "Player";
        [SerializeField] private string _displayName = "Inventory";
        [SerializeField] [Min(4f)] private float _cellWidth = 64f;
        [SerializeField] [Min(4f)] private float _cellHeight = 64f;
        [SerializeField] private bool _applyBackgroundToImage = true;
        [SerializeField] private InventoryGridStyle _style = new();

        private readonly Dictionary<string, InventoryItemView> _itemViews = new(StringComparer.Ordinal);
        private readonly List<string> _staleViews = new();
        private RectTransform _rectTransform;
        private InventoryGridGraphic _gridGraphic;
        private InventoryGridGraphic _highlightGraphic;
        private RectTransform _itemsRoot;
        private RectTransform _labelsRoot;
        private InventoryWindow _window;
        private InventoryContainer _container;
        private IDisposable _containerBinding;
        private bool _dirty = true;
        private Vector2 _lastRectSize;
        private RectInt? _placementArea;
        private Color _placementColor;
        private readonly List<RectInt> _compatibleAreas = new();

        public string ContainerId => _containerId;
        public string OwnerId => _ownerId;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? _containerId : _displayName;
        public Vector2 CellSize => new(Mathf.Max(4f, _cellWidth), Mathf.Max(4f, _cellHeight));
        public InventoryGridStyle Style => _style;
        public RectTransform RectTransform => _rectTransform != null ? _rectTransform : _rectTransform = (RectTransform)transform;
        public RectTransform ItemsRoot => _itemsRoot;
        public RectTransform LabelsRoot => _labelsRoot;
        public InventoryGridGraphic GridGraphic => _gridGraphic;
        public InventoryGridGraphic HighlightGraphic => _highlightGraphic;
        public InventoryWindow Window => _window;
        public InventoryContainer Container => _container;
        public IReadOnlyDictionary<string, InventoryItemView> ItemViews => _itemViews;
        public abstract int Columns { get; }
        public abstract int Rows { get; }
        public abstract Vector2 ContentSize { get; }
        public abstract string LayoutWarning { get; }
        public virtual bool UsesItemFilter => true;

        public event Action<InventoryGridViewBase> LayoutRebuilt;

        public void Configure(string containerId, string ownerId, string displayName, Vector2 cellSize)
        {
            _containerId = containerId;
            _ownerId = ownerId;
            _displayName = displayName;
            _cellWidth = cellSize.x;
            _cellHeight = cellSize.y;
            SetDirty();
        }

        public void SetDirty() => _dirty = true;

        public void Rebuild()
        {
            _dirty = false;
            EnsureGenerated();
            ComputeLayout();
            ApplySize();
            _lastRectSize = RectTransform.rect.size;
            ApplyBackground();
            _gridGraphic.ClearQuads();
            DrawGrid(_gridGraphic);
            _gridGraphic.Commit();
            DrawLabels(_labelsRoot);
            RedrawHighlights();
            RelayoutItems();
            LayoutRebuilt?.Invoke(this);
        }

        public abstract InventoryContainer CreateContainer();
        public abstract bool TryGetCellAtLocal(Vector2 topLeftLocal, out Vector2Int cell);
        public abstract Rect GetAreaRect(RectInt area);

        public Vector2 ScreenToTopLeftLocal(Vector2 screenPosition, Camera eventCamera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(RectTransform, screenPosition, eventCamera, out Vector2 localPoint);
            Rect rect = RectTransform.rect;
            return new Vector2(localPoint.x - rect.xMin, rect.yMax - localPoint.y);
        }

        public bool TryGetCellAtScreen(Vector2 screenPosition, Camera eventCamera, out Vector2Int cell) => TryGetCellAtLocal(ScreenToTopLeftLocal(screenPosition, eventCamera), out cell);

        public Vector2 TopLeftToLocal(Vector2 topLeft)
        {
            Rect rect = RectTransform.rect;
            return new Vector2(rect.xMin + topLeft.x, rect.yMax - topLeft.y);
        }

        public virtual Vector2Int GetPlacementOrigin(Vector2 topLeftPointer, Vector2Int itemSize)
        {
            Vector2 cell = CellSize;
            Vector2 shifted = topLeftPointer - new Vector2((itemSize.x - 1) * cell.x * 0.5f, (itemSize.y - 1) * cell.y * 0.5f);
            return TryGetCellAtLocal(shifted, out Vector2Int origin) ? origin : new Vector2Int(Mathf.FloorToInt(shifted.x / cell.x), Mathf.FloorToInt(shifted.y / cell.y));
        }

        public virtual bool UsesSlotIcon(ItemInstance item, out Vector2Int slotSize)
        {
            slotSize = Vector2Int.zero;
            return false;
        }

        public virtual Rect GetIconRect(ItemInstance item, Rect area) => area;

        public void Bind(InventoryWindow window, InventoryContainer container)
        {
            Unbind();
            _window = window;
            _container = container;

            Rebuild();
            BindItems();
        }

        public void Unbind()
        {
            UnbindItems();

            foreach (InventoryItemView view in _itemViews.Values)
            {
                if (view != null)
                {
                    view.SetIconTarget(null);
                    DestroyGenerated(view.gameObject);
                }
            }

            _itemViews.Clear();
            _container = null;
            _window = null;
        }

        public InventoryItemView GetItemView(ItemInstance item) => item != null && _itemViews.TryGetValue(item.InstanceId, out InventoryItemView view) ? view : null;

        public void SetPlacementHighlight(RectInt area, Color areaColor)
        {
            _placementArea = area;
            _placementColor = areaColor;
            RedrawHighlights();
        }

        public void SetCompatibleAreas(IEnumerable<RectInt> areas)
        {
            _compatibleAreas.Clear();

            if (areas != null)
            {
                _compatibleAreas.AddRange(areas);
            }

            RedrawHighlights();
        }

        public void ClearHighlights()
        {
            _placementArea = null;
            _compatibleAreas.Clear();
            RedrawHighlights();
        }

        private void SyncItems()
        {
            if (_container == null || _itemsRoot == null)
            {
                return;
            }

            _staleViews.Clear();

            foreach (KeyValuePair<string, InventoryItemView> pair in _itemViews)
            {
                if (pair.Value == null || pair.Value.Item == null || _container.Contains(pair.Value.Item) == false)
                {
                    _staleViews.Add(pair.Key);
                }
            }

            foreach (string instanceId in _staleViews)
            {
                RemoveItemView(instanceId);
            }

            foreach (ItemInstance item in _container.Items)
            {
                SyncItemView(item);
            }

            OnItemsSynced();
        }

        private void SyncItemView(ItemInstance item)
        {
            if (_itemViews.TryGetValue(item.InstanceId, out InventoryItemView view) == false || view == null)
            {
                view = InventoryItemView.Create(_itemsRoot, GENERATED_FLAGS);
                _itemViews[item.InstanceId] = view;
            }

            if (view.Item == item)
            {
                view.Refresh();
            }
            else
            {
                view.Bind(item, this);
            }

            OnItemViewLayout(view);
            Func<ItemInstance, bool> filter = _window != null && UsesItemFilter ? _window.ItemFilter : null;
            view.SetDimmed(filter != null && filter(item) == false);
        }

        private void RemoveItemView(string instanceId)
        {
            if (_itemViews.Remove(instanceId, out InventoryItemView view) && view != null)
            {
                view.SetIconTarget(null);
                view.gameObject.SetActive(false);
                DestroyGenerated(view.gameObject);
            }
        }

        public void ApplyFilter()
        {
            Func<ItemInstance, bool> filter = _window != null && UsesItemFilter ? _window.ItemFilter : null;

            foreach (InventoryItemView view in _itemViews.Values)
            {
                if (view != null)
                {
                    view.SetDimmed(filter != null && view.Item != null && filter(view.Item) == false);
                }
            }
        }

        protected abstract void ComputeLayout();
        protected abstract void DrawGrid(InventoryGridGraphic graphic);
        protected virtual void DrawLabels(RectTransform labelsRoot)
        {
        }

        protected virtual void DrawSlotStateOverlay(InventoryGridGraphic graphic)
        {
        }

        protected virtual bool DrawCompatibleAreas(IReadOnlyList<RectInt> areas) => false;

        protected virtual void OnItemViewLayout(InventoryItemView view)
        {
        }

        protected virtual void OnItemsSynced()
        {
        }

        protected void DrawCellRegion(InventoryGridGraphic graphic, Rect region, int columns, int rows) => DrawCellRegion(graphic, region, columns, rows, CellSize);

        protected void DrawCellRegion(InventoryGridGraphic graphic, Rect region, int columns, int rows, Vector2 cell)
        {
            graphic.AddQuad(region, _style.CellColor);

            if (_style.ShowGrid && _style.GridLineThickness > 0f)
            {
                float half = _style.GridLineThickness * 0.5f;

                for (int x = 1; x < columns; x++)
                {
                    graphic.AddQuad(new Rect(region.x + x * cell.x - half, region.y, _style.GridLineThickness, region.height), _style.GridLineColor);
                }

                for (int y = 1; y < rows; y++)
                {
                    graphic.AddQuad(new Rect(region.x, region.y + y * cell.y - half, region.width, _style.GridLineThickness), _style.GridLineColor);
                }
            }

            if (_style.ShowBorder && _style.OuterBorderThickness > 0f)
            {
                float thickness = _style.OuterBorderThickness;
                float half = thickness * 0.5f;
                graphic.AddQuad(new Rect(region.x - half, region.y - half, region.width + thickness, thickness), _style.OuterBorderColor);
                graphic.AddQuad(new Rect(region.x - half, region.yMax - half, region.width + thickness, thickness), _style.OuterBorderColor);
                graphic.AddQuad(new Rect(region.x - half, region.y + half, thickness, region.height - thickness), _style.OuterBorderColor);
                graphic.AddQuad(new Rect(region.xMax - half, region.y + half, thickness, region.height - thickness), _style.OuterBorderColor);
            }
        }

        protected abstract void ApplySize();

        protected virtual void OnEnable()
        {
            _rectTransform = (RectTransform)transform;
            Rebuild();
            BindItems();
        }

        protected virtual void OnDisable()
        {
            if (Application.isPlaying == false)
            {
                return;
            }

            UnbindItems();
            ClearHighlights();
        }

        protected virtual void OnDestroy()
        {
            UnbindItems();

            _itemViews.Clear();
            _container = null;
            _window = null;
        }

        protected virtual void OnValidate() => _dirty = true;

        protected virtual void OnRectTransformDimensionsChange() => _dirty = true;

        protected virtual void Update()
        {
            if (_dirty || RectTransform.rect.size != _lastRectSize)
            {
                Rebuild();
            }
        }

        private void OnModelChanged(InventoryChange change)
        {
            if (change.Kind == InventoryChangeKind.Cleared)
            {
                SyncItems();
                RedrawHighlights();
                return;
            }

            if (change.Kind == InventoryChangeKind.SlotStateChanged)
            {
                OnItemsSynced();
                RedrawHighlights();
                return;
            }

            if (change.Item == null)
            {
                return;
            }

            if (_container.Contains(change.Item))
            {
                SyncItemView(change.Item);
            }
            else
            {
                RemoveItemView(change.Item.InstanceId);
            }
        }

        private void BindItems()
        {
            if (_containerBinding != null || _container?.Model == null || Application.isPlaying && isActiveAndEnabled == false)
            {
                return;
            }

            InventoryModel model = _container.Model;
            string containerId = _container.Id;
            _containerBinding = Observable.FromEvent<InventoryChange>(handler => model.Changed += handler, handler => model.Changed -= handler)
                .Where(change => change.Kind == InventoryChangeKind.Cleared || change.Affects(containerId))
                .Subscribe(OnModelChanged);
            SyncItems();
        }

        private void UnbindItems()
        {
            _containerBinding?.Dispose();
            _containerBinding = null;
        }

        private void RelayoutItems()
        {
            if (_container == null)
            {
                return;
            }

            foreach (InventoryItemView view in _itemViews.Values)
            {
                if (view != null && view.Item != null && _container.Contains(view.Item))
                {
                    view.Layout(GetAreaRect(_container.GetItemArea(view.Item)));
                    OnItemViewLayout(view);
                }
            }
        }

        private void RedrawHighlights()
        {
            if (_highlightGraphic == null)
            {
                return;
            }

            _highlightGraphic.ClearQuads();
            DrawSlotStateOverlay(_highlightGraphic);

            if (DrawCompatibleAreas(_compatibleAreas) == false)
            {
                foreach (RectInt area in _compatibleAreas)
                {
                    _highlightGraphic.AddQuad(GetAreaRect(area), _style.CompatibleSlotColor);
                }
            }

            if (_placementArea.HasValue)
            {
                _highlightGraphic.AddQuad(GetAreaRect(_placementArea.Value), _placementColor);
            }

            _highlightGraphic.Commit();
        }

        private void ApplyBackground()
        {
            if (_applyBackgroundToImage && TryGetComponent(out Image image))
            {
                image.color = _style.GridBackground;
                image.raycastTarget = true;
            }
        }

        private void EnsureGenerated()
        {
            _gridGraphic = EnsureChild(InventoryGeneratedRole.Grid, GRID_NAME, 0).GetComponent<InventoryGridGraphic>();
            _highlightGraphic = EnsureChild(InventoryGeneratedRole.Highlight, HIGHLIGHT_NAME, 1).GetComponent<InventoryGridGraphic>();
            _itemsRoot = EnsureChild(InventoryGeneratedRole.Items, ITEMS_NAME, -1);
            _labelsRoot = EnsureChild(InventoryGeneratedRole.Labels, LABELS_NAME, -1);
            _itemsRoot.SetSiblingIndex(Mathf.Max(0, transform.childCount - 2));
            _labelsRoot.SetAsLastSibling();
        }

        private RectTransform EnsureChild(InventoryGeneratedRole role, string childName, int siblingIndex)
        {
            RectTransform found = null;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);

                if (child.TryGetComponent(out InventoryGeneratedElement marker) == false || marker.Role != role)
                {
                    continue;
                }

                if (found == null)
                {
                    found = (RectTransform)child;
                }
                else
                {
                    DestroyGenerated(child.gameObject);
                }
            }

            if (found == null)
            {
                var childObject = new GameObject(childName, typeof(RectTransform));
                childObject.hideFlags = GENERATED_FLAGS;
                childObject.layer = gameObject.layer;
                found = (RectTransform)childObject.transform;
                found.SetParent(transform, false);
                childObject.AddComponent<InventoryGeneratedElement>().Role = role;
            }

            if ((role == InventoryGeneratedRole.Grid || role == InventoryGeneratedRole.Highlight) && found.TryGetComponent(out InventoryGridGraphic _) == false)
            {
                InventoryGridGraphic graphic = found.gameObject.AddComponent<InventoryGridGraphic>();
                graphic.raycastTarget = false;
            }

            found.gameObject.hideFlags = GENERATED_FLAGS;
            found.anchorMin = Vector2.zero;
            found.anchorMax = Vector2.one;
            found.pivot = new Vector2(0.5f, 0.5f);
            found.offsetMin = Vector2.zero;
            found.offsetMax = Vector2.zero;
            found.localScale = Vector3.one;
            found.localRotation = Quaternion.identity;

            if (siblingIndex >= 0)
            {
                found.SetSiblingIndex(siblingIndex);
            }

            return found;
        }

        protected static void DestroyGenerated(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        protected void ApplyContentSize(InventoryGridSizeMode sizeMode)
        {
            if (sizeMode != InventoryGridSizeMode.AutoSize)
            {
                return;
            }

            RectTransform rect = RectTransform;
            Vector2 size = ContentSize;

            if (Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x) && Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y))
            {
                if (rect.sizeDelta != size)
                {
                    rect.sizeDelta = size;
                }
            }
            else
            {
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
            }
        }
    }
}
