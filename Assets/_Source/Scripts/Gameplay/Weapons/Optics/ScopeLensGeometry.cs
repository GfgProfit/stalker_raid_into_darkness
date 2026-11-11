using UnityEngine;

namespace RaidIntoDarkness.Weapons.Optics
{
    public readonly struct ScopeLensGeometry
    {
        private const float MIN_AXIS_LENGTH = 0.000001f;

        public Quaternion Rotation { get; }
        public Vector3 EyeLocal { get; }
        public Vector3 ProjectionEyeLocal { get; }
        public Vector2 HalfSizeMeters { get; }
        public float Shear { get; }

        private ScopeLensGeometry(Quaternion rotation, Vector3 eyeLocal, Vector3 projectionEyeLocal, Vector2 halfSizeMeters, float shear)
        {
            Rotation = rotation;
            EyeLocal = eyeLocal;
            ProjectionEyeLocal = projectionEyeLocal;
            HalfSizeMeters = halfSizeMeters;
            Shear = shear;
        }

        public static bool TryCreate(Matrix4x4 axisLocalToWorld, Vector3 eyeWorld, Vector2 localHalfSizeMeters, out ScopeLensGeometry geometry)
        {
            geometry = default;
            Vector3 origin = axisLocalToWorld.MultiplyPoint3x4(Vector3.zero);
            Vector3 right = axisLocalToWorld.MultiplyVector(Vector3.right);
            Vector3 up = axisLocalToWorld.MultiplyVector(Vector3.up);
            Vector3 forward = axisLocalToWorld.MultiplyVector(Vector3.forward);
            if (!IsFinite(origin) || !IsFinite(right) || !IsFinite(up) || !IsFinite(forward) || !IsFinite(eyeWorld) || !float.IsFinite(localHalfSizeMeters.x) || !float.IsFinite(localHalfSizeMeters.y) || localHalfSizeMeters.x <= 0f || localHalfSizeMeters.y <= 0f)
            {
                return false;
            }

            float rightLength = right.magnitude;
            float upLength = up.magnitude;
            float forwardLength = forward.magnitude;
            if (!float.IsFinite(rightLength) || !float.IsFinite(upLength) || !float.IsFinite(forwardLength) || rightLength < MIN_AXIS_LENGTH || upLength < MIN_AXIS_LENGTH || forwardLength < MIN_AXIS_LENGTH)
            {
                return false;
            }

            Vector3 rightDirection = right / rightLength;
            Vector3 normal = Vector3.Cross(rightDirection, up / upLength);
            float normalLength = normal.magnitude;
            if (normalLength < MIN_AXIS_LENGTH)
            {
                return false;
            }

            normal /= normalLength;
            if (Vector3.Dot(normal, forward / forwardLength) <= MIN_AXIS_LENGTH)
            {
                return false;
            }

            Vector3 upDirection = Vector3.Cross(normal, rightDirection);
            float orthogonalUpLength = Vector3.Dot(up, upDirection);
            Vector2 halfSize = new Vector2(localHalfSizeMeters.x * rightLength, localHalfSizeMeters.y * orthogonalUpLength);
            float shear = Vector3.Dot(up, rightDirection) / orthogonalUpLength;
            Vector3 delta = eyeWorld - origin;
            Vector3 eyeLocal = new Vector3(Vector3.Dot(delta, rightDirection), Vector3.Dot(delta, upDirection), Vector3.Dot(delta, normal));
            Vector3 projectionEye = new Vector3(eyeLocal.x - shear * eyeLocal.y, eyeLocal.y, eyeLocal.z);
            if (!IsFinite(eyeLocal) || !IsFinite(projectionEye) || !float.IsFinite(shear) || !float.IsFinite(halfSize.x) || !float.IsFinite(halfSize.y) || halfSize.x < 0.0001f || halfSize.y < 0.0001f || halfSize.x > 10f || halfSize.y > 10f)
            {
                return false;
            }

            geometry = new ScopeLensGeometry(Quaternion.LookRotation(normal, upDirection), eyeLocal, projectionEye, halfSize, shear);
            return true;
        }

        public Matrix4x4 ApplyProjectionShear(Matrix4x4 projection)
        {
            projection.m01 = -Shear * projection.m00;
            return projection;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
