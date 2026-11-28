using System.Collections;
using System.Linq;
using Inventory;
using Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class FirstPersonWeaponObstructionPlayModeTests : OutdoorsSceneInputTestFixture
    {
        protected override string SceneName => "TEST_SCENE";

        public override void Setup()
        {
            base.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        [UnityTest]
        public IEnumerator DroppingIntoTheWeaponPathFromInventoryKeepsTheWeaponIdle()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController inventory = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = inventory.Window;
            InventoryModel model = inventory.Inventory.Model;
            inventory.Player.CameraController.SetLook(0f, 20f);
            ItemDefinition weaponDefinition = inventory.Inventory.Database.Definitions.First(item => DefinitionSection(item) == "wpn_ak74");
            ItemInstance weapon = model.CreateItem(weaponDefinition);
            Assert.That(model.TryAddAuto(weapon, window.PrimaryContainerId, false).Success, Is.True);
            Assert.That(model.TryEquip(weapon.InstanceId, window.EquipmentContainerId).Success, Is.True);
            FirstPersonWeaponController controller = inventory.Player.GetComponentInChildren<FirstPersonWeaponController>();
            int slot = weapon.Location.SlotId == FirstPersonWeaponBinder.PRIMARY_SLOT ? FirstPersonWeaponController.PRIMARY_SLOT : FirstPersonWeaponController.SECONDARY_SLOT;
            Assert.That(controller.Select(slot), Is.True);
            float timeout = Time.time + 6f;

            while (controller.State != FirstPersonWeaponState.Ready && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(controller.State, Is.EqualTo(FirstPersonWeaponState.Ready));
            ItemDefinition dropDefinition = inventory.Inventory.Database.Definitions.First(item => item?.GetComponent<EquipmentStatsComponent>()?.Section == "dolg_outfit");
            weapon.SetState(InventoryWeaponAmmoSource.AMMO_SECTION_STATE, weaponDefinition.GetComponent<WeaponStatsComponent>().AmmoClasses[0]);
            inventory.Open();

            foreach (int rounds in new[] { 0, 1 })
            {
                weapon.SetState(InventoryWeaponAmmoSource.MAGAZINE_STATE, rounds.ToString());
                ItemInstance itemToDrop = model.CreateItem(dropDefinition);
                Assert.That(model.TryAddAuto(itemToDrop, window.PrimaryContainerId, false).Success, Is.True);
                yield return new WaitForSeconds(0.3f);
                yield return new WaitForEndOfFrame();
                Assert.That(controller.Magazine, Is.EqualTo(rounds));

                Transform hands = inventory.Player.Rig.Hands.transform;
                Transform view = inventory.Player.CameraController.Camera.transform;
                Vector3 idlePosition = view.InverseTransformPoint(hands.position);
                FirstPersonWeaponMotionKind idleMotion = controller.CurrentMotion;
                window.OpenContextMenu(itemToDrop, window.FindGrid(window.PrimaryContainerId), new Vector2(400f, 400f), null);
                Press(Mouse.current.leftButton, queueEventOnly: true);
                yield return null;
                Assert.That(inventory.Player.GameplayInputBlocked, Is.True);
                Assert.That(controller.CurrentMotion, Is.EqualTo(idleMotion));
                Release(Mouse.current.leftButton, queueEventOnly: true);
                Assert.That(window.ContextMenu.Invoke(window.Label(InventoryBuiltInActions.DROP, "Drop")), Is.True);
                Assert.That(model.ContainsItem(itemToDrop), Is.False);
                float maxRetreat = 0f;
                timeout = Time.time + 0.28f;

                while (Time.time < timeout)
                {
                    yield return new WaitForEndOfFrame();
                    maxRetreat = Mathf.Max(maxRetreat, idlePosition.z - view.InverseTransformPoint(hands.position).z);
                    Assert.That(controller.CurrentMotion, Is.EqualTo(idleMotion));
                    yield return null;
                }

                Assert.That(controller.Magazine, Is.EqualTo(rounds));
                Assert.That(maxRetreat, Is.LessThan(0.02f), "The dropped item made the weapon retract");
            }

            inventory.Close();
        }

        [UnityTest]
        public IEnumerator EquippedAks74nRetractsAtAFrontObstacleAndReturnsToIdle()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController inventory = Object.FindAnyObjectByType<InventoryWindowController>();
            Assert.That(inventory, Is.Not.Null);
            InventoryModel model = inventory.Inventory.Model;
            ItemDefinition definition = inventory.Inventory.Database.Definitions.FirstOrDefault(item => DefinitionSection(item) == "wpn_ak74");
            Assert.That(definition, Is.Not.Null);
            ItemInstance weapon = model.CreateItem(definition);
            Assert.That(model.TryAddAuto(weapon, inventory.Window.PrimaryContainerId, false).Success, Is.True);
            Assert.That(model.TryEquip(weapon.InstanceId, inventory.Window.EquipmentContainerId).Success, Is.True);

            FirstPersonWeaponController controller = inventory.Player.GetComponentInChildren<FirstPersonWeaponController>();
            int slot = weapon.Location.SlotId == FirstPersonWeaponBinder.PRIMARY_SLOT ? FirstPersonWeaponController.PRIMARY_SLOT : FirstPersonWeaponController.SECONDARY_SLOT;
            Assert.That(controller.Select(slot), Is.True);
            float timeout = Time.time + 6f;
            while (controller.State != FirstPersonWeaponState.Ready && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(controller.State, Is.EqualTo(FirstPersonWeaponState.Ready));
            yield return new WaitForSeconds(0.3f);
            yield return new WaitForEndOfFrame();
            Transform hands = inventory.Player.Rig.Hands.transform;
            Vector3 rest = hands.localPosition;
            inventory.Player.CameraController.SetCursorLocked(true);
            Press(Mouse.current.rightButton, queueEventOnly: true);
            yield return new WaitForSeconds(0.5f);
            yield return new WaitForEndOfFrame();
            Assert.That(controller.IsAiming, Is.True);
            Assert.That(controller.AimFactor, Is.GreaterThan(0.95f));
            Assert.That(controller.TryGetMuzzleRay(out Ray bore), Is.True);
            Transform pivot = controller.Model.transform.parent;
            float length = Vector3.Dot(bore.origin - pivot.position, bore.direction);
            Assert.That(length, Is.GreaterThan(0.15f));
            Camera camera = inventory.Player.CameraController.Camera;
            Vector3 worldRest = hands.position;
            GameObject obstacle = new GameObject("Weapon Obstruction Test");
            try
            {
                obstacle.transform.position = bore.GetPoint(-length * 0.56f);
                var collider = obstacle.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.24f, 0.24f, 0.05f);
                obstacle.transform.rotation = Quaternion.LookRotation(bore.direction, camera.transform.up);
                Physics.SyncTransforms();
                var probeRay = new Ray(bore.GetPoint(-length), bore.direction);
                Assert.That(collider.Raycast(probeRay, out _, length), Is.True);
                yield return new WaitForSeconds(0.5f);
                yield return new WaitForEndOfFrame();
                Assert.That(Vector3.Dot(worldRest - hands.position, camera.transform.forward), Is.GreaterThan(0.035f));
                Assert.That(controller.IsAiming, Is.False);
                Assert.That(controller.AimFactor, Is.LessThan(0.05f));

                collider.enabled = false;
                Physics.SyncTransforms();
                float remainHeldUntil = Time.time + 0.45f;
                while (Time.time < remainHeldUntil)
                {
                    yield return null;
                    Assert.That(controller.IsAiming, Is.False, "A forced exit must consume the held aim request.");
                    Assert.That(controller.AimFactor, Is.LessThan(0.05f));
                }

                collider.enabled = true;
                Physics.SyncTransforms();
                Release(Mouse.current.rightButton, queueEventOnly: true);
                yield return new WaitForSeconds(0.2f);
                Press(Mouse.current.rightButton, queueEventOnly: true);
                yield return new WaitForSeconds(0.35f);
                Assert.That(controller.IsAiming, Is.False);
            }
            finally
            {
                Object.Destroy(obstacle);
            }

            yield return new WaitForSeconds(0.4f);
            Assert.That(controller.IsAiming, Is.False, "An aim request denied at the wall must stay consumed.");
            Release(Mouse.current.rightButton, queueEventOnly: true);
            yield return null;
            Press(Mouse.current.rightButton, queueEventOnly: true);
            yield return new WaitForSeconds(0.5f);
            Assert.That(controller.IsAiming, Is.True);
            Assert.That(controller.AimFactor, Is.GreaterThan(0.95f));

            Release(Mouse.current.rightButton, queueEventOnly: true);
            yield return new WaitForSeconds(0.5f);
            yield return new WaitForEndOfFrame();
            Assert.That(Vector3.Distance(hands.localPosition, rest), Is.LessThan(0.015f));
        }
    }
}
