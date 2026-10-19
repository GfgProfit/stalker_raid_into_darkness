using System;
using Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [Serializable]
    public sealed class WorldItemComponent : ItemDefinitionComponent
    {
        [SerializeField] private WorldItem _prefab;

        public WorldItem Prefab => _prefab;
    }
}
