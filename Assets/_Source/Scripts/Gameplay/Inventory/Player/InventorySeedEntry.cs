using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Application
{
    [Serializable]
    public sealed class InventorySeedEntry
    {
        [SerializeField] private string _containerId;
        [SerializeField] private ItemDefinition _definition;
        [SerializeField] [Min(1)] private int _amount = 1;
        [SerializeField] private Vector2Int _position;
        [SerializeField] private bool _rotated;
        [SerializeField] private bool _autoPlace;
        [SerializeField] private List<ItemDefinition> _visualVariant = new();
        [SerializeField] [Range(0f, 1f)] private float _condition = 1f;

        public InventorySeedEntry()
        {
        }

        public InventorySeedEntry(string containerId, ItemDefinition definition, int amount, Vector2Int position, bool rotated, bool autoPlace, IEnumerable<ItemDefinition> visualVariant = null)
        {
            _containerId = containerId;
            _definition = definition;
            _amount = amount;
            _position = position;
            _rotated = rotated;
            _autoPlace = autoPlace;
            _visualVariant = visualVariant == null ? new List<ItemDefinition>() : new List<ItemDefinition>(visualVariant);
        }

        public string ContainerId => _containerId;
        public ItemDefinition Definition => _definition;
        public int Amount => Mathf.Max(1, _amount);
        public Vector2Int Position => _position;
        public bool Rotated => _rotated;
        public bool AutoPlace => _autoPlace;
        public IReadOnlyList<ItemDefinition> VisualVariant => _visualVariant;
        public float Condition => Mathf.Clamp01(_condition);
    }
}
