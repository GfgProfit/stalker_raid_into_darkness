using System.Collections;
using Inventory;
using Inventory.IconGeneration;
using Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class OutdoorsSceneScopeTests : OutdoorsSceneInputTestFixture
    {
        protected override string SceneName => "TEST_SCENE";

        public override void Setup()
        {
            base.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        [UnityTest]
        public IEnumerator EquippedScopeChangesWithoutRebuildingHandsAndSurvivesDropAndPickup()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController inventory = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryModel model = inventory.Inventory.Model;
            InventoryWindow window = inventory.Window;
            FirstPersonWeaponController controller = inventory.Player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = Add(inventory, "wpn_aug_a3");
            ItemInstance first = Add(inventory, "acog");
            ItemInstance second = Add(inventory, "leupold");
            ItemInstance silencer = Add(inventory, "wpn_sil_nato");
            first.SetState("condition", "0.68");
            Assert.That(model.TryEquip(weapon.InstanceId, window.EquipmentContainerId).Success, Is.True);
            Assert.That(controller.Select(FirstPersonWeaponController.PRIMARY_SLOT), Is.True);
            float timeout = Time.time + 6f;

            while (controller.State != FirstPersonWeaponState.Ready && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(controller.State, Is.EqualTo(FirstPersonWeaponState.Ready));
            GameObject original = controller.Model;
            Assert.That(inventory.WeaponAttachments.Apply(first, weapon).Success, Is.True);
            Assert.That(controller.Model, Is.SameAs(original));
            AssertScope(original, first.Definition);
            Assert.That(controller.AimOffsetPosition, Is.EqualTo(Vector3.zero));
            Assert.That(inventory.WeaponAttachments.Install(silencer, weapon).Success, Is.True);
            Assert.That(controller.IsSilenced, Is.True);
            AssertScope(original, first.Definition);
            Assert.That(inventory.WeaponAttachments.Apply(second, weapon).Success, Is.True);
            Assert.That(controller.Model, Is.SameAs(original));
            AssertScope(original, second.Definition);
            Assert.That(model.GetItem(first.InstanceId).GetState("condition"), Is.EqualTo("0.68"));
            Assert.That(inventory.WeaponAttachments.DetachScope(weapon).Success, Is.True);
            AssertScope(original, null);
            Assert.That(controller.AimOffsetPosition, Is.EqualTo(controller.ActiveWeapon.AimOffsetPosition));
            Assert.That(inventory.WeaponAttachments.Install(model.GetItem(first.InstanceId), weapon).Success, Is.True);
            window.DropItemNow(weapon);
            yield return null;
            WorldItem dropped = null;

            foreach (WorldItem candidate in Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
            {
                if (candidate.Instance == weapon)
                {
                    dropped = candidate;
                }
            }

            Assert.That(dropped, Is.Not.Null);
            AssertScope(dropped.gameObject, first.Definition);
            Assert.That(dropped.GetComponentsInChildren<WorldItem>(true).Length, Is.EqualTo(1));
            Assert.That(dropped.GetComponentsInChildren<Rigidbody>(true).Length, Is.EqualTo(1));
            Assert.That(Object.FindAnyObjectByType<WorldItemInteractor>().TryPickUp(dropped), Is.True);
            Assert.That(inventory.WeaponAttachments.GetScope(model.GetItem(weapon.InstanceId)), Is.SameAs(first.Definition));
        }

        private static ItemInstance Add(InventoryWindowController inventory, string section)
        {
            foreach (ItemDefinition definition in inventory.Inventory.Database.Definitions)
            {
                if (DefinitionSection(definition) != section)
                {
                    continue;
                }

                ItemInstance item = inventory.Inventory.Model.CreateItem(definition);
                Assert.That(inventory.Inventory.Model.TryAddAuto(item, inventory.Window.PrimaryContainerId, false).Success, Is.True, section);
                return item;
            }

            Assert.Fail("Missing definition: " + section);
            return null;
        }

        [UnityTest]
        public IEnumerator EquipmentIconChangesForScopeReplacementAndRestoresAfterDetach()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController inventory = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = inventory.Window;
            InventoryModel model = inventory.Inventory.Model;
            ItemInstance weapon = Add(inventory, "wpn_aug_a3");
            ItemInstance first = Add(inventory, "acog");
            ItemInstance second = Add(inventory, "leupold");
            Assert.That(model.TryEquip(weapon.InstanceId, window.EquipmentContainerId).Success, Is.True);
            inventory.Open();
            yield return null;
            var equipment = window.FindGrid(weapon.ContainerId) as InventoryEquipmentPanel;
            Assert.That(equipment, Is.Not.Null);
            InventoryEquipmentSlot slot = equipment.GetSlotView(weapon.Location.SlotId);
            InventoryItemView view = equipment.GetItemView(weapon);
            yield return WaitForIcon(view);
            Sprite original = slot.Icon.sprite;
            Assert.That(original, Is.Not.Null);
            Assert.That(inventory.WeaponAttachments.Install(first, weapon).Success, Is.True);
            yield return WaitForIcon(view);
            Sprite scoped = slot.Icon.sprite;
            Assert.That(scoped, Is.Not.Null);
            Assert.That(scoped, Is.Not.SameAs(original));
            Assert.That(inventory.WeaponAttachments.Install(second, weapon).Success, Is.True);
            yield return WaitForIcon(view);
            Assert.That(slot.Icon.sprite, Is.Not.Null);
            Assert.That(slot.Icon.sprite, Is.Not.SameAs(scoped));
            Assert.That(slot.Icon.sprite, Is.Not.SameAs(original));
            Assert.That(inventory.WeaponAttachments.DetachScope(weapon).Success, Is.True);
            yield return WaitForIcon(view);
            Assert.That(slot.Icon.sprite, Is.SameAs(original));
        }

        private static IEnumerator WaitForIcon(InventoryItemView view)
        {
            float timeout = Time.time + 10f;

            while (view.IsIconLoading && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(view.IsIconLoading, Is.False);
        }

        private static void AssertScope(GameObject root, ItemDefinition definition)
        {
            int active = 0;
            GameObject standard = root.GetComponentInChildren<WeaponScopeVisuals>(true).DefaultScope;
            Assert.That(standard, Is.Not.Null);
            Assert.That(standard.name, Is.EqualTo("def_sight"));
            Assert.That(standard.activeSelf, Is.EqualTo(definition == null));

            foreach (ItemIconVariantPart part in root.GetComponentsInChildren<ItemIconVariantPart>(true))
            {
                if (part.Group != WeaponScopeVisuals.SCOPE_GROUP)
                {
                    continue;
                }

                Assert.That(part.gameObject.activeSelf, Is.EqualTo(part.Part == definition), part.Part.name);
                active += part.gameObject.activeSelf ? 1 : 0;
            }

            Assert.That(active, Is.EqualTo(definition != null ? 1 : 0));
        }
    }
}
