using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryItemView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float DRAGGED_SOURCE_ALPHA = 0.35f;
        private const float DIMMED_ALPHA = 0.25f;
        private const float INFO_BACKGROUND_PADDING_X = 3f;
        private const float INFO_BACKGROUND_PADDING_Y = 1f;
        private const float COMMUNITY_ICON_GAP = 5f;

        [SerializeField] private RectTransform _content;
        [SerializeField] private RectTransform _overlay;
        [SerializeField] private Image _background;
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _countText;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _infoText;
        [SerializeField] private TextMeshProUGUI _infoShadow;
        [SerializeField] private TextMeshProUGUI _nameShadow;
        [SerializeField] private TextMeshProUGUI _conditionText;
        [SerializeField] private TextMeshProUGUI _conditionShadow;
        [SerializeField] private Image[] _infoBackgrounds;
        [SerializeField] private Image[] _nameBackgrounds;
        [SerializeField] private Image[] _conditionBackgrounds;
        [SerializeField] private Image _badge;
        [SerializeField] private Image _communityIcon;

        private RectTransform _rectTransform;
        private InventoryTheme _appliedTheme;
        private Image _indicator;
        private Image _iconTarget;
        private Image _backgroundTarget;
        private CanvasGroup _canvasGroup;
        private InventoryGridViewBase _grid;
        private InventoryItemIconBinding _iconBinding;
        private bool _dimmed;
        private bool _dragSource;
        private bool _hovered;

        public ItemInstance Item { get; private set; }
        public InventoryGridViewBase Grid => _grid;
        public RectTransform RectTransform => _rectTransform;
        public Image Icon => _icon;
        public Image Background => _backgroundTarget != null ? _backgroundTarget : _background;
        public Sprite IconSprite => _icon != null ? _icon.sprite : null;
        public bool IsIconLoading => _iconBinding?.IsLoading == true;
        public string CountText => _appliedTheme != null ? _infoText.text : _countText != null ? _countText.text : string.Empty;
        public bool IsDimmed => _dimmed;
        private InventoryTheme Theme => _grid != null && _grid.Window != null ? _grid.Window.Theme : null;

        public static InventoryItemView Create(RectTransform parent, HideFlags hideFlags)
        {
            var root = new GameObject("Item", typeof(RectTransform));
            root.hideFlags = hideFlags;
            root.layer = parent.gameObject.layer;
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            InventoryItemView view = root.AddComponent<InventoryItemView>();
            view.Build(hideFlags);
            return view;
        }

        public void Bind(ItemInstance item, InventoryGridViewBase grid)
        {
            if (_grid != null && (Item != item || _grid != grid))
            {
                _grid.Window?.HandleItemViewDetached(this);
                SetHovered(false);
            }

            Item = item;
            _grid = grid;
            _canvasGroup.blocksRaycasts = true;
            Refresh();
        }

        public void BindDefinition(ItemDefinition definition, Image icon, IItemIconProvider provider)
        {
            _icon = icon;
            RequestIcon(new ItemIconRequest(definition, null, definition.Width, definition.Height, ItemIconProfileType.Default), provider);
        }

        public void BindCatalogItem(ItemDefinition definition, InventoryWindow window, Vector2 size, string shortName, InventoryItemDecoration decoration)
        {
            _rectTransform = (RectTransform)transform;
            _content.sizeDelta = size;
            ApplyTheme(window.Theme);
            ApplyDecoration(definition, decoration);
            ApplyTexts(definition, 1, shortName, false, definition.Weight, null);
            RequestIcon(new ItemIconRequest(definition, null, definition.Width, definition.Height, ItemIconProfileType.Default), window.IconProvider);
        }

        public void Refresh()
        {
            if (Item == null || _grid == null)
            {
                return;
            }

            ApplyTheme(Theme);
            ApplyDecoration();
            ApplyTexts(Item.Amount);
            bool slotIcon = _grid.UsesSlotIcon(Item, out Vector2Int slotSize);
            ItemIconRequest request = slotIcon ? ItemIconRequest.ForSlot(Item, slotSize.x, slotSize.y) : ItemIconRequest.ForItem(Item);
            RequestIcon(request);

            if (_grid.Container != null && _grid.Container.Contains(Item))
            {
                Layout(_grid.GetAreaRect(_grid.Container.GetItemArea(Item)));
            }
        }

        public void Layout(Rect topLeftArea)
        {
            if (Item == null || _grid == null)
            {
                return;
            }

            _rectTransform.anchoredPosition = new Vector2(topLeftArea.center.x, -topLeftArea.center.y);
            _rectTransform.sizeDelta = topLeftArea.size;

            if (_grid.UsesSlotIcon(Item, out _))
            {
                Rect iconArea = _grid.GetIconRect(Item, topLeftArea);
                _content.sizeDelta = iconArea.size;
                _content.anchoredPosition = new Vector2(iconArea.center.x - topLeftArea.center.x, topLeftArea.center.y - iconArea.center.y);
                _content.localRotation = Quaternion.identity;
                LayoutTextBackgrounds();
                return;
            }

            ApplyContent(Item.GetSize(false), _grid.CellSize, Item.Location.Rotated);
            LayoutTextBackgrounds();
        }

        public void SetDragSource(bool dragging)
        {
            _dragSource = dragging;
            UpdateAlpha();
        }

        public void SetDimmed(bool dimmed)
        {
            _dimmed = dimmed;
            UpdateAlpha();
        }

        public void SetIconTarget(Image target, Image backgroundTarget = null)
        {
            if (_iconTarget == target && _backgroundTarget == backgroundTarget)
            {
                return;
            }

            if (_iconTarget != null)
            {
                _iconTarget.canvasRenderer.SetAlpha(1f);
            }

            if (_backgroundTarget != null)
            {
                _backgroundTarget.enabled = false;
            }

            _iconTarget = target;
            _backgroundTarget = backgroundTarget;
            bool external = target != null;
            _background.enabled = external == false;
            _overlay.gameObject.SetActive(true);

            if (external)
            {
                target.preserveAspect = true;
            }

            if (Item != null)
            {
                ApplyDecoration();
            }

            SetIcon(_icon.sprite);
            UpdateAlpha();
        }

        public void SetIndicator(bool visible, Color indicatorColor)
        {
            _indicator.enabled = visible;
            _indicator.color = indicatorColor;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            SetHovered(true);
            _grid?.Window?.HandleItemPointerEnter(this, eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHovered(false);
            _grid?.Window?.HandleItemPointerExit(this, eventData);
        }

        public void OnPointerClick(PointerEventData eventData) => _grid?.Window?.HandleItemClick(this, eventData);

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = true;
        public void OnBeginDrag(PointerEventData eventData) => _grid?.Window?.HandleBeginDrag(this, eventData);
        public void OnDrag(PointerEventData eventData) => _grid?.Window?.HandleItemDrag(this, eventData);
        public void OnEndDrag(PointerEventData eventData) => _grid?.Window?.HandleItemEndDrag(this, eventData);

        private void OnDisable()
        {
            if (_grid == null && _iconBinding?.HasRequest == true)
            {
                _iconBinding.Reset();
                SetIcon(null);
            }

            SetHovered(false);
            _grid?.Window?.HandleItemViewDetached(this);

            if (_backgroundTarget != null)
            {
                _backgroundTarget.enabled = false;
            }
        }

        private void OnDestroy() => _iconBinding?.Cancel();

        private void ApplyContent(Vector2Int baseSize, Vector2 cellSize, bool rotated)
        {
            _content.sizeDelta = new Vector2(baseSize.x * cellSize.x, baseSize.y * cellSize.y);
            _content.anchoredPosition = Vector2.zero;
            _content.localRotation = Quaternion.Euler(0f, 0f, rotated ? -90f : 0f);
        }

        private void ApplyTexts(int amount)
        {
            ItemDefinition definition = Item.Definition;
            InventoryWindow window = _grid != null ? _grid.Window : null;
            string customName = window != null && window.ItemShortNameProvider != null ? window.ItemShortNameProvider(Item) : null;
            bool inSlot = _grid != null && _grid.UsesSlotIcon(Item, out _);
            float weight = _appliedTheme != null && _appliedTheme.ShowItemWeight ? Item.UnitWeight * amount : 0f;
            ApplyTexts(definition, amount, customName, inSlot, weight, _appliedTheme != null ? GetCondition(window) : null);
        }

        private float? GetCondition(InventoryWindow window)
        {
            float? conditionValue = window != null && window.ItemConditionProvider != null ? window.ItemConditionProvider(Item) : null;
            string condition = Item.GetState("condition");

            if (!conditionValue.HasValue && !string.IsNullOrEmpty(condition) && float.TryParse(condition, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
            {
                return value;
            }

            return conditionValue;
        }

        private void ApplyTexts(ItemDefinition definition, int amount, string customName, bool inSlot, float totalWeight, float? conditionValue)
        {
            InventoryTheme theme = _appliedTheme;
            bool showCount = definition.IsStackable && amount > 1;
            _nameText.alignment = inSlot && customName == null ? TextAlignmentOptions.Top : TextAlignmentOptions.BottomRight;
            _nameShadow.alignment = _nameText.alignment;

            if (theme == null)
            {
                _countText.enabled = showCount;

                if (showCount)
                {
                    _countText.SetText("{0}", amount);
                }

                _nameText.text = customName ?? definition.ShortName;
                _nameText.alignment = inSlot && customName == null ? TextAlignmentOptions.Top : customName != null ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.TopLeft;
                _nameText.enabled = string.IsNullOrEmpty(_nameText.text) == false;
                return;
            }

            _countText.enabled = false;
            string count = showCount && theme.ShowItemCount ? theme.FormatCount(amount) : string.Empty;
            string weight = theme.ShowItemWeight ? theme.FormatItemWeight(totalWeight) : string.Empty;
            string info = count.Length > 0 && weight.Length > 0 ? count + "\n" + weight : count + weight;
            SetLabel(_infoText, _infoShadow, info);
            SetLabel(_nameText, _nameShadow, customName ?? (theme.ShowsShortName(definition) ? definition.ShortName : string.Empty));
            string conditionText = string.Empty;

            if (conditionValue.HasValue && float.IsFinite(conditionValue.Value))
            {
                conditionText = (Mathf.Clamp01(conditionValue.Value) * 100f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
            }

            SetLabel(_conditionText, _conditionShadow, conditionText);
            LayoutTextBackgrounds();
        }

        private void ApplyTheme(InventoryTheme theme)
        {
            if (theme == _appliedTheme)
            {
                return;
            }

            _appliedTheme = theme;
            bool themed = theme != null;
            _infoText.gameObject.SetActive(themed);
            _conditionText.gameObject.SetActive(themed);
            _infoShadow.gameObject.SetActive(themed && theme.ItemTextShadow && theme.ItemInfoBackground.a <= 0f);
            _nameShadow.gameObject.SetActive(themed && theme.ItemTextShadow && theme.ItemInfoBackground.a <= 0f);
            _conditionShadow.gameObject.SetActive(themed && theme.ItemTextShadow && theme.ItemInfoBackground.a <= 0f);

            if (themed == false)
            {
                HideTextBackgrounds();
                _nameText.alignment = TextAlignmentOptions.TopLeft;
                return;
            }

            _nameText.alignment = TextAlignmentOptions.BottomRight;
            StyleLabel(_infoText, _infoShadow, theme.SmallFont, theme.ItemTextSize, theme.ItemTextColor, theme);
            StyleLabel(_nameText, _nameShadow, theme.BodyFont, theme.ItemNameSize, theme.ItemTextColor, theme);
            StyleLabel(_conditionText, _conditionShadow, theme.SmallFont, theme.ItemTextSize, theme.ConditionColor, theme);
        }

        private static void StyleLabel(TextMeshProUGUI label, TextMeshProUGUI shadow, TMP_FontAsset font, float size, Color color, InventoryTheme theme)
        {
            if (font != null)
            {
                label.font = font;
                shadow.font = font;
            }

            label.fontSize = size;
            shadow.fontSize = size;
            label.color = color;
            shadow.color = theme.ItemShadowColor;
            shadow.overrideColorTags = true;
            shadow.alignment = label.alignment;
            shadow.lineSpacing = label.lineSpacing;
        }

        private static void SetLabel(TextMeshProUGUI label, TextMeshProUGUI shadow, string value)
        {
            label.text = value;
            label.enabled = value.Length > 0;
            shadow.text = value;
            shadow.enabled = label.enabled;
        }

        private void LayoutTextBackgrounds()
        {
            LayoutTextBackgrounds(_infoText, _infoBackgrounds, _appliedTheme);
            LayoutTextBackgrounds(_nameText, _nameBackgrounds, _appliedTheme);
            LayoutTextBackgrounds(_conditionText, _conditionBackgrounds, _appliedTheme);

            if (_communityIcon.enabled)
            {
                float nameTop = _nameBackgrounds[0].enabled ? _overlay.rect.height + _nameBackgrounds[0].rectTransform.anchoredPosition.y : _nameText.rectTransform.offsetMin.y + _nameText.preferredHeight;
                _communityIcon.rectTransform.anchoredPosition = new Vector2(_nameText.rectTransform.offsetMax.x, nameTop + COMMUNITY_ICON_GAP);
            }
        }

        private static void LayoutTextBackgrounds(TextMeshProUGUI label, Image[] backgrounds, InventoryTheme theme)
        {
            if (theme == null || theme.ItemInfoBackground.a <= 0f || label.enabled == false)
            {
                for (int i = 0; i < backgrounds.Length; i++)
                {
                    backgrounds[i].enabled = false;
                }

                return;
            }

            label.ForceMeshUpdate();
            TMP_TextInfo textInfo = label.textInfo;

            for (int i = 0; i < backgrounds.Length; i++)
            {
                Image background = backgrounds[i];

                if (i >= textInfo.lineCount || textInfo.lineInfo[i].characterCount == 0)
                {
                    background.enabled = false;
                    continue;
                }

                Extents extents = textInfo.lineInfo[i].lineExtents;
                Rect textRect = label.rectTransform.rect;
                Vector2 textOffset = new Vector2(label.rectTransform.offsetMin.x, label.rectTransform.offsetMax.y);
                RectTransform rect = background.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(textOffset.x + extents.min.x - textRect.xMin - INFO_BACKGROUND_PADDING_X, textOffset.y + extents.max.y - textRect.yMax + INFO_BACKGROUND_PADDING_Y);
                rect.sizeDelta = new Vector2(extents.max.x - extents.min.x + INFO_BACKGROUND_PADDING_X * 2f, extents.max.y - extents.min.y + INFO_BACKGROUND_PADDING_Y * 2f);
                background.color = theme.ItemInfoBackground;
                background.enabled = true;
            }
        }

        private void HideTextBackgrounds()
        {
            for (int i = 0; i < _infoBackgrounds.Length; i++)
            {
                _infoBackgrounds[i].enabled = false;
            }

            for (int i = 0; i < _nameBackgrounds.Length; i++)
            {
                _nameBackgrounds[i].enabled = false;
            }

            for (int i = 0; i < _conditionBackgrounds.Length; i++)
            {
                _conditionBackgrounds[i].enabled = false;
            }
        }

        private void ApplyDecoration()
        {
            InventoryWindow window = _grid != null ? _grid.Window : null;
            InventoryItemDecoration decoration = window != null && window.ItemDecorator != null ? window.ItemDecorator(Item) : InventoryItemDecoration.None;
            ApplyDecoration(Item.Definition, decoration);
        }

        private void ApplyDecoration(ItemDefinition definition, InventoryItemDecoration decoration)
        {
            _communityIcon.sprite = decoration.CommunityIcon;
            _communityIcon.rectTransform.sizeDelta = decoration.CommunityIconSize;
            _communityIcon.enabled = decoration.CommunityIcon != null;
            Color background = decoration.HasTint ? decoration.Tint : BackgroundColor(definition);
            _background.color = _hovered ? HoverColor(background, decoration.HasTint) : background;
            _background.enabled = _iconTarget == null;

            if (_backgroundTarget != null)
            {
                _backgroundTarget.color = _background.color;
                _backgroundTarget.enabled = decoration.HasTint || _hovered;
            }

            _badge.sprite = decoration.Badge;
            _badge.enabled = decoration.Badge != null;
        }

        private void SetHovered(bool hovered)
        {
            if (_hovered == hovered || Item == null)
            {
                return;
            }

            _hovered = hovered;
            ApplyDecoration();
        }

        private Color HoverColor(Color background, bool tinted)
        {
            Color highlight = _grid != null ? _grid.Style.HighlightColor : Color.clear;

            if (tinted)
            {
                return new Color(background.r, background.g, background.b, Mathf.Clamp01(background.a + highlight.a));
            }

            return highlight.a > background.a ? highlight : background;
        }

        private Color BackgroundColor(ItemDefinition definition) => _appliedTheme != null && _appliedTheme.ShowItemBackground == false ? Color.clear : definition.BackgroundColor;

        private void UpdateAlpha()
        {
            _canvasGroup.alpha = _dragSource ? DRAGGED_SOURCE_ALPHA : _dimmed ? DIMMED_ALPHA : 1f;

            if (_iconTarget != null)
            {
                _iconTarget.canvasRenderer.SetAlpha(_canvasGroup.alpha);
            }

            if (_backgroundTarget != null)
            {
                _backgroundTarget.canvasRenderer.SetAlpha(_canvasGroup.alpha);
            }
        }

        private void RequestIcon(ItemIconRequest request, IItemIconProvider provider = null)
        {
            provider ??= _grid != null && _grid.Window != null ? _grid.Window.IconProvider : null;
            _iconBinding ??= new InventoryItemIconBinding(SetIcon);
            _iconBinding.Request(request, provider, _icon.sprite != null);
        }

        private void SetIcon(Sprite sprite)
        {
            _icon.sprite = sprite;
            _icon.enabled = sprite != null && _iconTarget == null;

            if (_iconTarget != null)
            {
                _iconTarget.sprite = sprite;
                _iconTarget.enabled = true;
                _iconTarget.gameObject.SetActive(sprite != null);
            }
        }

        private void Build(HideFlags hideFlags)
        {
            _rectTransform = (RectTransform)transform;
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            if (!TryGetComponent(out Image hitArea))
            {
                hitArea = gameObject.AddComponent<Image>();
            }
            hitArea.color = new Color(0f, 0f, 0f, 0f);
            hitArea.raycastTarget = true;
            _content = CreateChild("Content", hideFlags, _rectTransform, true);
            _content.anchorMin = new Vector2(0.5f, 0.5f);
            _content.anchorMax = new Vector2(0.5f, 0.5f);
            _background = _content.gameObject.AddComponent<Image>();
            _background.raycastTarget = false;
            _icon = CreateChild("Icon", hideFlags, _content, false).gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
            _icon.enabled = false;
            RectTransform overlay = CreateChild("Overlay", hideFlags, _rectTransform, false);
            _overlay = overlay;
            _infoBackgrounds = new Image[2];
            _nameBackgrounds = new Image[1];
            _conditionBackgrounds = new Image[1];

            for (int i = 0; i < _infoBackgrounds.Length; i++)
            {
                _infoBackgrounds[i] = CreateChild("InfoBackground" + i, hideFlags, overlay, true).gameObject.AddComponent<Image>();
                _infoBackgrounds[i].raycastTarget = false;
                _infoBackgrounds[i].enabled = false;
            }

            _nameBackgrounds[0] = CreateChild("NameBackground", hideFlags, overlay, true).gameObject.AddComponent<Image>();
            _nameBackgrounds[0].raycastTarget = false;
            _nameBackgrounds[0].enabled = false;
            _conditionBackgrounds[0] = CreateChild("ConditionBackground", hideFlags, overlay, true).gameObject.AddComponent<Image>();
            _conditionBackgrounds[0].raycastTarget = false;
            _conditionBackgrounds[0].enabled = false;
            _communityIcon = CreateChild("CommunityIcon", hideFlags, overlay, true).gameObject.AddComponent<Image>();
            _communityIcon.rectTransform.anchorMin = new Vector2(1f, 0f);
            _communityIcon.rectTransform.anchorMax = new Vector2(1f, 0f);
            _communityIcon.rectTransform.pivot = new Vector2(1f, 0f);
            _communityIcon.preserveAspect = true;
            _communityIcon.raycastTarget = false;
            _communityIcon.enabled = false;
            _infoShadow = CreateShadow("InfoShadow", hideFlags, overlay, TextAlignmentOptions.TopLeft);
            _infoText = CreateText("Info", hideFlags, overlay, TextAlignmentOptions.TopLeft, 15f);
            _conditionShadow = CreateShadow("ConditionShadow", hideFlags, overlay, TextAlignmentOptions.BottomLeft);
            _conditionText = CreateText("Condition", hideFlags, overlay, TextAlignmentOptions.BottomLeft, 15f);
            _countText = CreateText("Count", hideFlags, overlay, TextAlignmentOptions.BottomRight, 15f);
            _nameShadow = CreateShadow("NameShadow", hideFlags, overlay, TextAlignmentOptions.BottomRight);
            _nameText = CreateText("Name", hideFlags, overlay, TextAlignmentOptions.TopLeft, 12f);
            _infoText.gameObject.SetActive(false);
            _conditionText.gameObject.SetActive(false);
            _indicator = CreateChild("Indicator", hideFlags, overlay, true).gameObject.AddComponent<Image>();
            RectTransform indicatorRect = _indicator.rectTransform;
            indicatorRect.anchorMin = new Vector2(1f, 1f);
            indicatorRect.anchorMax = new Vector2(1f, 1f);
            indicatorRect.pivot = new Vector2(1f, 1f);
            indicatorRect.sizeDelta = new Vector2(8f, 8f);
            indicatorRect.anchoredPosition = new Vector2(-3f, -3f);
            _indicator.raycastTarget = false;
            _indicator.enabled = false;
            _badge = CreateChild("Badge", hideFlags, overlay, true).gameObject.AddComponent<Image>();
            RectTransform badgeRect = _badge.rectTransform;
            badgeRect.anchorMin = new Vector2(1f, 0.5f);
            badgeRect.anchorMax = new Vector2(1f, 0.5f);
            badgeRect.pivot = new Vector2(1f, 0.5f);
            badgeRect.sizeDelta = new Vector2(18f, 18f);
            badgeRect.anchoredPosition = new Vector2(-2f, 0f);
            _badge.raycastTarget = false;
            _badge.enabled = false;
        }

        private static RectTransform CreateChild(string childName, HideFlags hideFlags, RectTransform parent, bool fixedSize)
        {
            var child = new GameObject(childName, typeof(RectTransform));
            child.hideFlags = hideFlags;
            child.layer = parent.gameObject.layer;
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);

            if (fixedSize == false)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            return rect;
        }

        private static TextMeshProUGUI CreateShadow(string childName, HideFlags hideFlags, RectTransform parent, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI shadow = CreateText(childName, hideFlags, parent, alignment, 15f);
            shadow.rectTransform.offsetMin += new Vector2(1f, -1f);
            shadow.rectTransform.offsetMax += new Vector2(1f, -1f);
            shadow.gameObject.SetActive(false);
            return shadow;
        }

        private static TextMeshProUGUI CreateText(string childName, HideFlags hideFlags, RectTransform parent, TextAlignmentOptions alignment, float fontSize)
        {
            RectTransform rect = CreateChild(childName, hideFlags, parent, false);
            rect.offsetMin = new Vector2(4f, 2f);
            rect.offsetMax = new Vector2(-4f, -2f);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.alignment = alignment;
            text.fontSize = fontSize;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.color = new Color(0.92f, 0.92f, 0.88f, 1f);
            return text;
        }
    }
}
