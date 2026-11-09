using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyContactPolicyTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly GameObject _anomaly;
            private readonly UnityEngine.Random.State _randomState;
            private readonly List<GameObject> _owners = new List<GameObject>();

            public GameObject Target { get; }
            public Transform RootBone { get; }
            public Transform ChildBone { get; }
            public SkinnedMeshRenderer Renderer { get; }
            public AnomalyContactEffects Effects { get; }
            public AnomalyPlayerReaction Reaction { get; }
            public SphereCollider Collider { get; }
            public AnomalyProbe Probe { get; }

            public Rig()
            {
                _randomState = UnityEngine.Random.state;
                _scene = EditorSceneManager.NewPreviewScene();
                _anomaly = Create(nameof(AnomalyContactPolicyTests));
                AnomalyLightTestVisual visual = _anomaly.AddComponent<AnomalyLightTestVisual>();
                Effects = _anomaly.AddComponent<AnomalyContactEffects>();
                Reaction = _anomaly.AddComponent<AnomalyPlayerReaction>();
                SetField(Reaction, "_visual", visual);
                SetField(Reaction, "_smallObjectIgnoreDelay", -1f);
                visual.ShowIdle();
                Target = Create("Target");
                Collider = Target.AddComponent<SphereCollider>();
                Probe = Target.AddComponent<AnomalyProbe>();
                RootBone = new GameObject("root_stalker").transform;
                RootBone.SetParent(Target.transform, false);
                RootBone.localPosition = new Vector3(0.1f, 0.2f, 0.3f);
                ChildBone = new GameObject("spine").transform;
                ChildBone.SetParent(RootBone, false);
                Renderer = Target.AddComponent<SkinnedMeshRenderer>();
                Renderer.rootBone = RootBone;
                Renderer.bones = new[] { ChildBone };
            }

            public object AddContact(float age)
            {
                Type type = typeof(AnomalyPlayerReaction).GetNestedType("ProbeContact", BindingFlags.NonPublic);
                Assert.That(type, Is.Not.Null);
                object contact = Activator.CreateInstance(type, new object[] { Probe, Time.time - age });
                Contacts()[Collider] = contact;
                return contact;
            }

            public IDictionary Contacts()
            {
                return (IDictionary)GetField(Reaction, "_probeContacts");
            }

            public AnomalyContactEffects CreateOwner(string name)
            {
                GameObject root = Create(name);
                _owners.Add(root);
                return root.AddComponent<AnomalyContactEffects>();
            }

            public void Dispose()
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(_anomaly);
                    foreach (GameObject owner in _owners)
                    {
                        UnityEngine.Object.DestroyImmediate(owner);
                    }
                    UnityEngine.Object.DestroyImmediate(Target);
                    EditorSceneManager.ClosePreviewScene(_scene);
                }
                finally
                {
                    UnityEngine.Random.state = _randomState;
                }
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
        public void NativeRootAnchorUsesImportedRootBonePositionWithNoAddedOffset()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Effects, "_useRootBone", true);
                rig.Target.transform.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(10f, 20f, 30f));
                for (int index = 0; index < 12; index++)
                {
                    Transform anchor = ChooseAnchor(rig);
                    Assert.That(anchor, Is.SameAs(rig.RootBone));
                    Assert.That(anchor.position, Is.EqualTo(rig.Target.transform.TransformPoint(rig.RootBone.localPosition)));
                }
                rig.RootBone.localPosition = Vector3.zero;
                Assert.That(ChooseAnchor(rig).position, Is.EqualTo(rig.Target.transform.position));
            }
        }

        [Test]
        public void NativeRootAnchorFallsBackToTargetWhenRootIsMissingOrForeign()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Effects, "_useRootBone", true);
                rig.Renderer.rootBone = null;
                Assert.That(ChooseAnchor(rig), Is.SameAs(rig.Target.transform));
                GameObject foreign = new GameObject("ForeignRoot");
                SceneManager.MoveGameObjectToScene(foreign, rig.Target.scene);
                try
                {
                    rig.Renderer.rootBone = foreign.transform;
                    Assert.That(ChooseAnchor(rig), Is.SameAs(rig.Target.transform));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(foreign);
                }
            }
        }

        [Test]
        public void ExistingRandomBoneModeRemainsTheDefault()
        {
            using (var rig = new Rig())
            {
                Assert.That(ChooseAnchor(rig), Is.SameAs(rig.ChildBone));
            }
        }

        [Test]
        public void NonlivingIdleLifetimeIsIndependentFromUnlimitedProbeActivation()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Reaction, "_nonlivingIdleStopDelay", 50f);
                object early = rig.AddContact(0.25f);
                Invoke(rig.Reaction, "RemoveExpiredContacts");
                Assert.That(IdleStopped(early), Is.False);
                object before = rig.AddContact(49.75f);
                Invoke(rig.Reaction, "RemoveExpiredContacts");
                Assert.That(IdleStopped(before), Is.False);
                object expired = rig.AddContact(50.25f);
                Invoke(rig.Reaction, "RemoveExpiredContacts");
                Assert.That(IdleStopped(expired), Is.True);
                Assert.That((bool)Invoke(rig.Reaction, "HasActiveProbe"), Is.True);
                Assert.That(rig.Contacts().Contains(rig.Collider), Is.True);
            }
        }

        [Test]
        public void LeavingAndReenteringRestartsNonlivingIdleLifetime()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Reaction, "_nonlivingIdleStopDelay", 50f);
                object expired = rig.AddContact(51f);
                Invoke(rig.Reaction, "RemoveExpiredContacts");
                Assert.That(IdleStopped(expired), Is.True);
                Invoke(rig.Reaction, "OnTriggerExit", rig.Collider);
                Assert.That(rig.Contacts().Contains(rig.Collider), Is.False);
                Invoke(rig.Reaction, "RegisterContact", rig.Collider);
                object entered = rig.Contacts()[rig.Collider];
                Invoke(rig.Reaction, "RemoveExpiredContacts");
                Assert.That(entered, Is.Not.SameAs(expired));
                Assert.That(IdleStopped(entered), Is.False);
            }
        }

        [Test]
        public void NegativeNonlivingIdleDelayDisablesOnlyPresentationExpiry()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Reaction, "_nonlivingIdleStopDelay", -1f);
                object contact = rig.AddContact(500f);
                Invoke(rig.Reaction, "RemoveExpiredContacts");
                Assert.That(IdleStopped(contact), Is.False);
                Assert.That((bool)Invoke(rig.Reaction, "HasActiveProbe"), Is.True);
            }
        }

        [Test]
        public void ExistingNonlivingIdleExpiryRemainsUnchangedWithoutAnOverride()
        {
            using (var rig = new Rig())
            {
                object contact = rig.AddContact(0.25f);
                Invoke(rig.Reaction, "RemoveExpiredContacts");
                Assert.That(IdleStopped(contact), Is.True);
            }
        }

        [Test]
        public void TargetRegistryDeduplicatesSourceGroupAndBoneAcrossZoneOwners()
        {
            using (var rig = new Rig())
            {
                AnomalyContactRegistry registry = rig.Target.AddComponent<AnomalyContactRegistry>();
                AnomalyContactEffects other = rig.CreateOwner("OtherZone");
                int token = (int)Invoke(registry, "Register", "source/group", rig.RootBone, rig.Effects);
                Assert.That(token, Is.Not.Zero);
                Assert.That((int)Invoke(registry, "Register", "source/group", rig.RootBone, other), Is.Zero);
                Assert.That(registry.TryGetOwner("source/group", rig.RootBone, out AnomalyContactEffects owner), Is.True);
                Assert.That(owner, Is.SameAs(rig.Effects));
                Invoke(registry, "Forget", token, other);
                Assert.That(registry.TryGetOwner("source/group", rig.RootBone, out owner), Is.True);
                Assert.That(owner, Is.SameAs(rig.Effects));
            }
        }

        [Test]
        public void SourceStopRemovesAllBonesOfTheGroupAndKeepsOtherGroups()
        {
            using (var rig = new Rig())
            {
                AnomalyContactRegistry registry = rig.Target.AddComponent<AnomalyContactRegistry>();
                AnomalyContactEffects other = rig.CreateOwner("OtherZone");
                Invoke(registry, "Register", "source/group", rig.RootBone, rig.Effects);
                Invoke(registry, "Register", "source/group", rig.ChildBone, other);
                Invoke(registry, "Register", "source/other", rig.RootBone, other);
                registry.Stop("source/group");
                Assert.That(registry.TryGetOwner("source/group", rig.RootBone, out _), Is.False);
                Assert.That(registry.TryGetOwner("source/group", rig.ChildBone, out _), Is.False);
                Assert.That(registry.TryGetOwner("source/other", rig.RootBone, out AnomalyContactEffects owner), Is.True);
                Assert.That(owner, Is.SameAs(other));
            }
        }

        [Test]
        public void LeavingAnyZoneStopsTheSourceGroupWithoutReferenceCounting()
        {
            using (var rig = new Rig())
            {
                AnomalyContactRegistry registry = rig.Target.AddComponent<AnomalyContactRegistry>();
                AnomalyContactEffects other = rig.CreateOwner("OtherZone");
                SetField(other, "_sourceEffectKey", "source/group");
                Invoke(registry, "Register", "source/group", rig.RootBone, rig.Effects);
                other.StopTarget(rig.Target.transform);
                Assert.That(registry.TryGetOwner("source/group", rig.RootBone, out _), Is.False);
                Assert.That((int)Invoke(registry, "Register", "source/group", rig.RootBone, other), Is.Not.Zero);
            }
        }

        [Test]
        public void DestroyedOwnersCannotLeaveAStaleTargetRegistration()
        {
            using (var rig = new Rig())
            {
                AnomalyContactRegistry registry = rig.Target.AddComponent<AnomalyContactRegistry>();
                AnomalyContactEffects other = rig.CreateOwner("TemporaryZone");
                Invoke(registry, "Register", "source/group", rig.RootBone, other);
                UnityEngine.Object.DestroyImmediate(other.gameObject);
                Assert.That(registry.TryGetOwner("source/group", rig.RootBone, out _), Is.False);
                Assert.That((int)Invoke(registry, "Register", "source/group", rig.RootBone, rig.Effects), Is.Not.Zero);
            }
        }

        [Test]
        public void TargetDisableClearsRegistrationsAndCanStartAnewLifetime()
        {
            using (var rig = new Rig())
            {
                AnomalyContactRegistry registry = rig.Target.AddComponent<AnomalyContactRegistry>();
                Invoke(registry, "Register", "source/group", rig.RootBone, rig.Effects);
                Invoke(registry, "OnDisable");
                Assert.That(registry.TryGetOwner("source/group", rig.RootBone, out _), Is.False);
                Assert.That((int)Invoke(registry, "Register", "source/group", rig.RootBone, rig.Effects), Is.Not.Zero);
            }
        }

        [Test]
        public void NativeIdleExpiryStopsSourceGroupAgainAfterAMainHitRestartsIt()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Reaction, "_nonlivingIdleStopDelay", 50f);
                SetField(rig.Reaction, "_repeatNonlivingIdleStop", true);
                SetField(rig.Effects, "_sourceEffectKey", "source/group");
                AnomalyVisual visual = (AnomalyVisual)GetField(rig.Reaction, "_visual");
                FieldInfo idle = typeof(AnomalyVisual).GetField("_idleContactEffects", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(idle, Is.Not.Null);
                idle.SetValue(visual, rig.Effects);
                AnomalyContactRegistry registry = rig.Target.AddComponent<AnomalyContactRegistry>();
                object contact = rig.AddContact(51f);
                Invoke(registry, "Register", "source/group", rig.RootBone, rig.Effects);
                Invoke(rig.Reaction, "RemoveExpiredContacts");
                Assert.That(IdleStopped(contact), Is.True);
                Assert.That(registry.TryGetOwner("source/group", rig.RootBone, out _), Is.False);

                AnomalyContactEffects restarted = rig.CreateOwner("MainHitOwner");
                Assert.That((int)Invoke(registry, "Register", "source/group", rig.RootBone, restarted), Is.Not.Zero);
                Invoke(rig.Reaction, "RemoveExpiredContacts");
                Assert.That(registry.TryGetOwner("source/group", rig.RootBone, out _), Is.False);
                Assert.That((bool)Invoke(rig.Reaction, "HasActiveProbe"), Is.True);
            }
        }

        private static Transform ChooseAnchor(Rig rig)
        {
            return (Transform)Invoke(rig.Effects, "ChooseAnchor", rig.Target.transform, rig.Renderer);
        }

        private static bool IdleStopped(object contact)
        {
            return (bool)contact.GetType().GetProperty("IdleStopped").GetValue(contact);
        }

        private static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(target, arguments);
        }
    }
}
