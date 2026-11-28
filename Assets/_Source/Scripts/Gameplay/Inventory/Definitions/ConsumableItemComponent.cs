using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    [Serializable]
    public sealed class ConsumableItemComponent : ItemDefinitionComponent
    {
        [SerializeField] [Min(1)] private int _amountPerUse = 1;

        public int AmountPerUse => Mathf.Max(1, _amountPerUse);
    }
}
