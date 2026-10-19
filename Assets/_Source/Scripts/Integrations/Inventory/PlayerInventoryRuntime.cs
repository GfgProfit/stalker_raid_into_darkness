using System.Collections.Generic;
using Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [DisallowMultipleComponent]
    public sealed class PlayerInventoryRuntime : MonoBehaviour
    {
        [SerializeField] private ItemDatabase _database;
        [SerializeField] private InventorySeed _seed;

        private InventoryModel _model;
        private bool _seeded;

        public ItemDatabase Database => _database;
        public InventorySeed Seed => _seed;
        public bool IsSeeded => _seeded;

        public InventoryModel Model
        {
            get
            {
                if (_model == null)
                {
                    _model = new InventoryModel(_database);
                }

                return _model;
            }
        }

        public void SeedOnce()
        {
            if (_seeded || _seed == null)
            {
                return;
            }

            _seeded = true;
            _seed.Apply(Model);
        }

        public IReadOnlyList<ItemDefinition> CollectDefinitionsInUse()
        {
            var definitions = new List<ItemDefinition>();

            foreach (ItemInstance item in Model.Items)
            {
                if (definitions.Contains(item.Definition) == false)
                {
                    definitions.Add(item.Definition);
                }
            }

            return definitions;
        }

        public string SaveJson() => InventorySerializer.ToJson(Model);

        public InventoryLoadReport LoadJson(string json) => InventorySerializer.FromJson(Model, json);
    }
}
