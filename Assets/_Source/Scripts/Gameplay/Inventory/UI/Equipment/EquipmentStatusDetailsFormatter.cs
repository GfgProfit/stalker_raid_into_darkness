using RaidIntoDarkness.Inventory.Domain;
using System.Globalization;
using System.Text;
using RaidIntoDarkness.Player;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class EquipmentStatusDetailsFormatter
    {
        private static readonly Color WEAK_COLOR = new(0.6f, 0.84f, 0.64f, 1f);
        private static readonly Color AVERAGE_COLOR = new(0.96f, 0.78f, 0.45f, 1f);
        private static readonly Color STRONG_COLOR = new(0.91f, 0.6f, 0.52f, 1f);
        private static readonly Color DEFAULT_TEXT_COLOR = new(0.9f, 0.9f, 0.86f, 1f);
        private static readonly Color DEFAULT_MUTED_COLOR = new(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color DEFAULT_ACCENT_COLOR = new(0.94f, 0.85f, 0.71f, 1f);
        private static readonly EquipmentStatusDetail[] ROWS =
        {
            new(EquipmentStatusPresenter.STATUS_RADIATION, "stat_radiation", "Радиозащита", "Снижает воздействие очагов радиации.", AnomalyDamageType.Radiation),
            new(EquipmentStatusPresenter.STATUS_CHEMICAL, "stat_chemical_burn", "Химическая защита", "Снижает воздействие кислотных и химических аномалий.", AnomalyDamageType.ChemicalBurn),
            new(EquipmentStatusPresenter.STATUS_SHOCK, "stat_shock", "Электрозащита", "Снижает воздействие электрических аномалий.", AnomalyDamageType.Shock),
            new(EquipmentStatusPresenter.STATUS_FIRE, "stat_burn", "Термозащита", "Снижает воздействие жара и термических аномалий.", AnomalyDamageType.Burn),
            new(EquipmentStatusPresenter.STATUS_PSI, "stat_telepatic", "Пси-защита", "Снижает воздействие пси-полей на пси-здоровье.", AnomalyDamageType.Telepatic),
            new(EquipmentStatusPresenter.STATUS_WOUND, "stat_wound", "Защита от разрыва", "Снижает воздействие аномалий с разрывающим воздействием.", AnomalyDamageType.Wound),
            new(EquipmentStatusPresenter.STATUS_FIRE_WOUND, "stat_fire_wound", "Баллистическая защита", "Показывает общую баллистическую защиту экипировки.", AnomalyDamageType.None)
        };

        private readonly InventoryWindow _window;
        private readonly AnomalyBalanceConfig _config;
        private readonly StringBuilder _builder = new(1024);

        private Color TextColor => _window != null && _window.Theme != null ? _window.Theme.TextColor : DEFAULT_TEXT_COLOR;
        private Color MutedColor => _window != null && _window.Theme != null ? _window.Theme.MutedTextColor : DEFAULT_MUTED_COLOR;
        private Color AccentColor => _window != null && _window.Theme != null ? _window.Theme.AccentColor : DEFAULT_ACCENT_COLOR;

        public EquipmentStatusDetailsFormatter(InventoryWindow window, AnomalyBalanceConfig config = null)
        {
            _window = window;
            _config = config != null ? config : GameConfig.Load()?.AnomalyBalance;
        }

        public bool TryDescribe(string id, bool fullCondition, out string title, out string description)
        {
            for (int i = 0; i < ROWS.Length; i++)
            {
                EquipmentStatusDetail row = ROWS[i];
                if (row.Id != id)
                {
                    continue;
                }
                title = Label(row.Key, row.Title);
                ItemInstance outfit = EquipmentItemStats.FindEquipped(_window.Model?.GetContainer(_window.EquipmentContainerId), EquipmentStatsFormatter.OUTFIT_CATEGORY);
                ItemInstance helmet = EquipmentItemStats.FindEquipped(_window.Model?.GetContainer(_window.EquipmentContainerId), EquipmentStatsFormatter.HELMET_CATEGORY);
                EquipmentStatsComponent outfitStats = GetStats(outfit);
                bool helmetAvailable = outfitStats == null || outfitStats.HelmetAvailable;
                _builder.Clear();
                AppendStyled(row.Description, MutedColor);
                if (fullCondition)
                {
                    AppendNote("\nРасчёт при полном состоянии предметов.");
                }
                if (row.Type == AnomalyDamageType.None)
                {
                    AppendBallistic(outfit, helmet, helmetAvailable, fullCondition);
                }
                else
                {
                    InventoryContainer equipment = _window != null && _window.Model != null ? _window.Model.GetContainer(_window.EquipmentContainerId) : null;
                    AnomalyProtectionValues combined = AnomalyEquipmentProtection.GetCombinedValues(equipment, row.Type, _config, fullCondition: fullCondition);
                    float mean = (combined.Get(AnomalyStrength.Weak) + combined.Get(AnomalyStrength.Average) + combined.Get(AnomalyStrength.Strong)) / 3f;
                    _builder.Append('\n');
                    AppendField("Слабая: ", Percent(combined.Get(AnomalyStrength.Weak)), WEAK_COLOR);
                    AppendField("Средняя: ", Percent(combined.Get(AnomalyStrength.Average)), AVERAGE_COLOR);
                    AppendField("Сильная: ", Percent(combined.Get(AnomalyStrength.Strong)), STRONG_COLOR);
                    AppendSummary("Среднее в панели: " + Mathf.RoundToInt(mean) + " / 100 %");
                    AppendNote("\nСреднее = (слабая + средняя + сильная) / 3.");
                    AppendAnomalyItem("Костюм", outfit, row.Type, helmetAvailable, fullCondition);
                    if (AppendBuiltInHelmet(helmet, helmetAvailable) == false)
                    {
                        AppendAnomalyItem("Шлем", helmet, row.Type, helmetAvailable, fullCondition);
                    }
                    if (AnomalyEquipmentProtection.IsApplicable(helmet, row.Type, helmetAvailable, _config))
                    {
                        AnomalyProtectionValues withoutHelmet = AnomalyEquipmentProtection.GetCombinedValues(equipment, row.Type, _config, removed: helmet, fullCondition: fullCondition);
                        string gain = "+" + Number(combined.Get(AnomalyStrength.Weak) - withoutHelmet.Get(AnomalyStrength.Weak)) + " / +" + Number(combined.Get(AnomalyStrength.Average) - withoutHelmet.Get(AnomalyStrength.Average)) + " / +" + Number(combined.Get(AnomalyStrength.Strong) - withoutHelmet.Get(AnomalyStrength.Strong)) + " п.п.";
                        AppendField("Прибавка к общей защите: ", gain, WEAK_COLOR);
                    }
                    AppendNote("\n\nПорядок вкладов: слабая / средняя / сильная.");
                    AppendNote("\nШлем защищает от части воздействия, оставшейся после брони.");
                }
                description = _builder.ToString();
                return true;
            }
            title = string.Empty;
            description = string.Empty;
            return false;
        }

        public bool TryDescribeCondition(string id, StalkerActorCondition condition, out string title, out string description)
        {
            title = string.Empty;
            description = string.Empty;
            if (condition == null)
            {
                return false;
            }
            float value;
            float maximum;
            float capacity;
            string meaning;
            switch (id)
            {
                case InventoryWindowController.STATUS_HEALTH:
                    title = "Здоровье";
                    meaning = "Текущее здоровье персонажа.";
                    value = condition.Health;
                    maximum = condition.MaxHealth;
                    capacity = condition.HealthCapacity;
                    break;
                case InventoryWindowController.STATUS_POWER:
                    title = "Стамина";
                    meaning = "Текущий запас выносливости персонажа.";
                    value = condition.Power;
                    maximum = condition.MaxPower;
                    capacity = condition.PowerCapacity;
                    break;
                case InventoryWindowController.STATUS_HUNGER:
                    title = "Сытость";
                    meaning = "Текущий запас сытости персонажа.";
                    value = condition.Hunger;
                    maximum = condition.MaxHunger;
                    capacity = condition.HungerCapacity;
                    break;
                case InventoryWindowController.STATUS_THIRST:
                    title = "Вода";
                    meaning = "Текущий запас воды в организме персонажа.";
                    value = condition.Thirst;
                    maximum = condition.MaxThirst;
                    capacity = condition.ThirstCapacity;
                    break;
                case InventoryWindowController.STATUS_SLEEP:
                    title = "Отдых";
                    meaning = "Текущий запас отдыха персонажа.";
                    value = condition.Sleep;
                    maximum = condition.MaxSleep;
                    capacity = condition.SleepCapacity;
                    break;
                default:
                    return false;
            }
            _builder.Clear();
            AppendStyled(meaning, MutedColor);
            _builder.Append('\n');
            AppendField("Текущее значение: ", Number(value));
            AppendField("Текущий максимум: ", Number(maximum));
            AppendField("Базовая ёмкость: ", Number(capacity));
            if (float.IsFinite(capacity) && capacity > 0f)
            {
                _builder.Append('\n');
                AppendSummary("В панели: " + Mathf.RoundToInt(Mathf.Clamp01(value / capacity) * 100f) + " / " + Mathf.RoundToInt(Mathf.Clamp01(maximum / capacity) * 100f) + " %");
                AppendNote("\nСлева — текущее значение, справа — текущий максимум; оба указаны в процентах базовой ёмкости.");
            }
            description = _builder.ToString();
            return true;
        }

        private bool AppendBuiltInHelmet(ItemInstance helmet, bool helmetAvailable)
        {
            if (helmet != null || helmetAvailable)
            {
                return false;
            }
            AppendSection("Шлем: встроен в костюм");
            AppendNote("\nЕго защита учтена в показателях костюма.");
            return true;
        }

        private void AppendAnomalyItem(string label, ItemInstance item, AnomalyDamageType type, bool helmetAvailable, bool fullCondition)
        {
            EquipmentStatsComponent stats = GetStats(item);
            if (stats == null)
            {
                AppendSection(label + ": не надет");
                return;
            }
            AppendSection(label + ": " + item.Definition.DisplayName);
            AppendCondition(item, stats, fullCondition, true);
            if (!AnomalyEquipmentProtection.IsApplicable(item, type, helmetAvailable, _config))
            {
                AppendNote(helmetAvailable ? "\nВклад отключён для этого типа защиты." : "\nНе учитывается: у костюма встроенный шлем.");
                return;
            }
            AppendField("Без износа: ", FormatValues(AnomalyEquipmentProtection.GetItemValues(item, type, _config, true)));
            AppendField("Эффективная защита: ", FormatValues(AnomalyEquipmentProtection.GetItemValues(item, type, _config, fullCondition)));
        }

        private void AppendBallistic(ItemInstance outfit, ItemInstance helmet, bool helmetAvailable, bool fullCondition)
        {
            float outfitValue = GetBallistic(outfit, fullCondition);
            float helmetValue = helmetAvailable ? GetBallistic(helmet, fullCondition) : 0f;
            _builder.Append('\n');
            AppendField("Общая защита: ", Percent(Mathf.Clamp(outfitValue + helmetValue, 0f, 100f)));
            AppendSummary("В панели: " + Mathf.RoundToInt(Mathf.Clamp(outfitValue + helmetValue, 0f, 100f)) + " / 100 %");
            AppendNote("\nДеление на слабую, среднюю и сильную аномалию к баллистической защите не применяется.");
            AppendBallisticItem("Костюм", outfit, fullCondition, true);
            if (AppendBuiltInHelmet(helmet, helmetAvailable) == false)
            {
                AppendBallisticItem("Шлем", helmet, fullCondition, helmetAvailable);
            }
            AppendNote("\n\nБаллистические показатели складываются с ограничением 100%; каждый вклад зависит от состояния своего предмета.");
        }

        private void AppendBallisticItem(string label, ItemInstance item, bool fullCondition, bool applicable)
        {
            EquipmentStatsComponent stats = GetStats(item);
            if (stats == null)
            {
                AppendSection(label + ": не надет");
                return;
            }
            AppendSection(label + ": " + item.Definition.DisplayName);
            AppendCondition(item, stats, fullCondition, false);
            if (applicable)
            {
                AppendField("Эффективная защита: ", Percent(GetBallistic(item, fullCondition)));
            }
            else
            {
                AppendNote("\nНе учитывается: у костюма встроенный шлем.");
            }
        }

        private void AppendCondition(ItemInstance item, EquipmentStatsComponent stats, bool fullCondition, bool anomaly)
        {
            float condition = AnomalyEquipmentProtection.GetCondition(item);
            float calculatedCondition = fullCondition ? 1f : condition;
            float efficiency = anomaly ? (_config != null ? _config.EvaluateConditionEfficiency(calculatedCondition) : AnomalyProtectionMath.DefaultEfficiency(calculatedCondition)) : calculatedCondition;
            AppendField("Состояние предмета: ", (condition * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%");
            AppendField("Эффективность защиты: ", Percent(efficiency * 100f));
            AppendField(anomaly ? "Аномальная прочность: " : "Физическая прочность: ", Percent(anomaly ? stats.AnomalyDurability : stats.PhysicalDurability));
        }

        private void AppendSection(string value)
        {
            _builder.Append("\n\n");
            AppendStyled(value, AccentColor, true);
        }

        private void AppendSummary(string value)
        {
            _builder.Append('\n');
            AppendStyled(value, AccentColor, true);
        }

        private void AppendField(string label, string value, Color? valueColor = null)
        {
            _builder.Append('\n');
            AppendStyled(label, MutedColor);
            AppendStyled(value, valueColor ?? TextColor);
        }

        private void AppendNote(string value) => AppendStyled(value, MutedColor);

        private void AppendStyled(string value, Color color, bool bold = false)
        {
            if (bold)
            {
                _builder.Append("<b>");
            }
            _builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(color)).Append('>').Append(value).Append("</color>");
            if (bold)
            {
                _builder.Append("</b>");
            }
        }

        private string Label(string key, string fallback) => _window != null ? _window.Label(key, fallback) : fallback;

        private static EquipmentStatsComponent GetStats(ItemInstance item) => item != null ? item.Definition.GetComponent<EquipmentStatsComponent>() : null;

        private static float GetBallistic(ItemInstance item, bool fullCondition)
        {
            EquipmentStatsComponent stats = GetStats(item);
            return stats != null ? (stats.GetValue(EquipmentStat.FireWound) + stats.GetValue(EquipmentStat.HelmetFireWound)) * (fullCondition ? 1f : AnomalyEquipmentProtection.GetCondition(item)) : 0f;
        }

        private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Percent(float value) => Number(value) + "%";

        private static string FormatValues(AnomalyProtectionValues values) => Percent(values.Get(AnomalyStrength.Weak)) + " / " + Percent(values.Get(AnomalyStrength.Average)) + " / " + Percent(values.Get(AnomalyStrength.Strong));
    }
}
