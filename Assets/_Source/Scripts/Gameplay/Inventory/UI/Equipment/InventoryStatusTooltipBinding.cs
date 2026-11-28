using System;
using RaidIntoDarkness.Player;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventoryStatusTooltipBinding : IDisposable
    {
        private readonly InventoryWindow _window;
        private readonly InventoryStatusPanel _status;
        private readonly Func<StalkerActorCondition> _readCondition;

        private EquipmentStatusDetailsFormatter _formatter;
        private string _hoveredStatus;
        private Vector2 _position;
        private Camera _eventCamera;
        private bool _dirty;
        private bool _fullCondition;

        public InventoryStatusTooltipBinding(InventoryWindow window, InventoryStatusPanel status, Func<StalkerActorCondition> readCondition)
        {
            _window = window;
            _status = status;
            _readCondition = readCondition;
            _status.PointerEntered += OnPointerEnter;
            _status.PointerMoved += OnPointerMove;
            _status.PointerExited += OnPointerExit;
        }

        public bool Matches(InventoryWindow window, InventoryStatusPanel status) => _window == window && _status == status;

        public void Invalidate() => _dirty = true;

        public void Invalidate(string status)
        {
            if (_hoveredStatus == status)
            {
                _dirty = true;
            }
        }

        public void Refresh(bool fullCondition)
        {
            _dirty |= _fullCondition != fullCondition;
            _fullCondition = fullCondition;
            if (string.IsNullOrEmpty(_hoveredStatus) || _window == null || _status == null)
            {
                return;
            }

            if (_window.IsOpen == false || _window.IsDragging || _window.HasModalWidget)
            {
                OnPointerExit();
                return;
            }

            if (_dirty == false)
            {
                return;
            }

            _formatter ??= new EquipmentStatusDetailsFormatter(_window);
            if (_formatter.TryDescribe(_hoveredStatus, _fullCondition, out string title, out string description) || _formatter.TryDescribeCondition(_hoveredStatus, _readCondition(), out title, out description))
            {
                _window.ShowTooltip(title, description, _position, _eventCamera);
            }

            _dirty = false;
        }

        public void Dispose()
        {
            if (_status != null)
            {
                _status.PointerEntered -= OnPointerEnter;
                _status.PointerMoved -= OnPointerMove;
                _status.PointerExited -= OnPointerExit;
            }

            OnPointerExit();
        }

        private void OnPointerEnter(string id, Vector2 position, Camera eventCamera)
        {
            _hoveredStatus = id;
            _position = position;
            _eventCamera = eventCamera;
            _dirty = true;
            Refresh(_fullCondition);
        }

        private void OnPointerMove(Vector2 position, Camera eventCamera)
        {
            _position = position;
            _eventCamera = eventCamera;
            if (string.IsNullOrEmpty(_hoveredStatus) == false && _window != null)
            {
                _window.MoveExternalTooltip(position, eventCamera);
            }
        }

        private void OnPointerExit()
        {
            _hoveredStatus = null;
            _dirty = false;
            if (_window != null)
            {
                _window.HideTooltip();
            }
        }
    }
}
