using System.Collections;
using System.Collections.Generic;
using Inventory;
using Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.FirstPerson;
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
        public IEnumerator ShiftPressedOverHoveredItemTransfersOnceInBothDirections()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemDefinition definition = null;

            foreach (ItemDefinition candidate in controller.Inventory.Database.Definitions)
            {
                if (candidate.name == "ammo_5.56x45_fmj")
                {
                    definition = candidate;
                }
            }

            ItemInstance item = model.CreateItem(definition, 10);
            Assert.That(model.TryAddAuto(item, window.PrimaryContainerId, false).Success, Is.True);
            controller.Open(true);
            yield return null;

            for (int i = 0; i < 2; i++)
            {
                InventoryItemView view = window.FindGrid(item.ContainerId).GetItemView(item);
                var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, view.RectTransform.position) };
                ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                yield return null;
                string destination = item.ContainerId == window.PrimaryContainerId ? window.SecondaryContainerId : window.PrimaryContainerId;
                Press(_keyboard.leftShiftKey, queueEventOnly: true);
                yield return null;
                yield return null;
                Assert.That(item.ContainerId, Is.EqualTo(destination));
                yield return new WaitForSeconds(0.1f);
                Assert.That(item.ContainerId, Is.EqualTo(destination));
                Release(_keyboard.leftShiftKey, queueEventOnly: true);
                yield return null;
            }

            controller.Close();
        }

        [UnityTest]
        public IEnumerator DroppingWhileLookingDownDoesNotIntersectThePlayerOrLaunchSideways()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            WorldItemInteractor interactor = Object.FindAnyObjectByType<WorldItemInteractor>();
            controller.Player.CameraController.SetLook(0f, 90f);
            yield return null;
            yield return new WaitForEndOfFrame();
            CharacterController character = controller.Player.GetComponent<CharacterController>();

            foreach (string section in new[] { "ammo_5.56x45_fmj", "wpn_aug_a3" })
            {
                ItemDefinition definition = null;

                foreach (ItemDefinition candidate in controller.Inventory.Database.Definitions)
                {
                    if (candidate.name == section)
                    {
                        definition = candidate;
                    }
                }

                WorldItem dropped = interactor.Spawn(definition, 1);
                Assert.That(dropped, Is.Not.Null, section);
                Physics.SyncTransforms();

                foreach (Collider collider in dropped.GetComponentsInChildren<Collider>())
                {
                    Assert.That(Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation, character, character.transform.position, character.transform.rotation, out _, out _), Is.False, section);
                }

                Vector3 position = dropped.transform.position;
                Rigidbody body = dropped.GetComponent<Rigidbody>();
                yield return new WaitForSeconds(0.2f);
                Assert.That(Vector3.ProjectOnPlane(dropped.transform.position - position, Vector3.up).magnitude, Is.LessThan(0.1f), section);
                Assert.That(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude, Is.LessThan(0.5f), section);
                Object.Destroy(dropped.gameObject);
                yield return null;
            }
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
            Assert.That(player.Pickup, Is.Not.Null, "FPS_Player has no FirstPersonPickupAnimator");
            Assert.That(player.Rig.Hands.activeSelf, Is.False, "hands are visible before pick up");
            yield return Tap(_keyboard.fKey);
            Assert.That(interactor.IsPickingUp, Is.True, "F did not start the pick up animation");
            Assert.That(player.Pickup.IsPlaying, Is.True, "pick up animation is not playing");
            Assert.That(player.Rig.Hands.activeSelf, Is.True, "hands are hidden during pick up");
            Assert.That(CountItems(model, pickedDefinition), Is.EqualTo(pickedBefore), "item was taken before the middle of the animation");
            FirstPersonPickupHand hand = player.Pickup.CurrentHand;
            yield return Tap(_keyboard.fKey);
            Assert.That(player.Pickup.CurrentHand, Is.EqualTo(hand), "a repeated press restarted the pick up");
            float timeout = Time.time + player.Pickup.Duration + 1f;

            while (interactor.IsPickingUp && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(interactor.IsPickingUp, Is.False, "pick up did not finish");
            Assert.That(CountItems(model, pickedDefinition), Is.EqualTo(pickedBefore + 1), "F did not pick up the world item exactly once");
            Assert.That(spawned == null, Is.True, "picked world item was not removed from the scene");
            Assert.That(player.Rig.Hands.activeSelf, Is.False, "hands stay visible after pick up");

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
            Assert.That(player.HudAnimator.IsPlaying, Is.True, "outfit equip animation did not start");
            Assert.That(player.CameraController.HudFieldOfView, Is.EqualTo(player.HudAnimator.Current.HudFieldOfView).Within(0.0001f), "outfit animation hud_fov was not applied");
            yield return null;
            Assert.That(picked.ContainerId, Is.EqualTo(EQUIPMENT));
            InventoryEquipmentSlot equippedSlot = ((InventoryEquipmentPanel)equipmentGrid).GetSlotView(picked.Location.SlotId);
            Assert.That(equippedSlot, Is.Not.Null, "equipment panel has no slot object for " + picked.Location.SlotId);
            Assert.That(equippedSlot.Icon.sprite, Is.SameAs(pickedDefinition.FallbackIcon), "equipped icon is not shown in the slot icon image");
            Assert.That(equippedSlot.Icon.isActiveAndEnabled && equippedSlot.Icon.preserveAspect, Is.True, "equipped icon is hidden");
            yield return WaitForEquipmentAnimations(controller);
            Assert.That(player.CameraController.HudFieldOfView, Is.EqualTo(player.Profile.Camera.HudFieldOfView).Within(0.0001f), "hud_fov was not restored");
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

        [UnityTest]
        public IEnumerator HeadgearAndBackpackPlayAnimationsBeforeMoving()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSeconds(1.0f);

            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            StalkerPlayerController player = Object.FindAnyObjectByType<StalkerPlayerController>();
            yield return Tap(_keyboard.tabKey);
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            EquipmentAnimationQueue queue = controller.EquipmentAnimations;
            Assert.That(queue, Is.Not.Null, "equipment animations are not bound");
            Assert.That(player.HudAnimator, Is.Not.Null, "FPS_Player has no FirstPersonHudAnimator");
            Assert.That(player.ScreenFade, Is.Not.Null, "FPS_Player has no StalkerScreenFade");
            yield return WaitForEquipmentAnimations(controller);
            ItemInstance outfit = EquipmentStatsFormatter.FindEquipped(window, EquipmentStatsFormatter.OUTFIT_CATEGORY);

            if (outfit != null)
            {
                window.UnequipItemNow(outfit, InventoryUnequipTarget.Auto(window.PrimaryContainerId));
            }

            ItemInstance oldHat = EquipmentStatsFormatter.FindEquipped(window, EquipmentStatsFormatter.HELMET_CATEGORY);

            if (oldHat != null)
            {
                window.UnequipItemNow(oldHat, InventoryUnequipTarget.Auto(window.PrimaryContainerId));
            }

            ItemInstance hat = AddItem(model, controller, window, "helm_hardhat");
            window.EquipItem(hat);
            Assert.That(hat.ContainerId, Is.Not.EqualTo(EQUIPMENT), "helmet was equipped before its animation");
            Assert.That(queue.IsInUse(hat), Is.True);
            Assert.That(window.FindGrid(BACKPACK).GetItemView(hat).Background.color, Is.EqualTo(EquipmentAnimationQueue.IN_USE_TINT), "queued helmet is not highlighted");
            Assert.That(player.HudAnimator.IsPlaying, Is.True, "helmet equip animation did not start");
            Assert.That(player.HudAnimator.Item, Is.Not.Null, "helmet hud model is not shown");
            Assert.That(player.CameraEffects.IsItemEffectPlaying, Is.True, "helmet camera effect did not start");
            bool faded = false;
            float timeout = Time.time + 30.0f;

            while (queue.IsBusy && Time.time < timeout)
            {
                faded |= player.ScreenFade.IsPlaying;
                yield return null;
            }

            Assert.That(hat.ContainerId, Is.EqualTo(EQUIPMENT), "helmet was not equipped after its animation");
            Assert.That(faded, Is.True, "fade_use helmet did not play deimos_short.ppe");
            Assert.That(queue.IsInUse(hat), Is.False);
            window.UnequipItem(hat);
            Assert.That(player.ScreenFade.IsPlaying, Is.True, "unequip did not start with the fade");
            Assert.That(hat.ContainerId, Is.EqualTo(EQUIPMENT));
            yield return WaitForEquipmentAnimations(controller);
            Assert.That(hat.ContainerId, Is.EqualTo(BACKPACK), "helmet was not unequipped");

            ItemInstance pack = AddItem(model, controller, window, "itm_backpack");
            window.EquipItem(pack);
            Assert.That(pack.ContainerId, Is.Not.EqualTo(EQUIPMENT));
            Assert.That(player.HudAnimator.IsPlaying, Is.True, "backpack animation did not start");
            Assert.That(player.HudAnimator.Item, Is.Not.Null, "backpack hud model is not shown");
            yield return WaitForEquipmentAnimations(controller);
            Assert.That(pack.ContainerId, Is.EqualTo(EQUIPMENT), "backpack was not equipped after its animation");
            Assert.That(player.Rig.Hands.activeSelf, Is.False, "hands stay visible after equipment animations");
        }

        [UnityTest]
        public IEnumerator InventoryWorldAndStashSoundsFollowGra()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSeconds(1.0f);

            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            WorldItemInteractor interactor = Object.FindAnyObjectByType<WorldItemInteractor>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            var sounds = new List<InventorySound>();
            window.SoundPlayed += sounds.Add;
            Assert.That(window.Theme != null && window.Theme.Sounds != null, Is.True, "GRA theme has no inventory sound set");
            Assert.That(controller.ItemSounds, Is.Not.Null);

            yield return Tap(_keyboard.tabKey);
            Assert.That(sounds, Is.EqualTo(new[] { InventorySound.Open }), "opening the inventory must play inv_open");
            yield return WaitForEquipmentAnimations(controller);
            ItemInstance oldHat = EquipmentStatsFormatter.FindEquipped(window, EquipmentStatsFormatter.HELMET_CATEGORY);

            if (oldHat != null)
            {
                window.UnequipItemNow(oldHat, InventoryUnequipTarget.Auto(window.PrimaryContainerId), false);
            }

            ItemInstance outfit = EquipmentStatsFormatter.FindEquipped(window, EquipmentStatsFormatter.OUTFIT_CATEGORY);

            if (outfit != null)
            {
                window.UnequipItemNow(outfit, InventoryUnequipTarget.Auto(window.PrimaryContainerId), false);
            }

            Assert.That(sounds.Count, Is.EqualTo(1), "silent FDDA moves played a sound");
            ItemInstance hat = AddItem(model, controller, window, "helm_hardhat");
            yield return new WaitForSeconds(0.6f);
            window.OpenContextMenu(hat, window.FindGrid(BACKPACK), new Vector2(400f, 400f), null);
            Assert.That(sounds[sounds.Count - 1], Is.EqualTo(InventorySound.ContextMenu), "context menu must play inv_properties_2");
            window.CloseTopWidget();
            yield return new WaitForSeconds(0.6f);
            window.EquipItem(hat);
            Assert.That(sounds.Contains(InventorySound.ToSlot), Is.False, "inv_slot played before the FDDA animation finished");
            yield return WaitForEquipmentAnimations(controller);
            Assert.That(sounds[sounds.Count - 1], Is.EqualTo(InventorySound.ToSlot), "equipping must play inv_slot when the helmet reaches the slot");
            yield return new WaitForSeconds(0.6f);
            window.UnequipItem(hat);
            yield return WaitForEquipmentAnimations(controller);
            Assert.That(sounds[sounds.Count - 1], Is.EqualTo(InventorySound.ToBag), "unequipping must play inv_ruck");
            yield return new WaitForSeconds(0.6f);
            int worldBefore = Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None).Length;
            window.DropItem(hat);
            Assert.That(sounds[sounds.Count - 1], Is.EqualTo(InventorySound.Drop), "dropping must play inv_drop");
            yield return null;
            WorldItem dropped = null;

            foreach (WorldItem candidate in Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
            {
                if (candidate.Definition == hat.Definition)
                {
                    dropped = candidate;
                }
            }

            Assert.That(Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None).Length, Is.EqualTo(worldBefore + 1));
            WorldItemImpactSound impact = dropped.GetComponent<WorldItemImpactSound>();
            Assert.That(impact, Is.Not.Null, "world item prefab has no impact sound");
            float timeout = Time.time + 3f;

            while (impact.PlayedCount == 0 && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(impact.PlayedCount, Is.GreaterThan(0), "the dropped helmet hit the ground silently");
            Assert.That(impact.LastVolume, Is.InRange(0.1f, 1f));
            int taken = controller.ItemSounds.PlayedCount;
            Assert.That(interactor.TryPickUp(dropped), Is.True);
            Assert.That(controller.ItemSounds.PlayedCount, Is.EqualTo(taken + 1), "picking up must play snd_on_take");
            Assert.That(controller.ItemSounds.LastVolume, Is.EqualTo(InventoryItemSounds.TAKE_VOLUME));
            Assert.That(hat.Definition.GetComponent<ItemTakeSoundComponent>().Category, Is.EqualTo("helmet"));
            yield return new WaitForSeconds(0.6f);
            yield return Tap(_keyboard.tabKey);
            Assert.That(sounds[sounds.Count - 1], Is.EqualTo(InventorySound.Close), "closing must play inv_close");
            yield return Tap(_keyboard.bKey);
            yield return null;
            ItemInstance picked = null;

            foreach (ItemInstance item in window.FindGrid(BACKPACK).Container.Items)
            {
                if (item.Definition == hat.Definition)
                {
                    picked = item;
                }
            }

            yield return new WaitForSeconds(0.05f);
            window.QuickTransfer(picked);
            Assert.That(picked.ContainerId, Is.EqualTo(STASH));
            Assert.That(controller.ItemSounds.LastVolume, Is.EqualTo(InventoryItemSounds.PUT_IN_BOX_VOLUME), "putting into the stash must play snd_on_take at 0.75");
            yield return new WaitForSeconds(0.05f);
            window.QuickTransfer(picked);
            Assert.That(picked.ContainerId, Is.EqualTo(BACKPACK));
            Assert.That(controller.ItemSounds.LastVolume, Is.EqualTo(InventoryItemSounds.TAKE_VOLUME), "taking from the stash must play snd_on_take at 1.0");
            window.SoundPlayed -= sounds.Add;
            yield return Tap(_keyboard.tabKey);
        }

        [UnityTest]
        public IEnumerator TestWeaponAugCanBePickedUpAndEquipped()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSeconds(1.0f);

            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            WorldItemInteractor interactor = Object.FindAnyObjectByType<WorldItemInteractor>();
            InventoryWindow window = controller.Window;
            WorldItem aug = null;

            foreach (WorldItem candidate in Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
            {
                if (candidate.Definition != null && candidate.Definition.name == "wpn_aug_a3")
                {
                    aug = candidate;
                }
            }

            Assert.That(aug, Is.Not.Null, "the test AUG is not placed in the scene");
            ItemDefinition definition = aug.Definition;
            Assert.That(definition.IsCategory("Weapon"), Is.True);
            Assert.That(definition.Size, Is.EqualTo(new Vector2Int(5, 2)), "AUG A3 grid size must come from GRA inv_grid_width/height");
            Assert.That(definition.CanGenerateIcon, Is.True, "weapon icons must be generated, not imported");
            Assert.That(definition.Description, Does.Contain("<color=#5D0074> • <color=#8C8C8C>"), "module bullets must keep GRA d_purple / ui_gray_2 colors");
            Assert.That(definition.Description, Does.Not.Contain("%c["));
            WeaponStatsComponent stats = definition.GetComponent<WeaponStatsComponent>();
            Assert.That(stats, Is.Not.Null);
            Assert.That(stats.Section, Is.EqualTo("wpn_aug_a3"));
            Assert.That(stats.SilencerStatus, Is.EqualTo(WeaponAddonStatus.Attachable));
            Assert.That(stats.MagazineSize, Is.EqualTo(30));
            WeaponHudComponent hud = definition.GetComponent<WeaponHudComponent>();
            Assert.That(hud != null && hud.HudModel != null && hud.WeaponMotions != null && hud.HandsMotions != null, Is.True, "IWP HUD model and motions are missing");
            Assert.That(hud.AttachmentBones, Does.Contain(WeaponHudComponent.SCOPE_BONE).And.Contain(WeaponHudComponent.SILENCER_BONE));
            Assert.That(definition.GetComponent<ItemTakeSoundComponent>(), Is.Not.Null, "AUG has no snd_on_take sound");
            Assert.That(aug.GetComponent<WorldItemImpactSound>(), Is.Not.Null);
            Assert.That(interactor.TryPickUp(aug), Is.True);
            ItemInstance item = null;

            foreach (ItemInstance candidate in window.FindGrid(BACKPACK).Container.Items)
            {
                if (candidate.Definition == definition)
                {
                    item = candidate;
                }
            }

            Assert.That(item, Is.Not.Null, "the AUG did not reach the backpack");
            InventoryItemView view = null;

            foreach (InventoryItemView candidate in window.FindGrid(BACKPACK).ItemViews.Values)
            {
                view = candidate.Item == item ? candidate : view;
            }

            Assert.That(view, Is.Not.Null);
            Assert.That(view.CountText, Does.StartWith("<size=16><color=#F0D9B6FF>3.9"), "stack weight must be shown in the top-left corner with the HudMessage color");
            window.EquipItem(item);
            yield return WaitForEquipmentAnimations(controller);
            Assert.That(item.ContainerId, Is.EqualTo(EQUIPMENT));
            Assert.That(item.Location.SlotId, Is.EqualTo("Primary"));
        }

        [UnityTest]
        public IEnumerator GraWeaponAddonsHaveModelsAndGeneratedIcons()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;

            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            var addons = new Dictionary<string, ItemDefinition>();

            foreach (ItemDefinition definition in controller.Inventory.Database.Definitions)
            {
                if (definition.IsCategory("Attachment"))
                {
                    addons[definition.name] = definition;
                }
            }

            Assert.That(addons.Count, Is.GreaterThanOrEqualTo(140), "GRA addons used in the game were not imported");

            foreach (ItemDefinition addon in addons.Values)
            {
                Assert.That(addon.GetComponent<WeaponAddonComponent>(), Is.Not.Null, addon.name);
                Assert.That(addon.VisualPrefab, Is.Not.Null, addon.name + " has no GRA model");
                Assert.That(addon.IconOverride, Is.Not.Null, addon.name + " must use the GRA HD icon");
                Assert.That(addon.IconOverride.name, Is.EqualTo(addon.name));
                Assert.That(addon.CanGenerateIcon, Is.False, addon.name);
            }

            Assert.That(addons["wpn_sil_nato"].Size, Is.EqualTo(new Vector2Int(1, 1)), "GRA 2x2 must become 1x1");
            Assert.That(addons["wpn_sil_nato"].GetComponent<WeaponAddonComponent>().Kind, Is.EqualTo(WeaponAddonKind.Silencer));
            Assert.That(addons["acog"].GetComponent<WeaponAddonComponent>().Kind, Is.EqualTo(WeaponAddonKind.Scope));
            foreach (string scope in new[] { "pso1m21", "1p59", "t12", "1pn93", "leupold", "specter" })
            {
                Assert.That(addons.ContainsKey(scope), Is.True, scope + " (optics / thermal) is missing");
            }

            Assert.That(addons["ekp8_18"].VisualPrefab.name, Is.EqualTo("ekp8_18"), "EKP-8-18 must use its own MAS model, not the EKP-8-02 one");

            foreach (Renderer renderer in addons["wpn_sil_spec_isg"].VisualPrefab.GetComponentsInChildren<Renderer>(false))
            {
                Assert.That(renderer.sharedMaterial.name, Is.EqualTo("wpn_silencer_hexagon12"), "wpn_sil_spec_isg must show only the silencer, not the whole rifle");
            }

            foreach (string scope in new[] { "0kp2", "rmr", "exps3" })
            {
                foreach (Renderer renderer in addons[scope].VisualPrefab.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.That(renderer.sharedMaterial.name, Is.Not.EqualTo("wpn_case_med"), scope + " still uses the textureless case model");
                }
            }

            Assert.That(addons["wpn_addon_grenade_launcher_m203"].Size, Is.EqualTo(new Vector2Int(2, 1)), "GRA 4x2 must become 2x1");
            WeaponStatsComponent aug = null;

            foreach (ItemDefinition definition in controller.Inventory.Database.Definitions)
            {
                if (definition.name == "wpn_aug_a3")
                {
                    aug = definition.GetComponent<WeaponStatsComponent>();
                }
            }

            Assert.That(addons.ContainsKey(aug.SilencerSection), Is.True);
            Assert.That(addons.ContainsKey(aug.GrenadeLauncherSection), Is.True);

            foreach (string scope in aug.Scopes)
            {
                Assert.That(addons.ContainsKey(scope), Is.True, "AUG scope " + scope + " is missing");
            }

            yield return new WaitForSeconds(1.0f);
            var placed = new HashSet<ItemDefinition>();
            WorldItem silencer = null;

            foreach (WorldItem worldItem in Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
            {
                if (worldItem.Definition != null && worldItem.Definition.IsCategory("Attachment"))
                {
                    placed.Add(worldItem.Definition);
                    silencer = worldItem.Definition.name == "wpn_sil_nato" ? worldItem : silencer;
                }
            }

            Assert.That(placed.Count, Is.EqualTo(addons.Count), "every addon must lie in the world");
            Assert.That(silencer, Is.Not.Null);
            Assert.That(silencer.transform.position.y, Is.GreaterThan(-5f), "the silencer fell through the ground");
            Assert.That(Object.FindAnyObjectByType<WorldItemInteractor>().TryPickUp(silencer), Is.True);
            bool inBackpack = false;

            foreach (ItemInstance item in controller.Window.FindGrid(BACKPACK).Container.Items)
            {
                inBackpack |= item.Definition.name == "wpn_sil_nato";
            }

            Assert.That(inBackpack, Is.True, "the picked silencer did not reach the backpack");
        }

        private static ItemInstance AddItem(InventoryModel model, InventoryWindowController controller, InventoryWindow window, string definitionName)
        {
            ItemDefinition found = null;

            foreach (ItemDefinition definition in controller.Inventory.Database.Definitions)
            {
                if (definition.name == definitionName)
                {
                    found = definition;
                }
            }

            Assert.That(found, Is.Not.Null, definitionName + " is missing in the item database");
            ItemInstance item = model.CreateItem(found);
            Assert.That(model.TryAddAuto(item, window.PrimaryContainerId).Success, Is.True, "no room for " + definitionName);
            return item;
        }

        private static IEnumerator WaitForEquipmentAnimations(InventoryWindowController controller)
        {
            float timeout = Time.time + 30.0f;

            while (controller.EquipmentAnimations != null && controller.EquipmentAnimations.IsBusy && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(controller.EquipmentAnimations == null || controller.EquipmentAnimations.IsBusy == false, Is.True, "equipment animations did not finish");
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
