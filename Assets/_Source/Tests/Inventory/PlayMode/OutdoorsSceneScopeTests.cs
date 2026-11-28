using System.Collections;
using System.Linq;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.IconGeneration;
using RaidIntoDarkness.Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Inventory.Tests
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
        public IEnumerator MountedFirearmScopesUseTheStockSightDepth()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController inventory = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryModel model = inventory.Inventory.Model;
            FirstPersonWeaponController controller = inventory.Player.GetComponentInChildren<FirstPersonWeaponController>();
            Transform camera = inventory.Player.CameraController.Camera.transform;
            ItemInstance weapon = Add(inventory, "wpn_mp9");
            ItemInstance redDot = Add(inventory, "ac10632");
            ItemInstance holographic = Add(inventory, "e0t2");
            Assert.That(model.TryEquip(weapon.InstanceId, inventory.Window.EquipmentContainerId).Success, Is.True);
            int slot = weapon.Location.SlotId == SlotId.Primary ? FirstPersonWeaponController.PRIMARY_SLOT : FirstPersonWeaponController.SECONDARY_SLOT;
            Assert.That(controller.Select(slot), Is.True);
            DevTest aimOverride = controller.GetComponent<DevTest>();
            if (aimOverride != null)
            {
                aimOverride.enabled = false;
            }

            controller.DevelopmentAimOverride = true;
            float timeout = Time.time + 6f;
            while ((controller.State != FirstPersonWeaponState.Ready || controller.AimFactor < 0.999f) && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(controller.AimFactor, Is.EqualTo(1f).Within(0.001f));
            yield return new WaitForSeconds(0.35f);
            float stockDepth = camera.InverseTransformPoint(controller.HandsModel.transform.position).z;
            foreach (ItemInstance scope in new[] { redDot, holographic })
            {
                Assert.That(inventory.WeaponAttachments.Apply(scope, weapon).Success, Is.True);
                yield return null;
                Assert.That(Mathf.Abs(controller.AimOffsetPosition.z - controller.ActiveWeapon.AimOffsetPosition.z), Is.GreaterThan(0.01f));
                float currentDepth = camera.InverseTransformPoint(controller.HandsModel.transform.position).z;
                Assert.That(currentDepth, Is.EqualTo(stockDepth).Within(0.002f), scope.Definition.name);
            }

            Assert.That(inventory.WeaponAttachments.DetachScope(weapon).Success, Is.True);
            yield return null;
            Assert.That(camera.InverseTransformPoint(controller.HandsModel.transform.position).z, Is.EqualTo(stockDepth).Within(0.002f));
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
            Assert.That(model.TrySetItemStateInPlace(first, "condition", "0.68"), Is.True);
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
            Assert.That(controller.AimOffsetPosition, Is.EqualTo(first.Definition.GetComponent<WeaponAddonComponent>().AimOffsetPosition));
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

        [UnityTest]
        public IEnumerator EqualsCyclesOnlyTheMountedScopeReticle()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController inventory = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryModel model = inventory.Inventory.Model;
            FirstPersonWeaponController controller = inventory.Player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = Add(inventory, "wpn_aug_a3");
            ItemInstance scope = Add(inventory, "exps3");
            Assert.That(model.TryEquip(weapon.InstanceId, inventory.Window.EquipmentContainerId).Success, Is.True);
            Assert.That(controller.Select(FirstPersonWeaponController.PRIMARY_SLOT), Is.True);
            float timeout = Time.time + 6f;

            while (controller.State != FirstPersonWeaponState.Ready && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(controller.State, Is.EqualTo(FirstPersonWeaponState.Ready));
            Assert.That(inventory.WeaponAttachments.Apply(scope, weapon).Success, Is.True);
            WeaponReticleCycle reticle = controller.Model.GetComponentInChildren<WeaponReticleCycle>();
            Assert.That(reticle, Is.Not.Null);
            Assert.That(reticle.VariantCount, Is.EqualTo(6));
            Assert.That(reticle.VariantIndex, Is.Zero);
            Renderer renderer = reticle.GetComponent<Renderer>();
            int textureProperty = Shader.PropertyToID("_RedDotTex");
            var properties = new MaterialPropertyBlock();
            Keyboard keyboard = Keyboard.current;

            for (int step = 1; step <= reticle.VariantCount; step++)
            {
                Press(keyboard.equalsKey, queueEventOnly: true);
                yield return null;
                Assert.That(reticle.VariantIndex, Is.EqualTo(step % reticle.VariantCount));
                Release(keyboard.equalsKey, queueEventOnly: true);
                yield return null;
            }

            renderer.GetPropertyBlock(properties, 0);
            Assert.That(properties.GetTexture(textureProperty), Is.SameAs(renderer.sharedMaterial.GetTexture(textureProperty)));

            foreach (WeaponReticleCycle inactive in controller.Model.GetComponentsInChildren<WeaponReticleCycle>(true))
            {
                if (inactive != reticle)
                {
                    Assert.That(inactive.VariantIndex, Is.Zero);
                }
            }
        }

        [UnityTest]
        public IEnumerator FiringKeepsTheSelectedOpticalReticleAndRenderTarget()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController inventory = Object.FindAnyObjectByType<InventoryWindowController>();
            FirstPersonWeaponController controller = inventory.Player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = Add(inventory, "wpn_aug_a3");
            ItemInstance scope = Add(inventory, "specter");
            string ammoSection = weapon.Definition.GetComponent<WeaponStatsComponent>().AmmoClasses[0];
            ItemDefinition ammo = inventory.Inventory.Database.Definitions.Single(item => item.GetComponent<AmmoStatsComponent>()?.Section == ammoSection);
            InventoryModel model = inventory.Inventory.Model;
            ItemInstance loaded = weapon.CreateDraft();
            loaded.SetState(InventoryWeaponAmmoSource.AMMO_SECTION_STATE, ammoSection);
            loaded.SetState(InventoryWeaponAmmoSource.MAGAZINE_STATE, "2");
            Assert.That(model.TryReconfigureItem(weapon, loaded, weapon.Location.Position).Success, Is.True);
            Assert.That(model.TryEquip(weapon.InstanceId, inventory.Window.EquipmentContainerId).Success, Is.True);
            int slot = weapon.Location.SlotId == SlotId.Primary ? FirstPersonWeaponController.PRIMARY_SLOT : FirstPersonWeaponController.SECONDARY_SLOT;
            Assert.That(controller.Select(slot), Is.True);
            float timeout = Time.time + 6f;

            while (controller.State != FirstPersonWeaponState.Ready && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(controller.State, Is.EqualTo(FirstPersonWeaponState.Ready));
            Assert.That(inventory.WeaponAttachments.Install(scope, weapon).Success, Is.True);
            inventory.Player.CameraController.SetCursorLocked(true);
            DevTest aimOverride = controller.GetComponent<DevTest>();
            if (aimOverride != null)
            {
                aimOverride.enabled = false;
            }

            controller.DevelopmentAimOverride = true;
            WeaponReticleCycle reticle = controller.Model.GetComponentInChildren<WeaponReticleCycle>();
            Assert.That(reticle, Is.Not.Null);
            Assert.That(reticle.VariantCount, Is.GreaterThan(1));
            Renderer lens = reticle.GetComponent<Renderer>();
            int textureProperty = Shader.PropertyToID("_ReticleSdf");
            int targetProperty = Shader.PropertyToID("_RenderTexture");
            int availableProperty = Shader.PropertyToID("_ScopeAvailable");
            var properties = new MaterialPropertyBlock();
            timeout = Time.time + 6f;
            lens.GetPropertyBlock(properties, 0);

            while (properties.GetFloat(availableProperty) <= 0f && Time.time < timeout)
            {
                yield return null;
                lens.GetPropertyBlock(properties, 0);
            }

            Assert.That(properties.GetFloat(availableProperty), Is.GreaterThan(0f));
            Texture target = properties.GetTexture(targetProperty);
            Assert.That(target, Is.TypeOf<RenderTexture>());
            Press(Keyboard.current.equalsKey, queueEventOnly: true);
            yield return null;
            Release(Keyboard.current.equalsKey, queueEventOnly: true);
            Assert.That(reticle.VariantIndex, Is.EqualTo(1));
            lens.GetPropertyBlock(properties, 0);
            Texture selected = properties.GetTexture(textureProperty);
            Assert.That(selected, Is.Not.Null);
            string ownerId = model.GetContainer(weapon.ContainerId).OwnerId;
            float weaponWeight = weapon.UnitWeight;
            float carriedWeight = model.GetOwnerWeight(ownerId);
            bool inventoryNotified = false;
            void ObserveShot(InventoryChange change)
            {
                if (change.Kind == InventoryChangeKind.ItemStateChanged && change.Item == weapon)
                {
                    inventoryNotified = true;
                }
            }

            model.Changed += ObserveShot;
            Press(Mouse.current.leftButton, queueEventOnly: true);
            yield return null;
            Release(Mouse.current.leftButton, queueEventOnly: true);
            yield return null;
            model.Changed -= ObserveShot;
            Assert.That(controller.Magazine, Is.EqualTo(1));
            Assert.That(inventoryNotified, Is.True);
            Assert.That(weapon.UnitWeight, Is.EqualTo(weaponWeight - ammo.Weight).Within(0.0001f));
            Assert.That(model.GetOwnerWeight(ownerId), Is.EqualTo(carriedWeight - ammo.Weight).Within(0.0001f));
            Assert.That(reticle.VariantIndex, Is.EqualTo(1));
            lens.GetPropertyBlock(properties, 0);
            Assert.That(properties.GetTexture(targetProperty), Is.SameAs(target));
            Assert.That(properties.GetTexture(textureProperty), Is.SameAs(selected));
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
