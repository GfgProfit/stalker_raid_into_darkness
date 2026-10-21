using System;
using Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [Serializable]
    public sealed class AmmoStatsComponent : ItemDefinitionComponent
    {
        [SerializeField] private string _section;
        [SerializeField] private int _boxSize;
        [SerializeField] private float _boxWeight;
        [SerializeField] private int _cost;
        [SerializeField] private float _armorPiercing;
        [SerializeField] private float _hitMultiplier;
        [SerializeField] private float _impulseMultiplier;
        [SerializeField] [HideInInspector] private float _speedMultiplier;
        [SerializeField] [HideInInspector] private float _dispersionMultiplier;
        [SerializeField] [HideInInspector] private float _airResistance;
        [SerializeField] private int _buckshot;
        [SerializeField] private float _averageWeaponHitPower;
        [SerializeField] private float _monsterDamageMultiplier;
        [SerializeField] private int _penetration;
        [SerializeField] private int _damagePerProjectile;
        [SerializeField] private bool _hasTooltipParameters;

        public string Section => _section ?? string.Empty;
        public int BoxSize => _boxSize;
        public float BoxWeight => _boxWeight;
        public int Cost => _cost;
        public float ArmorPiercing => _armorPiercing;
        public float HitMultiplier => _hitMultiplier;
        public float ImpulseMultiplier => _impulseMultiplier;
        public float SpeedMultiplier => _speedMultiplier;
        public float DispersionMultiplier => _dispersionMultiplier;
        public float AirResistance => _airResistance;
        public int Buckshot => _buckshot;
        public float AverageWeaponHitPower => _averageWeaponHitPower;
        public float MonsterDamageMultiplier => _monsterDamageMultiplier;
        public int Penetration => _penetration;
        public int DamagePerProjectile => _damagePerProjectile;
        public bool HasTooltipParameters => _hasTooltipParameters;
    }
}
