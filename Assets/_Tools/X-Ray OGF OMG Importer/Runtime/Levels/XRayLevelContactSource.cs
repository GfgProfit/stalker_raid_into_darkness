using System;
using System.Collections.Generic;
using UnityEngine;
using XRay.Importer.Core.Level;

namespace XRay.Importer.Levels
{
    public readonly struct XRayPassableContact
    {
        public XRayPassableContact(XRayGameMaterial material, Vector3 point)
        {
            Material = material;
            Point = point;
        }

        public XRayGameMaterial Material { get; }
        public Vector3 Point { get; }
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class XRayLevelContactSource : MonoBehaviour
    {
        private const float SURFACE_TOLERANCE = 0.025f;

        private static readonly List<XRayLevelContactSource> _sources = new List<XRayLevelContactSource>();

        [SerializeField] private TextAsset _collisionSource;
        [SerializeField] private TextAsset _materialSource;

        private readonly List<int> _faces = new List<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private XRayCollisionModel _collision;
        private XRayGameMaterials _materials;
        private bool _invalid;

        public TextAsset CollisionSource => _collisionSource;
        public TextAsset MaterialSource => _materialSource;

        public void Initialize(TextAsset collision, TextAsset materials)
        {
            _collisionSource = collision;
            _materialSource = materials;
            _collision = null;
            _materials = null;
            _invalid = false;
        }

        public void InitializeData(byte[] collision, byte[] materials)
        {
            _collision = XRayCollisionModel.Read(collision, name + " contact CFORM");
            _collision.BuildGrid(XRayDetails.SLOT_SIZE);
            _materials = XRayGameMaterials.Read(materials, name + " contact materials");
            _invalid = false;
        }

        public static bool TryResolveMaterial(RaycastHit hit, out XRayGameMaterial material)
        {
            material = null;
            if (hit.collider == null)
            {
                return false;
            }

            XRayLevelContactSource source = hit.collider.GetComponentInParent<XRayLevelContactSource>();
            return source != null && source.isActiveAndEnabled && source.TryResolveMaterial(hit.point, hit.normal, out material);
        }

        public bool TryResolveMaterial(Vector3 point, Vector3 normal, out XRayGameMaterial material)
        {
            material = null;
            if (!EnsureData())
            {
                return false;
            }

            Query(point, point, SURFACE_TOLERANCE);
            float closest = SURFACE_TOLERANCE * SURFACE_TOLERANCE;
            foreach (int face in _faces)
            {
                XRayGameMaterial candidate = Material(face);
                if (candidate == null || candidate.IsPassable && !candidate.IsActorObstacle)
                {
                    continue;
                }

                Triangle(face, out Vector3 a, out Vector3 b, out Vector3 c);
                Vector3 faceNormal = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(faceNormal, normal) < 0.5f)
                {
                    continue;
                }

                float distance = (ClosestPoint(point, a, b, c) - point).sqrMagnitude;
                if (distance <= closest)
                {
                    closest = distance;
                    material = candidate;
                }
            }

            return material != null;
        }

        public static bool TryFindPassableContact(Vector3 bottom, Vector3 top, float radius, out XRayGameMaterial material, out Vector3 point)
        {
            material = null;
            point = Vector3.zero;
            for (int i = _sources.Count - 1; i >= 0; i--)
            {
                XRayLevelContactSource source = _sources[i];
                if (source == null)
                {
                    _sources.RemoveAt(i);
                    continue;
                }

                if (source.TryGetPassableContact(bottom, top, radius, out material, out point))
                {
                    return true;
                }
            }

            return false;
        }

        public static void GetPassableContacts(Vector3 bottom, Vector3 top, float radius, List<XRayPassableContact> result)
        {
            result.Clear();
            for (int i = _sources.Count - 1; i >= 0; i--)
            {
                XRayLevelContactSource source = _sources[i];
                if (source == null)
                {
                    _sources.RemoveAt(i);
                    continue;
                }

                source.CollectPassableContacts(bottom, top, radius, result);
            }
        }

        public bool TryGetPassableContact(Vector3 bottom, Vector3 top, float radius, out XRayGameMaterial material, out Vector3 point)
        {
            material = null;
            point = Vector3.zero;
            if (!EnsureData())
            {
                return false;
            }

            Query(bottom, top, radius);
            foreach (int face in _faces)
            {
                XRayGameMaterial candidate = Material(face);
                if (candidate == null || !candidate.IsPassable || candidate.IsActorObstacle)
                {
                    continue;
                }

                Triangle(face, out Vector3 a, out Vector3 b, out Vector3 c);
                if (CapsuleDistanceSquared(bottom, top, a, b, c, out point) <= radius * radius)
                {
                    material = candidate;
                    return true;
                }
            }

            return false;
        }

        private void CollectPassableContacts(Vector3 bottom, Vector3 top, float radius, List<XRayPassableContact> result)
        {
            if (!EnsureData())
            {
                return;
            }

            Query(bottom, top, radius);
            foreach (int face in _faces)
            {
                XRayGameMaterial material = Material(face);
                if (material == null || !material.IsPassable || material.IsActorObstacle)
                {
                    continue;
                }

                bool duplicate = false;
                for (int i = 0; i < result.Count; i++)
                {
                    if (result[i].Material == material)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    continue;
                }

                Triangle(face, out Vector3 a, out Vector3 b, out Vector3 c);
                if (CapsuleDistanceSquared(bottom, top, a, b, c, out Vector3 point) <= radius * radius)
                {
                    result.Add(new XRayPassableContact(material, point));
                }
            }
        }

        private void OnEnable()
        {
            if (_collisionSource != null && _materialSource != null)
            {
                _collision = null;
                _materials = null;
                _invalid = false;
            }

            if (!_sources.Contains(this))
            {
                _sources.Add(this);
            }
        }

        private void OnDisable()
        {
            _sources.Remove(this);
        }

        private bool EnsureData()
        {
            if (_collision != null && _materials != null)
            {
                return true;
            }

            if (_invalid || _collisionSource == null || _materialSource == null)
            {
                return false;
            }

            try
            {
                _collision = XRayRuntimeCollisionSource.Load(_collisionSource);
                _materials = XRayGameMaterials.Read(_materialSource.bytes, name + " contact materials");
                return true;
            }
            catch (Exception exception)
            {
                _invalid = true;
                Debug.LogError($"X-Ray contact source '{name}' cannot load collision materials: {exception.Message}", this);
                return false;
            }
        }

        private void Query(Vector3 bottom, Vector3 top, float radius)
        {
            Matrix4x4 inverse = transform.worldToLocalMatrix;
            Vector3 a = inverse.MultiplyPoint3x4(bottom);
            Vector3 b = inverse.MultiplyPoint3x4(top);
            Vector3 extent = new Vector3(new Vector3(inverse.m00, inverse.m01, inverse.m02).magnitude, new Vector3(inverse.m10, inverse.m11, inverse.m12).magnitude, new Vector3(inverse.m20, inverse.m21, inverse.m22).magnitude) * radius;
            Vector3 min = Vector3.Min(a, b) - extent;
            Vector3 max = Vector3.Max(a, b) + extent;
            _collision.QueryBox(min.x, min.y, min.z, max.x, max.y, max.z, _faces, _seen);
        }

        private XRayGameMaterial Material(int face)
        {
            return _materials.GetById(_collision.Materials[face]);
        }

        private void Triangle(int face, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            a = Vertex(_collision.Triangles[face * 3]);
            b = Vertex(_collision.Triangles[face * 3 + 1]);
            c = Vertex(_collision.Triangles[face * 3 + 2]);
        }

        private Vector3 Vertex(int index)
        {
            return transform.TransformPoint(new Vector3(_collision.Vertices[index * 3], _collision.Vertices[index * 3 + 1], _collision.Vertices[index * 3 + 2]));
        }

        public static float CapsuleDistanceSquared(Vector3 bottom, Vector3 top, Vector3 a, Vector3 b, Vector3 c, out Vector3 point)
        {
            point = ClosestPoint(bottom, a, b, c);
            float best = (bottom - point).sqrMagnitude;
            Vector3 normal = Vector3.Cross(b - a, c - a);
            Vector3 direction = top - bottom;
            float denominator = Vector3.Dot(normal, direction);
            if (Mathf.Abs(denominator) > 0.000001f)
            {
                float t = Vector3.Dot(normal, a - bottom) / denominator;
                if (t >= 0.0f && t <= 1.0f)
                {
                    Vector3 intersection = bottom + direction * t;
                    Vector3 intersectionPoint = ClosestPoint(intersection, a, b, c);
                    float distance = (intersectionPoint - intersection).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        point = intersectionPoint;
                    }
                }
            }

            Vector3 endPoint = ClosestPoint(top, a, b, c);
            float endDistance = (top - endPoint).sqrMagnitude;
            if (endDistance < best)
            {
                best = endDistance;
                point = endPoint;
            }

            EdgeDistance(bottom, top, a, b, ref best, ref point);
            EdgeDistance(bottom, top, b, c, ref best, ref point);
            EdgeDistance(bottom, top, c, a, ref best, ref point);
            return best;
        }

