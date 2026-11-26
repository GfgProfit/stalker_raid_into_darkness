using System;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [Serializable]
    internal struct MagazineBonePair
    {
        [SerializeField] private Transform _installed;
        [SerializeField] private Transform _replacement;

        public Transform Installed => _installed;
        public Transform Replacement => _replacement;
    }
}
