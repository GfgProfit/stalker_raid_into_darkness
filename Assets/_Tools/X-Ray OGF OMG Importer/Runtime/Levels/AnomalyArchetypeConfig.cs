using UnityEngine;

namespace XRay.Importer.Levels
{
    [CreateAssetMenu(menuName = "Raid Into Darkness/Anomalies/Archetype")]
    public sealed class AnomalyArchetypeConfig : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private AnomalyDamageType _damageType;
        [SerializeField, Min(0f)] private float _baseDamage;
        [SerializeField, Range(0f, 100f)] private float _equipmentWearPerHitPercent;

        public string Id => _id;
        public AnomalyDamageType DamageType => _damageType;
        public float BaseDamage => _baseDamage;
        public float EquipmentWearPerHitPercent => _equipmentWearPerHitPercent;

        public bool TryValidate(out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(_id))
            {
                diagnostic = "Anomaly archetype requires an ID.";
            }
            else if (!System.Enum.IsDefined(typeof(AnomalyDamageType), _damageType) || !float.IsFinite(_baseDamage) || _baseDamage < 0f)
            {
                diagnostic = "Anomaly archetype requires a valid damage type and finite non-negative base damage.";
            }
            else if (!float.IsFinite(_equipmentWearPerHitPercent) || _equipmentWearPerHitPercent < 0f || _equipmentWearPerHitPercent > 100f)
            {
                diagnostic = "Anomaly equipment wear per hit must be between 0 and 100 percentage points.";
            }
            return diagnostic == null;
        }
    }
}
