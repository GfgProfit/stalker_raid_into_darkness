using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    public readonly struct ItemLocation : IEquatable<ItemLocation>
    {
        public ItemLocation(string containerId, Vector2Int position, bool rotated, SlotId slotId)
        {
            ContainerId = containerId ?? string.Empty;
            Position = position;
            Rotated = rotated;
            SlotId = slotId;
        }

        public string ContainerId { get; }
        public Vector2Int Position { get; }
        public bool Rotated { get; }
        public SlotId SlotId { get; }
        public bool IsPlaced => string.IsNullOrEmpty(ContainerId) == false;

        public static ItemLocation None => new(string.Empty, Vector2Int.zero, false, SlotId.None);

        public bool Equals(ItemLocation other) => ContainerId == other.ContainerId && Position == other.Position && Rotated == other.Rotated && SlotId == other.SlotId;
        public override bool Equals(object obj) => obj is ItemLocation other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ContainerId, Position, Rotated, SlotId);
        public override string ToString() => IsPlaced ? $"{ContainerId}{(SlotId != SlotId.None ? "/" + SlotId : string.Empty)} {Position}{(Rotated ? " R" : string.Empty)}" : "none";
    }
}
