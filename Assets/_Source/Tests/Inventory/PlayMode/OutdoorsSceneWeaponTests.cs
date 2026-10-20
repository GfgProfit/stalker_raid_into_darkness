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
            Assert.That(weapons.Magazine, Is.EqualTo(30));
            Assert.That(weapons.Reserve, Is.EqualTo(90));
            FirstPersonWeaponAmmoView ammoView = player.GetComponentInChildren<FirstPersonWeaponAmmoView>(true);
            Assert.That(ammoView, Is.Not.Null);
            Assert.That(ammoView.Text.gameObject.activeInHierarchy, Is.True);
            Assert.That(ammoView.Text.text, Is.EqualTo("30/90"));

            yield return Tap(_mouse.leftButton);
            yield return null;
            Assert.That(weapons.Magazine, Is.EqualTo(29));
            Assert.That(ammoView.Text.text, Is.EqualTo("29/90"));
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
            Assert.That(weapons.Reserve, Is.EqualTo(90 - (30 - afterBurst)));

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
