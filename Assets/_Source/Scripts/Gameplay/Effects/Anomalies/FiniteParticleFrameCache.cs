using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    internal static class FiniteParticleFrameCache
    {
        private const int MAX_CONFIGURATIONS = 16;
        private const int MAX_FRAME_COUNT = 128;
        private const int CHECKPOINT_STRIDE = 8;
        private const int MAX_CHECKPOINT = 512;

        private static readonly Dictionary<(int, float), SequenceSet> CACHE = new Dictionary<(int, float), SequenceSet>(MAX_CONFIGURATIONS);

        private static long _clock;

        internal static bool UseReference { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            CACHE.Clear();
            _clock = 0;
            UseReference = false;
        }

        internal static float Evaluate(float age, uint randomSeed, int frameCount, float framesPerSecond, bool animated, bool randomStart, bool randomPlayback)
        {
            int count = Mathf.Max(1, frameCount);
            if (UseReference || !animated || count > MAX_FRAME_COUNT || float.IsNaN(framesPerSecond) || float.IsInfinity(framesPerSecond) || framesPerSecond < 0)
            {
                return SpatialBubbleParticleFrames.EvaluateFrame(age, randomSeed, frameCount, framesPerSecond, animated, randomStart, randomPlayback);
            }
            uint random = unchecked(randomSeed * 747796405u + 2891336453u);
            random ^= random >> 16;
            int start = randomStart ? (int)(random % (uint)count) : 0;
            bool backward = randomPlayback && (random & 0x80000000u) != 0u;
            int steps = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(0f, age) / 0.033f) + 1);
            var key = (count, framesPerSecond);
            if (!CACHE.TryGetValue(key, out var sequences))
            {
                if (CACHE.Count == MAX_CONFIGURATIONS)
                {
                    var oldestKey = default((int, float));
                    long oldest = long.MaxValue;
                    foreach (var pair in CACHE)
                    {
                        if (pair.Value.LastUse < oldest)
                        {
                            oldest = pair.Value.LastUse;
                            oldestKey = pair.Key;
                        }
                    }
                    CACHE.Remove(oldestKey);
                }
                sequences = new SequenceSet(count);
                CACHE.Add(key, sequences);
            }
            sequences.LastUse = ++_clock;
            return sequences.Evaluate(start, backward, steps, count, framesPerSecond);
        }

        private sealed class SequenceSet
        {
            private readonly float[][] _checkpoints;
            private readonly int[] _blocks;

            public long LastUse { get; set; }

            public SequenceSet(int count)
            {
                _checkpoints = new float[count * 2][];
                _blocks = new int[count * 2];
            }

            public float Evaluate(int start, bool backward, int steps, int count, float fps)
            {
                int origin = start + (backward ? count : 0);
                var checkpoints = _checkpoints[origin];
                if (checkpoints == null)
                {
                    checkpoints = new float[MAX_CHECKPOINT + 1];
                    checkpoints[0] = start;
                    _checkpoints[origin] = checkpoints;
                }
                int block = Math.Min(MAX_CHECKPOINT, steps / CHECKPOINT_STRIDE);
                for (int next = _blocks[origin] + 1; next <= block; next++)
                {
                    float value = checkpoints[next - 1];
                    for (int step = 0; step < CHECKPOINT_STRIDE; step++)
                    {
                        value = SpatialBubbleParticleFrames.AdvanceFrame(value, fps, count, backward);
                    }
                    checkpoints[next] = value;
                    _blocks[origin] = next;
                }
                float frame = checkpoints[block];
                for (int step = block * CHECKPOINT_STRIDE; step < steps; step++)
                {
                    frame = SpatialBubbleParticleFrames.AdvanceFrame(frame, fps, count, backward);
                }
                return frame;
            }
        }
    }
}
