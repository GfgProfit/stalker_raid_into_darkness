using System;
using System.Collections.Generic;
using Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
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

    [CreateAssetMenu(menuName = "Raid Into Darkness/Inventory/Inventory Seed", fileName = "InventorySeed")]
    public sealed class InventorySeed : ScriptableObject
    {
        [SerializeField] private List<InventorySeedEntry> _entries = new();

        public IReadOnlyList<InventorySeedEntry> Entries => _entries;

        public void SetEntries(IEnumerable<InventorySeedEntry> entries) => _entries = new List<InventorySeedEntry>(entries);

        public int Apply(InventoryModel model)
        {
            int added = 0;

            foreach (InventorySeedEntry entry in _entries)
            {
                if (entry.Definition == null)
                {
                    continue;
                }

                ItemInstance item = model.CreateItem(entry.Definition, entry.Amount);
                var variant = new List<string>();

                foreach (ItemDefinition part in entry.VisualVariant)
                {
                    if (part != null)
                    {
                        variant.Add(part.DefinitionId);
                    }
                }

                if (variant.Count > 0)
                {
                    item.SetVisualVariant(variant);
                }

                if (entry.Condition < 1f && !entry.Definition.IsCategory("Grenade"))
                {
                    item.SetState(EquipmentStatsFormatter.CONDITION_STATE, entry.Condition.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                InventoryResult result = entry.AutoPlace ? model.TryAddAuto(item, entry.ContainerId, false) : model.TryAdd(item, entry.ContainerId, entry.Position, entry.Rotated);

                if (result.Success)
                {
                    added++;
                }
                else
                {
                    Debug.LogWarning($"Inventory seed could not place {entry.Definition.DisplayName} x{entry.Amount} into '{entry.ContainerId}' at {entry.Position}: {result.Failure}.", this);
                }
            }

            return added;
        }
    }
}
