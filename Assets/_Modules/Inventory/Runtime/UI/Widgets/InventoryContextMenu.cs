using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryContextMenu : MonoBehaviour
    {
        private const float ROW_HEIGHT = 29f;
        private const float PADDING = 14f;
        private const float LIST_SHIFT = -8f;
        private const float ICON_INSET = 3f;
        private const float ICON_GAP = 3f;
        private const float DIVIDER_HEIGHT = 12f;
        private const float DIVIDER_LINE_HEIGHT = 3f;
        private const float MIN_WIDTH = 82f;
        private const float MIN_HEIGHT = 56f;
        private const float FONT_SIZE = 21f;
        private const float FRAME_SCALE = 0.71f;
        private static readonly Color TEXT_COLOR = new(150f / 255f, 150f / 255f, 150f / 255f, 1f);
        private static readonly Color HOVER_TEXT_COLOR = new(220f / 255f, 220f / 255f, 220f / 255f, 1f);
        private static readonly Color DISABLED_TEXT_COLOR = new(80f / 255f, 80f / 255f, 80f / 255f, 1f);
        private static readonly Color DIVIDER_FALLBACK_COLOR = new(1f, 1f, 1f, 0.15f);
        private static readonly Color HIGHLIGHT_FALLBACK_COLOR = new(1f, 1f, 1f, 0.12f);

        private readonly List<Row> _rows = new();
        private readonly List<Entry> _ordered = new();
        private RectTransform _rectTransform;
        private RectTransform _panel;
        private Image _frame;
        private RectTransform _list;
        private Image _highlight;
        private Row _hovered;
        private Action _onHidden;
        private float _width;

        public InventoryTheme Theme { get; set; }
        public bool IsVisible => gameObject.activeSelf;
        public int EntryCount => _ordered.Count;

        public void Show(Vector2 screenPosition, Camera eventCamera, IReadOnlyList<Entry> entries, Action onHidden = null)
        {
            EnsureBuilt();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            ApplyTheme();
            _onHidden = onHidden;
            _ordered.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                _ordered.Add(entries[i]);
            }

            SortByGroup(_ordered);
            int rowCount = 0;

            for (int i = 0; i < _ordered.Count; i++)
            {
                if (i > 0 && _ordered[i].Group != _ordered[i - 1].Group)
                {
                    PrepareDivider(rowCount);
                    rowCount++;
                }

                PrepareEntry(rowCount, i);
                rowCount++;
            }

            for (int i = rowCount; i < _rows.Count; i++)
            {
                _rows[i].Root.gameObject.SetActive(false);
            }

            float textWidth = 0f;

            for (int i = 0; i < rowCount; i++)
            {
                if (_rows[i].IsDivider == false)
                {
                    textWidth = Mathf.Max(textWidth, Mathf.Ceil(MeasureWidth(_rows[i].Label)));
                }
            }

            float iconSize = ROW_HEIGHT - ICON_INSET * 2f;
            float column = iconSize + ICON_GAP;
            _width = Mathf.Max(textWidth + PADDING * 2f + column, MIN_WIDTH);
            float rowWidth = _width - PADDING;
            float y = 0f;

            for (int i = 0; i < rowCount; i++)
            {
                Row row = _rows[i];
                float height = row.IsDivider ? DIVIDER_HEIGHT : ROW_HEIGHT;
                row.Top = y;
                row.Height = height;
                InventoryUIFactory.SetLayout(row.Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -y - height), new Vector2(rowWidth, -y));
                InventoryUIFactory.SetLayout(row.Icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -ICON_INSET - iconSize), new Vector2(iconSize, -ICON_INSET));
                InventoryUIFactory.SetLayout(row.Label.rectTransform, Vector2.zero, Vector2.one, new Vector2(column, 0f), Vector2.zero);
                InventoryUIFactory.SetLayout(row.Divider.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -DIVIDER_LINE_HEIGHT * 0.5f), new Vector2(0f, DIVIDER_LINE_HEIGHT * 0.5f));
                y += height;
            }

            float panelHeight = Mathf.Max(ROW_HEIGHT + y, MIN_HEIGHT);
            InventoryUIFactory.SetLayout(_list, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(PADDING + LIST_SHIFT, -PADDING - y), new Vector2(PADDING + LIST_SHIFT + rowWidth, -PADDING));
            SetHovered(null);
            _panel.sizeDelta = new Vector2(_width, panelHeight);
            Vector2 local = InventoryUIFactory.ScreenToParentLocal(_panel, screenPosition, eventCamera);
            InventoryUIFactory.PlaceInsideParent(_panel, local);
        }

        public void Hide()
        {
            if (gameObject.activeSelf == false)
            {
                return;
            }

            SetHovered(null);
            gameObject.SetActive(false);
            Action hidden = _onHidden;
            _onHidden = null;
            hidden?.Invoke();
        }

        public bool Invoke(string label)
        {
            foreach (Row row in _rows)
            {
                if (row.Root.gameObject.activeSelf && row.IsDivider == false && row.EntryIndex >= 0 && _ordered[row.EntryIndex].Enabled && row.Label.text == Capitalize(label))
                {
                    Execute(row);
                    return true;
                }
            }

            return false;
        }

        private void Awake() => EnsureBuilt();

        private static void SortByGroup(List<Entry> entries)
        {
            for (int i = 1; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                int j = i - 1;

                while (j >= 0 && entries[j].Group > entry.Group)
                {
                    entries[j + 1] = entries[j];
                    j--;
                }

                entries[j + 1] = entry;
            }
        }

        private static string Capitalize(string text)
        {
            if (string.IsNullOrEmpty(text) || char.IsUpper(text[0]))
            {
                return text ?? string.Empty;
            }

            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static float MeasureWidth(TextMeshProUGUI text)
        {
            text.ForceMeshUpdate(true);
            return text.text.Length > 0 ? text.GetPreferredValues(text.text).x : 0f;
        }

        private void PrepareDivider(int index)
        {
            Row row = GetRow(index);
            row.IsDivider = true;
            row.EntryIndex = -1;
            row.Handler.Index = index;
            row.Root.gameObject.SetActive(true);
            row.Icon.enabled = false;
            row.Label.text = string.Empty;
            Sprite divider = Theme != null ? Theme.ContextMenuDivider : null;
            row.Divider.sprite = divider;
            row.Divider.color = divider != null ? Color.white : DIVIDER_FALLBACK_COLOR;
            row.Divider.gameObject.SetActive(true);
        }

        private void PrepareEntry(int index, int entryIndex)
        {
            Entry entry = _ordered[entryIndex];
            Row row = GetRow(index);
            row.IsDivider = false;
            row.EntryIndex = entryIndex;
            row.Handler.Index = index;
            row.Root.gameObject.SetActive(true);
            row.Divider.gameObject.SetActive(false);
            row.Icon.sprite = entry.Icon;
            row.Icon.enabled = entry.Icon != null;
            row.Icon.color = entry.Enabled ? Color.white : DISABLED_TEXT_COLOR;
            row.Label.text = Capitalize(entry.Label);
            row.Label.color = entry.Enabled ? TEXT_COLOR : DISABLED_TEXT_COLOR;

            if (Theme != null && Theme.BodyFont != null)
            {
                row.Label.font = Theme.BodyFont;
            }
        }

        private Row GetRow(int index)
        {
            while (_rows.Count <= index)
            {
                _rows.Add(CreateRow(_rows.Count));
            }

            return _rows[index];
        }

        private Row CreateRow(int index)
        {
            RectTransform root = InventoryUIFactory.CreateRect("Row " + index, _list);
            Image hitArea = root.gameObject.AddComponent<Image>();
            hitArea.color = new Color(0f, 0f, 0f, 0f);
            hitArea.raycastTarget = true;
            RectTransform iconRect = InventoryUIFactory.CreateRect("Icon", root);
            Image icon = iconRect.gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            TextMeshProUGUI label = InventoryUIFactory.CreateText("Label", root, string.Empty, FONT_SIZE, TextAlignmentOptions.MidlineLeft, TEXT_COLOR);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            RectTransform dividerRect = InventoryUIFactory.CreateRect("Divider", root);
            Image divider = dividerRect.gameObject.AddComponent<Image>();
            divider.raycastTarget = false;
            InventoryContextMenuRow handler = root.gameObject.AddComponent<InventoryContextMenuRow>();
            var row = new Row(root, icon, label, divider, handler);
            handler.Entered += OnRowEntered;
            handler.Exited += OnRowExited;
            handler.Clicked += OnRowClicked;
            return row;
        }

        private void ApplyTheme()
        {
            Sprite frame = Theme != null ? Theme.ContextMenuFrame : null;
            _frame.sprite = frame;
            _frame.color = frame != null ? Color.white : InventoryUIFactory.PanelColor;
            _frame.type = frame != null ? Image.Type.Tiled : Image.Type.Simple;
            _frame.pixelsPerUnitMultiplier = FRAME_SCALE;
            Sprite highlight = Theme != null ? Theme.ContextMenuHighlight : null;
            _highlight.sprite = highlight;
            _highlight.color = highlight != null ? Color.white : HIGHLIGHT_FALLBACK_COLOR;
        }

        private void OnRowEntered(InventoryContextMenuRow handler)
        {
            Row row = handler.Index >= 0 && handler.Index < _rows.Count ? _rows[handler.Index] : null;
            SetHovered(row != null && row.IsDivider == false && row.EntryIndex >= 0 && _ordered[row.EntryIndex].Enabled ? row : null);
        }

        private void OnRowExited(InventoryContextMenuRow handler)
        {
            if (_hovered != null && _hovered.Handler == handler)
            {
                SetHovered(null);
            }
        }

        private void OnRowClicked(InventoryContextMenuRow handler)
        {
            Row row = handler.Index >= 0 && handler.Index < _rows.Count ? _rows[handler.Index] : null;

            if (row != null && row.IsDivider == false && row.EntryIndex >= 0 && _ordered[row.EntryIndex].Enabled)
            {
                Execute(row);
            }
        }

        private void Execute(Row row)
        {
            Action callback = _ordered[row.EntryIndex].Callback;
            Hide();
            callback?.Invoke();
        }

        private void SetHovered(Row row)
        {
            if (_hovered != null && _hovered.EntryIndex >= 0 && _hovered.EntryIndex < _ordered.Count)
            {
                _hovered.Label.color = _ordered[_hovered.EntryIndex].Enabled ? TEXT_COLOR : DISABLED_TEXT_COLOR;
            }

            _hovered = row;

            if (_highlight == null)
            {
                return;
            }

            if (row == null)
            {
                _highlight.gameObject.SetActive(false);
                return;
            }

            row.Label.color = HOVER_TEXT_COLOR;
            InventoryUIFactory.SetLayout(_highlight.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(PADDING * 0.5f, -PADDING - row.Top - row.Height), new Vector2(_width - PADDING * 0.5f, -PADDING - row.Top));
            _highlight.gameObject.SetActive(true);
        }

        private void EnsureBuilt()
        {
            if (_rectTransform != null)
            {
                return;
            }

            _rectTransform = (RectTransform)transform;
            InventoryUIFactory.Stretch(_rectTransform);
            Image blocker = InventoryUIFactory.AddPanel(gameObject, new Color(0f, 0f, 0f, 0f));
            blocker.raycastTarget = true;
            InventoryUIFactory.GetOrAdd<InventoryClickCatcher>(gameObject).Clicked = Hide;
            _panel = InventoryUIFactory.CreateRect("Panel", transform);
            _frame = _panel.gameObject.AddComponent<Image>();
            _frame.raycastTarget = true;
            RectTransform highlight = InventoryUIFactory.CreateRect("Highlight", _panel);
            _highlight = highlight.gameObject.AddComponent<Image>();
            _highlight.raycastTarget = false;
            _highlight.gameObject.SetActive(false);
            _list = InventoryUIFactory.CreateRect("List", _panel);
        }

        public readonly struct Entry
        {
            public Entry(string label, bool enabled, Action callback, Sprite icon = null, int group = 0)
            {
                Label = label ?? string.Empty;
                Enabled = enabled;
                Callback = callback;
                Icon = icon;
                Group = group;
            }

            public string Label { get; }
            public bool Enabled { get; }
            public Action Callback { get; }
            public Sprite Icon { get; }
            public int Group { get; }
        }

        private sealed class Row
        {
            public Row(RectTransform root, Image icon, TextMeshProUGUI label, Image divider, InventoryContextMenuRow handler)
            {
                Root = root;
                Icon = icon;
                Label = label;
                Divider = divider;
                Handler = handler;
            }

            public RectTransform Root { get; }
            public Image Icon { get; }
            public TextMeshProUGUI Label { get; }
            public Image Divider { get; }
            public InventoryContextMenuRow Handler { get; }
            public bool IsDivider { get; set; }
            public int EntryIndex { get; set; } = -1;
            public float Top { get; set; }
            public float Height { get; set; }
        }
    }
}
