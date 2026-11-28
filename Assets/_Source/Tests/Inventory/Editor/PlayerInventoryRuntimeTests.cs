using RaidIntoDarkness.Inventory.Infrastructure;
using RaidIntoDarkness.Inventory.Domain;
using System.Reflection;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class PlayerInventoryRuntimeTests
    {
        [Test]
        public void ConfiguredEquipmentLocksAndRelocatesItemsWithoutAnInventoryWindow()
        {
            var root = new GameObject(nameof(PlayerInventoryRuntimeTests));
            var layout = ScriptableObject.CreateInstance<InventorySlotLayout>();
            var outfit = ScriptableObject.CreateInstance<ItemDefinition>();
            var helmet = ScriptableObject.CreateInstance<ItemDefinition>();
            var backpack = ScriptableObject.CreateInstance<ItemDefinition>();

            try
            {
                ConfigureItem(outfit, "outfit", "Armor", true);
                ConfigureItem(helmet, "helmet", "Head", false);
                ConfigureItem(backpack, "backpack", "Backpack", false);
                layout.SetData("HOBX", new[]
                {
                    new SlotLayoutBinding('H', SlotId.Head, "Head", SlotMode.Single, SlotRule.Create(new[] { "Head" })),
                    new SlotLayoutBinding('O', SlotId.Outfit, "Outfit", SlotMode.Single, SlotRule.Create(new[] { "Armor" })),
                    new SlotLayoutBinding('B', SlotId.Backpack, "Backpack", SlotMode.Single, SlotRule.Create(new[] { "Backpack" })),
                    new SlotLayoutBinding('X', SlotId.Belt1, "Belt", SlotMode.Single, SlotRule.Create(null))
                });

                var runtime = root.AddComponent<PlayerInventoryRuntime>();
                runtime.Configure(new InventoryGridSettings("Bag", "Player", "Bag", 4, 2, 0), new InventoryGridSettings("Stash", "World", "Stash", 2, 2, 0), "Equipment", "Equipment", layout, new[] { SlotId.Head, SlotId.Outfit, SlotId.Backpack }, null, null, null);
                MethodInfo start = typeof(PlayerInventoryRuntime).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(start, Is.Not.Null);
                start.Invoke(runtime, null);

                InventoryModel model = runtime.Model;
                var equipment = (SlottedContainer)model.GetContainer("Equipment");
                Assert.That(equipment.GetSlot(SlotId.Belt1), Is.Null);

                ItemInstance headItem = model.CreateItem(helmet);
                ItemInstance backpackItem = model.CreateItem(backpack);
                ItemInstance outfitItem = model.CreateItem(outfit);
                Assert.That(model.TryAdd(headItem, equipment.Id, new Vector2Int(0, 0), false).Success, Is.True);
                Assert.That(model.TryAdd(backpackItem, equipment.Id, new Vector2Int(2, 0), false).Success, Is.True);
                int outfitEvents = 0;
                model.ItemEquipped += (item, container, slot) =>
                {
                    if (item == outfitItem)
                    {
                        outfitEvents++;
                        Assert.That(equipment.IsSlotLocked(SlotId.Head), Is.True);
                        Assert.That(equipment.IsSlotLocked(SlotId.Backpack), Is.True);
                        Assert.That(headItem.ContainerId, Is.EqualTo("Bag"));
                        Assert.That(backpackItem.ContainerId, Is.EqualTo("Bag"));
                    }
                };
                Assert.That(model.TryAdd(outfitItem, equipment.Id, new Vector2Int(1, 0), false).Success, Is.True);
                Assert.That(outfitEvents, Is.EqualTo(1));

                Assert.That(equipment.IsSlotLocked(SlotId.Head), Is.True);
                Assert.That(equipment.IsSlotLocked(SlotId.Backpack), Is.True);
                Assert.That(headItem.ContainerId, Is.EqualTo("Bag"));
                Assert.That(backpackItem.ContainerId, Is.EqualTo("Bag"));

                Assert.That(model.TryTransfer(outfitItem.InstanceId, "Bag").Success, Is.True);
                Assert.That(equipment.IsSlotLocked(SlotId.Head), Is.False);
                Assert.That(equipment.IsSlotLocked(SlotId.Backpack), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(layout);
                Object.DestroyImmediate(outfit);
                Object.DestroyImmediate(helmet);
                Object.DestroyImmediate(backpack);
            }
        }

        [Test]
        public void DestroyedInventoryStopsProvidingWeightAndSprintState()
        {
            var root = new GameObject(nameof(PlayerInventoryRuntimeTests));
            var layout = ScriptableObject.CreateInstance<InventorySlotLayout>();
            var outfit = ScriptableObject.CreateInstance<ItemDefinition>();

            try
            {
                outfit.Configure("outfit", "Armor", Vector2Int.one, 4f, 1, false);
                outfit.AddComponent(new EquipmentStatsComponent());
                using (var serialized = new SerializedObject(outfit))
                {
                    SerializedProperty stats = serialized.FindProperty("_components").GetArrayElementAtIndex(0);
                    stats.FindPropertyRelative("_additionalWeight").floatValue = 2f;
                    stats.FindPropertyRelative("_sprintAllowed").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                layout.SetData("O", new[] { new SlotLayoutBinding('O', SlotId.Outfit, "Outfit", SlotMode.Single, SlotRule.Create(new[] { "Armor" })) });
                var runtime = root.AddComponent<PlayerInventoryRuntime>();
                runtime.Configure(new InventoryGridSettings("Bag", "Player", "Bag", 1, 1, 0), new InventoryGridSettings("Stash", "World", "Stash", 1, 1, 0), "Equipment", "Equipment", layout, null, null, null, null);
                Assert.That(runtime.EnsureInitialized(), Is.True);
                Assert.That(runtime.Model.TryAdd(runtime.Model.CreateItem(outfit), "Equipment", Vector2Int.zero, false).Success, Is.True);

                var condition = root.AddComponent<StalkerActorCondition>();
                condition.SetCarryWeightSource(runtime);
                condition.SetSprintSource(runtime);
                Assert.That(condition.TotalWeight, Is.EqualTo(4f));
                Assert.That(condition.AdditionalWeight, Is.EqualTo(2f));
                Assert.That(condition.SprintAllowed, Is.False);

                Object.DestroyImmediate(runtime);

                Assert.That(condition.TotalWeight, Is.Zero);
                Assert.That(condition.AdditionalWeight, Is.Zero);
                Assert.That(condition.SprintAllowed, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(layout);
                Object.DestroyImmediate(outfit);
            }
        }

        private static void ConfigureItem(ItemDefinition definition, string id, string category, bool blocksEquipment)
        {
            using var serialized = new SerializedObject(definition);
            serialized.FindProperty("_definitionId").stringValue = id;
            serialized.FindProperty("_category").stringValue = category;

            if (blocksEquipment)
            {
                SerializedProperty components = serialized.FindProperty("_components");
                components.arraySize = 1;
                SerializedProperty stats = components.GetArrayElementAtIndex(0);
                stats.managedReferenceValue = new EquipmentStatsComponent();
                stats.FindPropertyRelative("_helmetAvailable").boolValue = false;
                stats.FindPropertyRelative("_repairType").stringValue = "outfit_exo";
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
