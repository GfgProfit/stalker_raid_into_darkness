using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    public static class MonolithBallistics
    {
        private const float GRAVITY = 9.81f;
        private const float AIR_RESISTANCE = 1f;
        private const float AIR_RESISTANCE_EPSILON = 0.1f;
        private const float VERTICAL_EPSILON = 0.0000001f;

        public static Vector3 Position(Vector3 origin, Vector3 velocity, float time)
        {
            time = Mathf.Max(0f, time);
            float parabolicTime = Mathf.Max(0f, 1f / AIR_RESISTANCE - AIR_RESISTANCE_EPSILON);

            if (time < parabolicTime)
            {
                float resistance = velocity.x * velocity.x + velocity.z * velocity.z > VERTICAL_EPSILON ? AIR_RESISTANCE : 0f;
                return origin + velocity * time + (Vector3.down * GRAVITY - velocity * resistance) * (time * time * 0.5f);
            }

            Vector3 position = origin + velocity * parabolicTime + (Vector3.down * GRAVITY - velocity * AIR_RESISTANCE) * (parabolicTime * parabolicTime * 0.5f);
            Vector3 fallingVelocity = ParabolicVelocity(velocity, parabolicTime);
            float fallingTime = time - parabolicTime;
            return position + fallingVelocity * fallingTime + Vector3.down * (GRAVITY * fallingTime * fallingTime * 0.5f);
        }

        public static Vector3 Velocity(Vector3 velocity, float time)
        {
            time = Mathf.Max(0f, time);
            float parabolicTime = Mathf.Max(0f, 2f / AIR_RESISTANCE - AIR_RESISTANCE_EPSILON);

            if (time < parabolicTime)
            {
                return velocity.x * velocity.x + velocity.z * velocity.z > VERTICAL_EPSILON ? ParabolicVelocity(velocity, time) : velocity + Vector3.down * (GRAVITY * time);
            }

            return ParabolicVelocity(velocity, parabolicTime) + Vector3.down * (GRAVITY * (time - parabolicTime));
        }

        private static Vector3 ParabolicVelocity(Vector3 velocity, float time)
        {
            return velocity * Mathf.Max(0f, 1f - AIR_RESISTANCE * time) + Vector3.down * (GRAVITY * time);
        }
    }
}
