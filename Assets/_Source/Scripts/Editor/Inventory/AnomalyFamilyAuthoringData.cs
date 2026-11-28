using System;

namespace RaidIntoDarkness.Inventory.Editor
{
    [Serializable]
    internal sealed class AnomalyFamilyAuthoringData
    {
        public string id;
        public string damageType;
        public float baseDamage;
        public float equipmentWearPerHitPercent;
    }
}
