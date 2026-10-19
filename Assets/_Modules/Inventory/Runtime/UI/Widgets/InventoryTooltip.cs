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
    public sealed class InventoryTooltip : MonoBehaviour
    {
        private const float WIDTH = 300f;
        private static readonly Vector2 POINTER_OFFSET = new(18f, -18f);

        private readonly StringBuilder _builder = new(256);
        private RectTransform _rectTransform;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;

        public InventoryTheme Theme { get; set; }
        public bool IsVisible => gameObject.activeSelf;
        public string Title => _title != null ? _title.text : string.Empty;

        public void Show(ItemInstance item, Vector2 screenPosition, Camera eventCamera)
        {
            EnsureBuilt();
            ItemDefinition definition = item.Definition;
            _title.text = definition.DisplayName;
            _builder.Clear();
            _builder.Append("<color=#A6A89E>").Append(InventoryTheme.Resolve(Theme, "category_" + definition.Category, definition.Category)).Append("  ").Append(definition.Width).Append('x').Append(definition.Height).Append("</color>\n");

            if (string.IsNullOrEmpty(definition.Description) == false)
            {
                _builder.Append(definition.Description).Append('\n');
            }

            _builder.Append(InventoryTheme.Resolve(Theme, "tooltip_weight", "Weight: ")).Append(InventoryUIFactory.FormatWeight(item.TotalWeight)).Append(InventoryTheme.Resolve(Theme, "unit_kg", " kg"));

            if (definition.IsStackable)
            {
                _builder.Append(InventoryTheme.Resolve(Theme, "tooltip_amount", "   Amount: ")).Append(item.Amount).Append('/').Append(definition.MaxStack);
            }

            if (definition.Tags.Count > 0)
            {
                _builder.Append("\n<color=#A6A89E>").Append(InventoryTheme.Resolve(Theme, "tooltip_tags", "Tags: ")).Append(string.Join(", ", definition.Tags)).Append("</color>");
            }

            _body.text = _builder.ToString();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            float height = _title.preferredHeight + _body.GetPreferredValues(_body.text, WIDTH - 20f, 0f).y + 26f;
            _rectTransform.sizeDelta = new Vector2(WIDTH, height);
            Follow(screenPosition, eventCamera);
        }

        public void Follow(Vector2 screenPosition, Camera eventCamera)
        {
            if (IsVisible == false)
            {
                return;
            }

            Vector2 local = InventoryUIFactory.ScreenToParentLocal(_rectTransform, screenPosition, eventCamera);
            InventoryUIFactory.PlaceInsideParent(_rectTransform, local + POINTER_OFFSET);
        }

        public void Hide() => gameObject.SetActive(false);

        private void Awake() => EnsureBuilt();

        private void EnsureBuilt()
        {
            if (_rectTransform != null)
            {
                return;
            }

            _rectTransform = (RectTransform)transform;
            InventoryUIFactory.AddPanel(gameObject, InventoryUIFactory.PanelColor, false);
            CanvasGroup group = InventoryUIFactory.GetOrAdd<CanvasGroup>(gameObject);
            group.blocksRaycasts = false;
            group.interactable = false;
            _title = InventoryUIFactory.CreateText("Title", transform, string.Empty, 18f, TextAlignmentOptions.TopLeft, InventoryUIFactory.AccentColor);
            InventoryUIFactory.SetLayout(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -32f), new Vector2(-10f, -8f));
            _body = InventoryUIFactory.CreateText("Body", transform, string.Empty, 14f, TextAlignmentOptions.TopLeft, InventoryUIFactory.TextColor);
            InventoryUIFactory.SetLayout(_body.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -34f));
            _body.richText = true;
        }
    }
}
