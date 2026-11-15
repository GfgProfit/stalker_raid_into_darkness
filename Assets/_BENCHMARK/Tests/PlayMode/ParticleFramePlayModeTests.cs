using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rvm.Benchmark.Tests
{
    public sealed class ParticleFramePlayModeTests
    {
        private GameObject _root;

        [UnityTest]
        public IEnumerator ActualParticleOrderLifetimeRestartResizeAndAtlasRestorationRemainEquivalent()
        {
            _root = new GameObject("Particle frame equivalence fixture");
            var system = _root.AddComponent<ParticleSystem>();
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.maxParticles = 32;
            main.startSpeed = 0;
            var emission = system.emission;
            emission.enabled = false;
            var custom = system.customData;
            custom.enabled = true;
            custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            custom.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.enabled = false;
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_FrameColumns", 7);
            renderer.SetPropertyBlock(properties);
            var presenter = _root.AddComponent<AnomalyParticleFrames>();
            presenter.Configure(system, 128, 16, new Vector2(0.0625f, 0.125f), 32, true, true, true);
            var particles = new[]
            {
                new ParticleSystem.Particle { startLifetime = 1, remainingLifetime = 1, randomSeed = 5, startSize = 1 },
                new ParticleSystem.Particle { startLifetime = 2, remainingLifetime = 1, randomSeed = 5, startSize = 1 },
                new ParticleSystem.Particle { startLifetime = 3, remainingLifetime = 2.5f, randomSeed = 7, startSize = 1 }
            };
            for (int iteration = 0; iteration < 6; iteration++)
            {
                system.Play(false);
                system.SetParticles(particles, particles.Length);
                Check(presenter, system, 128, 32, true, true);
                system.Simulate(0.067f, false, false, false);
                Check(presenter, system, 128, 32, true, true);
                System.Array.Reverse(particles);
                particles[0].remainingLifetime = particles[0].startLifetime;
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                Check(presenter, system, 128, 32, true, true);
                yield return null;
            }
            main.maxParticles = 64;
            system.Play(false);
            system.SetParticles(particles, particles.Length);
            presenter.Configure(system, 66, 11, new Vector2(0.1f, 0.1f), 24, true, false, true);
            Check(presenter, system, 66, 24, false, true);
            system.Simulate(4, false, false, false);
            Check(presenter, system, 66, 24, false, true);
            Assert.That(system.particleCount, Is.Zero);
            presenter.enabled = false;
            renderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_FrameColumns"), Is.EqualTo(7));
            presenter.enabled = true;
            system.Play(false);
            system.SetParticles(particles, particles.Length);
            Check(presenter, system, 66, 24, false, true);
        }

        private static void Check(AnomalyParticleFrames presenter, ParticleSystem system, int count, float fps, bool start, bool playback)
        {
            typeof(AnomalyParticleFrames).GetMethod("UpdateFrames", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, new object[] { 0.033f });
            var particles = new ParticleSystem.Particle[64];
            int live = system.GetParticles(particles);
            var frames = new List<Vector4>();
            system.GetCustomParticleData(frames, ParticleSystemCustomData.Custom1);
            Assert.That(frames.Count, Is.EqualTo(live));
            for (int i = 0; i < live; i++)
            {
                float reference = ParticleFrameReference.EvaluateFrame(particles[i].startLifetime - particles[i].remainingLifetime, particles[i].randomSeed, count, fps, true, start, playback);
                Assert.That(frames[i], Is.EqualTo(new Vector4(Mathf.Floor(reference), 0, 0, 0)));
            }
        }

        [TearDown]
        public void Cleanup()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }
    }
}
