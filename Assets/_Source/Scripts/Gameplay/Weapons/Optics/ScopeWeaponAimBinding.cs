using UnityEngine;

namespace RaidIntoDarkness.Weapons.Optics
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class ScopeWeaponAimBinding : MonoBehaviour
    {
        [SerializeField] private FirstPersonWeaponController _weaponController;
        [SerializeField] private ScopeOpticsRenderer[] _scopes;
        [SerializeField] private int _selectedScopeIndex = -1;
        [SerializeField, Range(0f, 1f)] private float _activationAimFactor = 0.95f;

        private bool _diagnosticLogged;

        public int SelectedScopeIndex => _selectedScopeIndex;

        public void SelectScope(int index)
        {
            DeactivateScopes();
            _selectedScopeIndex = _scopes != null && index >= 0 && index < _scopes.Length ? index : -1;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_weaponController == null || _scopes == null || _scopes.Length == 0)
            {
                DeactivateScopes();
                if (!_diagnosticLogged)
                {
                    Debug.LogWarning("ScopeWeaponAimBinding requires an explicit weapon controller and scopes. Assign the references manually.", this);
                    _diagnosticLogged = true;
                }

                return;
            }

            bool aiming = _weaponController.isActiveAndEnabled && _weaponController.Model != null && _weaponController.State == FirstPersonWeaponState.Ready && !_weaponController.IsGrenadeMode && _weaponController.IsAiming && _weaponController.AimFactor >= _activationAimFactor;
            for (int i = 0; i < _scopes.Length; i++)
            {
                ScopeOpticsRenderer scope = _scopes[i];
                if (scope != null)
                {
                    scope.SetActive(aiming && i == _selectedScopeIndex);
                }
            }
        }

        private void OnDisable()
        {
            DeactivateScopes();
        }

        private void DeactivateScopes()
        {
            if (_scopes == null)
            {
                return;
            }

            for (int i = 0; i < _scopes.Length; i++)
            {
                if (_scopes[i] != null)
                {
                    _scopes[i].SetActive(false);
                }
            }
        }
    }
}
