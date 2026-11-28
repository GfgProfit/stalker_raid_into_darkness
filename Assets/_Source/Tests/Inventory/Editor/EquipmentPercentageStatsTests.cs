using RaidIntoDarkness.Inventory.Domain;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class EquipmentPercentageStatsTests
    {
        private readonly List<ItemDefinition> _definitions = new();

        private Scene _preview;
        private GameObject _root;
        private InventoryWindow _window;
        private AnomalyBalanceConfig _anomalyConfig;

        [SetUp]
        public void SetUp()
        {
            _preview = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("EquipmentPercentageStatsTests") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(_root, _preview);
            _window = _root.AddComponent<InventoryWindow>();
            _anomalyConfig = ScriptableObject.CreateInstance<AnomalyBalanceConfig>();
            using var serialized = new SerializedObject(_anomalyConfig);
            serialized.FindProperty("_conditionEfficiencyCurve").animationCurveValue = new AnimationCurve(new Keyframe(0f, 0f, 0.7f, 0.7f), new Keyframe(50f, 35f, 0.7f, 1.35f), new Keyframe(70f, 62f, 1.35f, 1.6f), new Keyframe(80f, 78f, 1.6f, 1.6f), new Keyframe(85f, 86f, 1.6f, 1.4f), new Keyframe(90f, 93f, 1.4f, 0.8f), new Keyframe(95f, 97f, 0.8f, 0.6f), new Keyframe(100f, 100f, 0.6f, 0.6f));
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
            Object.DestroyImmediate(_anomalyConfig);

            if (_preview.IsValid() && _preview.isLoaded)
            {
                EditorSceneManager.ClosePreviewScene(_preview);
            }
        }

        [TestCase("_fireWoundProtection", "Ballistic protection", true, false)]
        [TestCase("_helmetFireWoundProtection", "Helmet ballistic protection", true, false)]
        [TestCase("_woundProtection", "Rupture protection", true, true)]
        [TestCase("_strikeProtection", "Impact protection", true, true)]
        [TestCase("_explosionProtection", "Explosion protection", true, true)]
        [TestCase("_radiationProtection", "Radiation protection", true, true)]
        [TestCase("_telepaticProtection", "Psy protection", true, true)]
        [TestCase("_burnProtection", "Burn protection", true, true)]
        [TestCase("_shockProtection", "Shock protection", true, true)]
        [TestCase("_chemicalBurnProtection", "Chemical protection", true, true)]
        [TestCase("_physicalDurability", "Physical durability", false, false)]
        [TestCase("_anomalyDurability", "Anomaly durability", false, false)]
        public void StoredPercentUsesSharedAnomalyCurveAndPreservesOtherStats(string field, string label, bool affectedByCondition, bool anomalyProtection)
        {
            ItemDefinition definition = CreateDefinition(EquipmentStatsFormatter.OUTFIT_CATEGORY, false);
            SetValue(definition, field, 23.75f);
            var item = new ItemInstance(definition, 1);
            item.SetState(EquipmentStatsFormatter.CONDITION_STATE, anomalyProtection ? "0.5" : "0.13");
            var formatter = new EquipmentStatsFormatter(_window, _anomalyConfig);
            var rows = new List<InventoryItemStat>();
            formatter.Collect(item, rows);
            Assert.That(Find(rows, label).Value, Is.EqualTo(anomalyProtection ? "8.31%" : affectedByCondition ? "3.09%" : "23.75%"));
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
        public void StatusBarsUseSequentialAnomalyProtectionAndPreserveBallisticValues()
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
            var presenter = new EquipmentStatusPresenter(_window, panel, _anomalyConfig);
            presenter.Refresh(null, false);
            Assert.That(panel.GetValue(EquipmentStatusPresenter.STATUS_RADIATION), Is.EqualTo(0.048412f).Within(0.000001f));
            Assert.That(panel.GetValue(EquipmentStatusPresenter.STATUS_SHOCK), Is.EqualTo(0.062608f).Within(0.000001f));
            Assert.That(panel.GetValue(EquipmentStatusPresenter.STATUS_FIRE_WOUND), Is.EqualTo(0.055f).Within(0.000001f));
            presenter.Refresh(null, true);
            Assert.That(panel.GetValue(EquipmentStatusPresenter.STATUS_RADIATION), Is.EqualTo(0.44f).Within(0.000001f));
        }

        [Test]
        public void StatusTextUsesMeanProtectionAndRestoresCurrentValueAfterPreview()
        {
            InventoryModel model = BindEquipment();
            model.AddContainer(new GridContainer("Backpack", 2, 1));
            ItemDefinition definition = CreateDefinition(EquipmentStatsFormatter.OUTFIT_CATEGORY, true);
            using (var serialized = new SerializedObject(definition))
            {
                SerializedProperty protections = serialized.FindProperty("_components").GetArrayElementAtIndex(0).FindPropertyRelative("_anomalyProtections");
                protections.arraySize = 1;
                protections.GetArrayElementAtIndex(0).boxedValue = new AnomalyEquipmentProtectionEntry(AnomalyDamageType.Shock, new AnomalyProtectionValues(90f, 60f, 12f));
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            ItemInstance outfit = model.CreateItem(definition);
            Assert.That(model.TryAdd(outfit, "Equipment", Vector2Int.zero, false).Success, Is.True);
            var textObject = new GameObject("Stat Text", typeof(RectTransform));
            textObject.transform.SetParent(_root.transform, false);
            TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
            Color originalColor = new(0.7f, 0.6f, 0.5f, 1f);
            text.color = originalColor;
            InventoryStatusPanel panel = _root.AddComponent<InventoryStatusPanel>();
            panel.Configure(new[] { new InventoryStatusBar(EquipmentStatusPresenter.STATUS_SHOCK, null, null, null, text) });
            var presenter = new EquipmentStatusPresenter(_window, panel, _anomalyConfig);
            presenter.Refresh(null, false);
            Assert.That(text.text, Is.EqualTo("54 / 100 %"));
            Assert.That(model.TrySetItemStateInPlace(outfit, EquipmentStatsFormatter.CONDITION_STATE, "0.5"), Is.True);
            presenter.Refresh(null, false);
            Assert.That(text.text, Is.EqualTo("19 / 100 %"));
            Assert.That(panel.GetValue(EquipmentStatusPresenter.STATUS_SHOCK), Is.EqualTo(0.189f).Within(0.000001f));
            ItemDefinition candidateDefinition = CreateDefinition(EquipmentStatsFormatter.OUTFIT_CATEGORY, true);
            SetValue(candidateDefinition, "_shockProtection", 24f);
            ItemInstance candidate = model.CreateItem(candidateDefinition);
            Assert.That(model.TryAdd(candidate, "Backpack", Vector2Int.zero, false).Success, Is.True);
            presenter.Refresh(candidate, false);
            Assert.That(text.text, Is.EqualTo("<color=#00FF00FF>24</color> / 100 %"));
            Assert.That(text.color, Is.EqualTo(originalColor));
            presenter.Refresh(null, false);
            Assert.That(text.text, Is.EqualTo("19 / 100 %"));
            Assert.That(text.color, Is.EqualTo(originalColor));
            presenter.Refresh(null, true);
            Assert.That(text.text, Is.EqualTo("54 / 100 %"));
            Assert.That(outfit.State[EquipmentStatsFormatter.CONDITION_STATE], Is.EqualTo("0.5"));
        }

        [TestCase(0.543f, 1f, "54 / 100 %")]
        [TestCase(0.375f, 0.8f, "38 / 80 %")]
        [TestCase(-1f, 2f, "0 / 100 %")]
        public void StatusTextDisplaysRoundedPercentagesAndCurrentMaximum(float value, float maximum, string expected)
        {
            var textObject = new GameObject("Stat Text", typeof(RectTransform));
            textObject.transform.SetParent(_root.transform, false);
            TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
            var bar = new InventoryStatusBar("power", null, null, null, text);
            bar.Apply(value, maximum);
            Assert.That(text.text, Is.EqualTo(expected));
        }

        [Test]
        public void StatusHoverForwardsPointerAndIgnoresExitFromPreviousRow()
        {
            InventoryStatusPanel panel = _root.AddComponent<InventoryStatusPanel>();
            InventoryStatusHoverTarget target = _root.AddComponent<InventoryStatusHoverTarget>();
            target.Configure(panel, EquipmentStatusPresenter.STATUS_SHOCK);
            string hovered = null;
            Vector2 position = Vector2.zero;
            int exits = 0;
            panel.PointerEntered += (id, point, eventCamera) => hovered = id;
            panel.PointerMoved += (point, eventCamera) => position = point;
            panel.PointerExited += () => exits++;
            var pointer = new PointerEventData(null) { position = new Vector2(123f, 456f) };
            target.OnPointerEnter(pointer);
            Assert.That(hovered, Is.EqualTo(EquipmentStatusPresenter.STATUS_SHOCK));
            target.OnPointerMove(pointer);
            Assert.That(position, Is.EqualTo(pointer.position));
            panel.HandlePointerEnter(EquipmentStatusPresenter.STATUS_FIRE, Vector2.zero, null);
            target.OnPointerExit(pointer);
            Assert.That(exits, Is.Zero);
            panel.HandlePointerExit(EquipmentStatusPresenter.STATUS_FIRE);
            Assert.That(exits, Is.EqualTo(1));
            pointer.position = Vector2.one;
            target.OnPointerMove(pointer);
            Assert.That(position, Is.EqualTo(new Vector2(123f, 456f)));
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
            SetValue(definition, "_shockProtection", 47f);
            var item = new ItemInstance(definition, 1);
            item.SetState(EquipmentStatsFormatter.CONDITION_STATE, "0.5");
            var rows = new List<InventoryItemStat>();
            var formatter = new EquipmentStatsFormatter(_window, _anomalyConfig);
            formatter.Collect(item, rows);
            Assert.That(Find(rows, "Shock protection").Value, Is.EqualTo("16.45%"));
            rows.Clear();
            formatter.FullCondition = true;
            formatter.Collect(item, rows);
            Assert.That(Find(rows, "Shock protection").Value, Is.EqualTo("47%"));
        }

        [TestCase("0.999", "99.9%", "#33FF66")]
        [TestCase("1", "100%", "#33FF66")]
        [TestCase("0.91", "91%", "#33FF66")]
        [TestCase("0.599", "59.9%", "#CC0033")]
        [TestCase("0.6", "60%", "#FFFF00")]
        [TestCase("0.849", "84.9%", "#FFFF00")]
        [TestCase("0.85", "85%", "#33FF66")]
        [TestCase("0", "0%", "#CC0033")]
        public void ItemTooltipsUseOneOptionalConditionDecimalWithoutRoundingUpToAnInteger(string condition, string expectedPercent, string expectedColor)
        {
            foreach (string category in new[] { EquipmentStatsFormatter.OUTFIT_CATEGORY, EquipmentStatsFormatter.HELMET_CATEGORY, EquipmentStatsFormatter.BACKPACK_CATEGORY })
            {
                ItemDefinition definition = CreateDefinition(category, true);
                var item = new ItemInstance(definition, 1);
                item.SetState(EquipmentStatsFormatter.CONDITION_STATE, condition);
                var formatter = new EquipmentStatsFormatter(_window, _anomalyConfig);
                string expected = "Condition: <color=" + expectedColor + ">" + expectedPercent;
                Assert.That(formatter.Describe(item), Does.Contain(expected), category);
                formatter.FullCondition = true;
                Assert.That(formatter.Describe(item), Does.Contain(expected), category);
                Assert.That(item.GetState(EquipmentStatsFormatter.CONDITION_STATE), Is.EqualTo(condition), category);
            }
        }

        private InventoryModel BindEquipment()
        {
            var slots = new[] { new SlotDefinition(SlotId.Outfit, "Outfit", new RectInt(0, 0, 1, 1), SlotMode.Single, SlotRule.Create(new[] { EquipmentStatsFormatter.OUTFIT_CATEGORY })), new SlotDefinition(SlotId.Head, "Helmet", new RectInt(1, 0, 1, 1), SlotMode.Single, SlotRule.Create(new[] { EquipmentStatsFormatter.HELMET_CATEGORY })) };
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
