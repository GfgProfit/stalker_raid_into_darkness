using System;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [Serializable]
    public struct AnomalyEquipmentProtectionEntry
    {
        [SerializeField] private AnomalyDamageType _type;
        [SerializeField] private AnomalyProtectionValues _values;

        public AnomalyDamageType Type => _type;
        public AnomalyProtectionValues Values => _values;

        public AnomalyEquipmentProtectionEntry(AnomalyDamageType type, AnomalyProtectionValues values)
        {
            _type = type;
            _values = values;
        }
    }
}
