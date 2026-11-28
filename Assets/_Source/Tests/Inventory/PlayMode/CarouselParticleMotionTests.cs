#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class CarouselParticleMotionTests
    {
        private const string PREFAB_PATH = "Assets/_Source/Anomalies/Carousel/Carousel.prefab";

        [Test]
        public void FiniteRocksStayOnNativeOrbitAndFadeBeforeReplacement()
        {
            ParticleSystem system = Layer("gravi_rocks1");
            Assert.That(system.main.loop, Is.True);
            Assert.That(system.main.startLifetime.constant, Is.EqualTo(5f));
            AssertStableOrbit(system);
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            Assert.That(color.enabled, Is.True);
            Assert.That(color.color.gradient.Evaluate(0f).a, Is.Zero);
            Assert.That(color.color.gradient.Evaluate(0.5f).a, Is.GreaterThan(0.9f));
            Assert.That(color.color.gradient.Evaluate(1f).a, Is.Zero);
        }

        [Test]
        public void PersistentRocksDoNotRestartTheirMotionOrFreezeTheirAtlasAtInfiniteAge()
        {
            ParticleSystem system = Layer("gravi_rocks2");
            Assert.That(float.IsPositiveInfinity(system.main.startLifetime.constant), Is.True);
            Assert.That(system.main.maxParticles, Is.EqualTo(8));
            AssertStableOrbit(system);
            Assert.That(system.colorOverLifetime.enabled, Is.False);
            Assert.That(system.sizeOverLifetime.enabled, Is.False);
            Assert.That(system.rotationOverLifetime.enabled, Is.False);
            ParticleSystem.TextureSheetAnimationModule frames = system.textureSheetAnimation;
            Assert.That(frames.enabled, Is.True);
            Assert.That(frames.timeMode, Is.EqualTo(ParticleSystemAnimationTimeMode.FPS));
            Assert.That(frames.fps, Is.EqualTo(24f));
            Assert.That(frames.numTilesX, Is.EqualTo(4));
            Assert.That(frames.numTilesY, Is.EqualTo(4));
            ParticleSystem.CustomDataModule custom = system.customData;
            Assert.That(custom.GetMode(ParticleSystemCustomData.Custom1), Is.EqualTo(ParticleSystemCustomDataMode.Vector));
            ParticleSystem.MinMaxCurve frame = custom.GetVector(ParticleSystemCustomData.Custom1, 0);
            Assert.That(frame.mode, Is.EqualTo(ParticleSystemCurveMode.Constant));
            Assert.That(frame.constant, Is.Zero);
            Material material = system.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Assert.That(material, Is.Not.Null);
            Assert.That(material.GetTexture("_MainTex"), Is.Not.Null);
            Vector4 size = material.GetVector("_FrameSize");
            Assert.That(size.x, Is.EqualTo(1f));
            Assert.That(size.y, Is.EqualTo(1f));
            Assert.That(material.GetFloat("_FrameColumns"), Is.EqualTo(1f));
        }

        [Test]
        public void CoreBubblesRemainStationaryWithoutAgeDrivenDisappearance()
        {
            foreach (string name in new[] { "gravitational_core_bubble_small", "gravitational_core_bubble2" })
            {
                ParticleSystem system = Layer(name);
                ParticleSystem.MainModule main = system.main;
                Assert.That(float.IsPositiveInfinity(main.startLifetime.constant), Is.True, name);
                Assert.That(main.maxParticles, Is.EqualTo(1), name);
                Assert.That(system.transform.localPosition, Is.EqualTo(new Vector3(0f, 1.5f, 0f)), name);
                Assert.That(main.startSpeed.constant, Is.Zero, name);
                Assert.That(main.startSizeX.constant, Is.EqualTo(0.6f).Within(0.000001f), name);
                Assert.That(main.startSizeY.constant, Is.EqualTo(0.6f).Within(0.000001f), name);
                Assert.That(main.startSizeZ.constant, Is.EqualTo(0.6f).Within(0.000001f), name);
                Assert.That(main.startColor.color.a, Is.EqualTo(0.75f).Within(0.000001f), name);
                Assert.That(system.velocityOverLifetime.enabled, Is.False, name);
                Assert.That(system.noise.enabled, Is.False, name);
                Assert.That(system.forceOverLifetime.enabled, Is.False, name);
                Assert.That(system.colorOverLifetime.enabled, Is.False, name);
                Assert.That(system.sizeOverLifetime.enabled, Is.False, name);
                Assert.That(system.rotationOverLifetime.enabled, Is.False, name);
                Assert.That(system.lifetimeByEmitterSpeed.enabled, Is.False, name);
                Color expected = name == "gravitational_core_bubble_small" ? Color.white : new Color(0.356863f, 0.678431f, 0.760784f, 1f);
                Assert.That(main.startColor.color.r, Is.EqualTo(expected.r).Within(0.000001f), name);
                Assert.That(main.startColor.color.g, Is.EqualTo(expected.g).Within(0.000001f), name);
                Assert.That(main.startColor.color.b, Is.EqualTo(expected.b).Within(0.000001f), name);
            }
        }

        [Test]
        public void EnabledVelocityModulesUseOneCurveModePerAxisTriplet()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Assert.That(prefab, Is.Not.Null);
            int checkedModules = 0;
            foreach (ParticleSystem system in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                if (velocity.enabled == false)
                {
                    continue;
                }
                AssertTripletMode(velocity.x, velocity.y, velocity.z, system.name + "/linear");
                AssertTripletMode(velocity.orbitalX, velocity.orbitalY, velocity.orbitalZ, system.name + "/orbital");
                AssertTripletMode(velocity.orbitalOffsetX, velocity.orbitalOffsetY, velocity.orbitalOffsetZ, system.name + "/orbitalOffset");
                checkedModules++;
            }
            Assert.That(checkedModules, Is.GreaterThan(0));
        }

        private static void AssertTripletMode(ParticleSystem.MinMaxCurve x, ParticleSystem.MinMaxCurve y, ParticleSystem.MinMaxCurve z, string name)
        {
            Assert.That(y.mode, Is.EqualTo(x.mode), name + ": X/Y mode mismatch");
            Assert.That(z.mode, Is.EqualTo(x.mode), name + ": X/Z mode mismatch");
        }

        private static void AssertStableOrbit(ParticleSystem system)
        {
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            Assert.That(velocity.enabled, Is.True);
            Assert.That(velocity.x.mode, Is.EqualTo(ParticleSystemCurveMode.Constant));
            Assert.That(velocity.y.mode, Is.EqualTo(ParticleSystemCurveMode.Constant));
            Assert.That(velocity.z.mode, Is.EqualTo(ParticleSystemCurveMode.Constant));
            Assert.That(velocity.x.constant, Is.Zero);
            Assert.That(velocity.y.constant, Is.Zero);
            Assert.That(velocity.z.constant, Is.Zero);
            Assert.That(float.IsNaN(velocity.orbitalY.constantMin) || float.IsInfinity(velocity.orbitalY.constantMin), Is.False);
            Assert.That(float.IsNaN(velocity.orbitalY.constantMax) || float.IsInfinity(velocity.orbitalY.constantMax), Is.False);
            Assert.That(velocity.orbitalX.mode, Is.EqualTo(ParticleSystemCurveMode.TwoConstants));
            Assert.That(velocity.orbitalY.mode, Is.EqualTo(ParticleSystemCurveMode.TwoConstants));
            Assert.That(velocity.orbitalZ.mode, Is.EqualTo(ParticleSystemCurveMode.TwoConstants));
            Assert.That(velocity.orbitalX.constantMin, Is.Zero);
            Assert.That(velocity.orbitalX.constantMax, Is.Zero);
            Assert.That(velocity.orbitalZ.constantMin, Is.Zero);
            Assert.That(velocity.orbitalZ.constantMax, Is.Zero);
            Assert.That(velocity.orbitalY.constantMin, Is.GreaterThan(0f));
            Assert.That(velocity.orbitalY.constantMax, Is.GreaterThanOrEqualTo(velocity.orbitalY.constantMin));
            Assert.That(velocity.orbitalOffsetY.constant, Is.EqualTo(1.6f).Within(0.000001f));
            Assert.That(system.forceOverLifetime.enabled, Is.False);
            Assert.That(system.noise.enabled, Is.False);
        }

        private static ParticleSystem Layer(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Assert.That(prefab, Is.Not.Null);
            ParticleSystem found = null;
            foreach (ParticleSystem system in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.name == name)
                {
                    Assert.That(found, Is.Null, "Duplicate layer " + name);
                    found = system;
                }
            }
            Assert.That(found, Is.Not.Null, name);
            return found;
        }
    }
}
#endif

