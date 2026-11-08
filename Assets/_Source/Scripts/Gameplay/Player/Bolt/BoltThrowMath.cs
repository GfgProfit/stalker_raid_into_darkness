using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public static class BoltThrowMath
    {
        public const float MIN_SPEED = 10f;
        public const float CONSTANT_SPEED = 20f;
        public const float MAX_SPEED = 20f;
        public const float CHARGE_RATE = 10f;
        public const float LIFETIME = 10000f;

        public static float ChargeSpeed(float seconds)
        {
            return Mathf.Clamp(MIN_SPEED + Mathf.Max(0f, seconds) * CHARGE_RATE, MIN_SPEED, MAX_SPEED);
        }

        public static Vector3 LaunchVelocity(Vector3 direction, float speed, Vector3 inheritedVelocity)
        {
            return direction.normalized * Mathf.Clamp(speed, MIN_SPEED, MAX_SPEED) + inheritedVelocity;
        }

        public static Vector3 LaunchPosition(Vector3 cameraPosition, Quaternion actorRotation)
        {
            return cameraPosition + actorRotation * new Vector3(0.4f, 0.3f, 0.1f);
        }
    }
}
