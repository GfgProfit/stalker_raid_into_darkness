using System.Globalization;
using NUnit.Framework;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerMovementTests
    {
        private const float DT = 1.0f / 60.0f;
        private const float SPEED_TOLERANCE = 0.02f;

        private StalkerMotorHarness _rig;

        [SetUp]
        public void SetUp()
        {
            _rig = new StalkerMotorHarness();
            _rig.Settle(0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.Accel, "walk")]
        [TestCase(StalkerMoveFlags.Forward, "run")]
        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint, "sprint")]
        [TestCase(StalkerMoveFlags.Back, "run_back")]
        [TestCase(StalkerMoveFlags.Back | StalkerMoveFlags.Accel, "walk_back")]
        [TestCase(StalkerMoveFlags.RightStrafe, "run_strafe")]
        [TestCase(StalkerMoveFlags.LeftStrafe | StalkerMoveFlags.Accel, "walk_strafe")]
        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.RightStrafe, "run_diagonal")]
        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.RightStrafe | StalkerMoveFlags.Sprint, "sprint_diagonal")]
        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.Crouch, "crouch")]
        [TestCase(StalkerMoveFlags.Forward | StalkerMoveFlags.Crouch | StalkerMoveFlags.Accel, "low_crouch")]
        public void SteadySpeedMatchesXRayFormula(StalkerMoveFlags wish, string label)
        {
            float expected = ExpectedSpeed(wish);
            _rig.Run(wish, 2.0f, DT);
            float measured = _rig.HorizontalSpeed;
            TestContext.WriteLine($"{label}: expected {F(expected)} m/s, measured {F(measured)} m/s, flags {_rig.Motor.RealFlags}");
            Assert.That(measured, Is.EqualTo(expected).Within(expected * SPEED_TOLERANCE), label);
        }

        [Test]
        public void AccelerationAndBrakingFollowPullForceAndContactFriction()
        {
            StalkerEngineValues engine = _rig.Profile.Engine;
            float run = ExpectedSpeed(StalkerMoveFlags.Forward);
            float accelerateTime = -1.0f;
            _rig.Run(StalkerMoveFlags.Forward, 1.0f, 1.0f / 200.0f, time =>
            {
                if (accelerateTime < 0.0f && _rig.HorizontalSpeed >= run * 0.95f)
                {
                    accelerateTime = time;
                }
            });

            float stopTime = -1.0f;
            Vector3 start = _rig.Transform.position;
            _rig.Run(StalkerMoveFlags.None, 1.0f, 1.0f / 200.0f, time =>
            {
                if (stopTime < 0.0f && _rig.HorizontalSpeed <= 0.01f)
                {
                    stopTime = time;
                }
            });

            float stopDistance = Vector3.Distance(start, _rig.Transform.position);
            float pull = engine.PullForce * engine.ActorFriction * engine.GroundFriction;
            float mu = engine.ActorFriction * engine.GroundFriction * engine.ActorFriction * engine.GroundFriction;
            float expectedAccelerate = run * 0.95f / pull;
            float expectedStop = run / (mu * engine.Gravity);
            TestContext.WriteLine($"accelerate to 95% run: expected {F(expectedAccelerate)} s, measured {F(accelerateTime)} s");
            TestContext.WriteLine($"stop from run: expected {F(expectedStop)} s / {F(run * run / (2.0f * mu * engine.Gravity))} m, measured {F(stopTime)} s / {F(stopDistance)} m");
            Assert.That(accelerateTime, Is.EqualTo(expectedAccelerate).Within(0.03f));
            Assert.That(stopTime, Is.EqualTo(expectedStop).Within(0.03f));
        }

        [Test]
        public void JumpHeightAndAirTimeFollowJumpSpeedAndGravity()
        {
            StalkerActorValues actor = _rig.Profile.Actor;
            float gravity = _rig.Profile.Engine.Gravity;
            Measure(DT, out float apex, out float airTime);
            float expectedApex = actor.JumpSpeed * actor.JumpSpeed / (2.0f * gravity);
            float expectedAir = 2.0f * actor.JumpSpeed / gravity;
            TestContext.WriteLine($"jump apex: expected {F(expectedApex)} m, measured {F(apex)} m; air time: expected {F(expectedAir)} s, measured {F(airTime)} s");
            Assert.That(apex, Is.EqualTo(expectedApex).Within(0.05f));
            Assert.That(airTime, Is.EqualTo(expectedAir).Within(0.05f));
        }

        [Test]
        public void ResultsDoNotDependOnFrameRate()
        {
            float[] deltas = { 1.0f / 30.0f, 1.0f / 60.0f, 1.0f / 144.0f, 1.0f / 250.0f, 0.0237f };
            float referenceSpeed = -1.0f;
            float referenceApex = -1.0f;
            float referenceDistance = -1.0f;
            foreach (float delta in deltas)
            {
                _rig.Place(Vector3.zero);
                _rig.Settle(0.3f, delta);
                Vector3 start = _rig.Transform.position;
                _rig.Run(StalkerMoveFlags.Forward, 1.5f, delta);
                float speed = _rig.HorizontalSpeed;
                float distance = Vector3.Distance(start, _rig.Transform.position);
                _rig.Settle(0.5f, delta);
                Measure(delta, out float apex, out float _);
                TestContext.WriteLine($"dt {F(delta)}: run speed {F(speed)} m/s, distance in 1.5 s {F(distance)} m, jump apex {F(apex)} m");
                if (referenceSpeed < 0.0f)
                {
                    referenceSpeed = speed;
                    referenceApex = apex;
                    referenceDistance = distance;
                    continue;
                }

                Assert.That(speed, Is.EqualTo(referenceSpeed).Within(referenceSpeed * 0.01f));
                Assert.That(distance, Is.EqualTo(referenceDistance).Within(referenceDistance * 0.02f + 0.02f));
                Assert.That(apex, Is.EqualTo(referenceApex).Within(0.03f));
            }
        }

        [Test]
        public void AirControlIsWeak()
        {
            _rig.Run(StalkerMoveFlags.None, 0.2f, DT);
            _rig.Frame(StalkerMoveFlags.Jump, DT);
            _rig.Run(StalkerMoveFlags.RightStrafe, 0.5f, DT);
            float lateral = Mathf.Abs(_rig.Motor.Velocity.x);
            TestContext.WriteLine($"lateral speed after 0.5 s of strafe input in the air from a standing jump: {F(lateral)} m/s");
            Assert.That(lateral, Is.LessThan(1.5f));
            Assert.That(lateral, Is.GreaterThan(0.05f));
        }

        [Test]
        public void CannotStandUpUnderLowCeiling()
        {
            _rig.Run(StalkerMoveFlags.Crouch, 0.3f, DT);
            Assert.That(_rig.Motor.Stance.CurrentBox, Is.EqualTo(StalkerStanceController.CROUCH_BOX));
            _rig.AddBox("Roof", new Vector3(0.0f, 1.45f + 0.1f, 0.0f), new Vector3(3.0f, 0.2f, 3.0f), Quaternion.identity);
            _rig.Run(StalkerMoveFlags.None, 0.5f, DT);
            Assert.That(_rig.Motor.Stance.CurrentBox, Is.EqualTo(StalkerStanceController.CROUCH_BOX), "stood up under a 1.45 m ceiling");
            Assert.That(_rig.Motor.RealFlags.Has(StalkerMoveFlags.Crouch), Is.True);
            TestContext.WriteLine($"camera height while crouched: {F(_rig.Motor.Stance.CameraHeight)} (target {F(_rig.Motor.Stance.TargetCameraHeight)})");
        }

        [Test]
        public void LowCrouchFitsUnderLowCeilingAndCrouchDoesNot()
        {
            _rig.Run(StalkerMoveFlags.Crouch | StalkerMoveFlags.Accel, 0.3f, DT);
            Assert.That(_rig.Motor.Stance.CurrentBox, Is.EqualTo(StalkerStanceController.LOW_CROUCH_BOX));
            _rig.AddBox("Roof", new Vector3(0.0f, 0.75f + 0.1f, 0.0f), new Vector3(3.0f, 0.2f, 3.0f), Quaternion.identity);
            _rig.Run(StalkerMoveFlags.Crouch, 0.5f, DT);
            Assert.That(_rig.Motor.Stance.CurrentBox, Is.EqualTo(StalkerStanceController.LOW_CROUCH_BOX), "crouch box grew under a 0.75 m ceiling");
            TestContext.WriteLine($"low crouch shape height {F(_rig.Motor.Stance.GetShape(2).Height)} m, camera {F(_rig.Motor.Stance.GetShape(2).CameraHeight)} m");
        }

        [TestCase(20.0f, true)]
        [TestCase(35.0f, true)]
        [TestCase(50.0f, true)]
        [TestCase(70.0f, false)]
        public void SlopeLimit(float angle, bool climbable)
        {
            float length = 8.0f;
            float rise = Mathf.Sin(angle * Mathf.Deg2Rad) * length * 0.5f;
            float run = Mathf.Cos(angle * Mathf.Deg2Rad) * length * 0.5f;
            _rig.AddBox("Slope", new Vector3(0.0f, rise - 0.2f, 2.0f + run), new Vector3(3.0f, 0.4f, length), Quaternion.Euler(-angle, 0.0f, 0.0f));
            _rig.Run(StalkerMoveFlags.Forward, 4.0f, DT);
            float height = _rig.Transform.position.y;
            TestContext.WriteLine($"slope {angle} deg: height after 4 s {F(height)} m");
            if (climbable)
            {
                Assert.That(height, Is.GreaterThan(1.0f));
            }
            else
            {
                Assert.That(height, Is.LessThan(1.0f));
            }
        }

        [TestCase(0.15f, true)]
        [TestCase(0.3f, true)]
        [TestCase(0.5f, true)]
        [TestCase(1.0f, false)]
        public void StepsAndLedges(float height, bool climbable)
        {
            _rig.AddBox("Ledge", new Vector3(0.0f, height * 0.5f, 8.0f), new Vector3(3.0f, height, 12.0f), Quaternion.identity);
            float y = 0.0f;
            _rig.Run(StalkerMoveFlags.Forward, 2.0f, DT, time => y = Mathf.Max(y, _rig.Transform.position.y));
            TestContext.WriteLine($"ledge {height} m: feet height {F(y)} m, z {F(_rig.Transform.position.z)}");
            Assert.That(y > height - 0.05f, Is.EqualTo(climbable));
        }

        [Test]
        public void LadderClimbUpAndExit()
        {
            float height = 3.0f;
            _rig.AddBox("Platform", new Vector3(0.0f, height - 0.1f, 3.0f), new Vector3(3.0f, 0.2f, 2.0f), Quaternion.identity);
            StalkerLadder ladder = _rig.AddLadder(new Vector3(0.0f, 0.0f, 1.9f), Vector3.back, height);
            _rig.Place(new Vector3(0.0f, 0.0f, 1.0f));
            _rig.Settle(0.3f);
            bool climbed = false;
            bool onTop = false;
            float maxSpeed = 0.0f;
            _rig.Run(StalkerMoveFlags.Forward, 6.0f, DT, time =>
            {
                if (Mathf.Repeat(time, 0.5f) < DT)
                {
                    TestContext.WriteLine($"t {F(time)} pos {_rig.Transform.position} vel {_rig.Motor.Velocity} flags {_rig.Motor.RealFlags} env {_rig.Motor.Environment}");
                }

                if (!_rig.Motor.Physics.Climbing && _rig.Transform.position.y > height - 0.05f && _rig.Transform.position.z > 2.0f && _rig.Motor.Physics.GoodGround)
                {
                    onTop = true;
                }

                if (_rig.Motor.Physics.Climbing)
                {
                    climbed = true;
                    maxSpeed = Mathf.Max(maxSpeed, _rig.Motor.Velocity.magnitude);
                }
            });

            StalkerActorValues actor = _rig.Profile.Actor;
            float expected = actor.WalkAccel * actor.RunCoef * actor.ClimbCoef / _rig.Profile.Engine.MaxVelocityDivider;
            TestContext.WriteLine($"ladder: climbed {climbed}, on top {onTop}, max climb speed {F(maxSpeed)} m/s (expected {F(expected)}), final {_rig.Transform.position}");
            Assert.That(climbed, Is.True);
            Assert.That(onTop, Is.True, "did not step off the ladder onto the platform");
            Assert.That(maxSpeed, Is.EqualTo(expected).Within(expected * 0.05f));
        }

        [Test]
        public void SprintLastsOnlyWhileHeld()
        {
            Assert.That(_rig.Profile.Input.SprintToggle, Is.False);
            _rig.Run(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint, 1.0f, DT);
            Assert.That(_rig.Motor.RealFlags.Has(StalkerMoveFlags.Sprint), Is.True);
            _rig.Run(StalkerMoveFlags.Forward, 2.0f / 60.0f, DT);
            Assert.That(_rig.Motor.RealFlags.Has(StalkerMoveFlags.Sprint), Is.False);
            _rig.Run(StalkerMoveFlags.Forward, 1.0f, DT);
            TestContext.WriteLine($"after releasing sprint: {F(_rig.HorizontalSpeed)} m/s");
            Assert.That(_rig.HorizontalSpeed, Is.EqualTo(ExpectedSpeed(StalkerMoveFlags.Forward)).Within(0.02f));
        }

        [Test]
        public void SprintStopsWithoutStaminaAndDrainsPower()
        {
            StalkerActorCondition condition = _rig.Motor.Condition;
            condition.SetPower(0.05f);
            _rig.Run(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint, 1.0f, DT);
            Assert.That(_rig.Motor.RealFlags.Has(StalkerMoveFlags.Sprint), Is.False);
            TestContext.WriteLine($"power 0.05: run_coef after stamina modifier {F(condition.RunCoef)}, speed {F(_rig.HorizontalSpeed)} m/s, limping {condition.IsLimping()}");
            Assert.That(_rig.Motor.RealFlags.IsAccelerated(), Is.False, "limping must force walking");
        }

        private void Measure(float deltaTime, out float apex, out float airTime)
        {
            float start = _rig.Transform.position.y;
            float top = start;
            float landed = -1.0f;
            bool left = false;
            _rig.Frame(StalkerMoveFlags.Jump, deltaTime);
            _rig.Run(StalkerMoveFlags.None, 1.5f, deltaTime, time =>
            {
                float y = _rig.Transform.position.y;
                top = Mathf.Max(top, y);
                if (y > start + 0.05f)
                {
                    left = true;
                }

                if (left && landed < 0.0f && y <= start + 0.001f)
                {
                    landed = time + deltaTime;
                }
            });

            apex = top - start;
            airTime = landed;
        }

        private float ExpectedSpeed(StalkerMoveFlags wish)
        {
            StalkerActorValues actor = _rig.Profile.Actor;
            float scale = actor.WalkAccel;
            bool walk = (wish & StalkerMoveFlags.Accel) != 0;
            bool back = (wish & StalkerMoveFlags.Back) != 0;
            bool crouch = (wish & StalkerMoveFlags.Crouch) != 0;
            bool sprint = (wish & StalkerMoveFlags.Sprint) != 0 && !crouch && !walk;
            bool strafe = (wish & (StalkerMoveFlags.LeftStrafe | StalkerMoveFlags.RightStrafe)) != 0;
            if (!walk)
            {
                scale *= back ? actor.RunBackCoef : actor.RunCoef;
            }
            else if (back)
            {
                scale *= actor.WalkBackCoef;
            }

            if (crouch)
            {
                scale *= actor.CrouchCoef;
            }

            if (sprint)
            {
                scale *= actor.SprintKoef;
            }

            if (strafe && !crouch)
            {
                scale *= sprint ? actor.SprintStrafeCoef : walk ? actor.WalkStrafeCoef : actor.RunStrafeCoef;
            }

            return scale / _rig.Profile.Engine.MaxVelocityDivider;
        }

        private static string F(float value)
        {
            return value.ToString("0.0000", CultureInfo.InvariantCulture);
        }
    }
}
