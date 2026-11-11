using UnityEngine;

namespace RaidIntoDarkness.Weapons.Optics
{
    public static class ScopeProProjection
    {
        public static Vector2 GetCaptureProjectionScale(Vector2 halfSizeMeters, float eyeDistanceMeters, float renderSize)
        {
            float halfWidth = ScopeOpticsMath.FiniteClamp(halfSizeMeters.x, 0.0001f, 10f, 0.014f);
            float halfHeight = ScopeOpticsMath.FiniteClamp(halfSizeMeters.y, 0.0001f, 10f, 0.014f);
            float distance = ScopeOpticsMath.FiniteClamp(eyeDistanceMeters, 0.001f, 100f, 0.09f);
            float size = ScopeOpticsMath.FiniteClamp(renderSize, 0.001f, 2f, 1f);
            return new Vector2(size * (1f + 2f * halfWidth / distance), size * (1f + 2f * halfHeight / distance));
        }
    }
}
