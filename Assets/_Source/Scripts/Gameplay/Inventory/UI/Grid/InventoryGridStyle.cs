using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    [Serializable]
    public sealed class InventoryGridStyle
    {
        [SerializeField] private Color _gridBackground = new(0.05f, 0.055f, 0.06f, 0.92f);
        [SerializeField] private Color _cellColor = new(1f, 1f, 1f, 0.025f);
        [SerializeField] private bool _showGrid = true;
        [SerializeField] private Color _gridLineColor = new(1f, 1f, 1f, 0.11764706f);
        [SerializeField] [Min(0f)] private float _gridLineThickness = 1f;
        [SerializeField] private bool _showBorder = true;
        [SerializeField] private Color _outerBorderColor = new(0.745283f, 0.745283f, 0.745283f, 0.25f);
        [SerializeField] [Min(0f)] private float _outerBorderThickness = 2f;
        [SerializeField] private Color _highlightColor = new(1f, 1f, 1f, 0.12f);
        [SerializeField] private Color _validPlacementColor = new(0f, 1f, 0.35f, 0.35f);
        [SerializeField] private Color _invalidPlacementColor = new(1f, 0.15f, 0.15f, 0.35f);
        [SerializeField] private Color _mergePlacementColor = new(1f, 0.7f, 0.1f, 0.4f);
        [SerializeField] private Color _compatibleSlotColor = new(0f, 1f, 0.35f, 0.16f);
        [SerializeField] private Color _lockedSlotColor = new(0f, 0f, 0f, 0.55f);

        public Color GridBackground => _gridBackground;
        public Color CellColor => _cellColor;
        public bool ShowGrid => _showGrid;
        public Color GridLineColor => _gridLineColor;
        public float GridLineThickness => Mathf.Max(0f, _gridLineThickness);
        public bool ShowBorder => _showBorder;
        public Color OuterBorderColor => _outerBorderColor;
        public float OuterBorderThickness => Mathf.Max(0f, _outerBorderThickness);
        public Color HighlightColor => _highlightColor;
        public Color ValidPlacementColor => _validPlacementColor;
        public Color InvalidPlacementColor => _invalidPlacementColor;
        public Color MergePlacementColor => _mergePlacementColor;
        public Color CompatibleSlotColor => _compatibleSlotColor;
        public Color LockedSlotColor => _lockedSlotColor;
    }
}
