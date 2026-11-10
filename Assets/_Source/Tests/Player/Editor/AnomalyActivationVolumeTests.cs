using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyActivationVolumeTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly List<GameObject> _objects = new List<GameObject>();

            public GameObject Root { get; }
            public SphereCollider Proxy { get; }
            public XRayAnomalyActivationVolume Volume { get; }

            public Rig()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                Root = Create("AnomalyVolume");
                Proxy = Root.AddComponent<SphereCollider>();
                Proxy.radius = 10f;
                Proxy.isTrigger = true;
                Volume = Root.AddComponent<XRayAnomalyActivationVolume>();
            }

            public GameObject Create(string name)
            {
                GameObject root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, _scene);
                root.hideFlags = HideFlags.HideAndDontSave;
                _objects.Add(root);
                return root;
            }

            public SphereCollider Sphere(Vector3 center, float radius)
            {
                GameObject root = Create("SourceSphere");
                root.transform.SetParent(Root.transform, false);
                root.transform.localPosition = center;
                SphereCollider sphere = root.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = radius;
                return sphere;
            }

            public BoxCollider Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                GameObject root = Create("SourceBox");
                root.transform.SetParent(Root.transform, false);
                root.transform.localPosition = center;
                root.transform.localRotation = rotation;
                BoxCollider box = root.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = size;
                return box;
            }

            public SphereCollider Candidate(Vector3 position, float radius)
            {
                GameObject root = Create("Candidate");
                root.transform.position = position;
                SphereCollider sphere = root.AddComponent<SphereCollider>();
                sphere.radius = radius;
                return sphere;
            }

            public StalkerPlayerController Player(Vector3 position)
            {
                GameObject root = Create("Player");
                root.transform.position = position;
                return root.AddComponent<StalkerPlayerController>();
            }

            public AnomalyPlayerReaction Reaction()
            {
                ContinuousAnomalyVisual visual = Root.AddComponent<ContinuousAnomalyVisual>();
                visual.ShowIdle();
                AnomalyPlayerReaction reaction = Root.AddComponent<AnomalyPlayerReaction>();
                SetField(reaction, "_visual", visual);
                Invoke(reaction, "OnDisable");
                Invoke(reaction, "Awake");
                Invoke(reaction, "OnEnable");
                return reaction;
            }

            public void Dispose()
            {
                foreach (GameObject root in _objects)
                {
                    if (root != null)
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                    }
                }
                EditorSceneManager.ClosePreviewScene(_scene);
            }
        }

        [Test]
        public void TwoSourceSpheresKeepTheirGapEvenInsideTheProximityProxy()
        {
            using (var rig = new Rig())
            {
                SphereCollider left = rig.Sphere(Vector3.left * 2f, 0.5f);
                SphereCollider right = rig.Sphere(Vector3.right * 2f, 0.5f);
                rig.Volume.Configure(rig.Proxy, new Collider[] { left, right });
                Assert.That(rig.Volume.Contains(Vector3.left * 2f), Is.True);
                Assert.That(rig.Volume.Contains(Vector3.right * 2f), Is.True);
                Assert.That(rig.Volume.Contains(Vector3.zero), Is.False);
                Assert.That(rig.Volume.Overlaps(rig.Candidate(Vector3.zero, 0.1f)), Is.False);
                Assert.That(rig.Volume.Overlaps(rig.Proxy), Is.False);
                Assert.That(rig.Volume.ActualShapes.Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void SourceSphereUsesItsTransformedCenterAndMaximumAbsoluteScale()
        {
            using (var rig = new Rig())
            {
                rig.Root.transform.position = new Vector3(7f, 3f, -4f);
                SphereCollider sphere = rig.Sphere(new Vector3(2f, 0f, 1f), 1f);
                sphere.center = new Vector3(0.2f, 0.4f, -0.1f);
                sphere.transform.localScale = new Vector3(-2f, 0.5f, 3f);
                rig.Volume.Configure(rig.Proxy, new Collider[] { sphere });
                Vector3 center = sphere.transform.TransformPoint(sphere.center);
                Assert.That(rig.Volume.Contains(center + Vector3.right * 3f), Is.True);
                Assert.That(rig.Volume.Contains(center + Vector3.right * 3.001f), Is.False);
            }
        }

        [Test]
        public void SourceBoxPreservesRotationScaleAndLocalCenter()
        {
            using (var rig = new Rig())
            {
                rig.Root.transform.position = new Vector3(4f, 5f, -3f);
                BoxCollider box = rig.Box(Vector3.right * 2f, new Vector3(2f, 4f, 2f), Quaternion.Euler(0f, 45f, 0f));
                box.center = new Vector3(0.5f, 0.25f, -0.5f);
                box.transform.localScale = new Vector3(2f, 1f, 3f);
                rig.Volume.Configure(rig.Proxy, new Collider[] { box });
                Assert.That(rig.Volume.Contains(box.transform.TransformPoint(box.center + Vector3.right)), Is.True);
                Assert.That(rig.Volume.Contains(box.transform.TransformPoint(box.center + Vector3.right * 1.001f)), Is.False);
            }
        }

        [Test]
        public void SphereContactsIncludeTheExactBoundaryWithoutAnInflatedActivationRadius()
        {
            using (var rig = new Rig())
            {
                rig.Volume.Configure(rig.Proxy, new Collider[] { rig.Sphere(Vector3.zero, 1f) });
                SphereCollider candidate = rig.Candidate(Vector3.right * 1.5f, 0.5f);
                Assert.That(rig.Volume.Overlaps(candidate), Is.True);
                candidate.transform.position = Vector3.right * 1.501f;
                Assert.That(rig.Volume.Overlaps(candidate), Is.False);
            }
        }

        [Test]
        public void CapsuleAgainstBoxRejectsTheExpandedBoxCornerFalsePositive()
        {
            using (var rig = new Rig())
            {
                rig.Volume.Configure(rig.Proxy, new Collider[] { rig.Box(Vector3.zero, Vector3.one * 2f, Quaternion.identity) });
                CapsuleCollider candidate = rig.Create("Capsule").AddComponent<CapsuleCollider>();
                candidate.height = 2f;
                candidate.radius = 0.5f;
                candidate.transform.position = new Vector3(1.4f, 0f, 1.4f);
                Assert.That(rig.Volume.Overlaps(candidate), Is.False);
                candidate.transform.position = new Vector3(1.35f, 0f, 1.35f);
                Assert.That(rig.Volume.Overlaps(candidate), Is.True);
                candidate.transform.position = Vector3.right * 1.5f;
                Assert.That(rig.Volume.Overlaps(candidate), Is.True);
            }
        }

        [Test]
        public void RotatedBoxesUseTheirRealAxesInsteadOfTheirWorldBounds()
        {
            using (var rig = new Rig())
            {
                Quaternion rotation = Quaternion.Euler(0f, 45f, 0f);
                BoxCollider source = rig.Box(Vector3.zero, new Vector3(4f, 0.5f, 0.5f), rotation);
                rig.Volume.Configure(rig.Proxy, new Collider[] { source });
                BoxCollider candidate = rig.Create("RotatedCandidate").AddComponent<BoxCollider>();
                candidate.size = source.size;
                candidate.transform.rotation = rotation;
                candidate.transform.position = rotation * Vector3.forward * 0.6f;
                Assert.That(rig.Volume.Overlaps(candidate), Is.False);
                candidate.transform.position = rotation * Vector3.forward * 0.5f;
                Assert.That(rig.Volume.Overlaps(candidate), Is.True);
            }
        }

        [Test]
        public void CharacterCapsuleUsesItsAxisAndRadiusAgainstTheSourceSphere()
        {
            using (var rig = new Rig())
            {
                rig.Volume.Configure(rig.Proxy, new Collider[] { rig.Sphere(Vector3.zero, 1f) });
                CharacterController body = rig.Create("Character").AddComponent<CharacterController>();
                body.radius = 0.5f;
                body.height = 3f;
                body.center = Vector3.zero;
                body.transform.position = Vector3.right * 1.5f;
                Assert.That(rig.Volume.Overlaps(body), Is.True);
                body.transform.position = Vector3.right * 1.501f;
                Assert.That(rig.Volume.Overlaps(body), Is.False);
            }
        }

        [Test]
        public void DisabledSourceShapesAndDisabledCandidatesCannotActivateTheUnion()
        {
            using (var rig = new Rig())
            {
                SphereCollider shape = rig.Sphere(Vector3.zero, 1f);
                rig.Volume.Configure(rig.Proxy, new Collider[] { shape });
                SphereCollider candidate = rig.Candidate(Vector3.zero, 0.1f);
                Assert.That(rig.Volume.Overlaps(candidate), Is.True);
                shape.enabled = false;
                Assert.That(rig.Volume.Overlaps(candidate), Is.False);
                Assert.That(rig.Volume.Contains(Vector3.zero), Is.False);
                shape.enabled = true;
                candidate.enabled = false;
                Assert.That(rig.Volume.Overlaps(candidate), Is.False);
            }
        }

        [Test]
        public void ReactionKeepsOneContactWhenExitingOneShapeWhileInsideAnother()
        {
            using (var rig = new Rig())
            {
                rig.Volume.Configure(rig.Proxy, new Collider[] { rig.Sphere(Vector3.left * 0.5f, 1f), rig.Sphere(Vector3.right * 0.5f, 1f) });
                AnomalyPlayerReaction reaction = rig.Reaction();
                AnomalyAudio audio = rig.Root.AddComponent<AnomalyAudio>();
                SetField(rig.Root.GetComponent<ContinuousAnomalyVisual>(), "_audio", audio);
                rig.Root.GetComponent<ContinuousAnomalyVisual>().ShowIdle();
                StalkerPlayerController player = rig.Player(Vector3.left * 0.5f);
                SphereCollider candidate = player.gameObject.AddComponent<SphereCollider>();
                candidate.radius = 0.1f;
                Invoke(reaction, "RegisterContact", candidate);
                Vector3 enteredAt = (Vector3)GetField(audio, "_entrancePosition");
                player.transform.position = Vector3.right * 0.75f;
                Invoke(reaction, "OnTriggerExit", candidate);
                Invoke(reaction, "RemoveExpiredContacts");
                Invoke(reaction, "RegisterContact", candidate);
                Assert.That(rig.Volume.Overlaps(candidate), Is.True);
                Assert.That(reaction.IsTouching(player), Is.True);
                Assert.That(((IDictionary)GetField(reaction, "_contacts")).Count, Is.EqualTo(1));
                Assert.That(GetField(audio, "_entrancePosition"), Is.EqualTo(enteredAt));
                player.transform.position = Vector3.right * 3f;
                Invoke(reaction, "OnTriggerExit", candidate);
                Assert.That(reaction.IsTouching(player), Is.False);
            }
        }

        [Test]
        public void ReactionExpiresProbeContactsAfterLeavingTheRealUnionWithoutAnExitCallback()
        {
            using (var rig = new Rig())
            {
                rig.Volume.Configure(rig.Proxy, new Collider[] { rig.Sphere(Vector3.zero, 1f) });
                AnomalyPlayerReaction reaction = rig.Reaction();
                SphereCollider candidate = rig.Candidate(Vector3.zero, 0.1f);
                candidate.gameObject.AddComponent<AnomalyProbe>();
                SetField(reaction, "_smallObjectIgnoreDelay", -1f);
                Invoke(reaction, "RegisterContact", candidate);
                Assert.That(Invoke(reaction, "HasActiveProbe"), Is.True);
                candidate.transform.position = Vector3.right * 3f;
                Assert.That(Invoke(reaction, "HasActiveProbe"), Is.False);
                Invoke(reaction, "RemoveExpiredContacts");
                Assert.That(((IDictionary)GetField(reaction, "_probeContacts")).Count, Is.Zero);
            }
        }

        [Test]
        public void TeleportUsesRealShapesInsteadOfTheProxyGap()
        {
            using (var rig = new Rig())
            {
                rig.Volume.Configure(rig.Proxy, new Collider[] { rig.Sphere(Vector3.left * 2f, 0.5f), rig.Sphere(Vector3.right * 2f, 0.5f) });
                SpatialBubbleTeleport teleport = rig.Root.AddComponent<SpatialBubbleTeleport>();
                Invoke(teleport, "Awake");
                CharacterController body = rig.Create("Character").AddComponent<CharacterController>();
                Assert.That(Invoke(teleport, "OverlapsPlayer", body), Is.False);
                body.transform.position = Vector3.left * 2f;
                Assert.That(Invoke(teleport, "OverlapsPlayer", body), Is.True);
            }
        }

        [Test]
        public void SourceScheduleOffCancelsPendingTeleportWithoutDisablingTheGameObject()
        {
            using (var rig = new Rig())
            {
                rig.Volume.Configure(rig.Proxy, new Collider[] { rig.Sphere(Vector3.zero, 1f) });
                AnomalyPlayerReaction reaction = rig.Reaction();
                SpatialBubbleTeleport teleport = rig.Root.AddComponent<SpatialBubbleTeleport>();
                Invoke(teleport, "Awake");
                SetField(teleport, "_activated", true);
                SetField(teleport, "_elapsed", 0.4f);
                reaction.SetSourceActive(false);
                Invoke(teleport, "Tick", 0.1f);
                Assert.That(teleport.IsActivated, Is.False);
                Assert.That(reaction.IsInfluencing, Is.False);
                Assert.That(rig.Root.activeInHierarchy, Is.True);
                reaction.SetSourceActive(true);
                Assert.That(reaction.IsInfluencing, Is.True);
            }
        }

        [Test]
        public void ReactionSourceGateBlocksContactsUntilTheNativeScheduleReactivates()
        {
            using (var rig = new Rig())
            {
                rig.Volume.Configure(rig.Proxy, new Collider[] { rig.Sphere(Vector3.zero, 1f) });
                AnomalyPlayerReaction reaction = rig.Reaction();
                SphereCollider candidate = rig.Candidate(Vector3.zero, 0.1f);
                candidate.gameObject.AddComponent<AnomalyProbe>();
                reaction.SetSourceActive(false);
                Invoke(reaction, "RegisterContact", candidate);
                Assert.That(((IDictionary)GetField(reaction, "_probeContacts")).Count, Is.Zero);
                reaction.SetSourceActive(true);
                Invoke(reaction, "RegisterContact", candidate);
                Assert.That(((IDictionary)GetField(reaction, "_probeContacts")).Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void SourceOffRemovesOwnedIdleContactAndProximityPostProcess()
        {
            StalkerMovementProfile movement = ScriptableObject.CreateInstance<StalkerMovementProfile>();
            AnomalyInfluenceProfile influence = ScriptableObject.CreateInstance<AnomalyInfluenceProfile>();
            AnomalyPostProcessAsset postProcess = ScriptableObject.CreateInstance<AnomalyPostProcessAsset>();
            try
            {
                using (var rig = new Rig())
                {
                    rig.Volume.Configure(rig.Proxy, new Collider[] { rig.Sphere(Vector3.zero, 1f) });
                    AnomalyPlayerReaction reaction = rig.Reaction();
                    SetField(influence, "_kind", AnomalyInfluenceKind.None);
                    SetField(influence, "_inherentPostProcess", postProcess);
                    SetField(reaction, "_influenceProfile", influence);
                    AnomalyContactEffects idle = rig.Root.AddComponent<AnomalyContactEffects>();
                    SetField(idle, "_sourceEffectKey", "source/idle");
                    SetField(rig.Root.GetComponent<ContinuousAnomalyVisual>(), "_idleContactEffects", idle);
                    StalkerPlayerController player = rig.Player(Vector3.zero);
                    StalkerMovementMotor motor = player.GetComponent<StalkerMovementMotor>();
                    motor.Initialize(movement);
                    SetField(player, "_motor", motor);
                    SetField(player, "_profile", movement);
                    SphereCollider candidate = player.gameObject.AddComponent<SphereCollider>();
                    candidate.radius = 0.1f;
                    Invoke(reaction, "RegisterContact", candidate);
                    AnomalyContactRegistry registry = player.gameObject.AddComponent<AnomalyContactRegistry>();
                    Assert.That(Invoke(registry, "Register", "source/idle", player.transform, idle), Is.Not.EqualTo(0));
                    StalkerAnomalyScreenEffects screen = player.gameObject.AddComponent<StalkerAnomalyScreenEffects>();
                    StalkerAnomalyResponse response = player.gameObject.AddComponent<StalkerAnomalyResponse>();
                    SetField(response, "_player", player);
                    SetField(response, "_screen", screen);
                    AnomalyProximityZone proximity = rig.Create("Proximity").AddComponent<AnomalyProximityZone>();
                    proximity.GetComponent<Rigidbody>().isKinematic = true;
                    SetField(proximity, "_source", reaction);
                    SetField(proximity, "_sourceVolume", rig.Proxy);
                    screen.Refresh(proximity.GetEntityId().GetHashCode(), postProcess, 1f, 100f);
                    response.RefreshZone(proximity);
                    Assert.That(proximity.IsAvailable, Is.True);
                    Assert.That(screen.ActiveLayerCount, Is.EqualTo(1));

                    reaction.SetSourceActive(false);
                    Invoke(response, "LateUpdate");

                    Assert.That(registry.TryGetOwner("source/idle", player.transform, out _), Is.False);
                    Assert.That(proximity.IsAvailable, Is.False);
                    Assert.That(screen.ActiveLayerCount, Is.Zero);
                    Assert.That(((IDictionary)GetField(reaction, "_contacts")).Count, Is.Zero);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(postProcess);
                UnityEngine.Object.DestroyImmediate(influence);
                UnityEngine.Object.DestroyImmediate(movement);
            }
        }

        [Test]
        public void ProxyCannotAccidentallyBeConfiguredAsAnActivationShape()
        {
            using (var rig = new Rig())
            {
                Assert.Throws<ArgumentException>(() => rig.Volume.Configure(rig.Proxy, new Collider[] { rig.Proxy }));
            }
        }

        private static object GetField(object target, string name)
        {
            return Field(target.GetType(), name).GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            Field(target.GetType(), name).SetValue(target, value);
        }

        private static FieldInfo Field(Type type, string name)
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
