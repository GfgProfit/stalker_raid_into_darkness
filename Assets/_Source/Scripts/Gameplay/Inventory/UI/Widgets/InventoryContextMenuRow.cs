using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RaidIntoDarkness.Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryContextMenuRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public event Action<InventoryContextMenuRow> Entered;
        public event Action<InventoryContextMenuRow> Exited;
        public event Action<InventoryContextMenuRow> Clicked;

        public int Index { get; set; } = -1;

        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke(this);

        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke(this);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                Clicked?.Invoke(this);
            }
        }
    }
}
