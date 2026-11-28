using RaidIntoDarkness.Inventory.Domain;
using System;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventorySplitDialog : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _amountText;
        private Slider _slider;
        private Action<int> _onConfirm;
        private IDisposable _amountBinding;

        public InventoryTheme Theme { get; set; }
        public bool IsVisible => gameObject.activeSelf;
        public int SelectedAmount => _slider != null ? Mathf.RoundToInt(_slider.value) : 0;

        public void Show(ItemInstance item, Action<int> onConfirm)
        {
            ClearBinding();
            EnsureBuilt();
            _onConfirm = onConfirm;
            _title.text = InventoryTheme.Resolve(Theme, "split_title", "Split ") + item.Definition.DisplayName;
            _slider.minValue = 1;
            _slider.maxValue = Mathf.Max(1, item.Amount - 1);
            _slider.value = Mathf.Max(1, item.Amount / 2);
            UpdateAmount(_slider.value);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        internal void Show(InventoryModel model, ItemInstance item, Action<int> onConfirm)
        {
            Show(item, onConfirm);
            var viewModel = new InventorySplitDialogViewModel(model, item);

            if (isActiveAndEnabled == false)
            {
                Hide();
                return;
            }

            ApplyMaximumAmount(viewModel.GetMaximumAmount());

            if (IsVisible)
            {
                _amountBinding = viewModel.MaximumAmountChanges.Subscribe(ApplyMaximumAmount);
            }
        }

        public void SetAmount(int amount)
        {
            EnsureBuilt();
            _slider.value = amount;
        }

        public void Confirm()
        {
            int amount = SelectedAmount;
            Action<int> confirm = _onConfirm;
            Hide();
            confirm?.Invoke(amount);
        }

        public void Hide()
        {
            ClearBinding();

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void Awake() => EnsureBuilt();
        private void OnDisable() => Hide();

        private void ClearBinding()
        {
            _onConfirm = null;
            _amountBinding?.Dispose();
            _amountBinding = null;
        }

        private void ApplyMaximumAmount(int maximum)
        {
            if (maximum < 1)
            {
                Hide();
                return;
            }

            _slider.maxValue = maximum;
        }

        private void UpdateAmount(float value) => _amountText.text = Mathf.RoundToInt(value).ToString();

        private void EnsureBuilt()
        {
            if (_rectTransform != null)
            {
                return;
            }

            _rectTransform = (RectTransform)transform;
            InventoryUIFactory.Stretch(_rectTransform);
            InventoryUIFactory.AddPanel(gameObject, new Color(0f, 0f, 0f, 0.45f));
            RectTransform panel = InventoryUIFactory.CreateRect("Panel", transform);
            InventoryUIFactory.AddPanel(panel.gameObject, InventoryUIFactory.PanelColor);
            panel.sizeDelta = new Vector2(360f, 170f);
            _title = InventoryUIFactory.CreateText("Title", panel, string.Empty, 18f, TextAlignmentOptions.Center, InventoryUIFactory.AccentColor);
            InventoryUIFactory.SetLayout(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -40f), new Vector2(-12f, -10f));
            _amountText = InventoryUIFactory.CreateText("Amount", panel, "1", 22f, TextAlignmentOptions.Center, InventoryUIFactory.TextColor);
            InventoryUIFactory.SetLayout(_amountText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -72f), new Vector2(-12f, -44f));
            _slider = CreateSlider(panel);
            Button ok = InventoryUIFactory.CreateButton("Confirm", panel, InventoryTheme.Resolve(Theme, "button_split", "Split"), 16f, Confirm);
            InventoryUIFactory.SetLayout((RectTransform)ok.transform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(6f, 12f), new Vector2(-12f, 46f));
            Button cancel = InventoryUIFactory.CreateButton("Cancel", panel, InventoryTheme.Resolve(Theme, "button_cancel", "Cancel"), 16f, Hide);
            InventoryUIFactory.SetLayout((RectTransform)cancel.transform, new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(12f, 12f), new Vector2(-6f, 46f));
            _slider.onValueChanged.AddListener(UpdateAmount);
        }

        private static Slider CreateSlider(RectTransform panel)
        {
            RectTransform root = InventoryUIFactory.CreateRect("Slider", panel);
            InventoryUIFactory.SetLayout(root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -104f), new Vector2(-20f, -84f));
            Slider slider = root.gameObject.AddComponent<Slider>();
            RectTransform background = InventoryUIFactory.Stretch(InventoryUIFactory.CreateRect("Background", root));
            InventoryUIFactory.AddPanel(background.gameObject, new Color(1f, 1f, 1f, 0.08f));
            RectTransform fillArea = InventoryUIFactory.Stretch(InventoryUIFactory.CreateRect("Fill Area", root));
            RectTransform fill = InventoryUIFactory.Stretch(InventoryUIFactory.CreateRect("Fill", fillArea));
            InventoryUIFactory.AddPanel(fill.gameObject, new Color(1f, 0.72f, 0.25f, 0.45f), false);
            RectTransform handleArea = InventoryUIFactory.Stretch(InventoryUIFactory.CreateRect("Handle Area", root));
            RectTransform handle = InventoryUIFactory.CreateRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(16f, 0f);
            Image handleImage = InventoryUIFactory.AddPanel(handle.gameObject, InventoryUIFactory.AccentColor);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.wholeNumbers = true;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }
    }
}
