using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory.UI
{
    [AddComponentMenu("Inventory/Inventory Equipment Panel")]
    public sealed class InventoryEquipmentPanel : InventoryGridViewBase
    {
        [SerializeField] private InventorySlotLayout _layout;
        [SerializeField] private List<InventoryEquipmentSlot> _slotViews = new();

        private readonly List<SlotDefinition> _slots = new();
        private readonly Dictionary<string, InventoryEquipmentSlot> _viewsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Rect> _slotRects = new(StringComparer.Ordinal);
        private readonly Vector3[] _corners = new Vector3[4];
        private int _gridWidth;
        private int _gridHeight;
        private string _layoutWarning = string.Empty;

        public InventorySlotLayout Layout => _layout;
        public IReadOnlyList<InventoryEquipmentSlot> SlotViews => _slotViews;
        public IReadOnlyList<SlotDefinition> Slots => _slots;
        public override int Columns => _gridWidth;
        public override int Rows => _gridHeight;
        public override Vector2 ContentSize => RectTransform.rect.size;
        public override string LayoutWarning => _layoutWarning;

        public void Configure(InventorySlotLayout layout, IEnumerable<InventoryEquipmentSlot> slotViews)
        {
            _layout = layout;
            _slotViews = slotViews == null ? new List<InventoryEquipmentSlot>() : new List<InventoryEquipmentSlot>(slotViews);
            Rebuild();
        }

        public InventoryEquipmentSlot GetSlotView(string slotId) => slotId != null && _viewsById.TryGetValue(slotId, out InventoryEquipmentSlot view) ? view : null;

        public override InventoryContainer CreateContainer()
        {
            ComputeLayout();
            return new SlottedContainer(ContainerId, _slots, Mathf.Max(1, _gridWidth), Mathf.Max(1, _gridHeight), true, OwnerId, DisplayName);
        }

        public override bool TryGetCellAtLocal(Vector2 topLeftLocal, out Vector2Int cell)
        {
            SlotDefinition slot = GetSlotAtVisualPosition(topLeftLocal);
            cell = slot != null ? slot.Bounds.position : new Vector2Int(-1, -1);
            return slot != null;
        }

        public override Vector2Int GetPlacementOrigin(Vector2 topLeftPointer, Vector2Int itemSize)
        {
            SlotDefinition slot = GetSlotAtVisualPosition(topLeftPointer);
            return slot != null ? slot.Bounds.position : new Vector2Int(-1, -1);
        }

        public override Rect GetAreaRect(RectInt area)
        {
            foreach (SlotDefinition slot in _slots)
            {
                if (slot.Contains(area.position) && _slotRects.TryGetValue(slot.Id, out Rect rect))
                {
                    return rect;
                }
            }

            return default;
        }

        public override bool UsesSlotIcon(ItemInstance item, out Vector2Int slotSize)
        {
            slotSize = Vector2Int.zero;
            SlotDefinition slot = item != null && Container is SlottedContainer slotted ? slotted.GetSlot(item.Location.SlotId) : null;

            if (slot == null)
            {
                return false;
            }

            slotSize = slot.Bounds.size;
            return true;
        }

        public override Rect GetIconRect(ItemInstance item, Rect area)
        {
            InventoryEquipmentSlot view = item != null ? GetSlotView(item.Location.SlotId) : null;
            return view != null ? ToTopLeftRect(view.IconRect) : area;
        }

        protected override void ComputeLayout()
        {
            _slots.Clear();
            _viewsById.Clear();
            _slotRects.Clear();
            _layoutWarning = string.Empty;

            foreach (InventoryEquipmentSlot view in _slotViews)
            {
                if (view != null && view.SlotId.Length > 0)
                {
                    _viewsById[view.SlotId] = view;
                }
            }

            if (_layout == null)
            {
                _layoutWarning = "Slot layout is not assigned.";
                _gridWidth = 1;
                _gridHeight = 1;
                return;
            }

            if (_layout.TryBuildSlots(out List<SlotDefinition> slots, out _gridWidth, out _gridHeight, out string error) == false)
            {
                _layoutWarning = error;
                _gridWidth = 1;
                _gridHeight = 1;
                return;
            }

            var defined = new HashSet<string>(StringComparer.Ordinal);

            foreach (SlotDefinition slot in slots)
            {
                defined.Add(slot.Id);

                if (_viewsById.TryGetValue(slot.Id, out InventoryEquipmentSlot view))
                {
                    _slots.Add(slot);
                    _slotRects[slot.Id] = ToTopLeftRect(view.RectTransform);
                }
            }

            foreach (string slotId in _viewsById.Keys)
            {
                if (defined.Contains(slotId) == false)
                {
                    _layoutWarning += $"Slot '{slotId}' is not defined in {_layout.name}. ";
                }
            }
        }

        protected override void ApplySize()
        {
        }

        protected override void DrawGrid(InventoryGridGraphic graphic)
        {
        }

        protected override void OnItemViewLayout(InventoryItemView view)
        {
            InventoryEquipmentSlot slot = GetSlotView(view.Item.Location.SlotId);
            view.SetIconTarget(slot != null ? slot.Icon : null, slot != null ? slot.GetHighlight() : null);
        }

        protected override void OnItemsSynced()
        {
            if (Application.isPlaying == false)
            {
                return;
            }

            SlottedContainer slotted = Container as SlottedContainer;

            foreach (InventoryEquipmentSlot slot in _viewsById.Values)
            {
                bool occupied = slotted != null && slotted.GetSlotOccupant(slot.SlotId) != null;

                if (occupied == false)
                {
                    slot.ClearIcon();
                }

                slot.SetLocked(slotted != null && occupied == false && slotted.GetSlot(slot.SlotId) != null && slotted.IsSlotAvailable(slot.SlotId) == false);
            }
        }

        private SlotDefinition GetSlotAtVisualPosition(Vector2 topLeftLocal)
        {
            foreach (SlotDefinition slot in _slots)
            {
                if (_slotRects.TryGetValue(slot.Id, out Rect rect) && rect.Contains(topLeftLocal))
                {
                    return slot;
                }
            }

            return null;
        }

        private Rect ToTopLeftRect(RectTransform target)
        {
            target.GetWorldCorners(_corners);
            Vector3 min = RectTransform.InverseTransformPoint(_corners[0]);
            Vector3 max = RectTransform.InverseTransformPoint(_corners[2]);
            Rect rect = RectTransform.rect;
            return Rect.MinMaxRect(min.x - rect.xMin, rect.yMax - max.y, max.x - rect.xMin, rect.yMax - min.y);
        }
    }
}
