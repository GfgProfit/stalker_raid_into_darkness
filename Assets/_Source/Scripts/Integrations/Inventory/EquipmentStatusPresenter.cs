using Inventory;
using Inventory.UI;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class EquipmentStatusPresenter
    {
        public const string STATUS_RADIATION = "radiation";
        public const string STATUS_CHEMICAL = "chemical";
        public const string STATUS_SHOCK = "shock";
        public const string STATUS_FIRE = "fire";
        public const string STATUS_PSI = "psi";
        public const string STATUS_WOUND = "wound";
        public const string STATUS_FIRE_WOUND = "fire_wound";

        private static readonly Color POSITIVE_COLOR = new(0f, 1f, 0f, 1f);
        private static readonly Color NEGATIVE_COLOR = new(1f, 0f, 0f, 1f);
        private static readonly Color NEUTRAL_COLOR = new(100f / 255f, 100f / 255f, 100f / 255f, 1f);

        private static readonly StatusBar[] BARS =
        {
            new(STATUS_RADIATION, EquipmentStat.Radiation),
            new(STATUS_CHEMICAL, EquipmentStat.ChemicalBurn),
            new(STATUS_SHOCK, EquipmentStat.Shock),
            new(STATUS_FIRE, EquipmentStat.Burn),
            new(STATUS_PSI, EquipmentStat.Telepatic),
            new(STATUS_WOUND, EquipmentStat.Wound),
            new(STATUS_FIRE_WOUND, EquipmentStat.FireWound)
        };

        private readonly InventoryWindow _window;
        private readonly InventoryStatusPanel _status;
        private readonly AnomalyBalanceConfig _anomalyConfig;

        public EquipmentStatusPresenter(InventoryWindow window, InventoryStatusPanel status, AnomalyBalanceConfig anomalyConfig = null)
        {
            _window = window;
            _status = status;
            _anomalyConfig = anomalyConfig != null ? anomalyConfig : GameConfig.Load()?.AnomalyBalance;
        }

        public AnomalyProtectionValues GetAnomalyProtectionValues(AnomalyDamageType type, bool fullCondition = false, ItemInstance replacement = null)
        {
            InventoryContainer equipment = _window != null && _window.Model != null ? _window.Model.GetContainer(_window.EquipmentContainerId) : null;
            return AnomalyEquipmentProtection.GetCombinedValues(equipment, type, _anomalyConfig, replacement: replacement, fullCondition: fullCondition);
        }

        public void Refresh(ItemInstance hovered, bool fullCondition)
        {
            ItemInstance outfit = EquipmentStatsFormatter.FindEquipped(_window, EquipmentStatsFormatter.OUTFIT_CATEGORY);
            ItemInstance helmet = EquipmentStatsFormatter.FindEquipped(_window, EquipmentStatsFormatter.HELMET_CATEGORY);
            EquipmentStatsComponent outfitStats = GetStats(outfit);
            EquipmentStatsComponent hoveredStats = IsBonusCandidate(hovered) ? GetStats(hovered) : null;
            bool hoveredOutfit = hoveredStats != null && hovered.Definition.IsCategory(EquipmentStatsFormatter.OUTFIT_CATEGORY);
            bool hoveredHelmet = hoveredStats != null && hovered.Definition.IsCategory(EquipmentStatsFormatter.HELMET_CATEGORY);

            if (hoveredHelmet && outfitStats != null && outfitStats.HelmetAvailable == false)
            {
                hoveredHelmet = false;
            }

            _status.ClearBonus();

            for (int i = 0; i < BARS.Length; i++)
            {
                StatusBar bar = BARS[i];
                if (AnomalyEquipmentProtection.TryGetDamageType(bar.Stat, out AnomalyDamageType anomalyType))
                {
                    float protection = Normalize(GetMeanProtection(GetAnomalyProtectionValues(anomalyType, fullCondition)));
                    _status.SetValue(bar.Id, protection);
                    if (hoveredOutfit || hoveredHelmet)
                    {
                        float preview = Normalize(GetMeanProtection(GetAnomalyProtectionValues(anomalyType, fullCondition, hovered)));
                        _status.SetBonus(bar.Id, preview, preview > protection ? POSITIVE_COLOR : preview < protection ? NEGATIVE_COLOR : NEUTRAL_COLOR);
                    }
                    continue;
                }
                float outfitValue = GetProtection(outfit, bar, fullCondition);
                float helmetValue = GetProtection(helmet, bar, fullCondition);
                float current = Normalize(outfitValue + (outfitStats == null || outfitStats.HelmetAvailable ? helmetValue : 0f));
                _status.SetValue(bar.Id, current);

                if (hoveredOutfit == false && hoveredHelmet == false)
                {
                    continue;
                }

                float bonus;

                if (hoveredOutfit)
                {
                    bonus = Normalize(GetProtection(hovered, bar, fullCondition) + (hoveredStats.HelmetAvailable ? helmetValue : 0f));
                }
                else
                {
                    bonus = Normalize(outfitValue + GetProtection(hovered, bar, fullCondition));
                }

                _status.SetBonus(bar.Id, bonus, bonus > current ? POSITIVE_COLOR : bonus < current ? NEGATIVE_COLOR : NEUTRAL_COLOR);
            }
        }

        private bool IsBonusCandidate(ItemInstance item) => item != null && item.ContainerId == _window.PrimaryContainerId;

        private static EquipmentStatsComponent GetStats(ItemInstance item) => item != null ? item.Definition.GetComponent<EquipmentStatsComponent>() : null;

        private static float Normalize(float value) => Mathf.Clamp01(value / 100f);

        private static float GetMeanProtection(AnomalyProtectionValues values) => (values.Get(AnomalyStrength.Weak) + values.Get(AnomalyStrength.Average) + values.Get(AnomalyStrength.Strong)) / 3f;

        private static float GetProtection(ItemInstance item, StatusBar bar, bool fullCondition)
        {
            EquipmentStatsComponent stats = GetStats(item);
            if (stats == null)
            {
                return 0f;
            }

            float value = stats.GetValue(bar.Stat);

            if (bar.Stat == EquipmentStat.FireWound)
            {
                value += stats.GetValue(EquipmentStat.HelmetFireWound);
            }

            return value * (fullCondition ? 1f : EquipmentStatsFormatter.GetCondition(item));
        }

        private readonly struct StatusBar
        {
            public StatusBar(string id, EquipmentStat stat)
            {
                Id = id;
                Stat = stat;
            }

            public string Id { get; }
            public EquipmentStat Stat { get; }
        }
    }
}
