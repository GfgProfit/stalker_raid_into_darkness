using NUnit.Framework;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class ElectraCycleTests
    {
        private const float EPSILON = 0.00001f;

        [Test]
        public void DefaultDurationsExposeExpectedRemainingTimes()
        {
            var cycle = new ElectraCycle();
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Idle));
            Assert.That(cycle.GetCooldownRemaining(0f), Is.Zero);
            Assert.That(cycle.Tick(0f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Awaking));
            Assert.That(cycle.GetCooldownRemaining(0f), Is.EqualTo(0.1f).Within(EPSILON));
            Assert.That(cycle.Tick(0.05f, true), Is.False);
            Assert.That(cycle.GetCooldownRemaining(0.05f), Is.EqualTo(0.05f).Within(EPSILON));
            Assert.That(cycle.Tick(0.1f, true), Is.True);
            Assert.That(cycle.GetCooldownRemaining(0.1f), Is.EqualTo(1.6f).Within(EPSILON));
            Assert.That(cycle.Tick(0.15f, true), Is.False);
            Assert.That(cycle.GetCooldownRemaining(0.15f), Is.EqualTo(1.55f).Within(EPSILON));
            Assert.That(cycle.Tick(0.2f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Accumulate));
            Assert.That(cycle.GetCooldownRemaining(0.2f), Is.EqualTo(1.5f).Within(EPSILON));
            Assert.That(cycle.Tick(0.7f, true), Is.False);
            Assert.That(cycle.GetCooldownRemaining(0.7f), Is.EqualTo(1f).Within(EPSILON));
        }

        [Test]
        public void ExactBoundariesAdvanceWithoutFiringBeforeAwakingCompletes()
        {
            var cycle = new ElectraCycle(0.125f, 0.25f, 0.5f);
            Assert.That(cycle.Tick(2f, true), Is.False);
            Assert.That(cycle.Tick(2.124f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Awaking));
            Assert.That(cycle.Tick(2.125f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Blowout));
            Assert.That(cycle.Tick(2.374f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Blowout));
            Assert.That(cycle.Tick(2.375f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Accumulate));
            Assert.That(cycle.Tick(2.874f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Accumulate));
            Assert.That(cycle.Tick(2.875f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Blowout));
        }

        [Test]
        public void LeavingDuringAwakingDoesNotCancelCommittedDischarge()
        {
            var cycle = new ElectraCycle();
            Assert.That(cycle.Tick(0f, true), Is.False);
            Assert.That(cycle.Tick(0.05f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Awaking));
            Assert.That(cycle.Tick(0.1f, false), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Blowout));
        }

        [Test]
        public void AccumulateWaitsFullDurationWithoutContact()
        {
            var cycle = new ElectraCycle();
            cycle.RecordDischarge(0f);
            Assert.That(cycle.Tick(0.1f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Accumulate));
            Assert.That(cycle.Tick(1.59f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Accumulate));
            Assert.That(cycle.GetCooldownRemaining(1.59f), Is.EqualTo(0.01f).Within(EPSILON));
            Assert.That(cycle.Tick(1.6f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Idle));
            Assert.That(cycle.GetCooldownRemaining(1.6f), Is.Zero);
        }

        [Test]
        public void ContactReentryDuringAccumulationDoesNotRestartDelay()
        {
            var cycle = new ElectraCycle(0.125f, 0.25f, 1.5f);
            cycle.RecordDischarge(0f);
            Assert.That(cycle.Tick(0.25f, false), Is.False);
            Assert.That(cycle.Tick(0.75f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Accumulate));
            Assert.That(cycle.GetCooldownRemaining(0.75f), Is.EqualTo(1f).Within(EPSILON));
            Assert.That(cycle.Tick(1.749f, true), Is.False);
            Assert.That(cycle.Tick(1.75f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Blowout));
        }

        [Test]
        public void LargeTicksAdvanceOnePhaseAndRestartItsClockAtNow()
        {
            var cycle = new ElectraCycle();
            Assert.That(cycle.Tick(0f, true), Is.False);
            Assert.That(cycle.Tick(100f, true), Is.True);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Blowout));
            Assert.That(cycle.GetCooldownRemaining(100f), Is.EqualTo(1.6f).Within(EPSILON));
            Assert.That(cycle.Tick(100.05f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Blowout));
            Assert.That(cycle.Tick(200f, true), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Accumulate));
            Assert.That(cycle.GetCooldownRemaining(200f), Is.EqualTo(1.5f).Within(EPSILON));
            Assert.That(cycle.Tick(200f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Accumulate));
            Assert.That(cycle.Tick(300f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Idle));
        }

        [Test]
        public void ManualDischargeReplacesAwakingAndRestartsBlowout()
        {
            var cycle = new ElectraCycle();
            cycle.Tick(0f, true);
            cycle.RecordDischarge(0.05f);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Blowout));
            Assert.That(cycle.GetCooldownRemaining(0.05f), Is.EqualTo(1.6f).Within(EPSILON));
            Assert.That(cycle.Tick(0.1f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Blowout));
            cycle.RecordDischarge(10f);
            Assert.That(cycle.GetCooldownRemaining(10f), Is.EqualTo(1.6f).Within(EPSILON));
            Assert.That(cycle.Tick(10.125f, false), Is.False);
            Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Accumulate));
            Assert.That(cycle.GetCooldownRemaining(10.125f), Is.EqualTo(1.5f).Within(EPSILON));
        }

        [Test]
        public void ResetClearsEveryActivePhaseAndPendingDischarge()
        {
            var awaking = new ElectraCycle();
            awaking.Tick(0f, true);
            var blowout = new ElectraCycle();
            blowout.RecordDischarge(0f);
            var accumulate = new ElectraCycle();
            accumulate.RecordDischarge(0f);
            accumulate.Tick(0.1f, false);

            foreach (ElectraCycle cycle in new[] { awaking, blowout, accumulate })
            {
                Assert.That(cycle.Phase, Is.Not.EqualTo(ElectraPhase.Idle));
                cycle.Reset();
                Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Idle));
                Assert.That(cycle.GetCooldownRemaining(2f), Is.Zero);
                Assert.That(cycle.Tick(2f, false), Is.False);
                Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Idle));
                Assert.That(cycle.Tick(2f, true), Is.False);
                Assert.That(cycle.Phase, Is.EqualTo(ElectraPhase.Awaking));
                Assert.That(cycle.GetCooldownRemaining(2f), Is.EqualTo(0.1f).Within(EPSILON));
            }
        }
    }
}
