using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    [Serializable]
    public sealed class InventorySlotVisual
    {
        [SerializeField] private SlotId _slotId;
        [SerializeField] private bool _overrideRect;
        [SerializeField] private Rect _rect;
        [SerializeField] private InventorySlotLabelAnchor _labelAnchor = InventorySlotLabelAnchor.Center;
        [SerializeField] private string _label;
        [SerializeField] private Sprite _highlightSprite;
        [SerializeField] private Sprite _lockedSprite;
        [SerializeField] private Rect _lockedRect;

        public InventorySlotVisual()
        {
        }

        public InventorySlotVisual(SlotId slotId, Rect rect, InventorySlotLabelAnchor labelAnchor, string label = null, Sprite highlightSprite = null, Sprite lockedSprite = null, Rect lockedRect = default)
        {
            _slotId = slotId;
            _overrideRect = true;
            _rect = rect;
            _labelAnchor = labelAnchor;
            _label = label;
            _highlightSprite = highlightSprite;
            _lockedSprite = lockedSprite;
            _lockedRect = lockedRect;
        }

        public SlotId SlotId => _slotId;
        public bool OverrideRect => _overrideRect;
        public Rect Rect => _rect;
        public InventorySlotLabelAnchor LabelAnchor => _labelAnchor;
        public string Label => _label;
        public Sprite HighlightSprite => _highlightSprite;
        public Sprite LockedSprite => _lockedSprite;
        public Rect LockedRect => _lockedRect;
    }
}
