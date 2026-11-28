using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    public readonly struct ItemPlacement
    {
        public ItemPlacement(Vector2Int position, RectInt area, bool rotated, SlotId slotId)
        {
            Position = position;
            Area = area;
            Rotated = rotated;
            SlotId = slotId;
        }

        public Vector2Int Position { get; }
        public RectInt Area { get; }
        public bool Rotated { get; }
        public SlotId SlotId { get; }
    }
}
