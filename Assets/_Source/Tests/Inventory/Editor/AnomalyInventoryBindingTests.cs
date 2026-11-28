using RaidIntoDarkness.Inventory.UI;
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
    public sealed class AnomalyInventoryBindingTests
    {
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        private Scene _scene;
        private GameObject _root;
        private AnomalyBalanceConfig _config;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _root = new GameObject(nameof(AnomalyInventoryBindingTests));
            _root.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _config = CreateAsset<AnomalyBalanceConfig>();
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
            EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void RuntimeBindsExternalInventoryBeforeAnyWindowExistsAndControllerLifetimeDoesNotOwnProtection()
        {
            StalkerPlayerController player = CreatePlayer(out AnomalyDamageReceiver receiver, out StalkerActorCondition condition);
            Assert.That(receiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.Zero);
            PlayerInventoryRuntime runtime = CreateInventory(80f, "0.5", player);
            InventoryWindowController controller = CreateController();
            controller.Configure(null, runtime, player, null, null);
            Assert.That(controller.Window, Is.Null);
            Assert.That(controller.gameObject.activeInHierarchy, Is.False);
            Assert.That(runtime.gameObject.activeInHierarchy, Is.False);
            Assert.That(receiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.28f).Within(0.000001f));
            Object.DestroyImmediate(controller.gameObject);
            Assert.That(receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), CreateVariant(AnomalyDamageType.Shock), _config), Is.True);
            Assert.That(condition.Health, Is.EqualTo(85.6f).Within(0.0001f));
        }

        [Test]
        public void RuntimeStartBindsSerializedPlayerEvenWhenTheWindowIsMissing()
        {
            StalkerPlayerController player = CreatePlayer(out AnomalyDamageReceiver receiver, out _);
            PlayerInventoryRuntime runtime = CreateInventory(60f, player: player);
            InventoryWindowController controller = CreateController();
            controller.Configure(null, runtime, player, null, null);
            Assert.That(controller.Window, Is.Null);
            Assert.That(receiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.6f).Within(0.000001f));
        }

        [Test]
        public void ReplacingPlayerOrInventoryUnregistersTheOldOwnerWithoutDuplicateProtection()
        {
            CreatePlayer(out AnomalyDamageReceiver firstReceiver, out _);
            CreatePlayer(out AnomalyDamageReceiver secondReceiver, out _);
            PlayerInventoryRuntime runtime = CreateInventory(80f);
            PlayerInventoryRuntime replacement = CreateInventory(40f);
            runtime.BindDamageReceiver(firstReceiver);
            Assert.That(firstReceiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.8f).Within(0.000001f));
            runtime.BindDamageReceiver(secondReceiver);
            runtime.BindDamageReceiver(secondReceiver);
            Assert.That(firstReceiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.Zero);
            Assert.That(secondReceiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.8f).Within(0.000001f));
            runtime.BindDamageReceiver(null);
            replacement.BindDamageReceiver(secondReceiver);
            Assert.That(secondReceiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.4f).Within(0.000001f));
            replacement.BindDamageReceiver(null);
            Assert.That(secondReceiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.Zero);
        }

        [Test]
        public void RuntimeCanReplaceADestroyedReceiverAndUnregistersItsManagedReferenceOnDestroy()
        {
            CreatePlayer(out AnomalyDamageReceiver firstReceiver, out _);
            CreatePlayer(out AnomalyDamageReceiver secondReceiver, out _);
            PlayerInventoryRuntime runtime = CreateInventory(80f);
            runtime.BindDamageReceiver(firstReceiver);
            Object.DestroyImmediate(firstReceiver);
            runtime.BindDamageReceiver(secondReceiver);
            Assert.That(secondReceiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.8f).Within(0.000001f));
            runtime.gameObject.SetActive(true);
            Object.DestroyImmediate(runtime);
            Assert.That(secondReceiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.Zero);
            var registered = (List<MonoBehaviour>)GetField(secondReceiver, "_registeredProtectionSources");
            Assert.That(registered, Is.Empty);
        }

        [Test]
        public void ReceiverReinitializationPreservesRegisteredInventoryDoseAndEventHistory()
        {
            CreatePlayer(out AnomalyDamageReceiver receiver, out StalkerActorCondition condition);
            PlayerInventoryRuntime runtime = CreateInventory(80f);
            runtime.BindDamageReceiver(receiver);
            var radiationHit = new AnomalyHitEvent(1, 1, 1);
            var shockHit = new AnomalyHitEvent(2, 1, 1);
            AnomalyVariantConfig radiation = CreateVariant(AnomalyDamageType.Radiation);
            AnomalyVariantConfig shock = CreateVariant(AnomalyDamageType.Shock);
            Assert.That(receiver.ApplyHit(radiationHit, radiation, _config), Is.True);
            Assert.That(receiver.ApplyHit(shockHit, shock, _config), Is.True);
            receiver.Initialize(condition);
            Assert.That(receiver.Radiation, Is.EqualTo(0.2f));
            Assert.That(receiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Weak, _config), Is.EqualTo(0.8f).Within(0.000001f));
            Assert.That(receiver.ApplyHit(radiationHit, radiation, _config), Is.False);
            Assert.That(receiver.ApplyHit(shockHit, shock, _config), Is.False);
            Assert.That(condition.Health, Is.EqualTo(96f).Within(0.0001f));
        }

        private StalkerPlayerController CreatePlayer(out AnomalyDamageReceiver receiver, out StalkerActorCondition condition)
        {
            GameObject root = CreateInactiveObject("Player");
            StalkerPlayerController player = root.AddComponent<StalkerPlayerController>();
            condition = root.GetComponent<StalkerActorCondition>();
            condition.Initialize(CreateAsset<StalkerMovementProfile>());
            receiver = root.AddComponent<AnomalyDamageReceiver>();
            receiver.Initialize(condition);
            return player;
        }

        private PlayerInventoryRuntime CreateInventory(float protection, string condition = null, StalkerPlayerController player = null)
        {
            PlayerInventoryRuntime runtime = CreateInactiveObject("ExternalInventory").AddComponent<PlayerInventoryRuntime>();
            InventorySlotLayout layout = CreateAsset<InventorySlotLayout>();
            layout.SetData("o", new[] { new SlotLayoutBinding('o', SlotId.Outfit, "Outfit", SlotMode.Single, SlotRule.Create(categories: new[] { AnomalyEquipmentProtection.OUTFIT_CATEGORY })) });
            var backpack = new InventoryGridSettings("Backpack", "Player", "Backpack", 2, 1, 0);
            var stash = new InventoryGridSettings("Stash", "World", "Stash", 2, 1, 0);
            runtime.Configure(backpack, stash, "Equipment", "Equipment", layout, new[] { SlotId.Outfit }, player, null, null);
            MethodInfo start = typeof(PlayerInventoryRuntime).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(start, Is.Not.Null);
            start.Invoke(runtime, null);
            ItemDefinition definition = CreateAsset<ItemDefinition>();
            using (var serialized = new SerializedObject(definition))
            {
                serialized.FindProperty("_definitionId").stringValue = "anomaly-binding-" + _assets.Count;
                serialized.FindProperty("_category").stringValue = AnomalyEquipmentProtection.OUTFIT_CATEGORY;
                SerializedProperty components = serialized.FindProperty("_components");
                components.arraySize = 1;
                components.GetArrayElementAtIndex(0).managedReferenceValue = new EquipmentStatsComponent();
                SerializedProperty entries = components.GetArrayElementAtIndex(0).FindPropertyRelative("_anomalyProtections");
                entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("_type").enumValueIndex = (int)AnomalyDamageType.Shock;
                SerializedProperty triple = entry.FindPropertyRelative("_values");
                triple.FindPropertyRelative("_weak").floatValue = protection;
                triple.FindPropertyRelative("_average").floatValue = protection;
                triple.FindPropertyRelative("_strong").floatValue = protection;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            ItemInstance item = runtime.Model.CreateItem(definition);
            if (condition != null)
            {
                item.SetState(AnomalyEquipmentProtection.CONDITION_STATE, condition);
            }

            Assert.That(runtime.Model.TryAdd(item, "Equipment", Vector2Int.zero, false).Success, Is.True);
            return runtime;
        }

        private InventoryWindowController CreateController()
        {
            InventoryWindowController controller = CreateInactiveObject("InventoryController").AddComponent<InventoryWindowController>();
            SetField(controller, "_prewarmIcons", false);
            return controller;
        }

        private AnomalyVariantConfig CreateVariant(AnomalyDamageType type)
        {
            var archetype = CreateAsset<AnomalyArchetypeConfig>();
            SetField(archetype, "_id", "binding_test");
            SetField(archetype, "_damageType", type);
            SetField(archetype, "_baseDamage", 20f);
            var variant = CreateAsset<AnomalyVariantConfig>();
            SetField(variant, "_sourceSection", "binding_test_weak");
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

        private static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
