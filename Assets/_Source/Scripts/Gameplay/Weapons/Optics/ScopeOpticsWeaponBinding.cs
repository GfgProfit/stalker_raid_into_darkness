using UnityEngine;

namespace RaidIntoDarkness.Weapons.Optics
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class ScopeOpticsWeaponBinding : MonoBehaviour, IFirstPersonWeaponOptics
    {
        [SerializeField] private ScopeOpticsRenderer _renderer;
        [SerializeField, Range(0f, 1f)] private float _activationAimFactor = 0.95f;
        [SerializeField, Min(0.1f)] private float _magnification = 8f;
        [SerializeField, Min(0.001f)] private float _magnificationPerScrollNotch = 0.35f;

        private FirstPersonWeaponController _controller;
        private Camera _viewerCamera;

        public ScopeOpticsRenderer Renderer => _renderer;
        public float Magnification => _magnification;
        public float AimHudFieldOfView => UsesOpticalMagnification ? _renderer.Configuration.AimHudFieldOfView : 0f;
        public bool UsesOpticalMagnification => isActiveAndEnabled && IsOwned() && _controller.isActiveAndEnabled && _renderer != null && _renderer.enabled && _renderer.Configuration != null;

        public void Bind(FirstPersonWeaponController controller, Camera viewer)
        {
            _controller = controller;
            _viewerCamera = viewer;
            if (_renderer == null)
            {
                if (controller != null)
                {
                    Debug.LogWarning("ScopeOpticsWeaponBinding requires an explicitly assigned Renderer.", this);
                }

                return;
            }

            _renderer.SetActive(false);
            _renderer.enabled = false;
            _renderer.BindViewerCamera(viewer, controller != null ? controller.HandsModel : null);
            if (controller != null && viewer != null)
            {
                ApplySettings(true);
                _renderer.enabled = true;
            }
        }

        public void SetMagnification(float magnification)
        {
            _magnification = magnification;
            if (IsOwned())
            {
                ApplySettings(false);
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || _renderer == null)
            {
                return;
            }

            _renderer.UpdateLensEnvironment();
            if (_controller == null)
            {
                return;
            }

            if (!UsesOpticalMagnification || _controller.IsGrenadeMode)
            {
                _renderer.SetActive(false);
                return;
            }

            bool aiming = _controller.State == FirstPersonWeaponState.Ready && _controller.IsAiming && _controller.AimFactor >= ScopeOpticsMath.FiniteClamp(_activationAimFactor, 0f, 1f, 0.95f);
            if (aiming)
            {
                ApplyMagnificationScroll(_controller.ScopeZoomScroll);
            }

            ApplySettings(false);
            _renderer.SetAimVisibility(ScopeOpticsMath.GetAimVisibility(_controller.AimFactor, _activationAimFactor));
        }

        public void ApplyMagnificationScroll(float scroll)
        {
            if (!UsesOpticalMagnification || _controller.IsGrenadeMode || _controller.State != FirstPersonWeaponState.Ready || !_controller.IsAiming || _controller.AimFactor < ScopeOpticsMath.FiniteClamp(_activationAimFactor, 0f, 1f, 0.95f) || !float.IsFinite(scroll))
            {
                return;
            }

            float sensitivity = ScopeOpticsMath.FiniteClamp(_magnificationPerScrollNotch, 0.001f, float.MaxValue, 0.35f);
            ScopeOpticsConfig config = _renderer.Configuration;
            if (config.HasDiscreteMagnification)
            {
                SetMagnification(config.GetNextMagnificationStep(_magnification, scroll));
                return;
            }

            float current = ScopeOpticsMath.ClampMagnification(_magnification, config.MinMagnification, config.MaxMagnification);
            float logarithm = Mathf.Clamp(Mathf.Log(current) + scroll * Mathf.Log(1f + sensitivity), Mathf.Log(config.MinMagnification), Mathf.Log(config.MaxMagnification));
            SetMagnification(Mathf.Exp(logarithm));
        }

        private void OnDisable()
        {
            if (_renderer != null)
            {
                _renderer.SetActive(false);
            }
        }

        private bool IsOwned()
        {
            return _controller != null && _viewerCamera != null && _controller.Model != null && transform.IsChildOf(_controller.Model.transform);
        }

        private void ApplySettings(bool immediate)
        {
            if (_renderer == null || _renderer.Configuration == null)
            {
                return;
            }

            ScopeOpticsConfig config = _renderer.Configuration;
            _magnification = config.ResolveMagnification(_magnification);
            _renderer.SetMagnification(_magnification, immediate);
        }

        [ContextMenu("Increase Magnification")]
        private void IncreaseMagnification()
        {
            SetMagnification(_magnification + 1f);
        }

        [ContextMenu("Decrease Magnification")]
        private void DecreaseMagnification()
        {
            SetMagnification(_magnification - 1f);
        }
    }
}
