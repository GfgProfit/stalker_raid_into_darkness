using System;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory.UI
{
    [Serializable]
    public sealed class InventoryStatusBar
    {
        [SerializeField] private string _id;
        [SerializeField] private Image _fill;
        [SerializeField] private Image _icon;
        [SerializeField] private Gradient _colorByValue;

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
    }
}
