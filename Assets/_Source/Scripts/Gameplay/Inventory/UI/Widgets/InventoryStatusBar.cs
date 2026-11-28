using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    [Serializable]
    public sealed class InventoryStatusBar
    {
        private const float BONUS_HEIGHT = 2f / 3f;

        [SerializeField] private string _id;
        [SerializeField] private Image _fill;
        [SerializeField] private Image _icon;
        [SerializeField] private Gradient _colorByValue;
        [SerializeField] private TMP_Text _text;

        private Image _bonus;
        private float _value;
        private float _maximum = 1f;
        private bool _hasBonus;
        private int _displayedValue = -1;
        private int _displayedMaximum = -1;
        private Color? _displayedColor;

        public InventoryStatusBar()
        {
        }

        public InventoryStatusBar(string id, Image fill, Image icon, Gradient colorByValue, TMP_Text text = null)
        {
            _id = id;
            _fill = fill;
            _icon = icon;
            _colorByValue = colorByValue;
            _text = text;
        }

        public string Id => _id ?? string.Empty;
        public Image Fill => _fill;
        public Image Icon => _icon;
        public TMP_Text Text => _text;

        public void Apply(float value, float maximum = 1f)
        {
            _value = Mathf.Clamp01(value);
            _maximum = Mathf.Clamp01(maximum);
            if (_hasBonus == false)
            {
                ApplyText(_value, _maximum);
            }
            if (_fill == null)
            {
                return;
            }

            float clamped = _value;
            _fill.fillAmount = clamped;

            if (_colorByValue != null)
            {
                _fill.color = _colorByValue.Evaluate(clamped);
            }
        }

        public void ApplyBonus(float value, Color color)
        {
            if (_text != null)
            {
                ApplyText(Mathf.Clamp01(value), _maximum, color);
                _hasBonus = true;
                return;
            }
            Image bonus = EnsureBonus();

            if (bonus == null)
            {
                return;
            }

            bonus.fillAmount = Mathf.Clamp01(value);
            bonus.color = color;
            bonus.enabled = true;
        }

        public void ClearBonus()
        {
            if (_hasBonus)
            {
                _hasBonus = false;
                ApplyText(_value, _maximum);
            }
            if (_bonus != null)
            {
                _bonus.enabled = false;
            }
        }

        private void ApplyText(float value, float maximum, Color? color = null)
        {
            if (_text == null)
            {
                return;
            }
            int currentPercent = Mathf.RoundToInt(value * 100f);
            int maximumPercent = Mathf.RoundToInt(maximum * 100f);
            if (_displayedValue == currentPercent && _displayedMaximum == maximumPercent && _displayedColor == color)
            {
                return;
            }
            _displayedValue = currentPercent;
            _displayedMaximum = maximumPercent;
            _displayedColor = color;
            string format = color.HasValue ? "<color=#" + ColorUtility.ToHtmlStringRGBA(color.Value) + ">{0}</color> / {1} %" : "{0} / {1} %";
            _text.SetText(format, (float)currentPercent, maximumPercent);
        }

        private Image EnsureBonus()
        {
            if (_bonus != null || _fill == null)
            {
                return _bonus;
            }

            RectTransform rect = InventoryUIFactory.CreateRect(_fill.name + "Bonus", _fill.transform);
            InventoryUIFactory.SetLayout(rect, Vector2.zero, new Vector2(1f, BONUS_HEIGHT), Vector2.zero, Vector2.zero);
            _bonus = rect.gameObject.AddComponent<Image>();
            _bonus.sprite = _fill.sprite;
            _bonus.type = Image.Type.Filled;
            _bonus.fillMethod = Image.FillMethod.Horizontal;
            _bonus.fillOrigin = (int)Image.OriginHorizontal.Left;
            _bonus.raycastTarget = false;
            _bonus.enabled = false;
            return _bonus;
        }
    }
}
