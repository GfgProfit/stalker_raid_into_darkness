using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public static class XRayWeatherFogMath
    {
        public const float HOMOGENEOUS_BASE_HEIGHT = 10000.0f;
        public const float HOMOGENEOUS_MAXIMUM_HEIGHT = 10001.0f;

        public static bool TryGetParameters(XRayWeatherSnapshot snapshot, out Vector4 parameters)
        {
            parameters = Vector4.zero;
            Color color = snapshot.FogColor;
            if (!Finite(snapshot.FogDensity) || !Finite(snapshot.FogDistance) || snapshot.FogDistance <= 0.0f || !Finite(snapshot.FarPlane) || snapshot.FarPlane <= 0.0f || !Finite(color.r) || !Finite(color.g) || !Finite(color.b)) return false;
            float near = snapshot.FogNear;
            float far = snapshot.FogFar;
            float span = far - near;
            if (!Finite(near) || !Finite(far) || !Finite(span) || span <= 0.0f) return false;
            float reciprocal = 1.0f / span;
            float offset = -near * reciprocal;
            if (!Finite(reciprocal) || !Finite(offset)) return false;
            parameters = new Vector4(offset, near, far, reciprocal);
            return true;
        }

        public static float EvaluateFactor(float distance, Vector4 parameters)
        {
            if (!Finite(distance) || !Finite(parameters.x) || !Finite(parameters.w) || parameters.w <= 0.0f) throw new ArgumentOutOfRangeException(nameof(distance));
            return (float)Math.Max(0.0, Math.Min(1.0, (double)distance * parameters.w + parameters.x));
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool TryGetMeanFreePath(XRayWeatherSnapshot snapshot, out float meanFreePath)
        {
            meanFreePath = 1.0f;
            if (!TryGetParameters(snapshot, out Vector4 parameters)) return false;
            double anchor = parameters.y + ((double)parameters.z - parameters.y) * (1.0 - Math.Exp(-1.0));
            meanFreePath = (float)Math.Max(1.0, anchor);
            return Finite(meanFreePath);
        }

        public static Color ToLinearColor(Color encoded)
        {
            if (!Finite(encoded.r) || !Finite(encoded.g) || !Finite(encoded.b)) throw new ArgumentOutOfRangeException(nameof(encoded));
            return new Color(Decode(encoded.r), Decode(encoded.g), Decode(encoded.b), 1.0f);
        }

        public static Color ToSkyTint(Color encoded)
        {
            if (!Finite(encoded.r) || !Finite(encoded.g) || !Finite(encoded.b)) throw new ArgumentOutOfRangeException(nameof(encoded));
            float maximum = Mathf.Max(0.0f, Mathf.Max(encoded.r, Mathf.Max(encoded.g, encoded.b)));
            if (maximum == 0.0f) return Color.white;
            var hue = new Color(Decode(Mathf.Max(0.0f, encoded.r) / maximum), Decode(Mathf.Max(0.0f, encoded.g) / maximum), Decode(Mathf.Max(0.0f, encoded.b) / maximum), 1.0f);
            return Color.Lerp(Color.white, hue, Mathf.Clamp01(maximum));
        }

        private static float Decode(float value)
        {
            return (float)Math.Min(65504.0, Math.Pow(Math.Max(0.0, value), 2.2));
        }
    }
}
