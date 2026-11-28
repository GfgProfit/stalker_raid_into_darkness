using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    [AddComponentMenu("Inventory/Inventory Slot Grid View")]
    public sealed class InventorySlotGridView : InventoryGridViewBase
    {
        private const string LOCK_PREFIX = "Slot Lock ";
        private const string HIGHLIGHT_PREFIX = "Slot Highlight ";

        [SerializeField] private InventorySlotLayout _layout;
        [SerializeField] [TextArea(2, 12)] private string _inlineMask = "AAB\nAAC";
        [SerializeField] private List<SlotLayoutBinding> _inlineBindings = new()
        {
            new SlotLayoutBinding('A', SlotId.Head, "A", SlotMode.Grid, null),
            new SlotLayoutBinding('B', SlotId.Outfit, "B", SlotMode.Grid, null),
            new SlotLayoutBinding('C', SlotId.Backpack, "C", SlotMode.Grid, null)
        };
        [SerializeField] [Min(0f)] private float _slotSpacing = 10f;
        [SerializeField] private bool _centerRows = true;
        [SerializeField] private bool _isEquipment = true;
        [SerializeField] private bool _showSlotLabels = true;
        [SerializeField] private Color _labelColor = new(1f, 1f, 1f, 0.35f);
        [SerializeField] [Min(6f)] private float _labelFontSize = 14f;
        [SerializeField] private TMP_FontAsset _labelFont;
        [SerializeField] private Vector2 _labelPadding = new(4f, 2f);
        [SerializeField] private List<InventorySlotVisual> _slotVisuals = new();

        private readonly List<SlotDefinition> _slots = new();
        private readonly Dictionary<SlotId, Rect> _slotRects = new();
        private readonly Dictionary<SlotId, InventorySlotVisual> _visualsById = new();
        private readonly Dictionary<SlotId, Image> _lockImages = new();
        private readonly Dictionary<SlotId, Image> _highlightImages = new();
        private readonly HashSet<SlotId> _decorationSlots = new();
        private readonly List<SlotId> _staleDecorations = new();
        private readonly List<TMP_Text> _labels = new();
        private RectTransform _decorationRoot;
        private SlotDefinition[,] _slotByCell = new SlotDefinition[0, 0];
        private int _gridWidth;
        private int _gridHeight;
        private Vector2 _contentSize;
        private string _layoutWarning = string.Empty;

        public InventorySlotLayout Layout => _layout;
        public IReadOnlyList<SlotDefinition> Slots => _slots;
        public IReadOnlyList<InventorySlotVisual> SlotVisuals => _slotVisuals;
        public bool IsEquipment => _isEquipment;
        public override int Columns => _gridWidth;
        public override int Rows => _gridHeight;
        public override Vector2 ContentSize => _contentSize;
        public override string LayoutWarning => _layoutWarning;
        public override bool UsesItemFilter => _isEquipment == false;

        public void SetLayout(InventorySlotLayout layout, float slotSpacing, bool centerRows, bool isEquipment)
        {
            _layout = layout;
            _slotSpacing = slotSpacing;
            _centerRows = centerRows;
            _isEquipment = isEquipment;
            Rebuild();
        }

        public void SetInlineLayout(string mask, IEnumerable<SlotLayoutBinding> bindings)
        {
            _layout = null;
            _inlineMask = mask;
            _inlineBindings = new List<SlotLayoutBinding>(bindings);
            Rebuild();
        }

        public void SetSlotVisuals(IEnumerable<InventorySlotVisual> visuals)
        {
            _slotVisuals = visuals == null ? new List<InventorySlotVisual>() : new List<InventorySlotVisual>(visuals);
            Rebuild();
        }

        public void SetLabelStyle(bool showLabels, TMP_FontAsset font, float fontSize, Color color, Vector2 padding)
        {
            _showSlotLabels = showLabels;
            _labelFont = font;
            _labelFontSize = fontSize;
            _labelColor = color;
            _labelPadding = padding;
            Rebuild();
        }

        public Vector2 GetSlotPosition(SlotId slotId) => _slotRects.TryGetValue(slotId, out Rect rect) ? rect.position : Vector2.zero;

        public Rect GetSlotRect(SlotId slotId) => _slotRects.TryGetValue(slotId, out Rect rect) ? rect : default;

        public override InventoryContainer CreateContainer()
        {
            ComputeLayout();
            return new SlottedContainer(ContainerId, _slots, Mathf.Max(1, _gridWidth), Mathf.Max(1, _gridHeight), _isEquipment, OwnerId, DisplayName);
        }

        public override bool TryGetCellAtLocal(Vector2 topLeftLocal, out Vector2Int cell)
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

        public override Vector2Int GetPlacementOrigin(Vector2 topLeftPointer, Vector2Int itemSize)
        {
            SlotDefinition hover = GetSlotAtVisualPosition(topLeftPointer);

            if (hover != null && hover.Mode == SlotMode.Single)
            {
                return hover.Bounds.position;
            }

            Vector2 cell = hover != null ? SlotCellSize(hover) : CellSize;
            Vector2 shifted = topLeftPointer - new Vector2((itemSize.x - 1) * cell.x * 0.5f, (itemSize.y - 1) * cell.y * 0.5f);
            SlotDefinition slot = GetSlotAtVisualPosition(shifted) ?? hover;
            return slot == null ? new Vector2Int(-1, -1) : CellInSlot(slot, shifted);
        }

        public override Rect GetAreaRect(RectInt area)
        {
            SlotDefinition slot = GetSlotAtCell(area.position);

            if (slot == null || _slotRects.TryGetValue(slot.Id, out Rect slotRect) == false)
            {
                Vector2 size = CellSize;
                return new Rect(area.x * size.x, area.y * size.y, area.width * size.x, area.height * size.y);
            }

            if (slot.Mode == SlotMode.Single)
            {
                return slotRect;
            }

            Vector2 cell = SlotCellSize(slot);
            return new Rect(slotRect.x + (area.x - slot.X) * cell.x, slotRect.y + (area.y - slot.Y) * cell.y, area.width * cell.x, area.height * cell.y);
        }

        public override bool UsesSlotIcon(ItemInstance item, out Vector2Int slotSize)
        {
            slotSize = Vector2Int.zero;

            if (item == null || Container is not SlottedContainer slotted)
            {
                return false;
            }

            SlotDefinition slot = slotted.GetSlot(item.Location.SlotId);

            if (slot == null || slot.Mode != SlotMode.Single)
            {
                return false;
            }

            slotSize = slot.Bounds.size;
            return true;
        }

        protected override void ComputeLayout()
        {
            _slots.Clear();
            _slotRects.Clear();
            _visualsById.Clear();
            _layoutWarning = string.Empty;
            bool parsed = _layout != null ? _layout.TryBuildSlots(out List<SlotDefinition> slots, out _gridWidth, out _gridHeight, out string error) : SlotMaskParser.TryParse(_inlineMask, _inlineBindings, out slots, out _gridWidth, out _gridHeight, out error);

            if (parsed == false)
            {
                _layoutWarning = error;
                _gridWidth = 1;
                _gridHeight = 1;
                _slotByCell = new SlotDefinition[1, 1];
                _contentSize = CellSize;
                return;
            }

            _slots.AddRange(slots);
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

            if (_slotVisuals != null)
            {
                foreach (InventorySlotVisual visual in _slotVisuals)
                {
                    if (visual == null)
                    {
                        continue;
                    }

                    if (visual.SlotId == SlotId.None || Enum.IsDefined(typeof(SlotId), visual.SlotId) == false)
                    {
                        _layoutWarning += "Invalid slot decoration identifier. ";
                    }
                    else if (_visualsById.TryAdd(visual.SlotId, visual) == false)
                    {
                        _layoutWarning += $"Duplicate decoration for slot '{visual.SlotId}'. ";
                    }
                }
            }

            ComputeGeometry();
        }

        protected override void ApplySize() => ApplyContentSize(InventoryGridSizeMode.AutoSize);

        protected override void DrawGrid(InventoryGridGraphic graphic)
        {
            foreach (SlotDefinition slot in _slots)
            {
                Rect region = _slotRects[slot.Id];

                if (slot.Mode == SlotMode.Single)
                {
                    DrawCellRegion(graphic, region, 1, 1, region.size);
                }
                else
                {
                    DrawCellRegion(graphic, region, slot.Width, slot.Height, SlotCellSize(slot));
                }
            }
        }

        protected override void DrawSlotStateOverlay(InventoryGridGraphic graphic)
        {
            SlottedContainer slotted = Container as SlottedContainer;

            foreach (SlotDefinition slot in _slots)
            {
                bool locked = slotted != null ? slotted.IsSlotAvailable(slot.Id) == false && slotted.IsSlotOccupied(slot.Id) == false : slot.Rule.StartsLocked;

                if (_lockImages.TryGetValue(slot.Id, out Image lockImage) && lockImage != null)
                {
                    lockImage.enabled = locked;
                    continue;
                }

                if (locked && slotted != null)
                {
                    graphic.AddQuad(_slotRects[slot.Id], Style.LockedSlotColor);
                }
            }
        }

        protected override bool DrawCompatibleAreas(IReadOnlyList<RectInt> areas)
        {
            if (_highlightImages.Count == 0)
            {
                return false;
            }

            foreach (Image image in _highlightImages.Values)
            {
                if (image != null)
                {
                    image.enabled = false;
                }
            }

            bool drewAll = true;

            for (int i = 0; i < areas.Count; i++)
            {
                SlotDefinition slot = GetSlotAtCell(areas[i].position);

                if (slot != null && _highlightImages.TryGetValue(slot.Id, out Image image) && image != null)
                {
                    image.enabled = true;
                }
                else
                {
                    drewAll = false;
                }
            }

            return drewAll;
        }

        protected override void DrawLabels(RectTransform labelsRoot)
        {
            SyncDecorations(labelsRoot);
            int needed = _showSlotLabels ? _slots.Count : 0;

            while (_labels.Count < needed)
            {
                var labelObject = new GameObject("Slot Label", typeof(RectTransform));
                labelObject.hideFlags = labelsRoot.gameObject.hideFlags;
                labelObject.transform.SetParent(labelsRoot, false);
                TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
                label.raycastTarget = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                _labels.Add(label);
            }

            for (int i = labelsRoot.childCount - 1; i >= 0; i--)
            {
                TMP_Text existing = labelsRoot.GetChild(i).GetComponent<TMP_Text>();

                if (existing != null && _labels.Contains(existing) == false)
                {
                    DestroyGenerated(existing.gameObject);
                }
            }

            for (int i = 0; i < _labels.Count; i++)
            {
                TMP_Text label = _labels[i];
                SlotDefinition slot = i < needed ? _slots[i] : null;
                InventorySlotVisual visual = slot != null && _visualsById.TryGetValue(slot.Id, out InventorySlotVisual found) ? found : null;
                InventorySlotLabelAnchor anchor = visual != null ? visual.LabelAnchor : InventorySlotLabelAnchor.Center;
                string text = visual != null && visual.Label != null ? visual.Label : slot?.DisplayName;
                bool visible = slot != null && anchor != InventorySlotLabelAnchor.None && string.IsNullOrEmpty(text) == false;
                label.gameObject.SetActive(visible);

                if (visible == false)
                {
                    continue;
                }

                Rect rect = _slotRects[slot.Id];
                RectTransform labelRect = label.rectTransform;
                labelRect.SetAsLastSibling();
                labelRect.anchorMin = new Vector2(0f, 1f);
                labelRect.anchorMax = new Vector2(0f, 1f);
                labelRect.pivot = new Vector2(0f, 1f);
                bool padded = anchor != InventorySlotLabelAnchor.Center;
                labelRect.anchoredPosition = new Vector2(rect.x + (padded ? _labelPadding.x : 0f), -rect.y - (padded ? _labelPadding.y : 0f));
                labelRect.sizeDelta = new Vector2(rect.width - (padded ? _labelPadding.x * 2f : 0f), rect.height - (padded ? _labelPadding.y * 2f : 0f));
                label.alignment = anchor switch
                {
                    InventorySlotLabelAnchor.TopLeft => TextAlignmentOptions.TopLeft,
                    InventorySlotLabelAnchor.TopRight => TextAlignmentOptions.TopRight,
                    _ => TextAlignmentOptions.Center
                };
                label.text = text;
                label.color = _labelColor;
                label.fontSize = _labelFontSize;

                if (_labelFont != null)
                {
                    label.font = _labelFont;
                }
            }
        }

        private void SyncDecorations(RectTransform labelsRoot)
        {
            if (_decorationRoot != labelsRoot)
            {
                _decorationRoot = labelsRoot;
                _lockImages.Clear();
                _highlightImages.Clear();

                for (int i = labelsRoot.childCount - 1; i >= 0; i--)
                {
                    Transform child = labelsRoot.GetChild(i);
                    if (child.TryGetComponent(out Image _))
                    {
                        DestroyGenerated(child.gameObject);
                    }
                }
            }

            SyncImages(labelsRoot, _lockImages, true);
            SyncImages(labelsRoot, _highlightImages, false);

            foreach (Image image in _highlightImages.Values)
            {
                if (image != null)
                {
                    image.enabled = false;
                }
            }
        }

        private Rect LockedRect(SlotDefinition slot, InventorySlotVisual visual)
        {
            Rect slotRect = _slotRects[slot.Id];
            Rect locked = visual.LockedRect;
            return locked.width > 0f && locked.height > 0f ? new Rect(slotRect.x + locked.x, slotRect.y + locked.y, locked.width, locked.height) : slotRect;
        }

        private void SyncImages(RectTransform root, Dictionary<SlotId, Image> images, bool locked)
        {
            _decorationSlots.Clear();

            foreach (SlotDefinition slot in _slots)
            {
                if (_visualsById.TryGetValue(slot.Id, out InventorySlotVisual visual) == false)
                {
                    continue;
                }

                Sprite sprite = locked ? visual.LockedSprite : visual.HighlightSprite;
                if (sprite == null)
                {
                    continue;
                }

                _decorationSlots.Add(slot.Id);
                if (images.TryGetValue(slot.Id, out Image image) == false || image == null)
                {
                    string prefix = locked ? LOCK_PREFIX : HIGHLIGHT_PREFIX;
                    var imageObject = new GameObject(prefix + slot.Id, typeof(RectTransform));
                    imageObject.hideFlags = root.gameObject.hideFlags;
                    imageObject.layer = root.gameObject.layer;
                    imageObject.transform.SetParent(root, false);
                    image = imageObject.AddComponent<Image>();
                    image.raycastTarget = false;
                    image.type = Image.Type.Simple;
                    images[slot.Id] = image;
                }

                Rect rect = locked ? LockedRect(slot, visual) : _slotRects[slot.Id];
                RectTransform imageRect = image.rectTransform;
                imageRect.anchorMin = new Vector2(0f, 1f);
                imageRect.anchorMax = new Vector2(0f, 1f);
                imageRect.pivot = new Vector2(0f, 1f);
                imageRect.anchoredPosition = new Vector2(rect.x, -rect.y);
                imageRect.sizeDelta = rect.size;
                image.sprite = sprite;
            }

            _staleDecorations.Clear();

            foreach (KeyValuePair<SlotId, Image> pair in images)
            {
                if (_decorationSlots.Contains(pair.Key) == false)
                {
                    DestroyGenerated(pair.Value != null ? pair.Value.gameObject : null);
                    _staleDecorations.Add(pair.Key);
                }
            }

            foreach (SlotId slotId in _staleDecorations)
            {
                images.Remove(slotId);
            }
        }

        private Vector2 SlotCellSize(SlotDefinition slot)
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

        private SlotDefinition GetSlotAtCell(Vector2Int cell)
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

        private void ComputeGeometry()
        {
            Vector2 cell = CellSize;
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

                if (_visualsById.TryGetValue(slot.Id, out InventorySlotVisual visual) && visual.OverrideRect)
                {
                    rect = visual.Rect;
                }
                else
                {
                    float x = 0f;

                    for (int y = slot.Y; y < slot.Y + slot.Height; y++)
                    {
                        float offset = _centerRows ? (maxRowWidth - rowWidths[y]) * 0.5f : 0f;
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
