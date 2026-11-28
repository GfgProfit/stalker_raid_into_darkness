using RaidIntoDarkness.Inventory.Domain;
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
        private readonly InventorySlotGridGeometry _geometry = new();
        private readonly Dictionary<SlotId, InventorySlotVisual> _visualsById = new();
        private readonly Dictionary<SlotId, Image> _lockImages = new();
        private readonly Dictionary<SlotId, Image> _highlightImages = new();
        private readonly HashSet<SlotId> _decorationSlots = new();
        private readonly List<SlotId> _staleDecorations = new();
        private readonly List<TMP_Text> _labels = new();
        private RectTransform _decorationRoot;
        private string _layoutWarning = string.Empty;

        public InventorySlotLayout Layout => _layout;
        public IReadOnlyList<SlotDefinition> Slots => _slots;
        public IReadOnlyList<InventorySlotVisual> SlotVisuals => _slotVisuals;
        public bool IsEquipment => _isEquipment;
        public override int Columns => _geometry.Columns;
        public override int Rows => _geometry.Rows;
        public override Vector2 ContentSize => _geometry.ContentSize;
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

        public Vector2 GetSlotPosition(SlotId slotId) => _geometry.GetSlotRect(slotId).position;

        public Rect GetSlotRect(SlotId slotId) => _geometry.GetSlotRect(slotId);

        public override bool TryGetCellAtLocal(Vector2 topLeftLocal, out Vector2Int cell) => _geometry.TryGetCellAtLocal(topLeftLocal, out cell);
        public override Vector2Int GetPlacementOrigin(Vector2 topLeftPointer, Vector2Int itemSize) => _geometry.GetPlacementOrigin(topLeftPointer, itemSize);
        public override Rect GetAreaRect(RectInt area) => _geometry.GetAreaRect(area);

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
            _visualsById.Clear();
            _layoutWarning = string.Empty;
            bool parsed = _layout != null ? _layout.TryBuildSlots(out List<SlotDefinition> slots, out int width, out int height, out string error) : SlotMaskParser.TryParse(_inlineMask, _inlineBindings, out slots, out width, out height, out error);

            if (parsed == false)
            {
                _layoutWarning = error;
                _geometry.Reset(CellSize);
                return;
            }

            _slots.AddRange(slots);

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

            _geometry.Build(_slots, _visualsById, width, height, CellSize, _slotSpacing, _centerRows);
        }

        protected override void ApplySize() => ApplyContentSize(InventoryGridSizeMode.AutoSize);

        protected override void DrawGrid(InventoryGridGraphic graphic)
        {
            foreach (SlotDefinition slot in _slots)
            {
                Rect region = _geometry.GetSlotRect(slot.Id);

                if (slot.Mode == SlotMode.Single)
                {
                    DrawCellRegion(graphic, region, 1, 1, region.size);
                }
                else
                {
                    DrawCellRegion(graphic, region, slot.Width, slot.Height, _geometry.SlotCellSize(slot));
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
                    graphic.AddQuad(_geometry.GetSlotRect(slot.Id), Style.LockedSlotColor);
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
                SlotDefinition slot = _geometry.GetSlotAtCell(areas[i].position);

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

                Rect rect = _geometry.GetSlotRect(slot.Id);
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
            Rect slotRect = _geometry.GetSlotRect(slot.Id);
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

                Rect rect = locked ? LockedRect(slot, visual) : _geometry.GetSlotRect(slot.Id);
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
    }
}
