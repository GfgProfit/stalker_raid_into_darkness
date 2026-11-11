using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyDamageSourceTests
    {
        [Test]
        public void ConfirmedCycleWearsOnceForMultipleCollidersWhileVisualReplayDoesNotWear()
        {
            using (var rig = new Rig())
            {
                var wear = rig.Player.Player.gameObject.AddComponent<AnomalyWearTestSource>();
                rig.Player.Receiver.RegisterProtectionSource(wear);
                SetField(rig.Variant.Archetype, "_equipmentWearPerHitPercent", 2f);
                rig.Source.Register(rig.Player.Collider);
                rig.Source.Register(rig.CreateSecondCollider(rig.Player));
                rig.Source.Visual.PlayDischarge();
                Assert.That(wear.Applications, Is.Zero);
                rig.Source.StartCycle();
                Assert.That(wear.Applications, Is.EqualTo(1));
                Assert.That(wear.Condition, Is.EqualTo(0.98f).Within(0.000001f));
                Invoke(rig.Source.Reaction, "ApplyPulse");
                Assert.That(wear.Applications, Is.EqualTo(1));
                rig.Source.NextCycle();
                Assert.That(wear.Applications, Is.EqualTo(2));
                Assert.That(wear.Condition, Is.EqualTo(0.96f).Within(0.000001f));
            }
        }

        [Test]
        public void BoltOnlyActivationCannotWearThePlayersEquipment()
        {
            using (var rig = new Rig())
            {
                var wear = rig.Player.Player.gameObject.AddComponent<AnomalyWearTestSource>();
                rig.Player.Receiver.RegisterProtectionSource(wear);
                SetField(rig.Variant.Archetype, "_equipmentWearPerHitPercent", 2f);
                rig.Source.Register(rig.CreateBolt());
                rig.Source.StartCycle();
                Assert.That(wear.Captures, Is.Zero);
                Assert.That(wear.Applications, Is.Zero);
                Assert.That(wear.Condition, Is.EqualTo(1f));
            }
        }

        [Test]
        public void SeparateSourceCyclesWearTheSameEquipmentIndependently()
        {
            using (var rig = new Rig())
            {
                var wear = rig.Player.Player.gameObject.AddComponent<AnomalyWearTestSource>();
                rig.Player.Receiver.RegisterProtectionSource(wear);
                SetField(rig.Variant.Archetype, "_equipmentWearPerHitPercent", 2f);
                Zone second = rig.CreateZone("SecondWearSource");
                rig.Source.Register(rig.Player.Collider);
                second.Register(rig.Player.Collider);
                rig.Source.StartCycle();
                second.StartCycle();
                Assert.That(wear.Applications, Is.EqualTo(2));
                Assert.That(wear.Condition, Is.EqualTo(0.96f).Within(0.000001f));
            }
        }

        private sealed class Target
        {
            public StalkerPlayerController Player { get; }
            public StalkerActorCondition Condition { get; }
            public SphereCollider Collider { get; }
            public AnomalyDamageReceiver Receiver { get; }
            public List<AnomalyDamageResult> Hits { get; } = new List<AnomalyDamageResult>();

            public Target(GameObject root, StalkerMovementProfile movement)
            {
                Player = root.AddComponent<StalkerPlayerController>();
                StalkerMovementMotor motor = root.GetComponent<StalkerMovementMotor>();
                motor.Initialize(movement);
                SetField(Player, "_motor", motor);
                SetField(Player, "_profile", movement);
                Condition = root.GetComponent<StalkerActorCondition>();
                Condition.Initialize(movement);
                Condition.ApplyConfiguration(null);
                Condition.SetHealth(100f);
                Collider = root.AddComponent<SphereCollider>();
                Collider.radius = 0.1f;
                Receiver = root.AddComponent<AnomalyDamageReceiver>();
                Receiver.Initialize(Condition);
                Receiver.HitApplied += Hits.Add;
                root.SetActive(true);
            }
        }

        private sealed class Zone
        {
            public AnomalyLightTestVisual Visual { get; }
            public AnomalyPlayerReaction Reaction { get; }
            public AnomalyDamageSource Source { get; }
            public SphereCollider Shape { get; }

            public Zone(GameObject root, AnomalyVariantConfig variant, AnomalyBalanceConfig balance)
            {
                SphereCollider proxy = root.AddComponent<SphereCollider>();
                proxy.radius = 5f;
                proxy.isTrigger = true;
                Shape = root.AddComponent<SphereCollider>();
                Shape.radius = 1f;
                Shape.isTrigger = true;
                XRayAnomalyActivationVolume volume = root.AddComponent<XRayAnomalyActivationVolume>();
                volume.Configure(proxy, new Collider[] { Shape });
                Visual = root.AddComponent<AnomalyLightTestVisual>();
                Reaction = root.AddComponent<AnomalyPlayerReaction>();
                SetField(Reaction, "_visual", Visual);
                SetField(Reaction, "_activationVolume", volume);
                SetField(Reaction, "_smallObjectIgnoreDelay", -1f);
                Source = root.AddComponent<AnomalyDamageSource>();
                SetField(Source, "_reaction", Reaction);
                root.SetActive(true);
                Invoke(Reaction, "OnDisable");
                Invoke(Reaction, "OnEnable");
                Invoke(Source, "OnDisable");
                Invoke(Source, "OnEnable");
                Source.ConfigureDamage(variant);
                SetField(Source, "_balance", balance);
                Visual.ShowIdle();
            }

            public void Register(Collider collider)
            {
                Invoke(Reaction, "OnTriggerEnter", collider);
            }

            public void StartCycle()
            {
                Invoke(Reaction, "Update");
                Invoke(Reaction, "Update");
            }

            public void NextCycle()
            {
                Invoke(Reaction, "Update");
                Invoke(Reaction, "Update");
            }
        }

        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly List<GameObject> _roots = new List<GameObject>();
            private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

            public AnomalyBalanceConfig Balance { get; }
            public AnomalyVariantConfig Variant { get; }
            public Target Player { get; }
            public Zone Source { get; }

            public Rig(bool continuous = false, float damage = 5f)
            {
                _scene = EditorSceneManager.NewPreviewScene();
                Balance = Asset<AnomalyBalanceConfig>();
                AnomalyArchetypeConfig archetype = Asset<AnomalyArchetypeConfig>();
                SetField(archetype, "_id", "test_source");
                SetField(archetype, "_damageType", AnomalyDamageType.Shock);
                SetField(archetype, "_baseDamage", damage);
                Variant = CreateVariant(archetype, "zone_test_average", continuous);
                SetField(Balance, "_archetypes", new[] { archetype });
                SetField(Balance, "_variants", new[] { Variant });
                Player = CreateTarget("Target");
                Source = CreateZone("Anomaly");
            }

            public Target CreateTarget(string name)
            {
                return new Target(CreateRoot(name), Asset<StalkerMovementProfile>());
            }

            public Zone CreateZone(string name)
            {
                return new Zone(CreateRoot(name), Variant, Balance);
            }

            public void DelayPulse(float delay)
            {
                AnomalyInfluenceProfile influence = Asset<AnomalyInfluenceProfile>();
                SetField(influence, "_kind", AnomalyInfluenceKind.None);
                SetField(influence, "_pulseDelay", delay);
                SetField(Source.Reaction, "_influenceProfile", influence);
                SetField(Source.Reaction, "_cycle", new AnomalyCycle(0f, 100f, 100f));
            }

            public SphereCollider CreateSecondCollider(Target target)
            {
                GameObject child = new GameObject("SecondCollider");
                child.transform.SetParent(target.Player.transform, false);
                SphereCollider collider = child.AddComponent<SphereCollider>();
                collider.radius = 0.1f;
                return collider;
            }

            public SphereCollider CreateBolt()
            {
                GameObject root = CreateRoot("Bolt");
                SphereCollider collider = root.AddComponent<SphereCollider>();
                collider.radius = 0.05f;
                root.AddComponent<AnomalyProbe>();
                root.SetActive(true);
                return collider;
            }

            public AnomalyVariantConfig CreateVariant(AnomalyArchetypeConfig archetype, string section, bool continuous = false)
            {
                AnomalyVariantConfig variant = Asset<AnomalyVariantConfig>();
                SetField(variant, "_sourceSection", section);
                SetField(variant, "_archetype", archetype);
                SetField(variant, "_strength", AnomalyStrength.Average);
                SetField(variant, "_damageMultiplier", 1f);
                SetField(variant, "_continuous", continuous);
                SetField(variant, "_hitInterval", 0.1f);
                return variant;
            }

            public void Dispose()
            {
                for (int i = _roots.Count - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(_roots[i]);
                }
                foreach (ScriptableObject asset in _assets)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
                EditorSceneManager.ClosePreviewScene(_scene);
            }

            private GameObject CreateRoot(string name)
            {
                var root = new GameObject(name);
                root.hideFlags = HideFlags.HideAndDontSave;
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, _scene);
                _roots.Add(root);
                return root;
            }

            private T Asset<T>() where T : ScriptableObject
            {
                T asset = ScriptableObject.CreateInstance<T>();
                asset.hideFlags = HideFlags.HideAndDontSave;
                _assets.Add(asset);
                return asset;
            }
        }

        [Test]
        public void ConfirmedCycleHitsEachPlayerOnceAcrossMultipleCollidersAndCanRepeatNextCycle()
        {
            using (var rig = new Rig())
            {
                Target second = rig.CreateTarget("SecondTarget");
                rig.Source.Register(rig.Player.Collider);
                rig.Source.Register(rig.CreateSecondCollider(rig.Player));
                rig.Source.Register(second.Collider);
                rig.Source.Register(rig.CreateSecondCollider(second));
                rig.Source.StartCycle();
                Assert.That(rig.Player.Hits.Count, Is.EqualTo(1));
                Assert.That(second.Hits.Count, Is.EqualTo(1));
                Assert.That(rig.Player.Condition.Health, Is.EqualTo(95f));
                Assert.That(second.Condition.Health, Is.EqualTo(95f));
                Assert.That(rig.Player.Hits[0].Hit.Sequence, Is.EqualTo(second.Hits[0].Hit.Sequence));
                Invoke(rig.Source.Reaction, "ApplyPulse");
                Invoke(rig.Source.Reaction, "ApplyPulse");
                Assert.That(rig.Player.Hits.Count, Is.EqualTo(1));
                Assert.That(second.Hits.Count, Is.EqualTo(1));
                rig.Source.NextCycle();
                Assert.That(rig.Player.Hits.Count, Is.EqualTo(2));
                Assert.That(second.Hits.Count, Is.EqualTo(2));
                Assert.That(rig.Player.Hits[1].Hit.Sequence, Is.GreaterThan(rig.Player.Hits[0].Hit.Sequence));
                Assert.That(rig.Player.Condition.Health, Is.EqualTo(90f));
            }
        }

        [Test]
        public void VisualDischargeAloneCannotConfirmOrApplyHealthDamage()
        {
            using (var rig = new Rig())
            {
                int confirmed = 0;
                rig.Source.Reaction.HitConfirmed += (_, _) => confirmed++;
                rig.Source.Register(rig.Player.Collider);
                rig.Source.Visual.PlayDischarge();
                Invoke(rig.Source.Reaction, "ApplyPulse");
                Assert.That(confirmed, Is.Zero);
                Assert.That(rig.Player.Hits, Is.Empty);
                Assert.That(rig.Player.Condition.Health, Is.EqualTo(100f));
                rig.Source.NextCycle();
                Assert.That(confirmed, Is.EqualTo(1));
                Assert.That(rig.Player.Condition.Health, Is.EqualTo(95f));
            }
        }

        [Test]
        public void VisualDischargeCannotCancelOrPostponeAnAlreadyConfirmedDelayedHit()
        {
            using (var rig = new Rig())
            {
                rig.DelayPulse(10f);
                rig.Source.Register(rig.Player.Collider);
                rig.Source.StartCycle();
                Assert.That(rig.Player.Hits, Is.Empty);
                float deadline = (float)GetField(rig.Source.Reaction, "_pulseAt");
                Assert.That(GetField(rig.Source.Reaction, "_confirmedPulse"), Is.True);
                rig.Source.Visual.PlayDischarge();
                Assert.That(GetField(rig.Source.Reaction, "_confirmedPulse"), Is.True);
                Assert.That((float)GetField(rig.Source.Reaction, "_pulseAt"), Is.EqualTo(deadline));
                SetField(rig.Source.Reaction, "_pulseAt", Time.time - 1f);
                Invoke(rig.Source.Reaction, "Update");
                Assert.That(rig.Player.Hits.Count, Is.EqualTo(1));
                Assert.That(rig.Player.Condition.Health, Is.EqualTo(95f));
                Invoke(rig.Source.Reaction, "ApplyPulse");
                Assert.That(rig.Player.Hits.Count, Is.EqualTo(1));
            }
        }

        [TestCase("source")]
        [TestCase("reaction")]
        [TestCase("visual")]
        [TestCase("player")]
        [TestCase("collider")]
        [TestCase("shape")]
        public void DisabledParticipantsCannotApplyCycleOrPeriodicDamage(string disabled)
        {
            using (var rig = new Rig())
            {
                rig.Source.Register(rig.Player.Collider);
                switch (disabled)
                {
                    case "source":
                        rig.Source.Source.enabled = false;
                        Invoke(rig.Source.Source, "OnDisable");
                        break;
                    case "reaction":
                        rig.Source.Reaction.enabled = false;
                        Invoke(rig.Source.Reaction, "OnDisable");
                        break;
                    case "visual":
                        rig.Source.Visual.DisableVisual();
                        break;
                    case "player":
                        rig.Player.Player.enabled = false;
                        break;
                    case "collider":
                        rig.Player.Collider.enabled = false;
                        break;
                    case "shape":
                        rig.Source.Shape.enabled = false;
                        break;
                }
                rig.Source.StartCycle();
                SetField(rig.Variant, "_continuous", true);
                rig.Source.Source.Tick(1f);
                Assert.That(rig.Player.Hits, Is.Empty);
                Assert.That(rig.Player.Condition.Health, Is.EqualTo(100f));
            }
        }

        [Test]
        public void LeavingActualShapesRejectsCachedContactsWithoutPhysicsSimulation()
        {
            using (var rig = new Rig())
            {
                rig.Source.Register(rig.Player.Collider);
                rig.Player.Player.transform.position = new Vector3(50f, 0f, 0f);
                rig.Source.StartCycle();
                SetField(rig.Variant, "_continuous", true);
                rig.Source.Source.Tick(1f);
                Assert.That(rig.Player.Hits, Is.Empty);
                Assert.That(rig.Player.Condition.Health, Is.EqualTo(100f));
            }
        }

        [Test]
        public void BoltCanTriggerPresentationWithoutDamagingAnUncontactedPlayer()
        {
            using (var rig = new Rig())
            {
                int confirmed = 0;
                rig.Source.Reaction.HitConfirmed += (_, _) => confirmed++;
                rig.Source.Register(rig.CreateBolt());
                rig.Source.StartCycle();
                Assert.That(rig.Source.Reaction.Phase, Is.EqualTo(AnomalyPhase.Blowout));
                Assert.That(confirmed, Is.Zero);
                Assert.That(rig.Player.Hits, Is.Empty);
                Assert.That(rig.Player.Condition.Health, Is.EqualTo(100f));
            }
        }

        [Test]
        public void PeriodicDamageIsIndependentOfFramePartitionAndDeduplicatesColliders()
        {
            using (var split = new Rig(true, 1f))
            using (var single = new Rig(true, 1f))
            {
                split.Source.Register(split.Player.Collider);
                split.Source.Register(split.CreateSecondCollider(split.Player));
                single.Source.Register(single.Player.Collider);
                for (int i = 0; i < 10; i++)
                {
                    split.Source.Source.Tick(0.1f);
                }
                single.Source.Source.Tick(1f);
                Assert.That(split.Player.Hits.Count, Is.EqualTo(10));
                Assert.That(single.Player.Hits.Count, Is.EqualTo(10));
                Assert.That(split.Player.Condition.Health, Is.EqualTo(90f));
                Assert.That(single.Player.Condition.Health, Is.EqualTo(split.Player.Condition.Health));
                for (int i = 0; i < 10; i++)
                {
                    Assert.That(split.Player.Hits[i].Hit.Sequence, Is.EqualTo(single.Player.Hits[i].Hit.Sequence));
                }
            }
        }

        [Test]
        public void ContactExitClearsPeriodicDebtBeforeReentry()
        {
            using (var rig = new Rig(true, 1f))
            {
                rig.Source.Register(rig.Player.Collider);
                rig.Source.Source.Tick(0.06f);
                Assert.That(rig.Player.Hits, Is.Empty);
                rig.Player.Player.transform.position = new Vector3(50f, 0f, 0f);
                Physics.SyncTransforms();
                Invoke(rig.Source.Reaction, "OnTriggerExit", rig.Player.Collider);
                rig.Source.Source.Tick(0.04f);
                rig.Player.Player.transform.position = Vector3.zero;
                Physics.SyncTransforms();
                rig.Source.Register(rig.Player.Collider);
                rig.Source.Source.Tick(0.04f);
                Assert.That(rig.Player.Hits, Is.Empty);
                rig.Source.Source.Tick(0.06f);
                Assert.That(rig.Player.Hits.Count, Is.EqualTo(1));
                Assert.That(rig.Player.Condition.Health, Is.EqualTo(99f));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SeparateZonesHaveIndependentEventsWithoutAGlobalCooldown(bool continuous)
        {
            using (var rig = new Rig(continuous, 1f))
            {
                Zone second = rig.CreateZone("SecondAnomaly");
                rig.Source.Register(rig.Player.Collider);
                second.Register(rig.Player.Collider);
                if (continuous)
                {
                    rig.Source.Source.Tick(0.1f);
                    second.Source.Tick(0.1f);
                }
                else
                {
                    rig.Source.StartCycle();
                    second.StartCycle();
                }
                Assert.That(rig.Player.Hits.Count, Is.EqualTo(2));
                Assert.That(rig.Player.Hits[0].Hit.SourceId, Is.Not.EqualTo(rig.Player.Hits[1].Hit.SourceId));
                Assert.That(rig.Player.Condition.Health, Is.EqualTo(98f));
            }
        }

        [Test]
        public void CatalogSectionsResolveDistinctVariantReferencesCaseInsensitively()
        {
            using (var rig = new Rig())
            {
                AnomalyVariantConfig alias = rig.CreateVariant(rig.Variant.Archetype, "zone_test_alias_average");
                SetField(rig.Balance, "_variants", new[] { rig.Variant, alias });
                Assert.That(rig.Balance.FindVariant("ZONE_TEST_AVERAGE"), Is.SameAs(rig.Variant));
                Assert.That(rig.Balance.FindVariant("ZONE_TEST_ALIAS_AVERAGE"), Is.SameAs(alias));
                Assert.That(alias, Is.Not.SameAs(rig.Variant));
                Assert.That(alias.Archetype, Is.SameAs(rig.Variant.Archetype));
                Assert.That(alias.InputDamage, Is.EqualTo(rig.Variant.InputDamage));
                rig.Source.Source.ConfigureDamage(alias);
                Assert.That(rig.Source.Source.Variant, Is.SameAs(alias));
                Assert.That(rig.Balance.FindVariant("zone_missing"), Is.Null);
            }
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(target, arguments);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(target);
        }
    }
}
