using System;
using System.Collections.Generic;
using R3;

namespace RaidIntoDarkness.UI
{
    internal interface ISettingsPageViewModel : IDisposable
    {
        ReadOnlyReactiveProperty<bool> IsDirty { get; }
        bool Apply(double now);
        void Cancel(double now);
        void Reset();
    }

    internal sealed class SettingsMenuViewModel : IDisposable
    {
        private readonly IReadOnlyList<ISettingsPageViewModel> _pages;
        private readonly ReactiveProperty<(int Index, bool IsDirty, bool HasChanges)> _state = new();
        private readonly CompositeDisposable _bindings = new();
        private int _selectedIndex;

        public ReadOnlyReactiveProperty<(int Index, bool IsDirty, bool HasChanges)> State => _state;

        public SettingsMenuViewModel(IReadOnlyList<ISettingsPageViewModel> pages, int selectedIndex)
        {
            _pages = pages;
            _selectedIndex = selectedIndex;
            foreach (ISettingsPageViewModel page in _pages)
            {
                _bindings.Add(page.IsDirty.Subscribe(_ => UpdateState()));
            }
        }

        public void Select(int index)
        {
            if (index != _selectedIndex)
            {
                _selectedIndex = index;
                UpdateState();
            }
        }

        public bool Apply(double now) => _pages[_selectedIndex].Apply(now);
        public void Cancel(double now) => _pages[_selectedIndex].Cancel(now);
        public void Reset() => _pages[_selectedIndex].Reset();

        public void Dispose()
        {
            _bindings.Dispose();
            foreach (ISettingsPageViewModel page in _pages)
            {
                page.Dispose();
            }

            _state.Dispose();
        }

        private void UpdateState()
        {
            bool hasChanges = false;
            foreach (ISettingsPageViewModel page in _pages)
            {
                if (page.IsDirty.CurrentValue)
                {
                    hasChanges = true;
                    break;
                }
            }

            _state.Value = (_selectedIndex, _pages[_selectedIndex].IsDirty.CurrentValue, hasChanges);
        }
    }
}
