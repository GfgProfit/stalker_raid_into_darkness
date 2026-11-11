using System.Globalization;
using Inventory;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public static class AnomalyEquipmentProtection
    {
        public const string OUTFIT_CATEGORY = "Armor";
        public const string HELMET_CATEGORY = "Head";
        public const string BACKPACK_CATEGORY = "Backpack";
        public const string CONDITION_STATE = "condition";

        public static float GetCondition(ItemInstance item)
        {
            string value = item != null ? item.GetState(CONDITION_STATE) : null;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float condition) && float.IsFinite(condition) ? Mathf.Clamp01(condition) : 1f;
        }

        public static bool TryGetDamageType(EquipmentStat stat, out AnomalyDamageType type)
        {
            type = stat switch
            {
                EquipmentStat.Burn => AnomalyDamageType.Burn,
                EquipmentStat.Shock => AnomalyDamageType.Shock,
                EquipmentStat.ChemicalBurn => AnomalyDamageType.ChemicalBurn,
                EquipmentStat.Radiation => AnomalyDamageType.Radiation,
                EquipmentStat.Telepatic => AnomalyDamageType.Telepatic,
                EquipmentStat.Strike => AnomalyDamageType.Strike,
                EquipmentStat.Wound => AnomalyDamageType.Wound,
                EquipmentStat.Explosion => AnomalyDamageType.Explosion,
                _ => AnomalyDamageType.None
            };
            return type != AnomalyDamageType.None;
        }

        public static float GetItemProtection(ItemInstance item, AnomalyDamageType type, AnomalyStrength strength, AnomalyBalanceConfig config, bool fullCondition = false, float? conditionOverride = null)
        {
            EquipmentStatsComponent stats = item != null ? item.Definition.GetComponent<EquipmentStatsComponent>() : null;
            return stats != null ? AnomalyProtectionMath.Effective(stats.GetAnomalyProtection(type, strength), fullCondition ? 1f : conditionOverride ?? GetCondition(item), config) : 0f;
        }

        public static AnomalyProtectionValues GetItemValues(ItemInstance item, AnomalyDamageType type, AnomalyBalanceConfig config, bool fullCondition = false)
        {
            return new AnomalyProtectionValues(GetItemProtection(item, type, AnomalyStrength.Weak, config, fullCondition) * 100f, GetItemProtection(item, type, AnomalyStrength.Average, config, fullCondition) * 100f, GetItemProtection(item, type, AnomalyStrength.Strong, config, fullCondition) * 100f);
        }

        public static float GetCombinedProtection(InventoryContainer equipment, AnomalyDamageType type, AnomalyStrength strength, AnomalyBalanceConfig config, ItemInstance replacement = null, ItemInstance removed = null, bool fullCondition = false, ItemInstance repaired = null, float repairedCondition = 1f)
        {
            ItemInstance outfit = replacement != null && replacement.Definition.IsCategory(OUTFIT_CATEGORY) ? replacement : null;
            if (outfit == null && equipment != null)
            {
                for (int i = 0; i < equipment.Items.Count; i++)
                {
                    ItemInstance item = equipment.Items[i];
                    if (item != removed && item.Definition.IsCategory(OUTFIT_CATEGORY))
                    {
                        outfit = item;
                        break;
                    }
                }
            }

            EquipmentStatsComponent outfitStats = outfit != null ? outfit.Definition.GetComponent<EquipmentStatsComponent>() : null;
            bool helmetAvailable = outfitStats == null || outfitStats.HelmetAvailable;
            float combined = 0f;
            if (equipment != null)
            {
                for (int i = 0; i < equipment.Items.Count; i++)
                {
                    ItemInstance item = equipment.Items[i];
                    if (item == removed || replacement != null && item.Definition.IsCategory(replacement.Definition.Category))
                    {
                        continue;
                    }
                    if (IsApplicable(item, type, helmetAvailable, config))
                    {
                        combined = AnomalyProtectionMath.Combine(combined, GetItemProtection(item, type, strength, config, fullCondition, item == repaired ? repairedCondition : (float?)null));
                    }
                }
            }
            if (IsApplicable(replacement, type, helmetAvailable, config))
            {
                combined = AnomalyProtectionMath.Combine(combined, GetItemProtection(replacement, type, strength, config, fullCondition, replacement == repaired ? repairedCondition : (float?)null));
            }
            return combined;
        }

        public static AnomalyProtectionValues GetCombinedValues(InventoryContainer equipment, AnomalyDamageType type, AnomalyBalanceConfig config, ItemInstance replacement = null, ItemInstance removed = null, bool fullCondition = false, ItemInstance repaired = null, float repairedCondition = 1f)
        {
            return new AnomalyProtectionValues(GetCombinedProtection(equipment, type, AnomalyStrength.Weak, config, replacement, removed, fullCondition, repaired, repairedCondition) * 100f, GetCombinedProtection(equipment, type, AnomalyStrength.Average, config, replacement, removed, fullCondition, repaired, repairedCondition) * 100f, GetCombinedProtection(equipment, type, AnomalyStrength.Strong, config, replacement, removed, fullCondition, repaired, repairedCondition) * 100f);
        }

        public static bool IsApplicable(ItemInstance item, AnomalyDamageType type, bool helmetAvailable, AnomalyBalanceConfig config)
        {
            return item != null && type != AnomalyDamageType.None && (item.Definition.IsCategory(OUTFIT_CATEGORY) || helmetAvailable && item.Definition.IsCategory(HELMET_CATEGORY) && (config != null ? config.HelmetApplies(type) : AnomalyBalanceConfig.DefaultHelmetApplies(type)));
        }
    }
}
