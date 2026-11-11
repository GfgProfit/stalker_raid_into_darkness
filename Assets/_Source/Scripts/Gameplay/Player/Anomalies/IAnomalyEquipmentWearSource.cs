using System.Collections.Generic;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    public readonly struct AnomalyEquipmentWearTarget
    {
        public AnomalyEquipmentWearTarget(IAnomalyEquipmentWearSource source, string itemId, float condition, float anomalyDurability)
        {
            Source = source;
            ItemId = itemId;
            Condition = condition;
            AnomalyDurability = anomalyDurability;
        }

        public IAnomalyEquipmentWearSource Source { get; }
        public string ItemId { get; }
        public float Condition { get; }
        public float AnomalyDurability { get; }
    }

    public interface IAnomalyEquipmentWearSource
    {
        void CaptureEquipmentWear(AnomalyDamageType type, AnomalyBalanceConfig config, List<AnomalyEquipmentWearTarget> targets);
        void ApplyEquipmentWear(AnomalyEquipmentWearTarget target, float remainingCondition);
    }
}
