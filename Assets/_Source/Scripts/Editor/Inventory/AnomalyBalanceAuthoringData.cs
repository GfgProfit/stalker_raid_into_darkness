using System;

namespace RaidIntoDarkness.Inventory.Editor
{
    [Serializable]
    internal sealed class AnomalyBalanceAuthoringData
    {
        public string[] helmetDamageTypes;
        public AnomalyFamilyAuthoringData[] families;
        public AnomalyVariantAuthoringData[] variants;
        public float radiationRecoveryPerHour;
        public float radiationDamagePerSecond;
        public AnomalyProtectionCurvePoint[] protectionEfficiencyCurve;
        public AnomalyItemProtectionAuthoringData[] itemProtections;
        public AnomalyPrefabBindingAuthoringData[] prefabBindings;
    }
}
