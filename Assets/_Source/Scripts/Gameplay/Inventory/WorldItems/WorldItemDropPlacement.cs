using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    internal sealed class WorldItemDropPlacement
    {
        private const float DROP_DISTANCE = 0.6f;
        private const float EXTRA_CLEARANCE_DISTANCE = 0.78f;
        private const float QUERY_MARGIN = 0.01f;
        private const float DIAGONAL_SCALE = 0.7071068f;
        private const int DIRECTION_COUNT = 5;

        private readonly Collider[] _overlaps = new Collider[64];
        private readonly RaycastHit[] _hits = new RaycastHit[32];

        public bool TryFindPosition(Transform item, Collider[] colliders, Transform player, Vector3 origin, Vector3 forward, out Vector3 position, out Vector3 direction)
        {
            Physics.SyncTransforms();
            Bounds bounds = default;
            bool hasBounds = false;

            foreach (Collider collider in colliders)
            {
                if (collider.enabled == false || collider.isTrigger)
                {
                    continue;
                }

                if (hasBounds)
                {
                    bounds.Encapsulate(collider.bounds);
                }
                else
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
            }

            position = default;
            direction = default;

            if (hasBounds == false)
            {
                return false;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);

            for (int i = 0; i < DIRECTION_COUNT; i++)
            {
                Vector3 candidate = i switch
                {
                    0 => forward,
                    1 => (forward + right) * DIAGONAL_SCALE,
                    2 => (forward - right) * DIAGONAL_SCALE,
                    3 => right,
                    _ => -right
                };
                float reachable = GetReachableDistance(origin, candidate, item, player);

                for (int step = 0; step < 2; step++)
                {
                    float distance = step == 0 ? DROP_DISTANCE : EXTRA_CLEARANCE_DISTANCE;

                    if (distance > reachable)
                    {
                        break;
                    }

                    Vector3 center = origin + candidate * distance;
                    Vector3 displacement = center - bounds.center;

                    if (HasClearance(item, colliders, bounds, displacement) == false)
                    {
                        continue;
                    }

                    position = item.position + displacement;
                    direction = candidate;
                    return true;
                }
            }

            return false;
        }

        private float GetReachableDistance(Vector3 origin, Vector3 direction, Transform item, Transform player)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, _hits, EXTRA_CLEARANCE_DISTANCE + QUERY_MARGIN, Physics.AllLayers, QueryTriggerInteraction.Ignore);

            if (count == _hits.Length)
            {
                return 0f;
            }

            float distance = EXTRA_CLEARANCE_DISTANCE;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                Transform obstacle = hit.collider.transform;

                if (obstacle.IsChildOf(player) == false && obstacle.IsChildOf(item) == false)
                {
                    distance = Mathf.Min(distance, hit.distance - QUERY_MARGIN);
                }
            }

            return distance;
        }

        private bool HasClearance(Transform item, Collider[] colliders, Bounds bounds, Vector3 displacement)
        {
            Vector3 extents = bounds.extents + Vector3.one * QUERY_MARGIN;
            int count = Physics.OverlapBoxNonAlloc(bounds.center + displacement, extents, _overlaps, Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            bool clear = count < _overlaps.Length;

            for (int i = 0; i < count && clear; i++)
            {
                Collider obstacle = _overlaps[i];
                Transform obstacleTransform = obstacle.transform;

                if (obstacleTransform.IsChildOf(item))
                {
                    continue;
                }

                foreach (Collider collider in colliders)
                {
                    if (collider.enabled == false || collider.isTrigger)
                    {
                        continue;
                    }

                    Transform shape = collider.transform;

                    if (Physics.ComputePenetration(collider, shape.position + displacement, shape.rotation, obstacle, obstacleTransform.position, obstacleTransform.rotation, out _, out _))
                    {
                        clear = false;
                        break;
                    }
                }
            }

            Array.Clear(_overlaps, 0, count);
            return clear;
        }
    }
}
