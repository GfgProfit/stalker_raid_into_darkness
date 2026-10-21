using System.Collections;
using Inventory;
using Inventory.IconGeneration;
using Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class OutdoorsSceneSilencerTests : InputTestFixture
    {
        private Keyboard _keyboard;
        private Mouse _mouse;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        [UnityTest]
        public IEnumerator SilencerResizesRightLeftAndRotatedWithoutOverlappingNeighbours()
        {
            yield return LoadScene();
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryModel model = controller.Inventory.Model;
            InventoryWeaponAttachments attachments = controller.WeaponAttachments;
            ItemDefinition weaponDefinition = Find(controller, "wpn_aug_a3");
            ItemDefinition silencerDefinition = Find(controller, "wpn_sil_nato");
            ItemDefinition fillerDefinition = FindSmall(controller);

            for (int i = 0; i < 4; i++)
            {
                bool rotated = i >= 2;
                bool left = i % 2 == 1;
                int length = left ? 7 : 6;
                InventoryContainer grid = model.AddContainer(new GridContainer("SilencerTest" + i, rotated ? 2 : length, rotated ? length : 2));
                ItemInstance weapon = model.CreateItem(weaponDefinition);
                ItemInstance silencer = Add(controller, silencerDefinition, controller.Window.SecondaryContainerId);
                silencer.SetState("condition", "0.67");
                Vector2Int position = left ? rotated ? Vector2Int.up : Vector2Int.right : Vector2Int.zero;
                Assert.That(model.TryAdd(weapon, grid.Id, position, rotated).Success, Is.True);

                if (left)
                {
                    ItemInstance neighbour = model.CreateItem(fillerDefinition);
                    Assert.That(model.TryAdd(neighbour, grid.Id, rotated ? new Vector2Int(0, 6) : new Vector2Int(6, 0), false).Success, Is.True);
                }

                Assert.That(attachments.Install(silencer, weapon, false).Success, Is.True);
                Assert.That(weapon.GetSize(false), Is.EqualTo(weaponDefinition.Size));
                Assert.That(model.ContainsItem(silencer), Is.True);
                float weight = grid.TotalWeight + silencer.TotalWeight;
                Assert.That(attachments.Install(silencer, weapon).Success, Is.True);
                Assert.That(weapon.GetSize(rotated), Is.EqualTo(rotated ? new Vector2Int(2, 6) : new Vector2Int(6, 2)));
                Assert.That(weapon.Location.Position, Is.EqualTo(Vector2Int.zero));
                Assert.That(grid.GetItemAt(rotated ? new Vector2Int(0, 5) : new Vector2Int(5, 0)), Is.SameAs(weapon));
                Assert.That(grid.TotalWeight, Is.EqualTo(weight).Within(0.0001f));
                Assert.That(weapon.VisualVariant, Does.Contain(silencerDefinition.DefinitionId));
                Assert.That(attachments.Detach(weapon).Success, Is.True);
                Assert.That(weapon.GetSize(rotated), Is.EqualTo(rotated ? new Vector2Int(2, 5) : new Vector2Int(5, 2)));
                Assert.That(weapon.Location.Position, Is.EqualTo(position));
                Assert.That(model.GetItem(silencer.InstanceId)?.GetState("condition"), Is.EqualTo("0.67"));
                Assert.That(grid.TotalWeight, Is.EqualTo(weight).Within(0.0001f));
            }

            InventoryContainer blocked = model.AddContainer(new GridContainer("SilencerBlocked", 5, 2));
            ItemInstance blockedWeapon = model.CreateItem(weaponDefinition);
            Assert.That(model.TryAdd(blockedWeapon, blocked.Id, Vector2Int.zero, false).Success, Is.True);
            ItemInstance rejected = Add(controller, silencerDefinition, controller.Window.SecondaryContainerId);
            Assert.That(attachments.Install(rejected, blockedWeapon).Success, Is.False);
            Assert.That(model.ContainsItem(rejected), Is.True);
            Assert.That(blockedWeapon.GetSize(false), Is.EqualTo(weaponDefinition.Size));
            Assert.That(blockedWeapon.VisualVariant, Is.Empty);
        }

        [UnityTest]
        public IEnumerator SilencerPersistsWithLoadedAmmoAndUsesOnlyOneWorldItemAndRigidbody()
        {
            yield return LoadScene();
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemInstance weapon = Add(controller, Find(controller, "wpn_aug_a3"), window.PrimaryContainerId);
            ItemInstance silencer = Add(controller, Find(controller, "wpn_sil_nato"), window.PrimaryContainerId);
            Add(controller, Find(controller, "ammo_5.56x45_fmj"), window.PrimaryContainerId, 30);

            using (var ammo = new InventoryWeaponAmmoSource(model, window.PrimaryContainerId))
            {
                ammo.Reload(weapon.InstanceId, 30);
                float total = model.GetOwnerWeight(model.GetContainer(window.PrimaryContainerId).OwnerId);
                Assert.That(controller.WeaponAttachments.Install(silencer, weapon).Success, Is.True);
                Assert.That(model.GetOwnerWeight(model.GetContainer(window.PrimaryContainerId).OwnerId), Is.EqualTo(total).Within(0.0001f));
                Assert.That(weapon.UnitWeight, Is.EqualTo(weapon.Definition.Weight + silencer.Definition.Weight + Find(controller, "ammo_5.56x45_fmj").Weight * 30).Within(0.0001f));
                var restored = new InventoryModel(model.Resolver);
                InventoryContainer backpack = model.GetContainer(window.PrimaryContainerId);
                restored.AddContainer(new GridContainer(backpack.Id, backpack.Width, backpack.Height, backpack.OwnerId));
                Assert.That(InventorySerializer.Restore(restored, InventorySerializer.Capture(model, new[] { backpack.Id })).IsClean, Is.True);
                Assert.That(restored.GetItem(weapon.InstanceId).GetSize(false), Is.EqualTo(new Vector2Int(6, 2)));
                Assert.That(restored.GetContainer(backpack.Id).GetItemArea(restored.GetItem(weapon.InstanceId)).size, Is.EqualTo(new Vector2Int(6, 2)));
                Assert.That(restored.GetItem(weapon.InstanceId).TotalWeight, Is.EqualTo(weapon.TotalWeight).Within(0.0001f));
                Assert.That(restored.GetItem(weapon.InstanceId).GetState(InventoryWeaponAttachments.SILENCER_ITEM_STATE), Is.EqualTo(weapon.GetState(InventoryWeaponAttachments.SILENCER_ITEM_STATE)));
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
                Assert.That(dropped.GetComponentsInChildren<WorldItem>(true).Length, Is.EqualTo(1));
                Assert.That(dropped.GetComponentsInChildren<Rigidbody>(true).Length, Is.EqualTo(1));
                ItemIconVariantPart part = dropped.GetComponentInChildren<ItemIconVariantPart>(true);
                Assert.That(part, Is.Not.Null);
                Assert.That(part.SizeDelta, Is.EqualTo(Vector2Int.right));
                Transform visual = FindChild(dropped.transform, "silencer");
                Assert.That(visual.gameObject.activeInHierarchy, Is.True);
                Assert.That(visual.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
                Assert.That(visual.GetComponent<BoxCollider>(), Is.Not.Null);
                Assert.That(visual.GetComponent<BoxCollider>().size.z, Is.GreaterThan(0.1f));
                Assert.That(Object.FindAnyObjectByType<WorldItemInteractor>().TryPickUp(dropped), Is.True);
                Assert.That(controller.WeaponAttachments.Detach(weapon).Success, Is.True);
                Assert.That(model.GetItem(silencer.InstanceId), Is.Not.Null);
            }
        }

        [UnityTest]
        public IEnumerator SilencerDragContextActionsAndHoverWorkInBothInventoriesAndEquipment()
        {
            yield return LoadScene();
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemInstance weapon = Add(controller, Find(controller, "wpn_aug_a3"), window.PrimaryContainerId);
            ItemInstance leftWeapon = Add(controller, weapon.Definition, window.SecondaryContainerId);
            ItemInstance equipped = Add(controller, weapon.Definition, window.PrimaryContainerId);
            Assert.That(model.TryEquip(equipped.InstanceId, window.EquipmentContainerId).Success, Is.True);
            ItemInstance silencer = Add(controller, Find(controller, "wpn_sil_nato"), window.PrimaryContainerId);
            ItemInstance incompatible = Add(controller, Find(controller, "wpn_sil_pbs1"), window.SecondaryContainerId);
            controller.Open(true);
            yield return null;
            yield return null;
            InventoryItemView silencerView = window.FindGrid(silencer.ContainerId).GetItemView(silencer);
            var pointer = new PointerEventData(EventSystem.current) { position = ScreenPoint(silencerView) };
            ExecuteEvents.Execute(silencerView.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            yield return null;
            Assert.That(window.ItemDecorator(weapon).Tint.a, Is.GreaterThan(0f));
            Assert.That(window.ItemDecorator(leftWeapon).Tint.a, Is.GreaterThan(0f));
            Assert.That(window.ItemDecorator(equipped).Tint.a, Is.GreaterThan(0f));
            ExecuteEvents.Execute(silencerView.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            InventoryItemView weaponView = window.FindGrid(weapon.ContainerId).GetItemView(weapon);
            pointer.position = ScreenPoint(weaponView);
            ExecuteEvents.Execute(weaponView.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            yield return null;
            Assert.That(window.ItemDecorator(silencer).Tint.a, Is.GreaterThan(0f));
            Assert.That(window.ItemDecorator(incompatible).Tint.a, Is.EqualTo(0f));
            ExecuteEvents.Execute(weaponView.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            var context = new InventoryActionContext(window, silencer, silencerView.Grid);
            IInventoryItemAction install = window.Actions.Find(InventoryWeaponAttachments.ATTACH_ACTION + FirstPersonWeaponBinder.PRIMARY_SLOT);
            Assert.That(install.IsVisible(context), Is.True);
            Assert.That(install.GetLabel(context), Is.EqualTo("Поставить на: " + equipped.Definition.ShortName));
            Assert.That(install.IsEnabled(context), Is.True);
            install.Execute(context);
            Assert.That(controller.WeaponAttachments.GetSilencer(equipped), Is.SameAs(silencer.Definition));
            IInventoryItemAction detach = window.Actions.Find(InventoryWeaponAttachments.DETACH_ACTION);
            var weaponContext = new InventoryActionContext(window, equipped, window.FindGrid(equipped.ContainerId));
            Assert.That(detach.GetLabel(weaponContext), Is.EqualTo("Снять глушитель"));
            detach.Execute(weaponContext);
            Assert.That(model.GetItem(silencer.InstanceId), Is.Not.Null);
            Assert.That(model.TryMove(incompatible.InstanceId, incompatible.ContainerId, new Vector2Int(0, model.GetContainer(incompatible.ContainerId).Height - 1), false).Success, Is.True);

            foreach (ItemInstance target in new[] { weapon, leftWeapon, equipped })
            {
                yield return null;
                silencer = model.GetItem(silencer.InstanceId);
                InventoryGridViewBase source = window.FindGrid(silencer.ContainerId);
                silencerView = source.GetItemView(silencer);
                InventoryItemView targetView = window.FindGrid(target.ContainerId).GetItemView(target);
                Assert.That(window.BeginDrag(silencer, source, 1, ScreenPoint(silencerView), null), Is.True);
                window.UpdateDrag(ScreenPoint(targetView), null);
                Assert.That(window.Drag.TargetItem, Is.SameAs(target));
                InventoryResult drop = window.CompleteDrag();
                Assert.That(drop.Success, Is.True, target.ContainerId + " " + target.Location.Position + " " + drop.Failure);
                Assert.That(controller.WeaponAttachments.GetSilencer(target), Is.SameAs(silencer.Definition));
                Assert.That(controller.WeaponAttachments.Detach(target).Success, Is.True);
            }

            controller.Close();
            ItemInstance last = model.GetItem(silencer.InstanceId);
            Assert.That(controller.WeaponAttachments.Install(last, equipped).Success, Is.True);
            ItemDefinition filler = FindSmall(controller);

            for (int i = 0; i < 2000; i++)
            {
                ItemInstance item = model.CreateItem(filler);

                if (model.TryAddAuto(item, window.PrimaryContainerId, false, false).Success == false)
                {
                    break;
                }
            }

            Assert.That(controller.WeaponAttachments.Detach(equipped).Success, Is.False);
            Assert.That(controller.WeaponAttachments.GetSilencer(equipped), Is.SameAs(last.Definition));
            Assert.That(model.GetItem(last.InstanceId), Is.Null);
        }

        [UnityTest]
        public IEnumerator EquippedWeaponIconUpdatesWhenSilencerIsInstalledAndRemoved()
        {
            yield return LoadScene();
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemInstance weapon = Add(controller, Find(controller, "wpn_aug_a3"), window.PrimaryContainerId);
            ItemInstance silencer = Add(controller, Find(controller, "wpn_sil_nato"), window.PrimaryContainerId);
            Assert.That(model.TryEquip(weapon.InstanceId, window.EquipmentContainerId).Success, Is.True);
            controller.Open();
            yield return null;
            InventoryEquipmentPanel equipment = window.FindGrid(weapon.ContainerId) as InventoryEquipmentPanel;
            Assert.That(equipment, Is.Not.Null);
            InventoryEquipmentSlot slot = equipment.GetSlotView(weapon.Location.SlotId);
            InventoryItemView view = equipment.GetItemView(weapon);
            yield return WaitForIcon(view);
            Sprite original = slot.Icon.sprite;
            Assert.That(original, Is.Not.Null);

            for (int i = 0; i < 2; i++)
            {
                Assert.That(controller.WeaponAttachments.Install(model.GetItem(silencer.InstanceId), weapon).Success, Is.True);
                yield return WaitForIcon(view);
                Assert.That(slot.Icon.sprite, Is.Not.Null);
                Assert.That(slot.Icon.sprite, Is.Not.SameAs(original), "Equipment slot kept the icon without the silencer");
                Assert.That(slot.Icon.gameObject.activeInHierarchy, Is.True);
                Assert.That(controller.WeaponAttachments.Detach(weapon).Success, Is.True);
                yield return WaitForIcon(view);
                Assert.That(slot.Icon.sprite, Is.SameAs(original), "Equipment slot kept the silenced icon");
            }

            controller.Close();
        }

        [UnityTest]
        public IEnumerator EquippedSilencerSwitchesHudSoundAndSmokeWithoutMuzzleFlame()
        {
            yield return LoadScene();
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            FirstPersonWeaponController weapons = controller.Player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = Add(controller, Find(controller, "wpn_aug_a3"), window.PrimaryContainerId);
            ItemInstance silencer = Add(controller, Find(controller, "wpn_sil_nato"), window.PrimaryContainerId);
            Add(controller, Find(controller, "ammo_5.56x45_fmj"), window.PrimaryContainerId, 30);
            Assert.That(model.TryEquip(weapon.InstanceId, window.EquipmentContainerId).Success, Is.True);

            using (var ammo = new InventoryWeaponAmmoSource(model, window.PrimaryContainerId))
            {
                ammo.Reload(weapon.InstanceId, 30);
            }

            Press(_keyboard.digit3Key, queueEventOnly: true);
            yield return null;
            Release(_keyboard.digit3Key, queueEventOnly: true);
            float timeout = Time.time + 6f;

            while (weapons.State != FirstPersonWeaponState.Ready && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Ready));
            GameObject originalModel = weapons.Model;
            FirstPersonWeaponView view = originalModel.GetComponent<FirstPersonWeaponView>();
            Assert.That(view.SilencerVisual, Is.Not.Null);
            Assert.That(view.SilencerVisual.activeSelf, Is.False);
            Assert.That(view.SilencerVisual.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(view.SilencerVisual.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            Assert.That(controller.WeaponAttachments.Install(silencer, weapon).Success, Is.True);
            Assert.That(weapons.Model, Is.SameAs(originalModel));
            Assert.That(weapons.IsSilenced, Is.True);
            Assert.That(view.SilencerVisual.activeSelf, Is.True);
            FirstPersonWeaponAsset asset = weapons.ActiveWeapon;
            Assert.That(asset.SilencedShotSounds.Count, Is.EqualTo(7));
            Assert.That(asset.SilencedShotSounds[0].name, Is.EqualTo("aug_silencer_stereo"));
            Assert.That(asset.SilencedFlameParticles, Is.Null);
            Assert.That(asset.SilencedSmokeParticles.name, Is.EqualTo("generic_shoot_00"));
            float weightBeforeShot = weapon.TotalWeight;
            Press(_mouse.leftButton, queueEventOnly: true);
            yield return null;
            Release(_mouse.leftButton, queueEventOnly: true);
            yield return null;
            Assert.That(weapons.Magazine, Is.EqualTo(29));
            Assert.That(weapon.TotalWeight, Is.EqualTo(weightBeforeShot - Find(controller, "ammo_5.56x45_fmj").Weight).Within(0.0001f));
            bool smoke = false;

            foreach (XRayParticleGroupPlayer effect in Object.FindObjectsByType<XRayParticleGroupPlayer>(FindObjectsSortMode.None))
            {
                Assert.That(effect.name == asset.FlameParticles.name && effect.IsPlaying, Is.False);
                smoke |= effect.name == asset.SilencedSmokeParticles.name && effect.IsPlaying;
            }

            Assert.That(smoke, Is.True);
            Assert.That(controller.WeaponAttachments.Detach(weapon).Success, Is.True);
            Assert.That(weapons.IsSilenced, Is.False);
            Assert.That(view.SilencerVisual.activeSelf, Is.False);
            Assert.That(weapons.Muzzle, Is.SameAs(view.Muzzle));
        }

        private static IEnumerator LoadScene()
        {
            SceneManager.LoadScene("OutdoorsScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
        }

        private static IEnumerator WaitForIcon(InventoryItemView view)
        {
            float timeout = Time.time + 5f;

            while (view.IsIconLoading && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(view.IsIconLoading, Is.False);
        }

        private static ItemDefinition Find(InventoryWindowController controller, string section)
        {
            foreach (ItemDefinition definition in controller.Inventory.Database.Definitions)
            {
                if (definition.name == section)
                {
                    return definition;
                }
            }

            Assert.Fail("Missing definition " + section);
            return null;
        }

        private static ItemDefinition FindSmall(InventoryWindowController controller)
        {
            foreach (ItemDefinition definition in controller.Inventory.Database.Definitions)
            {
                if (definition.Size == Vector2Int.one)
                {
                    return definition;
                }
            }

            Assert.Fail("Missing 1x1 definition");
            return null;
        }

        private static ItemInstance Add(InventoryWindowController controller, ItemDefinition definition, string containerId, int amount = 1)
        {
            ItemInstance item = controller.Inventory.Model.CreateItem(definition, amount);
            Assert.That(controller.Inventory.Model.TryAddAuto(item, containerId, false).Success, Is.True, definition.name);
            return item;
        }

        private static Vector2 ScreenPoint(InventoryItemView view) => RectTransformUtility.WorldToScreenPoint(null, view.RectTransform.position);

        private static Transform FindChild(Transform root, string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
