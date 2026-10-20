using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory.UI
{
    [CreateAssetMenu(menuName = "Inventory/UI Theme", fileName = "InventoryTheme")]
    public sealed class InventoryTheme : ScriptableObject
    {
        [SerializeField] private TMP_FontAsset _bodyFont;
        [SerializeField] private TMP_FontAsset _headerFont;
        [SerializeField] private TMP_FontAsset _accentFont;
        [SerializeField] private TMP_FontAsset _smallFont;
        [SerializeField] private Color _textColor = new(0.9f, 0.9f, 0.86f, 1f);
        [SerializeField] private Color _mutedTextColor = new(0.5f, 0.5f, 0.5f, 1f);
        [SerializeField] private Color _accentColor = new(0.94f, 0.85f, 0.71f, 1f);
        [SerializeField] private Color _negativeColor = new(1f, 0.2f, 0.2f, 1f);
        [SerializeField] private Sprite _panelSprite;
        [SerializeField] private InventoryIconSet _icons;
        [SerializeField] private InventorySoundSet _sounds;
        [SerializeField] private Sprite _contextMenuFrame;
        [SerializeField] private Sprite _contextMenuHighlight;
        [SerializeField] private Sprite _contextMenuDivider;
        [SerializeField] private Color _panelColor = Color.white;
        [SerializeField] private Vector4 _panelPadding = new(18f, 18f, 18f, 18f);
        [SerializeField] private bool _showItemBackground = true;
        [SerializeField] private bool _showItemWeight;
        [SerializeField] private bool _showItemCount = true;
        [SerializeField] private bool _itemTextShadow;
        [SerializeField] private string _countFormat = "x{0}";
        [SerializeField] private string _weightUnit = "kg";
        [SerializeField] [Min(4f)] private float _itemTextSize = 15f;
        [SerializeField] [Min(4f)] private float _itemNameSize = 18f;
        [SerializeField] [Min(4f)] private float _itemWeightSize = 16f;
        [SerializeField] private Color _itemTextColor = new(1f, 1f, 1f, 0.9f);
        [SerializeField] private Color _itemShadowColor = new(0f, 0f, 0f, 0.85f);
        [SerializeField] private Color _conditionColor = new(0.39f, 0.59f, 1f, 1f);
        [SerializeField] private Color _itemWeightColor = new(240f / 255f, 217f / 255f, 182f / 255f, 1f);
        [SerializeField] private Color _itemInfoBackground = new(0f, 0f, 0f, 0f);
        [SerializeField] private List<string> _shortNameCategories = new();
        [SerializeField] private List<InventoryThemeLabel> _labels = new();

        public TMP_FontAsset BodyFont => _bodyFont;
        public TMP_FontAsset HeaderFont => _headerFont != null ? _headerFont : _bodyFont;
        public TMP_FontAsset AccentFont => _accentFont != null ? _accentFont : _bodyFont;
        public TMP_FontAsset SmallFont => _smallFont != null ? _smallFont : _bodyFont;
        public Color TextColor => _textColor;
        public Color MutedTextColor => _mutedTextColor;
        public Color AccentColor => _accentColor;
        public Color NegativeColor => _negativeColor;
        public Sprite PanelSprite => _panelSprite;
        public InventoryIconSet Icons => _icons;
        public InventorySoundSet Sounds => _sounds;
        public Sprite ContextMenuFrame => _contextMenuFrame;
        public Sprite ContextMenuHighlight => _contextMenuHighlight;
        public Sprite ContextMenuDivider => _contextMenuDivider;
        public Color PanelColor => _panelColor;
        public Vector4 PanelPadding => _panelPadding;
        public bool ShowItemBackground => _showItemBackground;
        public bool ShowItemWeight => _showItemWeight;
        public bool ShowItemCount => _showItemCount;
        public bool ItemTextShadow => _itemTextShadow;
        public float ItemTextSize => _itemTextSize;
        public float ItemNameSize => _itemNameSize;
        public float ItemWeightSize => _itemWeightSize;
        public Color ItemTextColor => _itemTextColor;
        public Color ItemShadowColor => _itemShadowColor;
        public Color ConditionColor => _conditionColor;
        public Color ItemWeightColor => _itemWeightColor;
        public Color ItemInfoBackground => _itemInfoBackground;

        public static string Resolve(InventoryTheme theme, string key, string fallback) => theme != null ? theme.Label(key, fallback) : fallback;

        public Sprite Icon(string id) => _icons != null ? _icons.Get(id) : null;

        public string Label(string key, string fallback)
        {
            if (_labels != null)
            {
                for (int i = 0; i < _labels.Count; i++)
                {
                    if (_labels[i] != null && string.Equals(_labels[i].Key, key, StringComparison.Ordinal))
                    {
                        return _labels[i].Text;
                    }
                }
            }

            return fallback;
        }

        public string FormatCount(int amount) => string.Format(CultureInfo.InvariantCulture, string.IsNullOrEmpty(_countFormat) ? "{0}" : _countFormat, amount);

        public string FormatWeight(float weight) => Math.Round(weight, 2).ToString("0.##", CultureInfo.InvariantCulture) + _weightUnit;

        public string FormatItemWeight(float weight) => "<size=" + _itemWeightSize.ToString("0.##", CultureInfo.InvariantCulture) + "><color=#" + ColorUtility.ToHtmlStringRGBA(_itemWeightColor) + ">" + FormatWeight(weight) + "</color></size>";

        public bool ShowsShortName(ItemDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.ShortName))
            {
                return false;
            }

            if (_shortNameCategories == null || _shortNameCategories.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < _shortNameCategories.Count; i++)
            {
                if (definition.IsCategory(_shortNameCategories[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public void ApplyTo(Transform root, bool panelBackground)
        {
            if (root == null)
            {
                return;
            }

            if (_bodyFont != null)
            {
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.font != _bodyFont && text.font != HeaderFont && text.font != AccentFont && text.font != SmallFont)
                    {
                        text.font = _bodyFont;
                    }
                }
            }

            Transform panel = root.Find("Panel");
            Transform target = panel != null ? panel : root;

            if (panelBackground && _panelSprite != null && target.TryGetComponent(out Image image))
            {
                image.sprite = _panelSprite;
                image.type = Image.Type.Sliced;
                image.color = _panelColor;
            }
        }
    }
}
