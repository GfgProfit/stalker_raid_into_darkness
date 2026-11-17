using System;
using NUnit.Framework;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.GraphicsOptions.Tests
{
    public sealed class VideoShadowCapabilityTests
    {
        [Test]
        public void LocalShadowsRemainOptionalAndDoNotChangeTheUserOverride()
        {
            Func<float?> previous = XRayVideoOverrides.SetPunctualShadowDefaultsProvider(null);
            float? userDistance = XRayVideoOverrides.PunctualShadowDistance;
            bool? lensFlare = XRayVideoOverrides.LensFlare;
            int? fogQuality = XRayVideoOverrides.FogQuality;
            float lodBias = XRayVideoOverrides.LodBias;
            try
            {
                Assert.That(XRayVideoOverrides.DefaultPunctualShadowDistance, Is.Null);
                float? configuredDistance = 75.0f;
                XRayVideoOverrides.SetPunctualShadowDefaultsProvider(() => configuredDistance);
                XRayVideoOverrides.Apply(35.0f, lensFlare, fogQuality, lodBias);
                Assert.That(XRayVideoOverrides.DefaultPunctualShadowDistance, Is.EqualTo(75.0f));
                Assert.That(XRayVideoOverrides.PunctualShadowDistance, Is.EqualTo(35.0f));
                configuredDistance = 50.0f;
                Assert.That(XRayVideoOverrides.DefaultPunctualShadowDistance, Is.EqualTo(50.0f));
                configuredDistance = null;
                Assert.That(XRayVideoOverrides.DefaultPunctualShadowDistance, Is.Null);
                Assert.That(XRayVideoOverrides.PunctualShadowDistance, Is.EqualTo(35.0f));
            }
            finally
            {
                XRayVideoOverrides.SetPunctualShadowDefaultsProvider(previous);
                XRayVideoOverrides.Apply(userDistance, lensFlare, fogQuality, lodBias);
            }
        }

        [TestCase(0.0f)]
        [TestCase(-1.0f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidLocalShadowDefaultsAreUnavailable(float distance)
        {
            Func<float?> previous = XRayVideoOverrides.SetPunctualShadowDefaultsProvider(() => distance);
            try
            {
                Assert.That(XRayVideoOverrides.DefaultPunctualShadowDistance, Is.Null);
            }
            finally
            {
                XRayVideoOverrides.SetPunctualShadowDefaultsProvider(previous);
            }
        }
    }
}
