#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class FirstPersonBoltControllerTests
    {
        private readonly List<AnimationClip> _blendClips = new List<AnimationClip>();

        private GameObject _root;
        private GameObject _handsModel;
        private GameObject _itemModel;
        private AnimationClip _clip;
        private BoltDefinition _definition;
        private FirstPersonOutfitCatalog _catalog;
        private FirstPersonRig _rig;
        private FirstPersonBoltController _controller;
        private Scene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("BoltControllerTest");
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _root.SetActive(false);
            _handsModel = new GameObject("BoltHands");
            SceneManager.MoveGameObjectToScene(_handsModel, _scene);
            new GameObject("l_clavicle").transform.SetParent(_handsModel.transform, false);
            new GameObject(FirstPersonHudAnimator.ITEM_ANCHOR_BONE).transform.SetParent(_handsModel.transform, false);
            _itemModel = new GameObject("BoltHud");
            SceneManager.MoveGameObjectToScene(_itemModel, _scene);
            new GameObject("bolt").transform.SetParent(_itemModel.transform, false);
            _clip = new AnimationClip { name = "BoltTestMotion" };
            _clip.SetCurve("l_clavicle", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 0f));
            _definition = ScriptableObject.CreateInstance<BoltDefinition>();
            var definition = new SerializedObject(_definition);
            definition.FindProperty("_hudModel").objectReferenceValue = _itemModel;
            definition.FindProperty("_throwReleaseTime").floatValue = 0.9f;
            definition.FindProperty("_transitionDuration").floatValue = 0.2f;
            foreach (string property in new[] { "_draw", "_holster", "_idle", "_moving", "_sprint", "_bore", "_throwBegin", "_throwIdle", "_throwEnd" })
            {
                definition.FindProperty(property).objectReferenceValue = _clip;
            }
            definition.ApplyModifiedPropertiesWithoutUndo();
            _catalog = ScriptableObject.CreateInstance<FirstPersonOutfitCatalog>();
            _catalog.SetEntries(new List<FirstPersonOutfitEntry> { new FirstPersonOutfitEntry(FirstPersonOutfitCatalog.NO_OUTFIT_SECTION, "actor", "actor_hud", string.Empty, _handsModel, string.Empty, string.Empty, string.Empty, null, string.Empty, null, false, FirstPersonOutfitResolution.Resolved) });
            _rig = _root.AddComponent<FirstPersonRig>();
            var rig = new SerializedObject(_rig);
            rig.FindProperty("_catalog").objectReferenceValue = _catalog;
            rig.FindProperty("_handsRoot").objectReferenceValue = _root.transform;
            rig.ApplyModifiedPropertiesWithoutUndo();
            _rig.ApplyOutfit(FirstPersonOutfitCatalog.NO_OUTFIT_SECTION);
            var player = _root.AddComponent<StalkerPlayerController>();
            var playerData = new SerializedObject(player);
            playerData.FindProperty("_rig").objectReferenceValue = _rig;
            playerData.ApplyModifiedPropertiesWithoutUndo();
            _controller = _root.AddComponent<FirstPersonBoltController>();
            var controller = new SerializedObject(_controller);
            controller.FindProperty("_player").objectReferenceValue = player;
            controller.FindProperty("_definition").objectReferenceValue = _definition;
            controller.ApplyModifiedPropertiesWithoutUndo();
            _controller.Select();
            _controller.Tick(0f);
            _controller.Tick(1f);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.Ready));
        }

        [TearDown]
        public void TearDown()
        {
            if (_controller != null)
            {
                typeof(FirstPersonBoltController).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_controller, null);
            }
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_handsModel);
            Object.DestroyImmediate(_itemModel);
            Object.DestroyImmediate(_definition);
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_clip);
            foreach (AnimationClip clip in _blendClips)
            {
                Object.DestroyImmediate(clip);
            }
            _blendClips.Clear();
            EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void ChargedThrowAccumulatesOnlyAfterWindup()
        {
            Assert.That(_controller.BeginThrow(true), Is.True);
            _controller.Tick(0.5f);
            Assert.That(_controller.ThrowSpeed, Is.EqualTo(10f));
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.ThrowBegin));
            _controller.Tick(0.5f);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.ThrowIdle));
            Assert.That(_controller.ThrowSpeed, Is.EqualTo(10f));
            _controller.Tick(0.5f);
            Assert.That(_controller.ThrowSpeed, Is.EqualTo(15f));
            _controller.Tick(2f);
            Assert.That(_controller.ThrowSpeed, Is.EqualTo(20f));
            _controller.ReleaseThrow();
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.ThrowEnd));
        }

        [Test]
        public void ReleasingDuringWindupQueuesMinimumPowerThrow()
        {
            _controller.BeginThrow(true);
            _controller.Tick(0.1f);
            _controller.ReleaseThrow();
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.ThrowBegin));
            _controller.Tick(0.9f);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.ThrowEnd));
            Assert.That(_controller.ThrowSpeed, Is.EqualTo(10f));
        }

        [Test]
        public void PrimaryThrowCommitsFullPowerAfterWindup()
        {
            _controller.BeginThrow(false);
            Assert.That(_controller.ThrowSpeed, Is.EqualTo(20f));
            _controller.Tick(1f);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.ThrowEnd));
            Assert.That(_controller.ThrowSpeed, Is.EqualTo(20f));
            Assert.That(_controller.BeginThrow(false), Is.False);
        }

        [Test]
        public void TogglingBoltHolstersAndReleasesHandsWithoutInventory()
        {
            _controller.BeginThrow(true);
            _controller.Select();
            Assert.That(_controller.Selected, Is.False);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.Holstering));
            _controller.Tick(1f);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.Hidden));
            Assert.That(_controller.Model, Is.Null);
            Assert.That(_rig.HandsBusy, Is.False);
            _controller.Select();
            _controller.Tick(0f);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.Drawing));
            Assert.That(_rig.HandsOwner, Is.SameAs(_controller));
        }

        [Test]
        public void EquipmentRequestYieldsThenResumesSelectedBolt()
        {
            object equipment = new object();
            _rig.RequestHands(equipment);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.Holstering));
            _controller.Tick(1f);
            Assert.That(_rig.TryAcquireHands(equipment), Is.True);
            _controller.Tick(1f);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.Hidden));
            Assert.That(_rig.HandsOwner, Is.SameAs(equipment));
            _rig.ReleaseHands(equipment);
            _controller.Tick(0f);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.Drawing));
            Assert.That(_controller.Selected, Is.True);
        }

        [Test]
        public void DisableCancelsHeldThrowAndReleasesHands()
        {
            _controller.BeginThrow(true);
            _controller.Tick(1f);
            typeof(FirstPersonBoltController).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_controller, null);
            Assert.That(_controller.State, Is.EqualTo(FirstPersonBoltState.Hidden));
            Assert.That(_controller.Selected, Is.False);
            Assert.That(_rig.HandsBusy, Is.False);
            Assert.That(_controller.Model, Is.Null);
        }

        [Test]
        public void HandsAndBoltReachMidpointHalfwayThroughTransition()
        {
            object visual = PrepareBlendVisual();
            PlayBlendPose(visual, 0f);
            TickVisual(visual, 0.2f);
            AssertBlendPose(0f);
            PlayBlendPose(visual, 10f);
            AssertBlendPose(0f);
            TickVisual(visual, 0.1f);
            AssertBlendPose(5f);
            TickVisual(visual, 0.1f);
            AssertBlendPose(10f);
        }

        [Test]
        public void InterruptedTransitionPreservesCurrentHandsAndBoltPose()
        {
            object visual = PrepareBlendVisual();
            PlayBlendPose(visual, 0f);
            TickVisual(visual, 0.2f);
            PlayBlendPose(visual, 10f);
            TickVisual(visual, 0.1f);
            AssertBlendPose(5f);
            PlayBlendPose(visual, 20f);
            AssertBlendPose(5f);
            TickVisual(visual, 0.1f);
            AssertBlendPose(12.5f);
            TickVisual(visual, 0.1f);
            AssertBlendPose(20f);
        }

        private object PrepareBlendVisual()
        {
            _rig.Hands.transform.SetParent(_handsModel.transform, false);
            _rig.Hands.SetActive(true);
            Assert.That(_rig.Hands.activeInHierarchy, Is.True);
            Assert.That(_controller.Model.activeInHierarchy, Is.True);
            return typeof(FirstPersonBoltController).GetField("_visual", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_controller);
        }

        private void PlayBlendPose(object visual, float position)
        {
            AnimationClip hands = CreateBlendClip("l_clavicle", position);
            AnimationClip item = CreateBlendClip("bolt", position);
            visual.GetType().GetMethod("Play").Invoke(visual, new object[] { hands, item, 1f, 1f, true });
        }

        private AnimationClip CreateBlendClip(string bone, float position)
        {
            var clip = new AnimationClip { name = bone + "Pose" + position };
            clip.SetCurve(bone, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, position, 2f, position));
            _blendClips.Add(clip);
            return clip;
        }

        private void AssertBlendPose(float expected)
        {
            Assert.That(_rig.Hands.transform.Find("l_clavicle").localPosition.x, Is.EqualTo(expected).Within(0.02f), "Hands transition pose");
            Assert.That(_controller.Model.transform.Find("bolt").localPosition.x, Is.EqualTo(expected).Within(0.02f), "Bolt transition pose");
        }

        private static void TickVisual(object visual, float deltaTime)
        {
            visual.GetType().GetMethod("Tick").Invoke(visual, new object[] { deltaTime });
        }
    }
}
#endif
