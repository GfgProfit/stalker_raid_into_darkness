using System;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class FiniteParticleFrameTests
    {
        private static Func<float, uint, int, float, bool, bool, bool, float> Optimized()
        {
            var type = typeof(AnomalyParticleFrames).Assembly.GetType("RaidIntoDarkness.Effects.FiniteParticleFrameCache", true);
            return (Func<float, uint, int, float, bool, bool, bool, float>)Delegate.CreateDelegate(typeof(Func<float, uint, int, float, bool, bool, bool, float>), type.GetMethod("Evaluate", BindingFlags.NonPublic | BindingFlags.Static));
        }

        private static void Check(Func<float, uint, int, float, bool, bool, bool, float> optimized, float age, uint seed, int count, float fps, bool animated, bool start, bool playback)
        {
            float expected = FiniteParticleFrameOracle.EvaluateFrame(age, seed, count, fps, animated, start, playback);
            float actual = optimized(age, seed, count, fps, animated, start, playback);
            Assert.That(BitConverter.SingleToInt32Bits(actual), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)), age + "/" + seed + "/" + count + "/" + fps + "/" + animated + "/" + start + "/" + playback);
            Assert.That(Mathf.Floor(actual), Is.EqualTo(Mathf.Floor(expected)));
        }

        [Test]
        public void SourceBoundariesFlagsDirectionsAndFractionalFramesMatchIndependentOriginal()
        {
            var optimized = Optimized();
            var counts = new[] { 0, 1, 2, 8, 16, 25, 32, 64, 66, 125, 128, 129, 257 };
            var rates = new[] { 0f, 0.5f, 4f, 12f, 30f, 127.25f, 10000f };
            var seeds = new[] { 0u, 1u, 2u, 42u, 1234567u, uint.MaxValue };
            foreach (int count in counts)
            {
                foreach (float fps in rates)
                {
                    for (int flags = 0; flags < 8; flags++)
                    {
                        foreach (uint seed in seeds)
                        {
                            foreach (int step in new[] { 0, 1, 2, 7, 8, 9, 31, 32, 33, 100 })
                            {
                                float time = step * 0.033f;
                                Check(optimized, time, seed, count, fps, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
                                Check(optimized, time == 0 ? -float.Epsilon : BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(time) - 1), seed, count, fps, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
                                Check(optimized, BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(time) + 1), seed, count, fps, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void AgeReversalDuplicateSeedsReuseJumpsAndCacheEvictionDoNotChangeSequence()
        {
            var optimized = Optimized();
            for (int configuration = 0; configuration < 80; configuration++)
            {
                foreach (float age in new[] { 0f, 0.02f, 1f, 50f, 0.02f, 140f, 1f, 0f })
                {
                    foreach (uint seed in new[] { 42u, 1u, 42u, uint.MaxValue, 1u })
                    {
                        Check(optimized, age, seed, 16, configuration * 0.25f, true, true, true);
                    }
                }
            }
        }

        [Test]
        public void ReproducibleMixedInputQueriesRetainExactOriginalFloatBits()
        {
            var optimized = Optimized();
            var random = new System.Random(73199);
            for (int index = 0; index < 2500; index++)
            {
                Check(optimized, (float)(random.NextDouble() * 40), (uint)random.Next(), random.Next(1, 66), random.Next(0, 129) * 0.125f, true, index % 3 != 0, index % 2 == 0);
            }
        }
    }
}
