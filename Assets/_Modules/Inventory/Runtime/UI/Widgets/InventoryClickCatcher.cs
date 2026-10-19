using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public Action Clicked { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.pointerPressRaycast.gameObject == gameObject)
            {
                Clicked?.Invoke();
            }
        }
    }
}
