using System.Collections;
using NUnit.Framework;
using RaidIntoDarkness.Inventory.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class OutfitSlotTransitionTests
    {
        private const string TEST_SCENE = "TEST_SCENE";

        [UnityTest]
        public IEnumerator OutfitDropsAnIncompatibleHelmetWhenTheBackpackCannotTakeIt()
        {
            yield return LoadScene();
            PlayerInventoryRuntime inventory = FindInventory();
            InventoryModel model = inventory.Model;
            var equipment = (SlottedContainer)model.GetContainer(inventory.EquipmentContainerId);
            InventoryContainer carried = model.GetContainer(inventory.PrimaryContainerId);
            ItemInstance oldOutfit = AddEquipped(inventory, equipment, SlotId.Outfit, false);
            ItemInstance helmet = AddEquipped(inventory, equipment, SlotId.Head, false);
            ItemInstance incoming = AddOutfitToStash(inventory);
            carried.MaxWeight = oldOutfit.TotalWeight;
            int outfitEvents = 0;
            int dropEvents = 0;
            model.ItemEquipped += (item, container, slot) =>
            {
                if (item == incoming)
                {
                    outfitEvents++;
                    Assert.That(equipment.GetSlotOccupant(SlotId.Head), Is.Null);
                    Assert.That(equipment.IsSlotLocked(SlotId.Head), Is.True);
                    Assert.That(equipment.IsSlotLocked(SlotId.Backpack), Is.True);
                    Assert.That(model.GetItem(helmet.InstanceId), Is.Null);
                    Assert.That(CountWorldInstances(helmet), Is.EqualTo(1));
                }
            };
            inventory.ForcedDropCompleted += (item, result) =>
            {
                Assert.That(item, Is.SameAs(helmet));
                Assert.That(result.Success, Is.True);
                dropEvents++;
            };

            InventoryResult outcome = model.TryEquipItem(incoming, equipment.Id, SlotId.Outfit, carried.Id);

            Assert.That(outcome.Success, Is.True, outcome.Failure.ToString());
            Assert.That(oldOutfit.ContainerId, Is.EqualTo(carried.Id));
            Assert.That(equipment.GetSlotOccupant(SlotId.Outfit), Is.SameAs(incoming));
            Assert.That(model.TrySetSlotLocked(equipment.Id, SlotId.Head, false).Failure, Is.EqualTo(InventoryFailure.SlotUnavailable));
            Assert.That(outfitEvents, Is.EqualTo(1));
            Assert.That(dropEvents, Is.EqualTo(1));
            Assert.That(CountWorldInstances(helmet), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OutfitWithNoBackpackSpaceOrValidWorldDropKeepsOriginalItems()
        {
            yield return RejectOutfitChange(false);
        }

        [UnityTest]
        public IEnumerator FailureToClearTheBackpackSlotDoesNotMoveAnEarlierHelmet()
        {
            yield return RejectOutfitChange(true);
        }

        private static IEnumerator RejectOutfitChange(bool twoSlots)
        {
            yield return LoadScene();
            PlayerInventoryRuntime inventory = FindInventory();
            InventoryModel model = inventory.Model;
            var equipment = (SlottedContainer)model.GetContainer(inventory.EquipmentContainerId);
            InventoryContainer carried = model.GetContainer(inventory.PrimaryContainerId);
            ItemInstance oldOutfit = AddEquipped(inventory, equipment, SlotId.Outfit, false);
            ItemInstance helmet = AddEquipped(inventory, equipment, SlotId.Head, false);
            ItemInstance bag = twoSlots ? AddEquipped(inventory, equipment, SlotId.Backpack, false) : null;
            ItemInstance incoming = AddOutfitToStash(inventory);
            carried.MaxWeight = oldOutfit.TotalWeight + (twoSlots ? helmet.TotalWeight : 0f);
            ItemLocation originalOutfit = oldOutfit.Location;
            ItemLocation originalHelmet = helmet.Location;
            ItemLocation originalIncoming = incoming.Location;
            ItemLocation originalBag = bag?.Location ?? ItemLocation.None;
            int itemCount = model.ItemCount;
            float carryWeight = inventory.TotalWeight;
            float carryBonus = inventory.AdditionalWeight;
            float protection = inventory.GetAnomalyProtection(AnomalyDamageType.Burn, AnomalyStrength.Average, null);
            int changes = 0;
            int equipEvents = 0;
            int dropEvents = 0;
            model.Changed += _ => changes++;
            model.ItemEquipped += (_, _, _) => equipEvents++;
            model.ItemUnequipped += (_, _, _) => equipEvents++;
            inventory.ForcedDropCompleted += (_, _) => dropEvents++;
            GameObject blockers = BlockDropPaths(inventory);

            try
            {
                Assert.That(model.CanEquipItem(incoming, equipment.Id, carried.Id), Is.True);
                InventoryResult outcome = model.TryEquipItem(incoming, equipment.Id, SlotId.Outfit, carried.Id);

                Assert.That(outcome.Failure, Is.EqualTo(InventoryFailure.NoSpace));
                Assert.That(model.GetItem(oldOutfit.InstanceId), Is.SameAs(oldOutfit));
                Assert.That(model.GetItem(helmet.InstanceId), Is.SameAs(helmet));
                Assert.That(model.GetItem(incoming.InstanceId), Is.SameAs(incoming));
                Assert.That(oldOutfit.Location, Is.EqualTo(originalOutfit));
                Assert.That(helmet.Location, Is.EqualTo(originalHelmet));
                Assert.That(incoming.Location, Is.EqualTo(originalIncoming));
                Assert.That(oldOutfit.Amount, Is.EqualTo(1));
                Assert.That(helmet.Amount, Is.EqualTo(1));
                Assert.That(incoming.Amount, Is.EqualTo(1));
                Assert.That(carried.Items, Is.Empty);
                Assert.That(equipment.GetSlotOccupant(SlotId.Outfit), Is.SameAs(oldOutfit));
                Assert.That(equipment.GetSlotOccupant(SlotId.Head), Is.SameAs(helmet));
                Assert.That(equipment.IsSlotLocked(SlotId.Head), Is.False);
                Assert.That(equipment.IsSlotLocked(SlotId.Backpack), Is.False);
                Assert.That(model.ItemCount, Is.EqualTo(itemCount));
                Assert.That(inventory.TotalWeight, Is.EqualTo(carryWeight));
                Assert.That(inventory.AdditionalWeight, Is.EqualTo(carryBonus));
                Assert.That(inventory.GetAnomalyProtection(AnomalyDamageType.Burn, AnomalyStrength.Average, null), Is.EqualTo(protection));
                Assert.That(CountWorldInstances(helmet), Is.Zero);
                if (bag != null)
                {
                    Assert.That(model.GetItem(bag.InstanceId), Is.SameAs(bag));
                    Assert.That(bag.Location, Is.EqualTo(originalBag));
                    Assert.That(bag.Amount, Is.EqualTo(1));
                    Assert.That(equipment.GetSlotOccupant(SlotId.Backpack), Is.SameAs(bag));
                    Assert.That(CountWorldInstances(bag), Is.Zero);
                }

                Assert.That(changes, Is.Zero);
                Assert.That(equipEvents, Is.Zero);
                Assert.That(dropEvents, Is.Zero);
            }
            finally
            {
                Object.Destroy(blockers);
            }
        }

        private static IEnumerator LoadScene()
        {
            SceneManager.LoadScene(TEST_SCENE, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        private static PlayerInventoryRuntime FindInventory()
        {
            var inventory = Object.FindAnyObjectByType<PlayerInventoryRuntime>();
            Assert.That(inventory, Is.Not.Null);
            Assert.That(inventory.EnsureInitialized(), Is.True);
            inventory.Model.Clear();
            inventory.Player.transform.position += Vector3.up * 25f;
            Physics.SyncTransforms();
            return inventory;
        }

        private static ItemInstance AddEquipped(PlayerInventoryRuntime inventory, SlottedContainer equipment, SlotId slotId, bool restrictsEquipment)
        {
            ItemDefinition definition = FindDefinition(inventory.Database, slotId, restrictsEquipment);
            ItemInstance item = inventory.Model.CreateItem(definition);
            Assert.That(inventory.Model.TryAdd(item, equipment.Id, equipment.GetSlot(slotId).Bounds.position, false).Success, Is.True);
            return item;
        }

        private static ItemInstance AddOutfitToStash(PlayerInventoryRuntime inventory)
        {
            ItemInstance item = inventory.Model.CreateItem(FindDefinition(inventory.Database, SlotId.Outfit, true));
            var controller = Object.FindAnyObjectByType<InventoryWindowController>();
            Assert.That(inventory.Model.TryAddAuto(item, controller.Window.SecondaryContainerId).Success, Is.True);
            return item;
        }

        private static ItemDefinition FindDefinition(ItemDatabase database, SlotId slotId, bool restricted)
        {
            string category = slotId == SlotId.Outfit ? "Armor" : slotId == SlotId.Head ? "Head" : "Backpack";
            foreach (ItemDefinition definition in database.Definitions)
            {
                if (definition == null || definition.IsCategory(category) == false || definition.Weight <= 0f || definition.GetComponent<WorldItemComponent>()?.Prefab == null)
                {
                    continue;
                }

                EquipmentStatsComponent stats = definition.GetComponent<EquipmentStatsComponent>();
                if (slotId != SlotId.Outfit || stats != null && (restricted ? stats.HelmetAvailable == false && stats.RepairType == "outfit_exo" : stats.HelmetAvailable && stats.RepairType != "outfit_exo"))
                {
                    return definition;
                }
            }

            Assert.Fail($"No suitable item definition for {slotId}.");
            return null;
        }

        private static GameObject BlockDropPaths(PlayerInventoryRuntime inventory)
        {
            Transform player = inventory.Player.transform;
            Transform view = inventory.Player.CameraController.Camera.transform;
            CharacterController character = player.GetComponent<CharacterController>();
            Vector3 origin = character != null ? character.bounds.center : view.position;
            origin.y = view.position.y - 0.3f;
            var blockers = new GameObject(nameof(BlockDropPaths));
            AddWall(blockers.transform, origin + Vector3.forward * 0.31f, new Vector3(1f, 2f, 0.1f));
            AddWall(blockers.transform, origin + Vector3.back * 0.31f, new Vector3(1f, 2f, 0.1f));
            AddWall(blockers.transform, origin + Vector3.right * 0.31f, new Vector3(0.1f, 2f, 1f));
            AddWall(blockers.transform, origin + Vector3.left * 0.31f, new Vector3(0.1f, 2f, 1f));
            Physics.SyncTransforms();
            return blockers;
        }

        private static void AddWall(Transform parent, Vector3 position, Vector3 size)
        {
            var wall = new GameObject(nameof(BoxCollider));
            wall.transform.SetParent(parent);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider>().size = size;
        }

        private static int CountWorldInstances(ItemInstance item)
        {
            int count = 0;
            foreach (WorldItem worldItem in Object.FindObjectsByType<WorldItem>())
            {
                if (worldItem.Instance == item && worldItem.isActiveAndEnabled)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
