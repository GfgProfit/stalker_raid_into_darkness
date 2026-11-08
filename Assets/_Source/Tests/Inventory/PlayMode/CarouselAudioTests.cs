using NUnit.Framework;
using RaidIntoDarkness.Effects;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class CarouselAudioTests
    {
        [Test]
        public void SourceGainPreservesPolarityAndSamplesAboveUnityVolumeLimit()
        {
            float[] samples = { -0.4f, 0f, 0.25f, 0.7f };
            AnomalyAudioGain.Apply(samples, 2f);
            Assert.That(samples, Is.EqualTo(new[] { -0.8f, 0f, 0.5f, 1.4f }).Within(0.000001f));
        }

        [Test]
        public void UnitGainIsUnchangedAndZeroGainSilencesEveryChannel()
        {
            float[] samples = { -0.4f, 0.25f, 0.5f, -0.7f };
            AnomalyAudioGain.Apply(samples, 1f);
            Assert.That(samples, Is.EqualTo(new[] { -0.4f, 0.25f, 0.5f, -0.7f }));
            AnomalyAudioGain.Apply(samples, 0f);
            Assert.That(samples, Is.All.EqualTo(0f));
            Assert.DoesNotThrow(() => AnomalyAudioGain.Apply(null, 2f));
            Assert.DoesNotThrow(() => AnomalyAudioGain.Apply(new float[0], 2f));
        }
    }
}
