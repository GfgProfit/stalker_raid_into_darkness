using System.Collections.Generic;
using RaidIntoDarkness.Player;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    internal sealed class GrenadeExplosionDamage
    {
        private const float FRAGMENT_RANGE = 100f;
        private const float SURFACE_OFFSET = 0.002f;
        private const float MAX_COVER_ENTRY_DISTANCE = 0.025f;
        private const float MAX_COVER_THICKNESS = 0.06f;
        private const float MAX_COVER_EXIT_DISTANCE = 0.10f;
        private const int OVERLAP_BUFFER_SIZE = 64;
        private const int RAY_BUFFER_SIZE = 16;

        private readonly GrenadeAmmoData _ammo;
        private readonly Vector3 _center;
        private readonly Vector3 _origin;
        private readonly int _mask;
        private readonly Transform _weapon;
        private readonly Collider[] _overlaps = new Collider[OVERLAP_BUFFER_SIZE];
        private readonly RaycastHit[] _hits = new RaycastHit[RAY_BUFFER_SIZE];
        private readonly Dictionary<IHealthDamageReceiver, float> _damage = new Dictionary<IHealthDamageReceiver, float>();

#if UNITY_EDITOR
        internal GrenadeShotDebug DebugShot { get; set; }
#endif

        public GrenadeExplosionDamage(GrenadeAmmoData ammo, Vector3 center, Vector3 normal, int mask, Transform weapon)
        {
            _ammo = ammo;
            _center = center;
            _origin = center + normal * SURFACE_OFFSET;
            _mask = mask;
            _weapon = weapon;
        }

        public void Apply()
        {
            CollectBlastDamage();
            CollectFragmentDamage();
            foreach (KeyValuePair<IHealthDamageReceiver, float> hit in _damage)
            {
                if (hit.Key is not Component receiver || receiver == null || hit.Value <= 0f || !hit.Key.TryApplyHealthDamage(hit.Value))
                {
                    continue;
                }
#if UNITY_EDITOR
                DebugShot?.AddDamagedEntity();
#endif
            }
        }

        private void CollectBlastDamage()
        {
            float radius = _ammo.BlastRadius;
            if (radius <= 0f || _ammo.BlastDamage <= 0f)
            {
                return;
            }

            Collider[] overlaps = _overlaps;
            int count = Physics.OverlapSphereNonAlloc(_center, radius, overlaps, _mask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length)
            {
                overlaps = Physics.OverlapSphere(_center, radius, _mask, QueryTriggerInteraction.Ignore);
                count = overlaps.Length;
            }

            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i];
                IHealthDamageReceiver target = collider.GetComponentInParent<IHealthDamageReceiver>();
                if (target == null)
                {
                    continue;
                }

                Vector3 center = collider.bounds.center;
                Vector3 nearest = GetClosestPoint(collider, _origin);
                Vector3 vertical = Vector3.up * collider.bounds.extents.y * 0.65f;
                float distance = GetExposedDistance(nearest, target, radius);
                distance = Mathf.Min(distance, GetExposedDistance(center, target, distance));
                distance = Mathf.Min(distance, GetExposedDistance(GetClosestPoint(collider, center + vertical), target, distance));
                distance = Mathf.Min(distance, GetExposedDistance(GetClosestPoint(collider, center - vertical), target, distance));
                float amount = GetDamageAtDistance(_ammo.BlastDamage, distance, radius);
                if (amount > 0f && (!_damage.TryGetValue(target, out float existing) || amount > existing))
                {
                    _damage[target] = amount;
                }
            }
        }

        private void CollectFragmentDamage()
        {
            if (_ammo.FragmentDamage <= 0f)
            {
                return;
            }

            for (int i = 0; i < _ammo.FragmentCount; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                if (!TryHit(direction, FRAGMENT_RANGE, null, out RaycastHit hit))
                {
#if UNITY_EDITOR
                    DebugShot?.AddFragment(_origin + direction * FRAGMENT_RANGE, GrenadeFragmentResult.Miss);
#endif
                    continue;
                }

                IHealthDamageReceiver target = hit.collider.GetComponentInParent<IHealthDamageReceiver>();
                if (target == null && hit.distance <= MAX_COVER_ENTRY_DISTANCE && TryHit(direction, MAX_COVER_ENTRY_DISTANCE + MAX_COVER_THICKNESS + MAX_COVER_EXIT_DISTANCE, hit.collider, out RaycastHit next) && CanCrossThinCover(direction, next.distance + SURFACE_OFFSET, hit))
                {
                    hit = next;
                    target = hit.collider.GetComponentInParent<IHealthDamageReceiver>();
                }

                if (target != null)
                {
                    _damage.TryGetValue(target, out float existing);
                    _damage[target] = existing + GetDamageAtDistance(_ammo.FragmentDamage, Vector3.Distance(_center, hit.point), FRAGMENT_RANGE);
                }
#if UNITY_EDITOR
                DebugShot?.AddFragment(hit.point, target != null ? GrenadeFragmentResult.Target : GrenadeFragmentResult.Cover);
#endif
            }
        }

        private float GetExposedDistance(Vector3 point, IHealthDamageReceiver target, float maximum)
        {
            float distance = Vector3.Distance(_center, point);
            if (distance >= maximum)
            {
                return maximum;
            }

            Vector3 offset = point - _origin;
            Vector3 direction = offset.normalized;
            float rayLength = offset.magnitude;
            if (!TryHit(direction, rayLength, null, out RaycastHit hit) || ReferenceEquals(hit.collider.GetComponentInParent<IHealthDamageReceiver>(), target))
            {
                return distance;
            }

            if (CanCrossThinCover(direction, rayLength, hit) && (!TryHit(direction, rayLength, hit.collider, out RaycastHit next) || ReferenceEquals(next.collider.GetComponentInParent<IHealthDamageReceiver>(), target)))
            {
                return distance;
            }

            return maximum;
        }

        private bool CanCrossThinCover(Vector3 direction, float distance, RaycastHit entry)
        {
            if (entry.distance > MAX_COVER_ENTRY_DISTANCE || distance > MAX_COVER_ENTRY_DISTANCE + MAX_COVER_THICKNESS + MAX_COVER_EXIT_DISTANCE)
            {
                return false;
            }

            if (!entry.collider.Raycast(new Ray(_origin + direction * distance, -direction), out RaycastHit exit, distance))
            {
                return distance - entry.distance <= MAX_COVER_EXIT_DISTANCE && IsFlatSurface(entry);
            }

            float thickness = distance - entry.distance - exit.distance;
            return exit.distance <= MAX_COVER_EXIT_DISTANCE && thickness >= -SURFACE_OFFSET && thickness <= MAX_COVER_THICKNESS;
        }

        private static bool IsFlatSurface(RaycastHit hit)
        {
            if (hit.collider is not MeshCollider collider || collider.sharedMesh == null)
            {
                return false;
            }

            Bounds bounds = collider.sharedMesh.bounds;
            Transform mesh = collider.transform;
            float width = Mathf.Abs(Vector3.Dot(mesh.TransformVector(Vector3.right * bounds.size.x), hit.normal));
            float height = Mathf.Abs(Vector3.Dot(mesh.TransformVector(Vector3.up * bounds.size.y), hit.normal));
            float depth = Mathf.Abs(Vector3.Dot(mesh.TransformVector(Vector3.forward * bounds.size.z), hit.normal));
            return width + height + depth <= SURFACE_OFFSET;
        }

        private bool TryHit(Vector3 direction, float distance, Collider ignored, out RaycastHit closest)
        {
            closest = default;
            if (distance <= 0f)
            {
                return false;
            }

            RaycastHit[] hits = _hits;
            int count = Physics.RaycastNonAlloc(_origin, direction, hits, distance, _mask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(_origin, direction, distance, _mask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == ignored || hit.distance > distance || _weapon != null && hit.collider.transform.IsChildOf(_weapon))
                {
                    continue;
                }

                closest = hit;
                distance = hit.distance;
            }

            return closest.collider != null;
        }

        private static Vector3 GetClosestPoint(Collider collider, Vector3 point)
        {
            return collider is BoxCollider or SphereCollider or CapsuleCollider || collider is MeshCollider mesh && mesh.convex ? collider.ClosestPoint(point) : collider.bounds.ClosestPoint(point);
        }

        private static float GetDamageAtDistance(float damage, float distance, float radius)
        {
            return damage * Mathf.Clamp01(1f - distance / radius);
        }
    }
}
