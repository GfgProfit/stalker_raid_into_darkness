using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryStatusPanel : MonoBehaviour
    {
        [SerializeField] private List<InventoryStatusBar> _bars = new();

        private readonly Dictionary<string, float> _values = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _maximums = new(StringComparer.Ordinal);
        private string _hoveredId;

        public IReadOnlyList<InventoryStatusBar> Bars => _bars;

        public event Action<string, Vector2, Camera> PointerEntered;
        public event Action<Vector2, Camera> PointerMoved;
        public event Action PointerExited;

        public void Configure(IEnumerable<InventoryStatusBar> bars)
        {
            _bars = bars == null ? new List<InventoryStatusBar>() : new List<InventoryStatusBar>(bars);
            if (Application.isPlaying)
            {
                BindHoverTargets();
            }
        }

        public bool SetValue(string id, float value, float maximum = 1f)
        {
            bool found = false;

            for (int i = 0; i < _bars.Count; i++)
            {
                if (_bars[i] != null && string.Equals(_bars[i].Id, id, StringComparison.Ordinal))
                {
                    _bars[i].Apply(value, maximum);
                    found = true;
                }
            }

            if (found)
            {
                _values[id] = Mathf.Clamp01(value);
                _maximums[id] = Mathf.Clamp01(maximum);
            }

            return found;
        }

        public bool SetBonus(string id, float value, Color color)
        {
            bool found = false;

            for (int i = 0; i < _bars.Count; i++)
            {
                if (_bars[i] != null && string.Equals(_bars[i].Id, id, StringComparison.Ordinal))
                {
                    _bars[i].ApplyBonus(value, color);
                    found = true;
                }
            }

            return found;
        }

        public void ClearBonus()
        {
            for (int i = 0; i < _bars.Count; i++)
            {
                if (_bars[i] != null)
                {
                    _bars[i].ClearBonus();
                }
            }
        }

        public float GetValue(string id) => _values.TryGetValue(id, out float value) ? value : 0f;
        public float GetMaximum(string id) => _maximums.TryGetValue(id, out float value) ? value : 0f;

        public void HandlePointerEnter(string id, Vector2 position, Camera eventCamera)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }
            _hoveredId = id;
            PointerEntered?.Invoke(id, position, eventCamera);
        }

        public void HandlePointerMove(Vector2 position, Camera eventCamera)
        {
            if (string.IsNullOrEmpty(_hoveredId) == false)
            {
                PointerMoved?.Invoke(position, eventCamera);
            }
        }

        public void HandlePointerExit(string id)
        {
            if (string.IsNullOrEmpty(_hoveredId) == false && string.Equals(_hoveredId, id, StringComparison.Ordinal))
            {
                _hoveredId = null;
                PointerExited?.Invoke();
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                BindHoverTargets();
            }
        }

        private void OnDisable()
        {
            HandlePointerExit(_hoveredId);
        }

        private void BindHoverTargets()
        {
            for (int i = 0; i < _bars.Count; i++)
            {
                InventoryStatusBar bar = _bars[i];
                if (bar == null || bar.Text == null || bar.Text.transform.parent == null || bar.Text.transform.parent == transform)
                {
                    continue;
                }
                GameObject row = bar.Text.transform.parent.gameObject;
                if (row.TryGetComponent(out InventoryStatusHoverTarget target) == false)
                {
                    target = row.AddComponent<InventoryStatusHoverTarget>();
                }
                target.Configure(this, bar.Id);
            }
        }
    }
}
