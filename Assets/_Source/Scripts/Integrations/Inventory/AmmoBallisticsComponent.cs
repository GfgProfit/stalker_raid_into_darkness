using System;
using Inventory;
using RaidIntoDarkness.Weapons;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [Serializable]
    public sealed class AmmoBallisticsComponent : ItemDefinitionComponent
    {
        [SerializeField] private BallisticAmmoData _data = new BallisticAmmoData();

        public AmmoBallisticsComponent()
        {
        }

        public AmmoBallisticsComponent(BallisticAmmoData data)
        {
            _data = data;
        }

        public BallisticAmmoData Data => _data;
    }
}
