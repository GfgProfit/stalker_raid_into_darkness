using RaidIntoDarkness.FirstPerson;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(AnomalyProbe))]
    public sealed class BoltProjectile : MonoBehaviour
    {
        private const float CLEARANCE = 0.005f;

        private Rigidbody _body;
        private float _gravity;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (_body != null && _body.IsSleeping() == false)
            {
                _body.AddForce(Vector3.down * _gravity, ForceMode.Acceleration);
            }
        }

        public static BoltProjectile Launch(BoltDefinition definition, StalkerPlayerController player, Vector3 origin, Vector3 direction, float speed)
        {
            if (definition == null || definition.ProjectilePrefab == null || player == null)
            {
                return null;
            }

            Vector3 position = BoltThrowMath.LaunchPosition(origin, player.transform.rotation);
            Quaternion rotation = direction.sqrMagnitude > 0f ? Quaternion.LookRotation(direction) : player.transform.rotation;
            GameObject instance = Instantiate(definition.ProjectilePrefab, position, rotation);
            BoltProjectile projectile = instance.GetComponent<BoltProjectile>();
            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (projectile == null || body == null)
            {
                Debug.LogError($"{nameof(BoltProjectile)}: invalid projectile prefab '{definition.ProjectilePrefab.name}'.", definition);
                Destroy(instance);
                return null;
            }

            Collider[] colliders = instance.GetComponentsInChildren<Collider>();
            Collider[] ownerColliders = player.GetComponentsInChildren<Collider>();
            float radius = CLEARANCE;
            foreach (Collider collider in colliders)
            {
                radius = Mathf.Max(radius, collider.bounds.extents.magnitude);
                foreach (Collider owner in ownerColliders)
                {
                    Physics.IgnoreCollision(collider, owner);
                }
            }

            Vector3 offset = position - origin;
            int mask = player.Profile != null ? player.Profile.Unity.CollisionMask.value : Physics.DefaultRaycastLayers;
            if (Physics.SphereCast(origin, radius, offset.normalized, out RaycastHit hit, offset.magnitude, mask, QueryTriggerInteraction.Ignore) && hit.collider.transform.IsChildOf(player.transform) == false)
            {
                body.position = origin + offset.normalized * Mathf.Max(0f, hit.distance - CLEARANCE);
            }

            body.linearVelocity = BoltThrowMath.LaunchVelocity(direction, speed, player.Motor != null ? player.Motor.Velocity : Vector3.zero);
            body.useGravity = false;
            projectile._body = body;
            projectile._gravity = player.Profile != null ? player.Profile.Engine.Gravity : Physics.gravity.magnitude;
            body.maxAngularVelocity = 3f * Mathf.PI;
            float azimuth = Random.Range(0f, 2f * Mathf.PI);
            float polar = Random.Range(0f, Mathf.PI);
            float angularSpeed = Random.Range(2f * Mathf.PI, 3f * Mathf.PI);
            float radial = angularSpeed * Mathf.Sin(polar);
            body.angularVelocity = new Vector3(radial * Mathf.Cos(azimuth), radial * Mathf.Sin(azimuth), angularSpeed * Mathf.Cos(polar));
            Destroy(instance, BoltThrowMath.LIFETIME);
            return projectile;
        }
    }
}
