using System;
using RaidIntoDarkness.Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    [Serializable]
    public sealed class ItemCostComponent : ItemDefinitionComponent
    {
        [SerializeField] [Min(0)] private int _cost;

        public int Cost => _cost;

        internal static int? GetBaseCost(ItemDefinition definition)
        {
            return definition.GetComponent<ItemCostComponent>()?.Cost ?? definition.GetComponent<WeaponStatsComponent>()?.Cost ?? definition.GetComponent<EquipmentStatsComponent>()?.Cost ?? definition.GetComponent<AmmoStatsComponent>()?.Cost ?? definition.GetComponent<WeaponAddonComponent>()?.Cost;
        }
    }
}
