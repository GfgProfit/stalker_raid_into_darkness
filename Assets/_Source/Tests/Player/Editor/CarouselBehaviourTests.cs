using NUnit.Framework;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class CarouselBehaviourTests
    {
        [Test]
        public void AwakeningUsesFiftyMillisecondsBeforeBlowout()
        {
            var cycle = new AnomalyCycle(0.05f, 10f, 4f);
            Assert.That(cycle.Tick(0f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Awaking));
            Assert.That(cycle.Tick(0.049f, true), Is.False);
            Assert.That(cycle.Tick(0.05f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
        }

        [Test]
        public void FullCycleUsesTenSecondsOfBlowoutAndFourOfAccumulation()
        {
            var cycle = new AnomalyCycle(0.05f, 10f, 4f);
            cycle.RecordDischarge(0f);
            Assert.That(cycle.Tick(9.999f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            Assert.That(cycle.Tick(10f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
            Assert.That(cycle.GetCooldownRemaining(11f), Is.EqualTo(3f));
            Assert.That(cycle.Tick(13.999f, true), Is.False);
            Assert.That(cycle.Tick(14f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
        }

        [Test]
        public void LeavingAfterAwakeningDoesNotCancelCommittedDischarge()
        {
            var cycle = new AnomalyCycle(0.05f, 10f, 4f);
            cycle.Tick(0f, true);
            Assert.That(cycle.Tick(0.05f, false), Is.True);
            cycle.Tick(10.05f, false);
            cycle.Tick(14.051f, false);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Idle));
        }

        [Test]
        public void BoltsNeverAwakenCarouselDespiteUnlimitedSmallObjectLifetime()
        {
            var cycle = new AnomalyCycle(0.05f, 10f, 4f);
            Assert.That(cycle.Tick(0f, AnomalyProbe.CanActivate(0f, true, -1f)), Is.False);
            Assert.That(cycle.Tick(100f, AnomalyProbe.CanActivate(100f, true, -1f)), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Idle));
        }

        [Test]
        public void PullUsesSourceStrengthAndSixtyPercentInnerClamp()
        {
            Vector3 outer = AnomalyAttraction.Evaluate(Vector3.right * 3f, 5f, 0.6f, 600f);
            Vector3 inner = AnomalyAttraction.Evaluate(Vector3.right * 0.1f, 5f, 0.6f, 600f);
            float expected = 600f * Mathf.Pow(1f - 0.6f * 0.6f, 5f);
            Assert.That(outer.x, Is.EqualTo(expected).Within(0.0001f));
            Assert.That(inner.x, Is.EqualTo(expected).Within(0.0001f));
            Assert.That(inner.y, Is.Zero);
            Assert.That(inner.z, Is.Zero);
        }

        [Test]
        public void PullHasNoTangentialRotationOrForcedUpwardLaunch()
        {
            Vector3 offset = new Vector3(2f, -1f, 3f);
            Vector3 pull = AnomalyAttraction.Evaluate(offset, 5f, 0.6f, 600f);
            Assert.That(Vector3.Cross(offset.normalized, pull.normalized).magnitude, Is.LessThan(0.00001f));
            Assert.That(pull.y, Is.LessThan(0f));
        }

        [Test]
        public void PullStopsAtAndOutsideTriggerBoundary()
        {
            Assert.That(AnomalyAttraction.Evaluate(Vector3.forward * 5f, 5f, 0.6f, 600f), Is.EqualTo(Vector3.zero));
            Assert.That(AnomalyAttraction.Evaluate(Vector3.forward * 6f, 5f, 0.6f, 600f), Is.EqualTo(Vector3.zero));
        }
    }
}
