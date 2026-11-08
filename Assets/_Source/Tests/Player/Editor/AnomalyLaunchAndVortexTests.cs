using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyLaunchAndVortexTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly StalkerMovementProfile _profile;

            public StalkerCharacterPhysics Physics { get; }

            public Rig(float gravity)
            {
                _profile = ScriptableObject.CreateInstance<StalkerMovementProfile>();
                _profile.hideFlags = HideFlags.HideAndDontSave;
                typeof(StalkerEngineValues).GetField("_gravity", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_profile.Engine, gravity);
                Physics = new StalkerCharacterPhysics(_profile);
            }

            public float FindApex(float height)
            {
                Vector3 position = Vector3.zero;
                Physics.Launch(position, height);
                float apex = 0f;
                for (int step = 0; step < 1000; step++)
                {
                    position += Physics.Step(position, _profile.Engine.FixedStep);
                    Physics.ResetContacts();
                    apex = Mathf.Max(apex, position.y);
                    if (Physics.Velocity.y <= 0f)
                    {
                        return apex;
                    }
                }
                Assert.Fail("Launch did not reach its apex");
                return apex;
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_profile);
            }
        }

        [Test]
        public void LaunchReachesSixMetersWithOriginalAndChangedGravity()
        {
            foreach (float gravity in new[] { 9.81f, 19.62f, 39.24f })
            {
                using (var rig = new Rig(gravity))
                {
                    Assert.That(rig.FindApex(6f), Is.EqualTo(6f).Within(0.01f), "gravity=" + gravity);
                }
            }
        }

        [Test]
        public void LaunchPreservesHorizontalVelocityAndReplacesFallingSpeed()
        {
            using (var rig = new Rig(19.62f))
            {
                rig.Physics.SetVelocity(new Vector3(3f, -40f, -2f));
                rig.Physics.Launch(Vector3.zero, 6f);
                Assert.That(rig.Physics.Velocity.x, Is.EqualTo(3f));
                Assert.That(rig.Physics.Velocity.z, Is.EqualTo(-2f));
                Assert.That(rig.Physics.Velocity.y, Is.GreaterThan(15f));
                Assert.That(rig.Physics.Environment, Is.EqualTo(StalkerEnvironment.InAir));
            }
        }

        [Test]
        public void LaunchDetachesLadderAndClearsGroundBeforeFirstStep()
        {
            using (var rig = new Rig(19.62f))
            {
                rig.Physics.Climbing = true;
                rig.Physics.AddContact(Vector3.up);
                rig.Physics.Launch(Vector3.zero, 6f);
                Assert.That(rig.Physics.Climbing, Is.False);
                Assert.That(rig.Physics.Contacts.Any, Is.False);
                Vector3 first = rig.Physics.Step(Vector3.zero, 0.01f);
                Assert.That(first.y, Is.GreaterThan(0.15f));
            }
        }

        [Test]
        public void NonpositiveLaunchDoesNotChangeExistingMotion()
        {
            using (var rig = new Rig(19.62f))
            {
                Vector3 velocity = new Vector3(1f, -4f, 3f);
                rig.Physics.SetVelocity(velocity);
                rig.Physics.Launch(Vector3.zero, 0f);
                rig.Physics.Launch(Vector3.zero, -10f);
                Assert.That(rig.Physics.Velocity, Is.EqualTo(velocity));
            }
        }

        [Test]
        public void StationaryLaunchCanClearTheAnomalyWithDelayedAirInput()
        {
            using (var rig = new Rig(19.62f))
            {
                Vector3 position = Vector3.zero;
                rig.Physics.Launch(position, 6f, 8f);
                float apex = 0f;
                for (int step = 0; step < 1000; step++)
                {
                    rig.Physics.SetControl(step >= 50 ? Vector3.right : Vector3.zero);
                    position += rig.Physics.Step(position, 0.01f);
                    rig.Physics.ResetContacts();
                    apex = Mathf.Max(apex, position.y);
                    if (position.y <= 0f)
                    {
                        break;
                    }
                }
                Assert.That(position.x, Is.GreaterThan(2.8f));
                Assert.That(apex, Is.EqualTo(6f).Within(0.01f));
            }
        }

        [Test]
        public void AirSteeringDoesNotEraseEntryMomentumWithoutInput()
        {
            using (var rig = new Rig(19.62f))
            {
                rig.Physics.SetVelocity(new Vector3(7f, 0f, -2f));
                rig.Physics.Launch(Vector3.zero, 6f, 8f);
                Vector3 position = Vector3.zero;
                for (int step = 0; step < 50; step++)
                {
                    position += rig.Physics.Step(position, 0.01f);
                    rig.Physics.ResetContacts();
                }
                Assert.That(rig.Physics.Velocity.x, Is.EqualTo(7f));
                Assert.That(rig.Physics.Velocity.z, Is.EqualTo(-2f));
            }
        }

        [Test]
        public void LandingRestoresNormalAirControlForTheNextFall()
        {
            using (var rig = new Rig(19.62f))
            {
                rig.Physics.Launch(Vector3.zero, 6f, 8f);
                rig.Physics.SetVelocity(Vector3.zero);
                rig.Physics.AddContact(Vector3.up);
                rig.Physics.Step(Vector3.zero, 0.01f);
                rig.Physics.ResetContacts();
                rig.Physics.SetVelocity(Vector3.down);
                rig.Physics.SetControl(Vector3.forward);
                rig.Physics.Step(Vector3.up * 2f, 0.01f);
                Assert.That(rig.Physics.Velocity.z, Is.GreaterThanOrEqualTo(0f).And.LessThan(0.001f));
            }
        }

        [Test]
        public void VortexFalloffMatchesSourceFifthPowerWithoutHitPower()
        {
            Vector3 value = AnomalyAttraction.Evaluate(Vector3.left * 3f, 4f, 0.5f, 1000f);
            float expected = 1000f * Mathf.Pow(1f - 0.75f * 0.75f, 5f);
            Assert.That(value.x, Is.EqualTo(-expected).Within(0.0001f));
            Assert.That(value.y, Is.Zero);
            Assert.That(value.z, Is.Zero);
        }

        [Test]
        public void VortexInnerForceIsCappedAndOutsideForceIsZero()
        {
            Vector3 inner = AnomalyAttraction.Evaluate(Vector3.forward * 0.1f, 4f, 0.5f, 1000f);
            Vector3 boundary = AnomalyAttraction.Evaluate(Vector3.forward * 2f, 4f, 0.5f, 1000f);
            Assert.That(inner, Is.EqualTo(boundary));
            Assert.That(AnomalyAttraction.Evaluate(Vector3.forward * 4f, 4f, 0.5f, 1000f), Is.EqualTo(Vector3.zero));
            Assert.That(AnomalyAttraction.Evaluate(Vector3.forward * 8f, 4f, 0.5f, 1000f), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void VortexExactCenterAndInvalidRadiusRemainFinite()
        {
            Vector3 center = AnomalyAttraction.Evaluate(Vector3.zero, 4f, 0.5f, 1000f);
            Assert.That(center.x, Is.Zero);
            Assert.That(center.y, Is.EqualTo(1000f * Mathf.Pow(0.75f, 5f)).Within(0.0001f));
            Assert.That(center.z, Is.Zero);
            Assert.That(AnomalyAttraction.Evaluate(Vector3.one, 0f, 0.5f, 1000f), Is.EqualTo(Vector3.zero));
        }
    }
}
