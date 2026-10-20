using System.Collections.Generic;
using NUnit.Framework;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player.Editor;
using UnityEditor;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class FirstPersonEquipmentAnimationTests
    {
        private const string HANDS_BONE = "bone";
        private const string ITEM_ROOT = "root";
        private const string ITEM_CHILD = "child";
        private const float CLIP_LENGTH = 1.0f;

        private GameObject _root;
        private GameObject _handsModel;
        private GameObject _itemModel;
        private FirstPersonOutfitCatalog _catalog;
        private FirstPersonEquipmentAnimationLibrary _library;
        private AnimationClip _handsClip;
        private AnimationClip _itemClip;
        private FirstPersonRig _rig;
        private FirstPersonHudAnimator _animator;
        private FirstPersonPickupAnimator _pickup;
        private readonly List<string> _events = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _events.Clear();
            _handsModel = new GameObject("hands");
            new GameObject(HANDS_BONE).transform.SetParent(_handsModel.transform, false);
            new GameObject(FirstPersonHudAnimator.ITEM_ANCHOR_BONE).transform.SetParent(_handsModel.transform.Find(HANDS_BONE), false);
            _itemModel = CreateItemModel();
            _catalog = ScriptableObject.CreateInstance<FirstPersonOutfitCatalog>();
            _catalog.SetEntries(new List<FirstPersonOutfitEntry> { new FirstPersonOutfitEntry(FirstPersonOutfitCatalog.NO_OUTFIT_SECTION, "actor", "actor_hud", string.Empty, _handsModel, string.Empty, string.Empty, string.Empty, null, string.Empty, null, false, FirstPersonOutfitResolution.Resolved) });
            _handsClip = new AnimationClip { name = "hands" };
            _handsClip.SetCurve(HANDS_BONE, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0.0f, 0.0f, CLIP_LENGTH, 1.0f));
            _itemClip = new AnimationClip { name = "item" };
            _itemClip.SetCurve(ITEM_ROOT, typeof(Transform), "localPosition.y", AnimationCurve.Linear(0.0f, 5.0f, CLIP_LENGTH, 5.0f));
            _itemClip.SetCurve(ITEM_ROOT + "/" + ITEM_CHILD, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0.0f, 0.0f, CLIP_LENGTH, 2.0f));
            _library = ScriptableObject.CreateInstance<FirstPersonEquipmentAnimationLibrary>();
            _library.SetData(new List<FirstPersonHudMotion>
            {
                new FirstPersonHudMotion(FirstPersonHudMotion.MakeId(FirstPersonEquipmentAnimationLibrary.HEADGEAR_FALLBACK_SECTION, FirstPersonEquipmentAnimationLibrary.ANM_EQUIP), _handsClip, _itemModel, _itemClip, null, null, 0.0f, new Vector3(0.0f, 0.0f, 0.03f), Vector3.zero),
                new FirstPersonHudMotion(FirstPersonHudMotion.MakeId("helm_hardhat_headgear_hud", FirstPersonEquipmentAnimationLibrary.ANM_EQUIP), _handsClip, null, null, null, null, 0.6f, Vector3.zero, Vector3.zero),
                new FirstPersonHudMotion(FirstPersonHudMotion.MakeId("outfit_animation_backpack_equip_stalker_hud", FirstPersonEquipmentAnimationLibrary.ANM_USE), _handsClip, null, null, null, null, 0.6f, Vector3.zero, Vector3.zero),
                new FirstPersonHudMotion(FirstPersonHudMotion.MakeId("outfit_animation_backpack_equip_dolg_hud", FirstPersonEquipmentAnimationLibrary.ANM_USE), _handsClip, null, null, null, null, 0.6f, Vector3.zero, Vector3.zero)
            }, new List<string> { "helm_hardhat_headgear_hud" }, new List<string> { "helm_bandana" });
            _root = new GameObject("rig");
            var handsRoot = new GameObject("HandsRoot").transform;
            handsRoot.SetParent(_root.transform, false);
            _rig = _root.AddComponent<FirstPersonRig>();
            var rig = new SerializedObject(_rig);
            rig.FindProperty("_catalog").objectReferenceValue = _catalog;
            rig.FindProperty("_handsRoot").objectReferenceValue = handsRoot;
            rig.ApplyModifiedPropertiesWithoutUndo();
            _animator = _root.AddComponent<FirstPersonHudAnimator>();
            var animator = new SerializedObject(_animator);
            animator.FindProperty("_rig").objectReferenceValue = _rig;
            animator.FindProperty("_library").objectReferenceValue = _library;
            animator.ApplyModifiedPropertiesWithoutUndo();
            _pickup = _root.AddComponent<FirstPersonPickupAnimator>();
            var pickup = new SerializedObject(_pickup);
            pickup.FindProperty("_rig").objectReferenceValue = _rig;
            pickup.FindProperty("_leftHand").objectReferenceValue = _handsClip;
            pickup.FindProperty("_rightHand").objectReferenceValue = _handsClip;
            pickup.ApplyModifiedPropertiesWithoutUndo();
            _animator.Started += motion => _events.Add("start " + motion.Id);
            _animator.Stopped += (motion, completed) => _events.Add("stop " + motion.Id + " " + completed);
            Assert.That(_rig.ApplyOutfit(FirstPersonOutfitCatalog.NO_OUTFIT_SECTION), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            _animator.Cancel();
            _pickup.Cancel();
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_handsModel);
            Object.DestroyImmediate(_itemModel);
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_library);
            Object.DestroyImmediate(_handsClip);
            Object.DestroyImmediate(_itemClip);
        }

        [Test]
        public void HeadgearSectionFallsBackToGasmask()
        {
            Assert.That(_library.GetHeadgearSection("helm_hardhat"), Is.EqualTo("helm_hardhat_headgear_hud"));
            Assert.That(_library.GetHeadgearSection("helm_unknown"), Is.EqualTo(FirstPersonEquipmentAnimationLibrary.HEADGEAR_FALLBACK_SECTION));
            Assert.That(_library.Find("HELM_HARDHAT_HEADGEAR_HUD", FirstPersonEquipmentAnimationLibrary.ANM_EQUIP), Is.Not.Null);
            Assert.That(_library.Find("helm_hardhat_headgear_hud", FirstPersonEquipmentAnimationLibrary.ANM_UNEQUIP), Is.Null);
            Assert.That(_library.IsHeadgearBlacklisted("helm_bandana"), Is.True);
            Assert.That(_library.IsHeadgearBlacklisted("helm_hardhat"), Is.False);
            Assert.That(_library.UsesFade("helm_hardhat_headgear_hud"), Is.True);
            Assert.That(_library.UsesFade(FirstPersonEquipmentAnimationLibrary.HEADGEAR_FALLBACK_SECTION), Is.False);
        }

        [Test]
        public void BackpackSectionFallsBackToStalkerCommunity()
        {
            Assert.That(_library.GetBackpackSection(FirstPersonEquipmentAnimationLibrary.BACKPACK_EQUIP_PREFIX, "dolg"), Is.EqualTo("outfit_animation_backpack_equip_dolg_hud"));
            Assert.That(_library.GetBackpackSection(FirstPersonEquipmentAnimationLibrary.BACKPACK_EQUIP_PREFIX, "army"), Is.EqualTo("outfit_animation_backpack_equip_stalker_hud"));
        }

        [Test]
        public void MotionShowsHandsAttachesItemAndRestoresOnFinish()
        {
            FirstPersonHudMotion motion = _library.Find(FirstPersonEquipmentAnimationLibrary.HEADGEAR_FALLBACK_SECTION, FirstPersonEquipmentAnimationLibrary.ANM_EQUIP);
            Assert.That(_animator.TryPlay(motion, 2.0f), Is.True);
            Assert.That(_animator.Duration, Is.EqualTo(CLIP_LENGTH / 2.0f).Within(0.0001f));
            Assert.That(_rig.Hands.activeSelf, Is.True);
            Assert.That(_rig.HandsOwner, Is.SameAs(_animator));
            Assert.That(_rig.Hands.transform.localPosition, Is.EqualTo(new Vector3(0.0f, 0.0f, 0.03f)));
            Assert.That(_animator.Item, Is.Not.Null);
            Assert.That(_animator.Item.transform.parent.name, Is.EqualTo(FirstPersonHudAnimator.ITEM_ANCHOR_BONE));
            _animator.Tick(0.25f);
            Transform itemRoot = _animator.Item.transform.Find(ITEM_ROOT);
            Assert.That(itemRoot.localPosition, Is.EqualTo(Vector3.zero), "the script motion item root bone is not forced to identity");
            Assert.That(itemRoot.Find(ITEM_CHILD).localPosition.x, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(_rig.Hands.transform.Find(HANDS_BONE).localPosition.x, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(_pickup.TryPlay(FirstPersonPickupHand.Left, null, null), Is.False, "pickup must wait for the busy hands");
            _animator.Tick(0.3f);
            Assert.That(_animator.IsPlaying, Is.False);
            Assert.That(_animator.Item, Is.Null);
            Assert.That(_rig.Hands.activeSelf, Is.False);
            Assert.That(_rig.HandsBusy, Is.False);
            Assert.That(_rig.Hands.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(_events, Is.EqualTo(new[] { "start " + motion.Id, "stop " + motion.Id + " True" }));
        }

        [Test]
        public void SecondMotionIsRejectedWhilePlayingAndCancelReportsIncomplete()
        {
            FirstPersonHudMotion motion = _library.Find("helm_hardhat_headgear_hud", FirstPersonEquipmentAnimationLibrary.ANM_EQUIP);
            Assert.That(_animator.TryPlay(motion, 1.0f), Is.True);
            Assert.That(_animator.TryPlay(motion, 1.0f), Is.False);
            _animator.Cancel();
            Assert.That(_events, Is.EqualTo(new[] { "start " + motion.Id, "stop " + motion.Id + " False" }));
            Assert.That(_pickup.TryPlay(FirstPersonPickupHand.Left, null, null), Is.True);
            Assert.That(_animator.TryPlay(motion, 1.0f), Is.False, "hud motion must wait for the pickup");
        }

        [Test]
        public void ScreenFadeFollowsCurveAndDecaysAfterStop()
        {
            var host = new GameObject("fade");
            try
            {
                StalkerScreenFade fade = host.AddComponent<StalkerScreenFade>();
                fade.Configure(new AnimationCurve(new Keyframe(0.0f, 0.0f), new Keyframe(0.4f, -0.8f), new Keyframe(0.6f, -0.8f), new Keyframe(1.0f, 0.0f)), null);
                fade.Play();
                fade.Tick(0.4f);
                Assert.That(fade.Value, Is.EqualTo(-0.8f).Within(0.001f));
                fade.Stop();
                fade.Tick(0.1f);
                Assert.That(fade.Factor, Is.EqualTo(0.9f).Within(0.001f));
                Assert.That(fade.Value, Is.EqualTo(-0.72f).Within(0.001f));
                fade.Tick(0.6f);
                Assert.That(fade.IsPlaying, Is.False);
                Assert.That(fade.Value, Is.EqualTo(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ItemUseCameraEffectIsIndependentFromStepEffects()
        {
            var host = new GameObject("camera");
            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(host.transform, false);
            var asset = ScriptableObject.CreateInstance<XRayAnmAsset>();
            try
            {
                StalkerCameraEffects effects = host.AddComponent<StalkerCameraEffects>();
                effects.PlayItemEffect(asset);
                Assert.That(effects.IsItemEffectPlaying, Is.True);
                effects.StopItemEffect();
                Assert.That(effects.IsItemEffectPlaying, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ImportedLibraryCoversEveryProjectHeadgearOutfitAndBackpack()
        {
            var library = AssetDatabase.LoadAssetAtPath<FirstPersonEquipmentAnimationLibrary>(FirstPersonEquipmentAnimationSetup.LIBRARY_PATH);
            Assert.That(library, Is.Not.Null, "equipment animations were not set up");
            Assert.That(library.Find(FirstPersonEquipmentAnimationLibrary.OUTFIT_SECTION, FirstPersonEquipmentAnimationLibrary.ANM_EQUIP), Is.Not.Null);
            Assert.That(library.Find(FirstPersonEquipmentAnimationLibrary.OUTFIT_FAST_SECTION, FirstPersonEquipmentAnimationLibrary.ANM_EQUIP), Is.Not.Null);
            Assert.That(library.Find(library.GetBackpackSection(FirstPersonEquipmentAnimationLibrary.BACKPACK_EQUIP_PREFIX, FirstPersonEquipmentAnimationLibrary.DEFAULT_COMMUNITY), FirstPersonEquipmentAnimationLibrary.ANM_USE).ItemModel, Is.Not.Null);
            Assert.That(library.Find(library.GetBackpackSection(FirstPersonEquipmentAnimationLibrary.BACKPACK_UNEQUIP_PREFIX, FirstPersonEquipmentAnimationLibrary.DEFAULT_COMMUNITY), FirstPersonEquipmentAnimationLibrary.ANM_USE).ItemModel, Is.Not.Null);
            Assert.That(library.HeadgearBlacklist, Is.SupersetOf(new[] { "helm_bandana", "helm_cloth_mask", "helm_rp_bala" }));
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/_Source/Items/Definitions/Head" }))
            {
                string text = System.IO.File.ReadAllText(AssetDatabase.GUIDToAssetPath(guid));
                int index = text.IndexOf("_section: ", System.StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                string section = text.Substring(index + 10).Split('\n')[0].Trim();
                if (library.IsHeadgearBlacklisted(section))
                {
                    continue;
                }

                string hud = library.GetHeadgearSection(section);
                FirstPersonHudMotion equip = library.Find(hud, FirstPersonEquipmentAnimationLibrary.ANM_EQUIP);
                FirstPersonHudMotion unequip = library.Find(hud, FirstPersonEquipmentAnimationLibrary.ANM_UNEQUIP);
                Assert.That(equip != null && equip.Hands != null && equip.ItemModel != null && equip.Item != null && equip.CameraEffect != null && equip.Sound != null, Is.True, section + " has no complete equip motion in " + hud);
                Assert.That(unequip != null && unequip.Hands != null && unequip.ItemModel != null && unequip.Item != null, Is.True, section + " has no complete unequip motion in " + hud);
            }
        }

        private static GameObject CreateItemModel()
        {
            var item = new GameObject("item");
            var root = new GameObject(ITEM_ROOT).transform;
            root.SetParent(item.transform, false);
            var child = new GameObject(ITEM_CHILD).transform;
            child.SetParent(root, false);
            XRayModel model = item.AddComponent<XRayModel>();
            model.SetImportData("item.ogf", 4, "Skeleton", 1.0f, new[] { root, child }, new[] { ITEM_ROOT, ITEM_CHILD }, new string[0], string.Empty, string.Empty, null, 0, 0, 0, new string[0]);
            return item;
        }
    }
}