        private static void EdgeDistance(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1, ref float best, ref Vector3 point)
        {
            Vector3 u = p1 - p0;
            Vector3 v = q1 - q0;
            Vector3 w = p0 - q0;
            float a = Vector3.Dot(u, u);
            float b = Vector3.Dot(u, v);
            float c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w);
            float e = Vector3.Dot(v, w);
            float denominator = a * c - b * b;
            float s = denominator > 0.000001f ? Mathf.Clamp01((b * e - c * d) / denominator) : 0.0f;
            float t = c > 0.000001f ? Mathf.Clamp01((b * s + e) / c) : 0.0f;
            s = a > 0.000001f ? Mathf.Clamp01((b * t - d) / a) : 0.0f;
            t = c > 0.000001f ? Mathf.Clamp01((b * s + e) / c) : 0.0f;
            Vector3 candidate = q0 + v * t;
            float distance = (p0 + u * s - candidate).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                point = candidate;
            }
        }

        private static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            if (Vector3.Cross(ab, ac).sqrMagnitude < 0.0000000001f)
            {
                float best = float.MaxValue;
                Vector3 point = a;
                EdgeDistance(p, p, a, b, ref best, ref point);
                EdgeDistance(p, p, b, c, ref best, ref point);
                EdgeDistance(p, p, c, a, ref best, ref point);
                return point;
            }

            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0.0f && d2 <= 0.0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0.0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0.0f && d1 >= 0.0f && d3 <= 0.0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0.0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0.0f && d2 >= 0.0f && d6 <= 0.0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0.0f && d4 - d3 >= 0.0f && d5 - d6 >= 0.0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float inverse = 1.0f / (va + vb + vc);
            return a + ab * (vb * inverse) + ac * (vc * inverse);
        }
    }
}
