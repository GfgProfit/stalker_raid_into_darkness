using System;
using RaidIntoDarkness.Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    [Serializable]
    public sealed class InventoryGridSettings
    {
        [SerializeField] private string _id;
        [SerializeField] private string _ownerId;
        [SerializeField] private string _displayName;
        [SerializeField] [Min(1)] private int _columns;
        [SerializeField] [Min(1)] private int _rows;
        [SerializeField] [Min(0)] private int _rowGrowth;

        public InventoryGridSettings(string id, string ownerId, string displayName, int columns, int rows, int rowGrowth)
        {
            _id = id;
            _ownerId = ownerId;
            _displayName = displayName;
            _columns = columns;
            _rows = rows;
            _rowGrowth = rowGrowth;
        }

        internal string Id => _id;
        internal bool IsValid => !string.IsNullOrWhiteSpace(_id) && _columns > 0 && _rows > 0 && _rowGrowth >= 0;

        internal GridContainer CreateContainer() => new(_id, _columns, _rows, _ownerId, _displayName, _rowGrowth);
    }
}
