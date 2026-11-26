using System.Collections;
using System.Linq;
using Inventory;
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
            Assert.That(controller.TryGetMuzzleRay(out Ray bore), Is.True);
            Transform hands = inventory.Player.Rig.Hands.transform;
            Vector3 rest = hands.localPosition;
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
            }
            finally
            {
                Object.Destroy(obstacle);
            }

            yield return new WaitForSeconds(0.75f);
            yield return new WaitForEndOfFrame();
            Assert.That(Vector3.Distance(hands.localPosition, rest), Is.LessThan(0.015f));
        }
    }
}
