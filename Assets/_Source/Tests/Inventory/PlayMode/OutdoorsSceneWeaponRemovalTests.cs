using RaidIntoDarkness.Inventory.Infrastructure;
using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;
using System.Collections;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class OutdoorsSceneWeaponRemovalTests : OutdoorsSceneInputTestFixture
    {
        private const string SCENE = "TEST_SCENE";
        private const string AUG = "wpn_aug_a3";
        private const string TARGET_CONTAINER = "WeaponRemovalTarget";
        private const float TIMEOUT = 15.0f;

        private InventoryWindowController _controller;
        private InventoryWindow _window;
        private InventoryModel _model;
        private FirstPersonWeaponController _weapons;

        public override void Setup()
        {
            base.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        private IEnumerator LoadScene()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSeconds(1.0f);
            _controller = Object.FindAnyObjectByType<InventoryWindowController>();
            Assert.That(_controller, Is.Not.Null);
            _controller.Open(true);
            _window = _controller.Window;
            _model = _controller.Inventory.Model;
            _weapons = _controller.Player.GetComponentInChildren<FirstPersonWeaponController>();
            Assert.That(_weapons, Is.Not.Null);
            Assert.That(_controller.EquipmentAnimations, Is.Not.Null);
            yield return WaitWhile(() => _controller.EquipmentAnimations.IsBusy);
        }

        [UnityTest]
        public IEnumerator HeldPrimaryAndSecondaryFinishHideBeforeMovingToBag()
        {
            yield return LoadScene();
            SlotId[] slots = { SlotId.Primary, SlotId.Secondary };

            for (int i = 0; i < slots.Length; i++)
            {
                ItemInstance item = AddWeapon(slots[i]);
                yield return Draw(i);
                _window.UnequipItem(item);
                _window.UnequipItem(item);
                AssertPendingHide(item, slots[i]);
                _controller.Close();
                yield return WaitWhile(() => _controller.EquipmentAnimations.IsBusy);
                Assert.That(item.ContainerId, Is.EqualTo(_window.PrimaryContainerId));
                Assert.That(_weapons.State, Is.EqualTo(FirstPersonWeaponState.Hidden));
                Assert.That(_weapons.Model, Is.Null);
                Assert.That(_weapons.GetSlotWeapon(i), Is.Null);
                Assert.That(_controller.EquipmentAnimations.IsInUse(item), Is.False);
                Assert.That(_model.TryRemove(item.InstanceId).Success, Is.True);
                _controller.Open(true);
            }
        }

        [UnityTest]
        public IEnumerator HeldWeaponKeepsExactOtherContainerTargetUntilHideFinishes()
        {
            yield return LoadScene();
            ItemInstance item = AddWeapon(SlotId.Primary);
            _model.AddContainer(new GridContainer(TARGET_CONTAINER, 16, 16));
            var origin = new Vector2Int(3, 4);
            bool rotated = item.Definition.AllowRotation;
            yield return Draw(FirstPersonWeaponController.PRIMARY_SLOT);
            EquipmentAnimationQueue queue = _controller.EquipmentAnimations;
            Assert.That(queue.HandleUnequip(item, new InventoryUnequipTarget(TARGET_CONTAINER, origin, rotated, false)), Is.True);
            Assert.That(queue.HandleUnequip(item, InventoryUnequipTarget.Auto(_window.PrimaryContainerId)), Is.True);
            AssertPendingHide(item, SlotId.Primary);
            yield return WaitWhile(() => queue.IsBusy);
            Assert.That(item.ContainerId, Is.EqualTo(TARGET_CONTAINER));
            Assert.That(item.Location.Position, Is.EqualTo(origin));
            Assert.That(item.Location.Rotated, Is.EqualTo(rotated));
            Assert.That(_weapons.State, Is.EqualTo(FirstPersonWeaponState.Hidden));
            Assert.That(_weapons.Model, Is.Null);
        }

        [UnityTest]
        public IEnumerator InactiveWeaponMovesImmediatelyAndDuplicateHeldDropSpawnsOnce()
        {
            yield return LoadScene();
            ItemInstance held = AddWeapon(SlotId.Primary);
            ItemInstance inactive = AddWeapon(SlotId.Secondary);
            yield return Draw(FirstPersonWeaponController.PRIMARY_SLOT);
            _window.UnequipItem(inactive);
            Assert.That(inactive.ContainerId, Is.EqualTo(_window.PrimaryContainerId));
            Assert.That(_controller.EquipmentAnimations.IsInUse(inactive), Is.False);
            Assert.That(_weapons.State, Is.EqualTo(FirstPersonWeaponState.Ready));
            int worldCount = CountWorldWeapons();
            int dropped = 0;
            System.Action<ItemInstance> onDropped = item => dropped += item == held ? 1 : 0;
            _window.ItemDropped += onDropped;

            try
            {
                _window.DropItem(held);
                _window.DropItem(held);
                _window.UnequipItem(held);
                AssertPendingHide(held, SlotId.Primary);
                Assert.That(dropped, Is.Zero);
                Assert.That(CountWorldWeapons(), Is.EqualTo(worldCount));
                yield return WaitWhile(() => _controller.EquipmentAnimations.IsBusy);
                Assert.That(_model.ContainsItem(held), Is.False);
                Assert.That(dropped, Is.EqualTo(1));
                Assert.That(CountWorldWeapons(), Is.EqualTo(worldCount + 1));
                Assert.That(_weapons.State, Is.EqualTo(FirstPersonWeaponState.Hidden));
                Assert.That(_weapons.Model, Is.Null);
            }
            finally
            {
                _window.ItemDropped -= onDropped;
            }
        }

        private ItemInstance AddWeapon(SlotId slotId)
        {
            ItemDefinition definition = null;

            foreach (ItemDefinition candidate in _controller.Inventory.Database.Definitions)
            {
                if (DefinitionSection(candidate) == AUG)
                {
                    definition = candidate;
                    break;
                }
            }

            Assert.That(definition, Is.Not.Null);
            SlottedContainer equipment = (SlottedContainer)_model.GetContainer(_window.EquipmentContainerId);
            SlotDefinition slot = equipment.GetSlot(slotId);
            Assert.That(slot, Is.Not.Null);
            ItemInstance item = _model.CreateItem(definition, 1);
            Assert.That(_model.TryAddAuto(item, _window.PrimaryContainerId, false).Success, Is.True);
            Assert.That(_window.Model, Is.SameAs(_model));
            InventoryResult preview = _model.CanMove(item.InstanceId, equipment.Id, slot.Bounds.position, false, false);
            _window.EquipItemNow(item, slot);
            Assert.That(item.ContainerId, Is.EqualTo(equipment.Id), $"Slot {slotId} at {slot.Bounds.position}, preview {preview.Failure}, status '{_window.LastStatus}'.");
            Assert.That(item.Location.SlotId, Is.EqualTo(slotId));
            return item;
        }

        private IEnumerator Draw(int slot)
        {
            Assert.That(_weapons.Select(slot), Is.True);
            yield return WaitWhile(() => _weapons.State != FirstPersonWeaponState.Ready);
            Assert.That(_weapons.ActiveSlot, Is.EqualTo(slot));
            Assert.That(_weapons.Model, Is.Not.Null);
        }

        private void AssertPendingHide(ItemInstance item, SlotId slotId)
        {
            Assert.That(item.ContainerId, Is.EqualTo(_window.EquipmentContainerId));
            Assert.That(item.Location.SlotId, Is.EqualTo(slotId));
            Assert.That(_controller.EquipmentAnimations.IsInUse(item), Is.True);
            Assert.That(_weapons.State, Is.EqualTo(FirstPersonWeaponState.Holstering));
            Assert.That(_weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.Holster));
            Assert.That(_weapons.Model, Is.Not.Null);
        }

        private static int CountWorldWeapons()
        {
            int count = 0;

            foreach (WorldItem item in Object.FindObjectsByType<WorldItem>())
            {
                if (DefinitionSection(item.Definition) == AUG)
                {
                    count++;
                }
            }

            return count;
        }

        private static IEnumerator WaitWhile(System.Func<bool> condition)
        {
            float timeout = TIMEOUT;

            while (condition() && timeout > 0.0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.That(condition(), Is.False, "timed out");
        }
    }
}
