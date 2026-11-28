using RaidIntoDarkness.Inventory.Infrastructure;
using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;
using System.Collections;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.UI;
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
using XRay.Importer;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class OutdoorsSceneWeaponTests : OutdoorsSceneInputTestFixture
    {
        private const string SCENE = "TEST_SCENE";
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

        [TestCase(0f)]
        [TestCase(0.15f)]
        [TestCase(1f)]
        public void EmptyShotScalesHandsAroundIdleAndKeepsTheWeaponPose(float strength)
        {
            var hands = new GameObject("Hands");
            var handsBone = new GameObject("Bone");
            handsBone.transform.SetParent(hands.transform, false);
            var weapon = new GameObject("Weapon");
            var weaponBone = new GameObject("Bone");
            weaponBone.transform.SetParent(weapon.transform, false);
            var clips = new AnimationClip[3];
            var graph = new FirstPersonWeaponGraph("EmptyShot", hands.AddComponent<Animator>(), weapon.AddComponent<Animator>(), null);

            try
            {
                float[] positions = { 10f, 20f, 3f };

                for (int i = 0; i < clips.Length; i++)
                {
                    clips[i] = new AnimationClip();
                    clips[i].SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, positions[i]));
                }

                graph.Play(clips[0], clips[2], 1f, 0f, true);
                graph.Evaluate(0f);
                graph.PlayEmptyShot(clips[1], 1f, 0.05f, strength);
                graph.Evaluate(0.1f);
                Assert.That(handsBone.transform.localPosition.x, Is.EqualTo(10f + 10f * strength).Within(0.001f));
                Assert.That(weaponBone.transform.localPosition.x, Is.EqualTo(3f).Within(0.001f));
                graph.Evaluate(1f);
                Assert.That(handsBone.transform.localPosition.x, Is.EqualTo(10f).Within(0.001f));
                graph.PlayEmptyShot(clips[1], 1f, 0.05f, strength);
                graph.Evaluate(0.1f);
                graph.Play(clips[1], clips[2], 1f, 0f, false);
                graph.Evaluate(0f);
                Assert.That(handsBone.transform.localPosition.x, Is.EqualTo(20f).Within(0.001f));
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(hands);
                Object.DestroyImmediate(weapon);

                foreach (AnimationClip clip in clips)
                {
                    Object.DestroyImmediate(clip);
                }
            }
        }

        [Test]
        public void InterruptedCrossFadeKeepsTheBlendedHandsPose()
        {
            var hands = new GameObject("Hands");
            var bone = new GameObject("Bone");
            bone.transform.SetParent(hands.transform, false);
            var clips = new AnimationClip[3];
            var graph = new FirstPersonWeaponGraph("InterruptedFade", hands.AddComponent<Animator>(), null, null);

            try
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    clips[i] = new AnimationClip();
                    clips[i].SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, i * 10f));
                }

                graph.Play(clips[0], null, 1f, 0f, true);
                graph.Evaluate(0f);
                graph.Play(clips[1], null, 1f, 1f, true);
                graph.Evaluate(0.25f);
                float blended = bone.transform.localPosition.x;
                Assert.That(blended, Is.EqualTo(2.5f).Within(0.01f));

                for (int i = 0; i < 1000; i++)
                {
                    graph.Play(clips[i % 2 + 1], null, 1f, 1f, true);
                    graph.Evaluate(0f);
                    Assert.That(bone.transform.localPosition.x, Is.EqualTo(blended).Within(0.001f));
                    graph.Evaluate(0.02f);
                    blended = bone.transform.localPosition.x;
                }

                graph.Evaluate(1f);
                Assert.That(bone.transform.localPosition.x, Is.EqualTo(20f).Within(0.01f));
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(hands);

                foreach (AnimationClip clip in clips)
                {
                    Object.DestroyImmediate(clip);
                }
            }
        }

        [Test]
        public void MagazineSwapKeepsTheMagazineSeatedWhileHandsCrossFade()
        {
            var hands = new GameObject("Hands");
            var handBone = new GameObject("HandBone");
            handBone.transform.SetParent(hands.transform, false);
            var weapon = new GameObject("Weapon");
            var mainMagazine = new GameObject("MainMagazine");
            mainMagazine.transform.SetParent(weapon.transform, false);
            var extraMagazine = new GameObject("ExtraMagazine");
            extraMagazine.transform.SetParent(weapon.transform, false);
            var reloadObjects = new GameObject("ReloadObjects");
            reloadObjects.transform.SetParent(weapon.transform, false);
            var installedRenderer = weapon.AddComponent<SkinnedMeshRenderer>();
            var replacementRenderer = reloadObjects.AddComponent<SkinnedMeshRenderer>();
            installedRenderer.bones = new[] { mainMagazine.transform };
            replacementRenderer.bones = new[] { extraMagazine.transform };
            var idle = new AnimationClip();
            var reload = new AnimationClip();
            idle.SetCurve("HandBone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 0f));
            idle.SetCurve("MainMagazine", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 0f));
            idle.SetCurve("ExtraMagazine", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 10f));
            reload.SetCurve("HandBone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 10f));
            reload.SetCurve("MainMagazine", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 10f));
            reload.SetCurve("ExtraMagazine", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 0f));
            var view = weapon.AddComponent<FirstPersonWeaponView>();
            const System.Reflection.BindingFlags FIELDS = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(FirstPersonWeaponView).GetField("_magazineSwapObjects", FIELDS).SetValue(view, reloadObjects);
            typeof(FirstPersonWeaponView).GetField("_magazineSwapClips", FIELDS).SetValue(view, new[] { reload });
            typeof(FirstPersonWeaponView).GetField("_installedMagazineRenderers", FIELDS).SetValue(view, new[] { installedRenderer });
            typeof(FirstPersonWeaponView).GetField("_replacementMagazineRenderers", FIELDS).SetValue(view, new[] { replacementRenderer });
            var pairsField = typeof(FirstPersonWeaponView).GetField("_magazineBonePairs", FIELDS);
            var pairType = pairsField.FieldType.GetElementType();
            var pairs = System.Array.CreateInstance(pairType, 1);
            object pair = System.Activator.CreateInstance(pairType);
            pairType.GetField("_installed", FIELDS).SetValue(pair, mainMagazine.transform);
            pairType.GetField("_replacement", FIELDS).SetValue(pair, extraMagazine.transform);
            pairs.SetValue(pair, 0);
            pairsField.SetValue(view, pairs);
            var graph = new FirstPersonWeaponGraph("MagazineSwap", hands.AddComponent<Animator>(), weapon.AddComponent<Animator>(), null, view);
            var complete = typeof(FirstPersonWeaponGraph).GetMethod("CompleteMagazineSwap", FIELDS);

            try
            {
                Assert.That(reloadObjects.activeSelf, Is.False);
                graph.Play(idle, idle, 1f, 0f, true);
                graph.Evaluate(0f);
                graph.Play(reload, reload, 1f, 0.2f, false);
                graph.Evaluate(0.1f);
                Assert.That(reloadObjects.activeSelf, Is.True);
                Assert.That(extraMagazine.transform.localPosition.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(handBone.transform.localPosition.x, Is.EqualTo(5f).Within(0.001f));
                graph.Evaluate(0.1f);

                graph.Play(idle, idle, 1f, 0.2f, true);
                graph.Evaluate(0.1f);
                Assert.That(reloadObjects.activeSelf, Is.False);
                Assert.That(handBone.transform.localPosition.x, Is.EqualTo(5f).Within(0.001f));
                Assert.That(installedRenderer.bones[0], Is.SameAs(mainMagazine.transform));
                graph.Evaluate(0.1f);

                graph.Play(reload, reload, 1f, 0.2f, false);
                graph.Evaluate(0.2f);
                complete.Invoke(graph, null);
                graph.Play(idle, idle, 1f, 0.2f, true);
                graph.Evaluate(0.1f);
                Assert.That(reloadObjects.activeSelf, Is.True);
                Assert.That(installedRenderer.enabled, Is.False);
                Assert.That(replacementRenderer.bones[0], Is.SameAs(mainMagazine.transform));
                Assert.That(replacementRenderer.bones[0].localPosition.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(handBone.transform.localPosition.x, Is.EqualTo(5f).Within(0.001f));

                graph.Play(reload, reload, 1f, 0.2f, false);
                graph.Evaluate(0.2f);
                Assert.That(installedRenderer.enabled, Is.True);
                Assert.That(installedRenderer.bones[0], Is.SameAs(extraMagazine.transform));
                complete.Invoke(graph, null);
                graph.Play(idle, idle, 1f, 0.2f, true);
                graph.Evaluate(0.1f);
                Assert.That(reloadObjects.activeSelf, Is.False);
                Assert.That(installedRenderer.bones[0], Is.SameAs(mainMagazine.transform));
                Assert.That(installedRenderer.bones[0].localPosition.x, Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(hands);
                Object.DestroyImmediate(weapon);
                Object.DestroyImmediate(idle);
                Object.DestroyImmediate(reload);
            }
        }

        [UnityTest]
        public IEnumerator AimCameraPulseReturnsToNeutralWhileAimingAndAfterRelease()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            StalkerPlayerController player = controller.Player;
            FirstPersonWeaponController weapons = player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = AddItem(controller.Inventory.Model, FindDefinition(controller.Inventory.Database, AUG), controller.Window.PrimaryContainerId, 1);
            Assert.That(controller.Inventory.Model.TryEquip(weapon.InstanceId, controller.Window.EquipmentContainerId).Success, Is.True);
            player.CameraController.SetCursorLocked(true);
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            FirstPersonWeaponAsset asset = weapons.ActiveWeapon;
            StalkerCameraEffects effects = player.CameraEffects;
            effects.EffectsEnabled = false;
            effects.StopItemEffect();
            yield return null;
            Transform camera = player.CameraController.Camera.transform;
            float baseFov = player.CameraController.Camera.fieldOfView;
            float zoomedFov = 2f * Mathf.Atan(Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) / asset.AimZoom) * Mathf.Rad2Deg;
            var audio = (AudioSource)typeof(FirstPersonWeaponController).GetField("_audio", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(weapons);
            Assert.That(asset.AimZoom, Is.GreaterThan(1f));
            Assert.That(asset.AimInSounds.Count, Is.EqualTo(10));
            Assert.That(asset.AimOutSounds.Count, Is.EqualTo(10));
            Assert.That(asset.Find(FirstPersonWeaponMotionKind.Draw, 0).Sound.name, Is.EqualTo("generic_draw"));
            Assert.That(asset.Find(FirstPersonWeaponMotionKind.Holster, 0).Sound.name, Is.EqualTo("generic_holster"));
            Assert.That(camera.IsChildOf(effects.Pivot), Is.True);
            Quaternion neutral = Quaternion.Inverse(player.CameraController.CameraRoot.rotation) * camera.rotation;
            Assert.That(Mathf.Abs(asset.AimCameraRoll), Is.GreaterThan(0f));
            foreach (bool aiming in new[] { true, false })
            {
                if (aiming)
                {
                    Press(_mouse.rightButton, queueEventOnly: true);
                }
                else
                {
                    Release(_mouse.rightButton, queueEventOnly: true);
                }

                float peak = 0f;
                bool checkedSound = false;
                float until = Time.time + asset.AimTime + asset.AimCameraBlendTime * 8f;

                while (Time.time < until)
                {
                    yield return null;
                    if (!checkedSound && weapons.IsAiming == aiming)
                    {
                        CollectionAssert.Contains(aiming ? asset.AimInSounds : asset.AimOutSounds, audio.clip);
                        checkedSound = true;
                    }

                    Quaternion relative = Quaternion.Inverse(player.CameraController.CameraRoot.rotation) * camera.rotation;
                    Quaternion delta = Quaternion.Inverse(neutral) * relative;
                    float roll = Mathf.DeltaAngle(0f, delta.eulerAngles.z);
                    peak = Mathf.Max(peak, roll * Mathf.Sign(asset.AimCameraRoll) * (aiming ? 1f : -1f));
                }

                Assert.That(peak, Is.GreaterThan(Mathf.Abs(asset.AimCameraRoll) * 0.1f));
                Assert.That(peak, Is.LessThanOrEqualTo(Mathf.Abs(asset.AimCameraRoll) + 0.05f));
                Quaternion settledPose = Quaternion.Inverse(player.CameraController.CameraRoot.rotation) * camera.rotation;
                Assert.That(Quaternion.Angle(neutral, settledPose), Is.LessThan(0.05f));
                Assert.That(weapons.IsAiming, Is.EqualTo(aiming));
                Assert.That(player.CameraController.Camera.fieldOfView, Is.EqualTo(aiming ? zoomedFov : baseFov).Within(0.001f));
            }

            Quaternion previous = Quaternion.Inverse(player.CameraController.CameraRoot.rotation) * camera.rotation;

            for (int i = 0; i < 24; i++)
            {
                if (i % 2 == 0)
                {
                    Press(_mouse.rightButton, queueEventOnly: true);
                }
                else
                {
                    Release(_mouse.rightButton, queueEventOnly: true);
                }

                yield return null;
                yield return new WaitForEndOfFrame();
                Quaternion current = Quaternion.Inverse(player.CameraController.CameraRoot.rotation) * camera.rotation;
                Assert.That(Quaternion.Angle(previous, current), Is.LessThan(Mathf.Abs(asset.AimCameraRoll) * 0.5f));
                previous = current;
            }

            Release(_mouse.rightButton, queueEventOnly: true);
            yield return new WaitForSeconds(asset.AimCameraBlendTime * 6f);
            Quaternion settled = Quaternion.Inverse(player.CameraController.CameraRoot.rotation) * camera.rotation;
            Assert.That(Quaternion.Angle(neutral, settled), Is.LessThan(0.05f));
            Press(_mouse.rightButton, queueEventOnly: true);
            yield return new WaitForSeconds(asset.AimTime * 0.5f);
            Assert.That(Quaternion.Angle(effects.Pivot.localRotation, Quaternion.identity), Is.GreaterThan(0.05f));
            weapons.Hide();
            Assert.That(player.CameraController.Camera.fieldOfView, Is.EqualTo(baseFov).Within(0.001f));
            Assert.That(Quaternion.Angle(effects.Pivot.localRotation, Quaternion.identity), Is.LessThan(0.05f));
            Release(_mouse.rightButton, queueEventOnly: true);
        }

        [UnityTest]
        public IEnumerator AugAimTransitionsReverseTheActiveAnmWithoutRemappingTime()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            StalkerPlayerController player = controller.Player;
            FirstPersonWeaponController weapons = player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = AddItem(controller.Inventory.Model, FindDefinition(controller.Inventory.Database, AUG), controller.Window.PrimaryContainerId, 1);
            Assert.That(controller.Inventory.Model.TryEquip(weapon.InstanceId, controller.Window.EquipmentContainerId).Success, Is.True);
            player.CameraController.SetCursorLocked(true);
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            FirstPersonWeaponAsset asset = weapons.ActiveWeapon;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var setAiming = typeof(FirstPersonWeaponController).GetMethod("SetAiming", flags);
            var tickAim = typeof(FirstPersonWeaponController).GetMethod("TickAim", flags);
            var applyOffset = typeof(FirstPersonWeaponController).GetMethod("ApplyHandsOffset", flags);
            setAiming.Invoke(weapons, new object[] { true });
            tickAim.Invoke(weapons, new object[] { 0.12f });
            applyOffset.Invoke(weapons, null);
            AssertAimPose(player, weapons, asset.AimStart, 0.12f);
            Vector3 enteringPosition = player.Rig.Hands.transform.localPosition;
            Quaternion enteringRotation = player.Rig.Hands.transform.localRotation;
            setAiming.Invoke(weapons, new object[] { false });
            applyOffset.Invoke(weapons, null);
            Assert.That(Vector3.Distance(player.Rig.Hands.transform.localPosition, enteringPosition), Is.LessThan(0.00001f));
            Assert.That(Quaternion.Angle(player.Rig.Hands.transform.localRotation, enteringRotation), Is.LessThan(0.05f));
            tickAim.Invoke(weapons, new object[] { 0.04f });
            applyOffset.Invoke(weapons, null);
            AssertAimPose(player, weapons, asset.AimStart, 0.08f);
            setAiming.Invoke(weapons, new object[] { true });
            tickAim.Invoke(weapons, new object[] { 0.05f });
            applyOffset.Invoke(weapons, null);
            AssertAimPose(player, weapons, asset.AimStart, 0.13f);
            tickAim.Invoke(weapons, new object[] { asset.AimStart.Duration });
            applyOffset.Invoke(weapons, null);
            AssertAimPose(player, weapons, asset.AimIdle, 0f);
            setAiming.Invoke(weapons, new object[] { false });
            tickAim.Invoke(weapons, new object[] { 0.11f });
            applyOffset.Invoke(weapons, null);
            AssertAimPose(player, weapons, asset.AimEnd, 0.11f);
            Vector3 leavingPosition = player.Rig.Hands.transform.localPosition;
            Quaternion leavingRotation = player.Rig.Hands.transform.localRotation;
            setAiming.Invoke(weapons, new object[] { true });
            applyOffset.Invoke(weapons, null);
            Assert.That(Vector3.Distance(player.Rig.Hands.transform.localPosition, leavingPosition), Is.LessThan(0.00001f));
            Assert.That(Quaternion.Angle(player.Rig.Hands.transform.localRotation, leavingRotation), Is.LessThan(0.05f));
            tickAim.Invoke(weapons, new object[] { 0.04f });
            applyOffset.Invoke(weapons, null);
            AssertAimPose(player, weapons, asset.AimEnd, 0.07f);
            tickAim.Invoke(weapons, new object[] { asset.AimEnd.Duration });
            applyOffset.Invoke(weapons, null);
            AssertAimPose(player, weapons, asset.AimIdle, 0f);
            setAiming.Invoke(weapons, new object[] { false });
            tickAim.Invoke(weapons, new object[] { asset.AimEnd.Duration });
            applyOffset.Invoke(weapons, null);
            Assert.That(player.Rig.Hands.transform.localPosition, Is.EqualTo(asset.HandsPosition));
            Assert.That(Quaternion.Angle(player.Rig.Hands.transform.localRotation, asset.HandsRotation), Is.LessThan(0.05f));

            for (int i = 0; i < 20; i++)
            {
                Press(_mouse.rightButton, queueEventOnly: true);
                yield return null;
                yield return null;
                Release(_mouse.rightButton, queueEventOnly: true);
                yield return null;
                yield return null;
            }

            yield return new WaitForSeconds(asset.AimStart.Duration + asset.AimEnd.Duration + 0.1f);
            Assert.That(weapons.IsAiming, Is.False);
            Assert.That(weapons.AimFactor, Is.Zero);
            Assert.That(Vector3.Distance(player.Rig.Hands.transform.localPosition, asset.HandsPosition), Is.LessThan(0.00001f));
            Assert.That(Quaternion.Angle(player.Rig.Hands.transform.localRotation, asset.HandsRotation), Is.LessThan(0.05f));
        }

        [UnityTest]
        public IEnumerator AugKeepsEmptyWeaponPoseWhenAimingMovingAndSprinting()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryModel model = controller.Inventory.Model;
            StalkerPlayerController player = controller.Player;
            FirstPersonWeaponController weapons = player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = AddItem(model, FindDefinition(controller.Inventory.Database, AUG), controller.Window.PrimaryContainerId, 1);
            ItemDefinition fmj = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ss190");
            AddItem(model, fmj, controller.Window.PrimaryContainerId, 1);
            Assert.That(model.TryEquip(weapon.InstanceId, controller.Window.EquipmentContainerId).Success, Is.True);
            player.CameraController.SetCursorLocked(true);
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            yield return new WaitForSeconds(0.25f);
            FirstPersonWeaponAsset asset = weapons.ActiveWeapon;
            GameObject reference = Object.Instantiate(asset.HudModel);
            reference.SetActive(false);
            Transform referenceBolt = FindChild(reference.transform, "bolt");
            Assert.That(referenceBolt, Is.Not.Null);
            asset.Find(FirstPersonWeaponMotionKind.Idle, 0).Weapon.SampleAnimation(reference, 0f);
            Vector3 loadedPosition = referenceBolt.localPosition;
            Quaternion loadedRotation = referenceBolt.localRotation;
            asset.Find(FirstPersonWeaponMotionKind.IdleEmpty, 0).Weapon.SampleAnimation(reference, 0f);
            Vector3 emptyPosition = referenceBolt.localPosition;
            Quaternion emptyRotation = referenceBolt.localRotation;
            Object.Destroy(reference);
            Assert.That(Vector3.Distance(loadedPosition, emptyPosition) + Quaternion.Angle(loadedRotation, emptyRotation), Is.GreaterThan(0.001f));
            Transform bolt = FindChild(weapons.Model.transform, "bolt");
            Assert.That(weapons.Magazine, Is.Zero);
            AssertBoltPose(bolt, emptyPosition, emptyRotation);

            foreach (GameObject hudModel in new[] { player.Rig.Hands, player.Rig.Body, weapons.Model })
            {
                foreach (SkinnedMeshRenderer renderer in hudModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Assert.That(renderer.updateWhenOffscreen, Is.True, renderer.name);
                }
            }

            yield return Tap(_keyboard.digit3Key);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Holstering));
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Hidden);
            yield return Tap(_keyboard.digit3Key);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Drawing));
            bolt = FindChild(weapons.Model.transform, "bolt");
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            player.CameraEffects.StopItemEffect();
            yield return Tap(_mouse.leftButton);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.Shoot));
            Assert.That(player.CameraEffects.IsItemEffectPlaying, Is.False);
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            yield return new WaitForSeconds(0.12f);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.Shoot));
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            yield return new WaitForSeconds(asset.EmptyClickInterval - 0.12f + 0.02f);
            yield return Tap(_mouse.leftButton);
            yield return new WaitForSeconds(0.2f);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.Shoot));
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            yield return WaitWhile(() => weapons.CurrentMotion != FirstPersonWeaponMotionKind.Idle, 8f);
            yield return new WaitForSeconds(asset.CrossFade + 0.05f);
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            Press(_mouse.rightButton, queueEventOnly: true);
            yield return new WaitForSeconds(0.4f);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.IdleAim));
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var setAiming = typeof(FirstPersonWeaponController).GetMethod("SetAiming", flags);
            var applyOffset = typeof(FirstPersonWeaponController).GetMethod("ApplyHandsOffset", flags);

            for (int i = 0; i < 12; i++)
            {
                Vector3 position = player.Rig.Hands.transform.localPosition;
                Quaternion rotation = player.Rig.Hands.transform.localRotation;
                setAiming.Invoke(weapons, new object[] { !weapons.IsAiming });
                applyOffset.Invoke(weapons, null);
                Assert.That(Vector3.Distance(player.Rig.Hands.transform.localPosition, position), Is.LessThan(0.00001f));
                Assert.That(Quaternion.Angle(player.Rig.Hands.transform.localRotation, rotation), Is.LessThan(0.01f));
                yield return null;
                yield return null;
            }

            yield return new WaitForSeconds(0.4f);
            Assert.That(weapons.AimFactor, Is.EqualTo(1f).Within(0.001f));
            player.ScriptedWish = StalkerMoveFlags.Forward;
            yield return new WaitForSeconds(0.4f);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.MovingAim));
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            Release(_mouse.rightButton, queueEventOnly: true);
            yield return new WaitForSeconds(0.4f);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.Moving));
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            player.ScriptedWish = StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint;
            yield return WaitWhile(() => weapons.CurrentMotion != FirstPersonWeaponMotionKind.Sprint);
            yield return new WaitForSeconds(0.25f);
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            player.ScriptedWish = StalkerMoveFlags.None;
            yield return WaitWhile(() => weapons.CurrentMotion != FirstPersonWeaponMotionKind.Idle);
            yield return new WaitForSeconds(0.25f);
            AssertBoltPose(bolt, emptyPosition, emptyRotation);

            using (var ammo = new InventoryWeaponAmmoSource(model, controller.Window.PrimaryContainerId))
            {
                ammo.Reload(weapon.InstanceId, 30);
            }

            yield return new WaitForSeconds(0.25f);
            Assert.That(weapons.Magazine, Is.EqualTo(1));
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.Idle));
            AssertBoltPose(bolt, loadedPosition, loadedRotation);
            yield return Tap(_mouse.leftButton);
            Assert.That(weapons.Magazine, Is.Zero);
            Assert.That(asset.LastShotHoldFrame, Is.GreaterThan(0));
            yield return new WaitForSeconds(asset.LastShotHoldFrame / asset.Find(FirstPersonWeaponMotionKind.Shoot, 0).Weapon.frameRate + 0.06f);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.Shoot));
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            yield return WaitWhile(() => weapons.CurrentMotion != FirstPersonWeaponMotionKind.Idle);
            yield return new WaitForSeconds(0.25f);
            AssertBoltPose(bolt, emptyPosition, emptyRotation);
            AddItem(model, fmj, controller.Window.PrimaryContainerId, 1);
            yield return Tap(_keyboard.rKey);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.ReloadEmpty));
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready, 8f);
            yield return new WaitForSeconds(0.25f);
            Assert.That(weapons.Magazine, Is.EqualTo(1));
            AssertBoltPose(bolt, loadedPosition, loadedRotation);
        }

        [UnityTest]
        public IEnumerator LoadedAmmoContributesToWeaponWeightDuringCarryTransferAndDrop()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemInstance weapon = AddItem(model, FindDefinition(controller.Inventory.Database, AUG), window.PrimaryContainerId, 1);
            ItemDefinition fmj = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ss190");
            ItemDefinition ep = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ap");
            AddItem(model, fmj, window.PrimaryContainerId, 30);
            AddItem(model, ep, window.PrimaryContainerId, 40);
            InventoryContainer backpack = model.GetContainer(window.PrimaryContainerId);
            InventoryContainer stash = model.GetContainer(window.SecondaryContainerId);
            float initialWeight = model.GetOwnerWeight(backpack.OwnerId);
            int notifications = 0;
            System.Action<InventoryChange> onChanged = change => notifications += change.Kind == InventoryChangeKind.ItemStateChanged ? 1 : 0;
            model.Changed += onChanged;

            using (var source = new InventoryWeaponAmmoSource(model, backpack.Id))
            {
                source.Reload(weapon.InstanceId, 30);
                Assert.That(weapon.TotalWeight, Is.EqualTo(weapon.Definition.Weight + fmj.Weight * 30).Within(0.0001f));
                Assert.That(model.GetOwnerWeight(backpack.OwnerId), Is.EqualTo(initialWeight).Within(0.0001f));
                Assert.That(source.TryShoot(weapon.InstanceId), Is.True);
                Assert.That(weapon.TotalWeight, Is.EqualTo(weapon.Definition.Weight + fmj.Weight * 29).Within(0.0001f));
                Assert.That(model.GetOwnerWeight(backpack.OwnerId), Is.EqualTo(initialWeight - fmj.Weight).Within(0.0001f));
                Assert.That(source.TrySwitchAmmo(weapon.InstanceId), Is.True);
                Assert.That(weapon.TotalWeight, Is.EqualTo(weapon.Definition.Weight));
                Assert.That(model.GetOwnerWeight(backpack.OwnerId), Is.EqualTo(initialWeight - fmj.Weight).Within(0.0001f));
                source.Reload(weapon.InstanceId, 30);
                float loadedWeight = weapon.Definition.Weight + ep.Weight * 30;
                Assert.That(weapon.TotalWeight, Is.EqualTo(loadedWeight).Within(0.0001f));
                Assert.That(model.GetOwnerWeight(backpack.OwnerId), Is.EqualTo(initialWeight - fmj.Weight).Within(0.0001f));
                stash.MaxWeight = weapon.Definition.Weight;
                Assert.That(model.TryTransfer(weapon.InstanceId, stash.Id).Success, Is.False);
                stash.MaxWeight = -1f;
                Assert.That(model.TryTransfer(weapon.InstanceId, stash.Id).Success, Is.True);
                Assert.That(model.GetItem(weapon.InstanceId), Is.SameAs(weapon));
                Assert.That(weapon.ContainerId, Is.EqualTo(stash.Id));
                Assert.That(weapon.TotalWeight, Is.EqualTo(loadedWeight).Within(0.0001f));
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(30));
                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_ap"));
                Assert.That(model.TryTransfer(weapon.InstanceId, backpack.Id).Success, Is.True);
                window.DropItemNow(weapon);
                yield return null;
                WorldItem dropped = null;

                foreach (WorldItem candidate in Object.FindObjectsByType<WorldItem>())
                {
                    if (candidate.Instance == weapon)
                    {
                        dropped = candidate;
                    }
                }

                Assert.That(dropped, Is.Not.Null);
                Assert.That(dropped.Instance.TotalWeight, Is.EqualTo(loadedWeight).Within(0.0001f));
                Assert.That(Object.FindAnyObjectByType<WorldItemInteractor>().TryPickUp(dropped), Is.True);
                Assert.That(weapon.TotalWeight, Is.EqualTo(loadedWeight).Within(0.0001f));
                Assert.That(notifications, Is.GreaterThanOrEqualTo(4));
            }

            model.Changed -= onChanged;
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
            ItemDefinition definition = FindDefinition(controller.Inventory.Database, AUG);
            WorldItem aug = interactor.Spawn(definition, 1);
            Assert.That(aug, Is.Not.Null, "the AUG world prefab could not be placed in the test scene");
            Assert.That(aug.Definition, Is.SameAs(definition));
            WeaponHudComponent hud = definition.GetComponent<WeaponHudComponent>();
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
                if (candidate.Definition == definition)
                {
                    item = candidate;
                }
            }

            Assert.That(item, Is.Not.Null);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(interactor.TryPickUp(interactor.Spawn(FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ss190"), 15)), Is.True);
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
            Assert.That(ammoView.Text.text, Does.Contain("M855"));
            yield return Tap(_keyboard.rKey);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready, 8.0f);
            Assert.That(weapons.Magazine, Is.EqualTo(30));
            Assert.That(weapons.Reserve, Is.EqualTo(30));

            var observedShots = new List<(int Magazine, int Reserve, string Section)>();
            void ObserveShot(InventoryChange change)
            {
                if (change.Item == item && change.Kind == InventoryChangeKind.ItemStateChanged)
                {
                    observedShots.Add((WeaponMagazineState.GetLoadedRounds(item), weapons.Reserve, item.GetState(WeaponMagazineState.AMMO_SECTION_STATE)));
                }
            }

            string loadedSection = item.GetState(WeaponMagazineState.AMMO_SECTION_STATE);
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            controller.Inventory.Model.Changed += ObserveShot;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                yield return null;
                yield return Tap(_mouse.leftButton);
                yield return null;
                Assert.That(weapons.Magazine, Is.EqualTo(29));
                Assert.That(observedShots, Is.EqualTo(new[] { (29, 30, loadedSection) }));
                Assert.That(ammoView.Text.text, Does.StartWith("29/30 "));
                Assert.That(Quaternion.Angle(player.CameraController.Recoil.Rotation, Quaternion.identity), Is.GreaterThan(0.01f), "recoil did not move the camera");
            }
            finally
            {
                controller.Inventory.Model.Changed -= ObserveShot;
                Time.captureDeltaTime = previousCaptureDeltaTime;
            }

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
            WorldItem reloadPickup = interactor.Spawn(FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ss190"), 1);
            Assert.That(reloadPickup, Is.Not.Null);
            reloadPickup.GetComponent<Rigidbody>().isKinematic = true;
            Transform pickupView = player.CameraController.Camera.transform;
            Vector3 pickupOffset = reloadPickup.GetComponent<Collider>().bounds.center - reloadPickup.transform.position;
            reloadPickup.transform.position = pickupView.position + pickupView.forward * 1.2f - pickupOffset;
            Physics.SyncTransforms();
            Assert.That(interactor.FindTarget(), Is.SameAs(reloadPickup));
            Assert.That(interactor.BeginPickUp(reloadPickup), Is.False, "pickup started during reload");
            Assert.That(interactor.TryPickUp(reloadPickup), Is.False, "direct pickup bypassed reload");
            yield return Tap(_keyboard.fKey);
            Assert.That(interactor.IsPickingUp, Is.False, "F started pickup during reload");
            Assert.That(player.Pickup.IsPlaying, Is.False);
            Assert.That(window.Hint, Is.Empty, "pickup hint remains visible during reload");
            Assert.That(reloadPickup != null && reloadPickup.gameObject.activeSelf, Is.True);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready, 8.0f);
            Assert.That(weapons.Magazine, Is.EqualTo(30));
            Assert.That(weapons.Reserve, Is.EqualTo(afterBurst));
            yield return null;
            Assert.That(interactor.IsPickingUp, Is.False, "blocked F was queued until reload completed");
            Assert.That(reloadPickup != null && reloadPickup.gameObject.activeSelf, Is.True);
            reloadPickup.transform.position = pickupView.position + pickupView.forward * 1.2f - pickupOffset;
            Physics.SyncTransforms();
            yield return Tap(_keyboard.fKey);
            Assert.That(interactor.IsPickingUp, Is.True, "F did not resume after reload");
            yield return WaitWhile(() => interactor.IsPickingUp);
            Assert.That(reloadPickup == null, Is.True, "pickup after reload did not take the item");

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
        public IEnumerator AugUsesIwpAmmoWithoutStashAmmoOrMixedMagazines()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemInstance weapon = AddItem(model, FindDefinition(controller.Inventory.Database, AUG), window.PrimaryContainerId, 1);
            ItemDefinition fmj = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ss190");
            ItemDefinition epr = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ap");
            AddItem(model, FindDefinition(controller.Inventory.Database, "ammo_9x18_fmj"), window.PrimaryContainerId, 15);
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
                ItemInstance eprStack = AddItem(model, epr, window.PrimaryContainerId, 7);
                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(14));
                Assert.That(eprStack.Amount, Is.EqualTo(7));

                for (int i = 0; i < 14; i++)
                {
                    Assert.That(source.TryShoot(weapon.InstanceId), Is.True);
                }

                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_ss190"));
                Assert.That(source.GetReserve(weapon.InstanceId), Is.Zero);
                Assert.That(source.TryStartReload(weapon.InstanceId), Is.True);
                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_ap"));
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(7));
                Assert.That(source.TryShoot(weapon.InstanceId), Is.True);
                ItemInstance otherWeapon = AddItem(model, weapon.Definition, window.PrimaryContainerId, 1);
                Assert.That(source.GetMagazine(otherWeapon.InstanceId), Is.Zero);
                window.DropItemNow(weapon);
                yield return null;
                WorldItem dropped = null;

                foreach (WorldItem candidate in Object.FindObjectsByType<WorldItem>())
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
            ItemInstance weapon = AddItem(model, FindDefinition(controller.Inventory.Database, AUG), window.PrimaryContainerId, 1);
            window.EquipItem(weapon);
            yield return WaitWhile(() => controller.EquipmentAnimations.IsBusy);
            controller.Player.CameraController.SetCursorLocked(true);
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            FirstPersonWeaponAmmoView view = controller.Player.GetComponentInChildren<FirstPersonWeaponAmmoView>(true);
            Assert.That(view.Text.text, Does.StartWith("0/0 "));
            yield return Tap(_mouse.leftButton);
            Assert.That(weapons.Magazine, Is.Zero);
            ItemInstance ammo = AddItem(model, FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ss190"), window.PrimaryContainerId, 10);
            Assert.That(view.Text.text, Does.StartWith("0/10 "));
            Assert.That(model.TryTransfer(ammo.InstanceId, window.SecondaryContainerId).Success, Is.True);
            Assert.That(view.Text.text, Does.StartWith("0/0 "));
            Assert.That(model.TryTransfer(ammo.InstanceId, window.PrimaryContainerId).Success, Is.True);
            yield return Tap(_keyboard.rKey);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Reloading));
            Assert.That(ammo.Amount, Is.EqualTo(10));
            yield return Tap(_keyboard.digit3Key);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Reloading));
            weapons.Hide();
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Hidden));
            Assert.That(ammo.Amount, Is.EqualTo(10));
            Assert.That(weapon.GetState(WeaponMagazineState.MAGAZINE_STATE), Is.Null);
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
        public IEnumerator AugIwpCartridgesStaySeparateAndSwitchOnT()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            FirstPersonWeaponController weapons = controller.Player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = AddItem(model, FindDefinition(controller.Inventory.Database, AUG), window.PrimaryContainerId, 1);
            ItemDefinition fmj = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ss190");
            ItemDefinition ep = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ap");
            AddItem(model, fmj, window.PrimaryContainerId, 15);
            ItemInstance epBox = AddItem(model, ep, window.PrimaryContainerId, 15);
            window.EquipItem(weapon);
            yield return WaitWhile(() => controller.EquipmentAnimations.IsBusy);
            controller.Player.CameraController.SetCursorLocked(true);
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            yield return Tap(_keyboard.rKey);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready, 8f);
            Assert.That(weapons.Magazine, Is.EqualTo(15));
            Assert.That(weapons.AmmoSection, Is.EqualTo("ammo_5.56x45_ss190"));
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
            Assert.That(weapons.AmmoSection, Is.EqualTo("ammo_5.56x45_ap"));
            Assert.That(model.GetItem(epBox.InstanceId), Is.Null);
            yield return Tap(_keyboard.tKey);
            yield return Tap(_keyboard.digit3Key);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Reloading));
            weapons.Hide();
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Hidden));
            Assert.That(CountAmmo(model, ep, window.PrimaryContainerId), Is.EqualTo(15));
            Assert.That(weapon.GetState(WeaponMagazineState.MAGAZINE_STATE), Is.EqualTo("0"));
        }

        [UnityTest]
        public IEnumerator AugSwitchReturnsFullMagazineAndRefusesWhenBackpackIsFull()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryModel model = controller.Inventory.Model;
            string backpack = controller.Window.PrimaryContainerId;
            ItemInstance weapon = AddItem(model, FindDefinition(controller.Inventory.Database, AUG), backpack, 1);
            ItemDefinition fmj = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ss190");
            ItemDefinition ep = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ap");
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
                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_ap"));
                Assert.That(source.GetReserve(weapon.InstanceId), Is.Zero);
                Assert.That(source.TrySwitchAmmo(weapon.InstanceId), Is.True);
                source.Reload(weapon.InstanceId, 30);
                Assert.That(source.GetMagazine(weapon.InstanceId), Is.EqualTo(30));
                Assert.That(source.GetAmmoSection(weapon.InstanceId), Is.EqualTo("ammo_5.56x45_ss190"));
                Assert.That(CountAmmo(model, ep, backpack), Is.EqualTo(15));
                Assert.That(source.GetReserve(weapon.InstanceId), Is.Zero);
            }
        }

        private static void AssertAimPose(StalkerPlayerController player, FirstPersonWeaponController weapons, XRayAnmAsset clip, float time)
        {
            FirstPersonWeaponAsset asset = weapons.ActiveWeapon;
            Assert.That(clip.Evaluate(time, out Vector3 offset, out Quaternion rotation), Is.True);
            Quaternion aimRotation = FirstPersonWeaponAsset.ToAimRotation(asset.AimOffsetRotation * weapons.AimFactor);
            Quaternion basis = aimRotation * asset.HandsRotation;
            Vector3 expected = asset.AimOffsetPosition * weapons.AimFactor + aimRotation * asset.HandsPosition + basis * offset;
            Assert.That(Vector3.Distance(player.Rig.Hands.transform.localPosition, expected), Is.LessThan(0.00001f));
            Assert.That(Quaternion.Angle(player.Rig.Hands.transform.localRotation, basis * rotation), Is.LessThan(0.05f));
        }

        [UnityTest]
        public IEnumerator BallisticsUseLoadedAmmoAndExactMuzzleWithAndWithoutSilencerInHipAndAim()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryModel model = controller.Inventory.Model;
            StalkerPlayerController player = controller.Player;
            FirstPersonWeaponController weapons = player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemInstance weapon = AddItem(model, FindDefinition(controller.Inventory.Database, AUG), controller.Window.PrimaryContainerId, 1);
            ItemDefinition fmj = FindDefinition(controller.Inventory.Database, "ammo_5.56x45_ss190");
            AddItem(model, fmj, controller.Window.PrimaryContainerId, 12);
            Assert.That(model.TryEquip(weapon.InstanceId, controller.Window.EquipmentContainerId).Success, Is.True);
            player.CameraController.SetCursorLocked(true);
            yield return Tap(_keyboard.digit3Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            using var source = new InventoryWeaponAmmoSource(model, controller.Window.PrimaryContainerId);
            source.Reload(weapon.InstanceId, 30);
            float expectedSpeed = weapon.Definition.GetComponent<WeaponStatsComponent>().BulletSpeed * fmj.GetComponent<AmmoStatsComponent>().SpeedMultiplier;
            Assert.That(source.GetBulletSpeed(weapon.InstanceId), Is.EqualTo(expectedSpeed).Within(0.01f));
            var shoot = typeof(FirstPersonWeaponController).GetMethod("Shoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            foreach (bool silenced in new[] { false, true })
            {
                if (silenced)
                {
                    ItemInstance silencer = AddItem(model, FindDefinition(controller.Inventory.Database, "wpn_sil_nato"), controller.Window.PrimaryContainerId, 1);
                    Assert.That(controller.WeaponAttachments.Install(silencer, weapon).Success, Is.True);
                }

                foreach (bool aiming in new[] { false, true })
                {
                    if (aiming)
                    {
                        Press(_mouse.rightButton, queueEventOnly: true);
                    }
                    else
                    {
                        Release(_mouse.rightButton, queueEventOnly: true);
                    }

                    yield return new WaitForSeconds(0.5f);
                    Assert.That(weapons.IsAiming, Is.EqualTo(aiming));
                    Assert.That(weapons.IsSilenced, Is.EqualTo(silenced));
                    FirstPersonWeaponView view = weapons.Model.GetComponent<FirstPersonWeaponView>();
                    Transform muzzle = silenced ? view.SilencedMuzzle : view.Muzzle;
                    Assert.That(weapons.Muzzle, Is.SameAs(muzzle));
                    Vector3 position = muzzle.position;
                    Vector3 forward = muzzle.TransformVector(Vector3.forward).normalized;
                    int magazine = weapons.Magazine;
                    shoot.Invoke(weapons, new object[] { weapons.ActiveWeapon });
                    BallisticProjectile newest = null;

                    foreach (BallisticProjectile projectile in Object.FindObjectsByType<BallisticProjectile>())
                    {
                        if (Vector3.Distance(projectile.LaunchPosition, position) < 0.000001f)
                        {
                            newest = projectile;
                        }
                    }

                    Assert.That(newest, Is.Not.Null);
                    Assert.That(weapons.Magazine, Is.EqualTo(magazine - 1));
                    Assert.That(newest.AmmoData, Is.SameAs(fmj.GetComponent<AmmoBallisticsComponent>().Data));
                    Assert.That(newest.SpeedMetersPerSecond, Is.EqualTo(source.GetBulletSpeed(weapon.InstanceId)).Within(0.01f));
                    float expectedDispersion = source.GetDispersionDegrees(weapon.InstanceId) * (aiming ? 1f : GameConfig.Load().HipFireDispersionMultiplier);
                    Assert.That(weapons.ShotDispersionDegrees, Is.EqualTo(expectedDispersion).Within(0.0001f));
                    Assert.That(Vector3.Angle(newest.LaunchDirection, forward), Is.LessThanOrEqualTo(expectedDispersion + 0.01f));
                }
            }

            Release(_mouse.rightButton, queueEventOnly: true);
        }

        [UnityTest]
        public IEnumerator Aks74nLoadsAndDetachesGp25ThroughTheInventory()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = window.Model;
            StalkerPlayerController player = controller.Player;
            FirstPersonWeaponController weapons = player.GetComponentInChildren<FirstPersonWeaponController>();
            ItemDefinition weaponDefinition = Resources.Load<ItemDefinition>("Items/Weapons/Assault Rifles/wpn_aks74n/wpn_ak74_definition");
            ItemDefinition launcherDefinition = Resources.Load<ItemDefinition>("Items/Addons/GrenadeLaunchers/gl_gp25/wpn_addon_grenade_launcher_definition");
            ItemDefinition otherLauncher = Resources.Load<ItemDefinition>("Items/Addons/GrenadeLaunchers/gl_ag36/wpn_addon_grenade_launcher_ag36_definition");
            ItemDefinition grenade = Resources.Load<ItemDefinition>("Items/Ammo/ammo_vog-25/ammo_vog-25_definition");
            ItemDefinition otherGrenade = Resources.Load<ItemDefinition>("Items/Ammo/ammo_40x46_m433/ammo_m209_definition");
            FirstPersonWeaponAmmoView ammoView = player.GetComponentInChildren<FirstPersonWeaponAmmoView>(true);
            var actionAudio = (AudioSource)typeof(FirstPersonWeaponController).GetField("_audio", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(weapons);
            WeaponStatsComponent stats = weaponDefinition.GetComponent<WeaponStatsComponent>();
            Assert.That(stats.SupportsGrenadeLauncher(otherLauncher), Is.False);

            ItemInstance weapon = AddItem(model, weaponDefinition, window.PrimaryContainerId, 1);
            ItemInstance launcher = AddItem(model, launcherDefinition, window.PrimaryContainerId, 1);
            Assert.That(model.TrySetItemStateInPlace(launcher, "condition", "0.63"), Is.True);
            ItemInstance loaded = weapon.CreateDraft();
            loaded.SetState(WeaponMagazineState.AMMO_SECTION_STATE, stats.AmmoClasses[0]);
            loaded.SetState(WeaponMagazineState.MAGAZINE_STATE, "3");
            Assert.That(model.TryReconfigureItem(weapon, loaded, weapon.Location.Position).Success, Is.True);
            float weaponWeight = weapon.UnitWeight;
            Assert.That(model.TryEquip(weapon.InstanceId, window.EquipmentContainerId).Success, Is.True);
            player.CameraController.SetCursorLocked(true);
            yield return Tap(weapon.Location.SlotId == SlotId.Primary ? _keyboard.digit3Key : _keyboard.digit2Key);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            Assert.That(weapons.ActiveWeapon, Is.SameAs(weaponDefinition.GetComponent<WeaponHudComponent>().FirstPersonWeapon));
            Assert.That(weapons.TrySwitchGrenadeMode(), Is.False);
            string rifleHud = ammoView.Text.text;
            Assert.That(FindChild(weapons.Model.transform, "mesh_03_gp25").gameObject.activeSelf, Is.False);

            var weaponContext = new InventoryActionContext(window, weapon, null);
            var addonContext = new InventoryActionContext(window, launcher, null);
            IInventoryItemAction install = window.Actions.Find(InventoryAttachmentActions.InstallActionId(weapon.Location.SlotId));
            IInventoryItemAction detach = window.Actions.Find(InventoryWeaponAttachments.DETACH_GRENADE_LAUNCHER_ACTION);
            var style = new ContextMenuOverhaulStyle(window, null);
            Sprite icon = window.Theme.Icon("ui_cmo_gl");
            Assert.That(icon, Is.Not.Null);
            Assert.That(style.Resolve(addonContext, install).Icon, Is.SameAs(icon));
            Assert.That(style.Resolve(weaponContext, detach).Icon, Is.SameAs(icon));
            Assert.That(controller.WeaponAttachments.Preview(launcher, weapon).Success, Is.True);
            Assert.That(install.IsEnabled(addonContext), Is.True);
            install.Execute(addonContext);
            Assert.That(weapons.HasGrenadeLauncher, Is.True);
            Assert.That(FindChild(weapons.Model.transform, "mesh_03_gp25").gameObject.activeSelf, Is.True);
            Assert.That(weapon.UnitWeight, Is.EqualTo(weaponWeight + launcherDefinition.Weight).Within(0.0001f));
            Assert.That(weapons.ActiveWeapon.Find(FirstPersonWeaponMotionKind.Idle, 0, true).HandsName, Is.EqualTo("ak74_idle_w_gl"));

            yield return Tap(_keyboard.vKey);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.GrenadeModeEnter));
            Assert.That(actionAudio.clip.name, Is.EqualTo("abakan_gl_activate"));
            Assert.That(ammoView.Text.text, Does.StartWith("0/0 ").And.Contain(grenade.ShortName));
            yield return WaitWhile(() => actionAudio.timeSamples <= 0 && weapons.State == FirstPersonWeaponState.SwitchingGrenadeMode);
            Assert.That(actionAudio.timeSamples, Is.GreaterThan(0));
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            Assert.That(weapons.IsGrenadeMode, Is.True);
            Assert.That(weapons.ActiveWeapon.Find(FirstPersonWeaponMotionKind.Idle, 0, true, true).HandsName, Is.EqualTo("ak74_idle_g"));
            float launcherFieldOfView = player.CameraController.Camera.fieldOfView;
            FirstPersonWeaponAsset asset = weapons.ActiveWeapon;
            Transform hands = weapons.HandsModel.transform;
            Vector3 iwpAimPosition = new(-0.0034f, 0.0066f, 0f);
            Vector3 iwpAimRotation = new(-0.00016f, 0.00139f, 0f);
            Assert.That(asset.GrenadeAimOffsetPosition, Is.EqualTo(iwpAimPosition));
            Assert.That(asset.GrenadeAimOffsetRotation, Is.EqualTo(iwpAimRotation));
            var aimIdle = Resources.Load<XRayAnmAsset>("Items/Weapons/Assault Rifles/wpn_aks74n/Animations/Blend/aim_idle_abakan_gl");
            Assert.That(aimIdle.Evaluate(0f, out Vector3 blendPosition, out Quaternion blendRotation), Is.True);
            Quaternion aimRotation = FirstPersonWeaponAsset.ToAimRotation(iwpAimRotation);
            Quaternion aimBasis = aimRotation * asset.HandsRotation;
            Vector3 expectedAimPosition = iwpAimPosition + aimRotation * asset.HandsPosition + aimBasis * blendPosition;
            Quaternion expectedAimRotation = aimBasis * blendRotation;
            Press(_mouse.rightButton, queueEventOnly: true);
            yield return WaitWhile(() => weapons.CurrentMotion != FirstPersonWeaponMotionKind.IdleAim || weapons.AimFactor < 1f);
            yield return WaitWhile(() => Quaternion.Angle(hands.localRotation, expectedAimRotation) > 0.05f || Vector3.Distance(hands.localPosition, expectedAimPosition) > 0.0001f);
            Assert.That(weapons.IsAiming, Is.True);
            Assert.That(weapons.ActiveWeapon.Find(FirstPersonWeaponMotionKind.IdleAim, 0, true, true).HandsName, Is.EqualTo("ak74_idle_aim_g"));
            Assert.That(player.CameraController.Camera.fieldOfView, Is.EqualTo(launcherFieldOfView).Within(0.001f));
            Release(_mouse.rightButton, queueEventOnly: true);
            yield return WaitWhile(() => weapons.IsAiming);
            yield return WaitWhile(() => Quaternion.Angle(hands.localRotation, asset.HandsRotation) > 0.05f || Vector3.Distance(hands.localPosition, asset.HandsPosition) > 0.0001f);
            AddItem(model, otherGrenade, window.PrimaryContainerId, 1);
            yield return Tap(_keyboard.rKey);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.Ready));
            Assert.That(weapon.GetState(WeaponAddonComponent.GRENADE_ROUND_STATE), Is.Null);

            ItemInstance stack = AddItem(model, grenade, window.PrimaryContainerId, 2);
            Assert.That(ammoView.Text.text, Does.StartWith("0/2 ").And.Contain(grenade.ShortName));
            yield return Tap(_keyboard.rKey);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.GrenadeReload));
            Assert.That(actionAudio.clip.name, Is.EqualTo("gp25_grenload"));
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.LoadingGrenade));
            GameObject reloadRound = FindChild(weapons.Model.transform, "mesh_19_wpn_vog").gameObject;
            Assert.That(reloadRound.activeSelf, Is.False);
            Assert.That(stack.Amount, Is.EqualTo(2));
            yield return WaitWhile(() => actionAudio.timeSamples <= 0 && weapons.State == FirstPersonWeaponState.LoadingGrenade);
            Assert.That(actionAudio.timeSamples, Is.GreaterThan(0));
            yield return WaitWhile(() => !reloadRound.activeSelf && weapons.State == FirstPersonWeaponState.LoadingGrenade);
            Assert.That(weapons.State, Is.EqualTo(FirstPersonWeaponState.LoadingGrenade));
            Assert.That(reloadRound.activeSelf, Is.True);
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            Assert.That(reloadRound.activeSelf, Is.True);
            Assert.That(stack.Amount, Is.EqualTo(1));
            Assert.That(ammoView.Text.text, Does.StartWith("1/1 ").And.Contain(grenade.ShortName));
            Assert.That(weapon.GetState(WeaponAddonComponent.GRENADE_ROUND_STATE), Is.EqualTo(grenade.DefinitionId));
            Assert.That(weapon.UnitWeight, Is.EqualTo(weaponWeight + launcherDefinition.Weight + grenade.Weight).Within(0.0001f));
            yield return Tap(_keyboard.rKey);
            Assert.That(weapons.Magazine, Is.EqualTo(3));
            Assert.That(stack.Amount, Is.EqualTo(1));
            Assert.That(weapon.GetState(WeaponAddonComponent.GRENADE_ROUND_STATE), Is.EqualTo(grenade.DefinitionId));

            yield return Tap(_keyboard.vKey);
            Assert.That(weapons.CurrentMotion, Is.EqualTo(FirstPersonWeaponMotionKind.GrenadeModeExit));
            Assert.That(actionAudio.clip.name, Is.EqualTo("abakan_gl_deactivate"));
            Assert.That(ammoView.Text.text, Is.EqualTo(rifleHud));
            yield return WaitWhile(() => actionAudio.timeSamples <= 0 && weapons.State == FirstPersonWeaponState.SwitchingGrenadeMode);
            Assert.That(actionAudio.timeSamples, Is.GreaterThan(0));
            yield return WaitWhile(() => weapons.State != FirstPersonWeaponState.Ready);
            Assert.That(weapons.IsGrenadeMode, Is.False);
            Assert.That(detach.GetLabel(weaponContext), Is.EqualTo("Снять подствольник"));
            Assert.That(detach.IsEnabled(weaponContext), Is.True);
            detach.Execute(weaponContext);
            ItemInstance returned = model.GetItem(launcher.InstanceId);
            Assert.That(returned.Definition, Is.SameAs(launcherDefinition));
            Assert.That(returned.GetState("condition"), Is.EqualTo("0.63"));
            Assert.That(returned.GetState(WeaponAddonComponent.GRENADE_ROUND_STATE), Is.Null);
            Assert.That(returned.UnitWeight, Is.EqualTo(launcherDefinition.Weight).Within(0.0001f));
            Assert.That(stack.Amount, Is.EqualTo(2));
            Assert.That(weapon.GetState(WeaponAddonComponent.GRENADE_ROUND_STATE), Is.Null);
            Assert.That(weapons.HasGrenadeLauncher, Is.False);
            Assert.That(FindChild(weapons.Model.transform, "mesh_03_gp25").gameObject.activeSelf, Is.False);
            Assert.That(controller.WeaponAttachments.Install(returned, weapon).Success, Is.True);
            Assert.That(weapon.GetState(WeaponAddonComponent.GRENADE_ROUND_STATE), Is.Null);
        }

        private static void AssertBoltPose(Transform bolt, Vector3 position, Quaternion rotation)
        {
            Assert.That(Vector3.Distance(bolt.localPosition, position), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(bolt.localRotation, rotation), Is.LessThan(0.1f));
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
            foreach (XRayParticleGroupPlayer player in Object.FindObjectsByType<XRayParticleGroupPlayer>())
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
