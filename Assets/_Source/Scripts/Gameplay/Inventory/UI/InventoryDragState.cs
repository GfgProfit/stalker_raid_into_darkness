using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.Application;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class InventoryDragState
    {
        public ItemInstance Item;
        public InventoryGridViewBase SourceGrid;
        public InventoryItemView SourceView;
        public InventoryItemDragGhost Ghost { get; internal set; }
        public bool Rotated;
        public int Amount;
        public bool IsSplit;
        public Vector2 LastScreenPosition;
        public Camera EventCamera;
        public InventoryGridViewBase TargetGrid;
        public Vector2Int TargetOrigin;
        public InventoryDropKind Kind;
        public InventoryFailure Failure;
        public bool AutoPlace;

        public ItemInstance TargetItem { get; set; }
    }
}
