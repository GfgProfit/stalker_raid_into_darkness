using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    [CreateAssetMenu(menuName = "Inventory/Item Database", fileName = "ItemDatabase")]
    public sealed class ItemDatabase : ScriptableObject, IItemDefinitionResolver
    {
        [SerializeField] private List<ItemDefinition> _definitions = new();

        private Dictionary<string, ItemDefinition> _lookup;

        public IReadOnlyList<ItemDefinition> Definitions => _definitions;

        public bool TryResolve(string definitionId, out ItemDefinition definition)
        {
            EnsureLookup();
            definition = null;
            return string.IsNullOrEmpty(definitionId) == false && _lookup.TryGetValue(definitionId, out definition);
        }

        public void SetDefinitions(IEnumerable<ItemDefinition> definitions)
        {
            _definitions = new List<ItemDefinition>(definitions);
            _lookup = null;
        }

        private void OnValidate() => _lookup = null;

        private void EnsureLookup()
        {
            if (_lookup != null)
            {
                return;
            }

            _lookup = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

            for (int i = 0; i < _definitions.Count; i++)
            {
                ItemDefinition definition = _definitions[i];

                if (definition != null && string.IsNullOrEmpty(definition.DefinitionId) == false && _lookup.ContainsKey(definition.DefinitionId) == false)
                {
                    _lookup.Add(definition.DefinitionId, definition);
                }
            }
        }
    }
}
