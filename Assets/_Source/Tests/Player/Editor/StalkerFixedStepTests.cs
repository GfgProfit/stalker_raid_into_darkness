using NUnit.Framework;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerFixedStepTests
    {
        private static readonly Vector3 TEST_ORIGIN = new Vector3(20000.0f, 0.0f, 20000.0f);

        [Test]
        public void FractionalRenderTimeWaitsForACompletePhysicsStep()
        {
            using (StalkerMotorHarness rig = CreateRig())
            {
                float step = rig.Profile.Engine.FixedStep;
                Vector3 position = rig.Transform.position;
                Vector3 velocity = rig.Motor.Velocity;
                rig.Frame(StalkerMoveFlags.Forward, step * 0.25f);

                Assert.That((rig.Transform.position - position).sqrMagnitude, Is.LessThan(0.00000001f));
                Assert.That((rig.Motor.Velocity - velocity).sqrMagnitude, Is.LessThan(0.00000001f));

                rig.Frame(StalkerMoveFlags.Forward, step * 0.75f);
                Assert.That(rig.Transform.position.z - position.z, Is.GreaterThan(0.0005f));
                Assert.That(rig.Motor.Velocity.z, Is.GreaterThan(velocity.z));
            }
        }

        [Test]
        public void EqualSimulatedTimeKeepsMovementConsistentAcrossRenderRates()
        {
            int[] rates = { 30, 60, 144 };
            float referenceDistance = -1.0f;
            float referenceSpeed = -1.0f;
            foreach (int rate in rates)
            {
                using (StalkerMotorHarness rig = CreateRig())
                {
                    Vector3 start = rig.Transform.position;
                    for (int frame = 0; frame < rate; frame++)
                    {
                        rig.Motor.Condition.SetPower(rig.Motor.Condition.MaxPower);
                        rig.Frame(StalkerMoveFlags.Forward, 1.0f / rate);
                    }

                    float distance = rig.Transform.position.z - start.z;
                    float speed = rig.HorizontalSpeed;
                    TestContext.WriteLine($"{rate} render FPS: distance {distance:0.000000} m, final speed {speed:0.000000} m/s");
                    if (referenceDistance < 0.0f)
                    {
                        referenceDistance = distance;
                        referenceSpeed = speed;
                    }
                    else
                    {
                        Assert.That(distance, Is.EqualTo(referenceDistance).Within(0.01f));
                        Assert.That(speed, Is.EqualTo(referenceSpeed).Within(0.01f));
                    }
                }
            }
        }

        private static StalkerMotorHarness CreateRig()
        {
            var rig = new StalkerMotorHarness();
            rig.AddBox("FixedStepGround", TEST_ORIGIN + new Vector3(0.0f, -0.25f, 0.0f), new Vector3(20.0f, 0.5f, 20.0f), Quaternion.identity);
            rig.Place(TEST_ORIGIN);
            rig.Settle(0.5f);
            return rig;
        }
    }
}
