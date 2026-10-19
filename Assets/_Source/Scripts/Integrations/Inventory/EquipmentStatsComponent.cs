using System;
using Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public enum EquipmentStat
    {
        FireWound = 0,
        HelmetFireWound = 1,
        Wound = 2,
        Strike = 3,
        Explosion = 4,
        Radiation = 5,
        Telepatic = 6,
        Burn = 7,
        Shock = 8,
        ChemicalBurn = 9,
        ArtefactCount = 10,
        AdditionalWeight = 11,
        Speed = 12,
        PhysicalDurability = 13,
        AnomalyDurability = 14
    }

    [Serializable]
    public sealed class EquipmentStatsComponent : ItemDefinitionComponent
    {
        [SerializeField] private string _section;
        [SerializeField] private int _cost;
        [SerializeField] private string _repairType;
        [SerializeField] private string _community;
        [SerializeField] private float _burnProtection;
        [SerializeField] private float _shockProtection;
        [SerializeField] private float _chemicalBurnProtection;
        [SerializeField] private float _radiationProtection;
        [SerializeField] private float _telepaticProtection;
        [SerializeField] private float _woundProtection;
        [SerializeField] private float _strikeProtection;
        [SerializeField] private float _explosionProtection;
        [SerializeField] private float _fireWoundProtection;
        [SerializeField] private float _helmetFireWoundProtection;
        [SerializeField] private int _artefactCount;
        [SerializeField] private float _additionalWeight;
        [SerializeField] private float _powerLoss;
        [SerializeField] private bool _helmetAvailable = true;
        [SerializeField] private bool _sprintAllowed = true;
        [SerializeField] private string _nightVisionSection;
        [SerializeField] private float _speed = 1f;
        [SerializeField] private float _physicalDurability;
        [SerializeField] private float _anomalyDurability;

        public string Section => _section ?? string.Empty;
        public int Cost => _cost;
        public string RepairType => _repairType ?? string.Empty;
        public string Community => _community ?? string.Empty;
        public float BurnProtection => _burnProtection;
        public float ShockProtection => _shockProtection;
        public float ChemicalBurnProtection => _chemicalBurnProtection;
        public float RadiationProtection => _radiationProtection;
        public float TelepaticProtection => _telepaticProtection;
        public float WoundProtection => _woundProtection;
        public float StrikeProtection => _strikeProtection;
        public float ExplosionProtection => _explosionProtection;
        public float FireWoundProtection => _fireWoundProtection;
        public float HelmetFireWoundProtection => _helmetFireWoundProtection;
        public int ArtefactCount => _artefactCount;
        public float AdditionalWeight => _additionalWeight;
        public float PowerLoss => _powerLoss;
        public bool HelmetAvailable => _helmetAvailable;
        public bool SprintAllowed => _sprintAllowed;
        public string NightVisionSection => _nightVisionSection ?? string.Empty;
        public float Speed => _speed;
        public float PhysicalDurability => _physicalDurability;
        public float AnomalyDurability => _anomalyDurability;

        public float GetValue(EquipmentStat stat) => stat switch
        {
            EquipmentStat.FireWound => _fireWoundProtection,
            EquipmentStat.HelmetFireWound => _helmetAvailable ? 0f : _helmetFireWoundProtection,
            EquipmentStat.Wound => _woundProtection,
            EquipmentStat.Strike => _strikeProtection,
            EquipmentStat.Explosion => _explosionProtection,
            EquipmentStat.Radiation => _radiationProtection,
            EquipmentStat.Telepatic => _telepaticProtection,
            EquipmentStat.Burn => _burnProtection,
            EquipmentStat.Shock => _shockProtection,
            EquipmentStat.ChemicalBurn => _chemicalBurnProtection,
            EquipmentStat.ArtefactCount => _artefactCount,
            EquipmentStat.AdditionalWeight => _additionalWeight,
            EquipmentStat.Speed => _speed,
            EquipmentStat.PhysicalDurability => _physicalDurability,
            EquipmentStat.AnomalyDurability => _anomalyDurability,
            _ => 0f
        };
    }
}
