using System;
using R3;
using RaidIntoDarkness.DI;
using RaidIntoDarkness.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.UI
{
    public sealed class GameSettingsPage : SettingsPageView
    {
        [SerializeField] private Toggle _showItemDescriptions;

        [Inject] private IGameSettings _settings;
        private GameSettingsViewModel _viewModel;
        private IDisposable _descriptionBinding;

        internal override ISettingsPageViewModel Open()
        {
            _viewModel = new GameSettingsViewModel(_settings);
            _descriptionBinding = _viewModel.ShowItemDescriptions.Subscribe(_showItemDescriptions.SetIsOnWithoutNotify);
            _showItemDescriptions.onValueChanged.AddListener(EditDescriptions);
            return _viewModel;
        }

        internal override void Close()
        {
            _showItemDescriptions.onValueChanged.RemoveListener(EditDescriptions);
            _descriptionBinding.Dispose();
            _descriptionBinding = null;
            _viewModel = null;
        }

        private void EditDescriptions(bool value)
        {
            if (isActiveAndEnabled && _showItemDescriptions.IsInteractable() && !CanvasUpdateRegistry.IsRebuildingLayout())
            {
                _viewModel.SetShowItemDescriptions(value);
            }
        }
    }

    internal sealed class GameSettingsViewModel : ISettingsPageViewModel
    {
        private readonly IGameSettings _settings;
        private readonly ReactiveProperty<bool> _description;
        private readonly ReactiveProperty<bool> _dirty = new();
        private readonly IDisposable _committedBinding;

        public ReadOnlyReactiveProperty<bool> ShowItemDescriptions => _description;
        public ReadOnlyReactiveProperty<bool> IsDirty => _dirty;

        public GameSettingsViewModel(IGameSettings settings)
        {
            _settings = settings;
            _description = new ReactiveProperty<bool>(settings.ShowItemDescriptions.CurrentValue);
            _committedBinding = settings.ShowItemDescriptions.Subscribe(CommittedDescriptionChanged);
        }

        public bool Apply(double now) => _settings.TrySetShowItemDescriptions(_description.Value);
        public void Cancel(double now) => SetShowItemDescriptions(_settings.ShowItemDescriptions.CurrentValue);
        public void Reset() => SetShowItemDescriptions(GameSettingsStore.DEFAULT_SHOW_ITEM_DESCRIPTIONS);

        public void SetShowItemDescriptions(bool value)
        {
            _description.Value = value;
            _dirty.Value = value != _settings.ShowItemDescriptions.CurrentValue;
        }

        public void Dispose()
        {
            _committedBinding.Dispose();
            _description.Dispose();
            _dirty.Dispose();
        }

        private void CommittedDescriptionChanged(bool value)
        {
            if (!_dirty.Value)
            {
                _description.Value = value;
            }

            _dirty.Value = _description.Value != value;
        }
    }
}
