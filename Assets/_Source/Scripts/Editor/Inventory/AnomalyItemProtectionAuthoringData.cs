using System;

namespace RaidIntoDarkness.Inventory.Editor
{
    [Serializable]
    internal sealed class AnomalyItemProtectionAuthoringData
    {
        public string assetPath;
        public AnomalyProtectionAuthoringData[] entries;
    }
}
