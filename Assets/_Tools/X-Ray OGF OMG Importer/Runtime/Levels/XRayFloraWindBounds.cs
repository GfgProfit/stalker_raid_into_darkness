using UnityEngine;

namespace XRay.Importer.Levels
{
    public static class XRayFloraWindBounds
    {
        public const string MATERIAL_PROPERTY = "_MonolithFloraWind";

        private const float GROUP_ROUNDING_SCALE = 0.000002f;

        private static readonly int MATERIAL_ID = Shader.PropertyToID(MATERIAL_PROPERTY);

        public static XRayFloraWindState ActiveState()
        {
            XRayFloraWindProvider provider = XRayFloraWindProvider.Active;
            return provider != null && provider.isActiveAndEnabled && provider.State.Initialized && provider.State.Profile != XRayFloraWindProfile.Disabled ? provider.State : null;
        }

        public static bool Uses(Material material, XRayFloraWindState state)
        {
            return state != null && material != null && material.HasProperty(MATERIAL_ID) && Mathf.Abs(material.GetFloat(MATERIAL_ID) - (float)state.Profile) < 0.5f;
        }

        public static bool Uses(Material[] materials, XRayFloraWindState state)
        {
            if (state == null || materials == null)
            {
                return false;
            }

            foreach (Material material in materials)
            {
                if (Uses(material, state))
                {
                    return true;
                }
            }

            return false;
        }

        public static Bounds Expand(Bounds bounds, XRayFloraWindState state)
        {
            return ExpandCore(bounds, state, bounds.size.y);
        }

        public static Bounds Expand(Bounds bounds, XRayFloraWindState state, float sourceHeight)
        {
            Bounds expanded = ExpandCore(bounds, state, sourceHeight);
            if (state == null) return expanded;
            Vector3 center = expanded.center;
            Vector3 magnitude = new Vector3(Mathf.Abs(center.x), Mathf.Abs(center.y), Mathf.Abs(center.z)) + expanded.extents;
            expanded.Expand(Vector3.Max(Vector3.one, magnitude) * GROUP_ROUNDING_SCALE);
            return expanded;
        }

        private static Bounds ExpandCore(Bounds bounds, XRayFloraWindState state, float sourceHeight)
        {
            if (state == null)
            {
                return bounds;
            }

            float height = sourceHeight;
            float horizontal = state.MaxHorizontalDisplacement(height, 1.0f);
            float vertical = state.MaxVerticalDisplacement(height);
            bounds.Expand(new Vector3(horizontal * 2.0f, vertical * 2.0f, horizontal * 2.0f));
            return bounds;
        }
    }
}
