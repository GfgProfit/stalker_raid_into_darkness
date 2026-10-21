using System.Collections;
using Inventory;
using Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class OutdoorsSceneWeaponTests : InputTestFixture
    {
        private const string SCENE = "OutdoorsScene";
        private const string BACKPACK = "Backpack";
        private const string AUG = "wpn_aug_a3";
        private const float TIMEOUT = 5.0f;
        private const string LEFT_FINGER = "bip01_l_finger11";

        private Keyboard _keyboard;
        private Mouse _mouse;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        [UnityTest]
        public IEnumerator AugDrawsFiresReloadsAimsAndHolsters()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSeconds(1.0f);

            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            WorldItemInteractor interactor = Object.FindAnyObjectByType<WorldItemInteractor>();
            StalkerPlayerController player = controller.Player;
            FirstPersonWeaponController weapons = player.GetComponentInChildren<FirstPersonWeaponController>();
            Assert.That(weapons, Is.Not.Null, "FPS_Player has no FirstPersonWeaponController");
            Assert.That(player.CameraController.Recoil, Is.Not.Null, "camera recoil is not assigned");
            WorldItem aug = null;

            foreach (WorldItem candidate in Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
            {
                if (candidate.Definition != null && candidate.Definition.name == AUG)
                {
                    aug = candidate;
                }
            }

            Assert.That(aug, Is.Not.Null, "the test AUG is not placed in the scene");
            WeaponHudComponent hud = aug.Definition.GetComponent<WeaponHudComponent>();
            Assert.That(hud.FirstPersonWeapon, Is.Not.Null, "AUG has no first person weapon asset");
            FirstPersonWeaponAsset asset = hud.FirstPersonWeapon;
            Assert.That(asset.FlameParticles, Is.Not.Null);
            Assert.That(asset.SmokeParticles, Is.Not.Null);
            Assert.That(asset.ShotSounds.Count, Is.GreaterThan(0));
            Assert.That(asset.EmptySound, Is.Not.Null);
            Assert.That(asset.LastShellSound, Is.Not.Null);
            Assert.That(asset.LowAmmoSound, Is.Not.Null);
            Assert.That(asset.AimStart != null && asset.AimIdle != null && asset.AimEnd != null, Is.True, "IWP aim blend animations are missing");
            Assert.That(interactor.TryPickUp(aug), Is.True);
            InventoryWindow window = controller.Window;
            ItemInstance item = null;

            foreach (ItemInstance candidate in window.FindGrid(BACKPACK).Container.Items)
            {
                if (candidate.Definition == aug.Definition)
                {
                    item = candidate;
                }
            }

            Assert.That(item, Is.Not.Null);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(interactor.TryPickUp(interactor.Spawn(FindWorldDefinition("ammo_5.56x45_fmj"), 15)), Is.True);
            }

            window.EquipItem(item);
            yield return WaitWhile(() => controller.EquipmentAnimations != null && controller.EquipmentAnimations.IsBusy);
            Assert.That(weapons.GetSlotWeapon(FirstPersonWeaponController.PRIMARY_SLOT), Is.SameAs(asset));
            player.CameraController.SetCursorLocked(true);

            yield return Tap(_keyboard.digit3Key);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Drawing));
            Assert.That(player.Rig.HandsOwner, Is.SameAs(weapons));
            Assert.That(weapons.Model, Is.Not.Null);
            Assert.That(weapons.Muzzle, Is.Not.Null, "AUG HUD model has no muzzle point");
            Assert.That(weapons.Muzzle.name, Is.EqualTo("MuzzlePoint"), "muzzle must come from FirstPersonWeaponView");
            Assert.That(player.Combat, Is.True);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            Assert.That(weapons.Magazine, Is.EqualTo(0));
            Assert.That(weapons.Reserve, Is.EqualTo(60));
            FirstPersonWeaponAmmoView ammoView = player.GetComponentInChildren<FirstPersonWeaponAmmoView>(true);
            Assert.That(ammoView, Is.Not.Null);
            Assert.That(ammoView.Text.gameObject.activeInHierarchy, Is.True);
            Assert.That(ammoView.Text.text, Does.StartWith("0/60 "));
            Assert.That(ammoView.Text.text, Does.Contain("FMJ"));
            yield return Tap(_keyboard.rKey);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready, 8.0f);
            Assert.That(weapons.Magazine, Is.EqualTo(30));
            Assert.That(weapons.Reserve, Is.EqualTo(30));

            yield return Tap(_mouse.leftButton);
            yield return null;
            Assert.That(weapons.Magazine, Is.EqualTo(29));
            Assert.That(ammoView.Text.text, Does.StartWith("29/30 "));
            Assert.That(Quaternion.Angle(player.CameraController.Recoil.Rotation, Quaternion.identity), Is.GreaterThan(0.01f), "recoil did not move the camera");
            Assert.That(IsEffectPlaying(asset.FlameParticles.name), Is.True, "muzzle flame is not playing");
            Assert.That(IsEffectPlaying(asset.SmokeParticles.name), Is.True, "muzzle smoke is not playing");
            Assert.That(weapons.FireMode, Is.EqualTo(1));

            yield return Tap(_keyboard.xKey);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            Assert.That(weapons.FireMode, Is.EqualTo(FirstPersonWeaponAsset.AUTO_FIRE_MODE));
            Press(_mouse.leftButton, queueEventOnly: true);
            yield return new WaitForSeconds(0.6f);
            Release(_mouse.leftButton, queueEventOnly: true);
            yield return null;
            int afterBurst = weapons.Magazine;
            Assert.That(afterBurst, Is.LessThan(26), "automatic fire did not shoot several rounds");

            yield return Tap(_keyboard.rKey);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Reloading));
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready, 8.0f);
            Assert.That(weapons.Magazine, Is.EqualTo(30));
            Assert.That(weapons.Reserve, Is.EqualTo(afterBurst));

            Transform finger = FindChild(player.Rig.Hands.transform, LEFT_FINGER);
            Assert.That(finger, Is.Not.Null, "hands have no " + LEFT_FINGER);
            Quaternion grip = finger.localRotation;
            FirstPersonPickupAnimator pickup = player.Pickup;
            Assert.That(pickup.TryPlay(FirstPersonPickupHand.Left, null, null), Is.True, "pickup did not start over the weapon");
            Assert.That(pickup.IsOverlay, Is.True);
            float maxAngle = 0.0f;
            while (pickup.IsPlaying)
            {
                maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(grip, finger.localRotation));
                yield return null;
            }

            Assert.That(maxAngle, Is.GreaterThan(5.0f), "left hand fingers ignore the pickup overlay");
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Ready));

            Press(_mouse.rightButton, queueEventOnly: true);
            yield return new WaitForSeconds(0.8f);
            Assert.That(weapons.IsAiming, Is.True);
            Assert.That(weapons.AimFactor, Is.EqualTo(1.0f).Within(0.001f));
            Release(_mouse.rightButton, queueEventOnly: true);
            yield return new WaitForSeconds(0.3f);
            Assert.That(weapons.IsAiming, Is.False);

            yield return Tap(_keyboard.digit3Key);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Holstering));
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Hidden);
            Assert.That(player.Rig.HandsBusy, Is.False);
            Assert.That(player.Combat, Is.False);
            Assert.That(ammoView.Text.gameObject.activeInHierarchy, Is.False);
        }

        [UnityTest]
        public IEnumerator AugUsesFmjEpAndEprWithoutStashAmmoOrMixedMagazines()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemInstance weapon = AddItem(model, FindWorldDefinition(AUG), window.PrimaryContainerId, 1);
            ItemDefinition fmj = FindWorldDefinition("ammo_5.56x45_fmj");
            ItemDefinition ep = FindWorldDefinition("ammo_5.56x45_ss190");
            ItemDefinition epr = FindWorldDefinition("ammo_5.56x45_ap");
            AddItem(model, FindWorldDefinition("ammo_9x18_fmj"), window.PrimaryContainerId, 15);
            ItemInstance stashAmmo = AddItem(model, fmj, window.SecondaryContainerId, 15);

            using (var source = new InventoryWeaponAmmoSource(model, window.PrimaryContainerId))
            {
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.Zero);
                Assert.That(source.GetReserve(weapon.InstanceId), Is.Zero);
                Assert.That(source.TryShoot(weapon.InstanceId), Is.False);
                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.Zero);
                Assert.That(stashAmmo.Amount, Is.EqualTo(15));
                ItemInstance first = AddItem(model, fmj, window.PrimaryContainerId, 5);
                ItemInstance second = AddItem(model, fmj, window.PrimaryContainerId, 9);
                Assert.That(source.GetReserve(weapon.InstanceId), Is.EqualTo(14));
                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(14));
                Assert.That(model.GetItem(first.InstanceId), Is.Null);
                Assert.That(model.GetItem(second.InstanceId), Is.Null);
                ItemInstance epStack = AddItem(model, ep, window.PrimaryContainerId, 6);
                ItemInstance eprStack = AddItem(model, epr, window.PrimaryContainerId, 7);
                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(14));
                Assert.That(epStack.Amount, Is.EqualTo(6));
                Assert.That(eprStack.Amount, Is.EqualTo(7));

                for (int i = 0; i < 14; i++)
                {
                    Assert.That(source.TryShoot(weapon.InstanceId), Is.True);
                }

                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_fmj"));
                Assert.That(source.GetReserve(weapon.InstanceId), Is.Zero);
                Assert.That(source.TryStartReload(weapon.InstanceId), Is.True);
                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_ss190"));
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(6));
                Assert.That(eprStack.Amount, Is.EqualTo(7));

                for (int i = 0; i < 6; i++)
                {
                    Assert.That(source.TryShoot(weapon.InstanceId), Is.True);
                }

                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_ap"));
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(7));
                Assert.That(source.TryShoot(weapon.InstanceId), Is.True);
                ItemInstance otherWeapon = AddItem(model, weapon.Definition, window.PrimaryContainerId, 1);
                Assert.That(source.GetMagazine(otherWeapon.InstanceId), Is.Zero);
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
                Assert.That(Object.FindAnyObjectByType<WorldItemInteractor>().TryPickUp(dropped), Is.True);
                Assert.That(model.GetItem(weapon.InstanceId), Is.SameAs(weapon));
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(6));
                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_ap"));

                using (var rebound = new InventoryWeaponAmmoSource(model, window.PrimaryContainerId))
                {
                    Assert.That(rebound.GetMagazine(weapon.InstanceId), Is.EqualTo(6));
                }
            }
        }

        [UnityTest]
        public IEnumerator AugReserveTracksInventoryAndCancelledReloadConsumesNothing()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            FirstPersonWeaponController weapons = controller.Player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = AddItem(model, FindWorldDefinition(AUG), window.PrimaryContainerId, 1);
            window.EquipItem(weapon);
            yield return WaitWhile(() => controller.EquipmentAnimations.IsBusy);
            controller.Player.CameraController.SetCursorLocked(true);
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            FirstPersonWeaponAmmoView view = controller.Player.GetComponentInChildren<FirstPersonWeaponAmmoView>(true);
            Assert.That(view.Text.text, Does.StartWith("0/0 "));
            yield return Tap(_mouse.leftButton);
            Assert.That(weapons.Magazine, Is.Zero);
            ItemInstance ammo = AddItem(model, FindWorldDefinition("ammo_5.56x45_fmj"), window.PrimaryContainerId, 10);
            Assert.That(view.Text.text, Does.StartWith("0/10 "));
            Assert.That(model.TryTransfer(ammo.InstanceId, window.SecondaryContainerId).Success, Is.True);
            Assert.That(view.Text.text, Does.StartWith("0/0 "));
            Assert.That(model.TryTransfer(ammo.InstanceId, window.PrimaryContainerId).Success, Is.True);
            yield return Tap(_keyboard.rKey);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Reloading));
            Assert.That(ammo.Amount, Is.EqualTo(10));
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Hidden);
            Assert.That(ammo.Amount, Is.EqualTo(10));
            Assert.That(weapon.GetState(InventoryWeaponAmmoSource.MAGAZINE_STATE), Is.Null);
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            yield return Tap(_keyboard.rKey);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Reloading));
            Assert.That(model.TryTransfer(ammo.InstanceId, window.SecondaryContainerId).Success, Is.True);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready, 8f);
            Assert.That(weapons.Magazine, Is.Zero);
            Assert.That(ammo.Amount, Is.EqualTo(10));
            Assert.That(view.Text.text, Does.StartWith("0/0 "));
            Assert.That(model.TryTransfer(ammo.InstanceId, window.PrimaryContainerId).Success, Is.True);
            yield return Tap(_keyboard.rKey);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready, 8f);
            Assert.That(weapons.Magazine, Is.EqualTo(10));
            Assert.That(model.GetItem(ammo.InstanceId), Is.Null);
            Assert.That(view.Text.text, Does.StartWith("10/0 "));
        }

        [UnityTest]
        public IEnumerator AugFifteenFmjAndFifteenEpStaySeparateAndSwitchOnT()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            FirstPersonWeaponController weapons = controller.Player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = AddItem(model, FindWorldDefinition(AUG), window.PrimaryContainerId, 1);
            ItemDefinition fmj = FindWorldDefinition("ammo_5.56x45_fmj");
            ItemDefinition ep = FindWorldDefinition("ammo_5.56x45_ss190");
            ItemDefinition epr = FindWorldDefinition("ammo_5.56x45_ap");
            AddItem(model, fmj, window.PrimaryContainerId, 15);
            ItemInstance epBox = AddItem(model, ep, window.PrimaryContainerId, 15);
            ItemInstance eprBox = AddItem(model, epr, window.PrimaryContainerId, 15);
            window.EquipItem(weapon);
            yield return WaitWhile(() => controller.EquipmentAnimations.IsBusy);
            controller.Player.CameraController.SetCursorLocked(true);
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            yield return Tap(_keyboard.rKey);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready, 8f);
            Assert.That(weapons.Magazine, Is.EqualTo(15));
            Assert.That(weapons.AmmoSection, Is.EqualTo("ammo_5.56x45_fmj"));
            Assert.That(epBox.Amount, Is.EqualTo(15));
            yield return Tap(_keyboard.rKey);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Ready));
            Assert.That(weapons.Magazine, Is.EqualTo(15));
            Assert.That(epBox.Amount, Is.EqualTo(15));
            yield return Tap(_keyboard.tKey);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Reloading));
            Assert.That(weapons.Magazine, Is.Zero);
            Assert.That(CountAmmo(model, fmj, window.PrimaryContainerId), Is.EqualTo(15));
            Assert.That(epBox.Amount, Is.EqualTo(15));
            FirstPersonWeaponAmmoView view = controller.Player.GetComponentInChildren<FirstPersonWeaponAmmoView>(true);
            Assert.That(view.Text.text, Does.Contain(ep.ShortName));
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready, 8f);
            Assert.That(weapons.Magazine, Is.EqualTo(15));
            Assert.That(weapons.AmmoSection, Is.EqualTo("ammo_5.56x45_ss190"));
            Assert.That(model.GetItem(epBox.InstanceId), Is.Null);
            yield return Tap(_keyboard.tKey);
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Hidden);
            Assert.That(CountAmmo(model, ep, window.PrimaryContainerId), Is.EqualTo(15));
            Assert.That(eprBox.Amount, Is.EqualTo(15));
            Assert.That(weapon.GetState(InventoryWeaponAmmoSource.MAGAZINE_STATE), Is.EqualTo("0"));
        }

        [UnityTest]
        public IEnumerator AugSwitchReturnsFullMagazineAndRefusesWhenBackpackIsFull()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryModel model = controller.Inventory.Model;
            string backpack = controller.Window.PrimaryContainerId;
            ItemInstance weapon = AddItem(model, FindWorldDefinition(AUG), backpack, 1);
            ItemDefinition fmj = FindWorldDefinition("ammo_5.56x45_fmj");
            ItemDefinition ep = FindWorldDefinition("ammo_5.56x45_ss190");
            AddItem(model, fmj, backpack, 15);
            AddItem(model, fmj, backpack, 15);
            ItemInstance epBox = AddItem(model, ep, backpack, 15);

            using (var source = new InventoryWeaponAmmoSource(model, backpack))
            {
                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(30));
                model.GetContainer(backpack).MaxWeight = 0f;
                Assert.That(source.TrySwitchAmmo(weapon.InstanceId), Is.False);
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(30));
                Assert.That(CountAmmo(model, fmj, backpack), Is.Zero);
                Assert.That(epBox.Amount, Is.EqualTo(15));
                model.GetContainer(backpack).MaxWeight = -1f;
                Assert.That(source.TrySwitchAmmo(weapon.InstanceId), Is.True);
                Assert.That(CountAmmo(model, fmj, backpack), Is.EqualTo(30));
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.Zero);
                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(15));
                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_ss190"));
                Assert.That(source.GetReserve(weapon.InstanceId), Is.Zero);
                Assert.That(source.TrySwitchAmmo(weapon.InstanceId), Is.True);
                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(30));
                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_fmj"));
                Assert.That(CountAmmo(model, ep, backpack), Is.EqualTo(15));
                Assert.That(source.GetReserve(weapon.InstanceId), Is.Zero);
            }
        }

        private static int CountAmmo(InventoryModel model, ItemDefinition definition, string containerId)
        {
            int count = 0;

            foreach (ItemInstance item in model.GetContainer(containerId).Items)
            {
                if (item.Definition == definition)
                {
                    count += item.Amount;
                }
            }

            return count;
        }

        private static ItemInstance AddItem(InventoryModel model, ItemDefinition definition, string containerId, int amount)
        {
            ItemInstance item = model.CreateItem(definition, amount);
            Assert.That(model.TryAddAuto(item, containerId, false).Success, Is.True, definition.name);
            return item;
        }

        private static ItemDefinition FindWorldDefinition(string section)
        {
            foreach (WorldItem item in Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
            {
                if (item.Definition.name == section)
                {
                    return item.Definition;
                }
            }

            Assert.Fail("Missing world item: " + section);
            return null;
        }

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

        private static bool IsEffectPlaying(string prefabName)
        {
            foreach (XRayParticleGroupPlayer player in Object.FindObjectsByType<XRayParticleGroupPlayer>(FindObjectsSortMode.None))
            {
                if (player.name == prefabName && player.IsPlaying)
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator Tap(ButtonControl button)
        {
            Press(button, queueEventOnly: true);
            yield return null;
            Release(button, queueEventOnly: true);
            yield return null;
        }

        private static IEnumerator WaitWhile(System.Func<bool> condition, float timeout = TIMEOUT)
        {
            while (condition() && timeout > 0.0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.That(condition(), Is.False, "timed out");
        }
    }
}
