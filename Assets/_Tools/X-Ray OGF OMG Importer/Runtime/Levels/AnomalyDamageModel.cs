using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public enum AnomalyStrength
    {
        Weak,
        Average,
        Strong
    }

    public enum AnomalyDamageType
    {
        None,
        Burn,
        Shock,
        ChemicalBurn,
        Radiation,
        Telepatic,
        Strike,
        Wound,
        Explosion
    }

    [Serializable]
    public struct AnomalyProtectionValues
    {
        [SerializeField, Range(0f, 100f)] private float _weak;
        [SerializeField, Range(0f, 100f)] private float _average;
        [SerializeField, Range(0f, 100f)] private float _strong;

        public AnomalyProtectionValues(float weak, float average, float strong)
        {
            _weak = weak;
            _average = average;
            _strong = strong;
        }

        public float Get(AnomalyStrength strength)
        {
            return strength switch
            {
                AnomalyStrength.Weak => _weak,
                AnomalyStrength.Average => _average,
                AnomalyStrength.Strong => _strong,
                _ => throw new ArgumentOutOfRangeException(nameof(strength))
            };
        }
    }

    public static class AnomalyProtectionMath
    {
        private static readonly Vector2[] CONDITION_POINTS = { new Vector2(0f, 0f), new Vector2(50f, 35f), new Vector2(70f, 62f), new Vector2(80f, 78f), new Vector2(85f, 86f), new Vector2(90f, 93f), new Vector2(95f, 97f), new Vector2(100f, 100f) };

        public static AnimationCurve CreateDefaultCurve()
        {
            var keys = new Keyframe[CONDITION_POINTS.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                Vector2 point = CONDITION_POINTS[i];
                float incoming = i > 0 ? (point.y - CONDITION_POINTS[i - 1].y) / (point.x - CONDITION_POINTS[i - 1].x) : 0f;
                float outgoing = i + 1 < keys.Length ? (CONDITION_POINTS[i + 1].y - point.y) / (CONDITION_POINTS[i + 1].x - point.x) : 0f;
                keys[i] = new Keyframe(point.x, point.y, incoming, outgoing);
            }
            return new AnimationCurve(keys);
        }

        public static float DefaultEfficiency(float condition)
        {
            float percent = float.IsFinite(condition) ? Mathf.Clamp01(condition) * 100f : 0f;
            for (int i = 1; i < CONDITION_POINTS.Length; i++)
            {
                if (percent <= CONDITION_POINTS[i].x)
                {
                    Vector2 start = CONDITION_POINTS[i - 1];
                    Vector2 end = CONDITION_POINTS[i];
                    return Mathf.Lerp(start.y, end.y, (percent - start.x) / (end.x - start.x)) / 100f;
                }
            }
            return 1f;
        }

        public static float Effective(float nominalPercent, float condition, AnomalyBalanceConfig config)
        {
            float efficiency = config != null ? config.EvaluateConditionEfficiency(condition) : DefaultEfficiency(condition);
            return Mathf.Clamp01(nominalPercent / 100f) * efficiency;
        }

        public static float Combine(float currentFraction, float nextFraction)
        {
            return 1f - (1f - Mathf.Clamp01(currentFraction)) * (1f - Mathf.Clamp01(nextFraction));
        }
    }

    public interface IXRayAnomalyDamageSource
    {
        void ConfigureDamage(AnomalyVariantConfig variant);
    }
}
