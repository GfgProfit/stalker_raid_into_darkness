using UnityEngine;

namespace Inventory.UI
{
    public readonly struct InventoryItemDecoration
    {
        public InventoryItemDecoration(Color tint, Sprite badge, Sprite communityIcon = null, Vector2 communityIconSize = default)
        {
            Tint = tint;
            Badge = badge;
            CommunityIcon = communityIcon;
            CommunityIconSize = communityIconSize;
        }

        public Color Tint { get; }
        public Sprite Badge { get; }
        public Sprite CommunityIcon { get; }
        public Vector2 CommunityIconSize { get; }
        public bool HasTint => Tint.a > 0f;

        public static InventoryItemDecoration None => default;
    }
}
