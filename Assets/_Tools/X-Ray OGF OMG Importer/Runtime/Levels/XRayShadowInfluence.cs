using UnityEngine;

namespace XRay.Importer.Levels
{
    public sealed class XRayShadowInfluence
    {
        private readonly Light[] _sources;
        private readonly float _receiverDistance;
        private readonly float _margin;
        private readonly Vector3[] _corners = new Vector3[8];
        private readonly Vector3[] _planeCorners = new Vector3[4];
        private readonly SourceBounds[] _prepared;
        private bool _valid;
        private int _count;

        private struct SourceBounds
        {
            public LightType Type { get; set; }
            public Vector3 Right { get; set; }
            public Vector3 Up { get; set; }
            public Vector3 Forward { get; set; }
            public Vector3 Min { get; set; }
            public Vector3 Max { get; set; }
            public Vector3 Position { get; set; }
            public float RangeSquared { get; set; }
        }

        public XRayShadowInfluence(Light[] sources, float receiverDistance, float margin = 2.0f)
        {
            _sources = sources;
            _receiverDistance = receiverDistance;
            _margin = Mathf.Max(0.0f, margin);
            _prepared = new SourceBounds[sources != null ? sources.Length : 0];
        }

        public void Prepare(Camera camera)
        {
            _valid = camera != null && _sources != null && _sources.Length > 0 && !camera.stereoEnabled;
            if (!_valid)
            {
                return;
            }

            float far = _receiverDistance > 0.0f ? Mathf.Min(camera.farClipPlane, _receiverDistance) : camera.farClipPlane;
            for (int plane = 0; plane < 2; plane++)
            {
                camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), plane == 0 ? camera.nearClipPlane : far, Camera.MonoOrStereoscopicEye.Mono, _planeCorners);
                for (int corner = 0; corner < 4; corner++)
                {
                    _corners[plane * 4 + corner] = camera.transform.TransformPoint(_planeCorners[corner]);
                }
            }

            _count = 0;
            foreach (Light source in _sources)
            {
                if (source == null || !source.isActiveAndEnabled || source.shadows == LightShadows.None)
                {
                    continue;
                }

                var entry = new SourceBounds { Type = source.type, Right = source.transform.right, Up = source.transform.up, Forward = source.transform.forward, Position = source.transform.position, RangeSquared = (source.range + _margin) * (source.range + _margin), Min = Vector3.one * float.MaxValue, Max = Vector3.one * float.MinValue };
                if (source.type == LightType.Directional)
                {
                    foreach (Vector3 corner in _corners)
                    {
                        Vector3 projected = new Vector3(Vector3.Dot(corner, entry.Right), Vector3.Dot(corner, entry.Up), Vector3.Dot(corner, entry.Forward));
                        entry.Min = Vector3.Min(entry.Min, projected);
                        entry.Max = Vector3.Max(entry.Max, projected);
                    }
                }

                _prepared[_count++] = entry;
            }
        }

        public bool Intersects(Bounds caster)
        {
            if (!_valid)
            {
                return true;
            }

            for (int i = 0; i < _count; i++)
            {
                SourceBounds source = _prepared[i];
                if (source.Type == LightType.Directional)
                {
                    if (IntersectsDirectional(caster, source))
                    {
                        return true;
                    }
                }
                else if (source.Type == LightType.Point || source.Type == LightType.Spot)
                {
                    if (caster.SqrDistance(source.Position) <= source.RangeSquared)
                    {
                        return true;
                    }
                }
                else
                {
                    return true;
                }
            }

            return false;
        }

        private bool IntersectsDirectional(Bounds caster, SourceBounds source)
        {
            Vector3 center = new Vector3(Vector3.Dot(caster.center, source.Right), Vector3.Dot(caster.center, source.Up), Vector3.Dot(caster.center, source.Forward));
            Vector3 extent = new Vector3(ProjectExtent(caster.extents, source.Right), ProjectExtent(caster.extents, source.Up), ProjectExtent(caster.extents, source.Forward)) + Vector3.one * _margin;
            return center.x + extent.x >= source.Min.x && center.x - extent.x <= source.Max.x && center.y + extent.y >= source.Min.y && center.y - extent.y <= source.Max.y && center.z - extent.z <= source.Max.z;
        }

        private static float ProjectExtent(Vector3 extent, Vector3 axis)
        {
            return extent.x * Mathf.Abs(axis.x) + extent.y * Mathf.Abs(axis.y) + extent.z * Mathf.Abs(axis.z);
        }
    }
}
