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
        private const float SOURCE_STEP = 0.033f;

        private static readonly Dictionary<(int, float), SequenceSet> CACHE = new Dictionary<(int, float), SequenceSet>(MAX_CONFIGURATIONS);
        private static readonly Dictionary<(int, float, float), Texture2D> SHADER_LOOKUPS = new(MAX_CONFIGURATIONS);

        private static long _clock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            CACHE.Clear();
            foreach (Texture2D lookup in SHADER_LOOKUPS.Values)
            {
                if (lookup != null) UnityEngine.Object.Destroy(lookup);
            }
            SHADER_LOOKUPS.Clear();
            _clock = 0;
        }

        internal static Texture2D ResolveShaderLookup(int count, float framesPerSecond, float lifetime)
        {
            if (count < 1 || count > MAX_FRAME_COUNT || !float.IsFinite(framesPerSecond) || framesPerSecond < 0f || (lifetime != 1f && lifetime != 2f && lifetime != 5f)) return null;
            var key = (count, framesPerSecond, lifetime);
            if (SHADER_LOOKUPS.TryGetValue(key, out Texture2D lookup)) return lookup;
            if (SHADER_LOOKUPS.Count == MAX_CONFIGURATIONS || !SystemInfo.SupportsTextureFormat(TextureFormat.RGFloat)) return null;

            int length = Mathf.FloorToInt(lifetime / SOURCE_STEP) + 1;
            var entries = new Vector2[length];
            float frame = 0f;
            for (int index = 0; index < length; index++)
            {
                float age = index * SOURCE_STEP;
                if (index > 0)
                {
                    int bits = BitConverter.SingleToInt32Bits(age);
                    while (Mathf.FloorToInt(BitConverter.Int32BitsToSingle(bits) / SOURCE_STEP) < index) bits++;
                    while (Mathf.FloorToInt(BitConverter.Int32BitsToSingle(bits - 1) / SOURCE_STEP) >= index) bits--;
                    age = BitConverter.Int32BitsToSingle(bits);
                }
                frame = SpatialBubbleParticleFrames.AdvanceFrame(frame, framesPerSecond, count, false);
                entries[index] = new Vector2(age, Mathf.Floor(frame));
            }
            lookup = new Texture2D(length, 1, TextureFormat.RGFloat, false, true)
            {
                name = "X-Ray Particle Frame Lookup",
                hideFlags = HideFlags.HideAndDontSave
            };
            lookup.SetPixelData(entries, 0);
            lookup.Apply(false, true);
            SHADER_LOOKUPS.Add(key, lookup);
            return lookup;
        }

        internal static float Evaluate(float age, uint randomSeed, int frameCount, float framesPerSecond, bool animated, bool randomStart, bool randomPlayback)
        {
            SequenceSet sequences = Resolve(frameCount, framesPerSecond, animated);
            return sequences != null ? sequences.Evaluate(age, randomSeed, randomStart, randomPlayback) : SpatialBubbleParticleFrames.EvaluateFrame(age, randomSeed, frameCount, framesPerSecond, animated, randomStart, randomPlayback);
        }

        internal static SequenceSet Resolve(int frameCount, float framesPerSecond, bool animated)
        {
            int count = Mathf.Max(1, frameCount);
            if (!animated || count > MAX_FRAME_COUNT || float.IsNaN(framesPerSecond) || float.IsInfinity(framesPerSecond) || framesPerSecond < 0)
            {
                return null;
            }
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
                sequences = new SequenceSet(count, framesPerSecond);
                CACHE.Add(key, sequences);
            }
            sequences.LastUse = ++_clock;
            return sequences;
        }

        internal sealed class SequenceSet
        {
            private readonly float[][] _checkpoints;
            private readonly int[] _blocks;
            private readonly int _count;
            private readonly float _fps;

            public long LastUse { get; set; }

            public SequenceSet(int count, float fps)
            {
                _checkpoints = new float[count * 2][];
                _blocks = new int[count * 2];
                _count = count;
                _fps = fps;
            }

            public float Evaluate(float age, uint randomSeed, bool randomStart, bool randomPlayback)
            {
                uint random = unchecked(randomSeed * 747796405u + 2891336453u);
                random ^= random >> 16;
                int start = randomStart ? (int)(random % (uint)_count) : 0;
                bool backward = randomPlayback && (random & 0x80000000u) != 0u;
                int steps = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(0f, age) / SOURCE_STEP) + 1);
                int origin = start + (backward ? _count : 0);
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
                        value = SpatialBubbleParticleFrames.AdvanceFrame(value, _fps, _count, backward);
                    }
                    checkpoints[next] = value;
                    _blocks[origin] = next;
                }
                float frame = checkpoints[block];
                for (int step = block * CHECKPOINT_STRIDE; step < steps; step++)
                {
                    frame = SpatialBubbleParticleFrames.AdvanceFrame(frame, _fps, _count, backward);
                }
                return frame;
            }
        }
    }
}
