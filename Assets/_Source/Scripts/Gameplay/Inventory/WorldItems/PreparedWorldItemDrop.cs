using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    internal readonly struct PreparedWorldItemDrop
    {
        public PreparedWorldItemDrop(WorldItem worldItem, Collider[] colliders, Vector3 direction)
        {
            WorldItem = worldItem;
            Colliders = colliders;
            Direction = direction;
        }

        public WorldItem WorldItem { get; }
        public Collider[] Colliders { get; }
        public Vector3 Direction { get; }
    }
}
