using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    [Serializable]
    public sealed class SlotLayoutBinding
    {
        [SerializeField] private string _key;
        [SerializeField] private SlotId _slotId;
        [SerializeField] private string _displayName;
        [SerializeField] private string _group;
        [SerializeField] private SlotMode _mode = SlotMode.Grid;
        [SerializeField] private SlotRule _rule = new();

        public SlotLayoutBinding()
        {
        }

        public SlotLayoutBinding(char key, SlotId slotId, string displayName, SlotMode mode, SlotRule rule, string group = null)
        {
            _key = key.ToString();
            _slotId = slotId;
            _displayName = displayName;
            _mode = mode;
            _rule = rule ?? new SlotRule();
            _group = group;
        }

        public char Key => _key?.Length == 1 ? _key[0] : '\0';
        public SlotId SlotId => _slotId;
        public string DisplayName => _displayName ?? string.Empty;
        public string Group => _group ?? string.Empty;
        public SlotMode Mode => _mode;
        public SlotRule Rule => _rule ?? new SlotRule();
    }
}
