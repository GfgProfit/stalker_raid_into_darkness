using UnityEngine;

namespace XRay.Importer.Levels
{
    public static class XRayFloraWindBounds
    {
        public const string MATERIAL_PROPERTY = "_MonolithFloraWind";

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
            if (state == null)
            {
                return bounds;
            }

            float height = bounds.size.y;
            float horizontal = state.MaxHorizontalDisplacement(height, 1.0f);
            float vertical = state.MaxVerticalDisplacement(height);
            bounds.Expand(new Vector3(horizontal * 2.0f, vertical * 2.0f, horizontal * 2.0f));
            return bounds;
        }
    }
}
