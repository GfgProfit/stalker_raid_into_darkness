using System;

namespace RaidIntoDarkness.Inventory.Editor
{
    [Serializable]
    internal sealed class AnomalyVariantAuthoringData
    {
        public string section;
        public string familyId;
        public string prefabPath;
        public string strength;
        public float multiplier;
        public bool continuous;
        public float interval;
    }
}
