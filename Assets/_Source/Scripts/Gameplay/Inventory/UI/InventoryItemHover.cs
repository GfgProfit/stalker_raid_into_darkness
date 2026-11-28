using System;
using System.Collections.Generic;
using R3;
using RaidIntoDarkness.Inventory.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventoryItemHover
    {
        private readonly List<InventoryItemStat> _stats = new();
        private readonly List<RaycastResult> _raycastResults = new();

        private InventoryModel _model;
        private IDisposable _modelBinding;
        private InventoryTooltip _tooltip;
        private InventoryItemView _view;
        private ItemInstance _item;
        private Vector2 _position;
        private Camera _camera;
        private PointerEventData _pointer;
        private EventSystem _eventSystem;
        private float _start;
        private float _delay;
        private bool _external;

        public ItemInstance Item => _item;
        public Action<ItemInstance, List<InventoryItemStat>> StatsProvider { get; set; }
        public Func<ItemInstance, string> DescriptionProvider { get; set; }

        public void Configure(InventoryTooltip tooltip, float delay, InventoryTheme theme)
        {
            if (_tooltip != tooltip)
            {
                Clear();
                _tooltip = tooltip;
            }

            _delay = delay;
            if (_tooltip != null)
            {
                _tooltip.Theme = theme;
            }
        }

        public void Bind(InventoryModel model)
        {
            Unbind();
            _model = model;
            _modelBinding = Observable.FromEvent<InventoryChange>(handler => model.Changed += handler, handler => model.Changed -= handler).Subscribe(OnModelChanged);
        }

        public void Unbind()
        {
            _modelBinding?.Dispose();
            _modelBinding = null;
            _model = null;
            Clear();
            _stats.Clear();
            _raycastResults.Clear();
            _pointer = null;
            _eventSystem = null;
        }

        public bool Enter(InventoryItemView view, PointerEventData eventData)
        {
            if (view == null || IsCurrentView(view, view.Item) == false)
            {
                return false;
            }

            if (_view != view || _external)
            {
                HideView();
            }

            _external = false;
            _view = view;
            _item = view.Item;
            _start = Time.unscaledTime;
            _position = eventData.position;
            _camera = eventData.enterEventCamera;
            return true;
        }

        public void Exit(InventoryItemView view)
        {
            if (ReferenceEquals(_view, view) && ReferenceEquals(view, null) == false)
            {
                Clear();
            }
        }

        public void Clear()
        {
            _view = null;
            _item = null;
            _external = false;
            _position = default;
            _camera = null;
            _start = 0f;
            HideView();
        }

        public bool ShowExternal(string title, string description, Vector2 position, Camera camera)
        {
            if (_model == null || _tooltip == null)
            {
                return false;
            }

            _view = null;
            _item = null;
            _external = true;
            _position = position;
            _camera = camera;
            _tooltip.Show(title, description, position, camera);
            return true;
        }

        public void FollowExternal(Vector2 position, Camera camera)
        {
            if (_external && _tooltip != null)
            {
                _position = position;
                _camera = camera;
                _tooltip.Follow(position, camera);
            }
        }

        public void HideExternal()
        {
            if (_external)
            {
                Clear();
            }
        }

        public void Refresh()
        {
            if (_item != null && _tooltip != null && _tooltip.IsVisible)
            {
                if (IsCurrentView(_view, _item))
                {
                    ShowItem(PointerPosition);
                }
                else
                {
                    Clear();
                }
            }
        }

        public void Tick(bool canShow)
        {
            if (canShow == false)
            {
                Clear();
                return;
            }

            if (_tooltip == null || _item == null && _external == false)
            {
                HideView();
                return;
            }

            Vector2 position = PointerPosition;
            if (_external)
            {
                _tooltip.Follow(position, _camera);
                return;
            }

            if (_view == null || _view.isActiveAndEnabled == false || _view.Item != _item || IsPointerOverView(position) == false)
            {
                Clear();
                return;
            }

            if (Time.unscaledTime - _start >= _delay)
            {
                if (_tooltip.IsVisible)
                {
                    _tooltip.Follow(position, _camera);
                }
                else
                {
                    ShowItem(position);
                }
            }
        }

        public IReadOnlyList<InventoryItemStat> CollectStats(ItemInstance item)
        {
            _stats.Clear();
            if (item != null)
            {
                StatsProvider?.Invoke(item, _stats);
            }

            return _stats;
        }

        public string CollectDescription(ItemInstance item) => item != null ? DescriptionProvider?.Invoke(item) : null;

        private Vector2 PointerPosition => Pointer.current != null ? Pointer.current.position.ReadValue() : _position;

        private void ShowItem(Vector2 position) => _tooltip.Show(_item, position, _camera, CollectStats(_item), CollectDescription(_item));

        private void HideView()
        {
            if (_tooltip != null && _tooltip.IsVisible)
            {
                _tooltip.Hide();
            }
        }

        private bool IsCurrentView(InventoryItemView view, ItemInstance item)
        {
            if (view == null || item == null || view.isActiveAndEnabled == false || view.Item != item || _model == null || _model.ContainsItem(item) == false)
            {
                return false;
            }

            InventoryGridViewBase grid = view.Grid;
            return grid != null && grid.Container != null && grid.Container.Model == _model && grid.Container.Contains(item) && grid.GetItemView(item) == view;
        }

        private bool IsPointerOverView(Vector2 position)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            if (_pointer == null || _eventSystem != eventSystem)
            {
                _eventSystem = eventSystem;
                _pointer = new PointerEventData(eventSystem);
            }

            _pointer.Reset();
            _pointer.position = position;
            _raycastResults.Clear();
            eventSystem.RaycastAll(_pointer, _raycastResults);
            return _raycastResults.Count > 0 && _raycastResults[0].gameObject != null && _raycastResults[0].gameObject.GetComponentInParent<InventoryItemView>() == _view;
        }

        private void OnModelChanged(InventoryChange change)
        {
            if (_item != null && (change.Kind == InventoryChangeKind.Cleared || change.Item == _item || _view != null && _view.Grid != null && change.Affects(_view.Grid.ContainerId)))
            {
                if (IsCurrentView(_view, _item) == false)
                {
                    Clear();
                }
                else if (change.Item == _item && _tooltip != null && _tooltip.IsVisible)
                {
                    ShowItem(PointerPosition);
                }
            }
        }
    }
}
