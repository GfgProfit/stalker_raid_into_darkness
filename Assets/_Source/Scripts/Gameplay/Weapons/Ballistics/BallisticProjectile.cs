using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    public sealed class BallisticProjectile : MonoBehaviour
    {
        private const int HIT_BUFFER_SIZE = 64;
        private const float MIN_SPEED_SQUARED = 0.0001f;
        private const float MUZZLE_DEBUG_SIZE = 0.025f;
        private const float IMPACT_DEBUG_SIZE = 0.035f;

        private readonly HashSet<Collider> _ignoredColliders = new HashSet<Collider>();
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HIT_BUFFER_SIZE];

        private WeaponBallisticsSettings _weaponData;
        private BallisticAmmoData _ammoData;
        private Vector3 _position;
        private Vector3 _velocity;
        private Vector3 _trajectoryOrigin;
        private Vector3 _trajectoryVelocity;
        private float _trajectoryTime;
        private float _maxDistance;
        private bool _allowRicochet;
        private float _simulationAccumulator;
        private float _elapsedLifetime;
        private float _distanceTravelled;
        private BallisticTracerView _tracerView;
        private bool _initialized;

        public Vector3 LaunchPosition { get; private set; }
        public Vector3 LaunchDirection { get; private set; }
        public BallisticAmmoData AmmoData => _ammoData;
        public Vector3 Velocity => _velocity;
        public float SpeedMetersPerSecond => _velocity.magnitude;
        public float DistanceTravelledMeters => _distanceTravelled;

        public void Initialize(WeaponBallisticsSettings weaponData, BallisticAmmoData ammoData, Vector3 position, Vector3 direction, float speedMetersPerSecond, float maximumDistance, Transform ownerRoot, Transform weaponRoot)
        {
            _weaponData = weaponData;
            _ammoData = ammoData;
            LaunchPosition = position;
            LaunchDirection = direction.normalized;
            _position = position;
            _velocity = direction.normalized * Mathf.Max(0f, speedMetersPerSecond);
            _trajectoryOrigin = _position;
            _trajectoryVelocity = _velocity;
            _trajectoryTime = 0f;
            _maxDistance = maximumDistance;
            _allowRicochet = ammoData != null && ammoData.AllowRicochet;
            _simulationAccumulator = 0f;
            _elapsedLifetime = 0f;
            _distanceTravelled = 0f;

            AddIgnoredColliders(ownerRoot);
            AddIgnoredColliders(weaponRoot);
            DisableAndIgnoreProjectileColliders();
            DisableProjectileRigidbodyPhysics();

            transform.SetPositionAndRotation(_position, ResolveRotation(_velocity, transform.rotation));
            _initialized = _weaponData != null && _ammoData != null && _velocity.sqrMagnitude > MIN_SPEED_SQUARED;

            if (_initialized == false)
            {
                Destroy(gameObject);
                return;
            }

            _tracerView = BallisticTracerView.Create(_ammoData, _position);

            if (_weaponData.DrawBallisticDebug)
            {
                DrawMarker(_position, MUZZLE_DEBUG_SIZE, Color.cyan);
                Debug.DrawLine(_position, _position + direction.normalized * 0.25f, Color.cyan, _weaponData.BallisticDebugDurationSeconds);
            }
        }

        private void Update()
        {
            if (_initialized == false)
            {
                return;
            }

            float simulationStep = _weaponData.BallisticSimulationStepSeconds;
            int maxSteps = _weaponData.BallisticMaxSimulationStepsPerFrame;
            _simulationAccumulator = Mathf.Min(_simulationAccumulator + Time.deltaTime, simulationStep * maxSteps);

            int simulatedSteps = 0;

            while (_simulationAccumulator >= simulationStep && simulatedSteps < maxSteps)
            {
                _simulationAccumulator -= simulationStep;
                simulatedSteps++;

                if (SimulateStep(simulationStep) == false)
                {
                    return;
                }
            }
        }

        private bool SimulateStep(float deltaTime)
        {
            _elapsedLifetime += deltaTime;

            if (_distanceTravelled >= _maxDistance || _velocity.sqrMagnitude < 1f)
            {
                CompleteTracer();
                Destroy(gameObject);
                return false;
            }

            _trajectoryTime += deltaTime;
            Vector3 nextPosition = MonolithBallistics.Position(_trajectoryOrigin, _trajectoryVelocity, _trajectoryTime);
            Vector3 nextVelocity = MonolithBallistics.Velocity(_trajectoryVelocity, _trajectoryTime);

            if (IsFinite(nextPosition) == false || IsFinite(nextVelocity) == false)
            {
                Debug.LogError($"[{nameof(BallisticProjectile)}] Simulation produced a non-finite trajectory. Check weapon flatness and muzzle transform.", this);
                CompleteTracer();
                Destroy(gameObject);
                return false;
            }

            Vector3 displacement = nextPosition - _position;
            float travelDistance = displacement.magnitude;

            if (travelDistance > _maxDistance - _distanceTravelled)
            {
                travelDistance = _maxDistance - _distanceTravelled;
                displacement = displacement.normalized * travelDistance;
                nextPosition = _position + displacement;
            }

            if (travelDistance <= Mathf.Epsilon)
            {
                _velocity = nextVelocity;
                return true;
            }

            if (TryGetClosestHit(_position, displacement / travelDistance, travelDistance, out RaycastHit hit))
            {
                float impactFraction = Mathf.Clamp01(hit.distance / travelDistance);
                Vector3 impactVelocity = MonolithBallistics.Velocity(_trajectoryVelocity, _trajectoryTime - deltaTime + deltaTime * impactFraction);
                _distanceTravelled += hit.distance;
                MoveTracer(hit.point);

                if (_weaponData.DrawBallisticDebug)
                {
                    Debug.DrawLine(_position, hit.point, Color.yellow, _weaponData.BallisticDebugDurationSeconds);
                }

                return ResolveImpact(hit, impactVelocity);
            }

            if (_weaponData.DrawBallisticDebug)
            {
                Debug.DrawLine(_position, nextPosition, Color.green, _weaponData.BallisticDebugDurationSeconds);
            }

            _distanceTravelled += travelDistance;
            _position = nextPosition;
            _velocity = nextVelocity;
            transform.SetPositionAndRotation(_position, ResolveRotation(_velocity, transform.rotation));
            MoveTracer(_position);
            return true;
        }

        private bool ResolveImpact(RaycastHit hit, Vector3 impactVelocity)
        {
            BallisticImpactContext impact = new BallisticImpactContext(gameObject, _ammoData, hit, impactVelocity);
            IBallisticSurface ballisticSurface = hit.collider == null ? null : hit.collider.GetComponentInParent<IBallisticSurface>();

            if (ballisticSurface != null && ballisticSurface.TryResolvePenetration(impact, out BallisticPenetrationResult penetrationResult) && penetrationResult.ContinueProjectile && penetrationResult.ExitVelocity.sqrMagnitude > MIN_SPEED_SQUARED)
            {
                Vector3 exitDirection = penetrationResult.ExitVelocity.normalized;
                float offset = Mathf.Max(_weaponData.RicochetSurfaceOffsetMeters, 0.0001f);
                _position = penetrationResult.ExitPosition + exitDirection * offset;
                _velocity = penetrationResult.ExitVelocity;
                ResetTrajectory();
                transform.SetPositionAndRotation(_position, ResolveRotation(_velocity, transform.rotation));
                MoveTracer(_position);

                if (_weaponData.DrawBallisticDebug)
                {
                    DrawMarker(_position, IMPACT_DEBUG_SIZE, Color.blue);
                }

                return true;
            }

            if (TryRicochet(hit, impactVelocity))
            {
                return true;
            }

            _position = hit.point;
            transform.position = _position;

            if (_weaponData.DrawBallisticDebug)
            {
                DrawMarker(_position, IMPACT_DEBUG_SIZE, Color.red);
            }

            CompleteTracer();
            Destroy(gameObject);
            return false;
        }

        private bool TryRicochet(RaycastHit hit, Vector3 impactVelocity)
        {
            if (!_allowRicochet || impactVelocity.sqrMagnitude <= MIN_SPEED_SQUARED)
            {
                return false;
            }

            Vector3 incoming = impactVelocity.normalized;
            Vector3 reflected = Vector3.Reflect(incoming, hit.normal);
            Vector2 spread = Random.insideUnitCircle * Mathf.Tan(10f * Mathf.Deg2Rad);
            Vector3 direction = Quaternion.LookRotation(reflected) * new Vector3(spread.x, spread.y, 1f).normalized;

            if (Vector3.Dot(incoming, direction) <= Random.Range(0.2f, 0.6f))
            {
                return false;
            }

            float retention = Mathf.Clamp(1f - Mathf.Abs(Vector3.Dot(incoming, hit.normal)) * 0.8f, 0f, 0.5f);
            _allowRicochet = false;
            _position = hit.point + hit.normal * _weaponData.RicochetSurfaceOffsetMeters;
            _velocity = direction * (impactVelocity.magnitude * retention);
            ResetTrajectory();
            transform.SetPositionAndRotation(_position, ResolveRotation(_velocity, transform.rotation));
            MoveTracer(_position);
            return true;
        }

        private void ResetTrajectory()
        {
            _trajectoryOrigin = _position;
            _trajectoryVelocity = _velocity;
            _trajectoryTime = 0f;
        }

        private bool TryGetClosestHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit closestHit)
        {
            int hitCount = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, distance, _weaponData.BallisticHitMask, QueryTriggerInteraction.Ignore);
            if (hitCount < HIT_BUFFER_SIZE)
            {
                return TrySelectClosestHit(_hitBuffer, hitCount, out closestHit);
            }

            RaycastHit[] allHits = Physics.RaycastAll(origin, direction, distance, _weaponData.BallisticHitMask, QueryTriggerInteraction.Ignore);
            return TrySelectClosestHit(allHits, allHits.Length, out closestHit);
        }

        private bool TrySelectClosestHit(RaycastHit[] hits, int hitCount, out RaycastHit closestHit)
        {
            closestHit = default;
            float closestDistance = float.PositiveInfinity;
            bool hasHit = false;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = hits[i];

                if (hit.collider == null || _ignoredColliders.Contains(hit.collider) || hit.distance >= closestDistance)
                {
                    continue;
                }

                closestHit = hit;
                closestDistance = hit.distance;
                hasHit = true;
            }

            return hasHit;
        }

        private void AddIgnoredColliders(Transform root)
        {
            if (root == null)
            {
                return;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                _ignoredColliders.Add(colliders[i]);
            }
        }

        private void DisableAndIgnoreProjectileColliders()
        {
            Collider[] projectileColliders = GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < projectileColliders.Length; i++)
            {
                Collider projectileCollider = projectileColliders[i];
                _ignoredColliders.Add(projectileCollider);
                projectileCollider.enabled = false;
            }
        }

        private void DisableProjectileRigidbodyPhysics()
        {
            Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>(true);

            for (int i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].useGravity = false;
                rigidbodies[i].isKinematic = true;
            }
        }

        private void OnDestroy()
        {
            CompleteTracer();
        }

        private void MoveTracer(Vector3 position)
        {
            _tracerView?.MoveTo(position, _distanceTravelled, _elapsedLifetime);
        }

        private void CompleteTracer()
        {
            if (_tracerView == null)
            {
                return;
            }

            _tracerView.Complete();
            _tracerView = null;
        }

        private void DrawMarker(Vector3 position, float size, Color color)
        {
            float duration = _weaponData.BallisticDebugDurationSeconds;
            Debug.DrawLine(position - Vector3.right * size, position + Vector3.right * size, color, duration);
            Debug.DrawLine(position - Vector3.up * size, position + Vector3.up * size, color, duration);
            Debug.DrawLine(position - Vector3.forward * size, position + Vector3.forward * size, color, duration);
        }

        private static Quaternion ResolveRotation(Vector3 velocity, Quaternion fallback)
        {
            return velocity.sqrMagnitude <= MIN_SPEED_SQUARED ? fallback : Quaternion.LookRotation(velocity.normalized, Vector3.up);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsNaN(value.x) == false && float.IsInfinity(value.x) == false && float.IsNaN(value.y) == false && float.IsInfinity(value.y) == false && float.IsNaN(value.z) == false && float.IsInfinity(value.z) == false;
        }
    }
}
