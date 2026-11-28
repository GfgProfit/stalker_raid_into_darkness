using RaidIntoDarkness.Inventory.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory
{
    public static class InventoryBackpackViewport
    {
        public static ScrollRect Ensure(InventoryGridView grid)
        {
            RectTransform content = grid.RectTransform;
            if (content.parent.TryGetComponent(out ScrollRect existing) && existing.content == content)
            {
                return existing;
            }

            var viewportObject = new GameObject("BackpackViewport", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
            viewportObject.layer = content.gameObject.layer;
            var viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(content.parent, false);
            viewport.SetSiblingIndex(content.GetSiblingIndex());
            viewport.anchorMin = content.anchorMin;
            viewport.anchorMax = content.anchorMax;
            viewport.pivot = content.pivot;
            viewport.sizeDelta = content.sizeDelta;
            viewport.anchoredPosition = content.anchoredPosition;

            content.SetParent(viewport, false);
            var topLeft = new Vector2(0f, 1f);
            content.anchorMin = topLeft;
            content.anchorMax = topLeft;
            content.pivot = topLeft;
            content.anchoredPosition = Vector2.zero;

            ScrollRect scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 35f;
            return scroll;
        }
    }
}
