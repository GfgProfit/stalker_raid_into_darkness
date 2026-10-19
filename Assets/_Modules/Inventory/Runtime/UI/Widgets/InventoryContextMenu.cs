using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryContextMenu : MonoBehaviour
    {
        private const float WIDTH = 190f;
        private const float ROW_HEIGHT = 30f;
        private const float PADDING = 4f;

        private readonly List<Button> _buttons = new();
        private RectTransform _rectTransform;
        private RectTransform _panel;
        private Action _onHidden;

        public bool IsVisible => gameObject.activeSelf;
        public IReadOnlyList<Button> Buttons => _buttons;

        public void Show(Vector2 screenPosition, Camera eventCamera, IReadOnlyList<Entry> entries, Action onHidden = null)
        {
            EnsureBuilt();
            _onHidden = onHidden;

            while (_buttons.Count < entries.Count)
            {
                Button button = InventoryUIFactory.CreateButton("Action", _panel, string.Empty, 15f, null);
                _buttons.Add(button);
            }

            for (int i = 0; i < _buttons.Count; i++)
            {
                Button button = _buttons[i];
                bool visible = i < entries.Count;
                button.gameObject.SetActive(visible);
                button.onClick.RemoveAllListeners();

                if (visible == false)
                {
                    continue;
                }

                Entry entry = entries[i];
                RectTransform rect = (RectTransform)button.transform;
                InventoryUIFactory.SetLayout(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(PADDING, -PADDING - (i + 1) * ROW_HEIGHT), new Vector2(-PADDING, -PADDING - i * ROW_HEIGHT));
                button.GetComponentInChildren<TMP_Text>().text = entry.Label;
                button.interactable = entry.Enabled;
                Action callback = entry.Callback;
                button.onClick.AddListener(() =>
                {
                    Hide();
                    callback?.Invoke();
                });
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _panel.sizeDelta = new Vector2(WIDTH, entries.Count * ROW_HEIGHT + PADDING * 2f);
            Vector2 local = InventoryUIFactory.ScreenToParentLocal(_panel, screenPosition, eventCamera);
            InventoryUIFactory.PlaceInsideParent(_panel, local);
        }

        public void Hide()
        {
            if (gameObject.activeSelf == false)
            {
                return;
            }

            gameObject.SetActive(false);
            Action hidden = _onHidden;
            _onHidden = null;
            hidden?.Invoke();
        }

        public bool Invoke(string label)
        {
            foreach (Button button in _buttons)
            {
                if (button.gameObject.activeSelf && button.interactable && button.GetComponentInChildren<TMP_Text>().text == label)
                {
                    button.onClick.Invoke();
                    return true;
                }
            }

            return false;
        }

        private void Awake() => EnsureBuilt();

        private void EnsureBuilt()
        {
            if (_rectTransform != null)
            {
                return;
            }

            _rectTransform = (RectTransform)transform;
            InventoryUIFactory.Stretch(_rectTransform);
            Image blocker = InventoryUIFactory.AddPanel(gameObject, new Color(0f, 0f, 0f, 0f));
            blocker.raycastTarget = true;
            InventoryUIFactory.GetOrAdd<InventoryClickCatcher>(gameObject).Clicked = Hide;
            _panel = InventoryUIFactory.CreateRect("Panel", transform);
            InventoryUIFactory.AddPanel(_panel.gameObject, InventoryUIFactory.PanelColor);
        }

        public readonly struct Entry
        {
            public Entry(string label, bool enabled, Action callback)
            {
                Label = label;
                Enabled = enabled;
                Callback = callback;
            }

            public string Label { get; }
            public bool Enabled { get; }
            public Action Callback { get; }
        }
    }
}
