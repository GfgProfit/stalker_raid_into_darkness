using UnityEngine;

namespace RaidIntoDarkness.Weapons.Optics
{
    public static class ScopeOpticsMath
    {
        private const float MIN_EYE_DISTANCE = 0.001f;
        private const float MILLIRADIANS_TO_RADIANS = 0.001f;

        public static float FiniteClamp(float value, float minimum, float maximum, float fallback)
        {
            return Mathf.Clamp(float.IsFinite(value) ? value : fallback, minimum, maximum);
        }

        public static Vector2 FiniteVector(Vector2 value, Vector2 fallback)
        {
            return new Vector2(float.IsFinite(value.x) ? Mathf.Clamp(value.x, -1000f, 1000f) : fallback.x, float.IsFinite(value.y) ? Mathf.Clamp(value.y, -1000f, 1000f) : fallback.y);
        }

        public static float ClampMagnification(float value, float minimum, float maximum)
        {
            float safeMinimum = FiniteClamp(minimum, 0.1f, 100f, 1f);
            float safeMaximum = FiniteClamp(maximum, safeMinimum, 100f, safeMinimum);
            return FiniteClamp(value, safeMinimum, safeMaximum, safeMinimum);
        }

        public static float GetVerticalFieldOfView(ScopeOpticsConfig config, float magnification, float lensHalfHeightMeters, float eyeDistanceMeters)
        {
            float tangent = GetVerticalHalfAngleTangent(config, magnification, lensHalfHeightMeters, eyeDistanceMeters);
            return 2f * Mathf.Atan(tangent) * Mathf.Rad2Deg;
        }

        public static Matrix4x4 BuildProjection(ScopeOpticsConfig config, float magnification, Vector2 lensHalfSizeMeters, Vector3 eyeLocal, float near, float far)
        {
            float halfHeight = FiniteClamp(lensHalfSizeMeters.y, 0.0001f, 10f, 0.014f);
            float halfWidth = FiniteClamp(lensHalfSizeMeters.x, 0.0001f, 10f, halfHeight);
            float distance = FiniteClamp(-eyeLocal.z, MIN_EYE_DISTANCE, 100f, 0.09f);
            float verticalTangent = GetVerticalHalfAngleTangent(config, magnification, halfHeight, distance);
            float horizontalTangent = verticalTangent * halfWidth / halfHeight;
            float safeNear = FiniteClamp(near, 0.001f, 1000f, 0.01f);
            float safeFar = FiniteClamp(far, safeNear + 0.001f, 1000000f, 10000f);
            float angularMapping = verticalTangent / halfHeight;
            float centerX = -FiniteClamp(eyeLocal.x, -100f, 100f, 0f) * angularMapping;
            float centerY = -FiniteClamp(eyeLocal.y, -100f, 100f, 0f) * angularMapping;
            return Matrix4x4.Frustum((centerX - horizontalTangent) * safeNear, (centerX + horizontalTangent) * safeNear, (centerY - verticalTangent) * safeNear, (centerY + verticalTangent) * safeNear, safeNear, safeFar);
        }

        public static float GetReticleScale(ScopeOpticsConfig config, float magnification, float lensHalfHeightMeters, float eyeDistanceMeters)
        {
            if (config == null)
            {
                return 1f;
            }

            float reticleMagnification = config.ReticlePlane == ScopeReticlePlane.FirstFocalPlane ? magnification : config.CalibrationMagnification;
            float fieldTangent = GetVerticalHalfAngleTangent(config, reticleMagnification, lensHalfHeightMeters, eyeDistanceMeters);
            float calibrationTangent = GetVerticalHalfAngleTangent(config, config.CalibrationMagnification, lensHalfHeightMeters, eyeDistanceMeters);
            return FiniteClamp(calibrationTangent / fieldTangent, 0.000001f, 100000f, 1f);
        }

        public static Vector2 GetParallaxAngularError(Vector2 eyeOffsetMeters, float targetDistanceMeters, float parallaxDistanceMeters)
        {
            Vector2 offset = FiniteVector(eyeOffsetMeters, Vector2.zero);
            float target = FiniteClamp(targetDistanceMeters, 0.1f, 1000000f, 100f);
            float focus = FiniteClamp(parallaxDistanceMeters, 0.1f, 1000000f, 100f);
            return offset * (1f / target - 1f / focus);
        }

        public static Vector2 GetReticleParallaxOffset(ScopeOpticsConfig config, float magnification, Vector2 lensHalfSizeMeters, Vector3 eyeLocal, float focusDistanceMeters)
        {
            float focus = FiniteClamp(focusDistanceMeters, 0.1f, 1000000f, 100f);
            float eyeDistance = FiniteClamp(-eyeLocal.z, MIN_EYE_DISTANCE, 100f, 0.09f);
            Matrix4x4 projection = BuildProjection(config, magnification, lensHalfSizeMeters, eyeLocal, 0.01f, 10000000f);
            Vector4 clip = projection * new Vector4(-FiniteClamp(eyeLocal.x, -100f, 100f, 0f), -FiniteClamp(eyeLocal.y, -100f, 100f, 0f), -(focus + eyeDistance), 1f);
            return new Vector2(clip.x, clip.y) / (2f * clip.w);
        }

        public static float DegreesToMilliradians(float degrees)
        {
            return FiniteClamp(degrees, -360f, 360f, 0f) * Mathf.Deg2Rad / MILLIRADIANS_TO_RADIANS;
        }

        public static float MilliradiansToDegrees(float milliradians)
        {
            return FiniteClamp(milliradians, -100000f, 100000f, 0f) * MILLIRADIANS_TO_RADIANS * Mathf.Rad2Deg;
        }

        public static float MinutesOfAngleToMilliradians(float minutesOfAngle)
        {
            return DegreesToMilliradians(FiniteClamp(minutesOfAngle, -21600f, 21600f, 0f) / 60f);
        }

        private static float GetVerticalHalfAngleTangent(ScopeOpticsConfig config, float magnification, float lensHalfHeightMeters, float eyeDistanceMeters)
        {
            float halfHeight = FiniteClamp(lensHalfHeightMeters, 0.0001f, 10f, 0.014f);
            float distance = FiniteClamp(eyeDistanceMeters, MIN_EYE_DISTANCE, 100f, 0.09f);
            float safeMagnification = config != null ? ClampMagnification(magnification, config.MinMagnification, config.MaxMagnification) : ClampMagnification(magnification, 0.1f, 100f);
            float tangent;
            if (config != null && config.ProjectionMode == ScopeProjectionMode.CalibratedFieldOfView)
            {
                tangent = Mathf.Tan(0.5f * config.CalibrationVerticalFov * Mathf.Deg2Rad) * config.CalibrationMagnification / safeMagnification;
            }
            else
            {
                tangent = halfHeight / distance / safeMagnification;
            }

            float multiplier = config != null ? config.EvaluateFieldOfViewMultiplier(safeMagnification) / config.TrueOnePowerCalibration : 1f;
            return FiniteClamp(tangent * multiplier, 0.000001f, 1000f, 0.1f);
        }
    }
}
