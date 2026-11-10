using System;
using NUnit.Framework;
using RaidIntoDarkness.Player.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class GravityAnomalyParticleSizeTests
    {
        [Test]
        public void ZeroInitialSizeGrowsInSourceMetres()
        {
            WithParticle(system =>
            {
                GravityAnomalyParticleSizeRepair.Configure(system, 0f, 0f, 2f, 3f, 1f);
                Vector2 bounds = GravityAnomalyParticleSizeRepair.EvaluateBounds(0f, 0f, 2f, 3f, 0.5f);
                float expected = 2f * (1f - Mathf.Exp(-1.5f));
                Assert.That(bounds.x, Is.EqualTo(expected).Within(0.00001f));
                Assert.That(bounds.y, Is.EqualTo(expected).Within(0.00001f));
                Assert.That(system.main.startSizeX.constant, Is.EqualTo(1f));
                Assert.That(system.sizeOverLifetime.x.curveMin.Evaluate(0f), Is.Zero);
                Vector2 sampled = SimulateSizes(system, 1f, 0.5f, Vector3.zero);
                Assert.That(sampled.x, Is.EqualTo(expected).Within(0.002f));
                Assert.That(sampled.y, Is.EqualTo(expected).Within(0.002f));
            });
        }

        [Test]
        public void NonzeroInitialSizeTracksTargetWithoutNormalization()
        {
            WithParticle(system =>
            {
                GravityAnomalyParticleSizeRepair.Configure(system, 3f, 3f, 7f, 2f, 0.5f);
                float expected = 7f - 4f * Mathf.Exp(-0.5f);
                Vector2 sampled = SimulateSizes(system, 0.5f, 0.25f, Vector3.zero);
                Assert.That(sampled.x, Is.EqualTo(expected).Within(0.002f));
                Assert.That(sampled.y, Is.EqualTo(expected).Within(0.002f));
                Assert.That(system.sizeOverLifetime.x.curveMultiplier, Is.EqualTo(1f));
                Assert.That(system.sizeOverLifetime.x.curveMin.Evaluate(0f), Is.EqualTo(3f));
            });
        }

        [Test]
        public void SignedInitialRangeKeepsPhysicalEnvelopeFinite()
        {
            WithParticle(system =>
            {
                GravityAnomalyParticleSizeRepair.Configure(system, -2.5f, 2.5f, 3.3f, 1f, 0.3f);
                ParticleSystem.MinMaxCurve curve = system.sizeOverLifetime.x;
                Assert.That(curve.mode, Is.EqualTo(ParticleSystemCurveMode.TwoCurves));
                Assert.That(curve.curveMin.length, Is.GreaterThanOrEqualTo(129));
                Assert.That(curve.curveMin.Evaluate(0f), Is.Zero);
                Assert.That(curve.curveMax.Evaluate(0f), Is.EqualTo(2.5f));
                for (int index = 0; index <= 512; index++)
                {
                    float normalized = index / 512f;
                    float lower = curve.curveMin.Evaluate(normalized);
                    float upper = curve.curveMax.Evaluate(normalized);
                    Vector2 expected = GravityAnomalyParticleSizeRepair.EvaluateBounds(-2.5f, 2.5f, 3.3f, 1f, normalized * 0.3f);
                    Assert.That(float.IsNaN(lower) || float.IsInfinity(lower) || float.IsNaN(upper) || float.IsInfinity(upper), Is.False);
                    Assert.That(lower, Is.InRange(0f, 3.3f));
                    Assert.That(upper, Is.InRange(lower, 3.3f));
                    Assert.That(lower, Is.EqualTo(expected.x).Within(0.0001f));
                    Assert.That(upper, Is.EqualTo(expected.y).Within(0.0001f));
                }

                Vector2 sampled = SimulateSizes(system, 0.3f, 0.15f, Vector3.zero);
                Assert.That(sampled.x, Is.GreaterThanOrEqualTo(0f));
                Assert.That(sampled.y, Is.LessThanOrEqualTo(3.3f));
                Assert.That(sampled.y, Is.GreaterThan(0.1f));
            });
        }

        [Test]
        public void ConfigurePreservesUnrelatedParticlePropertiesAndIsIdempotent()
        {
            WithParticle(system =>
            {
                ParticleSystem.MainModule main = system.main;
                main.startLifetime = 4f;
                main.startSizeZ = 0.17f;
                main.maxParticles = 23;
                main.startColor = Color.magenta;
                ParticleSystem.ShapeModule shape = system.shape;
                shape.enabled = true;
                shape.radius = 6.25f;
                GravityAnomalyParticleSizeRepair.Configure(system, -2.5f, 2.5f, 3.3f, 1f, 0.3f);
                string first = EditorJsonUtility.ToJson(system);
                GravityAnomalyParticleSizeRepair.Configure(system, -2.5f, 2.5f, 3.3f, 1f, 0.3f);
                Assert.That(EditorJsonUtility.ToJson(system), Is.EqualTo(first));
                Assert.That(main.startLifetime.constant, Is.EqualTo(4f));
                Assert.That(main.startSizeZ.constant, Is.EqualTo(0.17f));
                Assert.That(main.maxParticles, Is.EqualTo(23));
                Assert.That(main.startColor.color, Is.EqualTo(Color.magenta));
                Assert.That(shape.enabled, Is.True);
                Assert.That(shape.radius, Is.EqualTo(6.25f));
                Assert.Throws<ArgumentException>(() => GravityAnomalyParticleSizeRepair.Configure(system, 2f, 1f, 3f, 1f, 1f));
                Assert.Throws<ArgumentException>(() => GravityAnomalyParticleSizeRepair.Configure(system, 0f, 1f, float.NaN, 1f, 1f));
            });
        }

        [Test]
        public void SourcePrefabsUseFiniteMetreCurves()
        {
            AssertPrefab(GravityAnomalyParticleSizeRepair.CAROUSEL_PREFAB, GravityAnomalyParticleSizeRepair.CAROUSEL_SYSTEM, 3.3f, false);
            AssertPrefab(GravityAnomalyParticleSizeRepair.VORTEX_PREFAB, GravityAnomalyParticleSizeRepair.VORTEX_SYSTEM, 2f, true);
        }

        [Test]
        public void SourcePrefabsProduceMetreSizedParticlesAtNearAndFarPositions()
        {
            AssertSourceSimulation(GravityAnomalyParticleSizeRepair.CAROUSEL_PREFAB, GravityAnomalyParticleSizeRepair.CAROUSEL_SYSTEM, 0.3f, 0.15f, 3.3f);
            AssertSourceSimulation(GravityAnomalyParticleSizeRepair.VORTEX_PREFAB, GravityAnomalyParticleSizeRepair.VORTEX_SYSTEM, 1f, 0.5f, 2f);
        }

        private static void AssertPrefab(string prefabPath, string systemPath, float maximum, bool growsFromZero)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            ParticleSystem system = prefab.transform.Find(systemPath).GetComponent<ParticleSystem>();
            Assert.That(system, Is.Not.Null, systemPath);
            Assert.That(system.main.startSize3D, Is.True, systemPath);
            Assert.That(system.main.startSizeX.constant, Is.EqualTo(1f), systemPath);
            Assert.That(system.main.startSizeY.constant, Is.EqualTo(1f), systemPath);
            Assert.That(system.sizeOverLifetime.x.curveMultiplier, Is.EqualTo(1f), systemPath);
            Assert.That(system.sizeOverLifetime.y.curveMultiplier, Is.EqualTo(1f), systemPath);
            GravityAnomalyParticleSizeState state = GravityAnomalyParticleSizeRepair.Inspect(system);
            Assert.That(state.MaximumAbsoluteSize, Is.LessThanOrEqualTo(maximum), systemPath);
            Assert.That(state.MaximumAbsoluteSize, Is.GreaterThan(0.1f), systemPath);
            if (growsFromZero)
            {
                Assert.That(system.sizeOverLifetime.x.curveMin.Evaluate(0f), Is.Zero);
                Assert.That(system.sizeOverLifetime.x.curveMin.Evaluate(0.5f), Is.GreaterThan(1f));
            }
        }

        private static void AssertSourceSimulation(string prefabPath, string systemPath, float lifetime, float age, float maximum)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            ParticleSystem source = prefab.transform.Find(systemPath).GetComponent<ParticleSystem>();
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                ParticleSystem near = UnityEngine.Object.Instantiate(source);
                ParticleSystem far = UnityEngine.Object.Instantiate(source);
                SceneManager.MoveGameObjectToScene(near.gameObject, scene);
                SceneManager.MoveGameObjectToScene(far.gameObject, scene);
                Vector2 nearSizes = SimulateSizes(near, lifetime, age, Vector3.zero);
                Vector2 farSizes = SimulateSizes(far, lifetime, age, new Vector3(20000f, -5000f, 30000f));
                Assert.That(nearSizes.x, Is.GreaterThanOrEqualTo(0f), systemPath);
                Assert.That(nearSizes.y, Is.InRange(0.1f, maximum), systemPath);
                Assert.That(farSizes.x, Is.EqualTo(nearSizes.x).Within(0.0001f), systemPath);
                Assert.That(farSizes.y, Is.EqualTo(nearSizes.y).Within(0.0001f), systemPath);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Vector2 SimulateSizes(ParticleSystem system, float lifetime, float age, Vector3 position)
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.transform.position = position;
            system.useAutoRandomSeed = false;
            system.randomSeed = 12345;
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            system.Simulate(0f, false, true, false);
            system.Emit(16);
            system.Simulate(age, false, false, false);
            var particles = new ParticleSystem.Particle[32];
            int count = system.GetParticles(particles);
            Assert.That(count, Is.GreaterThan(0), system.name);
            float minimum = float.PositiveInfinity;
            float maximum = 0f;
            for (int index = 0; index < count; index++)
            {
                Vector3 size = particles[index].GetCurrentSize3D(system);
                minimum = Mathf.Min(minimum, size.x, size.y);
                maximum = Mathf.Max(maximum, size.x, size.y);
            }

            return new Vector2(minimum, maximum);
        }

        private static void WithParticle(Action<ParticleSystem> action)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("ParticleSizeFixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                ParticleSystem system = root.AddComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                action(system);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
