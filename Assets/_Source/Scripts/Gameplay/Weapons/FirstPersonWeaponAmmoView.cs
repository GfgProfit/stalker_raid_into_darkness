using TMPro;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    public sealed class FirstPersonWeaponAmmoView : MonoBehaviour
    {
        [SerializeField] private FirstPersonWeaponController _controller;
        [SerializeField] private TMP_Text _text;
        [SerializeField] private GameObject _root;

        public TMP_Text Text => _text;

        private void OnEnable()
        {
            if (_controller == null)
            {
                return;
            }

            _controller.AmmoChanged += OnAmmoChanged;
            _controller.StateChanged += OnStateChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (_controller == null)
            {
                return;
            }

            _controller.AmmoChanged -= OnAmmoChanged;
            _controller.StateChanged -= OnStateChanged;
        }

        private void OnAmmoChanged(int magazine, int reserve)
        {
            Refresh();
        }

        private void OnStateChanged(FirstPersonWeaponState state)
        {
            Refresh();
        }

        private void Refresh()
        {
            bool visible = _controller.State != FirstPersonWeaponState.Hidden && _controller.ActiveWeapon is { IsMelee: false };
            GameObject root = _root != null ? _root : _text != null ? _text.gameObject : null;
            if (root != null && root != gameObject && root.activeSelf != visible)
            {
                root.SetActive(visible);
            }

            if (visible && _text != null)
            {
                _controller.GetHudAmmo(out int loaded, out int reserve, out string name);
                _text.text = $"{loaded}/{reserve} <size=65%>{name}</size>";
            }
        }
    }
}
