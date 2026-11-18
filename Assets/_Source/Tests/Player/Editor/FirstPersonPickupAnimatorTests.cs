using System.Collections.Generic;
using NUnit.Framework;
using RaidIntoDarkness.FirstPerson;
using UnityEditor;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class FirstPersonPickupAnimatorTests
    {
        private const string BONE = "bone";
        private const string PICKUP_MOTIONS_PATH = "Assets/_Source/FirstPerson/Animations/Hands/liz_fdda_redone_item_pickup.omf";
        private const string OUTFIT_CATALOG_PATH = "Assets/_Source/FirstPerson/Outfits/OutfitCatalog.asset";
        private const float CLIP_LENGTH = 1.0f;
        private const int SAMPLES = 2000;

        private GameObject _root;
        private GameObject _handsModel;
        private FirstPersonOutfitCatalog _catalog;
        private AnimationClip _left;
        private AnimationClip _right;
        private FirstPersonRig _rig;
        private FirstPersonPickupAnimator _pickup;
        private int _takes;
        private readonly List<bool> _finished = new List<bool>();

        [SetUp]
        public void SetUp()
        {
            _takes = 0;
            _finished.Clear();
            _handsModel = new GameObject("hands");
            new GameObject(BONE).transform.SetParent(_handsModel.transform, false);
            _catalog = ScriptableObject.CreateInstance<FirstPersonOutfitCatalog>();
            _catalog.SetEntries(new List<FirstPersonOutfitEntry> { new FirstPersonOutfitEntry(FirstPersonOutfitCatalog.NO_OUTFIT_SECTION, "actor", "actor_hud", string.Empty, _handsModel, string.Empty, string.Empty, string.Empty, null, string.Empty, null, false, FirstPersonOutfitResolution.Resolved) });
            _left = CreateClip("left", 1.0f);
            _right = CreateClip("right", -1.0f);
            _root = new GameObject("rig");
            var handsRoot = new GameObject("HandsRoot").transform;
            handsRoot.SetParent(_root.transform, false);
            _rig = _root.AddComponent<FirstPersonRig>();
            var rig = new SerializedObject(_rig);
            rig.FindProperty("_catalog").objectReferenceValue = _catalog;
            rig.FindProperty("_handsRoot").objectReferenceValue = handsRoot;
            rig.ApplyModifiedPropertiesWithoutUndo();
            _pickup = _root.AddComponent<FirstPersonPickupAnimator>();
            var pickup = new SerializedObject(_pickup);
            pickup.FindProperty("_rig").objectReferenceValue = _rig;
            pickup.FindProperty("_leftHand").objectReferenceValue = _left;
            pickup.FindProperty("_rightHand").objectReferenceValue = _right;
            pickup.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(_rig.ApplyOutfit(FirstPersonOutfitCatalog.NO_OUTFIT_SECTION), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            _pickup.Cancel();
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_handsModel);
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_left);
            Object.DestroyImmediate(_right);
        }

        [Test]
        public void HandsAreHiddenUntilPickUp()
        {
            Assert.That(_rig.Hands, Is.Not.Null);
            Assert.That(_rig.Hands.activeSelf, Is.False);
            Assert.That(_pickup.IsPlaying, Is.False);
        }

        [TestCase(0.0f, FirstPersonPickupHand.Left)]
        [TestCase(0.49f, FirstPersonPickupHand.Left)]
        [TestCase(0.5f, FirstPersonPickupHand.Right)]
        [TestCase(0.99f, FirstPersonPickupHand.Right)]
        public void FreeHandsChooseByRandomValue(float randomValue, FirstPersonPickupHand expected)
        {
            Assert.That(FirstPersonPickupAnimator.ChooseFreeHand(randomValue), Is.EqualTo(expected));
        }

        [Test]
        public void RandomChoiceUsesBothHandsWithoutStrictAlternation()
        {
            Random.State state = Random.state;
            Random.InitState(20261019);
            int left = 0;
            int repeats = 0;
            FirstPersonPickupHand previous = FirstPersonPickupHand.LeftWithWeapon;
            for (int i = 0; i < SAMPLES; i++)
            {
                FirstPersonPickupHand hand = FirstPersonPickupAnimator.ChooseFreeHand(Random.value);
                Assert.That(hand, Is.Not.EqualTo(FirstPersonPickupHand.LeftWithWeapon));
                left += hand == FirstPersonPickupHand.Left ? 1 : 0;
                repeats += hand == previous ? 1 : 0;
                previous = hand;
            }

            Random.state = state;
            Assert.That(left, Is.InRange(SAMPLES * 0.45f, SAMPLES * 0.55f));
            Assert.That(repeats, Is.GreaterThan(SAMPLES / 4));
        }

        [Test]
        public void SecondPickUpIsRejectedWhilePlaying()
        {
            Assert.That(_pickup.TryPlay(FirstPersonPickupHand.Left, OnTake, OnFinished), Is.True);
            Assert.That(_rig.Hands.activeSelf, Is.True);
            Assert.That(_pickup.TryPlay(FirstPersonPickupHand.Right, OnTake, OnFinished), Is.False);
            Assert.That(_pickup.TryPlay(OnTake, OnFinished), Is.False);
            Assert.That(_pickup.CurrentHand, Is.EqualTo(FirstPersonPickupHand.Left));
        }

        [Test]
        public void ItemIsTakenOnceAtHalfAndHandsHideAfterFinish()
        {
            Assert.That(_pickup.TryPlay(FirstPersonPickupHand.Right, OnTake, OnFinished), Is.True);
            float duration = CLIP_LENGTH / FirstPersonPickupAnimator.DEFAULT_SPEED;
            Assert.That(_pickup.Duration, Is.EqualTo(duration).Within(0.0001f));
            _pickup.Tick(duration * 0.4f);
            Assert.That(_takes, Is.EqualTo(0));
            Assert.That(_rig.Hands.transform.Find(BONE).localPosition.x, Is.LessThan(0.0f));
            _pickup.Tick(duration * 0.2f);
            Assert.That(_takes, Is.EqualTo(1));
            _pickup.Tick(duration * 0.3f);
            Assert.That(_takes, Is.EqualTo(1));
            Assert.That(_finished, Is.Empty);
            _pickup.Tick(duration * 0.2f);
            Assert.That(_takes, Is.EqualTo(1));
            Assert.That(_finished, Is.EqualTo(new[] { true }));
            Assert.That(_pickup.IsPlaying, Is.False);
            Assert.That(_rig.Hands.activeSelf, Is.False);
        }

        [Test]
        public void NextPickUpStartsFromTheBeginning()
        {
            Assert.That(_pickup.TryPlay(FirstPersonPickupHand.Left, OnTake, OnFinished), Is.True);
            _pickup.Tick(10.0f);
            Assert.That(_pickup.TryPlay(FirstPersonPickupHand.Left, OnTake, OnFinished), Is.True);
            Assert.That(_pickup.Elapsed, Is.EqualTo(0.0f));
            Assert.That(_rig.Hands.transform.Find(BONE).localPosition.x, Is.EqualTo(0.0f).Within(0.0001f));
            _pickup.Tick(0.1f);
            Assert.That(_rig.Hands.transform.Find(BONE).localPosition.x, Is.EqualTo(0.1f * FirstPersonPickupAnimator.DEFAULT_SPEED).Within(0.001f));
        }

        [Test]
        public void CancelBeforeTakeKeepsItemAndHidesHands()
        {
            Assert.That(_pickup.TryPlay(FirstPersonPickupHand.Left, OnTake, OnFinished), Is.True);
            _pickup.Tick(0.1f);
            _pickup.Cancel();
            Assert.That(_takes, Is.EqualTo(0));
            Assert.That(_finished, Is.EqualTo(new[] { false }));
            Assert.That(_rig.Hands.activeSelf, Is.False);
            _pickup.Cancel();
            Assert.That(_finished.Count, Is.EqualTo(1));
        }

        [Test]
        public void SpeedIsConfigurable()
        {
            _pickup.Speed = 2.0f;
            Assert.That(_pickup.TryPlay(FirstPersonPickupHand.Left, OnTake, OnFinished), Is.True);
            Assert.That(_pickup.Duration, Is.EqualTo(CLIP_LENGTH / 2.0f).Within(0.0001f));
        }

        [Test]
        public void ImportedPickUpMotionsMatchEveryHandsModel()
        {
            var library = AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(PICKUP_MOTIONS_PATH);
            Assert.That(library, Is.Not.Null, "pickup OMF was not imported");
            Assert.That(library.TryGetClip(FirstPersonPickupAnimator.LEFT_HAND_MOTION, out AnimationClip left), Is.True);
            Assert.That(library.TryGetClip(FirstPersonPickupAnimator.RIGHT_HAND_MOTION, out AnimationClip right), Is.True);
            Assert.That(left.length, Is.GreaterThan(0.0f));
            Assert.That(right.length, Is.GreaterThan(0.0f));
            var catalog = AssetDatabase.LoadAssetAtPath<FirstPersonOutfitCatalog>(OUTFIT_CATALOG_PATH);
            Assert.That(catalog, Is.Not.Null);
            foreach (FirstPersonOutfitEntry entry in catalog.Entries)
            {
                if (entry.HandsModel == null)
                {
                    continue;
                }

                foreach (AnimationClip clip in new[] { left, right })
                {
                    foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        Assert.That(binding.path.Length == 0 || entry.HandsModel.transform.Find(binding.path) != null, Is.True, $"{clip.name}: '{binding.path}' is missing in {entry.HandsModel.name}");
                    }
                }
            }
        }

        private static AnimationClip CreateClip(string name, float direction)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve(BONE, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0.0f, 0.0f, CLIP_LENGTH, direction));
            return clip;
        }

        private void OnTake()
        {
            _takes++;
        }

        private void OnFinished(bool taken)
        {
            _finished.Add(taken);
        }
    }
}
