using XRay.Importer.Levels;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal static class EquipmentItemStats
    {
        public static bool SupportsCondition(ItemDefinition definition)
        {
            return definition != null && !definition.IsCategory("Grenade") && (definition.GetComponent<EquipmentStatsComponent>() != null || definition.GetComponent<WeaponStatsComponent>() != null);
        }

        public static string GetCategory(ItemDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            if (definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY))
            {
                return AnomalyEquipmentProtection.OUTFIT_CATEGORY;
            }

            if (definition.IsCategory(AnomalyEquipmentProtection.HELMET_CATEGORY))
            {
                return AnomalyEquipmentProtection.HELMET_CATEGORY;
            }

            return definition.IsCategory(AnomalyEquipmentProtection.BACKPACK_CATEGORY) ? AnomalyEquipmentProtection.BACKPACK_CATEGORY : null;
        }

        public static ItemInstance FindEquipped(InventoryContainer equipment, string category)
        {
            if (equipment != null && category != null)
            {
                foreach (ItemInstance item in equipment.Items)
                {
                    if (item.Definition.IsCategory(category))
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        public static (float Value, float Difference) CompareAnomalyProtection(InventoryContainer equipment, AnomalyBalanceConfig config, ItemDefinition definition, EquipmentStatsComponent component, float condition, bool fullCondition, AnomalyDamageType type)
        {
            float value = GetMeanProtection(AnomalyEquipmentProtection.GetDefinitionValues(component, fullCondition ? 1f : condition, type, config));
            if (value == 0f)
            {
                return default;
            }

            AnomalyProtectionValues current = AnomalyEquipmentProtection.GetCombinedValues(equipment, type, config, fullCondition: fullCondition);
            AnomalyProtectionValues preview = AnomalyEquipmentProtection.GetPreviewValues(equipment, type, config, definition, condition, fullCondition);
            return (value, GetMeanProtection(preview) - GetMeanProtection(current));
        }

        private static float GetMeanProtection(AnomalyProtectionValues values) => (values.Get(AnomalyStrength.Weak) + values.Get(AnomalyStrength.Average) + values.Get(AnomalyStrength.Strong)) / 3f;
    }
}
