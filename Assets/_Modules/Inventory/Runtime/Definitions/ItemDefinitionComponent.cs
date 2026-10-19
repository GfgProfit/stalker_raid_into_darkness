using System;
using UnityEngine;

namespace Inventory
{
    [Serializable]
    public abstract class ItemDefinitionComponent
    {
    }

    [Serializable]
    public sealed class ConsumableItemComponent : ItemDefinitionComponent
    {
        [SerializeField] [Min(1)] private int _amountPerUse = 1;

        public int AmountPerUse => Mathf.Max(1, _amountPerUse);
    }

    [Serializable]
    public sealed class EquippableItemComponent : ItemDefinitionComponent
    {
        [SerializeField] private string[] _preferredSlotIds = Array.Empty<string>();

        public string[] PreferredSlotIds => _preferredSlotIds ?? Array.Empty<string>();
    }
}
