using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    [Serializable]
    internal sealed class AmmoBallisticsProfiles
    {
        [SerializeField] private AmmoBallisticsProfile[] _entries;

        public AmmoBallisticsProfile[] Entries => _entries;
    }
}
