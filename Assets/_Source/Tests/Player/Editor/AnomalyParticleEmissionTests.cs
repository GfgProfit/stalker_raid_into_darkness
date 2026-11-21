using System;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyEmissionTestVisual : AnomalyVisual
    {
        public override bool IsDischarging => false;

        protected override bool HasDischarge => false;

        protected override bool StartDischarge()
        {
            return false;
        }

        protected override void StopDischarge()
        {
        }

        protected override void TickVisual(float deltaTime)
        {
        }
    }

    public sealed class AnomalyParticleEmissionTests
    {
        [Test]
        public void SourceCountUsesFloorPlusOneBernoulliInsteadOfFractionAccumulation()
        {
            Assert.That(AnomalyParticleEmission.SampleCount(2f, 0.05f), Is.EqualTo(1));
            Assert.That(AnomalyParticleEmission.SampleCount(2f, 0.07f), Is.Zero);
            Assert.That(AnomalyParticleEmission.SampleCount(100f, 0.29f), Is.EqualTo(4));
            Assert.That(AnomalyParticleEmission.SampleCount(100f, 0.31f), Is.EqualTo(3));
            Assert.That(AnomalyParticleEmission.SampleCount(1000f, 0f), Is.EqualTo(33));
            Assert.That(AnomalyParticleEmission.SampleCount(1000f, 1f), Is.EqualTo(33));
            Assert.That(AnomalyParticleEmission.SampleCount(0f, 0f), Is.Zero);
            int total = 0;
            for (int index = 0; index < 10000; index++)
            {
                total += AnomalyParticleEmission.SampleCount(2f, (index + 0.5f) / 10000f);
            }

            Assert.That(total, Is.EqualTo(660));
            Assert.Throws<ArgumentOutOfRangeException>(() => AnomalyParticleEmission.SampleCount(float.NaN, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => AnomalyParticleEmission.SampleCount(2f, -0.5f));
        }

        [Test]
        public void NoParticlesAppearBeforeFirstSourceStepAndCapacityIsRespected()
        {
            WithRig((root, emitter, system, visual) =>
            {
                ParticleSystem.MainModule main = system.main;
                main.maxParticles = 40;
                emitter.Tick(0.02f);
                Assert.That(system.particleCount, Is.Zero);
                emitter.Tick(0.013f);
                Assert.That(system.particleCount, Is.EqualTo(33));
                emitter.Tick(AnomalyParticleEmission.SOURCE_STEP);
                Assert.That(system.particleCount, Is.EqualTo(40));
                emitter.Tick(1f);
                Assert.That(system.particleCount, Is.EqualTo(40));
            });
        }

        [Test]
        public void PausePreservesPendingStepButStopAndDisableResetIt()
        {
            WithRig((root, emitter, system, visual) =>
            {
                emitter.Tick(0.02f);
                system.Pause(false);
                emitter.Tick(10f);
                Assert.That(system.particleCount, Is.Zero);
                system.Play(false);
                emitter.Tick(0.013f);
                Assert.That(system.particleCount, Is.EqualTo(33));
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                emitter.Tick(0f);
                system.Play(false);
                emitter.Tick(0.02f);
                Assert.That(system.particleCount, Is.Zero);
                emitter.enabled = false;
                InvokeLifecycle(emitter, "OnDisable");
                emitter.Tick(1f);
                Assert.That(system.particleCount, Is.Zero);
                emitter.enabled = true;
                InvokeLifecycle(emitter, "OnEnable");
                emitter.Tick(0.02f);
                Assert.That(system.particleCount, Is.Zero);
                emitter.Tick(0.013f);
                Assert.That(system.particleCount, Is.EqualTo(33));
            });
        }

        [Test]
        public void HideAndShowWithinOneFrameCannotCarryPendingEmission()
        {
            WithRig((root, emitter, system, visual) =>
            {
                emitter.Tick(0.02f);
                visual.Hide();
                visual.ShowIdle();
                emitter.Tick(0.02f);
                Assert.That(system.particleCount, Is.Zero);
                emitter.Tick(0.013f);
                Assert.That(system.particleCount, Is.EqualTo(33));
            });
        }

        [Test]
        public void OriginalTimeLimitAndNonloopingDurationBothStopFurtherBirths()
        {
            WithRig((root, emitter, system, visual) =>
            {
                emitter.Configure(new[] { new AnomalyParticleEmissionSource(system, 1000f, 0.05f) });
                emitter.Tick(AnomalyParticleEmission.SOURCE_STEP);
                Assert.That(system.particleCount, Is.EqualTo(33));
                emitter.Tick(1f);
                Assert.That(system.particleCount, Is.EqualTo(33));
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = system.main;
                main.loop = false;
                main.duration = 0.05f;
                emitter.Configure(new[] { new AnomalyParticleEmissionSource(system, 1000f) });
                system.Play(false);
                emitter.Tick(AnomalyParticleEmission.SOURCE_STEP);
                Assert.That(system.particleCount, Is.EqualTo(33));
                emitter.Tick(1f);
                Assert.That(system.particleCount, Is.EqualTo(33));
            });
        }

        [Test]
        public void SourceClockUsesSimulationSpeedAndDropsCatchupBeyondThreeSteps()
        {
            WithRig((root, emitter, system, visual) =>
            {
                ParticleSystem.MainModule main = system.main;
                main.simulationSpeed = 0.5f;
                emitter.Tick(AnomalyParticleEmission.SOURCE_STEP);
                Assert.That(system.particleCount, Is.Zero);
                emitter.Tick(AnomalyParticleEmission.SOURCE_STEP);
                Assert.That(system.particleCount, Is.EqualTo(33));
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                main.simulationSpeed = 1f;
                emitter.ResetSources();
                system.Play(false);
                emitter.Tick(AnomalyParticleEmission.SOURCE_STEP * 10f);
                Assert.That(system.particleCount, Is.EqualTo(99));
            });
        }

        [Test]
        public void SeparateSourcesConsumeIndependentRandomDraws()
        {
            WithRig((root, emitter, first, visual) =>
            {
                ParticleSystem second = CreateSystem(root.transform, "Second");
                float rate = 0.5f / AnomalyParticleEmission.SOURCE_STEP;
                emitter.Configure(new[] { new AnomalyParticleEmissionSource(first, rate), new AnomalyParticleEmissionSource(second, rate) });
                UnityEngine.Random.InitState(84727);
                UnityEngine.Random.State initial = UnityEngine.Random.state;
                var expectedFirst = new int[32];
                var expectedSecond = new int[32];
                bool differ = false;
                for (int index = 0; index < 32; index++)
                {
                    expectedFirst[index] = (index > 0 ? expectedFirst[index - 1] : 0) + AnomalyParticleEmission.SampleCount(rate, UnityEngine.Random.value);
                    expectedSecond[index] = (index > 0 ? expectedSecond[index - 1] : 0) + AnomalyParticleEmission.SampleCount(rate, UnityEngine.Random.value);
                    differ |= expectedFirst[index] != expectedSecond[index];
                }

                UnityEngine.Random.state = initial;
                for (int index = 0; index < 32; index++)
                {
                    emitter.Tick(AnomalyParticleEmission.SOURCE_STEP);
                    Assert.That(first.particleCount, Is.EqualTo(expectedFirst[index]));
                    Assert.That(second.particleCount, Is.EqualTo(expectedSecond[index]));
                }

                Assert.That(differ, Is.True);
            });
        }

        [Test]
        public void InactiveSourceCannotEmitAndComponentDoesNotChangeNativeModules()
        {
            WithRig((root, emitter, system, visual) =>
            {
                ParticleSystem.EmissionModule emission = system.emission;
                ParticleSystem.Burst burst = new ParticleSystem.Burst(0.5f, 2);
                emission.SetBursts(new[] { burst });
                system.gameObject.SetActive(false);
                emitter.Tick(1f);
                Assert.That(system.particleCount, Is.Zero);
                system.gameObject.SetActive(true);
                system.Play(false);
                emitter.Tick(0.02f);
                Assert.That(system.particleCount, Is.Zero);
                emitter.Tick(0.013f);
                Assert.That(system.particleCount, Is.EqualTo(33));
                Assert.That(emission.enabled, Is.True);
                Assert.That(emission.rateOverTime.constant, Is.Zero);
                Assert.That(emission.burstCount, Is.EqualTo(1));
                Assert.That(emission.GetBurst(0).time, Is.EqualTo(0.5f));
                Assert.That(system.main.startLifetime.constant, Is.EqualTo(100f));
            });
        }

        [Test]
        public void ManualSourceBirthsTriggerNativeBirthSubemitters()
        {
            WithRig((root, emitter, parent, visual) =>
            {
                parent.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule parentMain = parent.main;
                parentMain.maxParticles = 1;
                ParticleSystem child = CreateSystem(parent.transform, "BirthChild");
                child.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule childMain = child.main;
                childMain.loop = false;
                childMain.duration = 1f;
                childMain.maxParticles = 8;
                ParticleSystem.EmissionModule childEmission = child.emission;
                childEmission.rateOverTime = 0f;
                childEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                ParticleSystem.SubEmittersModule subemitters = parent.subEmitters;
                subemitters.enabled = true;
                subemitters.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
                parent.Play(false);
                parent.Simulate(AnomalyParticleEmission.SOURCE_STEP, true, false, false);
                Assert.That(parent.particleCount, Is.Zero);
                Assert.That(child.particleCount, Is.Zero, "A child burst must require a parent birth rather than ordinary child simulation.");
                parent.Play(false);
                emitter.Tick(AnomalyParticleEmission.SOURCE_STEP);
                Assert.That(parent.particleCount, Is.EqualTo(1));
                parent.Simulate(AnomalyParticleEmission.SOURCE_STEP, true, false, false);
                Assert.That(child.particleCount, Is.GreaterThan(0), "Manual source births must preserve native Birth subemitters.");
                Assert.That(child.particleCount, Is.LessThanOrEqualTo(8));
                Assert.That(childEmission.rateOverTime.constant, Is.Zero);
            });
        }

        [Test]
        public void LoopWrapKeepsThePendingStepAndEarlierRewindClearsIt()
        {
            WithRig((root, emitter, system, visual) =>
            {
                system.time = 9.99f;
                emitter.Tick(0.02f);
                Assert.That(system.particleCount, Is.Zero);
                system.time = 0.002f;
                emitter.Tick(0.013f);
                Assert.That(system.particleCount, Is.EqualTo(33));

                system.time = 0.5f;
                emitter.Tick(0.02f);
                system.time = 0.1f;
                int beforeRewind = system.particleCount;
                emitter.Tick(0.013f);
                Assert.That(system.particleCount, Is.EqualTo(beforeRewind));
                emitter.Tick(0.02f);
                Assert.That(system.particleCount, Is.EqualTo(beforeRewind + 33));
            });
        }

        [Test]
        public void EmptyAndFullSourcesConsumeTheirDrawButAnExpiredSourceDoesNot()
        {
            WithRig((root, emitter, empty, visual) =>
            {
                ParticleSystem full = CreateSystem(root.transform, "Full");
                ParticleSystem limited = CreateSystem(root.transform, "Limited");
                ParticleSystem observed = CreateSystem(root.transform, "Observed");
                ParticleSystem.MainModule fullMain = full.main;
                fullMain.maxParticles = 1;
                full.Emit(1);
                float step = AnomalyParticleEmission.SOURCE_STEP;
                float rate = 0.5f / step;
                emitter.Configure(new[]
                {
                    new AnomalyParticleEmissionSource(empty, 0f),
                    new AnomalyParticleEmissionSource(full, 1000f),
                    new AnomalyParticleEmissionSource(limited, 1000f, step),
                    new AnomalyParticleEmissionSource(observed, rate)
                });
                UnityEngine.Random.InitState(38571);
                UnityEngine.Random.State initial = UnityEngine.Random.state;
                var expectedState = new UnityEngine.Random.State[6];
                var expectedObserved = new int[6];
                int total = 0;
                for (int index = 0; index < expectedState.Length; index++)
                {
                    _ = UnityEngine.Random.value;
                    _ = UnityEngine.Random.value;
                    if (index == 0) _ = UnityEngine.Random.value;
                    total += AnomalyParticleEmission.SampleCount(rate, UnityEngine.Random.value);
                    expectedObserved[index] = total;
                    expectedState[index] = UnityEngine.Random.state;
                }

                UnityEngine.Random.state = initial;
                for (int index = 0; index < expectedState.Length; index++)
                {
                    emitter.Tick(step);
                    Assert.That(empty.particleCount, Is.Zero);
                    Assert.That(full.particleCount, Is.EqualTo(1));
                    Assert.That(limited.particleCount, Is.EqualTo(33));
                    Assert.That(observed.particleCount, Is.EqualTo(expectedObserved[index]));
                    Assert.That(UnityEngine.Random.state, Is.EqualTo(expectedState[index]));
                }
            });
        }

        private static ParticleSystem CreateSystem(Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            ParticleSystem system = root.AddComponent<ParticleSystem>();
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.useAutoRandomSeed = false;
            system.randomSeed = 27;
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 10f;
            main.startLifetime = 100f;
            main.startSpeed = 0f;
            main.maxParticles = 1024;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            system.Play(false);
            return system;
        }

        private static void WithRig(Action<GameObject, AnomalyParticleEmission, ParticleSystem, AnomalyEmissionTestVisual> action)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            UnityEngine.Random.State random = UnityEngine.Random.state;
            AnomalyParticleEmission emitter = null;
            try
            {
                var root = new GameObject("AnomalyEmissionFixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                ParticleSystem system = CreateSystem(root.transform, "Idle");
                AnomalyEmissionTestVisual visual = root.AddComponent<AnomalyEmissionTestVisual>();
                typeof(AnomalyVisual).GetField("_idle", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(visual, system);
                emitter = root.AddComponent<AnomalyParticleEmission>();
                emitter.Configure(new[] { new AnomalyParticleEmissionSource(system, 1000f) });
                InvokeLifecycle(emitter, "OnDisable");
                InvokeLifecycle(emitter, "OnEnable");
                action(root, emitter, system, visual);
            }
            finally
            {
                if (emitter != null)
                {
                    InvokeLifecycle(emitter, "OnDisable");
                }

                EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Random.state = random;
            }
        }

        private static void InvokeLifecycle(AnomalyParticleEmission emitter, string method)
        {
            typeof(AnomalyParticleEmission).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(emitter, null);
        }
    }
}
