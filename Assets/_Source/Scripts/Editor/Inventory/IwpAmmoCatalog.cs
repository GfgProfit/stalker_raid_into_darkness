using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    [Serializable]
    internal sealed class IwpAmmoCatalog
    {
        [SerializeField] private string[] _augAmmo;
        [SerializeField] private IwpAmmoEntry[] _entries;

        public string[] AugAmmo => _augAmmo;
        public IwpAmmoEntry[] Entries => _entries;
    }
}
