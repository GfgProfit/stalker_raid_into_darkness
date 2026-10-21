using System;
using Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public enum WeaponAddonKind
    {
        Silencer = 0,
        Scope = 1,
        GrenadeLauncher = 2
    }

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
        [SerializeField] private AudioClip _attachSound;
        [SerializeField] private AudioClip _detachSound;

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
        public AudioClip AttachSound => _attachSound;
        public AudioClip DetachSound => _detachSound;
    }
}
