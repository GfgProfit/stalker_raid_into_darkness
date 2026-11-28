using System;
using RaidIntoDarkness.Weapons;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    [Serializable]
    internal sealed class AmmoBallisticsProfile
    {
        [SerializeField] private string _section;
        [SerializeField] private BallisticAmmoData _data;

        public string Section => _section;
        public BallisticAmmoData Data => _data;
    }
}
