using System;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Inventory.Domain
{
    [Serializable]
    public sealed class EquipmentStatsComponent : ItemDefinitionComponent
    {
        private const float COMMUNITY_ICON_SCALE = 1.2f;

        [SerializeField] private string _section;
        [SerializeField] private int _cost;
        [SerializeField] private string _repairType;
        [SerializeField] private string _community;
        [SerializeField] private Sprite _communityIcon;
        [SerializeField] private Vector2 _communityIconSize;
        [SerializeField] [Range(0f, 100f)] private float _burnProtection;
        [SerializeField] [Range(0f, 100f)] private float _shockProtection;
        [SerializeField] [Range(0f, 100f)] private float _chemicalBurnProtection;
        [SerializeField] [Range(0f, 100f)] private float _radiationProtection;
        [SerializeField] [Range(0f, 100f)] private float _telepaticProtection;
        [SerializeField] [Range(0f, 100f)] private float _woundProtection;
        [SerializeField] [Range(0f, 100f)] private float _strikeProtection;
        [SerializeField] [Range(0f, 100f)] private float _explosionProtection;
        [SerializeField] [Range(0f, 100f)] private float _fireWoundProtection;
        [SerializeField] [Range(0f, 100f)] private float _helmetFireWoundProtection;
        [SerializeField] private int _artefactCount;
        [SerializeField] private float _additionalWeight;
        [SerializeField] private float _powerLoss;
        [SerializeField] private bool _helmetAvailable = true;
        [SerializeField] private bool _sprintAllowed = true;
        [SerializeField] private string _nightVisionSection;
        [SerializeField] private float _speed = 1f;
        [SerializeField] [Range(0f, 100f)] private float _physicalDurability;
        [SerializeField] [Range(0f, 100f)] private float _anomalyDurability;
        [SerializeField] private AnomalyEquipmentProtectionEntry[] _anomalyProtections = Array.Empty<AnomalyEquipmentProtectionEntry>();

        public string Section => _section ?? string.Empty;
        public int Cost => _cost;
        public string RepairType => _repairType ?? string.Empty;
        public string Community => _community ?? string.Empty;
        public Sprite CommunityIcon => _communityIcon;
        public Vector2 CommunityIconSize => _communityIconSize * COMMUNITY_ICON_SCALE;
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

        public float GetAnomalyProtection(AnomalyDamageType type, AnomalyStrength strength)
        {
            if (_anomalyProtections != null)
            {
                for (int i = 0; i < _anomalyProtections.Length; i++)
                {
                    if (_anomalyProtections[i].Type == type)
                    {
                        return _anomalyProtections[i].Values.Get(strength);
                    }
                }
            }

            return type switch
            {
                AnomalyDamageType.Burn => _burnProtection,
                AnomalyDamageType.Shock => _shockProtection,
                AnomalyDamageType.ChemicalBurn => _chemicalBurnProtection,
                AnomalyDamageType.Radiation => _radiationProtection,
                AnomalyDamageType.Telepatic => _telepaticProtection,
                AnomalyDamageType.Strike => _strikeProtection,
                AnomalyDamageType.Wound => _woundProtection,
                AnomalyDamageType.Explosion => _explosionProtection,
                _ => 0f
            };
        }

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
