using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class SpatialBubbleMeshReactionTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly List<GameObject> _objects = new List<GameObject>();

            public SpatialBubbleMeshReaction Reaction { get; }
            public SpatialBubbleTeleport Teleport { get; }
            public MeshRenderer Surface { get; }
            public SphereCollider Volume { get; }
            public Camera Viewer { get; }

            public Rig()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                GameObject root = Create("Bubble");
                Volume = root.AddComponent<SphereCollider>();
                Volume.radius = 2f;
                Volume.isTrigger = true;
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                Teleport = root.AddComponent<SpatialBubbleTeleport>();
                Surface = root.AddComponent<MeshRenderer>();
                Reaction = root.AddComponent<SpatialBubbleMeshReaction>();
                Viewer = Create("Viewer").AddComponent<Camera>();
                Viewer.enabled = false;
                SetField(Teleport, "_activationVolume", Volume);
                SetField(Reaction, "_teleport", Teleport);
                SetField(Reaction, "_surface", Surface);
                SetField(Reaction, "_resolvedViewer", Viewer);
                Invoke(Reaction, "Awake");
                root.SetActive(true);
            }

            public GameObject Create(string name)
            {
                var root = new GameObject(name);
                root.SetActive(false);
                root.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(root, _scene);
                _objects.Add(root);
                return root;
            }

            public Rigidbody CreateBody(string name, bool probe = false, bool kinematic = false)
            {
                GameObject root = Create(name);
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.isKinematic = kinematic;
                if (probe)
                {
                    root.AddComponent<AnomalyProbe>();
                }
                root.SetActive(true);
                return body;
            }

            public Collider AddCollider(Rigidbody body, string name)
            {
                GameObject child = Create(name);
                child.transform.SetParent(body.transform, false);
                Collider collider = child.AddComponent<SphereCollider>();
                child.SetActive(true);
                return collider;
            }

            public void Tick(float delta)
            {
                Invoke(Reaction, "Tick", delta);
            }

            public void Dispose()
            {
                try
                {
                    Invoke(Reaction, "OnDisable");
                }
                finally
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
        }

        [Test]
        public void ProbeImpactCollapsesQuicklyReboundsAndSettlesWithoutMovingTheVolume()
        {
            using (var rig = new Rig())
            {
                Rigidbody probe = rig.CreateBody("Probe", true);
                Collider collider = rig.AddCollider(probe, "Collider");
                Vector3 scale = rig.Reaction.transform.localScale;
                float radius = rig.Volume.radius;
                Invoke(rig.Reaction, "OnTriggerEnter", collider);
                rig.Tick(0.034f);
                float beforePeak = GetField<float>(rig.Reaction, "_impact");
                rig.Tick(0.001f);
                Assert.That(GetField<float>(rig.Reaction, "_impact"), Is.EqualTo(1f).Within(0.0001f));
                Assert.That(Mathf.Abs(GetField<float>(rig.Reaction, "_impact") - beforePeak), Is.LessThan(0.005f));
                rig.Tick(0.46f);
                Assert.That(GetField<float>(rig.Reaction, "_impact"), Is.LessThan(-0.1f));
                rig.Tick(1f);
                Assert.That(GetField<float>(rig.Reaction, "_impact"), Is.Zero.Within(0.0001f));
                Assert.That(GetField<bool>(rig.Reaction, "_impactActive"), Is.False);
                Assert.That(rig.Reaction.transform.localScale, Is.EqualTo(scale));
                Assert.That(rig.Volume.radius, Is.EqualTo(radius));
                Assert.That(probe.linearVelocity, Is.EqualTo(Vector3.zero));
                Assert.That(rig.Teleport.IsActivated, Is.False);
            }
        }

        [Test]
        public void CompoundObjectAndStayCallbacksProduceOneImpulseUntilTheWholeBodyLeaves()
        {
            using (var rig = new Rig())
            {
                Rigidbody body = rig.CreateBody("Object");
                Collider first = rig.AddCollider(body, "First");
                Collider second = rig.AddCollider(body, "Second");
                Invoke(rig.Reaction, "OnTriggerEnter", first);
                rig.Tick(0.2f);
                Invoke(rig.Reaction, "OnTriggerStay", first);
                Invoke(rig.Reaction, "OnTriggerEnter", second);
                Assert.That(GetField<float>(rig.Reaction, "_impactElapsed"), Is.EqualTo(0.2f).Within(0.0001f));
                Invoke(rig.Reaction, "OnTriggerExit", first);
                Invoke(rig.Reaction, "OnTriggerStay", second);
                Assert.That(GetField<float>(rig.Reaction, "_impactElapsed"), Is.EqualTo(0.2f).Within(0.0001f));
                Invoke(rig.Reaction, "OnTriggerExit", second);
                Invoke(rig.Reaction, "OnTriggerStay", first);
                Assert.That(GetField<float>(rig.Reaction, "_impactElapsed"), Is.Zero);
                Assert.That(GetField<Dictionary<Rigidbody, int>>(rig.Reaction, "_bodyContacts").Count, Is.EqualTo(1));
                UnityEngine.Object.DestroyImmediate(body.gameObject);
                rig.Tick(0f);
                Assert.That(GetField<Dictionary<Collider, Rigidbody>>(rig.Reaction, "_contacts"), Is.Empty);
                Assert.That(GetField<Dictionary<Rigidbody, int>>(rig.Reaction, "_bodyContacts"), Is.Empty);
            }
        }

        [Test]
        public void PlayerSelfAndKinematicSceneryDoNotProduceObjectImpulses()
        {
            using (var rig = new Rig())
            {
                Invoke(rig.Reaction, "OnTriggerEnter", rig.Volume);
                Rigidbody scenery = rig.CreateBody("Scenery", false, true);
                Invoke(rig.Reaction, "OnTriggerEnter", rig.AddCollider(scenery, "SceneryCollider"));
                GameObject player = rig.Create("Player");
                player.AddComponent<StalkerPlayerController>();
                player.SetActive(true);
                Rigidbody ownedBody = rig.CreateBody("PlayerOwnedBody");
                ownedBody.transform.SetParent(player.transform, false);
                Invoke(rig.Reaction, "OnTriggerStay", rig.AddCollider(ownedBody, "PlayerCollider"));
                rig.Tick(0.1f);
                Assert.That(GetField<float>(rig.Reaction, "_impact"), Is.Zero);
                Assert.That(GetField<Dictionary<Collider, Rigidbody>>(rig.Reaction, "_contacts"), Is.Empty);
                Rigidbody probe = rig.CreateBody("KinematicProbe", true, true);
                Invoke(rig.Reaction, "OnTriggerEnter", rig.AddCollider(probe, "ProbeCollider"));
                Assert.That(GetField<bool>(rig.Reaction, "_impactActive"), Is.True);
            }
        }

        [Test]
        public void ProximityChargeAndSpringAreIndependentOfFramePartitioning()
        {
            using (var first = new Rig())
            using (var second = new Rig())
            {
                foreach (Rig rig in new[] { first, second })
                {
                    rig.Viewer.transform.position = new Vector3(0f, 0f, 3f);
                    SetField(rig.Teleport, "_activated", true);
                    SetField(rig.Teleport, "_elapsed", 0.45f);
                    Rigidbody probe = rig.CreateBody("Probe", true);
                    Invoke(rig.Reaction, "OnTriggerEnter", rig.AddCollider(probe, "Collider"));
                }
                first.Tick(0.5f);
                for (int index = 0; index < 50; index++)
                {
                    second.Tick(0.01f);
                }
                foreach (string field in new[] { "_proximity", "_charge", "_impact" })
                {
                    Assert.That(GetField<float>(first.Reaction, field), Is.EqualTo(GetField<float>(second.Reaction, field)).Within(0.00001f), field);
                }
                Assert.That(GetField<float>(first.Reaction, "_proximity"), Is.GreaterThan(0.5f));
                Assert.That(GetField<float>(first.Reaction, "_charge"), Is.GreaterThan(0.4f).And.LessThan(0.5f));
            }
        }

        [Test]
        public void DisableAndSurfaceReplacementResetOnlyOwnedVisualProperties()
        {
            using (var rig = new Rig())
            {
                var block = new MaterialPropertyBlock();
                block.SetFloat("_DestinationViewAvailable", 1f);
                block.SetFloat("_CoreRadius", 0.7f);
                rig.Surface.SetPropertyBlock(block);
                rig.Viewer.transform.position = Vector3.zero;
                rig.Tick(0.2f);
                rig.Surface.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_BubbleProximity"), Is.GreaterThan(0f));
                Assert.That(block.GetFloat("_BubbleFlowOffset"), Is.GreaterThan(0f));
                MeshRenderer replacement = rig.Create("Replacement").AddComponent<MeshRenderer>();
                SetField(rig.Reaction, "_surface", replacement);
                rig.Tick(0f);
                rig.Surface.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_BubbleProximity"), Is.Zero);
                Assert.That(block.GetFloat("_BubbleFlowOffset"), Is.Zero);
                Assert.That(block.GetFloat("_DestinationViewAvailable"), Is.EqualTo(1f));
                Assert.That(block.GetFloat("_CoreRadius"), Is.EqualTo(0.7f));
                Invoke(rig.Reaction, "OnDisable");
                replacement.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_BubbleProximity"), Is.Zero);
                Assert.That(block.GetFloat("_BubbleCharge"), Is.Zero);
                Assert.That(block.GetFloat("_BubbleImpact"), Is.Zero);
                Assert.That(block.GetFloat("_BubbleFlowOffset"), Is.Zero);
                Assert.DoesNotThrow(() => Invoke(rig.Reaction, "OnDisable"));
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(target);
        }

        private static void Invoke(SpatialBubbleMeshReaction target, string name, params object[] arguments)
        {
            MethodInfo method = typeof(SpatialBubbleMeshReaction).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(target, arguments);
        }
    }
}
