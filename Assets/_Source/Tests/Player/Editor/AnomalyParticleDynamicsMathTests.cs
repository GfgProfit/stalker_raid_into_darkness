using System;
using System.Collections.Generic;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyParticleDynamicsMathTests
    {
        [Test]
        public void VortexRotatesPositionAroundItsAxisWithoutChangingVelocity()
        {
            Vector3 position = Vector3.right;
            Vector3 velocity = new Vector3(2f, 3f, 4f);
            AnomalyParticleMotionAction action = Action(XRayParticleActionType.Vortex, Vector3.zero, Vector3.up, 0f, Mathf.PI * 0.5f, 10f);
            Apply(ref position, ref velocity, action, 1f);
            Assert.That(Vector3.Distance(position, Vector3.back), Is.LessThan(0.000001f));
            Assert.That(velocity, Is.EqualTo(new Vector3(2f, 3f, 4f)));
        }

        [Test]
        public void VortexPreservesAxialOffsetAndDistanceAndHonorsItsRadius()
        {
            Vector3 position = new Vector3(1f, 2f, 0f);
            Vector3 velocity = Vector3.zero;
            AnomalyParticleMotionAction action = Action(XRayParticleActionType.Vortex, Vector3.zero, Vector3.up, 2f, 30f, 3f);
            float radius = position.magnitude;
            Apply(ref position, ref velocity, action, 0.033f);
            Assert.That(position.y, Is.EqualTo(2f).Within(0.000001f));
            Assert.That(position.magnitude, Is.EqualTo(radius).Within(0.000001f));
            position = new Vector3(4f, 0f, 0f);
            Apply(ref position, ref velocity, action, 0.033f);
            Assert.That(position, Is.EqualTo(new Vector3(4f, 0f, 0f)));
        }

        [Test]
        public void OrbitAndScatterUseDifferentNativeDistanceFalloffs()
        {
            Vector3 position = Vector3.right * 2f;
            Vector3 velocity = Vector3.zero;
            Apply(ref position, ref velocity, Action(XRayParticleActionType.OrbitPoint, Vector3.zero, Vector3.zero, 2f, 16f, 10f), 1f);
            Assert.That(velocity, Is.EqualTo(Vector3.left * 4f));
            velocity = Vector3.zero;
            Apply(ref position, ref velocity, Action(XRayParticleActionType.Scatter, Vector3.zero, Vector3.zero, 4f, 16f, 10f), 1f);
            Assert.That(velocity, Is.EqualTo(Vector3.right * 2f));
        }

        [Test]
        public void NativeActionOrderMovesOnlyAfterPreviouslyAppliedVelocityActions()
        {
            Vector3 position = Vector3.zero;
            Vector3 velocity = Vector3.zero;
            AnomalyParticleMotionAction gravity = Action(XRayParticleActionType.Gravity, Vector3.up * 4f);
            AnomalyParticleMotionAction move = Action(XRayParticleActionType.Move);
            Apply(ref position, ref velocity, gravity, 0.5f);
            Apply(ref position, ref velocity, move, 0.5f);
            Assert.That(position, Is.EqualTo(Vector3.up));
            position = Vector3.zero;
            velocity = Vector3.zero;
            Apply(ref position, ref velocity, move, 0.5f);
            Apply(ref position, ref velocity, gravity, 0.5f);
            Assert.That(position, Is.EqualTo(Vector3.zero));
            Assert.That(velocity, Is.EqualTo(Vector3.up * 2f));
        }

        [Test]
        public void PackedDampingUsesSquaredBoundsAndDoesNotDampOutsideThem()
        {
            AnomalyParticleMotionAction damping = new AnomalyParticleMotionAction(XRayParticleActionType.Damping, 0u, Vector3.zero, Vector3.zero, 4f, 9f, 0f, 0, default, true);
            Vector3 position = Vector3.zero;
            Vector3 velocity = Vector3.right * 2f;
            Apply(ref position, ref velocity, damping, 0.5f);
            Assert.That(velocity, Is.EqualTo(Vector3.right));
            velocity = Vector3.right;
            Apply(ref position, ref velocity, damping, 0.5f);
            Assert.That(velocity, Is.EqualTo(Vector3.right));
            velocity = Vector3.right * 4f;
            Apply(ref position, ref velocity, damping, 0.5f);
            Assert.That(velocity, Is.EqualTo(Vector3.right * 4f));
        }

        [Test]
        public void RandomDisplaceChangesPositionRatherThanVelocity()
        {
            XRayParticleDomain domain = new XRayParticleDomain(XRayParticleDomainType.Point, new Vector3(2f, -1f, 3f), Vector3.zero, Vector3.zero, Vector3.zero, 0f, 0f);
            AnomalyParticleMotionAction action = new AnomalyParticleMotionAction(XRayParticleActionType.RandomDisplace, 0u, Vector3.zero, Vector3.zero, 0f, 0f, 0f, 0, domain, false);
            Vector3 position = Vector3.zero;
            Vector3 velocity = Vector3.one;
            Apply(ref position, ref velocity, action, 0.25f);
            Assert.That(position, Is.EqualTo(new Vector3(0.5f, -0.25f, 0.75f)));
            Assert.That(velocity, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void SourceCentersAndDirectionDomainsRespectNativeAllowRotate()
        {
            Matrix4x4 pose = Matrix4x4.TRS(Vector3.right * 10f, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
            AnomalyParticleMotionAction translated = new AnomalyParticleMotionAction(XRayParticleActionType.OrbitPoint, 0u, Vector3.right, Vector3.zero, 1f, 1f, 10f, 0, default, false).ToWorld(pose);
            AnomalyParticleMotionAction rotated = new AnomalyParticleMotionAction(XRayParticleActionType.OrbitPoint, XRayParticleAction.ALLOW_ROTATE, Vector3.right, Vector3.zero, 1f, 1f, 10f, 0, default, false).ToWorld(pose);
            Assert.That(translated.Vector0, Is.EqualTo(Vector3.right * 11f));
            Assert.That(Vector3.Distance(rotated.Vector0, new Vector3(10f, 0f, -1f)), Is.LessThan(0.000001f));
            Assert.That(Action(XRayParticleActionType.Gravity, Vector3.right).ToWorld(pose).Vector0, Is.EqualTo(Vector3.right));
        }

        [Test]
        public void SourceClockUsesThirtyThreeMillisecondsAndDropsExcessHitchSteps()
        {
            float accumulated = 0f;
            Assert.That(AnomalyParticleDynamics.AdvanceClock(ref accumulated, 0.0165f), Is.Zero);
            Assert.That(AnomalyParticleDynamics.AdvanceClock(ref accumulated, 0.0165f), Is.EqualTo(1));
            Assert.That(accumulated, Is.EqualTo(0f).Within(0.000001f));
            Assert.That(AnomalyParticleDynamics.AdvanceClock(ref accumulated, 1f), Is.EqualTo(3));
            Assert.That(accumulated, Is.LessThan(AnomalyParticleDynamics.STEP));
            Assert.That(AnomalyParticleDynamics.AdvanceClock(ref accumulated, 0f), Is.Zero);
        }

        [Test]
        public void NativeLoopKeepsRenderVelocityOutsideParticleIntegration()
        {
            var types = new List<Type>();
            CollectTypes(UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop(), types);
            int update = types.IndexOf(typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
            int particles = types.IndexOf(typeof(UnityEngine.PlayerLoop.PreLateUpdate.ParticleSystemBeginUpdateAll));
            int late = types.IndexOf(typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate));
            int renderers = types.IndexOf(typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAllRenderers));
            Assert.That(update, Is.GreaterThanOrEqualTo(0));
            Assert.That(particles, Is.GreaterThan(update));
            Assert.That(late, Is.GreaterThan(particles));
            Assert.That(renderers, Is.GreaterThan(late));
        }

        [Test]
        public void NativeNoiseMatchesIndependentFloat32ReferenceSamples()
        {
            Assert.That(AnomalyParticleNoise.Sample(Vector3.zero), Is.EqualTo(0f));
            Assert.That(AnomalyParticleNoise.Sample(new Vector3(0.5f, 0.25f, 0.75f)), Is.EqualTo(-0.366708219f).Within(0.00001f));
            Assert.That(AnomalyParticleNoise.Sample(new Vector3(1.125f, -2.5f, 3.75f)), Is.EqualTo(0.244003505f).Within(0.00001f));
            Assert.That(AnomalyParticleNoise.Sample(new Vector3(123.25f, 13.5f, -56.125f)), Is.EqualTo(-0.097639047f).Within(0.00001f));
        }

        [Test]
        public void NativeFractalUsesTheSourceOctaveFrequencyProgression()
        {
            Assert.That(AnomalyParticleNoise.Fractal(new Vector3(0.5f, 0.25f, 0.75f), 1.7f, 3), Is.EqualTo(0.006495125f).Within(0.00001f));
            Assert.That(AnomalyParticleNoise.Fractal(new Vector3(1.125f, -2.5f, 3.75f), 1.7f, 3), Is.EqualTo(-0.340711117f).Within(0.00001f));
            Assert.That(AnomalyParticleNoise.Fractal(new Vector3(123.25f, 13.5f, -56.125f), 1.7f, 3), Is.EqualTo(0.101249285f).Within(0.00001f));
        }

        [Test]
        public void NativeTurbulenceRedirectsVelocityWithoutChangingItsMagnitude()
        {
            Vector3 position = new Vector3(0.5f, 0.25f, 0.75f);
            Vector3 velocity = new Vector3(2f, 1f, 3f);
            Vector3 original = velocity;
            AnomalyParticleMotionAction action = new AnomalyParticleMotionAction(XRayParticleActionType.Turbulence, 0u, new Vector3(0.1f, -0.2f, 0.3f), Vector3.zero, 0.01f, 1.7f, 5f, 3, default, false);
            Apply(ref position, ref velocity, action, 0.033f, 0.66f);
            Assert.That(velocity.magnitude, Is.EqualTo(original.magnitude).Within(0.00001f));
            Assert.That(Vector3.Distance(velocity, original), Is.GreaterThan(0.00001f));
            velocity = Vector3.zero;
            Apply(ref position, ref velocity, action, 0.033f, 0.66f);
            Assert.That(velocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void SourceSinkDomainsKillInsideBoxAndOutsidePlaneWithoutCollisions()
        {
            XRayParticleDomain box = new XRayParticleDomain(XRayParticleDomainType.Box, new Vector3(4f, 0f, 4f), new Vector3(-4f, -3f, -4f), Vector3.zero, Vector3.zero, 0f, 0f);
            XRayParticleDomain plane = new XRayParticleDomain(XRayParticleDomainType.Plane, new Vector3(0f, -0.1f, 0f), Vector3.up, Vector3.zero, Vector3.zero, 0f, 0f);
            AnomalyParticleMotionAction sinkInside = new AnomalyParticleMotionAction(XRayParticleActionType.Sink, 0u, Vector3.zero, Vector3.zero, 1f, 0f, 0f, 0, box, false);
            AnomalyParticleMotionAction sinkOutside = new AnomalyParticleMotionAction(XRayParticleActionType.Sink, 0u, Vector3.zero, Vector3.zero, 0f, 0f, 0f, 0, plane, false);
            Vector3 velocity = Vector3.zero;
            Vector3 position = Vector3.down;
            Assert.That(AnomalyParticleDynamicsMath.Apply(ref position, ref velocity, sinkInside, 0.033f, 0f), Is.False);
            Assert.That(AnomalyParticleDynamicsMath.Apply(ref position, ref velocity, sinkOutside, 0.033f, 0f), Is.False);
            position = Vector3.zero;
            Assert.That(AnomalyParticleDynamicsMath.Apply(ref position, ref velocity, sinkOutside, 0.033f, 0f), Is.True);
        }

        [Test]
        public void BirthVelocityIsStableForItsSeedAndDoesNotConsumeGlobalRandomState()
        {
            UnityEngine.Random.State previous = UnityEngine.Random.state;
            try
            {
                XRayParticleDomain domain = new XRayParticleDomain(XRayParticleDomainType.Blob, Vector3.up, Vector3.zero, Vector3.zero, Vector3.zero, 1f, 0f);
                UnityEngine.Random.InitState(34128);
                float expected = UnityEngine.Random.value;
                UnityEngine.Random.InitState(34128);
                Vector3 first = AnomalyParticleDynamicsMath.GenerateVelocity(domain, 123u, Matrix4x4.identity, true, out _);
                AnomalyParticleDynamicsMath.GenerateVelocity(domain, 456u, Matrix4x4.identity, true, out _);
                Vector3 repeated = AnomalyParticleDynamicsMath.GenerateVelocity(domain, 123u, Matrix4x4.identity, true, out _);
                Assert.That(repeated, Is.EqualTo(first));
                Assert.That(UnityEngine.Random.value, Is.EqualTo(expected));
            }
            finally
            {
                UnityEngine.Random.state = previous;
            }
        }

        [Test]
        public void DegenerateForceCentersRemainFiniteInsteadOfWritingNanParticles()
        {
            foreach (XRayParticleActionType type in new[] { XRayParticleActionType.OrbitPoint, XRayParticleActionType.Vortex, XRayParticleActionType.Scatter })
            {
                Vector3 position = Vector3.zero;
                Vector3 velocity = Vector3.zero;
                Apply(ref position, ref velocity, Action(type, Vector3.zero, Vector3.up, 0f, 10f, 3f), 0.033f);
                Assert.That(position, Is.EqualTo(Vector3.zero));
                Assert.That(velocity, Is.EqualTo(Vector3.zero));
            }
        }

        private static AnomalyParticleMotionAction Action(XRayParticleActionType type, Vector3 vector0 = default, Vector3 vector1 = default, float parameter0 = 0f, float parameter1 = 0f, float parameter2 = 0f)
        {
            return new AnomalyParticleMotionAction(type, 0u, vector0, vector1, parameter0, parameter1, parameter2, 0, default, false);
        }

        private static void CollectTypes(UnityEngine.LowLevel.PlayerLoopSystem system, List<Type> types)
        {
            types.Add(system.type);
            if (system.subSystemList == null)
            {
                return;
            }
            foreach (UnityEngine.LowLevel.PlayerLoopSystem child in system.subSystemList)
            {
                CollectTypes(child, types);
            }
        }

        private static void Apply(ref Vector3 position, ref Vector3 velocity, AnomalyParticleMotionAction action, float deltaTime, float time = 0f)
        {
            Assert.That(AnomalyParticleDynamicsMath.Apply(ref position, ref velocity, action, deltaTime, time), Is.True);
        }
    }
}
