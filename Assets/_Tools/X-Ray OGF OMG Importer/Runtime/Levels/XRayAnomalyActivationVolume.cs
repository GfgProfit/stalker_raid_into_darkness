using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [DisallowMultipleComponent]
    public sealed class XRayAnomalyActivationVolume : MonoBehaviour
    {
        private const float FLOAT_EPSILON = 1.192092896e-7f;

        [SerializeField] private SphereCollider _proximityProxy;
        [SerializeField] private Collider[] _actualShapes = Array.Empty<Collider>();

        public SphereCollider ProximityProxy => _proximityProxy;
        public IReadOnlyList<Collider> ActualShapes => _actualShapes ?? Array.Empty<Collider>();

        public void Configure(SphereCollider proximityProxy, Collider[] actualShapes)
        {
            if (actualShapes == null || actualShapes.Length == 0)
            {
                throw new ArgumentException("Anomaly activation requires at least one source shape.", nameof(actualShapes));
            }
            var shapes = new List<Collider>(actualShapes.Length);
            foreach (Collider shape in actualShapes)
            {
                if (shape == null || shape == proximityProxy)
                {
                    throw new ArgumentException("Anomaly activation shapes cannot contain null or the proximity proxy.", nameof(actualShapes));
                }
                if (!(shape is SphereCollider) && !(shape is BoxCollider))
                {
                    throw new ArgumentException("Anomaly activation supports source sphere and box shapes: " + shape.GetType().Name, nameof(actualShapes));
                }
                if (shapes.Contains(shape) == false)
                {
                    shapes.Add(shape);
                }
            }
            _proximityProxy = proximityProxy;
            _actualShapes = shapes.ToArray();
        }

        public bool Contains(Vector3 worldPoint)
        {
            if (isActiveAndEnabled == false || _actualShapes == null)
            {
                return false;
            }
            foreach (Collider shape in _actualShapes)
            {
                if (IsAvailable(shape) == false || shape == _proximityProxy)
                {
                    continue;
                }
                if (shape is SphereCollider sphere)
                {
                    float radius = SphereRadius(sphere);
                    float tolerance = Tolerance(radius);
                    if ((worldPoint - sphere.transform.TransformPoint(sphere.center)).sqrMagnitude <= (radius + tolerance) * (radius + tolerance))
                    {
                        return true;
                    }
                }
                else if (shape is BoxCollider box)
                {
                    OrientedBox bounds = new OrientedBox(box);
                    if (PointBoxDistanceSquared(worldPoint, bounds) <= Tolerance(bounds.Extents.magnitude) * Tolerance(bounds.Extents.magnitude))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool Overlaps(Collider other)
        {
            if (isActiveAndEnabled == false || IsAvailable(other) == false || other == _proximityProxy || _actualShapes == null)
            {
                return false;
            }
            foreach (Collider shape in _actualShapes)
            {
                if (IsAvailable(shape) == false || shape == _proximityProxy || shape == other)
                {
                    continue;
                }
                if (OverlapsShape(shape, other))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsAvailable(Collider collider)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
        }

        private static bool OverlapsShape(Collider shape, Collider other)
        {
            if (shape is SphereCollider sphere)
            {
                Vector3 center = sphere.transform.TransformPoint(sphere.center);
                float radius = SphereRadius(sphere);
                if (other is SphereCollider otherSphere)
                {
                    float combined = radius + SphereRadius(otherSphere);
                    combined += Tolerance(combined);
                    return (center - otherSphere.transform.TransformPoint(otherSphere.center)).sqrMagnitude <= combined * combined;
                }
                if (other is BoxCollider otherBox)
                {
                    return SphereBox(center, radius, new OrientedBox(otherBox));
                }
                if (TryCapsule(other, out Vector3 from, out Vector3 to, out float capsuleRadius))
                {
                    float combined = radius + capsuleRadius;
                    combined += Tolerance(combined);
                    Vector3 direction = to - from;
                    float factor = direction.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(center - from, direction) / direction.sqrMagnitude) : 0f;
                    return (center - from - direction * factor).sqrMagnitude <= combined * combined;
                }
            }
            else if (shape is BoxCollider box)
            {
                OrientedBox bounds = new OrientedBox(box);
                if (other is SphereCollider otherSphere)
                {
                    return SphereBox(otherSphere.transform.TransformPoint(otherSphere.center), SphereRadius(otherSphere), bounds);
                }
                if (other is BoxCollider otherBox)
                {
                    return BoxesOverlap(bounds, new OrientedBox(otherBox));
                }
                if (TryCapsule(other, out Vector3 from, out Vector3 to, out float capsuleRadius))
                {
                    float radius = capsuleRadius + Tolerance(Mathf.Max(capsuleRadius, bounds.Extents.magnitude));
                    Quaternion inverse = Quaternion.Inverse(bounds.Rotation);
                    Vector3 localFrom = inverse * (from - bounds.Center);
                    Vector3 localTo = inverse * (to - bounds.Center);
                    return SegmentBoxDistanceSquared(localFrom, localTo, bounds.Extents) <= radius * radius;
                }
            }
            return Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation, other, other.transform.position, other.transform.rotation, out _, out _);
        }

        private static bool SphereBox(Vector3 center, float radius, OrientedBox box)
        {
            radius += Tolerance(Mathf.Max(radius, box.Extents.magnitude));
            return PointBoxDistanceSquared(center, box) <= radius * radius;
        }

        private static float PointBoxDistanceSquared(Vector3 point, OrientedBox box)
        {
            Vector3 local = Quaternion.Inverse(box.Rotation) * (point - box.Center);
            return PointBoxDistanceSquared(local, box.Extents);
        }

        private static float PointBoxDistanceSquared(Vector3 point, Vector3 extents)
        {
            Vector3 outside = new Vector3(Mathf.Max(Mathf.Abs(point.x) - extents.x, 0f), Mathf.Max(Mathf.Abs(point.y) - extents.y, 0f), Mathf.Max(Mathf.Abs(point.z) - extents.z, 0f));
            return outside.sqrMagnitude;
        }

        private static float SegmentBoxDistanceSquared(Vector3 from, Vector3 to, Vector3 extents)
        {
            Vector3 direction = to - from;
            float minimum = PointBoxDistanceSquared(from, extents);
            float left = 0f;
            for (int interval = 0; interval < 7 && left < 1f; interval++)
            {
                float right = 1f;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (direction[axis] == 0f)
                    {
                        continue;
                    }
                    float lower = (-extents[axis] - from[axis]) / direction[axis];
                    float upper = (extents[axis] - from[axis]) / direction[axis];
                    if (lower > left && lower < right)
                    {
                        right = lower;
                    }
                    if (upper > left && upper < right)
                    {
                        right = upper;
                    }
                }
                float midpoint = (left + right) * 0.5f;
                float quadratic = 0f;
                float linear = 0f;
                for (int axis = 0; axis < 3; axis++)
                {
                    float sample = from[axis] + direction[axis] * midpoint;
                    if (sample < -extents[axis] || sample > extents[axis])
                    {
                        float boundary = sample < 0f ? -extents[axis] : extents[axis];
                        quadratic += direction[axis] * direction[axis];
                        linear += direction[axis] * (from[axis] - boundary);
                    }
                }
                float nearest = quadratic > 0f ? Mathf.Clamp(-linear / quadratic, left, right) : left;
                minimum = Mathf.Min(minimum, Mathf.Min(PointBoxDistanceSquared(from + direction * nearest, extents), PointBoxDistanceSquared(from + direction * right, extents)));
                left = right;
            }
            return minimum;
        }

        private static bool BoxesOverlap(OrientedBox a, OrientedBox b)
        {
            Vector3 a0 = a.Rotation * Vector3.right;
            Vector3 a1 = a.Rotation * Vector3.up;
            Vector3 a2 = a.Rotation * Vector3.forward;
            Vector3 b0 = b.Rotation * Vector3.right;
            Vector3 b1 = b.Rotation * Vector3.up;
            Vector3 b2 = b.Rotation * Vector3.forward;
            if (Separates(a0, a, b) || Separates(a1, a, b) || Separates(a2, a, b) || Separates(b0, a, b) || Separates(b1, a, b) || Separates(b2, a, b))
            {
                return false;
            }
            return !(Separates(Vector3.Cross(a0, b0), a, b) || Separates(Vector3.Cross(a0, b1), a, b) || Separates(Vector3.Cross(a0, b2), a, b) || Separates(Vector3.Cross(a1, b0), a, b) || Separates(Vector3.Cross(a1, b1), a, b) || Separates(Vector3.Cross(a1, b2), a, b) || Separates(Vector3.Cross(a2, b0), a, b) || Separates(Vector3.Cross(a2, b1), a, b) || Separates(Vector3.Cross(a2, b2), a, b));
        }

        private static bool Separates(Vector3 axis, OrientedBox a, OrientedBox b)
        {
            float squared = axis.sqrMagnitude;
            if (squared <= FLOAT_EPSILON * FLOAT_EPSILON)
            {
                return false;
            }
            float projectionA = ProjectBox(axis, a);
            float projectionB = ProjectBox(axis, b);
            float tolerance = Tolerance(Mathf.Max(a.Extents.magnitude, b.Extents.magnitude)) * Mathf.Sqrt(squared);
            return Mathf.Abs(Vector3.Dot(b.Center - a.Center, axis)) > projectionA + projectionB + tolerance;
        }

        private static float ProjectBox(Vector3 axis, OrientedBox box)
        {
            return box.Extents.x * Mathf.Abs(Vector3.Dot(axis, box.Rotation * Vector3.right)) + box.Extents.y * Mathf.Abs(Vector3.Dot(axis, box.Rotation * Vector3.up)) + box.Extents.z * Mathf.Abs(Vector3.Dot(axis, box.Rotation * Vector3.forward));
        }

        private static bool TryCapsule(Collider collider, out Vector3 from, out Vector3 to, out float radius)
        {
            Vector3 center;
            float height;
            int direction;
            if (collider is CapsuleCollider capsule)
            {
                center = capsule.center;
                height = capsule.height;
                radius = capsule.radius;
                direction = capsule.direction;
            }
            else if (collider is CharacterController character)
            {
                center = character.center;
                height = character.height;
                radius = character.radius;
                direction = 1;
            }
            else
            {
                from = Vector3.zero;
                to = Vector3.zero;
                radius = 0f;
                return false;
            }
            Vector3 scale = Absolute(collider.transform.lossyScale);
            float radiusScale = direction == 0 ? Mathf.Max(scale.y, scale.z) : direction == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);
            radius *= radiusScale;
            float halfSegment = Mathf.Max(0f, height * scale[direction] * 0.5f - radius);
            Vector3 axis = direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward;
            Vector3 offset = collider.transform.rotation * axis * halfSegment;
            Vector3 position = collider.transform.TransformPoint(center);
            from = position - offset;
            to = position + offset;
            return true;
        }

        private static float SphereRadius(SphereCollider sphere)
        {
            Vector3 scale = Absolute(sphere.transform.lossyScale);
            return sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        }

        private static Vector3 Absolute(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static float Tolerance(float scale)
        {
            return Mathf.Max(1f, scale) * FLOAT_EPSILON * 4f;
        }

        private readonly struct OrientedBox
        {
            public Vector3 Center { get; }
            public Quaternion Rotation { get; }
            public Vector3 Extents { get; }

            public OrientedBox(BoxCollider box)
            {
                Center = box.transform.TransformPoint(box.center);
                Rotation = box.transform.rotation;
                Extents = Vector3.Scale(Absolute(box.size) * 0.5f, Absolute(box.transform.lossyScale));
            }
        }
    }
}
