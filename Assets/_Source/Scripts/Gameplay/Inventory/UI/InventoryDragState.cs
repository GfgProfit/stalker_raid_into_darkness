using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.Application;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class InventoryDragState
    {
        public ItemInstance Item { get; internal set; }
        public InventoryGridViewBase SourceGrid { get; internal set; }
        public InventoryItemView SourceView { get; internal set; }
        public InventoryItemDragGhost Ghost { get; internal set; }
        public bool Rotated { get; internal set; }
        public int Amount { get; internal set; }
        public bool IsSplit { get; internal set; }
        public Vector2 LastScreenPosition { get; internal set; }
        public Camera EventCamera { get; internal set; }
        public InventoryGridViewBase TargetGrid { get; internal set; }
        public Vector2Int TargetOrigin { get; internal set; }
        public InventoryDropKind Kind { get; internal set; }
        public InventoryFailure Failure { get; internal set; }
        public bool AutoPlace { get; internal set; }
        public ItemInstance TargetItem { get; internal set; }
    }
}
