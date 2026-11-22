using UnityEngine;

namespace XRay.Importer.Levels
{
    internal readonly struct XRayPointShadowWind
    {
        private const float GROUP_ROUNDING_SCALE = 0.000002f;

        private readonly bool _present;
        private readonly XRayFloraWindProfile _profile;
        private readonly float _speed;
        private readonly float _bendSpeed;
        private readonly float _trunkFactor;
        private readonly float _branch;
        private readonly float _amplitude;

        internal XRayPointShadowWind(XRayFloraWindState state)
        {
            this = default;
            if (state == null) return;
            _present = true;
            _profile = state.Profile;
            if (_profile == XRayFloraWindProfile.AnomalySsfx)
            {
                _speed = Mathf.Max(state.TreesSetup.w, Mathf.Clamp01(state.Weather.Velocity * 0.001f));
                float sqrtSpeed = Mathf.Clamp01(Mathf.Sqrt(_speed * 1.66f));
                _bendSpeed = Mathf.Clamp01(sqrtSpeed * 1.5f);
                _trunkFactor = Mathf.Abs(1.5f * state.TreesSetup.z);
                _branch = (2.5f * Mathf.Sqrt(2.0f) + 1.0f) * _speed;
            }
            else if (_profile == XRayFloraWindProfile.TrueStalkerXRay)
            {
                Vector4 treeWind = state.TreeWind;
                Vector4 treeWindPrev = state.TreeWindPrev;
                _amplitude = Mathf.Max(new Vector3(treeWind.x, treeWind.y, treeWind.z).magnitude, new Vector3(treeWindPrev.x, treeWindPrev.y, treeWindPrev.z).magnitude);
            }
        }

        internal Bounds Expand(Bounds bounds, float sourceHeight)
        {
            if (!_present) return bounds;
            float horizontal = 0.0f;
            float vertical = 0.0f;
            float height = Mathf.Max(0.0f, sourceHeight);
            if (_profile == XRayFloraWindProfile.AnomalySsfx)
            {
                float trunk = _bendSpeed * 0.006f * height * height * _trunkFactor * _bendSpeed;
                horizontal = trunk + _branch;
                vertical = Mathf.Clamp01(height * 0.1f) * _speed;
            }
            else if (_profile == XRayFloraWindProfile.TrueStalkerXRay)
            {
                horizontal = _amplitude * height * 2.0f * Mathf.Max(0.0f, 1.0f);
            }

            bounds.Expand(new Vector3(horizontal * 2.0f, vertical * 2.0f, horizontal * 2.0f));
            Vector3 center = bounds.center;
            Vector3 magnitude = new Vector3(Mathf.Abs(center.x), Mathf.Abs(center.y), Mathf.Abs(center.z)) + bounds.extents;
            bounds.Expand(Vector3.Max(Vector3.one, magnitude) * GROUP_ROUNDING_SCALE);
            return bounds;
        }
    }
}
