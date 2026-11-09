using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Player.Tests.Editor
{
    public sealed class SpatialBubblePersistentParticleTests
    {
        private GameObject _root;
        private SpatialBubblePersistentParticle _effect;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("SpatialBubblePersistentParticleTests") { hideFlags = HideFlags.HideAndDontSave };
            _root.SetActive(false);
            _effect = _root.AddComponent<SpatialBubblePersistentParticle>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void DisabledSourceKillOldDoesNotReplaceThePortalAfterThreeSeconds()
        {
            Set("_maximum", 2);
            Set("_sourceRate", 9000f);
            Set("_sourceRotation", 0.5f);
            Set("_targetRotation", 2f);
            Set("_rotationScale", 0.03f);
            Step(1000, 0f);
            Assert.That(Get<int>("_count"), Is.EqualTo(2));
            Assert.That(Get<float[]>("_ages")[0], Is.GreaterThan(32f));
            Assert.That(Get<float[]>("_rotations")[0], Is.GreaterThan(1f));
            ParticleSystem.Particle[] particles = Get<ParticleSystem.Particle[]>("_particles");
            Assert.That(float.IsPositiveInfinity(particles[0].startLifetime), Is.True);
            Assert.That(float.IsPositiveInfinity(particles[0].remainingLifetime), Is.True);
            Assert.That(particles[0].randomSeed, Is.EqualTo(1u));
            Assert.That(particles[1].randomSeed, Is.EqualTo(2u));
        }

        [Test]
        public void PersistentSourceColorStopsAtItsOriginalAgeWindow()
        {
            Set("_sourceRate", 9000f);
            Set("_sourceColor", new Color(0.2f, 0.3f, 0.4f, 0.1f));
            Set("_targetColor", new Color(0.8f, 0.5f, 0.2f, 0.3f));
            Set("_colorScale", 1f);
            Step(1, 0f);
            Assert.That(Get<Color[]>("_colors")[0], Is.EqualTo((Color)new Color32(51, 76, 102, 25)));
            Step(50, 0f);
            Color frozen = Get<Color[]>("_colors")[0];
            Assert.That(frozen.r, Is.GreaterThan(0.2f).And.LessThan(0.8f));
            Step(500, 0f);
            Assert.That(Get<Color[]>("_colors")[0], Is.EqualTo(frozen));
        }

        [Test]
        public void AtlasFrameKeepsAdvancingAcrossTheFormerParticleLifetime()
        {
            Set("_sourceRate", 9000f);
            Set("_frameCount", 64);
            Set("_framesPerSecond", 45f);
            Step(43, 0f);
            Assert.That(Get<float[]>("_frames")[0], Is.InRange(63.5f, 63.9f));
            Step(1, 0f);
            Assert.That(Get<float[]>("_frames")[0], Is.InRange(0.9f, 1.5f));
            Step(1000, 0f);
            Assert.That(Get<float[]>("_ages")[0], Is.GreaterThan(34f));
            Assert.That(Get<float[]>("_frames")[0], Is.InRange(0f, 63.99999f));
        }

        [Test]
        public void FractionalSourceRateRespectsCapacityAndKeepsExistingParticles()
        {
            Set("_maximum", 2);
            Set("_sourceRate", 25f);
            Step(1, 1f);
            Assert.That(Get<int>("_count"), Is.Zero);
            Step(1, 0f);
            Assert.That(Get<int>("_count"), Is.EqualTo(1));
            Step(1, 0f);
            Assert.That(Get<int>("_count"), Is.EqualTo(2));
            Step(100, 0f);
            Assert.That(Get<int>("_count"), Is.EqualTo(2));
            Assert.That(Get<float[]>("_ages")[0], Is.GreaterThan(Get<float[]>("_ages")[1]));
        }

        [Test]
        public void NegativeSourceRotationKeepsItsDirectionAndDisableResetsTheBirthState()
        {
            Set("_sourceRate", 9000f);
            Set("_sourceRotation", -1.570796f);
            Set("_targetRotation", 125.663704f);
            Set("_rotationScale", 0.001f);
            Step(100, 0f);
            Assert.That(Get<float[]>("_rotations")[0], Is.LessThan(-1.570796f));
            Invoke("OnDisable");
            Assert.That(Get<int>("_count"), Is.Zero);
            Step(1, 0f);
            Assert.That(Get<float[]>("_ages")[0], Is.EqualTo(0.033f));
            Assert.That(Get<float[]>("_rotations")[0], Is.InRange(-1.58f, -1.570796f));
        }

        private void Step(int count, float emissionRoll)
        {
            for (int index = 0; index < count; index++)
            {
                Invoke("AdvanceStep", emissionRoll);
            }
        }

        private void Set(string name, object value)
        {
            typeof(SpatialBubblePersistentParticle).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_effect, value);
        }

        private T Get<T>(string name)
        {
            return (T)typeof(SpatialBubblePersistentParticle).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_effect);
        }

        private void Invoke(string name, params object[] arguments)
        {
            typeof(SpatialBubblePersistentParticle).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_effect, arguments);
        }
    }
}
