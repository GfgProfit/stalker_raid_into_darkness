using NUnit.Framework;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class BoltThrowTests
    {
        [Test]
        public void ChargeIsClampedBetweenOriginalSpeeds()
        {
            Assert.That(BoltThrowMath.ChargeSpeed(-1f), Is.EqualTo(10f));
            Assert.That(BoltThrowMath.ChargeSpeed(0f), Is.EqualTo(10f));
            Assert.That(BoltThrowMath.ChargeSpeed(0.5f), Is.EqualTo(15f));
            Assert.That(BoltThrowMath.ChargeSpeed(1f), Is.EqualTo(20f));
            Assert.That(BoltThrowMath.ChargeSpeed(100f), Is.EqualTo(20f));
        }

        [Test]
        public void ThrowAddsPlayerMomentumWithoutChangingDirection()
        {
            Vector3 inherited = new Vector3(4f, 2f, -3f);
            Vector3 velocity = BoltThrowMath.LaunchVelocity(Vector3.forward * 5f, 20f, inherited);
            Assert.That(velocity, Is.EqualTo(new Vector3(4f, 2f, 17f)));
        }

        [Test]
        public void ThrowPointUsesActorRotationRatherThanCameraPitch()
        {
            Vector3 camera = new Vector3(1f, 2f, 3f);
            Vector3 offset = BoltThrowMath.LaunchPosition(camera, Quaternion.Euler(0f, 90f, 0f)) - camera;
            Assert.That(offset.x, Is.EqualTo(0.1f).Within(0.00001f));
            Assert.That(offset.y, Is.EqualTo(0.3f).Within(0.00001f));
            Assert.That(offset.z, Is.EqualTo(-0.4f).Within(0.00001f));
        }

        [Test]
        public void ElectricAndSpringboardIgnoreRestingProbeOnlyAfterHalfSecond()
        {
            Assert.That(AnomalyProbe.CanActivate(0f, false, 0.5f), Is.True);
            Assert.That(AnomalyProbe.CanActivate(0.5f, false, 0.5f), Is.True);
            Assert.That(AnomalyProbe.CanActivate(0.5001f, false, 0.5f), Is.False);
        }

        [Test]
        public void ThermalProbeHasNoSmallObjectTimeout()
        {
            Assert.That(AnomalyProbe.CanActivate(0f, false, -1f), Is.True);
            Assert.That(AnomalyProbe.CanActivate(BoltThrowMath.LIFETIME, false, -1f), Is.True);
        }

        [Test]
        public void VortexNeverAwakensFromSmallProbe()
        {
            Assert.That(AnomalyProbe.CanActivate(0f, true, -1f), Is.False);
            Assert.That(AnomalyProbe.CanActivate(0f, true, 0.5f), Is.False);
        }

        [Test]
        public void ProbeStartsOneDischargeWithoutKeepingElectricCycleAlive()
        {
            var cycle = new AnomalyCycle(0.1f, 1f, 0.5f);
            Assert.That(cycle.Tick(0f, AnomalyProbe.CanActivate(0f, false, 0.5f)), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Awaking));
            Assert.That(cycle.Tick(0.1f, AnomalyProbe.CanActivate(0.1f, false, 0.5f)), Is.True);
            Assert.That(cycle.Tick(1.2f, AnomalyProbe.CanActivate(1.2f, false, 0.5f)), Is.False);
            Assert.That(cycle.Tick(1.8f, AnomalyProbe.CanActivate(1.8f, false, 0.5f)), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Idle));
            Assert.That(cycle.Tick(1.9f, AnomalyProbe.CanActivate(0f, false, 0.5f)), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Awaking));
        }

        [Test]
        public void VortexProbeLeavesCycleIdle()
        {
            var cycle = new AnomalyCycle(0.3f, 2.5f, 1f);
            Assert.That(cycle.Tick(0f, AnomalyProbe.CanActivate(0f, true, 0.5f)), Is.False);
            Assert.That(cycle.Tick(1f, AnomalyProbe.CanActivate(1f, true, 0.5f)), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Idle));
        }
    }
}
