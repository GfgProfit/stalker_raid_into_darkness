using TMPro;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    public sealed class FirstPersonWeaponAmmoView : MonoBehaviour
    {
        private const string FORMAT = "{0}/{1}";

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
            bool visible = _controller.State != FirstPersonWeaponState.Hidden && _controller.ActiveWeapon != null;
            GameObject root = _root != null ? _root : _text != null ? _text.gameObject : null;
            if (root != null && root != gameObject && root.activeSelf != visible)
            {
                root.SetActive(visible);
            }

            if (visible && _text != null)
            {
                _text.SetText(FORMAT, _controller.Magazine, _controller.Reserve);
            }
        }
    }
}
