using UnityEngine;
using UnityEngine.EventSystems;

namespace RaidIntoDarkness.Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryStatusHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        private InventoryStatusPanel _panel;
        private string _id;

        public void Configure(InventoryStatusPanel panel, string id)
        {
            _panel = panel;
            _id = id;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_panel != null && eventData != null)
            {
                _panel.HandlePointerEnter(_id, eventData.position, eventData.enterEventCamera);
            }
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (_panel != null && eventData != null)
            {
                _panel.HandlePointerMove(eventData.position, eventData.enterEventCamera);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_panel != null)
            {
                _panel.HandlePointerExit(_id);
            }
        }

        private void OnDisable()
        {
            if (_panel != null)
            {
                _panel.HandlePointerExit(_id);
            }
        }
    }
}
