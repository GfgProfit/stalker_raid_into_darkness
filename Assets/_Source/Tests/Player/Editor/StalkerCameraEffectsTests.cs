using System.Globalization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerCameraEffectsTests
    {
        public const string PROFILE_PATH = "Assets/_Source/Settings/Player/Camera/StalkerCameraEffectsProfile.asset";

        private const float DT = 1.0f / 60.0f;

        private StalkerMotorHarness _rig;
        private StalkerCameraEffectsProfile _profile;
        private StalkerCameraEffects _effects;
        private Transform _cameraRoot;
        private Transform _pivot;

        [SetUp]
        public void SetUp()
        {
            _profile = AssetDatabase.LoadAssetAtPath<StalkerCameraEffectsProfile>(PROFILE_PATH);
            Assert.That(_profile, Is.Not.Null, "StalkerCameraEffectsProfile.asset is missing; run Tools > X-Ray > Build Player Foley And Camera Effects.");
            _rig = new StalkerMotorHarness();
            _cameraRoot = new GameObject("CameraRoot").transform;
            _cameraRoot.SetParent(_rig.Transform, false);
            _cameraRoot.localPosition = new Vector3(0.0f, 1.6f, 0.0f);
            _pivot = new GameObject("CameraMotionPivot").transform;
            _pivot.SetParent(_cameraRoot, false);
            _effects = _rig.Motor.gameObject.AddComponent<StalkerCameraEffects>();
            var serialized = new SerializedObject(_effects);
            serialized.FindProperty("_profile").objectReferenceValue = _profile;
            serialized.FindProperty("_pivot").objectReferenceValue = _pivot;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _rig.Settle(0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        [Test]
        public void WeaponAimRotationComposesWithMotionWithoutAccumulating()
        {
            Quaternion aim = Quaternion.AngleAxis(1f, Vector3.forward);
            _effects.SetWeaponAimRotation(aim);
            Assert.That(Quaternion.Angle(_pivot.localRotation, aim), Is.LessThan(0.05f));
            Run(StalkerMoveFlags.Forward, 0.5f, DT);
            Quaternion combined = _pivot.localRotation;
            _effects.SetWeaponAimRotation(aim);
            Assert.That(Quaternion.Angle(_pivot.localRotation, combined), Is.LessThan(0.05f));
            _effects.SetWeaponAimRotation(Quaternion.identity);
            Assert.That(Quaternion.Angle(_pivot.localRotation, combined * Quaternion.Inverse(aim)), Is.LessThan(0.05f));
        }

        [Test]
        public void IdlePlayerHasNoBob()
        {
            Run(StalkerMoveFlags.None, 2.0f, DT);
            Assert.That(_effects.BobAmplitude, Is.EqualTo(0.0f));
            Assert.That(_pivot.localPosition.magnitude, Is.LessThan(0.000001f));
            Assert.That(Quaternion.Angle(_pivot.localRotation, Quaternion.identity), Is.LessThan(0.0001f));
        }

        [Test]
        public void AmplitudeFollowsMovementModeAndStance()
        {
            StalkerBobbingValues bob = _profile.Bobbing;
            float walk = PeakHeight(StalkerMoveFlags.Forward | StalkerMoveFlags.Accel);
            float run = PeakHeight(StalkerMoveFlags.Forward);
            float sprint = PeakHeight(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint);
            float crouch = PeakHeight(StalkerMoveFlags.Forward | StalkerMoveFlags.Crouch);
            TestContext.WriteLine($"peak bob: walk {F(walk)}, run {F(run)}, sprint {F(sprint)}, crouch run {F(crouch)} (walk_amplitude {F(bob.WalkAmplitude)}, run_amplitude {F(bob.RunAmplitude)})");
            Assert.That(walk, Is.EqualTo(bob.WalkAmplitude * bob.HeadBobFactor).Within(bob.WalkAmplitude * 0.05f));
            Assert.That(run, Is.EqualTo(bob.RunAmplitude * bob.HeadBobFactor).Within(bob.RunAmplitude * 0.05f));
            Assert.That(sprint, Is.EqualTo(run).Within(bob.RunAmplitude * 0.05f));
            Assert.That(crouch, Is.EqualTo(bob.RunAmplitude * bob.CrouchFactor * bob.HeadBobFactor).Within(bob.RunAmplitude * 0.05f));
        }

        [Test]
        public void BobStopsWhenPushingIntoAWall()
        {
            _rig.AddBox("Wall", new Vector3(0.0f, 1.0f, 1.2f), new Vector3(6.0f, 2.0f, 0.4f), Quaternion.identity);
            Run(StalkerMoveFlags.Forward, 3.0f, DT);
            float tail = 0.0f;
            Run(StalkerMoveFlags.Forward, 1.0f, DT, () => tail = Mathf.Max(tail, _effects.BobAmplitude));
            TestContext.WriteLine($"against wall: speed {F(_rig.HorizontalSpeed)} m/s, flags {_rig.Motor.RealFlags}, max bob amplitude {F(tail)}");
            Assert.That(_rig.Motor.RealFlags.Has(StalkerMoveFlags.AnyMove), Is.False);
            Assert.That(tail, Is.EqualTo(0.0f));
            Assert.That(_pivot.localPosition.magnitude, Is.LessThan(0.000001f));
        }

        [Test]
        public void BobFadesOutSmoothlyAfterStop()
        {
            Run(StalkerMoveFlags.Forward, 2.0f, DT);
            Vector3 previous = _pivot.localPosition;
            float maxStep = 0.0f;
            float settled = -1.0f;
            float time = 0.0f;
            Run(StalkerMoveFlags.None, 1.5f, DT, () =>
            {
                time += DT;
                maxStep = Mathf.Max(maxStep, (_pivot.localPosition - previous).magnitude);
                previous = _pivot.localPosition;
                if (settled < 0.0f && _effects.BobReminder <= 0.0f)
                {
                    settled = time;
                }
            });

            TestContext.WriteLine($"fade: reminder zero after {F(settled)} s, max per-frame offset change {F(maxStep)} m");
            Assert.That(settled, Is.GreaterThan(0.0f).And.LessThan(1.0f / _profile.Bobbing.SpeedReminder + 0.5f));
            Assert.That(maxStep, Is.LessThan(_profile.Bobbing.RunAmplitude * 0.5f));
            Assert.That(_pivot.localPosition.magnitude, Is.LessThan(0.000001f));
        }

        [Test]
        public void OffsetDoesNotAccumulate()
        {
            for (int i = 0; i < 5; i++)
            {
                Run(StalkerMoveFlags.Forward | (i % 2 == 0 ? StalkerMoveFlags.None : StalkerMoveFlags.Sprint), 1.3f, DT);
                _effects.OnContact(10.0f);
                Run(StalkerMoveFlags.None, 0.7f, DT);
            }

            Run(StalkerMoveFlags.None, 2.0f, DT);
            Assert.That(_pivot.localPosition.magnitude, Is.LessThan(0.000001f));
            Assert.That(Quaternion.Angle(_pivot.localRotation, Quaternion.identity), Is.LessThan(0.0001f));
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void FadeAndAmplitudeDoNotDependOnFrameRate(int fps)
        {
            float dt = 1.0f / fps;
            float reminder = -1.0f;
            float time = 0.0f;
            Run(StalkerMoveFlags.Forward, 2.0f, dt, () =>
            {
                time += dt;
                if (reminder < 0.0f && time >= 0.1f)
                {
                    reminder = _effects.BobReminder;
                }
            });

            float expected = Mathf.Min(1.0f, _profile.Bobbing.SpeedReminder * 0.1f);
            TestContext.WriteLine($"{fps} fps: reminder at 0.1 s {F(reminder)}, amplitude {F(_effects.BobAmplitude)}");
            Assert.That(reminder, Is.EqualTo(expected).Within(_profile.Bobbing.SpeedReminder * Mathf.Max(dt, 1.0f / 60.0f) * 3.0f));
            Assert.That(_effects.BobAmplitude, Is.EqualTo(_profile.Bobbing.RunAmplitude * _profile.Bobbing.HeadBobFactor).Within(0.00001f));
        }

        [Test]
        public void LandingDipDependsOnContactSpeedAndRecovers()
        {
            float soft = LandingDip(5.0f);
            float hard = LandingDip(20.0f);
            StalkerFallEffectValues fall = _profile.Fall;
            float expected = fall.MaxDistance * Mathf.Pow(20.0f / fall.CrashSpeedMax, 2.0f) * _profile.LandingScale;
            TestContext.WriteLine($"landing dip: 5 m/s {F(soft)} m, 20 m/s {F(hard)} m (EffectorFall expected {F(expected)} m)");
            Assert.That(hard, Is.GreaterThan(soft));
            Assert.That(hard, Is.EqualTo(expected).Within(expected * 0.05f));
            Run(StalkerMoveFlags.None, 1.0f / fall.Speed + 0.1f, DT);
            Assert.That(_pivot.localPosition.magnitude, Is.LessThan(0.000001f));
        }

        [Test]
        public void EffectsNeverTouchLookOrientation()
        {
            Quaternion root = _cameraRoot.rotation;
            Quaternion player = _rig.Transform.rotation;
            _effects.OnJump();
            Run(StalkerMoveFlags.Forward, 1.0f, DT);
            _effects.OnLanded(12.0f);
            _effects.OnContact(12.0f);
            Run(StalkerMoveFlags.Forward, 0.5f, DT);
            Assert.That(Quaternion.Angle(root, _cameraRoot.rotation), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(player, _rig.Transform.rotation), Is.LessThan(0.0001f));
            float maxAngle = 0.0f;
            Run(StalkerMoveFlags.Forward, 1.0f, DT, () => maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(_pivot.localRotation, Quaternion.identity)));
            TestContext.WriteLine($"max pivot rotation while running: {F(maxAngle)} deg");
            Assert.That(maxAngle, Is.LessThan(3.0f));
        }

        [Test]
        public void JumpAndLandingAnmEffectsFollowReanimationProject()
        {
            StalkerAnmEffectValues anm = _profile.Anm;
            Assert.That(anm.Jump, Is.Not.Null, "jump_crp.anm was not imported");
            Assert.That(anm.Landing.Length, Is.GreaterThan(0), "landing_crp*.anm were not imported");
            _effects.OnJump();
            Assert.That(_effects.IsPlaying(StalkerCameraEffectSlot.Jump), Is.True);
            Run(StalkerMoveFlags.None, anm.Jump.Duration + 0.1f, DT);
            Assert.That(_effects.IsPlaying(StalkerCameraEffectSlot.Jump), Is.False);
            _effects.OnLanded(anm.LandingSpeed - 0.5f);
            Assert.That(_effects.IsPlaying(StalkerCameraEffectSlot.Landing), Is.False, "soft landing must not play landing_crp");
            Run(StalkerMoveFlags.None, 0.1f, DT);
            _effects.OnLanded(anm.LandingSpeed + 2.0f);
            Assert.That(_effects.IsPlaying(StalkerCameraEffectSlot.Landing), Is.True);
            Run(StalkerMoveFlags.None, 3.0f, DT);
            Assert.That(_pivot.localPosition.magnitude, Is.LessThan(0.000001f));
        }

        [Test]
        public void DisablingEffectsKeepsCameraNeutral()
        {
            _effects.EffectsEnabled = false;
            float max = 0.0f;
            Run(StalkerMoveFlags.Forward, 2.0f, DT, () => max = Mathf.Max(max, _pivot.localPosition.magnitude));
            Assert.That(max, Is.LessThan(0.000001f));
        }

        private float PeakHeight(StalkerMoveFlags wish)
        {
            _rig.Place(Vector3.zero);
            Run(StalkerMoveFlags.None, 1.0f, DT);
            Run(wish, 1.5f, DT);
            float peak = 0.0f;
            Run(wish, 2.0f, DT, () => peak = Mathf.Max(peak, _effects.MotionOffset.y));
            return peak;
        }

        private float LandingDip(float speed)
        {
            Run(StalkerMoveFlags.None, 1.0f, DT);
            _effects.OnContact(speed);
            float dip = 0.0f;
            Run(StalkerMoveFlags.None, 0.4f, DT, () => dip = Mathf.Max(dip, Mathf.Abs(_effects.MotionOffset.y)));
            return dip;
        }

        private void Run(StalkerMoveFlags wish, float seconds, float deltaTime, System.Action onFrame = null)
        {
            _rig.Run(wish, seconds, deltaTime, _ =>
            {
                _effects.Tick(_rig.Motor.State, false, -1.0f, deltaTime);
                onFrame?.Invoke();
            });
        }

        private static string F(float value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }
    }
}
