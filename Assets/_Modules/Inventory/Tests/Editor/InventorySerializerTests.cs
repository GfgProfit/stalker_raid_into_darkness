using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Inventory.Tests
{
    public sealed class InventorySerializerTests
    {
        private InventoryTestItems _items;
        private ItemDefinition _rifle;
        private ItemDefinition _ammo;
        private ItemDefinition _armor;
        private ItemDefinition _medkit;
        private InventorySlotLayout _layout;

        [SetUp]
        public void SetUp()
        {
            _items = new InventoryTestItems();
            _rifle = _items.Create("rifle", new Vector2Int(4, 2), 1, 3.5f, "Weapon");
            _ammo = _items.Create("ammo", Vector2Int.one, 60, 0.01f, "Ammo");
            _armor = _items.Create("armor", new Vector2Int(2, 3), 1, 6f, "Armor");
            _medkit = _items.Create("medkit", Vector2Int.one, 5, 0.2f, "Medical");
            _layout = _items.Track(ScriptableObject.CreateInstance<InventorySlotLayout>());
            _layout.SetData("AA1\nAA2", new List<SlotLayoutBinding>
            {
                new('A', "Armor", "Armor", SlotMode.Single, SlotRule.Create(new[] { "Armor" })),
                new('1', "Pocket", "Pocket", SlotMode.Single, SlotRule.Create(null, null, null, null, new[] { "Armor" })),
                new('2', "Locked", "Locked", SlotMode.Single, SlotRule.Create(startsLocked: true))
            });
        }

        [TearDown]
        public void TearDown()
        {
            _items.Dispose();
        }

        [Test]
        public void RoundTripPreservesItemsStateAndSlots()
        {
            InventoryModel source = CreateModel();
            ItemInstance rifle = source.CreateItem(_rifle);
            rifle.SetVisualVariant(new[] { "scope", "grip" });
            rifle.SetState("condition", "0.75");
            source.TryAdd(rifle, "Backpack", new Vector2Int(2, 1), true);
            source.TryAdd(source.CreateItem(_ammo, 42), "Backpack", Vector2Int.zero, false);
            source.TryAdd(source.CreateItem(_armor), "Equipment", Vector2Int.zero, false);
            source.TryAdd(source.CreateItem(_medkit, 3), "Equipment", new Vector2Int(2, 0), false);
            source.TrySetSlotLocked("Equipment", "Locked", false);
            string json = InventorySerializer.ToJson(source);

            InventoryModel restored = CreateModel();
            InventoryLoadReport report = InventorySerializer.FromJson(restored, json);

            Assert.That(report.IsClean, Is.True);
            Assert.That(report.LoadedItems, Is.EqualTo(4));
            ItemInstance restoredRifle = restored.GetItem(rifle.InstanceId);
            Assert.That(restoredRifle, Is.Not.Null);
            Assert.That(restoredRifle.Location.Position, Is.EqualTo(new Vector2Int(2, 1)));
            Assert.That(restoredRifle.Location.Rotated, Is.True);
            Assert.That(restoredRifle.GetState("condition"), Is.EqualTo("0.75"));
            Assert.That(restoredRifle.VisualVariant, Is.EqualTo(new[] { "grip", "scope" }));
            var equipment = (SlottedContainer)restored.GetContainer("Equipment");
            Assert.That(equipment.GetSlotOccupant("Pocket").Amount, Is.EqualTo(3));
            Assert.That(equipment.GetSlotOccupant("Armor").DefinitionId, Is.EqualTo("armor"));
            Assert.That(equipment.IsSlotLocked("Locked"), Is.False);
            Assert.That(InventorySerializer.ToJson(restored), Is.EqualTo(json));
        }

        [Test]
        public void UnknownDefinitionsArePreservedAcrossSaves()
        {
            InventoryModel source = CreateModel();
            source.TryAdd(source.CreateItem(_ammo, 10), "Backpack", Vector2Int.zero, false);
            InventorySaveData data = InventorySerializer.Capture(source);
            data.Containers[0].Items.Add(new InventoryItemData { InstanceId = "legacy", DefinitionId = "removed_item", Amount = 2, X = 5, Y = 5 });

            InventoryModel restored = CreateModel();
            InventoryLoadReport report = InventorySerializer.Restore(restored, data);

            Assert.That(report.UnknownDefinitions.Count, Is.EqualTo(1));
            Assert.That(restored.UnresolvedItems.Count, Is.EqualTo(1));
            Assert.That(restored.ItemCount, Is.EqualTo(1));
            InventorySaveData resaved = InventorySerializer.Capture(restored);
            Assert.That(resaved.Unresolved.Count, Is.EqualTo(1));
            Assert.That(resaved.Unresolved[0].DefinitionId, Is.EqualTo("removed_item"));

            InventoryModel third = CreateModel();
            InventorySerializer.Restore(third, resaved);
            Assert.That(third.UnresolvedItems.Count, Is.EqualTo(1));
        }

        [Test]
        public void MissingContainersAndCollisionsAreReported()
        {
            var data = new InventorySaveData();
            var missing = new InventoryContainerData { ContainerId = "Vehicle" };
            missing.Items.Add(new InventoryItemData { InstanceId = "a", DefinitionId = "ammo", Amount = 1 });
            var backpack = new InventoryContainerData { ContainerId = "Backpack" };
            backpack.Items.Add(new InventoryItemData { InstanceId = "b", DefinitionId = "ammo", Amount = 1 });
            backpack.Items.Add(new InventoryItemData { InstanceId = "b", DefinitionId = "ammo", Amount = 1, X = 1 });
            data.Containers.Add(missing);
            data.Containers.Add(backpack);
            InventoryModel model = CreateModel();

            InventoryLoadReport report = InventorySerializer.Restore(model, data);

            Assert.That(report.MissingContainers, Is.EqualTo(new[] { "Vehicle" }));
            Assert.That(report.UnplacedItems.Count, Is.EqualTo(2));
            Assert.That(report.LoadedItems, Is.EqualTo(1));
        }

        [Test]
        public void NewerVersionLogsWarningAndStillLoads()
        {
            InventoryModel source = CreateModel();
            source.TryAdd(source.CreateItem(_ammo, 10), "Backpack", Vector2Int.zero, false);
            InventorySaveData data = InventorySerializer.Capture(source);
            data.Version = InventorySaveData.CURRENT_VERSION + 1;
            string json = JsonUtility.ToJson(data);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("newer than supported"));
            InventoryModel restored = CreateModel();
            InventoryLoadReport report = InventorySerializer.FromJson(restored, json);

            Assert.That(report.LoadedItems, Is.EqualTo(1));
        }

        private InventoryModel CreateModel()
        {
            var model = new InventoryModel(_items.Database);
            model.AddContainer(new GridContainer("Backpack", 10, 8, "Player"));
            model.AddContainer(SlottedContainer.FromLayout("Equipment", _layout, true, "Player"));
            return model;
        }
    }
}
