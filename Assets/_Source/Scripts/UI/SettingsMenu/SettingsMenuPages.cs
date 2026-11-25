using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace RaidIntoDarkness.UI
{
    [DisallowMultipleComponent]
    public sealed class SettingsMenuPages : MonoBehaviour
    {
        [SerializeField] private SettingsPageView[] _pages = Array.Empty<SettingsPageView>();
        [SerializeField] private SettingsPageView _defaultPage;

        private SettingsMenuViewModel _viewModel;
        private IDisposable _stateBinding;
        private UnityAction[] _tabActions;
        private bool _blocked;
        private int _visibleIndex = -1;

        internal IReadOnlyList<SettingsPageView> Pages => _pages;
        internal SettingsPageView ActivePage => _viewModel != null ? _pages[_viewModel.State.CurrentValue.Index] : null;
        internal bool HasPendingChanges => _viewModel?.State.CurrentValue.HasChanges == true;
        internal bool SelectedPageIsDirty => _viewModel?.State.CurrentValue.IsDirty == true;
        internal bool IsOpen => _viewModel != null;

        internal event Action Changed;

        internal bool IsConfigured()
        {
            if (_defaultPage == null || Array.IndexOf(_pages, _defaultPage) < 0)
            {
                return false;
            }

            foreach (SettingsPageView page in _pages)
            {
                if (page == null || page.TabButton == null)
                {
                    return false;
                }
            }

            return true;
        }

        internal T FindPage<T>() where T : SettingsPageView
        {
            foreach (SettingsPageView page in _pages)
            {
                if (page is T found)
                {
                    return found;
                }
            }

            return null;
        }

        internal void Open()
        {
            var viewModels = new ISettingsPageViewModel[_pages.Length];
            _tabActions = new UnityAction[_pages.Length];
            for (int i = 0; i < _pages.Length; i++)
            {
                SettingsPageView page = _pages[i];
                int index = i;
                viewModels[i] = page.Open();
                _tabActions[i] = () => SelectPage(index);
                page.TabButton.onClick.AddListener(_tabActions[i]);
            }

            _viewModel = new SettingsMenuViewModel(viewModels, Array.IndexOf(_pages, _defaultPage));
            _stateBinding = _viewModel.State.Subscribe(Render);
        }

        internal void Close()
        {
            if (_viewModel == null)
            {
                return;
            }

            _stateBinding.Dispose();
            for (int i = 0; i < _pages.Length; i++)
            {
                SettingsPageView page = _pages[i];
                page.TabButton.onClick.RemoveListener(_tabActions[i]);
                page.Close();
            }

            _viewModel.Dispose();
            _viewModel = null;
            _tabActions = null;
            _visibleIndex = -1;
        }

        private void OnDisable() => Close();

        internal bool Apply(double now) => _viewModel.Apply(now);
        internal void Cancel(double now) => _viewModel.Cancel(now);
        internal void ResetSelected() => _viewModel.Reset();
        internal void SetBlocked(bool blocked) => _blocked = blocked;

        private void SelectPage(int index)
        {
            if (!_blocked)
            {
                _viewModel.Select(index);
                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(_pages[index].InitialControl.gameObject);
                }
            }
        }

        private void Render((int Index, bool IsDirty, bool HasChanges) state)
        {
            if (_visibleIndex != state.Index)
            {
                _visibleIndex = state.Index;
                for (int i = 0; i < _pages.Length; i++)
                {
                    GameObject panel = _pages[i].gameObject;
                    bool selected = i == state.Index;
                    if (panel.activeSelf != selected)
                    {
                        panel.SetActive(selected);
                    }
                }
            }

            Changed?.Invoke();
        }
    }
}
