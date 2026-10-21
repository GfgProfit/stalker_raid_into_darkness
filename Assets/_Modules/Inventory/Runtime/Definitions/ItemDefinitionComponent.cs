using System;
using UnityEngine;

namespace Inventory
{
    [Serializable]
    public abstract class ItemDefinitionComponent
    {
        public virtual float GetAdditionalWeight(ItemInstance item, IItemDefinitionResolver resolver) => 0f;
        public virtual Vector2Int GetSizeDelta(ItemInstance item, IItemDefinitionResolver resolver) => Vector2Int.zero;
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
