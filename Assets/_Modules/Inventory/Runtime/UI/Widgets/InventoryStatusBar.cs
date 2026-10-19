using System;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory.UI
{
    [Serializable]
    public sealed class InventoryStatusBar
    {
        private const float BONUS_HEIGHT = 2f / 3f;

        [SerializeField] private string _id;
        [SerializeField] private Image _fill;
        [SerializeField] private Image _icon;
        [SerializeField] private Gradient _colorByValue;

        private Image _bonus;

        public InventoryStatusBar()
        {
        }

        public InventoryStatusBar(string id, Image fill, Image icon, Gradient colorByValue)
        {
            _id = id;
            _fill = fill;
            _icon = icon;
            _colorByValue = colorByValue;
        }

        public string Id => _id ?? string.Empty;
        public Image Fill => _fill;
        public Image Icon => _icon;

        public void Apply(float value)
        {
            if (_fill == null)
            {
                return;
            }

            float clamped = Mathf.Clamp01(value);
            _fill.fillAmount = clamped;

            if (_colorByValue != null)
            {
                _fill.color = _colorByValue.Evaluate(clamped);
            }
        }

        public void ApplyBonus(float value, Color color)
        {
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
            if (_bonus != null)
            {
                _bonus.enabled = false;
            }
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
