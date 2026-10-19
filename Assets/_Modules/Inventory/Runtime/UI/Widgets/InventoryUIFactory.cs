using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Inventory.UI
{
    public static class InventoryUIFactory
    {
        public static readonly Color PanelColor = new(0.07f, 0.075f, 0.08f, 0.97f);
        public static readonly Color PanelBorderColor = new(1f, 1f, 1f, 0.12f);
        public static readonly Color TextColor = new(0.9f, 0.9f, 0.86f, 1f);
        public static readonly Color MutedTextColor = new(0.65f, 0.66f, 0.62f, 1f);
        public static readonly Color ButtonColor = new(1f, 1f, 1f, 0.06f);
        public static readonly Color ButtonHoverColor = new(1f, 0.75f, 0.25f, 0.25f);
        public static readonly Color AccentColor = new(1f, 0.72f, 0.25f, 1f);

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.layer = parent.gameObject.layer;
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            return rect;
        }

        public static Image AddPanel(GameObject target, Color color, bool raycastTarget = true)
        {
            Image image = target.GetComponent<Image>();

            if (image == null)
            {
                image = target.AddComponent<Image>();
            }

            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize, TextAlignmentOptions alignment, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        public static Button CreateButton(string name, Transform parent, string text, float fontSize, UnityAction onClick)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = AddPanel(rect.gameObject, Color.white);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = ButtonHoverColor;
            colors.selectedColor = ButtonColor;
            colors.pressedColor = new Color(1f, 0.72f, 0.25f, 0.45f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.02f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            TextMeshProUGUI label = CreateText("Label", rect, text, fontSize, TextAlignmentOptions.MidlineLeft, TextColor);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(10f, 0f);
            label.textWrappingMode = TextWrappingModes.NoWrap;

            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            return button;
        }

        public static void PlaceInsideParent(RectTransform target, Vector2 desiredLocalTopLeft)
        {
            var parent = (RectTransform)target.parent;
            Rect parentRect = parent.rect;
            Vector2 size = target.rect.size;
            float x = Mathf.Clamp(desiredLocalTopLeft.x, parentRect.xMin, parentRect.xMax - size.x);
            float y = Mathf.Clamp(desiredLocalTopLeft.y, parentRect.yMin + size.y, parentRect.yMax);
            target.anchorMin = new Vector2(0.5f, 0.5f);
            target.anchorMax = new Vector2(0.5f, 0.5f);
            target.pivot = new Vector2(0f, 1f);
            target.localPosition = new Vector3(x, y, 0f);
        }

        public static Vector2 ScreenToParentLocal(RectTransform target, Vector2 screenPosition, Camera eventCamera)
        {
            var parent = (RectTransform)target.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPosition, eventCamera, out Vector2 local);
            return local;
        }

        public static void SetLayout(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        public static string FormatWeight(float weight) => weight.ToString(weight >= 10f ? "0.0" : "0.00", System.Globalization.CultureInfo.InvariantCulture);

        public static string Describe(InventoryFailure failure) => failure switch
        {
            InventoryFailure.None => string.Empty,
            InventoryFailure.Occupied => "Place is occupied",
            InventoryFailure.OutOfBounds => "Does not fit",
            InventoryFailure.NoSlot => "No slot here",
            InventoryFailure.SlotUnavailable => "Slot is locked",
            InventoryFailure.SlotRejected => "Item is not accepted by this slot",
            InventoryFailure.ValidatorRejected => "Placement rejected",
            InventoryFailure.RotationNotAllowed => "Item cannot be rotated",
            InventoryFailure.NoSpace => "Not enough space",
            InventoryFailure.DependentSlotOccupied => "Another slot depends on this item",
            InventoryFailure.StackFull => "Stack is full",
            _ => failure.ToString()
        };

        public static void Destroy(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        public static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        public static void Require(object value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
        }
    }
}
