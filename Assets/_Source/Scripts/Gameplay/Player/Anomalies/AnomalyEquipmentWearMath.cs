using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public static class AnomalyEquipmentWearMath
    {
        public static bool TryCalculateRemainingCondition(float condition, float baseWearPercent, float durability, out float remainingCondition)
        {
            remainingCondition = condition;
            if (!float.IsFinite(condition) || !float.IsFinite(baseWearPercent) || !float.IsFinite(durability) || baseWearPercent < 0f || baseWearPercent > 100f)
            {
                return false;
            }
            remainingCondition = Mathf.Max(0f, Mathf.Clamp01(condition) - baseWearPercent / (100f + Mathf.Clamp(durability, 0f, 100f)));
            return true;
        }
    }
}
