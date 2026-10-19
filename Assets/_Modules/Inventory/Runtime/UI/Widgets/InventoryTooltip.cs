using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryTooltip : MonoBehaviour
    {
        private const float WIDTH = 440f;
        private const float PADDING = 10f;
        private const float STAT_ROW_HEIGHT = 22f;
        private const float STAT_ICON_SIZE = 19f;
        private const float STAT_ICON_GAP = 7f;
        private const float STAT_VALUE_GAP = 12f;
        private const float STAT_BAR_WIDTH = 140f;
        private const float SECTION_GAP = 8f;
        private const float STAT_FONT_SIZE = 14f;
        private const float BAR_HEIGHT = 6f;
        private static readonly Vector2 POINTER_OFFSET = new(18f, -18f);
        private static readonly Color STAT_LABEL_COLOR = new(0.65f, 0.66f, 0.62f, 1f);
        private static readonly Color BAR_BACK_COLOR = new(1f, 1f, 1f, 0.1f);
        private static readonly Color NORMAL_COLOR = new(170f / 255f, 170f / 255f, 170f / 255f, 1f);
        private static readonly Color NEGATIVE_COLOR = new(200f / 255f, 50f / 255f, 50f / 255f, 1f);
        private static readonly Color BETTER_COLOR = new(100f / 255f, 1f, 100f / 255f, 200f / 255f);
        private static readonly Color WORSE_COLOR = new(1f, 100f / 255f, 100f / 255f, 200f / 255f);
        private static readonly Color HIGHLIGHT_COLOR = Color.white;
        private static readonly Color COMPARISON_COLOR = new(0.9f, 0.9f, 0.9f, 1f);

        private readonly StringBuilder _builder = new(256);
        private readonly List<StatRow> _statRows = new();
        private RectTransform _rectTransform;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _weight;
        private TextMeshProUGUI _body;
        private RectTransform _stats;
        private VerticalContentFitter _fitter;

        public InventoryTheme Theme { get; set; }
        public bool IsVisible => gameObject.activeSelf;
        public string Title => _title != null ? _title.text : string.Empty;
        public string Description => _body != null ? _body.text : string.Empty;
        public int StatCount { get; private set; }

        public void Show(ItemInstance item, Vector2 screenPosition, Camera eventCamera) => Show(item, screenPosition, eventCamera, null, null);

        public void Show(ItemInstance item, Vector2 screenPosition, Camera eventCamera, IReadOnlyList<InventoryItemStat> stats) => Show(item, screenPosition, eventCamera, stats, null);

        public void Show(ItemInstance item, Vector2 screenPosition, Camera eventCamera, IReadOnlyList<InventoryItemStat> stats, string description)
        {
            EnsureBuilt();

            if (Theme != null)
            {
                Theme.ApplyTo(transform, false);
            }

            ItemDefinition definition = item.Definition;
            _title.text = definition.DisplayName;
            _builder.Clear();
            _builder.Append(InventoryUIFactory.FormatWeight(item.TotalWeight)).Append(InventoryTheme.Resolve(Theme, "unit_kg", " kg"));

            if (definition.IsStackable)
            {
                _builder.Append(InventoryTheme.Resolve(Theme, "tooltip_amount", "   Amount: ")).Append(item.Amount).Append('/').Append(definition.MaxStack);
            }

            _weight.text = _builder.ToString();
            string body = description ?? definition.Description;
            bool hasDescription = string.IsNullOrEmpty(body) == false;
            _body.text = hasDescription ? body : string.Empty;
            _body.gameObject.SetActive(hasDescription);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            float statsHeight = ApplyStats(stats);
            float contentHeight = MeasureHeight(_title) + SECTION_GAP + MeasureHeight(_weight);

            if (hasDescription)
            {
                contentHeight += SECTION_GAP + MeasureHeight(_body);
            }

            if (StatCount > 0)
            {
                contentHeight += SECTION_GAP + statsHeight;
            }

            _rectTransform.sizeDelta = new Vector2(WIDTH, contentHeight + PADDING * 2f);
            _fitter.Refresh();
            Follow(screenPosition, eventCamera);
        }

        public void Follow(Vector2 screenPosition, Camera eventCamera)
        {
            if (IsVisible == false)
            {
                return;
            }

            Vector2 local = InventoryUIFactory.ScreenToParentLocal(_rectTransform, screenPosition, eventCamera);
            InventoryUIFactory.PlaceInsideParent(_rectTransform, local + POINTER_OFFSET);
        }

        public void Hide() => gameObject.SetActive(false);

        public static Color ToneColor(InventoryStatTone tone) => tone switch
        {
            InventoryStatTone.Negative => NEGATIVE_COLOR,
            InventoryStatTone.Better => BETTER_COLOR,
            InventoryStatTone.Worse => WORSE_COLOR,
            InventoryStatTone.Highlight => HIGHLIGHT_COLOR,
            _ => NORMAL_COLOR
        };

        private void Awake() => EnsureBuilt();

        private static float MeasureHeight(TextMeshProUGUI text)
        {
            text.ForceMeshUpdate(true);
            float height = text.GetPreferredValues(text.text, WIDTH - PADDING * 2f, 0f).y;
            RectTransform rect = text.rectTransform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            return height;
        }

        private static float MeasureWidth(TextMeshProUGUI text)
        {
            text.ForceMeshUpdate(true);
            return text.text.Length > 0 ? text.GetPreferredValues(text.text).x : 0f;
        }

        private static void StretchTop(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, rect.sizeDelta.y);
            rect.anchoredPosition = Vector2.zero;
        }

        private float ApplyStats(IReadOnlyList<InventoryItemStat> stats)
        {
            StatCount = stats != null ? stats.Count : 0;

            while (_statRows.Count < StatCount)
            {
                _statRows.Add(CreateStatRow(_statRows.Count));
            }

            bool anyIcon = false;
            bool anyBar = false;
            float labelWidth = 0f;
            float valueWidth = 0f;

            for (int i = 0; i < _statRows.Count; i++)
            {
                StatRow row = _statRows[i];
                bool visible = i < StatCount;
                row.Root.gameObject.SetActive(visible);

                if (visible == false)
                {
                    continue;
                }

                InventoryItemStat stat = stats[i];
                row.Root.anchoredPosition = new Vector2(0f, (StatCount - 1 - i) * STAT_ROW_HEIGHT);
                row.Icon.sprite = stat.Icon;
                row.Icon.enabled = stat.Icon != null;
                row.Label.font = _body.font;
                row.Value.font = _body.font;
                row.Comparison.font = _body.font;
                row.Label.text = stat.Label;
                row.Value.text = stat.Value;
                row.Value.color = ToneColor(stat.Tone);
                row.Comparison.text = stat.Comparison;
                row.Back.gameObject.SetActive(stat.HasBar);
                row.Fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(stat.Fill), 1f);
                row.Fill.color = Theme != null ? Theme.AccentColor : InventoryUIFactory.AccentColor;
                labelWidth = Mathf.Max(labelWidth, MeasureWidth(row.Label));
                valueWidth = Mathf.Max(valueWidth, MeasureWidth(row.Value));
                anyIcon |= stat.Icon != null;
                anyBar |= stat.HasBar;
            }

            float labelStart = anyIcon ? STAT_ICON_SIZE + STAT_ICON_GAP : 0f;
            float barStart = labelStart + labelWidth + STAT_VALUE_GAP;
            float valueStart = anyBar ? barStart + STAT_BAR_WIDTH + STAT_VALUE_GAP : barStart;
            float comparisonStart = valueStart + valueWidth + STAT_VALUE_GAP;

            for (int i = 0; i < StatCount; i++)
            {
                StatRow row = _statRows[i];
                InventoryUIFactory.SetLayout(row.Label.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(labelStart, 0f), new Vector2(labelStart + labelWidth, 0f));
                InventoryUIFactory.SetLayout((RectTransform)row.Back.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(barStart, -BAR_HEIGHT * 0.5f), new Vector2(barStart + STAT_BAR_WIDTH, BAR_HEIGHT * 0.5f));
                InventoryUIFactory.SetLayout(row.Value.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(valueStart, 0f), new Vector2(valueStart + valueWidth, 0f));
                InventoryUIFactory.SetLayout(row.Comparison.rectTransform, Vector2.zero, Vector2.one, new Vector2(comparisonStart, 0f), Vector2.zero);
            }

            float height = StatCount * STAT_ROW_HEIGHT;
            _stats.sizeDelta = new Vector2(_stats.sizeDelta.x, height);
            _stats.gameObject.SetActive(StatCount > 0);
            return height;
        }

        private StatRow CreateStatRow(int index)
        {
            RectTransform root = InventoryUIFactory.CreateRect("Stat " + index, _stats);
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(0f, STAT_ROW_HEIGHT);
            RectTransform icon = InventoryUIFactory.CreateRect("Icon", root);
            InventoryUIFactory.SetLayout(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, -STAT_ICON_SIZE * 0.5f), new Vector2(STAT_ICON_SIZE, STAT_ICON_SIZE * 0.5f));
            Image iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            TextMeshProUGUI label = CreateRowText("Label", root, STAT_LABEL_COLOR);
            RectTransform back = InventoryUIFactory.CreateRect("Bar", root);
            Image backImage = InventoryUIFactory.AddPanel(back.gameObject, BAR_BACK_COLOR, false);
            RectTransform fill = InventoryUIFactory.CreateRect("Fill", back);
            InventoryUIFactory.SetLayout(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image fillImage = InventoryUIFactory.AddPanel(fill.gameObject, InventoryUIFactory.AccentColor, false);
            TextMeshProUGUI value = CreateRowText("Value", root, NORMAL_COLOR);
            TextMeshProUGUI comparison = CreateRowText("Comparison", root, COMPARISON_COLOR);
            return new StatRow(root, iconImage, label, backImage, fillImage, value, comparison);
        }

        private static TextMeshProUGUI CreateRowText(string name, RectTransform root, Color color)
        {
            TextMeshProUGUI text = InventoryUIFactory.CreateText(name, root, string.Empty, STAT_FONT_SIZE, TextAlignmentOptions.MidlineLeft, color);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        private void EnsureBuilt()
        {
            if (_rectTransform != null)
            {
                return;
            }

            _rectTransform = (RectTransform)transform;
            InventoryUIFactory.AddPanel(gameObject, InventoryUIFactory.PanelColor, false);
            CanvasGroup group = InventoryUIFactory.GetOrAdd<CanvasGroup>(gameObject);
            group.blocksRaycasts = false;
            group.interactable = false;
            RectTransform layout = InventoryUIFactory.Stretch(InventoryUIFactory.CreateRect("Layout", transform), PADDING);
            _fitter = layout.gameObject.AddComponent<VerticalContentFitter>();
            _fitter.SetSpacing(SECTION_GAP);
            _title = InventoryUIFactory.CreateText("Title", layout, string.Empty, 18f, TextAlignmentOptions.TopLeft, InventoryUIFactory.AccentColor);
            StretchTop(_title.rectTransform);
            _weight = InventoryUIFactory.CreateText("Weight", layout, string.Empty, 14f, TextAlignmentOptions.TopLeft, InventoryUIFactory.TextColor);
            StretchTop(_weight.rectTransform);
            _body = InventoryUIFactory.CreateText("Body", layout, string.Empty, 14f, TextAlignmentOptions.TopLeft, InventoryUIFactory.TextColor);
            StretchTop(_body.rectTransform);
            _body.richText = true;
            _stats = InventoryUIFactory.CreateRect("Stats", layout);
            StretchTop(_stats);
        }

        private sealed class StatRow
        {
            public StatRow(RectTransform root, Image icon, TextMeshProUGUI label, Image back, Image fill, TextMeshProUGUI value, TextMeshProUGUI comparison)
            {
                Root = root;
                Icon = icon;
                Label = label;
                Back = back;
                Fill = fill;
                Value = value;
                Comparison = comparison;
            }

            public RectTransform Root { get; }
            public Image Icon { get; }
            public TextMeshProUGUI Label { get; }
            public Image Back { get; }
            public Image Fill { get; }
            public TextMeshProUGUI Value { get; }
            public TextMeshProUGUI Comparison { get; }
        }
    }
}
