using System;
using RaidIntoDarkness.Weapons;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    [Serializable]
    public sealed class AmmoBallisticsComponent : ItemDefinitionComponent
    {
        [SerializeField] private BallisticAmmoData _data = new BallisticAmmoData();
        [SerializeField] private GrenadeAmmoData _grenade;

        public AmmoBallisticsComponent()
        {
        }

        public AmmoBallisticsComponent(BallisticAmmoData data, GrenadeAmmoData grenade = null)
        {
            _data = data;
            _grenade = grenade;
        }

        public BallisticAmmoData Data => _data;
        public GrenadeAmmoData Grenade => _grenade;
    }
}
