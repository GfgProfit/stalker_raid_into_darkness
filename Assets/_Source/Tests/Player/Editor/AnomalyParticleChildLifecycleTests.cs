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
    public sealed class AnomalyParticleChildLifecycleTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly List<GameObject> _objects = new List<GameObject>();

            public GameObject Root { get; }
            public ParticleSystem Source { get; }
            public AnomalyParticleChildEmitters Emitters { get; }

            public Rig()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                Root = Create("ChildLifecycle");
                Source = Root.AddComponent<ParticleSystem>();
                var main = Source.main;
                main.playOnAwake = false;
                Emitters = Root.AddComponent<AnomalyParticleChildEmitters>();
            }

            public GameObject Create(string name)
            {
                GameObject root = new GameObject(name);
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, _scene);
                root.hideFlags = HideFlags.HideAndDontSave;
                _objects.Add(root);
                return root;
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
        public void RebindingChildTemplatesDisposesOldPoolsInsteadOfReusingWrongEffects()
        {
            using (var rig = new Rig())
            {
                GameObject pool = rig.Create("OldPool");
                pool.transform.SetParent(rig.Root.transform, false);
                GameObject old = rig.Create("OldTemplateInstance");
                old.transform.SetParent(pool.transform, false);
                ParticleSystem oldSystem = old.AddComponent<ParticleSystem>();
                var stack = new Stack<ParticleSystem>();
                stack.Push(oldSystem);
                ((IList)Field(rig.Emitters, "_pools").GetValue(rig.Emitters)).Add(stack);
                Field(rig.Emitters, "_poolRoot").SetValue(rig.Emitters, pool.transform);
                ParticleSystem replacement = rig.Create("NewTemplate").AddComponent<ParticleSystem>();

                rig.Emitters.Configure(rig.Source, new[] { new AnomalyParticleChildLink(replacement, true) });

                Assert.That(pool == null, Is.True);
                Assert.That(oldSystem == null, Is.True);
                IList pools = (IList)Field(rig.Emitters, "_pools").GetValue(rig.Emitters);
                Assert.That(pools.Count, Is.EqualTo(1));
                Assert.That(((Stack<ParticleSystem>)pools[0]).Count, Is.Zero);
            }
        }

        [Test]
        public void ReusedParentSeedsAreRecognizedByAgeAndCannotCaptureOldFollowers()
        {
            using (var rig = new Rig())
            {
                Type parentType = typeof(AnomalyParticleChildEmitters).GetNestedType("ParentState", BindingFlags.NonPublic);
                object parent = Activator.CreateInstance(parentType, true);
                parentType.GetProperty("Age").SetValue(parent, 5f);
                parentType.GetProperty("StartLifetime").SetValue(parent, 10f);
                ParticleSystem.Particle particle = new ParticleSystem.Particle { startLifetime = 10f, remainingLifetime = 7f };
                MethodInfo isNewBirth = typeof(AnomalyParticleChildEmitters).GetMethod("IsNewBirth", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(isNewBirth.Invoke(null, new[] { (object)particle, parent }), Is.True);
                particle.remainingLifetime = 4f;
                Assert.That(isNewBirth.Invoke(null, new[] { (object)particle, parent }), Is.False);

                Type childType = typeof(AnomalyParticleChildEmitters).GetNestedType("ChildState", BindingFlags.NonPublic);
                object following = Activator.CreateInstance(childType, true);
                childType.GetProperty("Seed").SetValue(following, 42u);
                childType.GetProperty("Following").SetValue(following, true);
                object free = Activator.CreateInstance(childType, true);
                childType.GetProperty("Seed").SetValue(free, 42u);
                childType.GetProperty("Following").SetValue(free, false);
                IList children = (IList)Field(rig.Emitters, "_children").GetValue(rig.Emitters);
                children.Add(following);
                children.Add(free);

                Invoke(rig.Emitters, "DetachChildren", 42u);

                Assert.That(childType.GetProperty("Following").GetValue(following), Is.False);
                Assert.That(childType.GetProperty("Following").GetValue(free), Is.False);
                Assert.That(children.Count, Is.EqualTo(2));
                children.Clear();
            }
        }

        [Test]
        public void ChildrenInheritNativeDynamicVelocityInsteadOfQuantizedFrameDisplacement()
        {
            using (var rig = new Rig())
            {
                AnomalyParticleDynamics dynamics = rig.Root.AddComponent<AnomalyParticleDynamics>();
                Type stateType = typeof(AnomalyParticleDynamics).GetNestedType("ParticleState", BindingFlags.NonPublic);
                Vector3 expected = new Vector3(2f, 3f, -4f);
                object state = Activator.CreateInstance(stateType, new object[] { Vector3.zero, expected, UnityEngine.Random.state });
                IDictionary particles = (IDictionary)Field(dynamics, "_states").GetValue(dynamics);
                particles[42u] = state;
                rig.Emitters.Configure(rig.Source, Array.Empty<AnomalyParticleChildLink>());
                ParticleSystem.Particle particle = new ParticleSystem.Particle { randomSeed = 42u, velocity = Vector3.zero };

                Vector3 velocity = (Vector3)Invoke(rig.Emitters, "ParentVelocity", particle, Vector3.right * 1000f, null);

                Assert.That(velocity, Is.EqualTo(expected));
                Assert.That(dynamics.TryGetParticleVelocity(42u, out Vector3 native), Is.True);
                Assert.That(native, Is.EqualTo(expected));
                stateType.GetProperty("Alive").SetValue(state, false);
                Assert.That(dynamics.TryGetParticleVelocity(42u, out _), Is.False);
            }
        }

        private static FieldInfo Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return field;
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(target, arguments);
        }
    }
}
