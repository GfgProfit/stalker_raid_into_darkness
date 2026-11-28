using UnityEngine;

namespace RaidIntoDarkness.Inventory.Application
{
    public readonly struct InventoryUnequipTarget
    {
        public InventoryUnequipTarget(string containerId, Vector2Int origin, bool rotated, bool autoPlace)
        {
            ContainerId = containerId;
            Origin = origin;
            Rotated = rotated;
            AutoPlace = autoPlace;
        }

        public string ContainerId { get; }
        public Vector2Int Origin { get; }
        public bool Rotated { get; }
        public bool AutoPlace { get; }

        public static InventoryUnequipTarget Auto(string containerId) => new(containerId, Vector2Int.zero, false, true);
    }
}
