using System.Collections.Generic;
using Inventory;
using Inventory.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class EquipmentPercentageStatsTests
    {
        private readonly List<ItemDefinition> _definitions = new();

        private Scene _preview;
        private GameObject _root;
        private InventoryWindow _window;

        [SetUp]
        public void SetUp()
        {
            _preview = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("EquipmentPercentageStatsTests") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(_root, _preview);
            _window = _root.AddComponent<InventoryWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            for (int i = 0; i < _definitions.Count; i++)
            {
                Object.DestroyImmediate(_definitions[i]);
            }

            _definitions.Clear();

            if (_preview.IsValid() && _preview.isLoaded)
            {
                EditorSceneManager.ClosePreviewScene(_preview);
            }
        }

        [TestCase("_fireWoundProtection", "Ballistic protection", true)]
        [TestCase("_helmetFireWoundProtection", "Helmet ballistic protection", true)]
        [TestCase("_woundProtection", "Rupture protection", true)]
        [TestCase("_strikeProtection", "Impact protection", true)]
        [TestCase("_explosionProtection", "Explosion protection", true)]
        [TestCase("_radiationProtection", "Radiation protection", true)]
        [TestCase("_telepaticProtection", "Psy protection", true)]
        [TestCase("_burnProtection", "Burn protection", true)]
        [TestCase("_shockProtection", "Shock protection", true)]
        [TestCase("_chemicalBurnProtection", "Chemical protection", true)]
        [TestCase("_physicalDurability", "Physical durability", false)]
        [TestCase("_anomalyDurability", "Anomaly durability", false)]
        public void StoredPercentUsesConditionOnlyForProtection(string field, string label, bool affectedByCondition)
        {
            ItemDefinition definition = CreateDefinition(EquipmentStatsFormatter.OUTFIT_CATEGORY, false);
            SetValue(definition, field, 23.75f);
            var item = new ItemInstance(definition, 1);
            item.SetState(EquipmentStatsFormatter.CONDITION_STATE, "0.13");
            var formatter = new EquipmentStatsFormatter(_window);
            var rows = new List<InventoryItemStat>();
            formatter.Collect(item, rows);
            Assert.That(Find(rows, label).Value, Is.EqualTo(affectedByCondition ? "3.09%" : "23.75%"));
            formatter.FullCondition = true;
            rows.Clear();
            formatter.Collect(item, rows);
            Assert.That(Find(rows, label).Value, Is.EqualTo("23.75%"));
        }

        [Test]
        public void SpeedKeepsItsExistingMultiplierAndLabel()
        {
            ItemDefinition definition = CreateDefinition(EquipmentStatsFormatter.OUTFIT_CATEGORY, true);
            SetValue(definition, "_speed", 1.03f);
            var item = new ItemInstance(definition, 1);
            item.SetState(EquipmentStatsFormatter.CONDITION_STATE, "0.1");
            var rows = new List<InventoryItemStat>();
            new EquipmentStatsFormatter(_window).Collect(item, rows);
            Assert.That(Find(rows, "Mobility multiplier").Value, Is.EqualTo("103 %"));
            Assert.That(definition.GetComponent<EquipmentStatsComponent>().Speed, Is.EqualTo(1.03f));
        }

        [Test]
        public void ResistanceComparisonUsesEachItemsConditionAndFullConditionToggle()
        {
            InventoryModel model = BindEquipment();
            ItemDefinition equippedDefinition = CreateDefinition(EquipmentStatsFormatter.OUTFIT_CATEGORY, true);
            SetValue(equippedDefinition, "_fireWoundProtection", 20f);
            ItemInstance equipped = model.CreateItem(equippedDefinition);
            equipped.SetState(EquipmentStatsFormatter.CONDITION_STATE, "0.9");
            Assert.That(model.TryAdd(equipped, "Equipment", Vector2Int.zero, false).Success, Is.True);
            ItemDefinition candidateDefinition = CreateDefinition(EquipmentStatsFormatter.OUTFIT_CATEGORY, true);
            SetValue(candidateDefinition, "_fireWoundProtection", 30f);
            var candidate = new ItemInstance(candidateDefinition, 1);
            candidate.SetState(EquipmentStatsFormatter.CONDITION_STATE, "0.1");
            var rows = new List<InventoryItemStat>();
            new EquipmentStatsFormatter(_window).Collect(candidate, rows);
            InventoryItemStat row = Find(rows, "Ballistic protection");
            Assert.That(row.Value, Is.EqualTo("3%"));
            Assert.That(row.Comparison, Is.EqualTo("(-15)"));
            Assert.That(row.Tone, Is.EqualTo(InventoryStatTone.Worse));
            rows.Clear();
            new EquipmentStatsFormatter(_window) { FullCondition = true }.Collect(candidate, rows);
            row = Find(rows, "Ballistic protection");
            Assert.That(row.Value, Is.EqualTo("30%"));
            Assert.That(row.Comparison, Is.EqualTo("(+10)"));
            Assert.That(row.Tone, Is.EqualTo(InventoryStatTone.Better));
        }

        [Test]
        public void StatusBarsCombineEachItemsConditionWithoutOriginalScale()
        {
            InventoryModel model = BindEquipment();
            ItemDefinition outfitDefinition = CreateDefinition(EquipmentStatsFormatter.OUTFIT_CATEGORY, true);
            SetValue(outfitDefinition, "_radiationProtection", 20f);
            SetValue(outfitDefinition, "_shockProtection", 40f);
            SetValue(outfitDefinition, "_fireWoundProtection", 25f);
            SetValue(outfitDefinition, "_helmetFireWoundProtection", 55f);
            ItemInstance outfit = model.CreateItem(outfitDefinition);
            outfit.SetState(EquipmentStatsFormatter.CONDITION_STATE, "0.2");
            Assert.That(model.TryAdd(outfit, "Equipment", Vector2Int.zero, false).Success, Is.True);
            ItemDefinition helmetDefinition = CreateDefinition(EquipmentStatsFormatter.HELMET_CATEGORY, true);
            SetValue(helmetDefinition, "_radiationProtection", 30f);
            SetValue(helmetDefinition, "_shockProtection", 10f);
            SetValue(helmetDefinition, "_fireWoundProtection", 5f);
            ItemInstance helmet = model.CreateItem(helmetDefinition);
            helmet.SetState(EquipmentStatsFormatter.CONDITION_STATE, "0.1");
            Assert.That(model.TryAdd(helmet, "Equipment", Vector2Int.right, false).Success, Is.True);
            InventoryStatusPanel panel = _root.AddComponent<InventoryStatusPanel>();
            panel.Configure(new[] { new InventoryStatusBar(EquipmentStatusPresenter.STATUS_RADIATION, null, null, null), new InventoryStatusBar(EquipmentStatusPresenter.STATUS_SHOCK, null, null, null), new InventoryStatusBar(EquipmentStatusPresenter.STATUS_FIRE_WOUND, null, null, null) });
            var presenter = new EquipmentStatusPresenter(_window, panel);
            presenter.Refresh(null, false);
            Assert.That(panel.GetValue(EquipmentStatusPresenter.STATUS_RADIATION), Is.EqualTo(0.07f).Within(0.000001f));
            Assert.That(panel.GetValue(EquipmentStatusPresenter.STATUS_SHOCK), Is.EqualTo(0.09f).Within(0.000001f));
            Assert.That(panel.GetValue(EquipmentStatusPresenter.STATUS_FIRE_WOUND), Is.EqualTo(0.055f).Within(0.000001f));
            presenter.Refresh(null, true);
            Assert.That(panel.GetValue(EquipmentStatusPresenter.STATUS_RADIATION), Is.EqualTo(0.5f).Within(0.000001f));
        }

        [Test]
        public void BuiltInHelmetVisibilityIsPreserved()
        {
            ItemDefinition definition = CreateDefinition(EquipmentStatsFormatter.OUTFIT_CATEGORY, true);
            SetValue(definition, "_helmetFireWoundProtection", 40f);
            var rows = new List<InventoryItemStat>();
            new EquipmentStatsFormatter(_window).Collect(new ItemInstance(definition, 1), rows);
            Assert.That(rows.Exists(row => row.Label == "Helmet ballistic protection"), Is.False);
            Assert.That(definition.GetComponent<EquipmentStatsComponent>().HelmetFireWoundProtection, Is.EqualTo(40f));
        }

        [Test]
        public void HelmetTooltipUsesItsOwnCondition()
        {
            ItemDefinition definition = CreateDefinition(EquipmentStatsFormatter.HELMET_CATEGORY, true);
            SetValue(definition, "_shockProtection", 47.5f);
            var item = new ItemInstance(definition, 1);
            item.SetState(EquipmentStatsFormatter.CONDITION_STATE, "0.5");
            var rows = new List<InventoryItemStat>();
            var formatter = new EquipmentStatsFormatter(_window);
            formatter.Collect(item, rows);
            Assert.That(Find(rows, "Shock protection").Value, Is.EqualTo("23.75%"));
            rows.Clear();
            formatter.FullCondition = true;
            formatter.Collect(item, rows);
            Assert.That(Find(rows, "Shock protection").Value, Is.EqualTo("47.5%"));
        }

        private InventoryModel BindEquipment()
        {
            var slots = new[] { new SlotDefinition("Outfit", "Outfit", new RectInt(0, 0, 1, 1), SlotMode.Single, SlotRule.Create(new[] { EquipmentStatsFormatter.OUTFIT_CATEGORY })), new SlotDefinition("Helmet", "Helmet", new RectInt(1, 0, 1, 1), SlotMode.Single, SlotRule.Create(new[] { EquipmentStatsFormatter.HELMET_CATEGORY })) };
            var model = new InventoryModel();
            model.AddContainer(new SlottedContainer("Equipment", slots, 2, 1, true));
            _window.ConfigureContainers("Backpack", "Loot", "Equipment", "Player", 35f);
            _window.Bind(model, null);
            return model;
        }

        private ItemDefinition CreateDefinition(string category, bool helmetAvailable)
        {
            ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
            _definitions.Add(definition);
            using var serialized = new SerializedObject(definition);
            serialized.FindProperty("_definitionId").stringValue = "percentage-test-" + _definitions.Count;
            serialized.FindProperty("_category").stringValue = category;
            SerializedProperty components = serialized.FindProperty("_components");
            components.arraySize = 1;
            components.GetArrayElementAtIndex(0).managedReferenceValue = new EquipmentStatsComponent();
            components.GetArrayElementAtIndex(0).FindPropertyRelative("_helmetAvailable").boolValue = helmetAvailable;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private static void SetValue(ItemDefinition definition, string field, float value)
        {
            using var serialized = new SerializedObject(definition);
            serialized.FindProperty("_components").GetArrayElementAtIndex(0).FindPropertyRelative(field).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static InventoryItemStat Find(List<InventoryItemStat> rows, string label)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Label == label)
                {
                    return rows[i];
                }
            }

            Assert.Fail("Missing tooltip row: " + label);
            return default;
        }
    }
}
