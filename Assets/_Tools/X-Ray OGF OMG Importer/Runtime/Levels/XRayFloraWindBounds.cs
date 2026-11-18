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

        internal readonly struct SnapshotKey
        {
            private readonly bool _present;
            private readonly bool _initialized;
            private readonly XRayFloraWindProfile _profile;
            private readonly float _treesSetupZ;
            private readonly float _treesSetupW;
            private readonly float _velocity;
            private readonly Vector3 _treeWind;
            private readonly Vector3 _treeWindPrev;

            public SnapshotKey(XRayFloraWindState state)
            {
                _present = state != null;
                _initialized = state != null && state.Initialized;
                _profile = state != null ? state.Profile : default;
                bool ssfx = _profile == XRayFloraWindProfile.AnomalySsfx;
                bool tree = _profile == XRayFloraWindProfile.TrueStalkerXRay;
                _treesSetupZ = ssfx ? state.TreesSetup.z : default;
                _treesSetupW = ssfx ? state.TreesSetup.w : default;
                _velocity = ssfx ? state.Weather.Velocity : default;
                _treeWind = tree ? new Vector3(state.TreeWind.x, state.TreeWind.y, state.TreeWind.z) : default;
                _treeWindPrev = tree ? new Vector3(state.TreeWindPrev.x, state.TreeWindPrev.y, state.TreeWindPrev.z) : default;
            }

            public bool Equals(SnapshotKey other)
            {
                return _present == other._present && _initialized == other._initialized && _profile == other._profile && _treesSetupZ.Equals(other._treesSetupZ) && _treesSetupW.Equals(other._treesSetupW) && _velocity.Equals(other._velocity) && _treeWind.Equals(other._treeWind) && _treeWindPrev.Equals(other._treeWindPrev);
            }
        }
    }
}
