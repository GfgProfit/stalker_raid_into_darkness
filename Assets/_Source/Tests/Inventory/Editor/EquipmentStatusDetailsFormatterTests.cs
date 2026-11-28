using System.Collections.Generic;
using System.Text.RegularExpressions;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class EquipmentStatusDetailsFormatterTests
    {
        private readonly List<ItemDefinition> _definitions = new();

        private Scene _preview;
        private GameObject _root;
        private InventoryWindow _window;
        private InventoryModel _model;
        private AnomalyBalanceConfig _config;

        [SetUp]
        public void SetUp()
        {
            _preview = EditorSceneManager.NewPreviewScene();
            _root = new GameObject(nameof(EquipmentStatusDetailsFormatterTests)) { hideFlags = HideFlags.HideAndDontSave };
            _root.SetActive(false);
            SceneManager.MoveGameObjectToScene(_root, _preview);
            _window = _root.AddComponent<InventoryWindow>();
            _model = new InventoryModel();
            _model.AddContainer(new GridContainer("Equipment", 2, 1));
            _window.ConfigureContainers("Backpack", "Loot", "Equipment", "Player", 35f);
            _window.Bind(_model, null);
            _config = ScriptableObject.CreateInstance<AnomalyBalanceConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_config);
            foreach (ItemDefinition definition in _definitions)
            {
                Object.DestroyImmediate(definition);
            }
            _definitions.Clear();
            EditorSceneManager.ClosePreviewScene(_preview);
        }

        [Test]
        public void DetailsExplainCombinedTriplesConditionAndBothEquipmentContributions()
        {
            ItemInstance outfit = Add(CreateDefinition("Armor", "Тестовая броня", new AnomalyProtectionValues(80f, 40f, 20f)), Vector2Int.zero);
            Assert.That(_window.Model.TrySetItemStateInPlace(outfit, EquipmentStatsFormatter.CONDITION_STATE, "0.5"), Is.True);
            Add(CreateDefinition("Head", "Тестовый шлем", new AnomalyProtectionValues(20f, 30f, 40f)), Vector2Int.right);
            var formatter = new EquipmentStatusDetailsFormatter(_window, _config);
            Assert.That(formatter.TryDescribe(EquipmentStatusPresenter.STATUS_SHOCK, false, out string title, out string description), Is.True);
            Assert.That(title, Is.EqualTo("Электрозащита"));
            Assert.That(Plain(description), Does.Contain("Слабая: 42.4%"));
            Assert.That(Plain(description), Does.Contain("Средняя: 39.8%"));
            Assert.That(Plain(description), Does.Contain("Сильная: 44.2%"));
            Assert.That(Plain(description), Does.Contain("Среднее в панели: 42 / 100 %"));
            Assert.That(Plain(description), Does.Contain("Костюм: Тестовая броня"));
            Assert.That(Plain(description), Does.Contain("Шлем: Тестовый шлем"));
            Assert.That(Plain(description), Does.Contain("Состояние предмета: 50%"));
            Assert.That(Plain(description), Does.Contain("Эффективность защиты: 35%"));
            Assert.That(Plain(description), Does.Contain("Эффективная защита: 28% / 14% / 7%"));
            Assert.That(Plain(description), Does.Contain("Прибавка к общей защите: +14.4 / +25.8 / +37.2 п.п."));
            Assert.That(formatter.TryDescribe(EquipmentStatusPresenter.STATUS_SHOCK, true, out _, out description), Is.True);
            Assert.That(Plain(description), Does.Contain("Среднее в панели: 65 / 100 %"));
            Assert.That(Plain(description), Does.Contain("Расчёт при полном состоянии предметов."));
            Assert.That(outfit.GetState(EquipmentStatsFormatter.CONDITION_STATE), Is.EqualTo("0.5"));
        }

        [Test]
        public void DetailsExplainBlockedExternalHelmetAndBallisticCondition()
        {
            ItemDefinition definition = CreateDefinition("Armor", "Броня со шлемом", new AnomalyProtectionValues(80f, 40f, 20f), false);
            using (var serialized = new SerializedObject(definition))
            {
                SerializedProperty stats = serialized.FindProperty("_components").GetArrayElementAtIndex(0);
                stats.FindPropertyRelative("_fireWoundProtection").floatValue = 25f;
                stats.FindPropertyRelative("_helmetFireWoundProtection").floatValue = 10f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            ItemInstance outfit = Add(definition, Vector2Int.zero);
            Assert.That(_window.Model.TrySetItemStateInPlace(outfit, EquipmentStatsFormatter.CONDITION_STATE, "0.5"), Is.True);
            Add(CreateDefinition("Head", "Отдельный шлем", new AnomalyProtectionValues(90f, 90f, 90f)), Vector2Int.right);
            var formatter = new EquipmentStatusDetailsFormatter(_window, _config);
            formatter.TryDescribe(EquipmentStatusPresenter.STATUS_SHOCK, false, out _, out string description);
            Assert.That(Plain(description), Does.Contain("Слабая: 28%"));
            Assert.That(Plain(description), Does.Contain("Не учитывается: у костюма встроенный шлем."));
            formatter.TryDescribe(EquipmentStatusPresenter.STATUS_FIRE_WOUND, false, out _, out description);
            Assert.That(Plain(description), Does.Contain("Общая защита: 17.5%"));
            Assert.That(Plain(description), Does.Contain("В панели: 18 / 100 %"));
            Assert.That(Plain(description), Does.Not.Contain("Слабая:"));
        }

        [TestCase(EquipmentStatusPresenter.STATUS_SHOCK)]
        [TestCase(EquipmentStatusPresenter.STATUS_FIRE_WOUND)]
        public void BuiltInHelmetIsDescribedWithoutSeparateHelmet(string id)
        {
            Add(CreateDefinition("Armor", "Броня со шлемом", new AnomalyProtectionValues(80f, 40f, 20f), false), Vector2Int.zero);
            var formatter = new EquipmentStatusDetailsFormatter(_window, _config);
            Assert.That(formatter.TryDescribe(id, false, out _, out string description), Is.True);
            Assert.That(Plain(description), Does.Contain("Шлем: встроен в костюм"));
            Assert.That(Plain(description), Does.Contain("Его защита учтена в показателях костюма."));
            Assert.That(Plain(description), Does.Not.Contain("Шлем: не надет"));
        }

        [Test]
        public void EmptyEquipmentProducesZeroAndUnknownStatusDoesNotProduceTooltip()
        {
            var formatter = new EquipmentStatusDetailsFormatter(_window, _config);
            Assert.That(formatter.TryDescribe(EquipmentStatusPresenter.STATUS_CHEMICAL, false, out _, out string description), Is.True);
            Assert.That(Plain(description), Does.Contain("Среднее в панели: 0 / 100 %"));
            Assert.That(Plain(description), Does.Contain("Костюм: не надет"));
            Assert.That(Plain(description), Does.Contain("Шлем: не надет"));
            Assert.That(formatter.TryDescribe("unknown", false, out string title, out description), Is.False);
            Assert.That(title, Is.Empty);
            Assert.That(description, Is.Empty);
        }

        [Test]
        public void DetailsUseThemeColorsAndBalancedSemanticSections()
        {
            InventoryTheme theme = ScriptableObject.CreateInstance<InventoryTheme>();
            try
            {
                using (var serialized = new SerializedObject(theme))
                {
                    serialized.FindProperty("_textColor").colorValue = Color.white;
                    serialized.FindProperty("_mutedTextColor").colorValue = Color.gray;
                    serialized.FindProperty("_accentColor").colorValue = Color.cyan;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                _window.ConfigurePresentation(theme, null, string.Empty, string.Empty, Color.red);
                ItemInstance outfit = Add(CreateDefinition("Armor", "Тестовая броня", new AnomalyProtectionValues(80f, 40f, 20f)), Vector2Int.zero);
                Assert.That(_window.Model.TrySetItemStateInPlace(outfit, EquipmentStatsFormatter.CONDITION_STATE, "0.5"), Is.True);
                Add(CreateDefinition("Head", "Тестовый шлем", new AnomalyProtectionValues(20f, 30f, 40f)), Vector2Int.right);
                var formatter = new EquipmentStatusDetailsFormatter(_window, _config);
                formatter.TryDescribe(EquipmentStatusPresenter.STATUS_SHOCK, false, out _, out string description);
                Assert.That(description, Does.Contain("<b><color=#00FFFFFF>Среднее в панели: 42 / 100 %</color></b>"));
                Assert.That(description, Does.Contain("<b><color=#00FFFFFF>Костюм: Тестовая броня</color></b>"));
                Assert.That(description, Does.Contain("<b><color=#00FFFFFF>Шлем: Тестовый шлем</color></b>"));
                Assert.That(description, Does.Contain("<color=#808080FF>Состояние предмета: </color><color=#FFFFFFFF>50%</color>"));
                Assert.That(description, Does.Contain("<color=#99D6A3FF>42.4%</color>"));
                Assert.That(description, Does.Contain("<color=#F5C773FF>39.8%</color>"));
                Assert.That(description, Does.Contain("<color=#E89985FF>44.2%</color>"));
                Assert.That(description, Does.Contain("<color=#99D6A3FF>+14.4 / +25.8 / +37.2 п.п.</color>"));
                AssertBalancedTags(description);
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [TestCase("0.999", "99.9%")]
        [TestCase("1", "100%")]
        [TestCase("0.91", "91%")]
        public void ItemConditionUsesOneOptionalDecimal(string state, string expected)
        {
            ItemInstance outfit = Add(CreateDefinition("Armor", "Тестовая броня", new AnomalyProtectionValues(80f, 40f, 20f)), Vector2Int.zero);
            Assert.That(_window.Model.TrySetItemStateInPlace(outfit, EquipmentStatsFormatter.CONDITION_STATE, state), Is.True);
            var formatter = new EquipmentStatusDetailsFormatter(_window, _config);
            formatter.TryDescribe(EquipmentStatusPresenter.STATUS_SHOCK, false, out _, out string description);
            Assert.That(Plain(description), Does.Contain("Состояние предмета: " + expected));
            AssertBalancedTags(description);
        }

        [Test]
        public void ConditionTooltipShowsValuesAndReducedMaximumAgainstCapacity()
        {
            StalkerActorCondition condition = _root.AddComponent<StalkerActorCondition>();
            condition.ApplyConfiguration(null);
            condition.SetMaxPower(70f);
            condition.SetPower(30f);
            var formatter = new EquipmentStatusDetailsFormatter(_window, _config);
            Assert.That(formatter.TryDescribeCondition(InventoryWindowController.STATUS_POWER, condition, out string title, out string description), Is.True);
            Assert.That(title, Is.EqualTo("Стамина"));
            Assert.That(Plain(description), Does.Contain("Текущее значение: 30"));
            Assert.That(Plain(description), Does.Contain("Текущий максимум: 70"));
            Assert.That(Plain(description), Does.Contain("Базовая ёмкость: 100"));
            Assert.That(Plain(description), Does.Contain("В панели: 30 / 70 %"));
            Assert.That(formatter.TryDescribeCondition("unknown", condition, out _, out _), Is.False);
            Assert.That(formatter.TryDescribeCondition(InventoryWindowController.STATUS_POWER, null, out _, out _), Is.False);
        }

        private ItemInstance Add(ItemDefinition definition, Vector2Int position)
        {
            ItemInstance item = _model.CreateItem(definition);
            Assert.That(_model.TryAdd(item, "Equipment", position, false).Success, Is.True);
            return item;
        }

        private static string Plain(string value) => Regex.Replace(value, "<[^>]+>", string.Empty);

        private static void AssertBalancedTags(string value)
        {
            var tags = new Stack<string>();
            foreach (Match match in Regex.Matches(value, "<(/?)(b|color)(?:=[^>]+)?>"))
            {
                string tag = match.Groups[2].Value;
                if (match.Groups[1].Value.Length == 0)
                {
                    tags.Push(tag);
                }
                else
                {
                    Assert.That(tags.Count, Is.GreaterThan(0));
                    Assert.That(tags.Pop(), Is.EqualTo(tag));
                }
            }
            Assert.That(tags, Is.Empty);
        }

        private ItemDefinition CreateDefinition(string category, string displayName, AnomalyProtectionValues values, bool helmetAvailable = true)
        {
            ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
            _definitions.Add(definition);
            using var serialized = new SerializedObject(definition);
            serialized.FindProperty("_definitionId").stringValue = "status-details-test-" + _definitions.Count;
            serialized.FindProperty("_displayName").stringValue = displayName;
            serialized.FindProperty("_category").stringValue = category;
            SerializedProperty components = serialized.FindProperty("_components");
            components.arraySize = 1;
            components.GetArrayElementAtIndex(0).managedReferenceValue = new EquipmentStatsComponent();
            SerializedProperty stats = components.GetArrayElementAtIndex(0);
            stats.FindPropertyRelative("_helmetAvailable").boolValue = helmetAvailable;
            SerializedProperty protections = stats.FindPropertyRelative("_anomalyProtections");
            protections.arraySize = 1;
            protections.GetArrayElementAtIndex(0).boxedValue = new AnomalyEquipmentProtectionEntry(AnomalyDamageType.Shock, values);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }
    }
}
