using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class InventoryItemDragGhost : MonoBehaviour
    {
        private ItemInstance _item;
        private RectTransform _content;
        private Image _icon;
        private InventoryItemIconBinding _iconBinding;

        public RectTransform RectTransform => (RectTransform)transform;

        internal static InventoryItemDragGhost Create(RectTransform parent, ItemInstance item, Vector2 cellSize, bool rotated, InventoryTheme theme, IItemIconProvider iconProvider)
        {
            RectTransform rect = InventoryUIFactory.CreateRect("Dragged Item", parent);
            rect.gameObject.hideFlags = HideFlags.DontSave;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            CanvasGroup canvas = rect.gameObject.AddComponent<CanvasGroup>();
            canvas.alpha = 0.85f;
            canvas.blocksRaycasts = false;
            InventoryItemDragGhost ghost = rect.gameObject.AddComponent<InventoryItemDragGhost>();
            ghost._item = item;
            ghost._content = InventoryUIFactory.CreateRect("Content", rect);
            ghost._content.gameObject.hideFlags = HideFlags.DontSave;
            ghost._content.anchorMin = ghost._content.anchorMax = ghost._content.pivot = new Vector2(0.5f, 0.5f);
            InventoryUIFactory.AddPanel(ghost._content.gameObject, theme != null && theme.ShowItemBackground == false ? Color.clear : item.Definition.BackgroundColor, false);
            RectTransform iconRect = InventoryUIFactory.Stretch(InventoryUIFactory.CreateRect("Icon", ghost._content));
            iconRect.gameObject.hideFlags = HideFlags.DontSave;
            ghost._icon = iconRect.gameObject.AddComponent<Image>();
            ghost._icon.raycastTarget = false;
            ghost._icon.preserveAspect = true;
            ghost.SetRotation(rotated, cellSize);
            ghost._iconBinding = new InventoryItemIconBinding(ghost.DisplayIcon);
            ghost._iconBinding.Request(ItemIconRequest.ForItem(item), iconProvider, false);
            return ghost;
        }

        internal void SetRotation(bool rotated, Vector2 cellSize)
        {
            Vector2Int size = _item.GetSize(rotated);
            Vector2Int baseSize = _item.GetSize(false);
            RectTransform.sizeDelta = new Vector2(size.x * cellSize.x, size.y * cellSize.y);
            _content.sizeDelta = new Vector2(baseSize.x * cellSize.x, baseSize.y * cellSize.y);
            _content.anchoredPosition = Vector2.zero;
            _content.localRotation = Quaternion.Euler(0f, 0f, rotated ? -90f : 0f);
        }

        private void OnDisable() => _iconBinding?.Cancel();

        private void OnDestroy() => _iconBinding?.Reset();

        private void DisplayIcon(Sprite sprite)
        {
            _icon.sprite = sprite;
            _icon.enabled = sprite != null;
        }
    }
}
