using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalySourceBehaviorTests
    {
        private const string SPEED_KEY = "zone_no_gravity";

        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly GameObject _anomaly;
            private readonly GameObject _player;
            private readonly GameObject _probe;
            private readonly StalkerMovementProfile _movementProfile;

            public ContinuousAnomalyVisual Visual { get; }
            public AnomalyAudio Audio { get; }
            public AnomalyPlayerReaction Reaction { get; }
            public StalkerPlayerController Player { get; }
            public StalkerMovementMotor Motor { get; }
            public StalkerActorCondition Condition { get; }
            public StalkerAnomalyResponse Response { get; }
            public SphereCollider PlayerCollider { get; }
            public SphereCollider ProbeCollider { get; }

            public Rig()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                _anomaly = Create("SourceAnomaly");
                Visual = _anomaly.AddComponent<ContinuousAnomalyVisual>();
                Audio = _anomaly.AddComponent<AnomalyAudio>();
                SetField(Visual, "_audio", Audio);
                SetField(Visual, "_keepIdleDuringDischarge", true);
                Visual.ShowIdle();
                Reaction = _anomaly.AddComponent<AnomalyPlayerReaction>();
                SetField(Reaction, "_visual", Visual);
                SetField(Reaction, "_smallObjectIgnoreDelay", -1f);
                Invoke(Reaction, "OnDisable");
                Invoke(Reaction, "OnEnable");
                _player = Create("Player");
                _player.SetActive(false);
                _movementProfile = ScriptableObject.CreateInstance<StalkerMovementProfile>();
                _movementProfile.hideFlags = HideFlags.HideAndDontSave;
                Player = _player.AddComponent<StalkerPlayerController>();
                Motor = _player.GetComponent<StalkerMovementMotor>();
                Condition = _player.GetComponent<StalkerActorCondition>();
                Motor.Initialize(_movementProfile);
                SetField(Player, "_motor", Motor);
                SetField(Player, "_profile", _movementProfile);
                PlayerCollider = _player.AddComponent<SphereCollider>();
                Response = _player.AddComponent<StalkerAnomalyResponse>();
                SetField(Response, "_player", Player);
                _player.SetActive(true);
                _probe = Create("Probe");
                _probe.transform.position = new Vector3(4f, 5f, 6f);
                ProbeCollider = _probe.AddComponent<SphereCollider>();
                _probe.AddComponent<AnomalyProbe>();
            }

            public void RegisterPlayer()
            {
                Invoke(Reaction, "RegisterContact", PlayerCollider);
            }

            public void RegisterProbe()
            {
                Invoke(Reaction, "RegisterContact", ProbeCollider);
            }

            public void UpdateRunSpeed(bool nearby, float now, float deltaTime = 0.02f)
            {
                Invoke(Response, "UpdateRunSpeed", nearby, 0.6f, now, deltaTime);
            }

            public void Dispose()
            {
                Invoke(Reaction, "OnDisable");
                UnityEngine.Object.DestroyImmediate(_anomaly);
                UnityEngine.Object.DestroyImmediate(_player);
                UnityEngine.Object.DestroyImmediate(_probe);
                UnityEngine.Object.DestroyImmediate(_movementProfile);
                EditorSceneManager.ClosePreviewScene(_scene);
            }

            private GameObject Create(string name)
            {
                GameObject root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, _scene);
                root.hideFlags = HideFlags.HideAndDontSave;
                return root;
            }
        }

        [Test]
        public void ExistingDefaultsKeepStationaryPlayerAndProbeActivation()
        {
            using (var rig = new Rig())
            {
                rig.RegisterPlayer();
                rig.RegisterProbe();
                Assert.That(Invoke(rig.Reaction, "HasActivePlayer"), Is.True);
                Assert.That(Invoke(rig.Reaction, "HasActiveProbe"), Is.True);
            }
        }

        [Test]
        public void ActorOnlyFieldsRejectProbeContactsAndKeepPlayerContacts()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Reaction, "_actorOnly", true);
                rig.RegisterProbe();
                Assert.That(((IDictionary)GetField(rig.Reaction, "_probeContacts")).Count, Is.Zero);
                Assert.That(GetField(rig.Audio, "_entrancePosition"), Is.EqualTo(Vector3.zero));
                Assert.That(Invoke(rig.Reaction, "HasActiveProbe"), Is.False);
                rig.RegisterPlayer();
                Assert.That(rig.Reaction.IsTouching(rig.Player), Is.True);
                Assert.That(Invoke(rig.Reaction, "HasActivePlayer"), Is.True);
            }
        }

        [Test]
        public void IgnoredNonlivingObjectsKeepEntrancePresentationWithoutActivationOrHit()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Reaction, "_ignoreNonlivingObjects", true);
                rig.RegisterProbe();
                Assert.That(GetField(rig.Audio, "_entrancePosition"), Is.EqualTo(rig.ProbeCollider.transform.position));
                Assert.That(Invoke(rig.Reaction, "HasActiveProbe"), Is.False);
                Invoke(rig.Reaction, "ApplyPulse");
                Assert.That(GetField(rig.Audio, "_hitPosition"), Is.EqualTo(Vector3.zero));
                Assert.That(rig.Audio.GetComponentsInChildren<AudioSource>().Length, Is.Zero);
            }
        }

        [Test]
        public void BurningFuzzRequiresActualSpeedStrictlyGreaterThanSourceThreshold()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Reaction, "_minimumPlayerSpeed", 2f);
                rig.RegisterPlayer();
                foreach (float speed in new[] { 0f, 1.99f, 2f })
                {
                    SetField(rig.Motor, "_actualVelocity", speed);
                    Assert.That(Invoke(rig.Reaction, "HasActivePlayer"), Is.False);
                }
                SetField(rig.Motor, "_actualVelocity", 2.01f);
                Assert.That(Invoke(rig.Reaction, "HasActivePlayer"), Is.True);
                Assert.That(rig.Reaction.IsTouching(rig.Player), Is.True);
            }
        }

        [Test]
        public void BurningFuzzProbeContactCannotWakeButCanReceiveAnExistingBlowout()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Reaction, "_minimumPlayerSpeed", 2f);
                rig.RegisterProbe();
                Assert.That(Invoke(rig.Reaction, "HasActiveProbe"), Is.False);
                Invoke(rig.Reaction, "ApplyPulse");
                Assert.That(GetField(rig.Audio, "_hitPosition"), Is.EqualTo(rig.ProbeCollider.transform.position));
                Assert.That(rig.Visual.GetComponentsInChildren<AudioSource>().Length, Is.Zero);
            }
        }

        [Test]
        public void ContinuousFieldsDriveTheCycleWithoutInventingParticlesLightsOrAudio()
        {
            using (var rig = new Rig())
            {
                int discharges = 0;
                rig.Visual.DischargeStarted += () => discharges++;
                rig.Visual.PlayDischarge();
                Assert.That(discharges, Is.EqualTo(1));
                Assert.That(rig.Visual.IsDischarging, Is.True);
                Assert.That(rig.Reaction.Phase, Is.EqualTo(AnomalyPhase.Blowout));
                Assert.That(rig.Visual.GetComponentsInChildren<ParticleSystem>().Length, Is.Zero);
                Assert.That(rig.Visual.GetComponentsInChildren<Light>().Length, Is.Zero);
                Assert.That(rig.Visual.GetComponentsInChildren<AudioSource>().Length, Is.Zero);
                Invoke(rig.Visual, "TickVisual", 1f);
                Assert.That(rig.Visual.IsDischarging, Is.False);
                rig.Visual.Hide();
                Assert.That(rig.Reaction.Phase, Is.EqualTo(AnomalyPhase.Idle));
            }
        }

        [Test]
        public void SourceRunModifierKeepsNativeFirstSampleAndHundredMillisecondEntryCadence()
        {
            using (var rig = new Rig())
            {
                rig.UpdateRunSpeed(true, 0f);
                Assert.That(rig.Condition.RunModifiers[SPEED_KEY], Is.EqualTo(1f));
                rig.UpdateRunSpeed(true, 0.099f);
                Assert.That(rig.Condition.RunModifiers[SPEED_KEY], Is.EqualTo(1f));
                rig.UpdateRunSpeed(true, 0.1f);
                Assert.That(rig.Condition.RunModifiers[SPEED_KEY], Is.EqualTo(1f + 2f / 71f * (0.6f - 1f)).Within(0.000001f));
                rig.UpdateRunSpeed(true, 0.11f);
                Assert.That(rig.Condition.RunModifiers.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void NoGravityUsesTheExistingRunChannelWithoutChangingSprintHealthOrVelocity()
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetSpeedModifier("Other.Run", 0.8f, false);
                rig.Condition.SetSpeedModifier("Other.Sprint", 0.7f, true);
                rig.Condition.SetPower(37f);
                rig.Condition.SetHealth(61f);
                rig.Motor.Physics.SetVelocity(new Vector3(3f, 4f, 5f));
                float sprint = rig.Condition.SprintKoef;
                for (int index = 0; index < 100; index++)
                {
                    rig.UpdateRunSpeed(true, index * 0.11f);
                }
                Assert.That(rig.Condition.RunModifiers[SPEED_KEY], Is.InRange(0.6f, 0.65f));
                Assert.That(rig.Condition.RunModifiers["Other.Run"], Is.EqualTo(0.8f));
                Assert.That(rig.Condition.SprintModifiers["Other.Sprint"], Is.EqualTo(0.7f));
                Assert.That(rig.Condition.SprintKoef, Is.EqualTo(sprint));
                Assert.That(rig.Condition.Health, Is.EqualTo(61f));
                Assert.That(rig.Condition.Power, Is.EqualTo(37f));
                Assert.That(rig.Motor.Velocity, Is.EqualTo(new Vector3(3f, 4f, 5f)));
            }
        }

        [Test]
        public void NoGravityKeepsTheSourceOneSecondHoldBeforeRecovery()
        {
            using (var rig = new Rig())
            {
                rig.UpdateRunSpeed(true, 0f);
                SetField(rig.Response, "_runSpeedFactor", 0.6f);
                rig.UpdateRunSpeed(false, 0.9f);
                Assert.That(rig.Condition.RunModifiers[SPEED_KEY], Is.EqualTo(0.6f));
                rig.UpdateRunSpeed(false, 1.01f, 0.1f);
                float expected = 0.6f + 2f / 23f * 10f / 22f * 0.4f;
                Assert.That(rig.Condition.RunModifiers[SPEED_KEY], Is.EqualTo(expected).Within(0.000001f));
            }
        }

        [Test]
        public void NoGravityRecoveryRemovesOnlyItsModifierAtTheSourceThreshold()
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetSpeedModifier("Other.Run", 0.8f, false);
                rig.UpdateRunSpeed(true, 0f);
                SetField(rig.Response, "_runSpeedFactor", 0.979f);
                rig.UpdateRunSpeed(false, 2f, 1f);
                Assert.That(rig.Condition.RunModifiers.ContainsKey(SPEED_KEY), Is.False);
                Assert.That(rig.Condition.RunModifiers["Other.Run"], Is.EqualTo(0.8f));
            }
        }

        [Test]
        public void DisablingTheResponseReleasesOnlyItsOwnedSpeedModifier()
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetSpeedModifier("Other.Run", 0.8f, false);
                rig.UpdateRunSpeed(true, 0f);
                Invoke(rig.Response, "OnDisable");
                Assert.That(rig.Condition.RunModifiers.ContainsKey(SPEED_KEY), Is.False);
                Assert.That(rig.Condition.RunModifiers["Other.Run"], Is.EqualTo(0.8f));
            }
        }

        [Test]
        public void NewNonimpulsiveKindsPreserveVelocitySprintAndCondition()
        {
            using (var rig = new Rig())
            {
                AnomalyInfluenceProfile profile = ScriptableObject.CreateInstance<AnomalyInfluenceProfile>();
                try
                {
                    SetField(profile, "_actorPainEnabled", false);
                    Vector3 velocity = new Vector3(3f, 4f, 5f);
                    rig.Motor.Physics.SetVelocity(velocity);
                    StalkerMoveFlags flags = StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint;
                    SetField(rig.Motor, "_real", flags);
                    rig.Motor.Wish = flags;
                    foreach (AnomalyInfluenceKind kind in new[] { AnomalyInfluenceKind.Psychic, AnomalyInfluenceKind.Radiation, AnomalyInfluenceKind.NoGravity, AnomalyInfluenceKind.None })
                    {
                        SetField(profile, "_kind", kind);
                        rig.Response.ApplyPulse(profile);
                        Assert.That(rig.Motor.Velocity, Is.EqualTo(velocity));
                        Assert.That(rig.Motor.Wish, Is.EqualTo(flags));
                        Assert.That(rig.Condition.Health, Is.EqualTo(100f));
                        Assert.That(rig.Condition.Power, Is.EqualTo(100f));
                        Assert.That(rig.Condition.RunModifiers.Count, Is.Zero);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(profile);
                }
            }
        }

        private static object GetField(object target, string name)
        {
            return FindField(target.GetType(), name).GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FindField(target.GetType(), name).SetValue(target, value);
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }
                type = type.BaseType;
            }
            throw new InvalidOperationException("Missing test field " + name);
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(target, arguments);
        }
    }
}
