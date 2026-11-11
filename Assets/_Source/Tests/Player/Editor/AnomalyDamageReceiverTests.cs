using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyTestProtectionSource : MonoBehaviour, IAnomalyProtectionSource
    {
        public float Protection { get; set; }
        public int Calls { get; private set; }

        public float GetAnomalyProtection(AnomalyDamageType type, AnomalyStrength strength, AnomalyBalanceConfig config)
        {
            Calls++;
            return Protection;
        }
    }

    public sealed class AnomalyTestReadOnlyHealthSource : MonoBehaviour, IStalkerHealthSource
    {
        public float Health => 0.75f;
    }

    public sealed class AnomalyTestMutableHealthSource : MonoBehaviour, IStalkerMutableHealthSource
    {
        public float Health { get; private set; } = 0.75f;

        public void SetHealth(float normalizedHealth)
        {
            Health = Mathf.Clamp01(normalizedHealth);
        }
    }

    public sealed class AnomalyDamageReceiverTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

            public GameObject Root { get; }
            public StalkerActorCondition Condition { get; }
            public AnomalyDamageReceiver Receiver { get; }
            public AnomalyBalanceConfig Balance { get; }

            public Rig()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                Root = new GameObject(nameof(AnomalyDamageReceiverTests));
                Root.hideFlags = HideFlags.HideAndDontSave;
                Root.SetActive(false);
                SceneManager.MoveGameObjectToScene(Root, _scene);
                Condition = Root.AddComponent<StalkerActorCondition>();
                Condition.Initialize(CreateAsset<StalkerMovementProfile>());
                Receiver = Root.AddComponent<AnomalyDamageReceiver>();
                Receiver.Initialize(Condition);
                Balance = CreateAsset<AnomalyBalanceConfig>();
                Root.SetActive(true);
            }

            public AnomalyVariantConfig Profile(AnomalyDamageType type, float baseDamage = 20f, float multiplier = 1f, AnomalyStrength strength = AnomalyStrength.Average)
            {
                var archetype = CreateAsset<AnomalyArchetypeConfig>();
                SetField(archetype, "_id", "test_anomaly");
                SetField(archetype, "_damageType", type);
                SetField(archetype, "_baseDamage", baseDamage);
                var variant = CreateAsset<AnomalyVariantConfig>();
                SetField(variant, "_sourceSection", "test_anomaly_average");
                SetField(variant, "_archetype", archetype);
                SetField(variant, "_strength", strength);
                SetField(variant, "_damageMultiplier", multiplier);
                return variant;
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Root);
                foreach (ScriptableObject asset in _assets)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
                EditorSceneManager.ClosePreviewScene(_scene);
            }

            private T CreateAsset<T>() where T : ScriptableObject
            {
                T asset = ScriptableObject.CreateInstance<T>();
                asset.hideFlags = HideFlags.HideAndDontSave;
                _assets.Add(asset);
                return asset;
            }
        }

        [Test]
        public void DuplicateAndStaleEventsAreRejectedWhileSourcesAndGenerationsRemainIndependent()
        {
            using (var rig = new Rig())
            {
                AnomalyVariantConfig profile = rig.Profile(AnomalyDamageType.Shock, 10f);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 2), profile, rig.Balance), Is.True);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 2), profile, rig.Balance), Is.False);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), profile, rig.Balance), Is.False);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 3), profile, rig.Balance), Is.True);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(2, 1, 2), profile, rig.Balance), Is.True);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 2, 1), profile, rig.Balance), Is.True);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 4), profile, rig.Balance), Is.False);
                Assert.That(rig.Condition.Health, Is.EqualTo(60f));
            }
        }

        [Test]
        public void IndependentProtectionIsSampledOnceAndDuplicateProvidersAreNotCountedTwice()
        {
            using (var rig = new Rig())
            {
                var first = rig.Root.AddComponent<AnomalyTestProtectionSource>();
                var second = rig.Root.AddComponent<AnomalyTestProtectionSource>();
                first.Protection = 0.5f;
                second.Protection = 0.2f;
                rig.Receiver.Initialize(rig.Condition, first, second, first);
                AnomalyDamageResult result = default;
                rig.Receiver.HitApplied += value => result = value;
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(AnomalyDamageType.Shock), rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.EqualTo(92f).Within(0.0001f));
                Assert.That(first.Calls, Is.EqualTo(1));
                Assert.That(second.Calls, Is.EqualTo(1));
                Assert.That(result.Protection, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(result.AppliedDamage, Is.EqualTo(8f).Within(0.0001f));
                Assert.That(result.InputDamage, Is.EqualTo(20f));
            }
        }

        [Test]
        public void InvalidProtectionIsReportedOnceAndFiniteProtectionIsClamped()
        {
            using (var rig = new Rig())
            {
                var invalid = rig.Root.AddComponent<AnomalyTestProtectionSource>();
                var complete = rig.Root.AddComponent<AnomalyTestProtectionSource>();
                invalid.Protection = float.NaN;
                complete.Protection = 1.5f;
                rig.Receiver.Initialize(rig.Condition, invalid, complete);
                LogAssert.Expect(LogType.Warning, "Anomaly protection sources must return a finite fraction. The invalid protection value was ignored.");
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(AnomalyDamageType.Shock), rig.Balance), Is.True);
                Assert.That(rig.Receiver.GetProtection(AnomalyDamageType.Shock, AnomalyStrength.Average, rig.Balance), Is.EqualTo(1f));
                Assert.That(rig.Condition.Health, Is.EqualTo(100f));
            }
        }

        [Test]
        public void StrengthChangesUnprotectedHealthDamageThroughTheVariantMultiplier()
        {
            using (var rig = new Rig())
            {
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(AnomalyDamageType.Strike, 20f, 0.5f, AnomalyStrength.Weak), rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.EqualTo(90f));
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 2), rig.Profile(AnomalyDamageType.Strike, 20f, 1f), rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.EqualTo(70f));
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 3), rig.Profile(AnomalyDamageType.Strike, 20f, 1.5f, AnomalyStrength.Strong), rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.EqualTo(40f));
            }
        }

        [Test]
        public void MutableExternalHealthIsAuthoritativeAndInternalFallbackRemainsUntouched()
        {
            using (var rig = new Rig())
            {
                var source = rig.Root.AddComponent<AnomalyTestMutableHealthSource>();
                rig.Condition.SetHealthSource(source);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(AnomalyDamageType.Burn), rig.Balance), Is.True);
                Assert.That(source.Health, Is.EqualTo(0.55f).Within(0.0001f));
                Assert.That(rig.Condition.Health, Is.EqualTo(55f).Within(0.0001f));
                Assert.That(rig.Condition.GetStat(StalkerStatKind.Health).Value, Is.EqualTo(100f));
            }
        }

        [Test]
        public void ReadOnlyExternalHealthRejectsDamageAndWarnsOnceWithoutChangingFallback()
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetHealthSource(rig.Root.AddComponent<AnomalyTestReadOnlyHealthSource>());
                AnomalyVariantConfig profile = rig.Profile(AnomalyDamageType.Shock);
                var hit = new AnomalyHitEvent(1, 1, 1);
                LogAssert.Expect(LogType.Warning, "Anomaly damage requires the assigned health source to implement IStalkerMutableHealthSource. The authoritative health was not changed.");
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                Assert.That(rig.Condition.Health, Is.EqualTo(75f));
                Assert.That(rig.Condition.GetStat(StalkerStatKind.Health).Value, Is.EqualTo(100f));
                rig.Condition.SetHealthSource(rig.Root.AddComponent<AnomalyTestMutableHealthSource>());
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.EqualTo(55f).Within(0.0001f));
            }
        }

        [Test]
        public void PsychicHitChangesOnlyPsyHealth()
        {
            using (var rig = new Rig())
            {
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(AnomalyDamageType.Telepatic, 25f), rig.Balance), Is.True);
                Assert.That(rig.Condition.PsyHealth, Is.EqualTo(75f));
                Assert.That(rig.Condition.Health, Is.EqualTo(100f));
                Assert.That(rig.Receiver.Radiation, Is.Zero);
            }
        }

        [Test]
        public void RadiationHitAccumulatesDoseAndLaterToxicityUsesTheSharedRates()
        {
            using (var rig = new Rig())
            {
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(AnomalyDamageType.Radiation, 25f), rig.Balance), Is.True);
                Assert.That(rig.Receiver.Radiation, Is.EqualTo(0.25f));
                Assert.That(rig.Condition.Health, Is.EqualTo(100f));
                rig.Receiver.Tick(10f, rig.Balance);
                float expectedDamage = (0.25f * 10f - 0.5f * rig.Balance.RadiationDecayRate * 100f) * rig.Balance.RadiationHealthRate;
                Assert.That(rig.Receiver.Radiation, Is.EqualTo(0.25f - rig.Balance.RadiationDecayRate * 10f).Within(0.000001f));
                Assert.That(rig.Condition.Health, Is.EqualTo(100f - expectedDamage).Within(0.0001f));
                Assert.That(rig.Condition.PsyHealth, Is.EqualTo(100f));
            }
        }

        [Test]
        public void RadiationIntegrationIsFrameIndependentAndStopsToxicityAtZeroDose()
        {
            var single = new AnomalyRadiationState();
            var split = new AnomalyRadiationState();
            single.AddDosePoints(100f);
            split.AddDosePoints(100f);
            float singleDamage = single.Tick(20f, 0.1f, 0.2f);
            float splitDamage = 0f;
            for (int i = 0; i < 2000; i++)
            {
                splitDamage += split.Tick(0.01f, 0.1f, 0.2f);
            }
            Assert.That(single.Level, Is.Zero);
            Assert.That(split.Level, Is.Zero);
            Assert.That(singleDamage, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(splitDamage, Is.EqualTo(singleDamage).Within(0.0001f));
            Assert.That(single.Tick(20f, 0.1f, 0.2f), Is.Zero);
        }

        [Test]
        public void FullRadiationDealsPointTwoHpPerSecondWithoutRecovery()
        {
            var state = new AnomalyRadiationState();
            state.AddDosePoints(250f);
            Assert.That(state.Level, Is.EqualTo(1f));
            Assert.That(state.Tick(10f, 0f, 0.2f), Is.EqualTo(2f).Within(0.0001f));
            Assert.That(state.Level, Is.EqualTo(1f));
        }

        [Test]
        public void InvalidNumericDataDoesNotChangeHealthOrConsumeTheEvent()
        {
            using (var rig = new Rig())
            {
                AnomalyVariantConfig profile = rig.Profile(AnomalyDamageType.Shock);
                var hit = new AnomalyHitEvent(1, 1, 1);
                SetField(profile, "_damageMultiplier", float.NaN);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                SetField(profile, "_damageMultiplier", 1f);
                SetField(rig.Balance, "_radiationDamagePerSecond", float.PositiveInfinity);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                SetField(rig.Balance, "_radiationDamagePerSecond", 0.2f);
                Assert.That(rig.Condition.Health, Is.EqualTo(100f));
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.EqualTo(80f));
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
