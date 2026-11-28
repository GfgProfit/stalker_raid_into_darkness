using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.UI;
using RaidIntoDarkness.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RaidIntoDarkness.UI
{
    public sealed class SpawnMenuItemCell : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private InventoryItemView _itemView;
        [SerializeField] private Image _selection;

        private SpawnMenuView _owner;
        private SpawnMenuEntry _entry;
        private SpawnMenuEntry _pressed;

        internal void Bind(SpawnMenuView owner, SpawnMenuEntry entry, Rect area, InventoryWindow inventoryWindow, bool selected)
        {
            _owner = owner;
            _entry = entry;
            _pressed = null;
            var rect = (RectTransform)transform;
            rect.anchoredPosition = new Vector2(area.x, -area.y);
            rect.sizeDelta = area.size;
            ItemDefinition definition = entry.Definition;
            string shortName = InventoryWindowController.FormatAmmoShortName(definition);
            if (shortName == null)
            {
                shortName = string.IsNullOrEmpty(definition.ShortName) ? definition.DisplayName : definition.ShortName;
            }
            InventoryItemDecoration decoration = InventoryWindowController.AddCommunityIcon(definition, InventoryItemDecoration.None);
            _itemView.BindCatalogItem(definition, inventoryWindow, area.size, shortName, decoration);
            _selection.enabled = selected;
        }

        internal void SetSelected(SpawnMenuEntry selected) => _selection.enabled = _entry == selected;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                _pressed = _entry;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || _entry == null || _pressed != _entry)
            {
                return;
            }

            _pressed = null;
            _owner.ClickItem(_entry, eventData);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_owner != null)
            {
                _owner.HoverItem(this, _entry, eventData);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_owner != null)
            {
                _owner.LeaveItem(this);
            }
        }

        private void OnDisable()
        {
            _pressed = null;
            if (_owner != null)
            {
                _owner.LeaveItem(this);
            }
        }
    }
}
