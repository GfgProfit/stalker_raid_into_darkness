using System;
using RaidIntoDarkness.Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    [Serializable]
    public sealed class WorldItemComponent : ItemDefinitionComponent
    {
        [SerializeField] private WorldItem _prefab;

        public WorldItem Prefab => _prefab;
    }
}
