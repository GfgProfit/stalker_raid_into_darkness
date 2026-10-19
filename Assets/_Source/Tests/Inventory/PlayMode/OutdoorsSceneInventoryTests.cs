using System.Collections;
using System.Collections.Generic;
using Inventory;
using Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class OutdoorsSceneInventoryTests : InputTestFixture
    {
        private const string SCENE = "OutdoorsScene";
        private const string BACKPACK = "Backpack";
        private const string STASH = "Stash";
        private const string EQUIPMENT = "Equipment";

        private Keyboard _keyboard;
        private Mouse _mouse;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        [UnityTest]
        public IEnumerator InventoryOpensBlocksGameplayAndSupportsItemOperations()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSeconds(1.0f);

            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            StalkerPlayerController player = Object.FindAnyObjectByType<StalkerPlayerController>();
            Assert.That(controller, Is.Not.Null, "OutdoorsScene has no InventoryWindowController");
            Assert.That(player, Is.Not.Null);
            Assert.That(Object.FindObjectsByType<InventoryWindowController>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<InventoryWindow>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
            InventoryWindow window = controller.Window;
            Assert.That(window.IsOpen, Is.False, "inventory must start closed");
            Assert.That(controller.Player, Is.SameAs(player));

            yield return Tap(_keyboard.tabKey);
            Assert.That(window.IsOpen, Is.True, "Tab did not open the inventory");
            Assert.That(player.GameplayInputBlocked, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            InventoryModel model = controller.Inventory.Model;
            Assert.That(controller.Inventory.IsSeeded, Is.True);
            InventoryGridViewBase backpackGrid = window.FindGrid(BACKPACK);
            InventoryGridViewBase stashGrid = window.FindGrid(STASH);
            InventoryGridViewBase equipmentGrid = window.FindGrid(EQUIPMENT);
            Assert.That(backpackGrid.Columns, Is.GreaterThan(0));
            Assert.That(backpackGrid.Rows, Is.GreaterThan(0));
            Assert.That(stashGrid.Columns, Is.GreaterThan(0));
            Assert.That(stashGrid.Rows, Is.GreaterThan(0));
            Assert.That(window.SecondaryVisible, Is.False, "Tab must open the personal inventory without the stash panel");
            yield return Tap(_keyboard.bKey);
            Assert.That(window.IsOpen, Is.True);
            Assert.That(window.SecondaryVisible, Is.True, "B did not show the stash panel");
            Assert.That(equipmentGrid.Container, Is.InstanceOf<SlottedContainer>());
            var equipment = (SlottedContainer)equipmentGrid.Container;
            Assert.That(equipment.GetSlot("Primary"), Is.Not.Null);
            Assert.That(equipment.GetSlot("Secondary"), Is.Not.Null);
            Assert.That(equipment.GetSlot("Knife"), Is.Not.Null);
            Assert.That(controller.Sorting, Is.Not.Null, "sorting was not bound");
            AssertViewsMatchModel(window);

            float yaw = player.CameraController.YawDegrees;
            float pitch = player.CameraController.PitchDegrees;
            Vector3 position = player.transform.position;
            Press(_keyboard.wKey, queueEventOnly: true);

            for (int i = 0; i < 20; i++)
            {
                Set(_mouse.delta, new Vector2(150f, 80f), queueEventOnly: true);
                yield return null;
            }

            yield return new WaitForSeconds(0.4f);
            Assert.That(player.CameraController.YawDegrees, Is.EqualTo(yaw).Within(0.01f), "mouse rotated the camera while the inventory was open");
            Assert.That(player.CameraController.PitchDegrees, Is.EqualTo(pitch).Within(0.01f));
            Assert.That(Horizontal(player.transform.position - position), Is.LessThan(0.05f), "WASD moved the player while the inventory was open");
            Release(_keyboard.wKey, queueEventOnly: true);
            yield return null;

            player.ScriptedWish = StalkerMoveFlags.Forward;
            position = player.transform.position;
            yield return new WaitForSeconds(0.5f);
            player.ScriptedWish = StalkerMoveFlags.None;
            Assert.That(Horizontal(player.transform.position - position), Is.GreaterThan(0.3f), "simulation is paused while the inventory is open");

            yield return Tap(_keyboard.escapeKey);
            Assert.That(window.IsOpen, Is.False, "Escape did not close the inventory");
            yield return null;
            yield return null;
            Assert.That(player.GameplayInputBlocked, Is.False, "gameplay input was not restored");

            if (Application.isBatchMode == false)
            {
                Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.Locked));
            }

            yaw = player.CameraController.YawDegrees;
            position = player.transform.position;
            Press(_keyboard.wKey, queueEventOnly: true);

            for (int i = 0; i < 10; i++)
            {
                Set(_mouse.delta, new Vector2(60f, 0f), queueEventOnly: true);
                yield return null;
            }

            yield return new WaitForSeconds(0.6f);
            Release(_keyboard.wKey, queueEventOnly: true);
            yield return null;

            if (Application.isBatchMode == false)
            {
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(player.CameraController.YawDegrees, yaw)), Is.GreaterThan(1f), "mouse look did not resume after closing");
            }

            Assert.That(Horizontal(player.transform.position - position), Is.GreaterThan(0.3f), "WASD did not resume after closing");

            WorldItemInteractor interactor = Object.FindAnyObjectByType<WorldItemInteractor>();
            Assert.That(interactor, Is.Not.Null, "OutdoorsScene has no WorldItemInteractor");
            int sceneItems = Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None).Length;
            Assert.That(sceneItems, Is.GreaterThan(0), "OutdoorsScene has no world items to pick up");
            ItemDefinition pickedDefinition = null;

            foreach (ItemDefinition definition in controller.Inventory.Database.Definitions)
            {
                if (definition.TryGetComponent(out WorldItemComponent worldComponent) && worldComponent.Prefab != null)
                {
                    pickedDefinition = definition;
                    break;
                }
            }

            Assert.That(pickedDefinition, Is.Not.Null, "no item definition has a world prefab");
            WorldItem spawned = interactor.Spawn(pickedDefinition, 1);
            Assert.That(spawned, Is.Not.Null);
            spawned.GetComponent<Rigidbody>().isKinematic = true;
            Transform view = player.CameraController.Camera.transform;
            Vector3 colliderOffset = spawned.GetComponent<Collider>().bounds.center - spawned.transform.position;
            spawned.transform.position = view.position + view.forward * 1.2f - colliderOffset;
            Physics.SyncTransforms();
            yield return null;
            Assert.That(interactor.FindTarget(), Is.SameAs(spawned), "the spawned world item is not under the crosshair");
            yield return null;
            Assert.That(window.Hint, Does.Contain("Подобрать"), "pick up hint was not shown");
            int pickedBefore = CountItems(model, pickedDefinition);
            yield return Tap(_keyboard.fKey);
            yield return null;
            Assert.That(CountItems(model, pickedDefinition), Is.EqualTo(pickedBefore + 1), "F did not pick up the world item");
            Assert.That(spawned == null, Is.True, "picked world item was not removed from the scene");

            yield return Tap(_keyboard.tabKey);
            ItemInstance picked = null;

            foreach (ItemInstance item in window.FindGrid(BACKPACK).Container.Items)
            {
                if (item.Definition == pickedDefinition)
                {
                    picked = item;
                }
            }

            Assert.That(picked, Is.Not.Null);
            InventoryItemView pickedView = window.FindGrid(BACKPACK).GetItemView(picked);
            Assert.That(pickedView.Icon.preserveAspect, Is.True, "grid icons must keep the sprite aspect");
            controller.Sorting.SetFavorite(pickedDefinition, true);
            Color favoriteColor = controller.Sorting.Decorate(picked).Tint;
            Assert.That(pickedView.Background.color, Is.EqualTo(favoriteColor));
            Move(_mouse.position, RectTransformUtility.WorldToScreenPoint(null, pickedView.RectTransform.position), queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.That(pickedView.Background.color.a, Is.GreaterThan(favoriteColor.a), "hovered favorite item is not highlighted");
            Assert.That(new Vector3(pickedView.Background.color.r, pickedView.Background.color.g, pickedView.Background.color.b), Is.EqualTo(new Vector3(favoriteColor.r, favoriteColor.g, favoriteColor.b)), "hover must keep the favorite hue");
            Move(_mouse.position, new Vector2(5f, 5f), queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.That(pickedView.Background.color, Is.EqualTo(favoriteColor), "favorite color did not return after hover");
            controller.Sorting.SetFavorite(pickedDefinition, false);
            IReadOnlyList<InventoryItemStat> stats = window.CollectStats(picked);
            Assert.That(stats.Count, Is.GreaterThanOrEqualTo(9), "equipment stats are missing in the tooltip data");
            Assert.That(stats[0].Value.EndsWith("%") && stats[0].Icon != null, Is.True);
            ItemDefinition nosorog = null;

            foreach (ItemDefinition definition in controller.Inventory.Database.Definitions)
            {
                if (definition.name == "army_nosorog_outfit")
                {
                    nosorog = definition;
                }
            }

            Assert.That(nosorog, Is.Not.Null);
            var nosorogValues = new List<string>();

            foreach (InventoryItemStat stat in window.CollectStats(model.CreateItem(nosorog)))
            {
                nosorogValues.Add(stat.Value);
            }

            Assert.That(nosorogValues, Is.EqualTo(new[] { "+62 %", "+37 %", "+66 %", "+61 %", "+44 %", "+52 %", "+16 %", "+25 %", "+16 %", "+91 %", "2", "+108 кг", "100 %", "98 %", "72 %" }), "army_nosorog_outfit stats differ from the game");
            window.Tooltip.Show(picked, new Vector2(400f, 400f), null, stats);
            Assert.That(window.Tooltip.StatCount, Is.EqualTo(stats.Count));
            Assert.That(((RectTransform)window.Tooltip.transform).sizeDelta.x, Is.GreaterThanOrEqualTo(440f));
            window.Tooltip.Hide();
            window.EquipItem(picked);
            yield return null;
            Assert.That(picked.ContainerId, Is.EqualTo(EQUIPMENT));
            InventoryEquipmentSlot equippedSlot = ((InventoryEquipmentPanel)equipmentGrid).GetSlotView(picked.Location.SlotId);
            Assert.That(equippedSlot, Is.Not.Null, "equipment panel has no slot object for " + picked.Location.SlotId);
            Assert.That(equippedSlot.Icon.sprite, Is.SameAs(pickedDefinition.FallbackIcon), "equipped icon is not shown in the slot icon image");
            Assert.That(equippedSlot.Icon.isActiveAndEnabled && equippedSlot.Icon.preserveAspect, Is.True, "equipped icon is hidden");
            window.UnequipItem(picked);
            yield return null;
            Assert.That(equippedSlot.Icon.isActiveAndEnabled, Is.False, "unequipped slot still shows the icon");
            int worldBefore = Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None).Length;
            window.DropItem(picked);
            yield return null;
            Assert.That(model.ContainsItem(picked), Is.False);
            Assert.That(Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None).Length, Is.EqualTo(worldBefore + 1), "dropping did not spawn a world item");
            yield return Tap(_keyboard.tabKey);
            yield return null;
            yield return null;

            int generated = window.GetComponentsInChildren<InventoryGeneratedElement>(true).Length;
            int itemCount = model.ItemCount;
            yield return Tap(_keyboard.tabKey);
            Assert.That(window.IsOpen, Is.True);
            yield return null;
            Assert.That(model.ItemCount, Is.EqualTo(itemCount), "reopening re-seeded the inventory");
            Assert.That(window.GetComponentsInChildren<InventoryGeneratedElement>(true).Length, Is.EqualTo(generated), "reopening duplicated generated grid elements");
            AssertViewsMatchModel(window);

            yield return Tap(_keyboard.tabKey);
            Assert.That(window.IsOpen, Is.False, "Tab did not close the inventory");
            yield return null;
            yield return null;
            Assert.That(player.GameplayInputBlocked, Is.False);
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        private IEnumerator Tap(ButtonControl button)
        {
            Press(button, queueEventOnly: true);
            yield return null;
            Release(button, queueEventOnly: true);
            yield return null;
        }

        private static int CountItems(InventoryModel model, ItemDefinition definition)
        {
            int count = 0;

            foreach (ItemInstance item in model.Items)
            {
                if (item.Definition == definition)
                {
                    count += item.Amount;
                }
            }

            return count;
        }

        private static float Horizontal(Vector3 delta) => new Vector2(delta.x, delta.z).magnitude;

        private static void AssertViewsMatchModel(InventoryWindow window)
        {
            var seen = new HashSet<string>();

            foreach (InventoryGridViewBase grid in window.Grids)
            {
                Assert.That(grid.ItemViews.Count, Is.EqualTo(grid.Container.Items.Count), $"{grid.ContainerId} item views are out of sync");
                Assert.That(grid.ItemsRoot.GetComponentsInChildren<InventoryItemView>(true).Length, Is.EqualTo(grid.Container.Items.Count), $"{grid.ContainerId} has duplicated item views");

                foreach (ItemInstance item in grid.Container.Items)
                {
                    Assert.That(seen.Add(item.InstanceId), Is.True, $"{item.InstanceId} is shown twice");
                }
            }
        }
    }
}
