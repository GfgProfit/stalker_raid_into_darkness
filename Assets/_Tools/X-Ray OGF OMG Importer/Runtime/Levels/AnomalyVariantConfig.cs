using UnityEngine;

namespace XRay.Importer.Levels
{
    [CreateAssetMenu(menuName = "Raid Into Darkness/Anomalies/Variant")]
    public sealed class AnomalyVariantConfig : ScriptableObject
    {
        [SerializeField] private string _sourceSection;
        [SerializeField] private AnomalyArchetypeConfig _archetype;
        [SerializeField] private AnomalyStrength _strength = AnomalyStrength.Average;
        [SerializeField, Min(0f)] private float _damageMultiplier = 1f;
        [SerializeField] private bool _continuous;
        [SerializeField, Min(0.001f)] private float _hitInterval = 0.1f;

        public string SourceSection => _sourceSection;
        public AnomalyArchetypeConfig Archetype => _archetype;
        public AnomalyStrength Strength => _strength;
        public float DamageMultiplier => _damageMultiplier;
        public float InputDamage => _archetype != null ? _archetype.BaseDamage * _damageMultiplier : 0f;
        public bool Continuous => _continuous;
        public float HitInterval => _hitInterval;

        public bool TryValidate(out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(_sourceSection) || _archetype == null)
            {
                diagnostic = "Anomaly variant requires its source section and shared archetype.";
            }
            else if (!_archetype.TryValidate(out diagnostic))
            {
                return false;
            }
            else if (!System.Enum.IsDefined(typeof(AnomalyStrength), _strength) || !float.IsFinite(_damageMultiplier) || _damageMultiplier < 0f || !float.IsFinite(InputDamage))
            {
                diagnostic = "Anomaly variant strength and damage multiplier are invalid.";
            }
            else if (!float.IsFinite(_hitInterval) || _hitInterval <= 0f)
            {
                diagnostic = "Anomaly hit interval must be finite and positive.";
            }
            return diagnostic == null;
        }
    }
}
