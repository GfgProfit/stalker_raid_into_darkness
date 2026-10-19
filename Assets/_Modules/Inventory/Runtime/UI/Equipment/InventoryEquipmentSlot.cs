using UnityEngine;
using UnityEngine.UI;

namespace Inventory.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Inventory/Inventory Equipment Slot")]
    public sealed class InventoryEquipmentSlot : MonoBehaviour
    {
        [SerializeField] private string _slotId;
        [SerializeField] private Image _icon;

        public string SlotId => _slotId ?? string.Empty;
        public Image Icon => _icon;
        public RectTransform RectTransform => (RectTransform)transform;
        public RectTransform IconRect => _icon != null ? _icon.rectTransform : RectTransform;

        public void Configure(string slotId, Image icon)
        {
            _slotId = slotId;
            _icon = icon;
        }

        public void ClearIcon()
        {
            if (_icon == null)
            {
                return;
            }

            _icon.sprite = null;
            _icon.canvasRenderer.SetAlpha(1f);
            _icon.gameObject.SetActive(false);
        }
    }
}
