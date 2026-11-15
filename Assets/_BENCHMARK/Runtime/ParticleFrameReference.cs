using UnityEngine;

namespace Rvm.Benchmark
{
    public static class ParticleFrameReference
    {
        private const float SOURCE_STEP = 0.033f;
        private const float FRAME_PRECISION = 255f;

        public static float EvaluateFrame(float age, uint randomSeed, int frameCount, float framesPerSecond, bool animated, bool randomStart, bool randomPlayback)
        {
            int count = Mathf.Max(1, frameCount);
            uint random = unchecked(randomSeed * 747796405u + 2891336453u);
            random ^= random >> 16;
            float frame = randomStart ? random % (uint)count : 0f;
            if (animated == false)
            {
                return frame;
            }
            bool backward = randomPlayback && (random & 0x80000000u) != 0u;
            int steps = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(0f, age) / SOURCE_STEP) + 1);
            for (int index = 0; index < steps; index++)
            {
                frame = AdvanceFrame(frame, framesPerSecond, count, backward);
            }
            return frame;
        }

        public static float AdvanceFrame(float frame, float framesPerSecond, int frameCount, bool backward)
        {
            float value = frame + (backward ? -1f : 1f) * framesPerSecond * SOURCE_STEP;
            int count = Mathf.Max(1, frameCount);
            if (value > count)
            {
                value -= count;
            }
            if (value < 0f)
            {
                value += count;
            }
            return (ushort)Mathf.FloorToInt(value * FRAME_PRECISION) / FRAME_PRECISION;
        }

    }
}
