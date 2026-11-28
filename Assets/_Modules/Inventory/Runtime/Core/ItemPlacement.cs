using UnityEngine;

namespace Inventory
{
    public readonly struct ItemPlacement
    {
        public ItemPlacement(Vector2Int position, RectInt area, bool rotated, string slotId)
        {
            Position = position;
            Area = area;
            Rotated = rotated;
            SlotId = slotId ?? string.Empty;
        }

        public Vector2Int Position { get; }
        public RectInt Area { get; }
        public bool Rotated { get; }
        public string SlotId { get; }
    }
}
