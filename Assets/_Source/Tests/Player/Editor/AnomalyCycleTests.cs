using NUnit.Framework;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyCycleTests
    {
        private const float EPSILON = 0.00001f;

        private static readonly float[][] _sourcePresets = { new[] { 0.1f, 0.1f, 1.5f, 1.6f }, new[] { 0.1f, 7f, 0.25f, 7.25f }, new[] { 0.01f, 0.1f, 1f, 1.1f } };

        [Test]
        public void SourcePresetsExposeExpectedRemainingTimes()
        {
            foreach (float[] preset in _sourcePresets)
            {
                var cycle = new AnomalyCycle(preset[0], preset[1], preset[2]);
                float halfwayAwaking = preset[0] * 0.5f;
                float halfwayBlowout = preset[0] + preset[1] * 0.5f;
                Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Idle));
                Assert.That(cycle.GetCooldownRemaining(0f), Is.Zero);
                Assert.That(cycle.Tick(0f, true), Is.False);
                Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Awaking));
                Assert.That(cycle.GetCooldownRemaining(0f), Is.EqualTo(preset[0]).Within(EPSILON));
                Assert.That(cycle.Tick(halfwayAwaking, true), Is.False);
                Assert.That(cycle.GetCooldownRemaining(halfwayAwaking), Is.EqualTo(halfwayAwaking).Within(EPSILON));
                Assert.That(cycle.Tick(preset[0], true), Is.True);
                Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
                Assert.That(cycle.GetCooldownRemaining(preset[0]), Is.EqualTo(preset[3]).Within(EPSILON));
                Assert.That(cycle.Tick(halfwayBlowout, true), Is.False);
                Assert.That(cycle.GetCooldownRemaining(halfwayBlowout), Is.EqualTo(preset[3] - preset[1] * 0.5f).Within(EPSILON));
            }
        }

        [Test]
        public void ExactBoundariesAdvanceWithoutEarlyDischarge()
        {
            var cycle = new AnomalyCycle(0.125f, 0.25f, 0.5f);
            Assert.That(cycle.Tick(2f, true), Is.False);
            Assert.That(cycle.Tick(2.124f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Awaking));
            Assert.That(cycle.Tick(2.125f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            Assert.That(cycle.Tick(2.374f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            Assert.That(cycle.Tick(2.375f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
            Assert.That(cycle.Tick(2.874f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
            Assert.That(cycle.Tick(2.875f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
        }

        [Test]
        public void LeavingDuringAwakingDoesNotCancelCommittedDischarge()
        {
            var cycle = new AnomalyCycle(0.125f, 0.25f, 0.5f);
            Assert.That(cycle.Tick(0f, true), Is.False);
            Assert.That(cycle.Tick(0.0625f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Awaking));
            Assert.That(cycle.Tick(0.125f, false), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
        }

        [Test]
        public void EmptyZoneWaitsFullBlowoutAndAccumulateDurations()
        {
            var cycle = new AnomalyCycle(0.125f, 0.25f, 0.5f);
            cycle.RecordDischarge(0f);
            Assert.That(cycle.Tick(0.249f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            Assert.That(cycle.Tick(0.25f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
            Assert.That(cycle.GetCooldownRemaining(0.25f), Is.EqualTo(0.5f).Within(EPSILON));
            Assert.That(cycle.Tick(0.749f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
            Assert.That(cycle.Tick(0.75f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Idle));
            Assert.That(cycle.GetCooldownRemaining(0.75f), Is.Zero);
        }

        [Test]
        public void ContactReentryDoesNotSkipOrRestartTheCurrentCycle()
        {
            var cycle = new AnomalyCycle(0.125f, 0.25f, 0.5f);
            cycle.RecordDischarge(0f);
            Assert.That(cycle.Tick(0.0625f, false), Is.False);
            Assert.That(cycle.Tick(0.125f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            Assert.That(cycle.GetCooldownRemaining(0.125f), Is.EqualTo(0.625f).Within(EPSILON));
            Assert.That(cycle.Tick(0.25f, false), Is.False);
            Assert.That(cycle.Tick(0.5f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
            Assert.That(cycle.GetCooldownRemaining(0.5f), Is.EqualTo(0.25f).Within(EPSILON));
            Assert.That(cycle.Tick(0.75f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
        }

        [Test]
        public void OccupiedZoneRepeatsWithoutAnotherAwakingPhase()
        {
            foreach (float[] preset in _sourcePresets)
            {
                var cycle = new AnomalyCycle(preset[0], preset[1], preset[2]);
                cycle.RecordDischarge(0f);
                Assert.That(cycle.Tick(preset[1], true), Is.False);
                Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
                Assert.That(cycle.Tick(preset[3], true), Is.True);
                Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
                Assert.That(cycle.GetCooldownRemaining(preset[3]), Is.EqualTo(preset[3]).Within(EPSILON));
                Assert.That(cycle.Tick(preset[3] + preset[1], true), Is.False);
                Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
                Assert.That(cycle.Tick(preset[3] * 2f, true), Is.True);
                Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            }
        }

        [Test]
        public void LargeTicksAdvanceOnePhaseAndStartItsClockAtNow()
        {
            var cycle = new AnomalyCycle(0.125f, 0.25f, 0.5f);
            Assert.That(cycle.Tick(0f, true), Is.False);
            Assert.That(cycle.Tick(100f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            Assert.That(cycle.GetCooldownRemaining(100f), Is.EqualTo(0.75f).Within(EPSILON));
            Assert.That(cycle.Tick(100.125f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            Assert.That(cycle.Tick(200f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
            Assert.That(cycle.GetCooldownRemaining(200f), Is.EqualTo(0.5f).Within(EPSILON));
            Assert.That(cycle.Tick(200f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
            Assert.That(cycle.Tick(300f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            Assert.That(cycle.GetCooldownRemaining(300f), Is.EqualTo(0.75f).Within(EPSILON));
        }

        [Test]
        public void ManualDischargeReplacesAwakingAndRestartsBlowout()
        {
            var cycle = new AnomalyCycle(0.125f, 0.25f, 0.5f);
            cycle.Tick(0f, true);
            cycle.RecordDischarge(0.0625f);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            Assert.That(cycle.GetCooldownRemaining(0.0625f), Is.EqualTo(0.75f).Within(EPSILON));
            Assert.That(cycle.Tick(0.125f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            cycle.RecordDischarge(10f);
            Assert.That(cycle.GetCooldownRemaining(10f), Is.EqualTo(0.75f).Within(EPSILON));
            Assert.That(cycle.Tick(10.25f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
            Assert.That(cycle.GetCooldownRemaining(10.25f), Is.EqualTo(0.5f).Within(EPSILON));
        }

        [Test]
        public void ResetClearsEveryActivePhaseAndPendingDischarge()
        {
            var awaking = new AnomalyCycle(0.125f, 0.25f, 0.5f);
            awaking.Tick(0f, true);
            var blowout = new AnomalyCycle(0.125f, 0.25f, 0.5f);
            blowout.RecordDischarge(0f);
            var accumulate = new AnomalyCycle(0.125f, 0.25f, 0.5f);
            accumulate.RecordDischarge(0f);
            accumulate.Tick(0.25f, false);

            foreach (AnomalyCycle cycle in new[] { awaking, blowout, accumulate })
            {
                Assert.That(cycle.Phase, Is.Not.EqualTo(AnomalyPhase.Idle));
                cycle.Reset();
                Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Idle));
                Assert.That(cycle.GetCooldownRemaining(10f), Is.Zero);
                Assert.That(cycle.Tick(10f, false), Is.False);
                Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Idle));
                Assert.That(cycle.Tick(10f, true), Is.False);
                Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Awaking));
                Assert.That(cycle.GetCooldownRemaining(10f), Is.EqualTo(0.125f).Within(EPSILON));
            }
        }

        [Test]
        public void ZeroAndNegativeDurationsStillAdvanceOnePhasePerTick()
        {
            var cycle = new AnomalyCycle(-1f, 0f, -5f);
            Assert.That(cycle.Tick(5f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Awaking));
            Assert.That(cycle.GetCooldownRemaining(5f), Is.Zero);
            Assert.That(cycle.Tick(5f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Blowout));
            Assert.That(cycle.GetCooldownRemaining(5f), Is.Zero);
            Assert.That(cycle.Tick(5f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Accumulate));
            Assert.That(cycle.Tick(5f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(AnomalyPhase.Idle));
        }
    }
}
