using UnityEngine;
using UnityEngine.UI;

namespace Inventory.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Inventory/Inventory Equipment Slot")]
    public sealed class InventoryEquipmentSlot : MonoBehaviour
    {
        private const string LOCK_NAME = "Lock";

        [SerializeField] private string _slotId;
        [SerializeField] private Image _icon;
        [SerializeField] private Sprite _lockedSprite;

        private Image _lock;

        public string SlotId => _slotId ?? string.Empty;
        public Image Icon => _icon;
        public RectTransform RectTransform => (RectTransform)transform;
        public RectTransform IconRect => _icon != null ? _icon.rectTransform : RectTransform;
        public Sprite LockedSprite => _lockedSprite;
        public bool IsLockVisible => _lock != null && _lock.gameObject.activeSelf;

        public void Configure(string slotId, Image icon)
        {
            _slotId = slotId;
            _icon = icon;
        }

        public void SetLockedSprite(Sprite sprite)
        {
            _lockedSprite = sprite;

            if (_lock != null)
            {
                _lock.sprite = sprite;
            }
        }

        public void SetLocked(bool locked)
        {
            if (_lock == null)
            {
                if (locked == false || _lockedSprite == null)
                {
                    return;
                }

                RectTransform rect = InventoryUIFactory.CreateRect(LOCK_NAME, transform);
                InventoryUIFactory.SetLayout(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                _lock = rect.gameObject.AddComponent<Image>();
                _lock.sprite = _lockedSprite;
                _lock.preserveAspect = true;
                _lock.raycastTarget = false;
            }

            _lock.transform.SetAsLastSibling();
            _lock.gameObject.SetActive(locked);
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
