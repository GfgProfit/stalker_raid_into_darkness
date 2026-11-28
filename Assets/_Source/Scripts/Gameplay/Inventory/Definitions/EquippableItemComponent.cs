using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    [Serializable]
    public sealed class EquippableItemComponent : ItemDefinitionComponent
    {
        [SerializeField] private SlotId[] _preferredSlotIds = Array.Empty<SlotId>();

        public IReadOnlyList<SlotId> PreferredSlotIds => _preferredSlotIds ?? Array.Empty<SlotId>();
    }
}
