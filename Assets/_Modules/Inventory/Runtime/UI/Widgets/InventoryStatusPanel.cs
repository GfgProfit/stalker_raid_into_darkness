using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryStatusPanel : MonoBehaviour
    {
        [SerializeField] private List<InventoryStatusBar> _bars = new();

        private readonly Dictionary<string, float> _values = new(StringComparer.Ordinal);

        public IReadOnlyList<InventoryStatusBar> Bars => _bars;

        public void Configure(IEnumerable<InventoryStatusBar> bars)
        {
            _bars = bars == null ? new List<InventoryStatusBar>() : new List<InventoryStatusBar>(bars);
        }

        public bool SetValue(string id, float value)
        {
            bool found = false;

            for (int i = 0; i < _bars.Count; i++)
            {
                if (_bars[i] != null && string.Equals(_bars[i].Id, id, StringComparison.Ordinal))
                {
                    _bars[i].Apply(value);
                    found = true;
                }
            }

            if (found)
            {
                _values[id] = Mathf.Clamp01(value);
            }

            return found;
        }

        public float GetValue(string id) => _values.TryGetValue(id, out float value) ? value : 0f;
    }
}
