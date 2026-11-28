using System.Collections.Generic;
using System.Globalization;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Player;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Inventory
{
    public static class AnomalyEquipmentWear
    {
        public static void Capture(IAnomalyEquipmentWearSource source, InventoryModel model, InventoryContainer equipment, AnomalyDamageType type, AnomalyBalanceConfig config, List<AnomalyEquipmentWearTarget> targets)
        {
            if (source == null || model == null || equipment == null || equipment.Model != model || targets == null)
            {
                return;
            }
            EquipmentStatsComponent outfitStats = null;
            for (int i = 0; i < equipment.Items.Count; i++)
            {
                ItemInstance item = equipment.Items[i];
                if (item.Definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY))
                {
                    outfitStats = item.Definition.GetComponent<EquipmentStatsComponent>();
                    break;
                }
            }
            bool helmetAvailable = outfitStats == null || outfitStats.HelmetAvailable;
            for (int i = 0; i < equipment.Items.Count; i++)
            {
                ItemInstance item = equipment.Items[i];
                EquipmentStatsComponent stats = item.Definition.GetComponent<EquipmentStatsComponent>();
                if (model.ContainsItem(item) && stats != null && AnomalyEquipmentProtection.IsApplicable(item, type, helmetAvailable, config))
                {
                    targets.Add(new AnomalyEquipmentWearTarget(source, item.InstanceId, AnomalyEquipmentProtection.GetCondition(item), stats.AnomalyDurability));
                }
            }
        }

        public static void Apply(IAnomalyEquipmentWearSource source, InventoryModel model, InventoryContainer equipment, AnomalyEquipmentWearTarget target, float remainingCondition)
        {
            if (source == null || !ReferenceEquals(target.Source, source) || model == null || equipment == null || equipment.Model != model || !float.IsFinite(remainingCondition) || remainingCondition < 0f || remainingCondition > 1f)
            {
                return;
            }
            ItemInstance item = model.GetItem(target.ItemId);
            if (item == null || !equipment.Contains(item) || item.ContainerId != equipment.Id || item.Definition.GetComponent<EquipmentStatsComponent>() == null || !(item.Definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY) || item.Definition.IsCategory(AnomalyEquipmentProtection.HELMET_CATEGORY)) || remainingCondition >= AnomalyEquipmentProtection.GetCondition(item))
            {
                return;
            }
            model.TrySetItemStateInPlace(item, AnomalyEquipmentProtection.CONDITION_STATE, remainingCondition.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
