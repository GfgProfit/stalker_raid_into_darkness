using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public static class AnomalyAttraction
    {
        public static Vector3 Evaluate(Vector3 offset, float radius, float innerRadius, float strength)
        {
            if (radius <= 0f || strength <= 0f)
            {
                return Vector3.zero;
            }
            float distance = offset.magnitude;
            float ratio = Mathf.Max(distance / radius, Mathf.Clamp01(innerRadius));
            float relative = Mathf.Max(0f, 1f - ratio * ratio);
            Vector3 direction = distance > 0.000001f ? offset / distance : Vector3.up;
            return direction * (strength * relative * relative * relative * relative * relative);
        }
    }
}
