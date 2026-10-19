using Inventory;
using Inventory.UI;
using UnityEngine;

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

        private const float ELEMENTAL_SCALE = 0.1f;

        private static readonly Color POSITIVE_COLOR = new(0f, 1f, 0f, 1f);
        private static readonly Color NEGATIVE_COLOR = new(1f, 0f, 0f, 1f);
        private static readonly Color NEUTRAL_COLOR = new(100f / 255f, 100f / 255f, 100f / 255f, 1f);

        private static readonly StatusBar[] BARS =
        {
            new(STATUS_RADIATION, EquipmentStat.Radiation, EquipmentStatsFormatter.MAX_RADIATION_DAMAGE, true),
            new(STATUS_CHEMICAL, EquipmentStat.ChemicalBurn, EquipmentStatsFormatter.MAX_CHEMICAL_DAMAGE, true),
            new(STATUS_SHOCK, EquipmentStat.Shock, EquipmentStatsFormatter.MAX_SHOCK_DAMAGE, true),
            new(STATUS_FIRE, EquipmentStat.Burn, EquipmentStatsFormatter.MAX_BURN_DAMAGE, true),
            new(STATUS_PSI, EquipmentStat.Telepatic, EquipmentStatsFormatter.MAX_TELEPATIC_DAMAGE, false),
            new(STATUS_WOUND, EquipmentStat.Wound, EquipmentStatsFormatter.MAX_WOUND_DAMAGE, false),
            new(STATUS_FIRE_WOUND, EquipmentStat.FireWound, EquipmentStatsFormatter.MAX_FIRE_WOUND_DAMAGE, false)
        };

        private readonly InventoryWindow _window;
        private readonly InventoryStatusPanel _status;

        public EquipmentStatusPresenter(InventoryWindow window, InventoryStatusPanel status)
        {
            _window = window;
            _status = status;
        }

        public void Refresh(ItemInstance hovered, bool fullCondition)
        {
            ItemInstance outfit = EquipmentStatsFormatter.FindEquipped(_window, EquipmentStatsFormatter.OUTFIT_CATEGORY);
            ItemInstance helmet = EquipmentStatsFormatter.FindEquipped(_window, EquipmentStatsFormatter.HELMET_CATEGORY);
            EquipmentStatsComponent outfitStats = GetStats(outfit);
            EquipmentStatsComponent helmetStats = GetStats(helmet);
            float outfitCondition = fullCondition ? 1f : EquipmentStatsFormatter.GetCondition(outfit);
            float helmetCondition = fullCondition ? 1f : EquipmentStatsFormatter.GetCondition(helmet);
            EquipmentStatsComponent hoveredStats = IsBonusCandidate(hovered) ? GetStats(hovered) : null;
            bool hoveredOutfit = hoveredStats != null && hovered.Definition.IsCategory(EquipmentStatsFormatter.OUTFIT_CATEGORY);
            bool hoveredHelmet = hoveredStats != null && hovered.Definition.IsCategory(EquipmentStatsFormatter.HELMET_CATEGORY);
            float hoveredCondition = fullCondition ? 1f : EquipmentStatsFormatter.GetCondition(hovered);

            if (hoveredHelmet && outfitStats != null && outfitStats.HelmetAvailable == false)
            {
                hoveredHelmet = false;
            }

            _status.ClearBonus();

            for (int i = 0; i < BARS.Length; i++)
            {
                StatusBar bar = BARS[i];
                float outfitValue = GetProtection(outfitStats, bar, outfitCondition);
                float helmetValue = GetProtection(helmetStats, bar, helmetCondition);
                float current = Normalize(outfitValue + helmetValue, bar);
                _status.SetValue(bar.Id, current);

                if (hoveredOutfit == false && hoveredHelmet == false)
                {
                    continue;
                }

                float bonus;

                if (hoveredOutfit)
                {
                    bonus = Normalize(GetProtection(hoveredStats, bar, hoveredCondition) + (hoveredStats.HelmetAvailable ? helmetValue : 0f), bar);
                }
                else
                {
                    bonus = Normalize(outfitValue + GetProtection(hoveredStats, bar, hoveredCondition), bar);
                }

                _status.SetBonus(bar.Id, bonus, bonus > current ? POSITIVE_COLOR : bonus < current ? NEGATIVE_COLOR : NEUTRAL_COLOR);
            }
        }

        private bool IsBonusCandidate(ItemInstance item) => item != null && item.ContainerId == _window.PrimaryContainerId;

        private static EquipmentStatsComponent GetStats(ItemInstance item) => item != null ? item.Definition.GetComponent<EquipmentStatsComponent>() : null;

        private static float Normalize(float value, StatusBar bar) => Mathf.Clamp01((float)(value / bar.Max));

        private static float GetProtection(EquipmentStatsComponent stats, StatusBar bar, float condition)
        {
            if (stats == null)
            {
                return 0f;
            }

            float value = stats.GetValue(bar.Stat);

            if (bar.Stat == EquipmentStat.FireWound)
            {
                value += stats.GetValue(EquipmentStat.HelmetFireWound);
            }

            return value * condition * (bar.Elemental ? ELEMENTAL_SCALE : 1f);
        }

        private readonly struct StatusBar
        {
            public StatusBar(string id, EquipmentStat stat, double max, bool elemental)
            {
                Id = id;
                Stat = stat;
                Max = max;
                Elemental = elemental;
            }

            public string Id { get; }
            public EquipmentStat Stat { get; }
            public double Max { get; }
            public bool Elemental { get; }
        }
    }
}
