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
    public sealed class AnomalyWearTestSource : MonoBehaviour, IAnomalyProtectionSource, IAnomalyEquipmentWearSource
    {
        public float Condition { get; set; } = 1f;
        public float Durability { get; set; }
        public float BaseProtection { get; set; }
        public string ItemId { get; set; } = "test-outfit";
        public bool DuplicateTarget { get; set; }
        public int Captures { get; private set; }
        public int Applications { get; private set; }
        public Action Capturing { get; set; }
        public Action Protecting { get; set; }
        public Action Applying { get; set; }

        public float GetAnomalyProtection(AnomalyDamageType type, AnomalyStrength strength, AnomalyBalanceConfig config)
        {
            Protecting?.Invoke();
            return AnomalyProtectionMath.Effective(BaseProtection * 100f, Condition, config);
        }

        public void CaptureEquipmentWear(AnomalyDamageType type, AnomalyBalanceConfig config, List<AnomalyEquipmentWearTarget> targets)
        {
            Captures++;
            Capturing?.Invoke();
            var target = new AnomalyEquipmentWearTarget(this, ItemId, Condition, Durability);
            targets.Add(target);
            if (DuplicateTarget)
            {
                targets.Add(target);
            }
        }

        public void ApplyEquipmentWear(AnomalyEquipmentWearTarget target, float remainingCondition)
        {
            Applications++;
            Condition = remainingCondition;
            Applying?.Invoke();
        }
    }

    public sealed class AnomalyWearTestAuthority : MonoBehaviour, IAnomalyDamageAuthority
    {
        public bool IsAuthoritative { get; set; } = true;
    }

    public sealed class AnomalyWearTestHealthSource : MonoBehaviour, IStalkerMutableHealthSource
    {
        public float Health { get; private set; } = 1f;
        public Action Applying { get; set; }

        public void SetHealth(float normalizedHealth)
        {
            Health = normalizedHealth;
            Applying?.Invoke();
        }
    }

    public sealed class AnomalyEquipmentWearReceiverTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

            public GameObject Root { get; }
            public StalkerActorCondition Condition { get; }
            public AnomalyDamageReceiver Receiver { get; }
            public AnomalyWearTestSource Source { get; }
            public AnomalyBalanceConfig Balance { get; }

            public Rig()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                Root = new GameObject(nameof(AnomalyEquipmentWearReceiverTests));
                Root.hideFlags = HideFlags.HideAndDontSave;
                Root.SetActive(false);
                SceneManager.MoveGameObjectToScene(Root, _scene);
                Condition = Root.AddComponent<StalkerActorCondition>();
                Condition.Initialize(CreateAsset<StalkerMovementProfile>());
                Source = Root.AddComponent<AnomalyWearTestSource>();
                Receiver = Root.AddComponent<AnomalyDamageReceiver>();
                Receiver.Initialize(Condition, Source);
                Balance = CreateAsset<AnomalyBalanceConfig>();
                Root.SetActive(true);
            }

            public AnomalyVariantConfig Profile(float wear = 2f, AnomalyDamageType type = AnomalyDamageType.Shock, float multiplier = 1f, AnomalyStrength strength = AnomalyStrength.Average)
            {
                var archetype = CreateAsset<AnomalyArchetypeConfig>();
                SetField(archetype, "_id", "test_wear");
                SetField(archetype, "_damageType", type);
                SetField(archetype, "_baseDamage", 20f);
                SetField(archetype, "_equipmentWearPerHitPercent", wear);
                var variant = CreateAsset<AnomalyVariantConfig>();
                SetField(variant, "_sourceSection", "test_wear_variant");
                SetField(variant, "_archetype", archetype);
                SetField(variant, "_damageMultiplier", multiplier);
                SetField(variant, "_strength", strength);
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

        [TestCase(1f, 2f, 0f, 0.98f)]
        [TestCase(1f, 2f, 50f, 0.9866667f)]
        [TestCase(1f, 2f, 100f, 0.99f)]
        [TestCase(0f, 2f, 0f, 0f)]
        [TestCase(1.2f, 2f, 0f, 0.98f)]
        [TestCase(-0.1f, 2f, 0f, 0f)]
        [TestCase(1f, 2f, -1f, 0.98f)]
        [TestCase(1f, 2f, 101f, 0.99f)]
        [TestCase(1f, 100f, 0f, 0f)]
        [TestCase(0.01f, 2f, 0f, 0f)]
        public void WearMathPreservesUnitsAndConditionBounds(float condition, float wear, float durability, float expected)
        {
            Assert.That(AnomalyEquipmentWearMath.TryCalculateRemainingCondition(condition, wear, durability, out float remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(expected).Within(0.000001f));
        }

        [TestCase(float.NaN, 2f, 0f)]
        [TestCase(float.PositiveInfinity, 2f, 0f)]
        [TestCase(1f, float.NaN, 0f)]
        [TestCase(1f, float.PositiveInfinity, 0f)]
        [TestCase(1f, -1f, 0f)]
        [TestCase(1f, 101f, 0f)]
        [TestCase(1f, 2f, float.NaN)]
        [TestCase(1f, 2f, float.NegativeInfinity)]
        public void WearMathRejectsInvalidInputs(float condition, float wear, float durability)
        {
            Assert.That(AnomalyEquipmentWearMath.TryCalculateRemainingCondition(condition, wear, durability, out _), Is.False);
        }

        [Test]
        public void HitCapturesThenCalculatesProtectionThenCommitsHealthAndWearBeforePublishing()
        {
            using (var rig = new Rig())
            {
                var order = new List<string>();
                var health = rig.Root.AddComponent<AnomalyWearTestHealthSource>();
                rig.Condition.SetHealthSource(health);
                rig.Source.BaseProtection = 0.5f;
                rig.Source.Capturing = () => order.Add("capture");
                rig.Source.Protecting = () => order.Add("protection");
                health.Applying = () => order.Add("health");
                rig.Source.Applying = () => order.Add("wear");
                AnomalyDamageResult result = default;
                rig.Receiver.HitApplied += value =>
                {
                    result = value;
                    order.Add("published");
                    Assert.That(rig.Source.Condition, Is.EqualTo(0.98f).Within(0.000001f));
                    Assert.That(rig.Condition.Health, Is.EqualTo(90f).Within(0.0001f));
                };
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(), rig.Balance), Is.True);
                Assert.That(order, Is.EqualTo(new[] { "capture", "protection", "health", "wear", "published" }));
                Assert.That(result.Protection, Is.EqualTo(0.5f).Within(0.000001f));
                Assert.That(result.AppliedDamage, Is.EqualTo(10f).Within(0.0001f));
            }
        }

        [Test]
        public void ReducedConditionChangesOnlyTheNextHitsProtection()
        {
            using (var rig = new Rig())
            {
                rig.Source.BaseProtection = 0.5f;
                AnomalyVariantConfig profile = rig.Profile(10f);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), profile, rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.EqualTo(90f).Within(0.0001f));
                Assert.That(rig.Source.Condition, Is.EqualTo(0.9f).Within(0.000001f));
                float nextProtection = AnomalyProtectionMath.Effective(50f, 0.9f, rig.Balance);
                Assert.That(nextProtection, Is.LessThan(0.5f));
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 2), profile, rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.EqualTo(90f - 20f * (1f - nextProtection)).Within(0.0001f));
            }
        }

        [Test]
        public void WearUsesCapturedConditionEvenWhenHealthSetterChangesLiveState()
        {
            using (var rig = new Rig())
            {
                var health = rig.Root.AddComponent<AnomalyWearTestHealthSource>();
                rig.Condition.SetHealthSource(health);
                health.Applying = () => rig.Source.Condition = 0.5f;
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(), rig.Balance), Is.True);
                Assert.That(rig.Source.Condition, Is.EqualTo(0.98f).Within(0.000001f));
            }
        }

        [Test]
        public void FullProtectionAndZeroHealthDoNotChangeWearAmount()
        {
            using (var rig = new Rig())
            {
                rig.Source.BaseProtection = 1f;
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(), rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.EqualTo(100f));
                Assert.That(rig.Source.Condition, Is.EqualTo(0.98f).Within(0.000001f));
                rig.Condition.SetHealth(0f);
                rig.Source.BaseProtection = 0f;
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 2), rig.Profile(), rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.Zero);
                Assert.That(rig.Source.Condition, Is.EqualTo(0.96f).Within(0.000001f));
            }
        }

        [Test]
        public void StrengthChangesHealthDamageWithoutChangingWear()
        {
            using (var rig = new Rig())
            {
                float[] multipliers = { 0.5f, 1f, 1.5f };
                AnomalyStrength[] strengths = { AnomalyStrength.Weak, AnomalyStrength.Average, AnomalyStrength.Strong };
                for (int i = 0; i < strengths.Length; i++)
                {
                    rig.Source.Condition = 1f;
                    Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, (ulong)i + 1), rig.Profile(2f, AnomalyDamageType.Shock, multipliers[i], strengths[i]), rig.Balance), Is.True);
                    Assert.That(rig.Source.Condition, Is.EqualTo(0.98f).Within(0.000001f));
                }
                Assert.That(rig.Condition.Health, Is.EqualTo(40f).Within(0.0001f));
            }
        }

        [Test]
        public void ReadOnlyHealthRejectsWearAndLeavesTheEventAvailableForRetry()
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetHealthSource(rig.Root.AddComponent<AnomalyTestReadOnlyHealthSource>());
                AnomalyVariantConfig profile = rig.Profile();
                var hit = new AnomalyHitEvent(1, 1, 1);
                LogAssert.Expect(LogType.Warning, "Anomaly damage requires the assigned health source to implement IStalkerMutableHealthSource. The authoritative health was not changed.");
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                Assert.That(rig.Source.Condition, Is.EqualTo(1f));
                Assert.That(rig.Source.Applications, Is.Zero);
                rig.Condition.SetHealthSource(null);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.True);
                Assert.That(rig.Source.Applications, Is.EqualTo(1));
                Assert.That(rig.Condition.Health, Is.EqualTo(80f));
            }
        }

        [Test]
        public void SharedEventHistoryRejectsDuplicatesAndKeepsDifferentAnomaliesIndependent()
        {
            using (var rig = new Rig())
            {
                AnomalyVariantConfig profile = rig.Profile();
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 2), profile, rig.Balance), Is.True);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 2), profile, rig.Balance), Is.False);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), profile, rig.Balance), Is.False);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(2, 1, 2), profile, rig.Balance), Is.True);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 2, 1), profile, rig.Balance), Is.True);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 3), profile, rig.Balance), Is.False);
                Assert.That(rig.Source.Applications, Is.EqualTo(3));
                Assert.That(rig.Source.Condition, Is.EqualTo(0.94f).Within(0.000001f));
                Assert.That(rig.Condition.Health, Is.EqualTo(40f));
            }
        }

        [Test]
        public void DuplicateItemIdsAcrossProvidersWearTheItemOnlyOnce()
        {
            using (var rig = new Rig())
            {
                rig.Source.DuplicateTarget = true;
                var duplicate = rig.Root.AddComponent<AnomalyWearTestSource>();
                rig.Receiver.Initialize(rig.Condition, rig.Source, duplicate, rig.Source);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(), rig.Balance), Is.True);
                Assert.That(rig.Source.Captures, Is.EqualTo(1));
                Assert.That(rig.Source.Applications, Is.EqualTo(1));
                Assert.That(duplicate.Captures, Is.EqualTo(1));
                Assert.That(duplicate.Applications, Is.Zero);
                Assert.That(rig.Source.Condition, Is.EqualTo(0.98f).Within(0.000001f));
            }
        }

        [Test]
        public void InvalidWearSnapshotCannotPoisonHealthOrApplyNonFiniteCondition()
        {
            using (var rig = new Rig())
            {
                rig.Source.Durability = float.NaN;
                var hit = new AnomalyHitEvent(1, 1, 1);
                AnomalyVariantConfig profile = rig.Profile();
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.True);
                Assert.That(rig.Source.Applications, Is.Zero);
                Assert.That(rig.Source.Condition, Is.EqualTo(1f));
                Assert.That(rig.Condition.Health, Is.EqualTo(80f));
                rig.Source.Durability = 0f;
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
            }
        }

        [Test]
        public void ZeroWearSkipsEquipmentCaptureAndStillAppliesHealthDamage()
        {
            using (var rig = new Rig())
            {
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(1, 1, 1), rig.Profile(0f), rig.Balance), Is.True);
                Assert.That(rig.Source.Captures, Is.Zero);
                Assert.That(rig.Source.Applications, Is.Zero);
                Assert.That(rig.Condition.Health, Is.EqualTo(80f));
            }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        [TestCase(101f)]
        public void InvalidArchetypeWearRejectsTheWholeHitWithoutConsumingIt(float wear)
        {
            using (var rig = new Rig())
            {
                var hit = new AnomalyHitEvent(1, 1, 1);
                AnomalyVariantConfig profile = rig.Profile(wear);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                Assert.That(rig.Condition.Health, Is.EqualTo(100f));
                Assert.That(rig.Source.Captures, Is.Zero);
                Assert.That(rig.Source.Applications, Is.Zero);
                SetField(profile.Archetype, "_equipmentWearPerHitPercent", 2f);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.True);
            }
        }

        [Test]
        public void NoneDamageRejectsWearAndReceiverReinitializationPreservesAcceptedHistory()
        {
            using (var rig = new Rig())
            {
                var hit = new AnomalyHitEvent(1, 1, 1);
                Assert.That(rig.Receiver.ApplyHit(hit, rig.Profile(2f, AnomalyDamageType.None), rig.Balance), Is.False);
                AnomalyVariantConfig profile = rig.Profile();
                Assert.That(rig.Source.Captures, Is.Zero);
                Assert.That(rig.Condition.Health, Is.EqualTo(100f));
                rig.Receiver.enabled = false;
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.True);
                rig.Receiver.enabled = true;
                rig.Receiver.Initialize(rig.Condition, rig.Source);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                Assert.That(rig.Source.Applications, Is.EqualTo(1));
            }
        }

        [Test]
        public void DeniedAuthorityPreventsWearHealthAndRadiationMutationWithoutConsumingEvents()
        {
            using (var rig = new Rig())
            {
                var authority = rig.Root.AddComponent<AnomalyWearTestAuthority>();
                authority.IsAuthoritative = false;
                rig.Receiver.SetAuthoritySource(authority);
                var hit = new AnomalyHitEvent(1, 1, 1);
                AnomalyVariantConfig profile = rig.Profile();
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(2, 1, 1), rig.Profile(2f, AnomalyDamageType.Radiation), rig.Balance), Is.False);
                Assert.That(rig.Source.Captures, Is.Zero);
                Assert.That(rig.Source.Applications, Is.Zero);
                Assert.That(rig.Receiver.Radiation, Is.Zero);
                Assert.That(rig.Condition.Health, Is.EqualTo(100f));
                authority.IsAuthoritative = true;
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.True);
                var radiationHit = new AnomalyHitEvent(2, 1, 1);
                Assert.That(rig.Receiver.ApplyHit(radiationHit, rig.Profile(0f, AnomalyDamageType.Radiation), rig.Balance), Is.True);
                float radiation = rig.Receiver.Radiation;
                float health = rig.Condition.Health;
                authority.IsAuthoritative = false;
                rig.Receiver.Tick(10f, rig.Balance);
                Assert.That(rig.Receiver.Radiation, Is.EqualTo(radiation));
                Assert.That(rig.Condition.Health, Is.EqualTo(health));
                authority.IsAuthoritative = true;
                rig.Receiver.Tick(10f, rig.Balance);
                Assert.That(rig.Receiver.Radiation, Is.LessThan(radiation));
                Assert.That(rig.Condition.Health, Is.LessThan(health));
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
            }
        }

        [Test]
        public void AuthorityRevokedByHealthSetterPreventsWearAndKeepsTheAcceptedEventConsumed()
        {
            using (var rig = new Rig())
            {
                var authority = rig.Root.AddComponent<AnomalyWearTestAuthority>();
                rig.Receiver.SetAuthoritySource(authority);
                var health = rig.Root.AddComponent<AnomalyWearTestHealthSource>();
                rig.Condition.SetHealthSource(health);
                health.Applying = () => authority.IsAuthoritative = false;
                var hit = new AnomalyHitEvent(1, 1, 1);
                AnomalyVariantConfig profile = rig.Profile();
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.True);
                Assert.That(rig.Condition.Health, Is.EqualTo(80f).Within(0.0001f));
                Assert.That(rig.Source.Applications, Is.Zero);
                Assert.That(rig.Source.Condition, Is.EqualTo(1f));
                authority.IsAuthoritative = true;
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                Assert.That(rig.Source.Applications, Is.Zero);
                Assert.That(rig.Condition.Health, Is.EqualTo(80f).Within(0.0001f));
            }
        }

        [Test]
        public void AssignedInvalidDisabledAndDestroyedAuthoritiesFailClosed()
        {
            using (var rig = new Rig())
            {
                var hit = new AnomalyHitEvent(1, 1, 1);
                AnomalyVariantConfig profile = rig.Profile();
                rig.Receiver.SetAuthoritySource(rig.Source);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                var authority = rig.Root.AddComponent<AnomalyWearTestAuthority>();
                authority.enabled = false;
                rig.Receiver.SetAuthoritySource(authority);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                authority.enabled = true;
                UnityEngine.Object.DestroyImmediate(authority);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                Assert.That(rig.Source.Applications, Is.Zero);
                rig.Receiver.SetAuthoritySource(null);
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.True);
            }
        }

        [Test]
        public void ReentrantHealthAndWearCallbacksRejectReplayWhilePublishedHitsCanChainIndependentSources()
        {
            using (var rig = new Rig())
            {
                var health = rig.Root.AddComponent<AnomalyWearTestHealthSource>();
                rig.Condition.SetHealthSource(health);
                AnomalyVariantConfig profile = rig.Profile();
                var hit = new AnomalyHitEvent(1, 1, 1);
                int rejectedReplays = 0;
                health.Applying = () =>
                {
                    Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                    rejectedReplays++;
                };
                rig.Source.Applying = () =>
                {
                    Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.False);
                    rejectedReplays++;
                };
                int published = 0;
                rig.Receiver.HitApplied += result =>
                {
                    published++;
                    if (result.Hit.SourceId == 1)
                    {
                        Assert.That(rig.Receiver.ApplyHit(new AnomalyHitEvent(2, 1, 1), profile, rig.Balance), Is.True);
                    }
                };
                Assert.That(rig.Receiver.ApplyHit(hit, profile, rig.Balance), Is.True);
                Assert.That(rig.Source.Applications, Is.EqualTo(2));
                Assert.That(rig.Source.Condition, Is.EqualTo(0.96f).Within(0.000001f));
                Assert.That(rig.Condition.Health, Is.EqualTo(60f).Within(0.0001f));
                Assert.That(rejectedReplays, Is.EqualTo(4));
                Assert.That(published, Is.EqualTo(2));
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
