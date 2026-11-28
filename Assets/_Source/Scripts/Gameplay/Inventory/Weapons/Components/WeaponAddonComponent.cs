using System;
using RaidIntoDarkness.Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    [Serializable]
    public sealed class WeaponAddonComponent : ItemDefinitionComponent
    {
        [SerializeField] private string _section;
        [SerializeField] private WeaponAddonKind _kind;
        [SerializeField] private int _cost;
        [SerializeField] private float _bulletHitPowerFactor = 1f;
        [SerializeField] private float _bulletHitImpulseFactor = 1f;
        [SerializeField] private float _bulletSpeedFactor = 1f;
        [SerializeField] private float _fireDispersionBaseFactor = 1f;
        [SerializeField] private float _camDispersionFactor = 1f;
        [SerializeField] private float _camDispersionIncrementFactor = 1f;
        [SerializeField] private float _scopeZoomFactor;
        [SerializeField] private bool _overrideAimOffsetPosition;
        [SerializeField] private Vector3 _aimOffsetPosition;
        [SerializeField] private AudioClip _attachSound;
        [SerializeField] private AudioClip _detachSound;
        [SerializeField] private ItemDefinition _grenadeAmmo;
        [SerializeField] private ItemDefinition _alternativeGrenadeAmmo;
        [SerializeField] private Vector3 _recoilIncreasePercent;
        [SerializeField] private float _recoilSnappinessIncreasePercent;
        [SerializeField, Range(0f, 100f)] private float _recoilDampingPercent;

        public string Section => _section ?? string.Empty;
        public WeaponAddonKind Kind => _kind;
        public int Cost => _cost;
        public float BulletHitPowerFactor => _bulletHitPowerFactor;
        public float BulletHitImpulseFactor => _bulletHitImpulseFactor;
        public float BulletSpeedFactor => _bulletSpeedFactor;
        public float FireDispersionBaseFactor => _fireDispersionBaseFactor;
        public float CamDispersionFactor => _camDispersionFactor;
        public float CamDispersionIncrementFactor => _camDispersionIncrementFactor;
        public float ScopeZoomFactor => _scopeZoomFactor;
        public bool OverrideAimOffsetPosition => _overrideAimOffsetPosition;
        public Vector3 AimOffsetPosition => _aimOffsetPosition;
        public AudioClip AttachSound => _attachSound;
        public AudioClip DetachSound => _detachSound;
        public ItemDefinition GrenadeAmmo => _grenadeAmmo;
        public ItemDefinition AlternativeGrenadeAmmo => _alternativeGrenadeAmmo;
        public Vector3 RecoilIncreasePercent => _recoilIncreasePercent;
        public float RecoilSnappinessIncreasePercent => _recoilSnappinessIncreasePercent;
        public float RecoilDampingPercent => _recoilDampingPercent;
        public Vector3 RecoilMultiplier => Kind == WeaponAddonKind.Silencer ? Vector3.one * RecoilDampingMultiplier : Vector3.one + _recoilIncreasePercent / 100f;
        public float RecoilSnappinessMultiplier => Kind == WeaponAddonKind.Silencer ? RecoilDampingMultiplier : 1f + _recoilSnappinessIncreasePercent / 100f;

        private float RecoilDampingMultiplier => 1f - _recoilDampingPercent / 100f;

        internal static (Vector3 recoil, float snappiness) GetEquippedRecoilMultipliers(WeaponAddonComponent silencer, WeaponAddonComponent launcher)
        {
            Vector3 recoil = Vector3.Scale(silencer?.RecoilMultiplier ?? Vector3.one, launcher?.RecoilMultiplier ?? Vector3.one);
            float snappiness = (silencer?.RecoilSnappinessMultiplier ?? 1f) * (launcher?.RecoilSnappinessMultiplier ?? 1f);
            return (recoil, snappiness);
        }

        public override float GetAdditionalWeight(ItemInstance item, IItemDefinitionResolver resolver) => GetLoadedGrenade(item)?.Weight ?? 0f;

        public bool SupportsGrenadeAmmo(ItemDefinition definition) => definition != null && (definition == _grenadeAmmo || definition == _alternativeGrenadeAmmo);

        public ItemDefinition GetLoadedGrenade(ItemInstance item)
        {
            string roundId = item?.GetState(InventoryWeaponAttachments.GRENADE_ROUND_STATE);

            if (_grenadeAmmo != null && roundId == _grenadeAmmo.DefinitionId)
            {
                return _grenadeAmmo;
            }

            return _alternativeGrenadeAmmo != null && roundId == _alternativeGrenadeAmmo.DefinitionId ? _alternativeGrenadeAmmo : null;
        }
    }
}
