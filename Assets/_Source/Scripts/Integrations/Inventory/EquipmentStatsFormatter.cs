using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Inventory;
using Inventory.UI;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class EquipmentStatsFormatter
    {
        public const string OUTFIT_CATEGORY = "Armor";
        public const string HELMET_CATEGORY = "Head";
        public const string BACKPACK_CATEGORY = "Backpack";
        public const string CONDITION_STATE = "condition";
        public const char NUMBER_SEPARATOR = '.';

        private const double PERCENT = 100.0;
        private const string PERCENT_UNIT = "%";
        private const string KILOGRAM_UNIT = "stat_unit_kg";
        private const string ICON_BALLISTIC = "ui_am_propery_01";
        private const string ICON_WOUND = "ui_am_prop_wound";
        private const string ICON_STRIKE = "ui_am_prop_strike";
        private const string ICON_EXPLOSION = "ui_am_prop_explo";
        private const string ICON_RADIATION = "ui_am_propery_09";
        private const string ICON_TELEPATIC = "ui_am_propery_11";
        private const string ICON_BURN = "ui_am_prop_thermo";
        private const string ICON_SHOCK = "ui_am_prop_electro";
        private const string ICON_CHEMICAL = "ui_am_prop_chem";
        private const string ICON_ARTEFACT = "ui_am_prop_artefact";
        private const string ICON_WEIGHT = "ui_am_propery_08";
        private const string ICON_WEIGHT_NEGATIVE = "ui_am_prop_carry_weight_negative";
        private const string ICON_SPEED = "ui_am_prop_sprint";
        private const string ICON_PHYSICAL = "ui_am_prop_damage";
        private const string ICON_ANOMALY = "ui_am_prop_Vibros";
        private const string BULLET = " • ";
        private const string FEATURE_BULLET = "  • ";
        private const int CONDITION_RED_THRESHOLD = 60;
        private const int CONDITION_YELLOW_THRESHOLD = 85;

        private static readonly Color GRAY_1_COLOR = new(170f / 255f, 170f / 255f, 170f / 255f, 1f);
        private static readonly Color GRAY_2_COLOR = new(140f / 255f, 140f / 255f, 140f / 255f, 1f);
        private static readonly Color ORANGE_COLOR = new(238f / 255f, 153f / 255f, 26f / 255f, 1f);
        private static readonly Color RED_COLOR = new(204f / 255f, 0f, 51f / 255f, 1f);
        private static readonly Color YELLOW_COLOR = new(1f, 1f, 0f, 1f);
        private static readonly Color GREEN_COLOR = new(51f / 255f, 1f, 102f / 255f, 1f);
        private static readonly Color PURPLE_COLOR = new(93f / 255f, 0f, 116f / 255f, 1f);

        private static readonly StatRow[] OUTFIT_ROWS =
        {
            new("stat_fire_wound", "Ballistic protection", ICON_BALLISTIC, null, EquipmentStat.FireWound, 1.0, PERCENT_UNIT, true, true),
            new("stat_fire_wound_helmet", "Helmet ballistic protection", ICON_BALLISTIC, null, EquipmentStat.HelmetFireWound, 1.0, PERCENT_UNIT, true, true),
            new("stat_wound", "Rupture protection", ICON_WOUND, null, EquipmentStat.Wound, 1.0, PERCENT_UNIT, true, true),
            new("stat_strike", "Impact protection", ICON_STRIKE, null, EquipmentStat.Strike, 1.0, PERCENT_UNIT, true, true),
            new("stat_explosion", "Explosion protection", ICON_EXPLOSION, null, EquipmentStat.Explosion, 1.0, PERCENT_UNIT, true, true),
            new("stat_radiation", "Radiation protection", ICON_RADIATION, null, EquipmentStat.Radiation, 1.0, PERCENT_UNIT, true, true),
            new("stat_telepatic", "Psy protection", ICON_TELEPATIC, null, EquipmentStat.Telepatic, 1.0, PERCENT_UNIT, true, true),
            new("stat_burn", "Burn protection", ICON_BURN, null, EquipmentStat.Burn, 1.0, PERCENT_UNIT, true, true),
            new("stat_shock", "Shock protection", ICON_SHOCK, null, EquipmentStat.Shock, 1.0, PERCENT_UNIT, true, true),
            new("stat_chemical_burn", "Chemical protection", ICON_CHEMICAL, null, EquipmentStat.ChemicalBurn, 1.0, PERCENT_UNIT, true, true),
            new("stat_artefact_count", "Artefact slots", ICON_ARTEFACT, null, EquipmentStat.ArtefactCount, 1.0, string.Empty, false, false),
            new("stat_additional_weight", "Carry weight", ICON_WEIGHT, ICON_WEIGHT_NEGATIVE, EquipmentStat.AdditionalWeight, 1.0, KILOGRAM_UNIT, true, false),
            new("stat_speed", "Mobility multiplier", ICON_SPEED, null, EquipmentStat.Speed, PERCENT, PERCENT_UNIT, false, false),
            new("stat_durability_physical", "Physical durability", ICON_PHYSICAL, null, EquipmentStat.PhysicalDurability, 1.0, PERCENT_UNIT, false, false),
            new("stat_durability_anomaly", "Anomaly durability", ICON_ANOMALY, null, EquipmentStat.AnomalyDurability, 1.0, PERCENT_UNIT, false, false)
        };

        private static readonly StatRow[] BACKPACK_ROWS =
        {
            new("stat_additional_weight", "Carry weight", ICON_WEIGHT, ICON_WEIGHT_NEGATIVE, EquipmentStat.AdditionalWeight, 1.0, KILOGRAM_UNIT, true, false)
        };

        private readonly InventoryWindow _window;
        private readonly StringBuilder _builder = new(1024);

        public EquipmentStatsFormatter(InventoryWindow window)
        {
            _window = window;
        }

        public bool FullCondition { get; set; }

        public static float GetCondition(ItemInstance item)
        {
            string value = item != null ? item.GetState(CONDITION_STATE) : null;

            if (string.IsNullOrEmpty(value) == false && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float condition))
            {
                return Mathf.Clamp01(condition);
            }

            return 1f;
        }

        public static string GetEquipmentCategory(ItemDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            if (definition.IsCategory(OUTFIT_CATEGORY))
            {
                return OUTFIT_CATEGORY;
            }

            if (definition.IsCategory(HELMET_CATEGORY))
            {
                return HELMET_CATEGORY;
            }

            return definition.IsCategory(BACKPACK_CATEGORY) ? BACKPACK_CATEGORY : null;
        }

        public static ItemInstance FindEquipped(InventoryWindow window, string category)
        {
            InventoryContainer container = window != null && window.Model != null ? window.Model.GetContainer(window.EquipmentContainerId) : null;

            if (container == null || category == null)
            {
                return null;
            }

            for (int i = 0; i < container.Items.Count; i++)
            {
                if (container.Items[i].Definition.IsCategory(category))
                {
                    return container.Items[i];
                }
            }

            return null;
        }

        public static string FormatNumber(double value, bool sign)
        {
            long rounded = (long)Math.Round(value);
            string prefix = rounded < 0 ? "-" : sign && rounded > 0 ? "+" : string.Empty;
            return prefix + Utils.FormatNumber(Math.Abs(rounded), NUMBER_SEPARATOR);
        }

        public void Collect(ItemInstance item, List<InventoryItemStat> stats)
        {
            ItemDefinition definition = item.Definition;
            EquipmentStatsComponent component = definition.GetComponent<EquipmentStatsComponent>();
            string category = GetEquipmentCategory(definition);

            if (component == null || category == null)
            {
                return;
            }

            ItemInstance equipped = FindEquipped(_window, category);
            EquipmentStatsComponent other = equipped != null ? equipped.Definition.GetComponent<EquipmentStatsComponent>() : null;
            float condition = GetCondition(item);
            float otherCondition = FullCondition ? 1f : GetCondition(equipped);
            string kilograms = _window.Label(KILOGRAM_UNIT, "kg");
            StatRow[] rows = category == BACKPACK_CATEGORY ? BACKPACK_ROWS : OUTFIT_ROWS;

            for (int i = 0; i < rows.Length; i++)
            {
                StatRow row = rows[i];
                float value = component.GetValue(row.Stat);
                float otherValue = other != null ? other.GetValue(row.Stat) : 0f;

                if (row.Condition)
                {
                    value *= FullCondition && row.LiteralPercent ? 1f : condition;
                    otherValue *= otherCondition;
                }
                Add(stats, row, value, otherValue, row.Unit == KILOGRAM_UNIT ? kilograms : row.Unit);
            }
        }

        public string Describe(ItemInstance item)
        {
            ItemDefinition definition = item.Definition;
            EquipmentStatsComponent component = definition.GetComponent<EquipmentStatsComponent>();

            if (component == null || GetEquipmentCategory(definition) == null)
            {
                return null;
            }

            float condition = GetCondition(item);
            long cost = (long)Math.Ceiling(component.Cost * (double)(decimal)condition);
            int percent = (int)Math.Ceiling((double)(decimal)condition * PERCENT);
            Color conditionColor = percent < CONDITION_RED_THRESHOLD ? RED_COLOR : percent < CONDITION_YELLOW_THRESHOLD ? YELLOW_COLOR : GREEN_COLOR;
            _builder.Clear();
            AppendColor(GRAY_1_COLOR).Append(_window.Label("tooltip_cost", "Cost:")).Append(' ');
            AppendColor(ORANGE_COLOR).Append(Utils.FormatNumber(cost, NUMBER_SEPARATOR)).Append(' ');
            AppendColor(GRAY_1_COLOR).Append(_window.Label("unit_currency", "RU")).Append("\n\n");
            AppendColor(conditionColor).Append(BULLET);
            AppendColor(GRAY_1_COLOR).Append(_window.Label("tooltip_condition", "Condition")).Append(": ");
            AppendColor(conditionColor).Append(Utils.FormatNumber(percent, NUMBER_SEPARATOR)).Append('%');

            if (definition.Description.Length > 0)
            {
                _builder.Append("\n\n");
                AppendColor(GRAY_1_COLOR).Append(definition.Description);
            }

            if (definition.IsCategory(OUTFIT_CATEGORY))
            {
                AppendFeatures(component);
            }

            return _builder.ToString();
        }

        private void AppendFeatures(EquipmentStatsComponent component)
        {
            string community = component.Community.Length > 0 ? _window.Label("community_" + component.Community, component.Community) : _window.Label("community_unknown", "Unknown");
            _builder.Append("\n\n");
            AppendColor(GRAY_1_COLOR).Append(' ').Append(_window.Label("tooltip_features", "FEATURES")).Append(":\n");
            AppendColor(YELLOW_COLOR).Append(FEATURE_BULLET);
            AppendColor(GRAY_2_COLOR).Append(_window.Label("feature_community", "faction")).Append(": ");
            AppendColor(GRAY_1_COLOR).Append(community).Append('\n');
            AppendColor(PURPLE_COLOR).Append(FEATURE_BULLET);
            AppendColor(GRAY_2_COLOR).Append(_window.Label("feature_helmet", "headgear")).Append(": ");
            AppendColor(GRAY_1_COLOR).Append(component.HelmetAvailable ? _window.Label("feature_helmet_none", "none") : _window.Label("feature_helmet_builtin", "built-in")).Append('\n');
            AppendColor(PURPLE_COLOR).Append(FEATURE_BULLET);
            AppendColor(GRAY_2_COLOR).Append(_window.Label("feature_sprint", "sprinting:")).Append(' ').Append(component.SprintAllowed ? _window.Label("feature_possible", "possible") : _window.Label("feature_not_possible", "impossible"));
        }

        private StringBuilder AppendColor(Color color) => _builder.Append("<color=").Append(Utils.ToHexRGB(color)).Append('>');

        private void Add(List<InventoryItemStat> stats, StatRow row, float value, float otherValue, string unit)
        {
            if (value == 0f)
            {
                return;
            }

            if (row.LiteralPercent)
            {
                AddPercentage(stats, row, value, otherValue);
                return;
            }

            double raw = (double)(decimal)value * row.Magnitude;
            double scaled = Math.Ceiling(raw);
            InventoryStatTone tone = scaled >= 0 ? InventoryStatTone.Normal : InventoryStatTone.Negative;
            string comparison = null;

            if (otherValue != 0f)
            {
                double otherRaw = (double)(decimal)otherValue * row.Magnitude;

                if (scaled != Math.Ceiling(otherRaw))
                {
                    comparison = "(" + FormatNumber(Math.Ceiling(scaled - otherRaw), true) + ")";
                    tone = scaled > otherRaw ? InventoryStatTone.Better : InventoryStatTone.Worse;
                }
            }

            string number = FormatNumber(scaled, row.Sign);
            string text = unit.Length > 0 ? number + " " + unit : number;
            string iconId = value < 0f && row.NegativeIcon != null ? row.NegativeIcon : row.Icon;
            stats.Add(new InventoryItemStat(_window.Label(row.Key, row.Fallback), text, _window.Theme != null ? _window.Theme.Icon(iconId) : null, tone, comparison));
        }

        private void AddPercentage(List<InventoryItemStat> stats, StatRow row, float value, float otherValue)
        {
            InventoryStatTone tone = value >= 0f ? InventoryStatTone.Normal : InventoryStatTone.Negative;
            string comparison = null;

            if (otherValue != 0f && value != otherValue)
            {
                float difference = value - otherValue;
                comparison = "(" + (difference > 0f ? "+" : string.Empty) + difference.ToString("0.##", CultureInfo.InvariantCulture) + ")";
                tone = value > otherValue ? InventoryStatTone.Better : InventoryStatTone.Worse;
            }

            string text = value.ToString("0.##", CultureInfo.InvariantCulture) + PERCENT_UNIT;
            stats.Add(new InventoryItemStat(_window.Label(row.Key, row.Fallback), text, _window.Theme != null ? _window.Theme.Icon(row.Icon) : null, tone, comparison));
        }

        private readonly struct StatRow
        {
            public StatRow(string key, string fallback, string icon, string negativeIcon, EquipmentStat stat, double magnitude, string unit, bool sign, bool condition)
            {
                Key = key;
                Fallback = fallback;
                Icon = icon;
                NegativeIcon = negativeIcon;
                Stat = stat;
                Magnitude = magnitude;
                Unit = unit;
                Sign = sign;
                Condition = condition;
            }

            public string Key { get; }
            public string Fallback { get; }
            public string Icon { get; }
            public string NegativeIcon { get; }
            public EquipmentStat Stat { get; }
            public double Magnitude { get; }
            public string Unit { get; }
            public bool Sign { get; }
            public bool Condition { get; }
            public bool LiteralPercent => Unit == PERCENT_UNIT && Stat != EquipmentStat.Speed;
        }
    }
}
