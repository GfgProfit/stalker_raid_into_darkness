using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    public static class AnomalyParticleNoise
    {
        private const int TABLE_SIZE = 256;
        private const int TABLE_MASK = TABLE_SIZE - 1;
        private const float COORDINATE_OFFSET = 10000f;
        private const float OCTAVE_SCALE = 2.059f;

        private static readonly int[] PERMUTATIONS = new int[TABLE_SIZE * 2 + 2];
        private static readonly Vector3[] GRADIENTS = new Vector3[TABLE_SIZE * 2 + 2];

        static AnomalyParticleNoise()
        {
            uint randomState = 1u;
            for (int index = 0; index < TABLE_SIZE; index++)
            {
                Vector3 value;
                float squared;
                do
                {
                    value = new Vector3((Next(ref randomState) % 512 - TABLE_SIZE) / (float)TABLE_SIZE, (Next(ref randomState) % 512 - TABLE_SIZE) / (float)TABLE_SIZE, (Next(ref randomState) % 512 - TABLE_SIZE) / (float)TABLE_SIZE);
                    squared = value.sqrMagnitude;
                }
                while (squared > 1f);
                float magnitude = Mathf.Sqrt(squared);
                GRADIENTS[index] = new Vector3(value.x / magnitude, value.y / magnitude, value.z / magnitude);
                PERMUTATIONS[index] = index;
            }
            for (int index = TABLE_SIZE; index > 0; index -= 2)
            {
                int other = Next(ref randomState) % TABLE_SIZE;
                int value = PERMUTATIONS[index];
                PERMUTATIONS[index] = PERMUTATIONS[other];
                PERMUTATIONS[other] = value;
            }
            for (int index = 0; index < TABLE_SIZE + 2; index++)
            {
                PERMUTATIONS[TABLE_SIZE + index] = PERMUTATIONS[index];
                GRADIENTS[TABLE_SIZE + index] = GRADIENTS[index];
            }
        }

        public static float Sample(Vector3 position)
        {
            Vector3 shifted = position + Vector3.one * COORDINATE_OFFSET;
            int x = (int)shifted.x;
            int y = (int)shifted.y;
            int z = (int)shifted.z;
            int x0 = x & TABLE_MASK;
            int y0 = y & TABLE_MASK;
            int z0 = z & TABLE_MASK;
            int x1 = (x0 + 1) & TABLE_MASK;
            int y1 = (y0 + 1) & TABLE_MASK;
            int z1 = (z0 + 1) & TABLE_MASK;
            float rx0 = shifted.x - x;
            float ry0 = shifted.y - y;
            float rz0 = shifted.z - z;
            float rx1 = rx0 - 1f;
            float ry1 = ry0 - 1f;
            float rz1 = rz0 - 1f;
            int left = PERMUTATIONS[x0];
            int right = PERMUTATIONS[x1];
            int b00 = PERMUTATIONS[left + y0];
            int b10 = PERMUTATIONS[right + y0];
            int b01 = PERMUTATIONS[left + y1];
            int b11 = PERMUTATIONS[right + y1];
            float sx = Curve(rx0);
            float sy = Curve(ry0);
            float sz = Curve(rz0);
            float a = Lerp(sx, Dot(rx0, ry0, rz0, GRADIENTS[b00 + z0]), Dot(rx1, ry0, rz0, GRADIENTS[b10 + z0]));
            float b = Lerp(sx, Dot(rx0, ry1, rz0, GRADIENTS[b01 + z0]), Dot(rx1, ry1, rz0, GRADIENTS[b11 + z0]));
            float c = Lerp(sy, a, b);
            a = Lerp(sx, Dot(rx0, ry0, rz1, GRADIENTS[b00 + z1]), Dot(rx1, ry0, rz1, GRADIENTS[b10 + z1]));
            b = Lerp(sx, Dot(rx0, ry1, rz1, GRADIENTS[b01 + z1]), Dot(rx1, ry1, rz1, GRADIENTS[b11 + z1]));
            return 1.5f * Lerp(sz, c, Lerp(sy, a, b));
        }

        public static float Fractal(Vector3 position, float frequency, int octaves)
        {
            if (frequency == 0f || octaves <= 0)
            {
                return 0f;
            }
            float boost = frequency;
            float sum = 0f;
            for (int index = 0; index < octaves; index++)
            {
                sum += Sample(position * frequency) / frequency;
                frequency *= OCTAVE_SCALE;
            }
            return sum * boost;
        }

        private static int Next(ref uint state)
        {
            state = unchecked(state * 214013u + 2531011u);
            return (int)((state >> 16) & 32767u);
        }

        private static float Curve(float value)
        {
            return value * value * (3f - 2f * value);
        }

        private static float Lerp(float amount, float from, float to)
        {
            return from + amount * (to - from);
        }

        private static float Dot(float x, float y, float z, Vector3 gradient)
        {
            return x * gradient.x + y * gradient.y + z * gradient.z;
        }
    }
}
