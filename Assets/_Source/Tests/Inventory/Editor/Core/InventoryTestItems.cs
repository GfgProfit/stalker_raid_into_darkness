using RaidIntoDarkness.Inventory.Domain;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Core.Tests
{
    internal sealed class InventoryTestItems
    {
        private readonly List<Object> _created = new();
        private readonly ItemDatabase _database;

        public InventoryTestItems()
        {
            _database = ScriptableObject.CreateInstance<ItemDatabase>();
            _created.Add(_database);
        }

        public ItemDatabase Database => _database;

        public ItemDefinition Create(string id, Vector2Int size, int maxStack = 1, float weight = 0f, string category = "Misc", bool allowRotation = true, params string[] tags)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            definition.name = id;
            definition.Configure(id, category, size, weight, maxStack, allowRotation, tags);
            _created.Add(definition);
            var definitions = new List<ItemDefinition>(_database.Definitions) { definition };
            _database.SetDefinitions(definitions);
            return definition;
        }

        public T Track<T>(T target) where T : Object
        {
            _created.Add(target);
            return target;
        }

        public void Dispose()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }
    }
}
