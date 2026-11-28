using System.Collections.Generic;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class AnomalyEquipmentProtectionTests
    {
        private readonly List<ItemDefinition> _definitions = new();

        private Scene _preview;
        private GameObject _root;
        private AnomalyBalanceConfig _config;

        [SetUp]
        public void SetUp()
        {
            _preview = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("AnomalyEquipmentProtectionTests") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(_root, _preview);
            _config = ScriptableObject.CreateInstance<AnomalyBalanceConfig>();
            using var serialized = new SerializedObject(_config);
            serialized.FindProperty("_conditionEfficiencyCurve").animationCurveValue = new AnimationCurve(new Keyframe(0f, 0f, 0.7f, 0.7f), new Keyframe(50f, 35f, 0.7f, 1.35f), new Keyframe(70f, 62f, 1.35f, 1.6f), new Keyframe(80f, 78f, 1.6f, 1.6f), new Keyframe(85f, 86f, 1.6f, 1.4f), new Keyframe(90f, 93f, 1.4f, 0.8f), new Keyframe(95f, 97f, 0.8f, 0.6f), new Keyframe(100f, 100f, 0.6f, 0.6f));
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_config);
            for (int i = 0; i < _definitions.Count; i++)
            {
                Object.DestroyImmediate(_definitions[i]);
            }
            _definitions.Clear();
            EditorSceneManager.ClosePreviewScene(_preview);
        }

        [Test]
        public void StoredTriplesOverrideLegacyValuesWithoutRuntimeMutation()
        {
            ItemDefinition definition = CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Shock, new AnomalyProtectionValues(80f, 60f, 40f));
            using (var serialized = new SerializedObject(definition))
            {
                SerializedProperty stats = serialized.FindProperty("_components").GetArrayElementAtIndex(0);
                stats.FindPropertyRelative("_shockProtection").floatValue = 13f;
                stats.FindPropertyRelative("_burnProtection").floatValue = 17f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var item = new ItemInstance(definition, 1);
            EquipmentStatsComponent component = definition.GetComponent<EquipmentStatsComponent>();
            Assert.That(AnomalyEquipmentProtection.GetItemProtection(item, AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.8f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetItemProtection(item, AnomalyDamageType.Shock, AnomalyStrength.Average, _config), Is.EqualTo(0.6f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetItemProtection(item, AnomalyDamageType.Shock, AnomalyStrength.Strong, _config), Is.EqualTo(0.4f).Within(0.000001f));
            Assert.That(component.GetAnomalyProtection(AnomalyDamageType.Burn, AnomalyStrength.Strong), Is.EqualTo(17f));
            Assert.That(component.ShockProtection, Is.EqualTo(13f));
            Assert.That(item.State.Count, Is.Zero);
        }

        [TestCase("1", 80f)]
        [TestCase("0.95", 77.6f)]
        [TestCase("0.9", 74.4f)]
        [TestCase("0.85", 68.8f)]
        [TestCase("0.8", 62.4f)]
        [TestCase("0.7", 49.6f)]
        [TestCase("0.5", 28f)]
        [TestCase("0", 0f)]
        public void CurrentProtectionUsesTheSharedCurveForAllStrengths(string condition, float weakProtection)
        {
            ItemDefinition definition = CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Shock, new AnomalyProtectionValues(80f, 60f, 40f));
            var item = new ItemInstance(definition, 1);
            item.SetState(AnomalyEquipmentProtection.CONDITION_STATE, condition);
            AnomalyProtectionValues current = AnomalyEquipmentProtection.GetItemValues(item, AnomalyDamageType.Shock, _config);
            Assert.That(current.Get(AnomalyStrength.Weak), Is.EqualTo(weakProtection).Within(0.0001f));
            Assert.That(current.Get(AnomalyStrength.Average), Is.EqualTo(weakProtection * 0.75f).Within(0.0001f));
            Assert.That(current.Get(AnomalyStrength.Strong), Is.EqualTo(weakProtection * 0.5f).Within(0.0001f));
            Assert.That(AnomalyEquipmentProtection.GetItemValues(item, AnomalyDamageType.Shock, null).Get(AnomalyStrength.Weak), Is.EqualTo(weakProtection).Within(0.0001f));
        }

        [Test]
        public void ProtectionUsesTheExplicitConfiguredCurveWithoutAnExtraPenalty()
        {
            var item = new ItemInstance(CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Shock, new AnomalyProtectionValues(80f, 60f, 40f)), 1);
            item.SetState(AnomalyEquipmentProtection.CONDITION_STATE, "0.5");
            using (var serialized = new SerializedObject(_config))
            {
                serialized.FindProperty("_conditionEfficiencyCurve").animationCurveValue = AnimationCurve.Linear(0f, 0f, 100f, 100f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(AnomalyEquipmentProtection.GetItemProtection(item, AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.4f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetItemProtection(item, AnomalyDamageType.Shock, AnomalyStrength.Weak, null), Is.EqualTo(0.28f).Within(0.000001f));
            Assert.That(item.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.5"));
        }

        [Test]
        public void ApplicableOutfitAndHelmetAbsorbSequentially()
        {
            InventoryModel model = CreateModel();
            Add(model, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Radiation, new AnomalyProtectionValues(70f, 60f, 50f)), Vector2Int.zero);
            Add(model, CreateDefinition(AnomalyEquipmentProtection.HELMET_CATEGORY, AnomalyDamageType.Radiation, new AnomalyProtectionValues(20f, 20f, 20f)), Vector2Int.right);
            AnomalyProtectionValues current = AnomalyEquipmentProtection.GetCombinedValues(model.GetContainer("Equipment"), AnomalyDamageType.Radiation, _config);
            Assert.That(current.Get(AnomalyStrength.Weak), Is.EqualTo(76f).Within(0.0001f));
            Assert.That(current.Get(AnomalyStrength.Average), Is.EqualTo(68f).Within(0.0001f));
            Assert.That(current.Get(AnomalyStrength.Strong), Is.EqualTo(60f).Within(0.0001f));
        }

        [TestCase(AnomalyDamageType.Burn)]
        [TestCase(AnomalyDamageType.Shock)]
        [TestCase(AnomalyDamageType.Strike)]
        [TestCase(AnomalyDamageType.ChemicalBurn)]
        [TestCase(AnomalyDamageType.Radiation)]
        [TestCase(AnomalyDamageType.Telepatic)]
        [TestCase(AnomalyDamageType.Wound)]
        [TestCase(AnomalyDamageType.Explosion)]
        public void HelmetStacksWithOutfitAndUnrelatedItemsDoNotProtect(AnomalyDamageType type)
        {
            InventoryModel model = CreateModel();
            ItemInstance outfit = Add(model, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, type, new AnomalyProtectionValues(40f, 30f, 20f)), Vector2Int.zero);
            Add(model, CreateDefinition(AnomalyEquipmentProtection.HELMET_CATEGORY, type, new AnomalyProtectionValues(90f, 90f, 90f)), Vector2Int.right);
            Add(model, CreateDefinition("Backpack", type, new AnomalyProtectionValues(100f, 100f, 100f)), new Vector2Int(2, 0));
            InventoryContainer equipment = model.GetContainer("Equipment");
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, type, AnomalyStrength.Weak, _config), Is.EqualTo(0.94f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, type, AnomalyStrength.Weak, null), Is.EqualTo(0.94f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, type, AnomalyStrength.Weak, _config, removed: outfit), Is.EqualTo(0.9f).Within(0.000001f));
        }

        [Test]
        public void HelmetApplicabilityUsesTheExplicitConfigWithoutChangingOtherBodyProtection()
        {
            using (var serialized = new SerializedObject(_config))
            {
                SerializedProperty types = serialized.FindProperty("_helmetDamageTypes");
                types.arraySize = 2;
                types.GetArrayElementAtIndex(0).enumValueIndex = (int)AnomalyDamageType.Radiation;
                types.GetArrayElementAtIndex(1).enumValueIndex = (int)AnomalyDamageType.Telepatic;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            InventoryModel model = CreateModel();
            ItemInstance outfit = Add(model, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Shock, new AnomalyProtectionValues(40f, 30f, 20f)), Vector2Int.zero);
            ItemInstance helmet = Add(model, CreateDefinition(AnomalyEquipmentProtection.HELMET_CATEGORY, AnomalyDamageType.Shock, new AnomalyProtectionValues(80f, 60f, 40f)), Vector2Int.right);
            using (var serialized = new SerializedObject(outfit.Definition))
            {
                serialized.FindProperty("_components").GetArrayElementAtIndex(0).FindPropertyRelative("_strikeProtection").floatValue = 30f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            using (var serialized = new SerializedObject(helmet.Definition))
            {
                serialized.FindProperty("_components").GetArrayElementAtIndex(0).FindPropertyRelative("_strikeProtection").floatValue = 90f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            InventoryContainer equipment = model.GetContainer("Equipment");
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Shock, AnomalyStrength.Weak, _config, removed: outfit), Is.Zero);
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.4f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Strike, AnomalyStrength.Weak, _config), Is.EqualTo(0.3f).Within(0.000001f));
            using (var serialized = new SerializedObject(_config))
            {
                SerializedProperty types = serialized.FindProperty("_helmetDamageTypes");
                types.arraySize = 1;
                types.GetArrayElementAtIndex(0).enumValueIndex = (int)AnomalyDamageType.Shock;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Shock, AnomalyStrength.Weak, _config, removed: outfit), Is.EqualTo(0.8f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.88f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Strike, AnomalyStrength.Weak, _config), Is.EqualTo(0.3f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Strike, AnomalyStrength.Weak, null), Is.EqualTo(0.93f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetItemProtection(outfit, AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.4f).Within(0.000001f));
            Assert.That(outfit.State.Count, Is.Zero);
            Assert.That(helmet.State.Count, Is.Zero);
        }

        [TestCase(AnomalyDamageType.Radiation)]
        [TestCase(AnomalyDamageType.Shock)]
        public void BuiltInHelmetIsNotCountedAgainAndReplacementRechecksApplicability(AnomalyDamageType type)
        {
            InventoryModel model = CreateModel();
            ItemDefinition outfit = CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, type, new AnomalyProtectionValues(70f, 70f, 70f), false);
            Add(model, outfit, Vector2Int.zero);
            Add(model, CreateDefinition(AnomalyEquipmentProtection.HELMET_CATEGORY, type, new AnomalyProtectionValues(20f, 20f, 20f)), Vector2Int.right);
            InventoryContainer equipment = model.GetContainer("Equipment");
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, type, AnomalyStrength.Average, _config), Is.EqualTo(0.7f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, type, AnomalyStrength.Average, null), Is.EqualTo(0.7f).Within(0.000001f));
            var replacement = new ItemInstance(CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, type, new AnomalyProtectionValues(80f, 80f, 80f)), 1);
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, type, AnomalyStrength.Average, _config, replacement), Is.EqualTo(0.84f).Within(0.000001f));
            Assert.That(equipment.Items.Count, Is.EqualTo(2));
        }

        [Test]
        public void RemovalAndRepairPreviewPreserveStateAndIndependentHelmet()
        {
            InventoryModel model = CreateModel();
            ItemInstance outfit = Add(model, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Radiation, new AnomalyProtectionValues(70f, 70f, 70f)), Vector2Int.zero);
            outfit.SetState(AnomalyEquipmentProtection.CONDITION_STATE, "0.5");
            ItemInstance helmet = Add(model, CreateDefinition(AnomalyEquipmentProtection.HELMET_CATEGORY, AnomalyDamageType.Radiation, new AnomalyProtectionValues(20f, 20f, 20f)), Vector2Int.right);
            InventoryContainer equipment = model.GetContainer("Equipment");
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Radiation, AnomalyStrength.Average, _config), Is.EqualTo(0.396f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Radiation, AnomalyStrength.Average, _config, removed: outfit), Is.EqualTo(0.2f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Radiation, AnomalyStrength.Average, _config, fullCondition: true), Is.EqualTo(0.76f).Within(0.000001f));
            Assert.That(outfit.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.5"));
            Assert.That(helmet.State.Count, Is.Zero);
            Assert.That(equipment.Items.Count, Is.EqualTo(2));
        }

        [Test]
        public void RepairPreviewChangesOnlyTheSelectedItemsHypotheticalCondition()
        {
            InventoryModel model = CreateModel();
            ItemInstance outfit = Add(model, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Radiation, new AnomalyProtectionValues(70f, 70f, 70f)), Vector2Int.zero);
            ItemInstance helmet = Add(model, CreateDefinition(AnomalyEquipmentProtection.HELMET_CATEGORY, AnomalyDamageType.Radiation, new AnomalyProtectionValues(20f, 20f, 20f)), Vector2Int.right);
            outfit.SetState(AnomalyEquipmentProtection.CONDITION_STATE, "0.5");
            helmet.SetState(AnomalyEquipmentProtection.CONDITION_STATE, "0.5");
            InventoryContainer equipment = model.GetContainer("Equipment");
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Radiation, AnomalyStrength.Average, _config, repaired: outfit), Is.EqualTo(0.721f).Within(0.000001f));
            Assert.That(AnomalyEquipmentProtection.GetCombinedProtection(equipment, AnomalyDamageType.Radiation, AnomalyStrength.Average, _config, repaired: outfit, repairedCondition: 0.8f), Is.EqualTo(0.57778f).Within(0.000001f));
            Assert.That(outfit.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.5"));
            Assert.That(helmet.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.5"));
        }

        [Test]
        public void AnomalyDurabilityDoesNotModifyProtectionOrCondition()
        {
            ItemDefinition definition = CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Shock, new AnomalyProtectionValues(80f, 60f, 40f));
            var item = new ItemInstance(definition, 1);
            item.SetState(AnomalyEquipmentProtection.CONDITION_STATE, "0.5");
            using (var serialized = new SerializedObject(definition))
            {
                serialized.FindProperty("_components").GetArrayElementAtIndex(0).FindPropertyRelative("_anomalyDurability").floatValue = 100f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(AnomalyEquipmentProtection.GetItemProtection(item, AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.28f).Within(0.000001f));
            Assert.That(item.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.5"));
        }

        [Test]
        public void RuntimeProtectionReadsEquipmentConditionWithoutAWindow()
        {
            PlayerInventoryRuntime runtime = _root.AddComponent<PlayerInventoryRuntime>();
            InventoryModel model = runtime.Model;
            model.AddContainer(new GridContainer("Equipment", 3, 1));
            ItemInstance outfit = Add(model, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Shock, new AnomalyProtectionValues(80f, 60f, 40f)), Vector2Int.zero);
            Assert.That(runtime.GetAnomalyProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.8f).Within(0.000001f));
            outfit.SetState(AnomalyEquipmentProtection.CONDITION_STATE, "0.5");
            Assert.That(runtime.GetAnomalyProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.28f).Within(0.000001f));
        }

        [TestCase(80f, 65f, 50f, "80%", "(+8)", InventoryStatTone.Better)]
        [TestCase(80f, 55f, 40f, "80%", "(+8)", InventoryStatTone.Better)]
        [TestCase(60f, 65f, 60f, "60%", "(-8)", InventoryStatTone.Worse)]
        [TestCase(70f, 65f, 60f, "70%", "", InventoryStatTone.Normal)]
        public void TooltipComparisonUsesOnlyWeakTotalProtectionGain(float weak, float average, float strong, string expectedValue, string expectedComparison, InventoryStatTone expectedTone)
        {
            InventoryModel model = CreateModel();
            Add(model, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Radiation, new AnomalyProtectionValues(70f, 60f, 50f)), Vector2Int.zero);
            Add(model, CreateDefinition(AnomalyEquipmentProtection.HELMET_CATEGORY, AnomalyDamageType.Radiation, new AnomalyProtectionValues(20f, 20f, 20f)), Vector2Int.right);
            InventoryWindow window = _root.AddComponent<InventoryWindow>();
            window.ConfigureContainers("Backpack", "Loot", "Equipment", "Player", 35f);
            window.Bind(model, null);
            var candidate = new ItemInstance(CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Radiation, new AnomalyProtectionValues(weak, average, strong)), 1);
            var rows = new List<InventoryItemStat>();
            new EquipmentStatsFormatter(window, _config).Collect(candidate, rows);
            InventoryItemStat row = rows.Find(value => value.Label == "Radiation protection");
            Assert.That(row.Value, Is.EqualTo(expectedValue));
            Assert.That(row.Comparison, Is.EqualTo(expectedComparison));
            Assert.That(row.Tone, Is.EqualTo(expectedTone));
            Assert.That(candidate.State.Count, Is.Zero);
        }

        [TestCase("NaN", 1f)]
        [TestCase("Infinity", 1f)]
        [TestCase("invalid", 1f)]
        [TestCase("-1", 0f)]
        [TestCase("2", 1f)]
        public void MalformedConditionCannotPoisonProtection(string condition, float expected)
        {
            var item = new ItemInstance(CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, AnomalyDamageType.Shock, new AnomalyProtectionValues(80f, 60f, 40f)), 1);
            item.SetState(AnomalyEquipmentProtection.CONDITION_STATE, condition);
            Assert.That(AnomalyEquipmentProtection.GetCondition(item), Is.EqualTo(expected));
            Assert.That(float.IsFinite(AnomalyEquipmentProtection.GetItemProtection(item, AnomalyDamageType.Shock, AnomalyStrength.Weak, _config)), Is.True);
        }

        private static InventoryModel CreateModel()
        {
            var model = new InventoryModel();
            model.AddContainer(new GridContainer("Equipment", 3, 1));
            return model;
        }

        private static ItemInstance Add(InventoryModel model, ItemDefinition definition, Vector2Int position)
        {
            ItemInstance item = model.CreateItem(definition);
            Assert.That(model.TryAdd(item, "Equipment", position, false).Success, Is.True);
            return item;
        }

        private ItemDefinition CreateDefinition(string category, AnomalyDamageType type, AnomalyProtectionValues values, bool helmetAvailable = true)
        {
            ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
            _definitions.Add(definition);
            using var serialized = new SerializedObject(definition);
            serialized.FindProperty("_definitionId").stringValue = "anomaly-protection-test-" + _definitions.Count;
            serialized.FindProperty("_category").stringValue = category;
            SerializedProperty components = serialized.FindProperty("_components");
            components.arraySize = 1;
            components.GetArrayElementAtIndex(0).managedReferenceValue = new EquipmentStatsComponent();
            SerializedProperty stats = components.GetArrayElementAtIndex(0);
            stats.FindPropertyRelative("_helmetAvailable").boolValue = helmetAvailable;
            SerializedProperty entries = stats.FindPropertyRelative("_anomalyProtections");
            entries.arraySize = 1;
            SerializedProperty entry = entries.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("_type").enumValueIndex = (int)type;
            SerializedProperty triple = entry.FindPropertyRelative("_values");
            triple.FindPropertyRelative("_weak").floatValue = values.Get(AnomalyStrength.Weak);
            triple.FindPropertyRelative("_average").floatValue = values.Get(AnomalyStrength.Average);
            triple.FindPropertyRelative("_strong").floatValue = values.Get(AnomalyStrength.Strong);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }
    }
}
