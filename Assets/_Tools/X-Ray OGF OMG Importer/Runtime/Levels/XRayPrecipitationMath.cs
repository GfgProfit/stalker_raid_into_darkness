using UnityEngine;

namespace XRay.Importer.Levels
{
    public static class XRayPrecipitationMath
    {
        public const float SOURCE_HEIGHT = 20.0f;
        public const float SOURCE_MAX_DISTANCE = 30.0f;
        public const float SOURCE_SPLASH_SECONDS = 0.3f;
        public const float SOURCE_MIN_SPEED = 40.0f;
        public const float SOURCE_MAX_SPEED = 80.0f;

        private const float MIN_DENSITY = 0.0001f;
        private const float MAX_TILT_DEGREES = 35.0f;

        public static int DropCount(float density, int budget)
        {
            if (!float.IsFinite(density) || density < MIN_DENSITY || budget <= 0) return 0;
            return Mathf.FloorToInt(0.01f * (1.0f + 99.0f * Mathf.Clamp01(density)) * budget);
        }

        public static Vector3 FallDirection(float windDirection, float windVelocity, float windNoise, float response)
        {
            float wind = Mathf.Clamp01(Mathf.Max(0.0f, windVelocity) * 0.001f);
            float tilt = MAX_TILT_DEGREES * Mathf.Deg2Rad * Mathf.Clamp01((wind + windNoise * 2.0f) * Mathf.Max(0.0f, response));
            float horizontal = Mathf.Sin(tilt);
            return new Vector3(horizontal * Mathf.Sin(windDirection), -Mathf.Cos(tilt), horizontal * Mathf.Cos(windDirection));
        }

        public static float SpeedFactor(float windVelocity, float speed)
        {
            return Mathf.Max(0.0f, speed) * Mathf.Clamp(Mathf.Clamp01(windVelocity * 0.001f) * 1.5f, 0.5f, 1.0f);
        }
    }
}
