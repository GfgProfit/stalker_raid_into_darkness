using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyImpactTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _previewScene;
            private readonly GameObject _root;
            private readonly StalkerMovementProfile _profile;

            public StalkerMovementMotor Motor { get; }
            public StalkerCharacterPhysics Physics { get; }
            public StalkerActorCondition Condition { get; }

            public Rig()
            {
                _previewScene = EditorSceneManager.NewPreviewScene();
                _root = new GameObject(nameof(AnomalyImpactTests));
                SceneManager.MoveGameObjectToScene(_root, _previewScene);
                _root.SetActive(false);
                _root.hideFlags = HideFlags.HideAndDontSave;
                _profile = ScriptableObject.CreateInstance<StalkerMovementProfile>();
                _profile.hideFlags = HideFlags.HideAndDontSave;
                Motor = _root.AddComponent<StalkerMovementMotor>();
                Physics = new StalkerCharacterPhysics(_profile);
                Condition = _root.GetComponent<StalkerActorCondition>();
                Condition.Initialize(_profile);
                SetField(Motor, "_physics", Physics);
                SetField(Motor, "_condition", Condition);
            }

            public void SetRealFlags(StalkerMoveFlags flags)
            {
                SetField(Motor, "_real", flags);
            }

            public void SetAirborne()
            {
                typeof(StalkerCharacterPhysics).GetProperty(nameof(StalkerCharacterPhysics.LoseControl)).SetValue(Physics, true);
            }

            public StalkerAnomalyResponse AddResponse()
            {
                StalkerPlayerController player = _root.AddComponent<StalkerPlayerController>();
                SetField(player, "_motor", Motor);
                SetField(player, "_profile", _profile);
                StalkerAnomalyResponse response = _root.AddComponent<StalkerAnomalyResponse>();
                SetField(response, "_player", player);
                _root.SetActive(true);
                return response;
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_root);
                UnityEngine.Object.DestroyImmediate(_profile);
                EditorSceneManager.ClosePreviewScene(_previewScene);
            }
        }

        [Test]
        public void LaunchingShockPreservesGroundedMomentumAndCancelsSprint()
        {
            using (var rig = new Rig())
            {
                Vector3 velocity = new Vector3(4f, 0f, -3f);
                rig.Physics.SetVelocity(velocity);
                rig.SetRealFlags(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint);
                rig.Motor.Wish = StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint;
                rig.Motor.ApplyAnomalyShock(false);
                rig.Motor.LaunchFromAnomaly(6f, 8f);
                Assert.That(rig.Motor.Velocity.x, Is.EqualTo(velocity.x));
                Assert.That(rig.Motor.Velocity.z, Is.EqualTo(velocity.z));
                Assert.That(rig.Motor.Velocity.y, Is.GreaterThan(15f));
                Assert.That(rig.Motor.Wish, Is.EqualTo(StalkerMoveFlags.Forward));
            }
        }

        [Test]
        public void GroundedShockStopsAllVelocityAxesWithoutChangingControlAcceleration()
        {
            using (var rig = new Rig())
            {
                Vector3 acceleration = new Vector3(4.0f, 0.0f, -2.0f);
                rig.Physics.SetVelocity(new Vector3(3.0f, -5.0f, 7.0f));
                rig.Physics.SetControl(acceleration);
                Assert.That(rig.Motor.Environment, Is.EqualTo(StalkerEnvironment.OnGround));

                rig.Motor.ApplyAnomalyShock();

                Assert.That(rig.Motor.Velocity, Is.EqualTo(Vector3.zero));
                Assert.That(rig.Physics.Acceleration, Is.EqualTo(acceleration));
            }
        }

        [Test]
        public void AirborneShockPreservesVelocityAndCancelsActiveSprint()
        {
            using (var rig = new Rig())
            {
                Vector3 velocity = new Vector3(-3.0f, 5.0f, 7.0f);
                rig.SetAirborne();
                rig.Physics.SetVelocity(velocity);
                rig.SetRealFlags(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint);
                rig.Motor.Wish = StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint;
                Assert.That(rig.Motor.Environment, Is.EqualTo(StalkerEnvironment.InAir));

                rig.Motor.ApplyAnomalyShock();

                Assert.That(rig.Motor.Velocity, Is.EqualTo(velocity));
                Assert.That(rig.Motor.Wish, Is.EqualTo(StalkerMoveFlags.Forward));
            }
        }

        [Test]
        public void LadderShockStopsExistingVelocityWithoutDetachingTheLadder()
        {
            using (var rig = new Rig())
            {
                rig.Physics.Climbing = true;
                rig.Physics.SetVelocity(new Vector3(-2.0f, 4.0f, 1.0f));
                Assert.That(rig.Motor.Environment, Is.EqualTo(StalkerEnvironment.AtWall));

                rig.Motor.ApplyAnomalyShock();

                Assert.That(rig.Motor.Velocity, Is.EqualTo(Vector3.zero));
                Assert.That(rig.Physics.Climbing, Is.True);
            }
        }

        [Test]
        public void ActiveSprintShockPreservesOtherCommandsAndActualFlags()
        {
            using (var rig = new Rig())
            {
                StalkerMoveFlags real = StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint;
                StalkerMoveFlags kept = StalkerMoveFlags.Forward | StalkerMoveFlags.Jump | StalkerMoveFlags.LeftStrafe;
                rig.SetRealFlags(real);
                rig.Motor.Wish = kept | StalkerMoveFlags.Sprint;

                rig.Motor.ApplyAnomalyShock();

                Assert.That(rig.Motor.Wish, Is.EqualTo(kept));
                Assert.That(rig.Motor.RealFlags, Is.EqualTo(real));
            }
        }

        [Test]
        public void RequestedSprintIsPreservedWhenSprintIsNotActuallyActive()
        {
            using (var rig = new Rig())
            {
                StalkerMoveFlags wish = StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint;
                rig.SetRealFlags(StalkerMoveFlags.Forward);
                rig.Motor.Wish = wish;

                rig.Motor.ApplyAnomalyShock();

                Assert.That(rig.Motor.Wish, Is.EqualTo(wish));
            }
        }

        [Test]
        public void ShockDoesNotChangeHealthPowerOrConditionSpeedModifiers()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Condition, "_neutralHealth", 0.61f);
                SetField(rig.Condition, "_neutralSatiety", 0.43f);
                rig.Condition.SetPower(0.37f);
                rig.Condition.SetSpeedModifier("AnomalyImpactTests.Run", 0.8f, false);
                rig.Condition.SetSpeedModifier("AnomalyImpactTests.Sprint", 0.7f, true);
                float run = rig.Condition.RunCoef;
                float sprint = rig.Condition.SprintKoef;

                rig.Motor.ApplyAnomalyShock();

                Assert.That(rig.Condition.Health, Is.EqualTo(0.61f));
                Assert.That(rig.Condition.Satiety, Is.EqualTo(0.43f));
                Assert.That(rig.Condition.Power, Is.EqualTo(0.37f));
                Assert.That(rig.Condition.RunCoef, Is.EqualTo(run));
                Assert.That(rig.Condition.SprintKoef, Is.EqualTo(sprint));
                Assert.That(rig.Condition.RunModifiers.Count, Is.EqualTo(1));
                Assert.That(rig.Condition.SprintModifiers.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void ChemicalPulsePreservesGroundedMovementSprintAndActorCondition()
        {
            AssertNonimpulsivePulse(AnomalyInfluenceKind.Chemical, false);
        }

        [Test]
        public void ChemicalPulsePreservesAirborneMovementSprintAndActorCondition()
        {
            AssertNonimpulsivePulse(AnomalyInfluenceKind.Chemical, true);
        }

        [Test]
        public void ThermalPulsePreservesGroundedMovementSprintAndActorCondition()
        {
            AssertNonimpulsivePulse(AnomalyInfluenceKind.Thermal, false);
        }

        [Test]
        public void ThermalPulsePreservesAirborneMovementSprintAndActorCondition()
        {
            AssertNonimpulsivePulse(AnomalyInfluenceKind.Thermal, true);
        }

        private static void AssertNonimpulsivePulse(AnomalyInfluenceKind kind, bool airborne)
        {
            using (var rig = new Rig())
            {
                StalkerAnomalyResponse response = rig.AddResponse();
                AnomalyInfluenceProfile profile = ScriptableObject.CreateInstance<AnomalyInfluenceProfile>();
                try
                {
                    SetField(profile, "_kind", kind);
                    Vector3 velocity = new Vector3(3.5f, -2f, 6f);
                    StalkerMoveFlags flags = StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint;
                    if (airborne)
                    {
                        rig.SetAirborne();
                    }
                    rig.Physics.SetVelocity(velocity);
                    rig.SetRealFlags(flags);
                    rig.Motor.Wish = flags;
                    SetField(rig.Condition, "_neutralHealth", 0.61f);
                    rig.Condition.SetPower(0.37f);
                    rig.Condition.SetSpeedModifier("AnomalyImpactTests.Nonimpulsive", 0.8f, false);
                    float speed = rig.Condition.RunCoef;
                    int impacts = 0;
                    response.Impact += value => impacts++;

                    response.ApplyPulse(profile);

                    Assert.That(impacts, Is.EqualTo(1));
                    Assert.That(rig.Motor.Velocity, Is.EqualTo(velocity));
                    Assert.That(rig.Motor.Wish, Is.EqualTo(flags));
                    Assert.That(rig.Motor.RealFlags, Is.EqualTo(flags));
                    Assert.That(rig.Condition.Health, Is.EqualTo(0.61f));
                    Assert.That(rig.Condition.Power, Is.EqualTo(0.37f));
                    Assert.That(rig.Condition.RunCoef, Is.EqualTo(speed));
                    Assert.That(rig.Condition.RunModifiers.Count, Is.EqualTo(1));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(profile);
                }
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
