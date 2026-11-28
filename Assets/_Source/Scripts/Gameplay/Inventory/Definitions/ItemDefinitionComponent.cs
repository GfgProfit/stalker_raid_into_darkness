using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    [Serializable]
    public abstract class ItemDefinitionComponent
    {
        public virtual float GetAdditionalWeight(ItemInstance item, IItemDefinitionResolver resolver) => 0f;
        public virtual Vector2Int GetSizeDelta(ItemInstance item, IItemDefinitionResolver resolver) => Vector2Int.zero;
        public virtual string GetIconVariantId(ItemInstance item) => null;
    }
}
