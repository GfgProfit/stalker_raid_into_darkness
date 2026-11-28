using RaidIntoDarkness.Inventory.Domain;
using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    [Serializable]
    public sealed class WorldItemComponent : ItemDefinitionComponent
    {
        [SerializeField] private WorldItem _prefab;

        public WorldItem Prefab => _prefab;
    }
}
