using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class DevTest : MonoBehaviour
    {
        [SerializeField, InspectorName("inZoom")] private bool _inZoom;

        private FirstPersonWeaponController _controller;

        private void OnEnable()
        {
            _controller = GetComponent<FirstPersonWeaponController>();
            if (_controller == null)
            {
                Debug.LogError("DevTest must be attached to the player's FirstPersonWeaponController object.", this);
                enabled = false;
                return;
            }

            _controller.DevelopmentAimOverride = _inZoom;
        }

        private void Update()
        {
            if (_controller != null)
            {
                _controller.DevelopmentAimOverride = _inZoom;
            }
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.DevelopmentAimOverride = false;
            }
        }
    }
}
