using System;
using System.Collections.Generic;
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
        private static readonly Color NORMAL_COLOR = new(170f / 255f, 170f / 255f, 170f / 255f, 1f);
        private static readonly Color NEGATIVE_COLOR = new(200f / 255f, 50f / 255f, 50f / 255f, 1f);
        private static readonly Color BETTER_COLOR = new(100f / 255f, 1f, 100f / 255f, 200f / 255f);
        private static readonly Color WORSE_COLOR = new(1f, 100f / 255f, 100f / 255f, 200f / 255f);
        private static readonly Color HIGHLIGHT_COLOR = Color.white;
        private static readonly Color COMPARISON_COLOR = new(0.9f, 0.9f, 0.9f, 1f);

        private readonly List<StatRow> _statRows = new();
        private RectTransform _rectTransform;
        private Image _background;
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
            ShowItem(item.Definition, item.TotalWeight, screenPosition, eventCamera, stats, description);
        }

        public void ShowDefinition(ItemDefinition definition, Vector2 screenPosition, Camera eventCamera, IReadOnlyList<InventoryItemStat> stats, string description)
        {
            ShowItem(definition, definition.Weight, screenPosition, eventCamera, stats, description);
        }

        private void ShowItem(ItemDefinition definition, float weight, Vector2 screenPosition, Camera eventCamera, IReadOnlyList<InventoryItemStat> stats, string description)
        {
            EnsureBuilt();
            _weight.text = InventoryUIFactory.FormatWeight(weight) + InventoryTheme.Resolve(Theme, "unit_kg", " kg");
            ShowContent(definition.DisplayName, description ?? definition.Description, stats, true, screenPosition, eventCamera);
        }

        public void Show(string title, string description, Vector2 screenPosition, Camera eventCamera)
        {
            EnsureBuilt();
            ShowContent(title, description, null, false, screenPosition, eventCamera);
        }

        private void ShowContent(string title, string description, IReadOnlyList<InventoryItemStat> stats, bool showWeight, Vector2 screenPosition, Camera eventCamera)
        {
            if (Theme != null)
            {
                Theme.ApplyTo(transform, false);
            }

            _background.sprite = null;
            _background.type = Image.Type.Simple;
            _background.color = Theme != null ? Theme.TooltipBackgroundColor : InventoryUIFactory.TooltipBackgroundColor;
            _title.text = title ?? string.Empty;
            _weight.gameObject.SetActive(showWeight);
            bool hasDescription = string.IsNullOrEmpty(description) == false;
            _body.text = hasDescription ? description : string.Empty;
            _body.gameObject.SetActive(hasDescription);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            float statsHeight = ApplyStats(stats);
            float contentHeight = MeasureHeight(_title);

            if (showWeight)
            {
                contentHeight += SECTION_GAP + MeasureHeight(_weight);
            }

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
            return text.text.Length > 0 ? text.preferredWidth : 0f;
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
            _stats.gameObject.SetActive(StatCount > 0);

            while (_statRows.Count < StatCount)
            {
                _statRows.Add(CreateStatRow(_statRows.Count));
            }

            bool anyIcon = false;
            bool anyBar = false;
            float labelWidth = 0f;
            float valueWidth = 0f;
            float barValueWidth = 0f;
            float comparisonWidth = 0f;
            Color barBackground = Theme != null ? Theme.TooltipStatBarBackgroundColor : InventoryTheme.DEFAULT_TOOLTIP_STAT_BAR_BACKGROUND_COLOR;
            Color barFill = Theme != null ? Theme.AccentColor : InventoryUIFactory.AccentColor;
            Color barText = Theme != null ? Theme.TextColor : NORMAL_COLOR;
            Color barBetter = Theme != null ? Theme.ConditionColor : BETTER_COLOR;
            Color barWorse = Theme != null ? Theme.TooltipStatWorseColor : InventoryTheme.DEFAULT_TOOLTIP_STAT_WORSE_COLOR;

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
                row.Icon.sprite = stat.Icon;
                row.Icon.enabled = stat.Icon != null;
                row.Label.font = _body.font;
                row.Value.font = _body.font;
                row.Comparison.font = _body.font;
                row.Label.text = stat.Label;
                row.Value.text = stat.Value;
                bool hasCurrentBar = stat.HasCurrentBar;
                Color deltaColor = stat.Tone == InventoryStatTone.Better ? barBetter : barWorse;
                row.Value.color = hasCurrentBar ? deltaColor : stat.HasBar && stat.Tone == InventoryStatTone.Normal ? barText : ToneColor(stat.Tone);
                row.Comparison.text = stat.Comparison;
                row.Back.gameObject.SetActive(stat.HasBar);
                row.Back.color = barBackground;
                float baseline = Mathf.Clamp01(stat.Fill);
                float current = hasCurrentBar ? Mathf.Clamp01(stat.CurrentFill) : baseline;
                float shared = Mathf.Min(baseline, current);
                float end = Mathf.Max(baseline, current);
                row.Fill.rectTransform.anchorMax = new Vector2(shared, 1f);
                row.Fill.color = barFill;
                labelWidth = Mathf.Max(labelWidth, MeasureWidth(row.Label));
                float preferredValueWidth = MeasureWidth(row.Value);
                if (end > shared)
                {
                    if (row.DeltaFill == null)
                    {
                        RectTransform delta = InventoryUIFactory.CreateRect("Delta Fill", row.Back.rectTransform);
                        row.DeltaFill = InventoryUIFactory.AddPanel(delta.gameObject, deltaColor, false);
                    }

                    row.DeltaFill.gameObject.SetActive(true);
                    InventoryUIFactory.SetLayout(row.DeltaFill.rectTransform, new Vector2(shared, 0f), new Vector2(end, 1f), Vector2.zero, Vector2.zero);
                    row.DeltaFill.color = deltaColor;
                }
                else if (row.DeltaFill != null)
                {
                    row.DeltaFill.gameObject.SetActive(false);
                }

                if (stat.HasBar)
                {
                    barValueWidth = Mathf.Max(barValueWidth, preferredValueWidth);
                }
                else
                {
                    valueWidth = Mathf.Max(valueWidth, preferredValueWidth);
                }
                comparisonWidth = Mathf.Max(comparisonWidth, MeasureWidth(row.Comparison));
                anyIcon |= stat.Icon != null;
                anyBar |= stat.HasBar;
            }

            float labelStart = anyIcon ? STAT_ICON_SIZE + STAT_ICON_GAP : 0f;
            float contentWidth = WIDTH - PADDING * 2f - labelStart;
            float comparisonGap = comparisonWidth > 0f ? STAT_VALUE_GAP : 0f;
            valueWidth = Mathf.Min(valueWidth, contentWidth * (anyIcon ? 0.35f : 0.5f));
            barValueWidth = Mathf.Min(barValueWidth, contentWidth * 0.35f);
            comparisonWidth = Mathf.Min(comparisonWidth, contentWidth * 0.25f);
            float availableWidth = contentWidth - comparisonWidth - STAT_VALUE_GAP - comparisonGap;
            float barWidth = anyBar ? Mathf.Min(STAT_BAR_WIDTH, (availableWidth - barValueWidth) * 0.4f) : 0f;
            float dataWidth = Mathf.Max(valueWidth, anyBar ? barWidth + STAT_VALUE_GAP + barValueWidth : 0f);
            labelWidth = Mathf.Min(labelWidth, availableWidth - dataWidth);
            float dataStart = labelStart + labelWidth + STAT_VALUE_GAP;
            float barValueStart = dataStart + barWidth + STAT_VALUE_GAP;
            float comparisonStart = dataStart + dataWidth + comparisonGap;
            float height = 0f;

            for (int i = 0; i < StatCount; i++)
            {
                StatRow row = _statRows[i];
                bool hasBar = stats[i].HasBar;
                float rowValueStart = hasBar ? barValueStart : dataStart;
                float rowValueWidth = hasBar ? barValueWidth : dataWidth;
                float labelHeight = row.Label.GetPreferredValues(row.Label.text, labelWidth, 0f).y;
                float valueHeight = row.Value.GetPreferredValues(row.Value.text, rowValueWidth, 0f).y;
                float rowHeight = Mathf.Max(STAT_ROW_HEIGHT, Mathf.Ceil(Mathf.Max(labelHeight, valueHeight)));
                TextAlignmentOptions alignment = !hasBar && valueHeight > STAT_ROW_HEIGHT ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
                row.Label.alignment = alignment;
                row.Value.alignment = alignment;
                row.Root.sizeDelta = new Vector2(0f, rowHeight);
                row.Root.anchoredPosition = new Vector2(0f, -height);
                height += rowHeight;
                InventoryUIFactory.SetLayout(row.Label.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(labelStart, 0f), new Vector2(labelStart + labelWidth, 0f));
                InventoryUIFactory.SetLayout(row.Back.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(dataStart, -BAR_HEIGHT * 0.5f), new Vector2(dataStart + barWidth, BAR_HEIGHT * 0.5f));
                InventoryUIFactory.SetLayout(row.Value.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(rowValueStart, 0f), new Vector2(rowValueStart + rowValueWidth, 0f));
                InventoryUIFactory.SetLayout(row.Comparison.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(comparisonStart, 0f), new Vector2(comparisonStart + comparisonWidth, 0f));
            }

            _stats.sizeDelta = new Vector2(_stats.sizeDelta.x, height);
            return height;
        }

        private StatRow CreateStatRow(int index)
        {
            RectTransform root = InventoryUIFactory.CreateRect("Stat " + index, _stats);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = new Vector2(0f, STAT_ROW_HEIGHT);
            RectTransform icon = InventoryUIFactory.CreateRect("Icon", root);
            InventoryUIFactory.SetLayout(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, -STAT_ICON_SIZE * 0.5f), new Vector2(STAT_ICON_SIZE, STAT_ICON_SIZE * 0.5f));
            Image iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            TextMeshProUGUI label = CreateRowText("Label", root, STAT_LABEL_COLOR);
            label.textWrappingMode = TextWrappingModes.Normal;
            RectTransform back = InventoryUIFactory.CreateRect("Bar", root);
            Image backImage = InventoryUIFactory.AddPanel(back.gameObject, InventoryTheme.DEFAULT_TOOLTIP_STAT_BAR_BACKGROUND_COLOR, false);
            RectTransform fill = InventoryUIFactory.CreateRect("Fill", back);
            InventoryUIFactory.SetLayout(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image fillImage = InventoryUIFactory.AddPanel(fill.gameObject, InventoryUIFactory.AccentColor, false);
            TextMeshProUGUI value = CreateRowText("Value", root, NORMAL_COLOR);
            value.textWrappingMode = TextWrappingModes.Normal;
            TextMeshProUGUI comparison = CreateRowText("Comparison", root, COMPARISON_COLOR);
            return new StatRow(root, iconImage, label, backImage, fillImage, value, comparison);
        }

        private static TextMeshProUGUI CreateRowText(string name, RectTransform root, Color color)
        {
            TextMeshProUGUI text = InventoryUIFactory.CreateText(name, root, string.Empty, STAT_FONT_SIZE, TextAlignmentOptions.MidlineLeft, color);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private void EnsureBuilt()
        {
            if (_rectTransform != null)
            {
                return;
            }

            _rectTransform = (RectTransform)transform;
            _background = InventoryUIFactory.AddPanel(gameObject, InventoryUIFactory.TooltipBackgroundColor, false);
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
            public Image DeltaFill { get; set; }
        }
    }
}
