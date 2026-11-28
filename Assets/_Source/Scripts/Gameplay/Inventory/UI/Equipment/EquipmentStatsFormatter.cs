using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.UI;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Inventory
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
        private const string COLOR_END = "</color>";
        private const int CONDITION_RED_THRESHOLD = 60;
        private const int CONDITION_YELLOW_THRESHOLD = 85;
        private const float RECOIL_SCALE_DEGREES = 10f;
        private const float FLATNESS_SCALE_METERS_PER_SECOND = 1300f;

        private static readonly Color GRAY_1_COLOR = new(170f / 255f, 170f / 255f, 170f / 255f, 1f);
        private static readonly Color GRAY_2_COLOR = new(140f / 255f, 140f / 255f, 140f / 255f, 1f);
        private static readonly Color ORANGE_COLOR = new(238f / 255f, 153f / 255f, 26f / 255f, 1f);
        private static readonly Color RED_COLOR = new(204f / 255f, 0f, 51f / 255f, 1f);
        private static readonly Color YELLOW_COLOR = new(1f, 1f, 0f, 1f);
        private static readonly Color GREEN_COLOR = new(51f / 255f, 1f, 102f / 255f, 1f);
        private static readonly Color PURPLE_COLOR = new(93f / 255f, 0f, 116f / 255f, 1f);
        private static readonly string STAT_MARKER = $"<color={Utils.ToHexRGB(PURPLE_COLOR)}>•</color> ";
        private static readonly NumberFormatInfo PRICE_NUMBER_FORMAT = new() { NumberGroupSeparator = NUMBER_SEPARATOR.ToString(), NumberDecimalSeparator = "," };

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
        private readonly InventoryWeaponAmmoSource _weaponAmmo;
        private readonly FirstPersonWeaponController _weapons;
        private readonly float _hipFireDispersionMultiplier;
        private readonly StringBuilder _builder = new(1024);

        public EquipmentStatsFormatter(InventoryWindow window, AnomalyBalanceConfig anomalyConfig = null) : this(window, null, null, anomalyConfig)
        {
        }

        internal EquipmentStatsFormatter(InventoryWindow window, InventoryWeaponAmmoSource weaponAmmo, FirstPersonWeaponController weapons, AnomalyBalanceConfig anomalyConfig = null)
        {
            _window = window;
            GameConfig gameConfig = GameConfig.Load();
            _anomalyConfig = anomalyConfig != null ? anomalyConfig : gameConfig?.AnomalyBalance;
            _hipFireDispersionMultiplier = gameConfig != null ? gameConfig.HipFireDispersionMultiplier : GameConfig.DEFAULT_HIP_FIRE_DISPERSION_MULTIPLIER;
            _weaponAmmo = weaponAmmo;
            _weapons = weapons;
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
            CollectDefinition(item.Definition, GetCondition(item), FullCondition, stats, item);
        }

        internal void CollectDefinition(ItemDefinition definition, float condition, bool fullCondition, List<InventoryItemStat> stats, ItemInstance item = null)
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

            WeaponAddonComponent addon = definition.GetComponent<WeaponAddonComponent>();
            if (addon != null && addon.Kind != WeaponAddonKind.Scope)
            {
                AddAddonStats(stats, addon);
                return;
            }

            FirstPersonWeaponAsset firearm = GetFirearmProfile(definition);
            if (firearm != null)
            {
                AddWeaponStats(stats, firearm, definition.GetComponent<WeaponStatsComponent>(), item);
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
                    value *= fullCondition && row.IsLiteralPercent(PERCENT_UNIT) ? 1f : condition;
                    otherValue *= otherCondition;
                }
                Add(stats, row, value, otherValue, row.Unit == KILOGRAM_UNIT ? kilograms : row.Unit);
            }
        }

        public string Describe(ItemInstance item)
        {
            return Describe(item, true);
        }

        internal string Describe(ItemInstance item, bool showDescription)
        {
            return DescribeDefinition(item.Definition, GetCondition(item), item.Amount, EquipmentAnimationQueue.IsPatchRemoved(item), showDescription);
        }

        internal string DescribeDefinition(ItemDefinition definition, float condition, int amount, bool patchRemoved = false, bool showDescription = true)
        {
            int? baseCost = ItemCostComponent.GetBaseCost(definition);
            if (!baseCost.HasValue)
            {
                return showDescription ? null : string.Empty;
            }

            int boxSize = Math.Max(1, definition.GetComponent<AmmoStatsComponent>()?.BoxSize ?? 1);
            decimal unitCost = (decimal)baseCost.Value / boxSize;
            if (SupportsCondition(definition))
            {
                unitCost *= (decimal)condition;
            }

            _builder.Clear();
            AppendCost("tooltip_cost", "Cost:", definition.IsStackable ? unitCost * amount : Math.Ceiling(unitCost));
            if (definition.IsStackable && amount > 1)
            {
                _builder.Append('\n');
                AppendCost("tooltip_unit_cost", "Per item:", unitCost);
            }

            EquipmentStatsComponent component = definition.GetComponent<EquipmentStatsComponent>();
            FirstPersonWeaponAsset firearm = GetFirearmProfile(definition);
            if (firearm != null || (component != null && GetEquipmentCategory(definition) != null))
            {
                double percent = (double)(decimal)condition * PERCENT;
                Color fullConditionColor = _window.Theme != null ? _window.Theme.ConditionColor : GREEN_COLOR;
                Color conditionColor = percent < CONDITION_RED_THRESHOLD ? RED_COLOR : percent < CONDITION_YELLOW_THRESHOLD ? YELLOW_COLOR : fullConditionColor;
                _builder.Append("\n\n");
                AppendColor(conditionColor).Append(BULLET).Append(COLOR_END);
                AppendColor(GRAY_1_COLOR).Append(_window.Label("tooltip_condition", "Condition")).Append(": ").Append(COLOR_END);
                AppendColor(conditionColor).Append(percent.ToString("0.#", CultureInfo.InvariantCulture)).Append('%').Append(COLOR_END);
            }

            if (showDescription && definition.Description.Length > 0)
            {
                _builder.Append("\n\n");
                AppendColor(GRAY_1_COLOR).Append(definition.Description).Append(COLOR_END);
            }

            if (component != null && definition.IsCategory(OUTFIT_CATEGORY))
            {
                AppendFeatures(component, patchRemoved);
            }

            return _builder.ToString();
        }

        private void AppendCost(string key, string fallback, decimal cost)
        {
            AppendColor(ORANGE_COLOR).Append(BULLET).Append(COLOR_END);
            AppendColor(GRAY_1_COLOR).Append(_window.Label(key, fallback)).Append(' ').Append(COLOR_END);
            AppendColor(ORANGE_COLOR).Append(Math.Round(cost, 2, MidpointRounding.AwayFromZero).ToString("#,0.##", PRICE_NUMBER_FORMAT)).Append(' ').Append(COLOR_END);
            AppendColor(GRAY_1_COLOR).Append(_window.Label("unit_currency", "RU")).Append(COLOR_END);
        }

        private static FirstPersonWeaponAsset GetFirearmProfile(ItemDefinition definition)
        {
            return definition.IsCategory("Weapon") && definition.GetComponent<WeaponStatsComponent>() != null && definition.GetComponent<WeaponHudComponent>()?.FirstPersonWeapon is { IsMelee: false, IsHandGrenade: false } profile ? profile : null;
        }

        private void AddWeaponStats(List<InventoryItemStat> rows, FirstPersonWeaponAsset profile, WeaponStatsComponent stats, ItemInstance item)
        {
            string section = InventoryWeaponAmmoSource.ResolveAmmoSection(item, stats);
            ItemDefinition ammo = _weaponAmmo?.FindDefinition(section);
            AmmoStatsComponent ammoStats = ammo?.GetComponent<AmmoStatsComponent>();
            int loaded = InventoryWeaponAmmoSource.GetLoadedMagazine(item);
            ItemDefinition silencer = stats.GetSilencer(item);
            WeaponAddonComponent silencerStats = silencer?.GetComponent<WeaponAddonComponent>();
            ItemDefinition scope = stats.GetScope(item);
            ItemDefinition launcher = stats.GetGrenadeLauncher(item);
            WeaponAddonComponent launcherStats = launcher?.GetComponent<WeaponAddonComponent>();
            string caliber = GetCaliber(section);
            if (!string.IsNullOrEmpty(caliber))
            {
                AddWeaponStat(rows, "stat_weapon_caliber", "Caliber", caliber);
            }

            if (loaded > 0 && ammo != null)
            {
                AddWeaponStat(rows, "stat_weapon_ammo", "Loaded ammunition", ShortDisplayName(ammo));
            }

            AddWeaponStat(rows, profile.IsStandaloneLauncher ? "stat_weapon_loaded" : "stat_weapon_magazine", profile.IsStandaloneLauncher ? "Loaded" : "Magazine", $"{loaded} / {profile.MagazineSize}");
            AddFireModes(rows, profile, item);
            float baseSpeed = InventoryWeaponAmmoSource.GetBulletSpeed(stats, ammoStats, null);
            float speed = InventoryWeaponAmmoSource.GetBulletSpeed(stats, ammoStats, silencerStats);
            AddWeaponBar(rows, "stat_weapon_flatness", "Flatness", FormatNumber(speed, false) + " " + _window.Label("stat_unit_mps", "m/s"), baseSpeed, speed, FLATNESS_SCALE_METERS_PER_SECOND, higherIsBetter: true);
            float aimTime = Mathf.Max(profile.AimTime, (profile.AimStart ?? profile.AimEnd)?.Duration ?? 0f);
            AddWeaponStat(rows, "stat_weapon_handling", "Handling (aim-in)", aimTime.ToString("0.##", PRICE_NUMBER_FORMAT) + " " + _window.Label("stat_unit_s", "s"));
            if (!profile.IsStandaloneLauncher || profile.UsesStandaloneHipDispersion)
            {
                float dispersion = InventoryWeaponAmmoSource.GetDispersionDegrees(stats, ammoStats, silencerStats);
                float aimed = profile.GetShotDispersionDegrees(dispersion, true, _hipFireDispersionMultiplier);
                float hip = profile.GetShotDispersionDegrees(dispersion, false, _hipFireDispersionMultiplier);
                AddWeaponStat(rows, "stat_weapon_dispersion", "Spread (aim / hip)", FormatAngle(aimed) + " / " + FormatAngle(hip));
            }

            if (scope != null || stats.ScopeStatus == WeaponAddonStatus.Permanent)
            {
                AddWeaponStat(rows, "stat_weapon_scope", "Sight", scope != null ? ShortDisplayName(scope) : _window.Label("stat_weapon_builtin", "Built-in"));
            }

            if (silencer != null || stats.SilencerStatus == WeaponAddonStatus.Permanent)
            {
                AddWeaponStat(rows, "stat_weapon_silencer", "Suppressor", silencer != null ? ShortDisplayName(silencer) : _window.Label("stat_weapon_builtin", "Built-in"));
            }

            if (launcher != null)
            {
                ItemDefinition round = launcherStats?.GetLoadedGrenade(item);
                string launcherValue = round != null ? $"{ShortDisplayName(launcher)} · {ShortDisplayName(round)} · 1 / 1" : $"{ShortDisplayName(launcher)} · 0 / 1";
                AddWeaponStat(rows, "stat_weapon_launcher", "Underbarrel launcher", launcherValue);
            }

            var (recoilMultiplier, _) = WeaponAddonComponent.GetEquippedRecoilMultipliers(silencerStats, launcherStats);
            Vector3 currentRecoil = Vector3.Scale(profile.Recoil, recoilMultiplier);
            AddWeaponBar(rows, "stat_weapon_recoil_vertical", "Vertical recoil", FormatAngle(currentRecoil.x), profile.Recoil.x, currentRecoil.x, RECOIL_SCALE_DEGREES, higherIsBetter: false);
            AddWeaponBar(rows, "stat_weapon_recoil_horizontal", "Horizontal recoil", FormatAngle(currentRecoil.y), profile.Recoil.y, currentRecoil.y, RECOIL_SCALE_DEGREES, higherIsBetter: false);
        }

        private void AddFireModes(List<InventoryItemStat> rows, FirstPersonWeaponAsset profile, ItemInstance item)
        {
            IReadOnlyList<int> modes = profile.FireModes;
            int active = Mathf.Clamp(_weapons?.GetSelectedFireModeIndex(item?.InstanceId) ?? 0, 0, modes.Count - 1);
            _builder.Clear();
            for (int i = 0; i < modes.Count; i++)
            {
                if (i > 0)
                {
                    _builder.Append('\n');
                }

                int mode = modes[i];
                string label = mode switch
                {
                    FirstPersonWeaponAsset.AUTO_FIRE_MODE => _window.Label("stat_weapon_mode_auto", "Auto"),
                    1 => _window.Label("stat_weapon_mode_single", "Single"),
                    _ => _window.FormatLabel("stat_weapon_mode_burst", "{0}-round burst", mode)
                };
                string rate = FormatNumber(profile.GetRoundsPerMinute(mode), false);
                if (i == active)
                {
                    AppendColor(ORANGE_COLOR);
                }

                _builder.Append(label).Append(" (").Append(rate).Append(')');
                if (i == active)
                {
                    _builder.Append(COLOR_END);
                }
            }

            AddWeaponStat(rows, "stat_weapon_fire_modes", "Fire modes (rpm)", _builder.ToString());
        }

        private void AddWeaponStat(List<InventoryItemStat> rows, string key, string fallback, string value)
        {
            rows.Add(new InventoryItemStat(STAT_MARKER + _window.Label(key, fallback), value));
        }

        private void AddWeaponBar(List<InventoryItemStat> rows, string key, string fallback, string value, float baseline, float current, float scale, bool higherIsBetter)
        {
            bool changed = !Mathf.Approximately(baseline, current);
            InventoryStatTone tone = InventoryStatTone.Normal;
            if (changed)
            {
                tone = (current > baseline) == higherIsBetter ? InventoryStatTone.Better : InventoryStatTone.Worse;
            }

            rows.Add(new InventoryItemStat(STAT_MARKER + _window.Label(key, fallback), value, tone: tone, fill: Mathf.Clamp01(baseline / scale), currentFill: changed ? Mathf.Clamp01(current / scale) : -1f));
        }

        private void AddAddonStats(List<InventoryItemStat> rows, WeaponAddonComponent addon)
        {
            if (addon.Kind == WeaponAddonKind.Silencer)
            {
                AddWeaponStat(rows, "stat_weapon_flatness", "Flatness", FormatAddonPercent((addon.BulletSpeedFactor - 1f) * 100f));
                AddWeaponStat(rows, "stat_weapon_recoil_damping", "Recoil damping", addon.RecoilDampingPercent.ToString("0.##", PRICE_NUMBER_FORMAT) + PERCENT_UNIT);
                return;
            }

            if (addon.Kind == WeaponAddonKind.GrenadeLauncher)
            {
                Vector3 recoil = addon.RecoilIncreasePercent;
                AddWeaponStat(rows, "stat_weapon_recoil_vertical", "Vertical recoil", FormatAddonPercent(recoil.x));
                AddWeaponStat(rows, "stat_weapon_recoil_horizontal", "Horizontal recoil", FormatAddonPercent(recoil.y));
                AddWeaponStat(rows, "stat_weapon_recoil_roll", "Recoil roll", FormatAddonPercent(recoil.z));
                AddWeaponStat(rows, "stat_weapon_recoil_snappiness", "Recoil snappiness", FormatAddonPercent(addon.RecoilSnappinessIncreasePercent));
            }
        }

        private static string FormatAddonPercent(float value) => value.ToString("+0.##;-0.##;0", PRICE_NUMBER_FORMAT) + PERCENT_UNIT;

        private static string FormatAngle(float angle) => angle.ToString("0.##", PRICE_NUMBER_FORMAT) + "°";

        private static string ShortDisplayName(ItemDefinition definition) => string.IsNullOrWhiteSpace(definition.ShortName) ? definition.DisplayName : definition.ShortName;

        private static string GetCaliber(string section)
        {
            const string AMMO_PREFIX = "ammo_";
            switch (section)
            {
                case "ammo_50ae":
                    return ".50 AE";
                case "ammo_44mag":
                case "ammo_44mag_boom":
                    return ".44 Magnum";
                case "ammo_m209":
                case "ammo_m397a1":
                    return "40x46 мм";
                case "ammo_vog-25":
                case "ammo_vog-25p":
                case "ammo_og-7b":
                    return "40 мм";
            }

            if (string.IsNullOrEmpty(section) || !section.StartsWith(AMMO_PREFIX, StringComparison.Ordinal))
            {
                return null;
            }

            int end = section.IndexOf('_', AMMO_PREFIX.Length);
            string caliber = end >= 0 ? section[AMMO_PREFIX.Length..end] : section[AMMO_PREFIX.Length..];
            return caliber.Length > 0 && char.IsDigit(caliber[0]) && caliber.Contains('x') ? caliber + " мм" : null;
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
            else if (grenade.FliesStraight)
            {
                type = _window.Label("grenade_type_rocket", "Rocket launcher round");
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

            if (row.IsLiteralPercent(PERCENT_UNIT))
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
    }
}
