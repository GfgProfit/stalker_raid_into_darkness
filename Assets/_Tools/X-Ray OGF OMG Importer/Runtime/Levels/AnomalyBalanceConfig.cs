using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [CreateAssetMenu(menuName = "Raid Into Darkness/Anomalies/Balance")]
    public sealed class AnomalyBalanceConfig : ScriptableObject
    {
        private static readonly AnomalyDamageType[] DEFAULT_HELMET_DAMAGE_TYPES = { AnomalyDamageType.Burn, AnomalyDamageType.Shock, AnomalyDamageType.ChemicalBurn, AnomalyDamageType.Radiation, AnomalyDamageType.Telepatic, AnomalyDamageType.Strike, AnomalyDamageType.Wound, AnomalyDamageType.Explosion };

        [SerializeField] private AnimationCurve _conditionEfficiencyCurve = AnomalyProtectionMath.CreateDefaultCurve();
        [SerializeField, Min(0f)] private float _radiationRecoveryPerHour = 4f;
        [SerializeField, Min(0f)] private float _radiationDamagePerSecond = 0.2f;
        [SerializeField] private AnomalyDamageType[] _helmetDamageTypes = (AnomalyDamageType[])DEFAULT_HELMET_DAMAGE_TYPES.Clone();
        [SerializeField] private AnomalyArchetypeConfig[] _archetypes = System.Array.Empty<AnomalyArchetypeConfig>();
        [SerializeField] private AnomalyVariantConfig[] _variants = System.Array.Empty<AnomalyVariantConfig>();

        public float RadiationRecoveryPerHour => _radiationRecoveryPerHour;
        public float RadiationDamagePerSecond => _radiationDamagePerSecond;
        public float RadiationDecayRate => _radiationRecoveryPerHour / 360000f;
        public float RadiationHealthRate => _radiationDamagePerSecond;
        public IReadOnlyList<AnomalyArchetypeConfig> Archetypes => _archetypes;
        public IReadOnlyList<AnomalyVariantConfig> Variants => _variants;

        public static bool DefaultHelmetApplies(AnomalyDamageType type)
        {
            return System.Array.IndexOf(DEFAULT_HELMET_DAMAGE_TYPES, type) >= 0;
        }

        public bool HelmetApplies(AnomalyDamageType type)
        {
            return _helmetDamageTypes != null && System.Array.IndexOf(_helmetDamageTypes, type) >= 0;
        }

        public float EvaluateConditionEfficiency(float condition)
        {
            float value = float.IsFinite(condition) ? Mathf.Clamp01(condition) : 0f;
            return _conditionEfficiencyCurve != null ? Mathf.Clamp01(_conditionEfficiencyCurve.Evaluate(value * 100f) / 100f) : AnomalyProtectionMath.DefaultEfficiency(value);
        }

        public AnomalyVariantConfig FindVariant(string sourceSection)
        {
            for (int i = 0; i < _variants.Length; i++)
            {
                if (_variants[i] != null && string.Equals(_variants[i].SourceSection, sourceSection, System.StringComparison.OrdinalIgnoreCase))
                {
                    return _variants[i];
                }
            }
            return null;
        }

        public bool TryValidate(out string diagnostic)
        {
            diagnostic = null;
            if (_helmetDamageTypes == null || _archetypes == null || _variants == null)
            {
                diagnostic = "Anomaly balance collections must be assigned.";
                return false;
            }
            foreach (AnomalyDamageType type in _helmetDamageTypes)
            {
                if (!System.Enum.IsDefined(typeof(AnomalyDamageType), type) || type == AnomalyDamageType.None)
                {
                    diagnostic = "Anomaly helmet applicability must contain valid damage types.";
                    return false;
                }
            }
            if (!float.IsFinite(_radiationRecoveryPerHour) || _radiationRecoveryPerHour < 0f || !float.IsFinite(_radiationDamagePerSecond) || _radiationDamagePerSecond < 0f || _conditionEfficiencyCurve == null || _conditionEfficiencyCurve.length < 2)
            {
                diagnostic = "Anomaly balance condition curve or radiation rates are invalid.";
                return false;
            }
            Keyframe[] keys = _conditionEfficiencyCurve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = keys[i];
                if (!float.IsFinite(key.time) || !float.IsFinite(key.value) || !float.IsFinite(key.inTangent) || !float.IsFinite(key.outTangent) || !float.IsFinite(key.inWeight) || !float.IsFinite(key.outWeight))
                {
                    diagnostic = "Anomaly condition efficiency curve cannot contain non-finite values or stepped tangents.";
                    return false;
                }
                if (i > 0 && key.time > 90f && key.value <= keys[i - 1].value)
                {
                    diagnostic = "Anomaly condition efficiency cannot contain a plateau between 90 and 100 percent condition.";
                    return false;
                }
            }
            float previous = -1f;
            for (int i = 0; i <= 1000; i++)
            {
                float value = _conditionEfficiencyCurve.Evaluate(i * 0.1f);
                if (!float.IsFinite(value) || value < 0f || value > 100f || value + 0.0001f < previous || i > 900 && value <= previous || i == 0 && Mathf.Abs(value) > 0.0001f || i == 1000 && Mathf.Abs(value - 100f) > 0.0001f)
                {
                    diagnostic = "Anomaly condition efficiency curve must be continuous, monotone and span 0 to 100 percent.";
                    return false;
                }
                previous = value;
            }
            if (EvaluateConditionEfficiency(1f) <= EvaluateConditionEfficiency(0.95f) || EvaluateConditionEfficiency(0.95f) <= EvaluateConditionEfficiency(0.9f))
            {
                diagnostic = "Anomaly condition efficiency must decrease between 100, 95 and 90 percent condition.";
                return false;
            }
            var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _archetypes.Length; i++)
            {
                if (_archetypes[i] == null || !_archetypes[i].TryValidate(out diagnostic) || !ids.Add(_archetypes[i].Id))
                {
                    diagnostic ??= "Anomaly balance contains a missing or duplicate archetype.";
                    return false;
                }
            }
            ids.Clear();
            for (int i = 0; i < _variants.Length; i++)
            {
                if (_variants[i] == null || !_variants[i].TryValidate(out diagnostic) || System.Array.IndexOf(_archetypes, _variants[i].Archetype) < 0 || !ids.Add(_variants[i].SourceSection))
                {
                    diagnostic ??= "Anomaly balance contains an unregistered archetype or missing/duplicate variant.";
                    return false;
                }
            }
            return true;
        }
    }
}
