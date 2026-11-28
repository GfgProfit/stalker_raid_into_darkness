using System.Globalization;
using RaidIntoDarkness.Inventory;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Inventory
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
            return stats != null ? GetProtection(stats, fullCondition ? 1f : conditionOverride ?? GetCondition(item), type, strength, config) : 0f;
        }

        public static AnomalyProtectionValues GetItemValues(ItemInstance item, AnomalyDamageType type, AnomalyBalanceConfig config, bool fullCondition = false)
        {
            EquipmentStatsComponent stats = item != null ? item.Definition.GetComponent<EquipmentStatsComponent>() : null;
            return GetDefinitionValues(stats, fullCondition ? 1f : GetCondition(item), type, config);
        }

        internal static AnomalyProtectionValues GetDefinitionValues(EquipmentStatsComponent stats, float condition, AnomalyDamageType type, AnomalyBalanceConfig config)
        {
            return new AnomalyProtectionValues(GetProtection(stats, condition, type, AnomalyStrength.Weak, config) * 100f, GetProtection(stats, condition, type, AnomalyStrength.Average, config) * 100f, GetProtection(stats, condition, type, AnomalyStrength.Strong, config) * 100f);
        }

        private static float GetProtection(EquipmentStatsComponent stats, float condition, AnomalyDamageType type, AnomalyStrength strength, AnomalyBalanceConfig config)
        {
            return stats != null ? AnomalyProtectionMath.Effective(stats.GetAnomalyProtection(type, strength), condition, config) : 0f;
        }

        public static float GetCombinedProtection(InventoryContainer equipment, AnomalyDamageType type, AnomalyStrength strength, AnomalyBalanceConfig config, ItemInstance replacement = null, ItemInstance removed = null, bool fullCondition = false, ItemInstance repaired = null, float repairedCondition = 1f)
        {
            return Combine(equipment, type, strength, config, replacement?.Definition, ReplacementCondition(replacement, repaired, repairedCondition), removed, fullCondition, repaired, repairedCondition);
        }

        private static float Combine(InventoryContainer equipment, AnomalyDamageType type, AnomalyStrength strength, AnomalyBalanceConfig config, ItemDefinition replacement, float replacementCondition, ItemInstance removed, bool fullCondition, ItemInstance repaired, float repairedCondition)
        {
            ItemDefinition outfit = replacement != null && replacement.IsCategory(OUTFIT_CATEGORY) ? replacement : null;
            if (outfit == null && equipment != null)
            {
                for (int i = 0; i < equipment.Items.Count; i++)
                {
                    ItemInstance item = equipment.Items[i];
                    if (item != removed && item.Definition.IsCategory(OUTFIT_CATEGORY))
                    {
                        outfit = item.Definition;
                        break;
                    }
                }
            }

            EquipmentStatsComponent outfitStats = outfit != null ? outfit.GetComponent<EquipmentStatsComponent>() : null;
            bool helmetAvailable = outfitStats == null || outfitStats.HelmetAvailable;
            float combined = 0f;
            if (equipment != null)
            {
                for (int i = 0; i < equipment.Items.Count; i++)
                {
                    ItemInstance item = equipment.Items[i];
                    if (item == removed || replacement != null && item.Definition.IsCategory(replacement.Category))
                    {
                        continue;
                    }
                    if (IsApplicable(item, type, helmetAvailable, config))
                    {
                        combined = AnomalyProtectionMath.Combine(combined, GetItemProtection(item, type, strength, config, fullCondition, item == repaired ? repairedCondition : (float?)null));
                    }
                }
            }
            if (IsDefinitionApplicable(replacement, type, helmetAvailable, config))
            {
                float protection = GetProtection(replacement.GetComponent<EquipmentStatsComponent>(), fullCondition ? 1f : replacementCondition, type, strength, config);
                combined = AnomalyProtectionMath.Combine(combined, protection);
            }
            return combined;
        }

        public static AnomalyProtectionValues GetCombinedValues(InventoryContainer equipment, AnomalyDamageType type, AnomalyBalanceConfig config, ItemInstance replacement = null, ItemInstance removed = null, bool fullCondition = false, ItemInstance repaired = null, float repairedCondition = 1f)
        {
            return CombineValues(equipment, type, config, replacement?.Definition, ReplacementCondition(replacement, repaired, repairedCondition), removed, fullCondition, repaired, repairedCondition);
        }

        internal static AnomalyProtectionValues GetPreviewValues(InventoryContainer equipment, AnomalyDamageType type, AnomalyBalanceConfig config, ItemDefinition definition, float condition, bool fullCondition)
        {
            return CombineValues(equipment, type, config, definition, condition, null, fullCondition, null, 1f);
        }

        private static AnomalyProtectionValues CombineValues(InventoryContainer equipment, AnomalyDamageType type, AnomalyBalanceConfig config, ItemDefinition replacement, float replacementCondition, ItemInstance removed, bool fullCondition, ItemInstance repaired, float repairedCondition)
        {
            return new AnomalyProtectionValues(Combine(equipment, type, AnomalyStrength.Weak, config, replacement, replacementCondition, removed, fullCondition, repaired, repairedCondition) * 100f, Combine(equipment, type, AnomalyStrength.Average, config, replacement, replacementCondition, removed, fullCondition, repaired, repairedCondition) * 100f, Combine(equipment, type, AnomalyStrength.Strong, config, replacement, replacementCondition, removed, fullCondition, repaired, repairedCondition) * 100f);
        }

        private static float ReplacementCondition(ItemInstance replacement, ItemInstance repaired, float repairedCondition)
        {
            return replacement == null ? 1f : replacement == repaired ? repairedCondition : GetCondition(replacement);
        }

        public static bool IsApplicable(ItemInstance item, AnomalyDamageType type, bool helmetAvailable, AnomalyBalanceConfig config)
        {
            return IsDefinitionApplicable(item?.Definition, type, helmetAvailable, config);
        }

        private static bool IsDefinitionApplicable(ItemDefinition definition, AnomalyDamageType type, bool helmetAvailable, AnomalyBalanceConfig config)
        {
            return definition != null && type != AnomalyDamageType.None && (definition.IsCategory(OUTFIT_CATEGORY) || helmetAvailable && definition.IsCategory(HELMET_CATEGORY) && (config != null ? config.HelmetApplies(type) : AnomalyBalanceConfig.DefaultHelmetApplies(type)));
        }
    }
}
