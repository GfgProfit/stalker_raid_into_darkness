using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Inventory.UI
{
    [Serializable]
    public sealed class InventoryInputBindings
    {
        [SerializeField] private InputActionReference _rotateItem;
        [SerializeField] private InputActionReference _transferModifier;

        public InputActionReference RotateItem => _rotateItem;
        public InputActionReference TransferModifier => _transferModifier;
        public bool RotatePressed => WasPressed(_rotateItem);
        public bool TransferHeld => IsHeld(_transferModifier);
        public bool TransferPressed => WasPressed(_transferModifier);

        public void Set(InputActionReference rotateItem, InputActionReference transferModifier)
        {
            _rotateItem = rotateItem;
            _transferModifier = transferModifier;
        }

        public void Enable()
        {
            Enable(_rotateItem);
            Enable(_transferModifier);
        }

        private static void Enable(InputActionReference reference)
        {
            if (reference != null && reference.action != null && reference.action.enabled == false)
            {
                reference.action.Enable();
            }
        }

        private static bool WasPressed(InputActionReference reference) => reference != null && reference.action != null && reference.action.WasPressedThisFrame();
        private static bool IsHeld(InputActionReference reference) => reference != null && reference.action != null && reference.action.IsPressed();
    }
}
