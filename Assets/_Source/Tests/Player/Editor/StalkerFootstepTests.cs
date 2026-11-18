using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerFootstepTests
    {
        public const string TIMING_PATH = "Assets/_Source/Settings/Player/Audio/StalkerFootstepTiming.asset";

        private const float DT = 1.0f / 60.0f;

        private StalkerMotorHarness _rig;
        private StalkerFootstepTimingLibrary _library;
        private StalkerFootstepSynchronizer _footsteps;
        private readonly List<StalkerFootstep> _steps = new List<StalkerFootstep>();

        [SetUp]
        public void SetUp()
        {
            _library = AssetDatabase.LoadAssetAtPath<StalkerFootstepTimingLibrary>(TIMING_PATH);
            Assert.That(_library, Is.Not.Null, "StalkerFootstepTiming.asset is missing; run Tools > X-Ray > Build Player Foley And Camera Effects.");
            _steps.Clear();
            _rig = new StalkerMotorHarness();
            _footsteps = _rig.Motor.gameObject.AddComponent<StalkerFootstepSynchronizer>();
            var serialized = new SerializedObject(_footsteps);
            serialized.FindProperty("_library").objectReferenceValue = _library;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _footsteps.Footstep += step => _steps.Add(step);
            _rig.Settle(0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        [Test]
        public void IdleProducesNoSteps()
        {
            Run(StalkerMoveFlags.None, 3.0f);
            Assert.That(_steps, Is.Empty);
        }

        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.Accel, "norm_walk_fwd_0")]
        [TestCase(StalkerMoveFlags.Forward, "norm_run_fwd_0")]
        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint, "norm_escape_00")]
        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.Crouch | StalkerMoveFlags.Accel, "cr_walk_fwd_0")]
        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.Crouch, "cr_run_fwd_0")]
        [TestCase(StalkerMoveFlags.LeftStrafe | StalkerMoveFlags.Accel, "norm_walk_ls_0")]
        public void StepRateFollowsXRayLegsTiming(StalkerMoveFlags wish, string legs)
        {
            Assert.That(_library.TryGetLegs(legs, out StalkerLegsMotionSteps timing), Is.True, legs + " has no step_params");
            Run(wish, 1.0f);
            _steps.Clear();
            const float SECONDS = 6.0f;
            Run(wish, SECONDS);
            float expected = SECONDS / timing.CycleTime * 2.0f;
            TestContext.WriteLine($"{legs}: cycle {F(timing.CycleTime)} s, steps {_steps.Count}, expected {F(expected)}, power L {F(timing.LeftPower)} R {F(timing.RightPower)}");
            Assert.That(_steps.Count, Is.EqualTo(expected).Within(2.0f));
            AssertAlternating();
            foreach (StalkerFootstep step in _steps)
            {
                Assert.That(step.LegsMotion, Is.EqualTo(legs));
                Assert.That(step.Power, Is.EqualTo(timing.GetPower(step.Side)));
            }
        }

        [Test]
        public void RunAndSprintStepFasterThanWalk()
        {
            float walk = Rate(StalkerMoveFlags.Forward | StalkerMoveFlags.Accel);
            float run = Rate(StalkerMoveFlags.Forward);
            float sprint = Rate(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint);
            TestContext.WriteLine($"steps per second: walk {F(walk)}, run {F(run)}, sprint {F(sprint)}");
            Assert.That(run, Is.GreaterThan(walk));
            Assert.That(sprint, Is.GreaterThanOrEqualTo(run * 0.95f));
        }

        [Test]
        public void NoStepsWhileAirborne()
        {
            Run(StalkerMoveFlags.Forward, 1.0f);
            _steps.Clear();
            bool airborne = false;
            int airSteps = 0;
            _rig.Frame(StalkerMoveFlags.Forward | StalkerMoveFlags.Jump, DT);
            _footsteps.Tick(_rig.Motor.State, null, DT);
            Run(StalkerMoveFlags.Forward, 1.5f, () =>
            {
                StalkerMovementState state = _rig.Motor.State;
                bool inAir = state.IsJumping || state.Environment == StalkerEnvironment.InAir;
                airborne |= inAir;
                if (inAir && _steps.Count > 0)
                {
                    airSteps += _steps.Count;
                    _steps.Clear();
                }
            });

            Assert.That(airborne, Is.True, "the jump did not leave the ground");
            Assert.That(airSteps, Is.EqualTo(0));
        }

        [Test]
        public void NoStepsWhenBlockedByAWall()
        {
            _rig.AddBox("Wall", new Vector3(0.0f, 1.0f, 1.2f), new Vector3(6.0f, 2.0f, 0.4f), Quaternion.identity);
            Run(StalkerMoveFlags.Forward, 3.0f);
            _steps.Clear();
            Run(StalkerMoveFlags.Forward, 3.0f);
            TestContext.WriteLine($"blocked: speed {F(_rig.HorizontalSpeed)}, flags {_rig.Motor.RealFlags}, steps {_steps.Count}");
            Assert.That(_steps, Is.Empty);
        }

        [Test]
        public void EachContactRaisesExactlyOneEvent()
        {
            Run(StalkerMoveFlags.Forward, 8.0f, null, 1.0f / 144.0f);
            int left = _steps.FindAll(step => step.Side == StalkerFootSide.Left).Count;
            int right = _steps.Count - left;
            TestContext.WriteLine($"144 fps run: L {left}, R {right}");
            Assert.That(Mathf.Abs(left - right), Is.LessThanOrEqualTo(1));
            AssertAlternating();
        }

        [Test]
        public void StepCycleMapsContactsToBobZeroCrossings()
        {
            var contacts = new List<StalkerFootContact> { new StalkerFootContact(0.1f, StalkerFootSide.Right), new StalkerFootContact(0.6f, StalkerFootSide.Left) };
            Assert.That(StalkerFootstepSynchronizer.GetStepCycle(0.6f, contacts), Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(StalkerFootstepSynchronizer.GetStepCycle(0.1f, contacts), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(StalkerFootstepSynchronizer.GetStepCycle(0.85f, contacts), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(StalkerFootstepSynchronizer.GetStepCycle(0.35f, contacts), Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(StalkerFootstepSynchronizer.GetStepCycle(0.2f, 0.2f, 0.65f), Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(StalkerFootstepSynchronizer.GetStepCycle(0.65f, 0.2f, 0.65f), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void SwmLocomotionClipsHaveAlternatingContacts()
        {
            int locomotion = 0;
            foreach (StalkerBodyMotionSteps body in _library.BodyMotions)
            {
                if (body.Contacts.Count == 0 || body.Motion.IndexOf("turn", System.StringComparison.Ordinal) >= 0 || body.Motion.IndexOf("jump", System.StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                locomotion++;
                var text = new System.Text.StringBuilder();
                foreach (StalkerFootContact contact in body.Contacts)
                {
                    text.Append(contact.Side == StalkerFootSide.Left ? " L" : " R").Append(F(contact.Phase));
                }

                TestContext.WriteLine($"{body.Motion} ({F(body.Length)} s):{text}");
                Assert.That(body.Clip, Is.Not.Null, body.Motion);
                Assert.That(HasSide(body.Contacts, StalkerFootSide.Left), Is.True, body.Motion);
                Assert.That(HasSide(body.Contacts, StalkerFootSide.Right), Is.True, body.Motion);
            }

            Assert.That(locomotion, Is.GreaterThan(5));
        }

        [Test]
        public void UnsupportedGroundGraceConsumesContactWithoutRestartingCadence()
        {
            StalkerMovementState state = TimingState(StalkerMoveFlags.Forward | StalkerMoveFlags.Accel);
            _footsteps.Tick(state, null, 0f, true);
            _footsteps.Tick(state, null, 0.15f, true);
            _footsteps.Tick(state, null, 0.1f, false);
            Assert.That(_steps, Is.Empty);
            Assert.That(_footsteps.Phase, Is.EqualTo(0.25f).Within(0.00001f));
            _footsteps.Tick(state, null, 0f, true);
            Assert.That(_steps, Is.Empty);
            _footsteps.Tick(state, null, 0.39f, true);
            Assert.That(_steps, Is.Empty);
            _footsteps.Tick(state, null, 0.02f, true);
            Assert.That(_steps.Count, Is.EqualTo(1));
            Assert.That(_steps[0].Side, Is.EqualTo(StalkerFootSide.Right));
        }

        [Test]
        public void RepeatedShortSupportGapsKeepTheNextValidContactAvailable()
        {
            StalkerMovementState state = TimingState(StalkerMoveFlags.Forward | StalkerMoveFlags.Accel);
            _footsteps.Tick(state, null, 0f, true);
            for (int frame = 0; frame < 180; frame++)
            {
                bool supported = frame % 10 >= 2;
                int previous = _steps.Count;
                _footsteps.Tick(state, null, DT, supported);
                Assert.That(_steps.Count - previous, Is.LessThanOrEqualTo(1));
                if (supported == false)
                {
                    Assert.That(_steps.Count, Is.EqualTo(previous));
                }
            }
            Assert.That(_steps.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(_steps.Count, Is.LessThanOrEqualTo(6));
        }

        [Test]
        public void UnsupportedMultipleCyclesCannotReplayQueuedContacts()
        {
            StalkerMovementState state = TimingState(StalkerMoveFlags.Forward | StalkerMoveFlags.Accel);
            _footsteps.Tick(state, null, 0f, true);
            _footsteps.Tick(state, null, 3.66f, false);
            Assert.That(_steps, Is.Empty);
            _footsteps.Tick(state, null, 0f, true);
            Assert.That(_steps, Is.Empty);
            _footsteps.Tick(state, null, 0.05f, true);
            Assert.That(_steps, Is.Empty);
            for (int frame = 0; frame < 35; frame++)
            {
                int previous = _steps.Count;
                _footsteps.Tick(state, null, DT, true);
                Assert.That(_steps.Count - previous, Is.LessThanOrEqualTo(1));
            }
            Assert.That(_steps.Count, Is.EqualTo(1));
            Assert.That(_steps[0].Side, Is.EqualTo(StalkerFootSide.Left));
        }

        [TestCase(-3f)]
        [TestCase(3f)]
        public void SupportedSlopeVelocityDoesNotSuppressFootsteps(float verticalVelocity)
        {
            StalkerMovementState state = TimingState(StalkerMoveFlags.Forward | StalkerMoveFlags.Accel, StalkerEnvironment.OnGround, verticalVelocity);
            for (int frame = 0; frame < 120; frame++)
            {
                _footsteps.Tick(state, null, DT, true);
            }
            Assert.That(_steps.Count, Is.EqualTo(4));
            AssertAlternating();
        }

        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.Jump, StalkerEnvironment.OnGround)]
        [TestCase(StalkerMoveFlags.Forward, StalkerEnvironment.InAir)]
        public void AirborneStateStillBlocksEventsEvenWhenSupportIsReported(StalkerMoveFlags flags, StalkerEnvironment environment)
        {
            StalkerMovementState state = TimingState(flags, environment);
            for (int frame = 0; frame < 120; frame++)
            {
                _footsteps.Tick(state, null, DT, true);
            }
            Assert.That(_steps, Is.Empty);
            Assert.That(_footsteps.LegsMotion, Is.Null);
        }

        [Test]
        public void ExplicitLadderSupportKeepsTheExistingClimbTimingChannel()
        {
            StalkerMovementState state = TimingState(StalkerMoveFlags.Forward | StalkerMoveFlags.Climb, StalkerEnvironment.AtWall);
            string motion = StalkerFootstepSynchronizer.GetLegsMotion(state.Flags);
            var library = ScriptableObject.CreateInstance<StalkerFootstepTimingLibrary>();
            library.SetImportData(new List<StalkerBodyMotionSteps>(), new List<StalkerLegsMotionSteps> { new StalkerLegsMotionSteps(motion, 1f, 1, 0.2f, 1f, 0.65f, 1f, false) }, string.Empty);
            var serialized = new SerializedObject(_footsteps);
            serialized.FindProperty("_library").objectReferenceValue = library;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            try
            {
                for (int frame = 0; frame < 120; frame++)
                {
                    _footsteps.Tick(state, null, DT, true);
                }
                Assert.That(_steps.Count, Is.EqualTo(4));
                Assert.That(_footsteps.LegsMotion, Is.EqualTo(motion));
            }
            finally
            {
                serialized.FindProperty("_library").objectReferenceValue = _library;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                UnityEngine.Object.DestroyImmediate(library);
            }
        }

        [Test]
        public void UnsupportedSubcyclesConsumeAlreadyPassedMarkersBeforeRecontact()
        {
            StalkerMovementState state = TimingState(StalkerMoveFlags.Forward | StalkerMoveFlags.Accel);
            string motion = StalkerFootstepSynchronizer.GetLegsMotion(state.Flags);
            var library = ScriptableObject.CreateInstance<StalkerFootstepTimingLibrary>();
            library.SetImportData(new List<StalkerBodyMotionSteps>(), new List<StalkerLegsMotionSteps> { new StalkerLegsMotionSteps(motion, 1.2f, 3, 0.2f, 1f, 0.65f, 1f, false) }, string.Empty);
            var serialized = new SerializedObject(_footsteps);
            serialized.FindProperty("_library").objectReferenceValue = library;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            try
            {
                _footsteps.Tick(state, null, 0f, true);
                _footsteps.Tick(state, null, 0.7f, false);
                Assert.That(_steps, Is.Empty);
                _footsteps.Tick(state, null, 0f, true);
                Assert.That(_steps, Is.Empty);
                _footsteps.Tick(state, null, 0.17f, true);
                Assert.That(_steps, Is.Empty);
                _footsteps.Tick(state, null, 0.02f, true);
                Assert.That(_steps.Count, Is.EqualTo(1));
                Assert.That(_steps[0].Side, Is.EqualTo(StalkerFootSide.Left));
            }
            finally
            {
                serialized.FindProperty("_library").objectReferenceValue = _library;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                UnityEngine.Object.DestroyImmediate(library);
            }
        }

        private static StalkerMovementState TimingState(StalkerMoveFlags flags, StalkerEnvironment environment = StalkerEnvironment.OnGround, float verticalVelocity = 0f)
        {
            return new StalkerMovementState(flags, environment, new Vector3(0f, verticalVelocity, 2f), Vector3.forward, 0, false, 2f, 2f);
        }

        private static bool HasSide(IReadOnlyList<StalkerFootContact> contacts, StalkerFootSide side)
        {
            foreach (StalkerFootContact contact in contacts)
            {
                if (contact.Side == side)
                {
                    return true;
                }
            }

            return false;
        }

        private void AssertAlternating()
        {
            for (int i = 1; i < _steps.Count; i++)
            {
                Assert.That(_steps[i].Side, Is.Not.EqualTo(_steps[i - 1].Side), $"double step at {i}");
            }
        }

        private float Rate(StalkerMoveFlags wish)
        {
            _rig.Place(Vector3.zero);
            Run(StalkerMoveFlags.None, 0.5f);
            Run(wish, 1.0f);
            _steps.Clear();
            Run(wish, 4.0f);
            return _steps.Count / 4.0f;
        }

        private void Run(StalkerMoveFlags wish, float seconds, System.Action onFrame = null, float deltaTime = DT)
        {
            _rig.Run(wish, seconds, deltaTime, _ =>
            {
                _footsteps.Tick(_rig.Motor.State, null, deltaTime);
                onFrame?.Invoke();
            });
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
