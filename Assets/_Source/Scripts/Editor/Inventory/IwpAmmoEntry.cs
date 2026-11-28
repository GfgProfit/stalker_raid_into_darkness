using System;
using RaidIntoDarkness.Weapons;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    [Serializable]
    internal sealed class IwpAmmoEntry
    {
        [SerializeField] private string _section;
        [SerializeField] private string _model;
        [SerializeField] private string _displayName;
        [SerializeField] private string _shortName;
        [SerializeField] private string _description;
        [SerializeField] private int _boxSize;
        [SerializeField] private float _boxWeight;
        [SerializeField] private int _gridWidth;
        [SerializeField] private int _gridHeight;
        [SerializeField] private int _cost;
        [SerializeField] private string _takeSoundCategory;
        [SerializeField] private float _armorPiercing;
        [SerializeField] private float _hitMultiplier;
        [SerializeField] private float _impulseMultiplier;
        [SerializeField] private float _speedMultiplier;
        [SerializeField] private float _dispersionMultiplier;
        [SerializeField] private float _airResistance;
        [SerializeField] private BallisticAmmoData _data;

        public string Section => _section;
        public string Model => _model;
        public string DisplayName => _displayName;
        public string ShortName => _shortName;
        public string Description => _description;
        public int BoxSize => _boxSize;
        public float BoxWeight => _boxWeight;
        public int GridWidth => _gridWidth;
        public int GridHeight => _gridHeight;
        public int Cost => _cost;
        public string TakeSoundCategory => _takeSoundCategory;
        public float ArmorPiercing => _armorPiercing;
        public float HitMultiplier => _hitMultiplier;
        public float ImpulseMultiplier => _impulseMultiplier;
        public float SpeedMultiplier => _speedMultiplier;
        public float DispersionMultiplier => _dispersionMultiplier;
        public float AirResistance => _airResistance;
        public BallisticAmmoData Data => _data;
    }
}
