using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryFilterBar : MonoBehaviour
    {
        [SerializeField] private InventoryWindow _window;
        [SerializeField] private List<InventoryFilterTab> _tabs = new();
        [SerializeField] private Color _normalColor = new(1f, 1f, 1f, 0.55f);
        [SerializeField] private Color _selectedColor = Color.white;

        private readonly List<Button> _hooked = new();

        public int SelectedIndex { get; private set; }
        public IReadOnlyList<InventoryFilterTab> Tabs => _tabs;

        public void Configure(InventoryWindow window, IEnumerable<InventoryFilterTab> tabs)
        {
            _window = window;
            _tabs = tabs == null ? new List<InventoryFilterTab>() : new List<InventoryFilterTab>(tabs);
        }

        public void Select(int index)
        {
            if (_tabs.Count == 0)
            {
                return;
            }

            SelectedIndex = Mathf.Clamp(index, 0, _tabs.Count - 1);
            InventoryFilterTab tab = _tabs[SelectedIndex];

            if (_window != null)
            {
                _window.SetItemFilter(tab.ShowsAll ? null : tab.Matches);
            }

            RefreshVisuals();
        }

        private void OnEnable()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                Button button = _tabs[i]?.Button;

                if (button == null || _hooked.Contains(button))
                {
                    continue;
                }

                int captured = i;
                button.onClick.AddListener(() => Select(captured));
                _hooked.Add(button);
            }

            RefreshVisuals();
        }

        private void OnDestroy()
        {
            foreach (Button button in _hooked)
            {
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                }
            }

            _hooked.Clear();
        }

        private void RefreshVisuals()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                Button button = _tabs[i]?.Button;

                if (button != null && button.targetGraphic != null)
                {
                    button.targetGraphic.color = i == SelectedIndex ? _selectedColor : _normalColor;
                }
            }
        }
    }
}
