using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.IconGeneration;
using RaidIntoDarkness.Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class OutdoorsSceneInventoryTests : OutdoorsSceneInputTestFixture
    {
        private const string SCENE = "TEST_SCENE";
        private const string BACKPACK = "Backpack";
        private const string STASH = "Stash";
        private const string EQUIPMENT = "Equipment";
        private const string NATO_SILENCER = "wpn_sil_nato";
        private const string GP25 = "wpn_addon_grenade_launcher_definition";
        private const double ICON_TIMEOUT_SECONDS = 20d;

        private Keyboard _keyboard;
        private Mouse _mouse;

        public override void Setup()
        {
            base.Setup();
            InputSystem.runInBackground = Application.runInBackground;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        [UnityTest]
        public IEnumerator ReopenedInventoryShowsStaminaWhenItReturnsToAnEarlierValue()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindAnyObjectByType<InventoryWindowController>();
            var player = Object.FindAnyObjectByType<StalkerPlayerController>();
            var panel = Object.FindAnyObjectByType<InventoryStatusPanel>(FindObjectsInactive.Include);
            Assert.That(controller, Is.Not.Null);
            Assert.That(player, Is.Not.Null);
            Assert.That(panel, Is.Not.Null);

            StalkerActorCondition condition = player.Motor.Condition;
            float capacity = condition.PowerCapacity;
            condition.SetMaxPower(capacity);
            var changed = typeof(StalkerActorCondition).GetField(nameof(StalkerActorCondition.StatChanged), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(changed, Is.Not.Null);
            int SubscriberCount() => (changed.GetValue(condition) as System.Delegate)?.GetInvocationList().Length ?? 0;

            int closedSubscribers = SubscriberCount();
            controller.Open();
            Assert.That(controller.IsOpen, Is.True);
            Assert.That(SubscriberCount(), Is.EqualTo(closedSubscribers + 1));
            condition.SetPower(capacity * 0.7f);

            for (int i = 0; i < 2; i++)
            {
                Assert.That(panel.GetValue(InventoryWindowController.STATUS_POWER), Is.EqualTo(0.7f).Within(0.0001f));
                controller.Close();
                Assert.That(SubscriberCount(), Is.EqualTo(closedSubscribers));
                condition.SetPower(capacity * 0.3f);
                Assert.That(panel.GetValue(InventoryWindowController.STATUS_POWER), Is.EqualTo(0.7f).Within(0.0001f));

                controller.Open();
                Assert.That(SubscriberCount(), Is.EqualTo(closedSubscribers + 1));
                Assert.That(panel.GetValue(InventoryWindowController.STATUS_POWER), Is.EqualTo(0.3f).Within(0.0001f));
                Assert.That(panel.GetMaximum(InventoryWindowController.STATUS_POWER), Is.EqualTo(1f));
                condition.SetPower(capacity * 0.7f);
                Assert.That(panel.GetValue(InventoryWindowController.STATUS_POWER), Is.EqualTo(0.7f).Within(0.0001f), "returning to the previously published value must update the reopened panel");
            }

            int staminaNotifications = 0;
            void CountStamina(StalkerStatKind kind)
            {
                if (kind == StalkerStatKind.Stamina)
                {
                    staminaNotifications++;
                }
            }

            condition.StatChanged += CountStamina;
            try
            {
                condition.SetPower(capacity * 0.7f);
                condition.SetPower(capacity * 0.6f);
                condition.SetPower(capacity * 0.6f);
                Assert.That(staminaNotifications, Is.EqualTo(1));
            }
            finally
            {
                condition.StatChanged -= CountStamina;
                controller.Close();
            }

            Assert.That(SubscriberCount(), Is.EqualTo(closedSubscribers));
        }

        [UnityTest]
        public IEnumerator ExoskeletonShowsBackpackLockAndPatchRemovalJoinsExistingStack()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemDefinition outfitDefinition = controller.Inventory.Database.Definitions.Single(definition => definition.GetComponent<EquipmentStatsComponent>()?.Section == "dolg_exo_outfit");
            ItemDefinition patchDefinition = controller.Inventory.Database.Definitions.Single(definition => definition.name == "dolg_patch");
            ItemInstance originalOutfit = EquipmentStatsFormatter.FindEquipped(window, EquipmentStatsFormatter.OUTFIT_CATEGORY);
            if (originalOutfit != null)
            {
                Assert.That(model.TryRemove(originalOutfit.InstanceId).Success, Is.True);
            }

            ItemInstance outfit = model.CreateItem(outfitDefinition);
            Assert.That(model.TryAddAuto(outfit, window.PrimaryContainerId).Success, Is.True);
            Assert.That(model.TryEquip(outfit.InstanceId, window.EquipmentContainerId).Success, Is.True);
            controller.Open();
            yield return null;
            var panel = (InventoryEquipmentPanel)window.FindGrid(EQUIPMENT);
            InventoryEquipmentSlot backpackSlot = panel.GetSlotView(SlotId.Backpack);
            Assert.That(backpackSlot.LockedSprite, Is.Not.Null);
            Assert.That(backpackSlot.LockedSprite, Is.SameAs(panel.GetSlotView(SlotId.Head).LockedSprite));
            Assert.That(backpackSlot.IsLockVisible, Is.True);

            var context = new InventoryActionContext(window, outfit, panel);
            IInventoryItemAction action = window.Actions.Find(InventoryWindowController.OUTFIT_PATCH_ACTION);
            Assert.That(action.IsVisible(context), Is.True);
            Assert.That(action.GetLabel(context), Is.EqualTo("Снять нашивку"));
            Assert.That(window.ActionStyleProvider(context, action).Icon.name, Is.EqualTo("ui_cmo_patch"));
            Assert.That(window.CollectDescription(outfit), Does.Not.Contain("Нашивка группировки отсутствует"));
            InventoryContainer bag = model.GetContainer(window.PrimaryContainerId);
            float capacity = bag.MaxWeight;
            bag.MaxWeight = bag.TotalWeight;
            action.Execute(context);
            Assert.That(controller.IsOpen, Is.True);
            Assert.That(controller.EquipmentAnimations.IsBusy, Is.False);
            Assert.That(EquipmentAnimationQueue.IsPatchRemoved(outfit), Is.False);
            Assert.That(bag.Items.Any(item => item.Definition == patchDefinition), Is.False);
            bag.MaxWeight = capacity;

            action.Execute(context);
            Assert.That(controller.IsOpen, Is.False);
            Assert.That(controller.Player.HudAnimator.Current, Is.SameAs(controller.Player.HudAnimator.Library.FindPatch("dolg", false, true)));
            controller.Player.HudAnimator.Cancel();
            yield return WaitForEquipmentAnimations(controller);
            Assert.That(EquipmentAnimationQueue.IsPatchRemoved(outfit), Is.False);
            Assert.That(bag.Items.Any(item => item.Definition == patchDefinition), Is.False);

            ItemInstance patch = model.CreateItem(patchDefinition);
            Assert.That(model.TryAddAuto(patch, bag.Id).Success, Is.True);
            controller.Open();
            action.Execute(context);
            yield return WaitForEquipmentAnimations(controller);
            Assert.That(EquipmentAnimationQueue.IsPatchRemoved(outfit), Is.True);
            Assert.That(window.CollectDescription(outfit), Does.Contain("Нашивка группировки отсутствует"));
            Assert.That(action.IsVisible(context), Is.False);
            action.Execute(context);
            Assert.That(bag.Items.Single(item => item.Definition == patchDefinition), Is.SameAs(patch));
            Assert.That(patch.Amount, Is.EqualTo(2));
            Assert.That(patch.Definition.FallbackIcon.name, Is.EqualTo("dolg_patch"));
            Assert.That(patch.Definition.Description, Does.Not.Contain("ХАРАКТЕРИСТИКИ"));
            Assert.That(patch.Definition.GetComponent<WorldItemComponent>().Prefab.Definition, Is.SameAs(patchDefinition));
            ItemInstance loosePatch = model.CreateItem(patchDefinition);
            Assert.That(model.TryAddAuto(loosePatch, bag.Id, false).Success, Is.True);
            Assert.That(model.TryMove(loosePatch.InstanceId, bag.Id, patch.Location.Position, false).Success, Is.True);
            Assert.That(bag.Items.Single(item => item.Definition == patchDefinition).Amount, Is.EqualTo(3));
            Assert.That(model.TryTransfer(outfit.InstanceId, window.PrimaryContainerId).Success, Is.True);
            controller.Open();
            Assert.That(backpackSlot.IsLockVisible, Is.False);
            Assert.That(EquipmentAnimationQueue.IsPatchRemoved(model.GetItem(outfit.InstanceId)), Is.True);
            Assert.That(model.GetItem(patch.InstanceId).Definition, Is.SameAs(patchDefinition));
            Assert.That(model.GetItem(patch.InstanceId).Amount, Is.EqualTo(3));
            controller.Close();
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
                if (candidate.name == "ammo_5.56x45_ss190")
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
                Vector3 center = view.RectTransform.TransformPoint(view.RectTransform.rect.center);
                Move(_mouse.position, RectTransformUtility.WorldToScreenPoint(null, center), queueEventOnly: true);
                yield return null;
                yield return null;
                Assert.That(window.HoveredItem, Is.SameAs(item));
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
        public IEnumerator HoverUsesInputRuntimeBackgroundFlagWhenEventSystemLosesFocus()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemDefinition definition = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ss190");
            ItemInstance item = model.CreateItem(definition);
            Assert.That(model.TryAddAuto(item, BACKPACK, mergeStacks: false).Success, Is.True);
            controller.Sorting.SetFavorite(definition, true);
            controller.Open();
            yield return null;

            EventSystem events = EventSystem.current;
            Assert.That(events, Is.Not.Null);
            var uiInput = events.currentInputModule as InputSystemUIInputModule;
            Assert.That(uiInput, Is.Not.Null);
            Assert.That(uiInput.point.action.controls.Any(control => control.device == _mouse), Is.True);
            Assert.That(InputSystem.settings.backgroundBehavior, Is.Not.EqualTo(InputSettings.BackgroundBehavior.IgnoreFocus));
            var focusHook = typeof(EventSystem).GetMethod("OnApplicationFocus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(focusHook, Is.Not.Null);
            bool previousFocus = events.isFocused;
            bool previousBackground = InputSystem.runInBackground;
            InventoryItemView view = window.FindGrid(BACKPACK).GetItemView(item);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.isActiveAndEnabled, Is.True);
            Color favorite = controller.Sorting.Decorate(item).Tint;
            Vector2 target = RectTransformUtility.WorldToScreenPoint(null, view.RectTransform.TransformPoint(view.RectTransform.rect.center));

            try
            {
                focusHook.Invoke(events, new object[] { true });
                InputSystem.runInBackground = true;
                Move(_mouse.position, new Vector2(5f, 5f), queueEventOnly: true);
                yield return null;
                yield return null;
                Assert.That(window.HoveredItem, Is.Not.SameAs(item));
                Assert.That(view.Background.color, Is.EqualTo(favorite));

                InputSystem.runInBackground = false;
                focusHook.Invoke(events, new object[] { false });
                Move(_mouse.position, target, queueEventOnly: true);
                yield return null;
                yield return null;

                Assert.That(events.isFocused, Is.False);
                Assert.That(InputSystem.runInBackground, Is.False);
                Assert.That(_mouse.position.ReadValue(), Is.EqualTo(target));
                var rays = new List<RaycastResult>();
                events.RaycastAll(new PointerEventData(events) { position = target, pointerId = _mouse.deviceId }, rays);
                Assert.That(rays, Is.Not.Empty);
                Assert.That(rays[0].gameObject.GetComponentInParent<InventoryItemView>(), Is.SameAs(view));
                RaycastResult blocked = uiInput.GetLastRaycastResult(_mouse.deviceId);
                Assert.That(blocked.gameObject == null ? null : blocked.gameObject.GetComponentInParent<InventoryItemView>(), Is.Not.SameAs(view));
                Assert.That(window.HoveredItem, Is.Not.SameAs(item));
                Assert.That(view.Background.color, Is.EqualTo(favorite));

                InputSystem.runInBackground = true;
                yield return null;
                yield return null;

                Assert.That(events.isFocused, Is.False);
                Assert.That(InputSystem.runInBackground, Is.True);
                Assert.That(_mouse.position.ReadValue(), Is.EqualTo(target));
                RaycastResult processed = uiInput.GetLastRaycastResult(_mouse.deviceId);
                Assert.That(processed.gameObject, Is.Not.Null);
                Assert.That(processed.gameObject.GetComponentInParent<InventoryItemView>(), Is.SameAs(view));
                Assert.That(processed.screenPosition, Is.EqualTo(target));
                Assert.That(window.HoveredItem, Is.SameAs(item));
                Assert.That(view.Background.color.a, Is.GreaterThan(favorite.a));
            }
            finally
            {
                InputSystem.runInBackground = previousBackground;
                focusHook.Invoke(events, new object[] { previousFocus });
                controller.Close();
            }
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

            foreach (string section in new[] { "ammo_5.56x45_ss190", "wpn_aug_a3" })
            {
                ItemDefinition definition = null;

                foreach (ItemDefinition candidate in controller.Inventory.Database.Definitions)
                {
                    if (DefinitionSection(candidate) == section)
                    {
                        definition = candidate;
                    }
                }

                WorldItem dropped = interactor.Spawn(definition, 1);
                Assert.That(dropped, Is.Not.Null, section);
                Rigidbody body = dropped.GetComponent<Rigidbody>();
                Assert.That(Vector3.Distance(body.position, dropped.transform.position), Is.LessThan(0.0001f), section);

                foreach (Collider collider in dropped.GetComponentsInChildren<Collider>())
                {
                    Assert.That(Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation, character, character.transform.position, character.transform.rotation, out _, out _), Is.False, section);
                }

                Vector3 position = dropped.transform.position;
                yield return new WaitForSeconds(0.2f);
                Assert.That(Vector3.ProjectOnPlane(dropped.transform.position - position, Vector3.up).magnitude, Is.LessThan(0.1f), section);
                Assert.That(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude, Is.LessThan(0.5f), section);
                Object.Destroy(dropped.gameObject);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator DragDropAreaFadesAndDropsOnlyWithStashClosed()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            controller.Player.CameraController.SetLook(0f, 90f);
            controller.Open();
            yield return null;

            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            CanvasGroup area = window.Content.GetComponentsInChildren<CanvasGroup>(true).Single(group => group.name == "Drop Item Area");
            var areaRect = (RectTransform)area.transform;
            Vector2 dropPoint = RectTransformUtility.WorldToScreenPoint(null, areaRect.TransformPoint(areaRect.rect.center));
            ItemDefinition definition = controller.Inventory.Database.Definitions.First(item => DefinitionSection(item) == "ammo_5.56x45_ss190");
            ItemInstance item = model.CreateItem(definition, 4);
            Assert.That(model.TryAddAuto(item, BACKPACK, mergeStacks: false).Success, Is.True);
            InventoryGridViewBase bag = window.FindGrid(BACKPACK);
            Vector2 start = RectTransformUtility.WorldToScreenPoint(null, bag.GetItemView(item).RectTransform.position);

            Assert.That(area.alpha, Is.Zero);
            Assert.That(area.blocksRaycasts, Is.False);
            Assert.That(window.BeginDrag(item, bag, item.Amount, start, null), Is.True);
            Assert.That(area.alpha, Is.Zero);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(area.alpha, Is.EqualTo(1f));
            window.CancelDrag();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(area.alpha, Is.Zero);
            Assert.That(model.ContainsItem(item), Is.True);

            Assert.That(window.BeginDrag(item, bag, item.Amount, start, null), Is.True);
            yield return new WaitForSecondsRealtime(0.25f);
            controller.Open(true);
            Assert.That(window.IsSecondaryOpen, Is.True);
            Assert.That(window.IsDragging, Is.False);
            Assert.That(area.alpha, Is.Zero);
            Assert.That(window.BeginDrag(item, bag, item.Amount, start, null), Is.True);
            window.UpdateDrag(dropPoint, null);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(area.alpha, Is.Zero);
            window.CompleteDrag();
            Assert.That(model.ContainsItem(item), Is.True);

            if (item.ContainerId != BACKPACK)
            {
                Assert.That(model.TryTransfer(item.InstanceId, BACKPACK).Success, Is.True);
            }

            controller.Open();
            Assert.That(window.IsSecondaryOpen, Is.False);
            start = RectTransformUtility.WorldToScreenPoint(null, bag.GetItemView(item).RectTransform.position);
            Assert.That(window.BeginDrag(item, bag, item.Amount, start, null), Is.True);
            yield return new WaitForSecondsRealtime(0.25f);
            window.UpdateDrag(dropPoint, null);
            Assert.That(window.CompleteDrag().Success, Is.True);
            Assert.That(model.ContainsItem(item), Is.False);
            WorldItem dropped = Object.FindObjectsByType<WorldItem>().Single(worldItem => worldItem.Instance == item);
            Assert.That(dropped.Amount, Is.EqualTo(4));
            Assert.That(dropped.GetComponent<Rigidbody>().angularVelocity.sqrMagnitude, Is.GreaterThan(0f));
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(area.alpha, Is.Zero);
            controller.Close();
        }

        [UnityTest]
        public IEnumerator DragIgnoresVisibleGridWithoutBoundContainer()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            controller.Open(true);
            yield return null;

            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            InventoryGridViewBase bag = window.FindGrid(BACKPACK);
            InventoryGridViewBase stash = window.FindGrid(STASH);
            ItemDefinition definition = controller.Inventory.Database.Definitions.First(item => DefinitionSection(item) == "ammo_5.56x45_ss190");
            ItemInstance dragged = model.CreateItem(definition);
            Assert.That(model.TryAddAuto(dragged, BACKPACK, mergeStacks: false).Success, Is.True);
            Vector2 start = RectTransformUtility.WorldToScreenPoint(null, bag.GetItemView(dragged).RectTransform.position);
            Vector2 target = RectTransformUtility.WorldToScreenPoint(null, stash.RectTransform.TransformPoint(stash.RectTransform.rect.center));
            Assert.That(window.BeginDrag(dragged, bag, dragged.Amount, start, null), Is.True);
            window.UpdateDrag(target, null);
            Assert.That(window.Drag.TargetGrid, Is.SameAs(stash));
            window.CancelDrag();

            Assert.That(model.RemoveContainer(STASH), Is.True);
            LogAssert.Expect(LogType.Error, $"Inventory grid '{stash.name}' references unconfigured container '{STASH}'.");
            window.Bind(model, window.IconProvider);
            yield return null;

            Assert.That(stash.isActiveAndEnabled, Is.True);
            Assert.That(stash.Container, Is.Null);
            Assert.That(window.BeginDrag(dragged, bag, dragged.Amount, start, null), Is.True);
            window.UpdateDrag(target, null);
            Assert.That(window.Drag.TargetGrid, Is.Null);
            Assert.That(window.CompleteDrag().Success, Is.False);
            Assert.That(dragged.ContainerId, Is.EqualTo(BACKPACK));
            controller.Close();
        }

        [UnityTest]
        public IEnumerator DroppingFromInventoryTossesItemsForwardAndDownWithSpin()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            controller.Player.CameraController.SetLook(0f, 90f);
            yield return null;
            controller.Open();
            yield return null;

            InventoryModel model = controller.Inventory.Model;
            Transform view = controller.Player.CameraController.Camera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(controller.Player.transform.forward, Vector3.up).normalized;
            CharacterController character = controller.Player.GetComponent<CharacterController>();

            foreach (string section in new[] { "ammo_5.56x45_ss190", "wpn_aug_a3" })
            {
                ItemDefinition definition = controller.Inventory.Database.Definitions.First(item => DefinitionSection(item) == section);
                ItemInstance item = model.CreateItem(definition);
                Assert.That(model.TryAddAuto(item, BACKPACK, mergeStacks: false).Success, Is.True, section);
                controller.Window.DropItem(item);
                Assert.That(model.ContainsItem(item), Is.False, section);
                WorldItem dropped = Object.FindObjectsByType<WorldItem>().Single(worldItem => worldItem.Instance == item);
                Rigidbody body = dropped.GetComponent<Rigidbody>();
                Vector3 initialPosition = body.position;
                Quaternion initialRotation = body.rotation;
                Assert.That(initialPosition.y, Is.LessThan(view.position.y), section);
                Assert.That(Vector3.Dot(body.linearVelocity, forward), Is.GreaterThan(1f), section);
                Assert.That(body.linearVelocity.y, Is.LessThan(0f), section);
                Assert.That(body.angularVelocity.magnitude, Is.GreaterThan(1f), section);

                Collider[] colliders = dropped.GetComponentsInChildren<Collider>();
                Bounds itemBounds = colliders[0].bounds;

                foreach (Collider collider in colliders)
                {
                    itemBounds.Encapsulate(collider.bounds);
                    Assert.That(Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation, character, character.transform.position, character.transform.rotation, out _, out _), Is.False, section);
                }

                Assert.That(Vector3.Dot(itemBounds.center - character.bounds.center, forward), Is.EqualTo(0.6f).Within(0.02f), section);
                yield return new WaitForSeconds(0.15f);
                Assert.That(Vector3.Dot(body.position - initialPosition, forward), Is.GreaterThan(0.1f), section);
                Assert.That(body.position.y, Is.LessThan(initialPosition.y), section);
                Assert.That(Quaternion.Angle(initialRotation, body.rotation), Is.GreaterThan(5f), section);
                Object.Destroy(dropped.gameObject);
                yield return null;
            }

            controller.Close();
        }

        [UnityTest]
        public IEnumerator InventoryDropAvoidsWallsAndKeepsItemWhenSurrounded()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            controller.Player.CameraController.SetLook(0f, 0f);
            yield return null;
            controller.Open();
            yield return null;

            InventoryModel model = controller.Inventory.Model;
            Transform view = controller.Player.CameraController.Camera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 dropCenter = view.position - Vector3.up * 0.3f;
            BoxCollider wall = CreateWall(forward, 0.52f);
            ItemDefinition definition = controller.Inventory.Database.Definitions.First(item => DefinitionSection(item) == "ammo_5.56x45_ss190");
            ItemInstance first = model.CreateItem(definition, 4);
            Assert.That(model.TryAddAuto(first, BACKPACK, mergeStacks: false).Success, Is.True);
            controller.Window.DropItem(first);
            WorldItem dropped = Object.FindObjectsByType<WorldItem>().Single(worldItem => worldItem.Instance == first);
            Assert.That(Vector3.Dot(dropped.transform.position - dropCenter, forward), Is.LessThan(0.42f), "The item crossed the wall during placement");

            foreach (Collider collider in dropped.GetComponentsInChildren<Collider>())
            {
                Assert.That(Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation, wall, wall.transform.position, wall.transform.rotation, out _, out _), Is.False, "The item started inside the wall");
            }

            Rigidbody body = dropped.GetComponent<Rigidbody>();
            Assert.That(body.maxDepenetrationVelocity, Is.EqualTo(2f));
            yield return new WaitForSeconds(0.2f);
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(6f), "Wall contact flung the item");

            CreateWall(-forward, 0.4f);
            CreateWall(right, 0.4f);
            CreateWall(-right, 0.4f);
            ItemInstance second = model.CreateItem(definition, 7);
            Assert.That(model.TryAddAuto(second, BACKPACK, mergeStacks: false).Success, Is.True);
            ItemLocation original = second.Location;
            controller.Window.DropItem(second);
            Assert.That(model.ContainsItem(second), Is.True);
            Assert.That(second.Location, Is.EqualTo(original));
            Assert.That(second.Amount, Is.EqualTo(7));
            Assert.That(Object.FindObjectsByType<WorldItem>().Any(worldItem => worldItem.Instance == second), Is.False);
            controller.Close();

            BoxCollider CreateWall(Vector3 direction, float distance)
            {
                var wallObject = new GameObject("Inventory drop test wall");
                wallObject.transform.SetPositionAndRotation(dropCenter + direction * distance, Quaternion.LookRotation(direction, Vector3.up));
                BoxCollider collider = wallObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(3f, 2f, 0.2f);
                return collider;
            }
        }

        [UnityTest]
        public IEnumerator LightweightInventoryDropsPlayTheirSurfaceImpact()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            controller.Player.CameraController.SetLook(0f, 0f);
            yield return null;
            controller.Open();
            yield return null;

            InventoryModel model = controller.Inventory.Model;
            ItemDefinition definition = controller.Inventory.Database.Definitions.First(item => DefinitionSection(item) == "ac10632");
            Transform view = controller.Player.CameraController.Camera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            GameObject plate = new GameObject("Inventory drop sound surface");
            plate.transform.SetPositionAndRotation(view.position + forward * 1.6f - Vector3.up * 0.95f, Quaternion.LookRotation(forward, Vector3.up));
            plate.AddComponent<BoxCollider>().size = new Vector3(1f, 0.1f, 2.1f);
            StalkerSurface surface = plate.AddComponent<StalkerSurface>();

            foreach (StalkerSurfaceType surfaceType in new[] { StalkerSurfaceType.Concrete, StalkerSurfaceType.Metal })
            {
                surface.Type = surfaceType;
                ItemInstance item = model.CreateItem(definition);
                Assert.That(model.TryAddAuto(item, BACKPACK, mergeStacks: false).Success, Is.True);
                controller.Window.DropItem(item);
                WorldItem dropped = Object.FindObjectsByType<WorldItem>().Single(worldItem => worldItem.Instance == item);
                WorldItemImpactSound impact = dropped.GetComponent<WorldItemImpactSound>();
                Assert.That(impact.Mass, Is.LessThan(1f));
                Assert.That(impact.PlayedCount, Is.Zero, "The impact clip played before the item hit a surface");
                float timeout = Time.time + 1.5f;

                while (impact.PlayedCount == 0 && Time.time < timeout)
                {
                    yield return null;
                }

                Assert.That(impact.PlayedCount, Is.EqualTo(1), $"{surfaceType}: the item fell without an impact sound");
                Assert.That(impact.LastSurface, Is.EqualTo(surfaceType));
                Assert.That(impact.LastClip, Is.Not.Null);
                Assert.That(impact.LastVolume, Is.InRange(0.2f, 1f));
                Assert.That(dropped.GetComponent<AudioSource>().isPlaying, Is.True);
                yield return new WaitForSeconds(0.3f);
                Assert.That(impact.PlayedCount, Is.EqualTo(1), "Minor bounces replayed the drop sound");
                Object.Destroy(dropped.gameObject);
                yield return null;
            }

            Object.Destroy(plate);
            controller.Close();
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
            Assert.That(Object.FindObjectsByType<InventoryWindowController>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<InventoryWindow>(FindObjectsInactive.Include).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include).Length, Is.EqualTo(1));
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
            Assert.That(equipment.GetSlot(SlotId.Primary), Is.Not.Null);
            Assert.That(equipment.GetSlot(SlotId.Secondary), Is.Not.Null);
            Assert.That(equipment.GetSlot(SlotId.Knife), Is.Not.Null);
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
            Assert.That(Object.FindObjectsByType<WorldItem>().Length, Is.GreaterThan(0), "The fixture spawned no world item to pick up");
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
            Vector2 requestedHover = RectTransformUtility.WorldToScreenPoint(null, pickedView.RectTransform.position);
            Vector2 pointerBeforeHover = _mouse.position.ReadValue();
            int frameBeforeHover = Time.frameCount;
            Move(_mouse.position, requestedHover, queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.That(pickedView.Background.color.a, Is.GreaterThan(favoriteColor.a), () => DescribeFavoriteHoverFailure(window, pickedView, requestedHover, pointerBeforeHover, frameBeforeHover));
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
                if (definition.GetComponent<EquipmentStatsComponent>()?.Section == "army_nosorog_outfit")
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

            Assert.That(nosorogValues, Is.EqualTo(new[] { "62%", "37%", "64.67%", "61.33%", "43%", "51%", "19%", "25.67%", "16.33%", "50.33%", "2", "+108 кг", "100 %", "98%", "72%" }), "army_nosorog_outfit must show its authored protection profile and equipment statistics");
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
            int worldBefore = Object.FindObjectsByType<WorldItem>().Length;
            window.DropItem(picked);
            yield return null;
            Assert.That(model.ContainsItem(picked), Is.False);
            Assert.That(Object.FindObjectsByType<WorldItem>().Length, Is.EqualTo(worldBefore + 1), "dropping did not spawn a world item");
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
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include).Length, Is.EqualTo(1));
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
            int worldBefore = Object.FindObjectsByType<WorldItem>().Length;
            window.DropItem(hat);
            Assert.That(sounds[sounds.Count - 1], Is.EqualTo(InventorySound.Drop), "dropping must play inv_drop");
            yield return null;
            WorldItem dropped = null;
            int droppedMatches = 0;

            foreach (WorldItem candidate in Object.FindObjectsByType<WorldItem>())
            {
                if (ReferenceEquals(candidate.Instance, hat))
                {
                    dropped = candidate;
                    droppedMatches++;
                }
            }

            Assert.That(droppedMatches, Is.EqualTo(1), "the dropped instance must have exactly one world object");
            Assert.That(Object.FindObjectsByType<WorldItem>().Length, Is.EqualTo(worldBefore + 1));
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
            ItemDefinition definition = FindDefinition(controller.Inventory.Database, "wpn_aug_a3");
            WorldItem aug = interactor.Spawn(definition, 1);
            Assert.That(aug, Is.Not.Null, "the AUG world prefab could not be placed in the test scene");
            Assert.That(aug.Definition, Is.SameAs(definition));
            Assert.That(definition.IsCategory("Weapon"), Is.True);
            Assert.That(definition.Size, Is.EqualTo(new Vector2Int(5, 2)), "AUG A3 grid size must come from GRA inv_grid_width/height");
            Assert.That(definition.CanGenerateIcon, Is.True, "weapon icons must be generated, not imported");
            Assert.That(definition.Description, Does.Contain("Steyr Arms").And.Contain("Picatinny"), "AUG must preserve its authored description");
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
            string tooltipDescription = new EquipmentStatsFormatter(window).Describe(item);
            Assert.That(tooltipDescription, Does.Contain("<color=#AAAAAA>" + definition.Description + "</color>"), "AUG tooltip must render its authored prose in the inventory description color");
            Assert.That(tooltipDescription, Does.Not.Contain("%c["));
            controller.Open();
            yield return null;
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
            Assert.That(item.Location.SlotId, Is.EqualTo(SlotId.Primary));
            controller.Close();
        }

        private string DescribeFavoriteHoverFailure(InventoryWindow window, InventoryItemView view, Vector2 requested, Vector2 before, int frameBefore)
        {
            EventSystem events = EventSystem.current;
            var uiInput = events != null ? events.currentInputModule as InputSystemUIInputModule : null;
            InputAction pointAction = uiInput != null ? uiInput.point?.action : null;
            Vector2 actual = _mouse.position.ReadValue();
            var raycasts = new List<RaycastResult>();
            if (events != null)
            {
                events.RaycastAll(new PointerEventData(events) { position = actual, pointerId = _mouse.deviceId }, raycasts);
            }

            Canvas canvas = view.GetComponentInParent<Canvas>();
            RectMask2D mask = view.GetComponentInParent<RectMask2D>();
            RectTransform rect = view.RectTransform;
            Vector2 currentCenter = RectTransformUtility.WorldToScreenPoint(canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null, rect.TransformPoint(rect.rect.center));
            string hits = string.Join(", ", raycasts.Take(4).Select(DescribeHoverHit));
            string moduleHit = uiInput != null ? DescribeHoverHit(uiInput.GetLastRaycastResult(_mouse.deviceId)) : "none";
            string controls = pointAction == null ? "none" : string.Join(", ", pointAction.controls.Select(control => control.path + "@" + control.device.deviceId));
            string input = $"Mouse {_mouse.deviceId}: {before}->{actual}, requested {requested}, current center {currentCenter}, current mouse {Mouse.current?.deviceId}, pointer {Pointer.current?.deviceId}/{Pointer.current?.position.ReadValue()}. Cursor {Cursor.lockState}, application focus {Application.isFocused}, EventSystem focus {(events != null ? events.isFocused : (bool?)null)}, run in background application/input {Application.runInBackground}/{InputSystem.runInBackground}, input background behavior {InputSystem.settings.backgroundBehavior}.";
            string viewState = $"Window open {window.IsOpen}, dragging {window.IsDragging}, modal {window.HasModalWidget}, hovered {window.HoveredItem?.InstanceId}, view active {view.isActiveAndEnabled}, item {view.Item?.InstanceId} in {view.Item?.ContainerId}, view bound {view.Grid != null && view.Grid.GetItemView(view.Item) == view}, canvas {canvas?.renderMode}/{canvas?.worldCamera?.name}, mask {mask?.canvasRect}.";
            return $"hovered favorite item is not highlighted. Frame {frameBefore}->{Time.frameCount}, screen {Screen.width}x{Screen.height}. {input} {viewState} Module {uiInput?.name}, point enabled {pointAction?.enabled}, controls [{controls}], input ray [{moduleHit}], manual rays [{hits}].";
        }

        private static string DescribeHoverHit(RaycastResult hit) => hit.gameObject != null ? $"{hit.gameObject.name} view={hit.gameObject.GetComponentInParent<InventoryItemView>()?.Item?.InstanceId} camera={hit.module?.eventCamera?.name} screen={hit.screenPosition}" : $"none screen={hit.screenPosition}";

        [UnityTest]
        public IEnumerator GraWeaponAddonsHaveModelsAndGeneratedIcons()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;

            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            Assert.That(controller, Is.Not.Null);
            ItemDatabase database = controller.Inventory.Database;
            var addons = new Dictionary<string, ItemDefinition>();
            var provider = new ItemIconCacheProvider();

            foreach (ItemDefinition definition in database.Definitions)
            {
                if (definition.IsCategory("Attachment"))
                {
                    addons[definition.name] = definition;
                }
            }

            Assert.That(addons.Count, Is.GreaterThanOrEqualTo(53), "GRA addons used in the game were not imported");

            foreach (ItemDefinition addon in addons.Values)
            {
                WeaponAddonComponent component = addon.GetComponent<WeaponAddonComponent>();
                Assert.That(component, Is.Not.Null, addon.name);
                Assert.That(addon.VisualPrefab, Is.Not.Null, addon.name + " has no GRA model");
                if (addon.CanGenerateIcon == false)
                {
                    Sprite configured = addon.IconOverride != null ? addon.IconOverride : addon.FallbackIcon;
                    var request = new ItemIconRequest(addon, null, addon.Width, addon.Height, ItemIconProfileType.Default);
                    Assert.That(configured, Is.Not.Null, addon.name + " has no configured icon source");
                    Assert.That(provider.TryGetIcon(request, out Sprite icon), Is.True, addon.name);
                    Assert.That(icon, Is.SameAs(configured), addon.name);
                }
            }

            ItemDefinition nato = addons[NATO_SILENCER];
            ItemDefinition gp25 = addons[GP25];
            Assert.That(nato.Size, Is.EqualTo(new Vector2Int(2, 1)), "NATO suppressor uses the authored two-cell footprint");
            Assert.That(nato.GetComponent<WeaponAddonComponent>().Kind, Is.EqualTo(WeaponAddonKind.Silencer));
            Assert.That(gp25.GetComponent<WeaponAddonComponent>().Kind, Is.EqualTo(WeaponAddonKind.GrenadeLauncher));
            Assert.That(addons["acog"].GetComponent<WeaponAddonComponent>().Kind, Is.EqualTo(WeaponAddonKind.Scope));
            foreach (string scope in new[] { "pso1m21", "1p59", "t12", "1pn93", "leupold", "specter" })
            {
                Assert.That(addons.ContainsKey(scope), Is.True, scope + " (optics / thermal) is missing");
            }

            foreach (string scope in new[] { "rmr", "exps3" })
            {
                foreach (Renderer renderer in addons[scope].VisualPrefab.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.That(renderer.sharedMaterial.name, Is.Not.EqualTo("wpn_case_med"), scope + " still uses the textureless case model");
                }
            }

            WeaponStatsComponent aug = null;

            foreach (ItemDefinition definition in database.Definitions)
            {
                if (DefinitionSection(definition) == "wpn_aug_a3")
                {
                    aug = definition.GetComponent<WeaponStatsComponent>();
                }
            }

            Assert.That(aug, Is.Not.Null);
            Assert.That(addons.ContainsKey(aug.SilencerSection), Is.True);
            Assert.That(aug.GrenadeLauncherStatus, Is.EqualTo(WeaponAddonStatus.None));
            Assert.That(aug.GrenadeLauncherSection, Is.Empty);

            foreach (string scope in aug.Scopes)
            {
                Assert.That(addons.ContainsKey(scope), Is.True, "AUG scope " + scope + " is missing");
            }

            ItemDefinition patch = database.Definitions.Single(definition => definition.name == "dolg_patch");
            Assert.That(patch.IconOverride, Is.Not.Null);
            yield return VerifyRequestedIcon(provider, patch, false);
            yield return VerifyRequestedIcon(provider, nato, true);
            yield return VerifyRequestedIcon(provider, gp25, true);

            WorldItemInteractor interactor = Object.FindAnyObjectByType<WorldItemInteractor>();
            Assert.That(interactor, Is.Not.Null);
            var fixtures = new List<WorldItem>();
            try
            {
                foreach (ItemDefinition addon in addons.Values)
                {
                    WorldItem placedItem = interactor.Spawn(addon, 1);
                    Assert.That(placedItem, Is.Not.Null, addon.name + " cannot spawn as a world item");
                    fixtures.Add(placedItem);
                    if (addon != nato)
                    {
                        Rigidbody body = placedItem.GetComponent<Rigidbody>();
                        Assert.That(body, Is.Not.Null, addon.name);
                        body.isKinematic = true;
                        body.detectCollisions = false;
                        placedItem.transform.position += Vector3.right * (fixtures.Count * 2f);
                    }
                }

                yield return new WaitForSeconds(1.0f);
                var placed = new HashSet<ItemDefinition>(fixtures.Where(worldItem => worldItem != null && worldItem.isActiveAndEnabled).Select(worldItem => worldItem.Definition));
                Assert.That(placed, Is.EquivalentTo(addons.Values), "every addon must be available as a world item");
                WorldItem silencer = fixtures.Single(worldItem => worldItem.Definition == nato);
                Assert.That(silencer.transform.position.y, Is.GreaterThan(-5f), "the silencer fell through the ground");
                Assert.That(interactor.TryPickUp(silencer), Is.True);
                InventoryContainer backpack = controller.Inventory.Model.GetContainer(BACKPACK);
                Assert.That(backpack.Items.Any(item => item.Definition == nato), Is.True, "the picked silencer did not reach the backpack");

                double deadline = Time.realtimeSinceStartupAsDouble + ICON_TIMEOUT_SECONDS;
                while ((ItemIconCache.IsPrewarmed == false || ItemIconCache.IsWorking) && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return null;
                }

                Assert.That(ItemIconCache.IsPrewarmed && ItemIconCache.IsWorking == false, Is.True, "scene icon prewarm and queued requests must finish before leaving PlayMode");
            }
            finally
            {
                foreach (WorldItem worldItem in fixtures)
                {
                    if (worldItem != null)
                    {
                        worldItem.gameObject.SetActive(false);
                        Object.Destroy(worldItem.gameObject);
                    }
                }
            }
        }

        private static IEnumerator VerifyRequestedIcon(IItemIconProvider provider, ItemDefinition definition, bool verifyGenerated)
        {
            using var cancellation = new CancellationTokenSource();
            var request = new ItemIconRequest(definition, null, definition.Width, definition.Height, ItemIconProfileType.Default);
            Sprite sprite = null;
            bool ready = false;
            double deadline = Time.realtimeSinceStartupAsDouble + ICON_TIMEOUT_SECONDS;
            provider.RequestIcon(request, result =>
            {
                sprite = result;
                ready = true;
            }, cancellation.Token);
            try
            {
                while (ready == false && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return null;
                }

                Assert.That(ready, Is.True, definition.name + " icon did not complete");
                Assert.That(sprite, Is.Not.Null, definition.name);
                Assert.That(sprite.texture, Is.Not.Null, definition.name);
                if (verifyGenerated == false)
                {
                    Assert.That(sprite, Is.SameAs(definition.IconOverride), definition.name);
                    yield break;
                }

                Assert.That(definition.CanGenerateIcon, Is.True, definition.name);
                Assert.That(sprite, Is.Not.SameAs(definition.FallbackIcon), definition.name + " returned the fallback sprite");
                Assert.That(sprite.name, Is.EqualTo(definition.name + " Runtime Icon"));
                int pixelsPerCell = ItemIconGeneratorSettings.LoadDefault().IconRenderScale * definition.Icon.PixelsPerCell;
                Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(definition.Width * pixelsPerCell, definition.Height * pixelsPerCell)));
                Assert.That(provider.TryGetIcon(request, out Sprite cached), Is.True, definition.name);
                Assert.That(cached, Is.SameAs(sprite), definition.name);

                AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(sprite.texture, 0, TextureFormat.RGBA32);
                while (readback.done == false && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return null;
                }

                Assert.That(readback.done && readback.hasError == false, Is.True, definition.name + " icon could not be read back");
                Assert.That(readback.GetData<Color32>().Any(pixel => pixel.a > 128), Is.True, definition.name + " icon has no visible pixels");
            }
            finally
            {
                cancellation.Cancel();
            }
        }

        private static ItemInstance AddItem(InventoryModel model, InventoryWindowController controller, InventoryWindow window, string definitionName)
        {
            ItemDefinition found = null;

            foreach (ItemDefinition definition in controller.Inventory.Database.Definitions)
            {
                if (DefinitionSection(definition) == definitionName)
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
