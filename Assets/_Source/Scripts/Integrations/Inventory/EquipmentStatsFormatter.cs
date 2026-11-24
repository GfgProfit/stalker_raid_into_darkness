using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Inventory;
using Inventory.UI;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class EquipmentStatsFormatter
    {
        public const string OUTFIT_CATEGORY = AnomalyEquipmentProtection.OUTFIT_CATEGORY;
        public const string HELMET_CATEGORY = AnomalyEquipmentProtection.HELMET_CATEGORY;
        public const string BACKPACK_CATEGORY = AnomalyEquipmentProtection.BACKPACK_CATEGORY;
        public const string CONDITION_STATE = AnomalyEquipmentProtection.CONDITION_STATE;
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
        private static readonly string STAT_MARKER = $"<color={Utils.ToHexRGB(PURPLE_COLOR)}>•</color> ";

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
        private readonly AnomalyBalanceConfig _anomalyConfig;
        private readonly StringBuilder _builder = new(1024);

        public EquipmentStatsFormatter(InventoryWindow window, AnomalyBalanceConfig anomalyConfig = null)
        {
            _window = window;
            _anomalyConfig = anomalyConfig != null ? anomalyConfig : GameConfig.Load()?.AnomalyBalance;
        }

        public bool FullCondition { get; set; }

        public static float GetCondition(ItemInstance item)
        {
            return AnomalyEquipmentProtection.GetCondition(item);
        }

        public static bool SupportsCondition(ItemDefinition definition)
        {
            return definition != null && !definition.IsCategory("Grenade") && (definition.GetComponent<EquipmentStatsComponent>() != null || definition.GetComponent<WeaponStatsComponent>() != null);
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
            CollectDefinition(item.Definition, GetCondition(item), FullCondition, stats);
        }

        internal void CollectDefinition(ItemDefinition definition, float condition, bool fullCondition, List<InventoryItemStat> stats)
        {
            if (definition.HasTag("Knife") && definition.GetComponent<WeaponHudComponent>()?.FirstPersonWeapon is { IsMelee: true } knife)
            {
                AddKnifeStats(stats, knife);
                return;
            }

            if (definition.IsCategory("Grenade") && definition.GetComponent<WeaponHudComponent>()?.FirstPersonWeapon is { IsHandGrenade: true, HandGrenade: { } handGrenade })
            {
                AddGrenadeStats(stats, handGrenade, true);
                return;
            }

            if (definition.GetComponent<AmmoBallisticsComponent>()?.Grenade is { IsConfigured: true } launcherGrenade)
            {
                AddGrenadeStats(stats, launcherGrenade, false);
                return;
            }

            EquipmentStatsComponent component = definition.GetComponent<EquipmentStatsComponent>();
            string category = GetEquipmentCategory(definition);

            if (component == null || category == null)
            {
                return;
            }

            ItemInstance equipped = FindEquipped(_window, category);
            EquipmentStatsComponent other = equipped != null ? equipped.Definition.GetComponent<EquipmentStatsComponent>() : null;
            float otherCondition = fullCondition ? 1f : GetCondition(equipped);
            string kilograms = _window.Label(KILOGRAM_UNIT, "kg");
            StatRow[] rows = category == BACKPACK_CATEGORY ? BACKPACK_ROWS : OUTFIT_ROWS;

            for (int i = 0; i < rows.Length; i++)
            {
                StatRow row = rows[i];
                if (AnomalyEquipmentProtection.TryGetDamageType(row.Stat, out AnomalyDamageType anomalyType))
                {
                    AddAnomalyProtection(stats, row, definition, component, condition, fullCondition, anomalyType);
                    continue;
                }
                float value = component.GetValue(row.Stat);
                float otherValue = other != null ? other.GetValue(row.Stat) : 0f;

                if (row.Condition)
                {
                    value *= fullCondition && row.LiteralPercent ? 1f : condition;
                    otherValue *= otherCondition;
                }
                Add(stats, row, value, otherValue, row.Unit == KILOGRAM_UNIT ? kilograms : row.Unit);
            }
        }

        public string Describe(ItemInstance item)
        {
            return DescribeDefinition(item.Definition, GetCondition(item), EquipmentAnimationQueue.IsPatchRemoved(item));
        }

        internal string DescribeDefinition(ItemDefinition definition, float condition, bool patchRemoved = false)
        {
            EquipmentStatsComponent component = definition.GetComponent<EquipmentStatsComponent>();

            if (component == null || GetEquipmentCategory(definition) == null)
            {
                return null;
            }

            long cost = (long)Math.Ceiling(component.Cost * (double)(decimal)condition);
            double percent = (double)(decimal)condition * PERCENT;
            Color conditionColor = percent < CONDITION_RED_THRESHOLD ? RED_COLOR : percent < CONDITION_YELLOW_THRESHOLD ? YELLOW_COLOR : GREEN_COLOR;
            _builder.Clear();
            AppendColor(GRAY_1_COLOR).Append(_window.Label("tooltip_cost", "Cost:")).Append(' ');
            AppendColor(ORANGE_COLOR).Append(Utils.FormatNumber(cost, NUMBER_SEPARATOR)).Append(' ');
            AppendColor(GRAY_1_COLOR).Append(_window.Label("unit_currency", "RU")).Append("\n\n");
            AppendColor(conditionColor).Append(BULLET);
            AppendColor(GRAY_1_COLOR).Append(_window.Label("tooltip_condition", "Condition")).Append(": ");
            AppendColor(conditionColor).Append(percent.ToString("0.#", CultureInfo.InvariantCulture)).Append('%');

            if (definition.Description.Length > 0)
            {
                _builder.Append("\n\n");
                AppendColor(GRAY_1_COLOR).Append(definition.Description);
            }

            if (definition.IsCategory(OUTFIT_CATEGORY))
            {
                AppendFeatures(component, patchRemoved);
            }

            return _builder.ToString();
        }

        private void AddKnifeStats(List<InventoryItemStat> stats, FirstPersonWeaponAsset weapon)
        {
            FirstPersonWeaponMotion weak = weapon.Find(FirstPersonWeaponMotionKind.MeleePrimary, 0);
            FirstPersonWeaponMotion strong = weapon.Find(FirstPersonWeaponMotionKind.MeleeSecondary, 0);
            if (weak == null || strong == null)
            {
                return;
            }

            stats.Add(new InventoryItemStat(STAT_MARKER + _window.Label("stat_knife_weak_damage", "Weak attack damage"), weak.MeleeDamageHp.ToString("0.##", CultureInfo.InvariantCulture) + " HP"));
            stats.Add(new InventoryItemStat(STAT_MARKER + _window.Label("stat_knife_strong_damage", "Strong attack damage"), strong.MeleeDamageHp.ToString("0.##", CultureInfo.InvariantCulture) + " HP"));
            string reach = weak.MeleeReach.ToString("0.##", CultureInfo.InvariantCulture);
            if (!Mathf.Approximately(weak.MeleeReach, strong.MeleeReach))
            {
                reach += " / " + strong.MeleeReach.ToString("0.##", CultureInfo.InvariantCulture);
            }

            stats.Add(new InventoryItemStat(STAT_MARKER + _window.Label("stat_knife_reach", "Attack reach"), reach + " " + _window.Label("stat_unit_m", "m")));
        }

        private void AddGrenadeStats(List<InventoryItemStat> stats, GrenadeAmmoData grenade, bool handThrown)
        {
            string type = _window.Label("grenade_type_launcher", "Underbarrel");
            if (handThrown)
            {
                type = grenade.ExplodeOnImpact ? _window.Label("grenade_type_hand_impact", "Impact hand grenade") : _window.Label("grenade_type_hand", "Hand grenade");
            }
            else if (grenade.ImpactBounceHeight > 0f)
            {
                type = _window.Label("grenade_type_launcher_bounce", "Bouncing underbarrel");
            }

            string meters = _window.Label("stat_unit_m", "m");
            string detonation = grenade.SelfDestructSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " " + _window.Label("stat_unit_s", "s");
            if (!handThrown || grenade.ExplodeOnImpact)
            {
                detonation = string.Format(CultureInfo.InvariantCulture, _window.Label("grenade_detonation_limit", "Up to {0}"), detonation);
            }

            stats.Add(new InventoryItemStat(STAT_MARKER + _window.Label("stat_grenade_type", "Grenade type"), type));
            stats.Add(new InventoryItemStat(STAT_MARKER + _window.Label("stat_grenade_blast_radius", "Blast radius"), grenade.BlastRadius.ToString("0.##", CultureInfo.InvariantCulture) + " " + meters));
            stats.Add(new InventoryItemStat(STAT_MARKER + _window.Label("stat_grenade_blast_damage", "Blast damage"), grenade.BlastDamage.ToString("0.##", CultureInfo.InvariantCulture) + " HP"));
            stats.Add(new InventoryItemStat(STAT_MARKER + _window.Label("stat_grenade_fragment_count", "Fragment count"), grenade.FragmentCount.ToString(CultureInfo.InvariantCulture)));
            stats.Add(new InventoryItemStat(STAT_MARKER + _window.Label("stat_grenade_fragment_damage", "Fragment damage"), grenade.FragmentDamage.ToString("0.##", CultureInfo.InvariantCulture) + " HP"));
            stats.Add(new InventoryItemStat(STAT_MARKER + _window.Label("stat_grenade_fragment_range", "Fragment range"), grenade.FragmentRange.ToString("0.##", CultureInfo.InvariantCulture) + " " + meters));
            stats.Add(new InventoryItemStat(STAT_MARKER + _window.Label("stat_grenade_detonation", "Detonation time"), detonation));
        }

        private void AppendFeatures(EquipmentStatsComponent component, bool patchRemoved)
        {
            string community = component.Community.Length > 0 ? _window.Label("community_" + component.Community, component.Community) : _window.Label("community_unknown", "Unknown");
            _builder.Append("\n\n");
            AppendColor(GRAY_1_COLOR).Append(' ').Append(_window.Label("tooltip_features", "FEATURES")).Append(":\n");
            AppendColor(YELLOW_COLOR).Append(FEATURE_BULLET);
            AppendColor(GRAY_2_COLOR).Append(_window.Label("feature_community", "faction")).Append(": ");
            AppendColor(GRAY_1_COLOR).Append(community).Append('\n');
            if (patchRemoved)
            {
                AppendColor(PURPLE_COLOR).Append(FEATURE_BULLET);
                AppendColor(GRAY_1_COLOR).Append(_window.Label("feature_patch_missing", "Нашивка группировки отсутствует")).Append('\n');
            }

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

        private void AddAnomalyProtection(List<InventoryItemStat> stats, StatRow row, ItemDefinition definition, EquipmentStatsComponent component, float condition, bool fullCondition, AnomalyDamageType type)
        {
            float value = GetMeanProtection(AnomalyEquipmentProtection.GetDefinitionValues(component, fullCondition ? 1f : condition, type, _anomalyConfig));
            if (value == 0f)
            {
                return;
            }
            InventoryContainer equipment = _window.Model != null ? _window.Model.GetContainer(_window.EquipmentContainerId) : null;
            AnomalyProtectionValues current = AnomalyEquipmentProtection.GetCombinedValues(equipment, type, _anomalyConfig, fullCondition: fullCondition);
            AnomalyProtectionValues preview = AnomalyEquipmentProtection.GetPreviewValues(equipment, type, _anomalyConfig, definition, condition, fullCondition);
            float difference = GetMeanProtection(preview) - GetMeanProtection(current);
            string comparison = null;
            InventoryStatTone tone = InventoryStatTone.Normal;
            if (difference != 0f)
            {
                comparison = "(" + FormatProtectionDifference(difference) + ")";
                tone = difference > 0f ? InventoryStatTone.Better : InventoryStatTone.Worse;
            }
            string text = FormatProtection(value);
            stats.Add(new InventoryItemStat(_window.Label(row.Key, row.Fallback), text, _window.Theme != null ? _window.Theme.Icon(row.Icon) : null, tone, comparison));
        }

        private static float GetMeanProtection(AnomalyProtectionValues values) => (values.Get(AnomalyStrength.Weak) + values.Get(AnomalyStrength.Average) + values.Get(AnomalyStrength.Strong)) / 3f;

        private static string FormatProtection(float value) => value.ToString("0.##", CultureInfo.InvariantCulture) + PERCENT_UNIT;

        private static string FormatProtectionDifference(float value) => (value > 0f ? "+" : string.Empty) + value.ToString("0.##", CultureInfo.InvariantCulture);

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
