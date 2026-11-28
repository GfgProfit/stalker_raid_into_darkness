using RaidIntoDarkness.Inventory.Infrastructure;
using RaidIntoDarkness.Inventory.Domain;
using System.Collections.Generic;
using System.Reflection;
using RaidIntoDarkness.Inventory;
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
    public sealed class AnomalyEquipmentWearInventoryTests
    {
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private readonly List<ItemDefinition> _definitions = new List<ItemDefinition>();

        private Scene _scene;
        private GameObject _root;
        private AnomalyBalanceConfig _config;
        private ItemDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _root = new GameObject(nameof(AnomalyEquipmentWearInventoryTests));
            _root.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _config = CreateAsset<AnomalyBalanceConfig>();
            _database = CreateAsset<ItemDatabase>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            foreach (ScriptableObject asset in _assets)
            {
                Object.DestroyImmediate(asset);
            }
            _assets.Clear();
            _definitions.Clear();
            EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void CaptureCopiesIdentityConditionAndBaseDurabilityWithoutInventoryItems()
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemDefinition outfitDefinition = CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 63f);
            ItemInstance outfit = Add(runtime, outfitDefinition, "Equipment", 0, "0.5");
            Add(runtime, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 72f), "Bag", 0, "0.7");
            Add(runtime, CreateDefinition(AnomalyEquipmentProtection.BACKPACK_CATEGORY, 100f), "Equipment", 1, "0.8");
            Add(runtime, CreateDefinition("Weapon", 100f), "Equipment", 2, "0.9");
            var targets = new List<AnomalyEquipmentWearTarget>();
            runtime.CaptureEquipmentWear(AnomalyDamageType.Shock, _config, targets);
            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].Source, Is.SameAs(runtime));
            Assert.That(targets[0].ItemId, Is.EqualTo(outfit.InstanceId));
            Assert.That(targets[0].Condition, Is.EqualTo(0.5f));
            Assert.That(targets[0].AnomalyDurability, Is.EqualTo(63f));
            runtime.ApplyEquipmentWear(targets[0], 0.2f);
            SetStat(outfitDefinition, "_anomalyDurability", 100f);
            Assert.That(targets[0].Condition, Is.EqualTo(0.5f));
            Assert.That(targets[0].AnomalyDurability, Is.EqualTo(63f));
        }

        [TestCase(AnomalyDamageType.Shock, true, 2)]
        [TestCase(AnomalyDamageType.Radiation, true, 2)]
        [TestCase(AnomalyDamageType.Telepatic, true, 2)]
        [TestCase(AnomalyDamageType.Radiation, false, 1)]
        [TestCase(AnomalyDamageType.None, true, 0)]
        public void CaptureSharesProtectionHelmetApplicability(AnomalyDamageType type, bool helmetAvailable, int expectedTargets)
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemInstance outfit = Add(runtime, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 63f, helmetAvailable), "Equipment", 0, "0.6");
            ItemInstance helmet = Add(runtime, CreateDefinition(AnomalyEquipmentProtection.HELMET_CATEGORY, 29f), "Equipment", 1, "0.8");
            var targets = new List<AnomalyEquipmentWearTarget>();
            runtime.CaptureEquipmentWear(type, _config, targets);
            Assert.That(targets.Count, Is.EqualTo(expectedTargets));
            if (expectedTargets > 0)
            {
                Assert.That(targets[0].ItemId, Is.EqualTo(outfit.InstanceId));
            }
            if (expectedTargets == 2)
            {
                Assert.That(targets[1].ItemId, Is.EqualTo(helmet.InstanceId));
                Assert.That(targets[1].Condition, Is.EqualTo(0.8f));
                Assert.That(targets[1].AnomalyDurability, Is.EqualTo(29f));
            }
        }

        [Test]
        public void ExplicitHelmetDamageTypesAndHelmetWithoutOutfitRemainSupported()
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemInstance helmet = Add(runtime, CreateDefinition(AnomalyEquipmentProtection.HELMET_CATEGORY, 29f), "Equipment", 0);
            SetField(_config, "_helmetDamageTypes", new[] { AnomalyDamageType.Shock });
            var targets = new List<AnomalyEquipmentWearTarget>();
            runtime.CaptureEquipmentWear(AnomalyDamageType.Shock, _config, targets);
            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].ItemId, Is.EqualTo(helmet.InstanceId));
            targets.Clear();
            runtime.CaptureEquipmentWear(AnomalyDamageType.Radiation, _config, targets);
            Assert.That(targets, Is.Empty);
        }

        [Test]
        public void MissingStatsAndMissingEquipmentDoNotProduceTargets()
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemDefinition definition = CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 63f);
            using (var serialized = new SerializedObject(definition))
            {
                serialized.FindProperty("_components").arraySize = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Add(runtime, definition, "Equipment", 0);
            var targets = new List<AnomalyEquipmentWearTarget>();
            runtime.CaptureEquipmentWear(AnomalyDamageType.Shock, _config, targets);
            Assert.That(targets, Is.Empty);
            SetField(runtime, "_equipmentContainerId", "Missing");
            runtime.CaptureEquipmentWear(AnomalyDamageType.Shock, _config, targets);
            Assert.That(targets, Is.Empty);
        }

        [Test]
        public void ApplyNotifiesModelOnlyForActualWearAndKeepsDefinitionAndBagState()
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemDefinition definition = CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 63f);
            ItemInstance outfit = Add(runtime, definition, "Equipment", 0, "0.8");
            ItemInstance spare = Add(runtime, definition, "Bag", 0, "0.7");
            var changes = new List<InventoryChange>();
            runtime.Model.Changed += changes.Add;
            AnomalyEquipmentWearTarget target = CaptureOutfit(runtime);
            runtime.ApplyEquipmentWear(target, 0.6f);
            runtime.ApplyEquipmentWear(target, 0.6f);
            runtime.ApplyEquipmentWear(target, 0.8f);
            Assert.That(AnomalyEquipmentProtection.GetCondition(outfit), Is.EqualTo(0.6f));
            Assert.That(spare.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.7"));
            Assert.That(definition.GetComponent<EquipmentStatsComponent>().AnomalyDurability, Is.EqualTo(63f));
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Kind, Is.EqualTo(InventoryChangeKind.ItemStateChanged));
            Assert.That(changes[0].Item, Is.SameAs(outfit));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-0.01f)]
        [TestCase(1.01f)]
        public void ApplyRejectsNonFiniteAndOutOfRangeCondition(float remainingCondition)
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemInstance outfit = Add(runtime, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 63f), "Equipment", 0, "0.8");
            var changes = new List<InventoryChange>();
            runtime.Model.Changed += changes.Add;
            runtime.ApplyEquipmentWear(CaptureOutfit(runtime), remainingCondition);
            Assert.That(outfit.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.8"));
            Assert.That(changes, Is.Empty);
        }

        [Test]
        public void MaximumAndZeroConditionAvoidRedundantStateChanges()
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemInstance outfit = Add(runtime, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 0f), "Equipment", 0);
            var changes = new List<InventoryChange>();
            runtime.Model.Changed += changes.Add;
            AnomalyEquipmentWearTarget target = CaptureOutfit(runtime);
            runtime.ApplyEquipmentWear(target, 1f);
            Assert.That(outfit.State.Count, Is.Zero);
            Assert.That(changes, Is.Empty);
            runtime.ApplyEquipmentWear(target, 0f);
            runtime.ApplyEquipmentWear(target, 0f);
            Assert.That(outfit.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0"));
            Assert.That(changes.Count, Is.EqualTo(1));
        }

        [Test]
        public void ApplyRejectsAnotherOwnerAndItemsMovedToInventoryAfterCapture()
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            PlayerInventoryRuntime other = CreateInventory();
            ItemDefinition definition = CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 63f);
            ItemInstance outfit = Add(runtime, definition, "Equipment", 0, "0.8");
            ItemInstance otherOutfit = Add(other, definition, "Equipment", 0, "0.9");
            AnomalyEquipmentWearTarget target = CaptureOutfit(runtime);
            other.ApplyEquipmentWear(target, 0.5f);
            runtime.ApplyEquipmentWear(new AnomalyEquipmentWearTarget(other, outfit.InstanceId, 0.8f, 63f), 0.5f);
            runtime.ApplyEquipmentWear(new AnomalyEquipmentWearTarget(runtime, otherOutfit.InstanceId, 0.9f, 63f), 0.5f);
            Assert.That(outfit.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.8"));
            Assert.That(otherOutfit.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.9"));
            Assert.That(runtime.Model.TryMove(outfit.InstanceId, "Bag", Vector2Int.zero, false).Success, Is.True);
            runtime.ApplyEquipmentWear(target, 0.5f);
            Assert.That(outfit.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.8"));
        }

        [Test]
        public void ApplyingWearPreservesUnchangedInventoryAndAuthoredDurability()
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemDefinition definition = CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 63f);
            ItemInstance outfit = Add(runtime, definition, "Equipment", 0, "0.8");
            ItemInstance spare = Add(runtime, definition, "Bag", 0, "0.7");
            runtime.ApplyEquipmentWear(CaptureOutfit(runtime), 0.6123457f);
            Assert.That(AnomalyEquipmentProtection.GetCondition(outfit), Is.EqualTo(0.6123457f));
            Assert.That(spare.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.7"));
            Assert.That(definition.GetComponent<EquipmentStatsComponent>().AnomalyDurability, Is.EqualTo(63f));
        }

        [Test]
        public void ReceiverUsesConditionBeforeHitAndRecalculatesProtectionAfterWear()
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemDefinition definition = CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 0f);
            ItemInstance outfit = Add(runtime, definition, "Equipment", 0);
            ItemInstance spare = Add(runtime, definition, "Bag", 0, "0.7");
            AnomalyDamageReceiver receiver = CreateReceiver(runtime, out StalkerActorCondition condition);
            AnomalyVariantConfig variant = CreateVariant(AnomalyDamageType.Shock, 10f);
            Assert.That(receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), variant, _config), Is.True);
            Assert.That(condition.Health, Is.EqualTo(96f).Within(0.0001f));
            Assert.That(AnomalyEquipmentProtection.GetCondition(outfit), Is.EqualTo(0.9f).Within(0.000001f));
            Assert.That(receiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.744f).Within(0.000001f));
            Assert.That(receiver.ApplyHit(new AnomalyHitEvent(1, 1, 2), variant, _config), Is.True);
            Assert.That(condition.Health, Is.EqualTo(90.88f).Within(0.0001f));
            Assert.That(AnomalyEquipmentProtection.GetCondition(outfit), Is.EqualTo(0.8f).Within(0.000001f));
            Assert.That(spare.GetState(AnomalyEquipmentProtection.CONDITION_STATE), Is.EqualTo("0.7"));
        }

        [Test]
        public void ReceiverDeduplicatesInventoryBindingAndHitWhileDifferentSourcesWearIndependently()
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemInstance outfit = Add(runtime, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 0f), "Equipment", 0);
            AnomalyDamageReceiver receiver = CreateReceiver(runtime, out _);
            runtime.BindDamageReceiver(receiver);
            var changes = new List<InventoryChange>();
            runtime.Model.Changed += changes.Add;
            AnomalyVariantConfig variant = CreateVariant(AnomalyDamageType.Shock, 10f);
            var hit = new AnomalyHitEvent(1, 1, 1);
            Assert.That(receiver.ApplyHit(hit, variant, _config), Is.True);
            Assert.That(receiver.ApplyHit(hit, variant, _config), Is.False);
            Assert.That(AnomalyEquipmentProtection.GetCondition(outfit), Is.EqualTo(0.9f).Within(0.000001f));
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(receiver.ApplyHit(new AnomalyHitEvent(2, 1, 1), variant, _config), Is.True);
            Assert.That(AnomalyEquipmentProtection.GetCondition(outfit), Is.EqualTo(0.8f).Within(0.000001f));
            Assert.That(changes.Count, Is.EqualTo(2));
        }

        [Test]
        public void ReceiverRejectsInactiveDamageAndZeroWearDoesNotNotifyOrChangeEquipment()
        {
            PlayerInventoryRuntime runtime = CreateInventory();
            ItemInstance outfit = Add(runtime, CreateDefinition(AnomalyEquipmentProtection.OUTFIT_CATEGORY, 63f), "Equipment", 0);
            AnomalyDamageReceiver receiver = CreateReceiver(runtime, out _);
            var changes = new List<InventoryChange>();
            runtime.Model.Changed += changes.Add;
            Assert.That(receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), CreateVariant(AnomalyDamageType.None, 10f), _config), Is.False);
            Assert.That(receiver.ApplyHit(new AnomalyHitEvent(2, 1, 1), CreateVariant(AnomalyDamageType.Shock, 0f), _config), Is.True);
            Assert.That(outfit.State.Count, Is.Zero);
            Assert.That(changes, Is.Empty);
        }

        private PlayerInventoryRuntime CreateInventory()
        {
            PlayerInventoryRuntime runtime = CreateInactiveObject("Inventory").AddComponent<PlayerInventoryRuntime>();
            SetField(runtime, "_database", _database);
            runtime.Model.AddContainer(new GridContainer("Equipment", 8, 1));
            runtime.Model.AddContainer(new GridContainer("Bag", 8, 1));
            return runtime;
        }

        private ItemDefinition CreateDefinition(string category, float durability, bool helmetAvailable = true)
        {
            ItemDefinition definition = CreateAsset<ItemDefinition>();
            using (var serialized = new SerializedObject(definition))
            {
                serialized.FindProperty("_definitionId").stringValue = "anomaly-wear-" + _assets.Count;
                serialized.FindProperty("_category").stringValue = category;
                SerializedProperty components = serialized.FindProperty("_components");
                components.arraySize = 1;
                components.GetArrayElementAtIndex(0).managedReferenceValue = new EquipmentStatsComponent();
                SerializedProperty stats = components.GetArrayElementAtIndex(0);
                stats.FindPropertyRelative("_anomalyDurability").floatValue = durability;
                stats.FindPropertyRelative("_helmetAvailable").boolValue = helmetAvailable;
                stats.FindPropertyRelative("_shockProtection").floatValue = 80f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            _definitions.Add(definition);
            _database.SetDefinitions(_definitions);
            return definition;
        }

        private static ItemInstance Add(PlayerInventoryRuntime runtime, ItemDefinition definition, string containerId, int x, string condition = null)
        {
            ItemInstance item = runtime.Model.CreateItem(definition);
            if (condition != null)
            {
                item.SetState(AnomalyEquipmentProtection.CONDITION_STATE, condition);
            }

            Assert.That(runtime.Model.TryAdd(item, containerId, new Vector2Int(x, 0), false).Success, Is.True);
            return item;
        }

        private AnomalyEquipmentWearTarget CaptureOutfit(PlayerInventoryRuntime runtime)
        {
            var targets = new List<AnomalyEquipmentWearTarget>();
            runtime.CaptureEquipmentWear(AnomalyDamageType.Shock, _config, targets);
            Assert.That(targets.Count, Is.EqualTo(1));
            return targets[0];
        }

        private AnomalyDamageReceiver CreateReceiver(PlayerInventoryRuntime runtime, out StalkerActorCondition condition)
        {
            GameObject root = CreateInactiveObject("Player");
            condition = root.AddComponent<StalkerActorCondition>();
            condition.Initialize(CreateAsset<StalkerMovementProfile>());
            AnomalyDamageReceiver receiver = root.AddComponent<AnomalyDamageReceiver>();
            receiver.Initialize(condition, runtime);
            return receiver;
        }

        private AnomalyVariantConfig CreateVariant(AnomalyDamageType type, float wear)
        {
            AnomalyArchetypeConfig archetype = CreateAsset<AnomalyArchetypeConfig>();
            SetField(archetype, "_id", "wear_inventory");
            SetField(archetype, "_damageType", type);
            SetField(archetype, "_baseDamage", 20f);
            SetField(archetype, "_equipmentWearPerHitPercent", wear);
            AnomalyVariantConfig variant = CreateAsset<AnomalyVariantConfig>();
            SetField(variant, "_sourceSection", "wear_inventory_weak");
            SetField(variant, "_archetype", archetype);
            SetField(variant, "_strength", AnomalyStrength.Weak);
            return variant;
        }

        private GameObject CreateInactiveObject(string name)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            root.transform.SetParent(_root.transform, false);
            return root;
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            asset.hideFlags = HideFlags.HideAndDontSave;
            _assets.Add(asset);
            return asset;
        }

        private static void SetStat(ItemDefinition definition, string name, float value)
        {
            using var serialized = new SerializedObject(definition);
            serialized.FindProperty("_components").GetArrayElementAtIndex(0).FindPropertyRelative(name).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
