using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyParticleFramesTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly GameObject _root;

            public ParticleSystem System { get; }
            public ParticleSystemRenderer Renderer { get; }
            public AnomalyParticleFrames Presenter { get; }

            public Rig(int maximum = 16)
            {
                _scene = EditorSceneManager.NewPreviewScene();
                _root = new GameObject(nameof(AnomalyParticleFramesTests));
                SceneManager.MoveGameObjectToScene(_root, _scene);
                _root.hideFlags = HideFlags.HideAndDontSave;
                System = _root.AddComponent<ParticleSystem>();
                System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = System.main;
                main.playOnAwake = false;
                main.loop = true;
                main.duration = 10f;
                main.startSpeed = 0f;
                main.maxParticles = maximum;
                System.useAutoRandomSeed = false;
                System.randomSeed = 27u;
                ParticleSystem.EmissionModule emission = System.emission;
                emission.enabled = false;
                ParticleSystem.CustomDataModule custom = System.customData;
                custom.enabled = true;
                custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                custom.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
                Renderer = System.GetComponent<ParticleSystemRenderer>();
                Renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1X });
                System.Play(false);
                System.Simulate(0f, false, true, false);
                System.Play(false);
                Presenter = _root.AddComponent<AnomalyParticleFrames>();
            }

            public void SetParticles(int count, float age, float lifetime = 10f)
            {
                var particles = new ParticleSystem.Particle[count];
                for (int index = 0; index < count; index++)
                {
                    particles[index] = new ParticleSystem.Particle { startLifetime = lifetime, remainingLifetime = lifetime - age, randomSeed = (uint)index + 1u, startSize = 1f, startColor = Color.white };
                }
                System.SetParticles(particles, particles.Length);
                Assert.That(System.particleCount, Is.EqualTo(count), "The fixture must contain the particles being presented.");
                Assert.That(System.GetParticles(particles), Is.EqualTo(count), "The native particle buffer must be readable before configuring the presenter.");
            }

            public List<Vector4> Frames()
            {
                var frames = new List<Vector4>();
                System.GetCustomParticleData(frames, ParticleSystemCustomData.Custom1);
                return frames;
            }

            public void Disable()
            {
                Presenter.enabled = false;
                Invoke(Presenter, "OnDisable");
            }

            public void Enable()
            {
                Presenter.enabled = true;
                Invoke(Presenter, "OnEnable");
            }

            public void Dispose()
            {
                Object.DestroyImmediate(_root);
                EditorSceneManager.ClosePreviewScene(_scene);
            }
        }

        [Test]
        public void RandomFramesStayWithinTheSourceFrameCountAndCoverTheAtlas()
        {
            using (var rig = new Rig(4096))
            {
                rig.SetParticles(4096, 0f);
                rig.Presenter.Configure(rig.System, 64, 8, new Vector2(0.125f, 0.125f), 24f, false, true, false);
                List<Vector4> frames = rig.Frames();
                Assert.That(frames.Count, Is.EqualTo(4096));
                var counts = new int[64];
                foreach (Vector4 frame in frames)
                {
                    Assert.That(frame.x, Is.InRange(0f, 63f));
                    Assert.That(frame.x, Is.EqualTo(Mathf.Floor(frame.x)));
                    counts[(int)frame.x]++;
                }
                foreach (int count in counts)
                {
                    Assert.That(count, Is.InRange(40, 90));
                }
            }
        }

        [Test]
        public void StaticFramesWithoutRandomStartRemainZeroAtEveryAge()
        {
            using (var rig = new Rig())
            {
                rig.SetParticles(16, 3f);
                rig.Presenter.Configure(rig.System, 5, 8, new Vector2(0.125f, 0.125f), 64f, false, false, true);
                List<Vector4> frames = rig.Frames();
                Assert.That(frames.Count, Is.EqualTo(16));
                foreach (Vector4 frame in frames)
                {
                    Assert.That(frame.x, Is.Zero);
                }
            }
        }

        [Test]
        public void AnimationUsesSourceFpsAndAgeInsteadOfNormalizedLifetime()
        {
            using (var rig = new Rig())
            {
                rig.SetParticles(1, 0.2f, 1f);
                rig.Presenter.Configure(rig.System, 64, 8, new Vector2(0.125f, 0.125f), 24f, true, false, false);
                Assert.That(rig.Frames()[0].x, Is.EqualTo(5f));
                rig.SetParticles(1, 0.2f, 10f);
                rig.Presenter.Configure(rig.System, 64, 8, new Vector2(0.125f, 0.125f), 24f, true, false, false);
                Assert.That(rig.Frames()[0].x, Is.EqualTo(5f));
                Assert.That(SpatialBubbleParticleFrames.AdvanceFrame(0f, 45f, 64, false), Is.EqualTo(378f / 255f));
            }
        }

        [Test]
        public void RandomPlaybackKeepsEachParticleDirectionStable()
        {
            using (var rig = new Rig(1024))
            {
                rig.SetParticles(1024, 0f);
                rig.Presenter.Configure(rig.System, 64, 8, new Vector2(0.125f, 0.125f), 6f, true, false, true);
                List<Vector4> first = rig.Frames();
                Assert.That(first.Count, Is.EqualTo(1024));
                rig.SetParticles(1024, 0.198f);
                rig.Presenter.Configure(rig.System, 64, 8, new Vector2(0.125f, 0.125f), 6f, true, false, true);
                List<Vector4> later = rig.Frames();
                Assert.That(later.Count, Is.EqualTo(1024));
                int forward = 0;
                int backward = 0;
                for (int index = 0; index < first.Count; index++)
                {
                    if (first[index].x == 0f)
                    {
                        forward++;
                        Assert.That(later[index].x, Is.GreaterThan(first[index].x));
                    }
                    else
                    {
                        backward++;
                        Assert.That(later[index].x, Is.LessThan(first[index].x));
                    }
                }
                Assert.That(forward, Is.GreaterThan(400));
                Assert.That(backward, Is.GreaterThan(400));
            }
        }

        [Test]
        public void InfiniteParticlesAdvanceWithoutNaNAndPausePreservesTheirFrame()
        {
            using (var rig = new Rig())
            {
                rig.SetParticles(1, 0f, float.PositiveInfinity);
                rig.Presenter.Configure(rig.System, 64, 8, new Vector2(0.125f, 0.125f), 24f, true, false, false);
                Assert.That(rig.Frames().Count, Is.EqualTo(1));
                Assert.That(rig.Frames()[0].x, Is.Zero);
                Tick(rig.Presenter, 0.033f);
                Assert.That(rig.Frames()[0].x, Is.EqualTo(1f));
                rig.System.Pause(false);
                Tick(rig.Presenter, 10f);
                Assert.That(rig.Frames()[0].x, Is.EqualTo(1f));
                rig.System.Play(false);
                Tick(rig.Presenter, 0.033f);
                Assert.That(rig.Frames()[0].x, Is.EqualTo(2f));
                Assert.That(float.IsNaN(rig.Frames()[0].x), Is.False);
                rig.System.Clear(false);
                Tick(rig.Presenter, 0f);
                Assert.That(rig.Frames().Count, Is.Zero);
                Assert.That(PersistentCount(rig.Presenter), Is.Zero);
                rig.SetParticles(1, 0f, float.PositiveInfinity);
                Tick(rig.Presenter, 0f);
                Assert.That(rig.Frames()[0].x, Is.Zero);
                rig.Disable();
                Assert.That(PersistentCount(rig.Presenter), Is.Zero);
            }
        }

        [Test]
        public void InfiniteParticleFramesFollowTheirSeedsWhenNativeStorageReorders()
        {
            using (var rig = new Rig())
            {
                rig.SetParticles(2, 0f, float.PositiveInfinity);
                rig.Presenter.Configure(rig.System, 64, 8, new Vector2(0.125f, 0.125f), 24f, true, true, true);
                Tick(rig.Presenter, 0.099f);
                List<Vector4> first = rig.Frames();
                Assert.That(first.Count, Is.EqualTo(2));
                var particles = new ParticleSystem.Particle[2];
                Assert.That(rig.System.GetParticles(particles), Is.EqualTo(2));
                Array.Reverse(particles);
                rig.System.SetParticles(particles, particles.Length);
                Tick(rig.Presenter, 0f);
                List<Vector4> reordered = rig.Frames();
                Assert.That(reordered[0].x, Is.EqualTo(first[1].x));
                Assert.That(reordered[1].x, Is.EqualTo(first[0].x));
                Assert.That(PersistentCount(rig.Presenter), Is.EqualTo(2));
            }
        }

        [Test]
        public void InfiniteParticleClockUsesSimulationSpeedAndDropsCatchupBeyondThreeSteps()
        {
            using (var rig = new Rig())
            {
                rig.SetParticles(1, 0f, float.PositiveInfinity);
                rig.Presenter.Configure(rig.System, 64, 8, new Vector2(0.125f, 0.125f), 24f, true, false, false);
                ParticleSystem.MainModule main = rig.System.main;
                main.simulationSpeed = 0.5f;
                Tick(rig.Presenter, 0.033f);
                Assert.That(rig.Frames()[0].x, Is.Zero);
                Tick(rig.Presenter, 0.033f);
                Assert.That(rig.Frames()[0].x, Is.EqualTo(1f));
                main.simulationSpeed = 1f;
                Tick(rig.Presenter, 0.33f);
                Assert.That(rig.Frames()[0].x, Is.EqualTo(3f));
            }
        }

        [Test]
        public void AtlasOverridesPreserveTheSharedMaterialAndRestoreTheOriginalBlock()
        {
            using (var rig = new Rig())
            {
                Shader shader = Shader.Find("RaidIntoDarkness/XRayParticle");
                Assert.That(shader, Is.Not.Null);
                var material = new Material(shader);
                try
                {
                    material.SetVector("_FrameSize", new Vector4(0.75f, 0.5f, 0f, 0f));
                    material.SetFloat("_FrameColumns", 4f);
                    rig.Renderer.sharedMaterial = material;
                    rig.Disable();
                    var original = new MaterialPropertyBlock();
                    original.SetVector("_FrameSize", new Vector4(0.5f, 0.25f, 0f, 0f));
                    original.SetFloat("_FrameColumns", 3f);
                    original.SetFloat("_AnomalyFrameSentinel", 23f);
                    rig.Renderer.SetPropertyBlock(original);
                    rig.Enable();
                    rig.Presenter.Configure(rig.System, 128, 8, new Vector2(0.125f, 0.0625f), 24f, true, true, false);
                    var applied = new MaterialPropertyBlock();
                    rig.Renderer.GetPropertyBlock(applied);
                    Assert.That(applied.GetVector("_FrameSize"), Is.EqualTo(new Vector4(0.125f, 0.0625f, 0f, 0f)));
                    Assert.That(applied.GetFloat("_FrameColumns"), Is.EqualTo(8f));
                    Assert.That(applied.GetFloat("_AnomalyFrameSentinel"), Is.EqualTo(23f));
                    Assert.That(rig.Renderer.sharedMaterial, Is.SameAs(material));
                    Assert.That(material.GetVector("_FrameSize"), Is.EqualTo(new Vector4(0.75f, 0.5f, 0f, 0f)));
                    Assert.That(material.GetFloat("_FrameColumns"), Is.EqualTo(4f));
                    rig.Presenter.Configure(rig.System, 16, 4, new Vector2(0.25f, 0.25f), 0f, false, true, false);
                    rig.Disable();
                    rig.Renderer.GetPropertyBlock(applied);
                    Assert.That(applied.GetVector("_FrameSize"), Is.EqualTo(original.GetVector("_FrameSize")));
                    Assert.That(applied.GetFloat("_FrameColumns"), Is.EqualTo(3f));
                    Assert.That(applied.GetFloat("_AnomalyFrameSentinel"), Is.EqualTo(23f));
                }
                finally
                {
                    rig.Renderer.sharedMaterial = null;
                    Object.DestroyImmediate(material);
                }
            }
        }

        [Test]
        public void DisableRestoresAnInitiallyEmptyPropertyBlock()
        {
            using (var rig = new Rig())
            {
                rig.Disable();
                rig.Renderer.SetPropertyBlock(null);
                rig.Enable();
                rig.Presenter.Configure(rig.System, 16, 4, new Vector2(0.25f, 0.25f), 0f, false, true, false);
                Assert.That(rig.Renderer.HasPropertyBlock(), Is.True);
                rig.Disable();
                Assert.That(rig.Renderer.HasPropertyBlock(), Is.False);
            }
        }

        [Test]
        public void ChangingParticleSystemsRestoresThePreviousRenderer()
        {
            using (var rig = new Rig())
            {
                rig.Disable();
                var original = new MaterialPropertyBlock();
                original.SetFloat("_FrameColumns", 11f);
                rig.Renderer.SetPropertyBlock(original);
                rig.Enable();
                rig.Presenter.Configure(rig.System, 16, 4, new Vector2(0.25f, 0.25f), 0f, false, true, false);
                var other = new GameObject("Other Particle System");
                SceneManager.MoveGameObjectToScene(other, rig.System.gameObject.scene);
                try
                {
                    ParticleSystem system = other.AddComponent<ParticleSystem>();
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ParticleSystemRenderer renderer = other.GetComponent<ParticleSystemRenderer>();
                    var secondOriginal = new MaterialPropertyBlock();
                    secondOriginal.SetFloat("_FrameColumns", 13f);
                    renderer.SetPropertyBlock(secondOriginal);
                    rig.Presenter.Configure(system, 64, 8, new Vector2(0.125f, 0.125f), 24f, true, true, false);
                    var block = new MaterialPropertyBlock();
                    rig.Renderer.GetPropertyBlock(block);
                    Assert.That(block.GetFloat("_FrameColumns"), Is.EqualTo(11f));
                    renderer.GetPropertyBlock(block);
                    Assert.That(block.GetFloat("_FrameColumns"), Is.EqualTo(8f));
                    rig.Disable();
                    renderer.GetPropertyBlock(block);
                    Assert.That(block.GetFloat("_FrameColumns"), Is.EqualTo(13f));
                }
                finally
                {
                    Object.DestroyImmediate(other);
                }
            }
        }

        [Test]
        public void InvalidAtlasSettingsAreRejected()
        {
            using (var rig = new Rig())
            {
                Assert.Throws<ArgumentNullException>(() => rig.Presenter.Configure(null, 1, 1, Vector2.one, 0f, false, false, false));
                Assert.Throws<ArgumentOutOfRangeException>(() => rig.Presenter.Configure(rig.System, 0, 1, Vector2.one, 0f, false, false, false));
                Assert.Throws<ArgumentOutOfRangeException>(() => rig.Presenter.Configure(rig.System, 1, 0, Vector2.one, 0f, false, false, false));
                Assert.Throws<ArgumentOutOfRangeException>(() => rig.Presenter.Configure(rig.System, 1, 1, Vector2.zero, 0f, false, false, false));
                Assert.Throws<ArgumentOutOfRangeException>(() => rig.Presenter.Configure(rig.System, 1, 1, new Vector2(float.NaN, 1f), 0f, false, false, false));
                Assert.Throws<ArgumentOutOfRangeException>(() => rig.Presenter.Configure(rig.System, 1, 1, Vector2.one, float.PositiveInfinity, false, false, false));
                Assert.Throws<ArgumentOutOfRangeException>(() => rig.Presenter.Configure(rig.System, 1, 1, Vector2.one, -1f, false, false, false));
            }
        }

        private static void Invoke(AnomalyParticleFrames presenter, string name)
        {
            typeof(AnomalyParticleFrames).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, null);
        }

        private static void Tick(AnomalyParticleFrames presenter, float deltaTime)
        {
            typeof(AnomalyParticleFrames).GetMethod("UpdateFrames", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, new object[] { deltaTime });
        }

        private static int PersistentCount(AnomalyParticleFrames presenter)
        {
            return ((ICollection)typeof(AnomalyParticleFrames).GetField("_persistentFrames", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presenter)).Count;
        }
    }
}
