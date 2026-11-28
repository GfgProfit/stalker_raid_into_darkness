using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryInspectPanel : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private Image _icon;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;

        public InventoryTheme Theme { get; set; }
        public bool IsVisible => gameObject.activeSelf;
        public ItemInstance Item { get; private set; }

        public void Show(ItemInstance item, Sprite icon)
        {
            EnsureBuilt();
            Item = item;
            ItemDefinition definition = item.Definition;
            _title.text = definition.DisplayName;
            string format = InventoryTheme.Resolve(Theme, "inspect_body", "{0}\n\nCategory: {1}\nSize: {2}x{3}\nWeight: {4} kg per unit\nStack: {5}/{6}\nTags: {7}\nDefinition: {8}\nInstance: {9}");
            string category = InventoryTheme.Resolve(Theme, "category_" + definition.Category, definition.Category);
            _body.text = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, definition.Description, category, definition.Width, definition.Height, InventoryUIFactory.FormatWeight(definition.Weight), item.Amount, definition.MaxStack, string.Join(", ", definition.Tags), definition.DefinitionId, item.InstanceId);
            SetIcon(icon);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void SetIcon(Sprite icon)
        {
            EnsureBuilt();
            _icon.sprite = icon;
            _icon.enabled = icon != null;

            if (icon != null)
            {
                float aspect = icon.rect.width / Mathf.Max(1f, icon.rect.height);
                RectTransform iconRect = _icon.rectTransform;
                float maxWidth = 300f;
                float maxHeight = 220f;
                float width = Mathf.Min(maxWidth, maxHeight * aspect);
                iconRect.sizeDelta = new Vector2(width, width / aspect);
            }
        }

        public void Hide()
        {
            Item = null;
            gameObject.SetActive(false);
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
            InventoryUIFactory.AddPanel(gameObject, new Color(0f, 0f, 0f, 0.55f));
            RectTransform panel = InventoryUIFactory.CreateRect("Panel", transform);
            InventoryUIFactory.AddPanel(panel.gameObject, InventoryUIFactory.PanelColor);
            panel.sizeDelta = new Vector2(720f, 360f);
            RectTransform iconRect = InventoryUIFactory.CreateRect("Icon", panel);
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(176f, 0f);
            _icon = iconRect.gameObject.AddComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;
            _title = InventoryUIFactory.CreateText("Title", panel, string.Empty, 22f, TextAlignmentOptions.TopLeft, InventoryUIFactory.AccentColor);
            InventoryUIFactory.SetLayout(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(350f, -50f), new Vector2(-16f, -14f));
            _body = InventoryUIFactory.CreateText("Body", panel, string.Empty, 15f, TextAlignmentOptions.TopLeft, InventoryUIFactory.TextColor);
            InventoryUIFactory.SetLayout(_body.rectTransform, Vector2.zero, Vector2.one, new Vector2(350f, 60f), new Vector2(-16f, -56f));
            Button close = InventoryUIFactory.CreateButton("Close", panel, InventoryTheme.Resolve(Theme, "button_close", "Close"), 16f, Hide);
            InventoryUIFactory.SetLayout((RectTransform)close.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-140f, 14f), new Vector2(-16f, 48f));
        }
    }
}
