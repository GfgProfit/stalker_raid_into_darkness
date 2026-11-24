using System;
using System.Collections.Generic;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.Player;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    public sealed class GrenadeProjectile : MonoBehaviour
    {
        internal const float MAXIMUM_AIM_DISTANCE = 300f;

        private const int HIT_BUFFER_SIZE = 16;
        private const float LAUNCH_SPEED = 76f;
        private const float BARREL_CLEARANCE = 0.3f;
        private const float MAX_STEERING_TIME = 0.05f;
        private const float MAX_SIMULATION_STEP = 1f / 120f;
        private const float MIN_FLIGHT_TIME = 0.01f;
        private const float COLLISION_RADIUS = 0.02f;
        private const float SURFACE_PROBE_PADDING = 0.005f;
        private const float BOUNCE_SURFACE_OFFSET = 0.005f;
        private const float EXPLOSION_LIFETIME = 11f;
        private const float SOUND_MAX_DISTANCE = 160f;
        private const float THROW_RESTITUTION = 0.34f;
        private const float THROW_REST_SPEED_SQUARED = 1f;
        private const float BOUNCE_SOUND_INTERVAL = 0.08f;
        private const string BOUNCE_MATERIAL = "objects\\grenade";

        private RaycastHit[] _hits = new RaycastHit[HIT_BUFFER_SIZE];
        private PhysicsScene _physicsScene;
        private GrenadeAmmoData _ammo;
        private StalkerSurfaceResolver _bounceResolver;
        private AudioMixerGroup _audioMixerGroup;
        private Transform _owner;
        private Transform _weapon;
        private int _hitMask;
        private Vector3 _origin;
        private Vector3 _launchVelocity;
        private Vector3 _gravity;
        private Vector3 _aimAcceleration;
        private Vector3 _bounceNormal;
        private float _clearanceTime;
        private float _steeringTime;
        private float _steeringEndTime;
        private float _steeringStep = MAX_SIMULATION_STEP;
        private float _aimTime;
        private float _elapsed;
        private float _travelled;
        private float _bounceStartTime;
        private float _detonationTime;
        private float _nextBounceSoundTime;
        private bool _hasBounced;
        private bool _isThrown;
        private bool _resting;
#if UNITY_EDITOR
        private GrenadeShotDebug _debug;
#endif

        internal static void Launch(GameObject visual, GrenadeAmmoData ammo, Ray muzzle, Ray aim, int hitMask, Transform owner, Transform weapon, AudioMixerGroup audioMixerGroup)
        {
            LaunchStandalone(visual, ammo, muzzle, aim, LAUNCH_SPEED, hitMask, owner, weapon, audioMixerGroup);
        }

        internal static void LaunchStandalone(GameObject visual, GrenadeAmmoData ammo, Ray muzzle, Ray aim, float speed, int hitMask, Transform owner, Transform weapon, AudioMixerGroup audioMixerGroup)
        {
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, muzzle.direction);
            GameObject instance = Instantiate(visual, muzzle.origin, rotation);
            GrenadeProjectile projectile = instance.AddComponent<GrenadeProjectile>();
            projectile.BeginFlight(ammo, muzzle, speed, hitMask, owner, weapon, audioMixerGroup);
            if (ammo.FlightTrail != null)
            {
                GameObject trail = Instantiate(ammo.FlightTrail, instance.transform, false);
                if (trail.TryGetComponent(out XRayParticleGroupPlayer group))
                {
                    group.Play(true);
                }
            }

            if (!ammo.FliesStraight)
            {
                projectile.InitializeAimedFlight(muzzle, aim, speed);
                return;
            }

#if UNITY_EDITOR
            if (GrenadeShotDebug.Enabled)
            {
                projectile.CreateStraightDebug(muzzle, speed);
            }
#endif
        }

        internal static void LaunchThrown(GameObject visual, GrenadeAmmoData ammo, Ray release, float speed, int hitMask, Transform owner, Transform weapon, AudioMixerGroup audioMixerGroup)
        {
            GameObject instance = Instantiate(visual, release.origin, Quaternion.FromToRotation(Vector3.forward, release.direction));
            GrenadeProjectile projectile = instance.AddComponent<GrenadeProjectile>();
            projectile.BeginFlight(ammo, release, speed, hitMask, owner, weapon, audioMixerGroup);
            projectile._isThrown = true;
#if UNITY_EDITOR
            if (GrenadeShotDebug.Enabled)
            {
                projectile.CreateThrowDebug(release);
            }
#endif
        }

        private void BeginFlight(GrenadeAmmoData ammo, Ray launch, float speed, int hitMask, Transform owner, Transform weapon, AudioMixerGroup audioMixerGroup)
        {
            _ammo = ammo;
            _audioMixerGroup = audioMixerGroup;
            _owner = owner;
            _weapon = weapon;
            _hitMask = hitMask;
            _physicsScene = gameObject.scene.GetPhysicsScene();
            _origin = launch.origin;
            _launchVelocity = launch.direction * speed;
            _gravity = Physics.gravity;
            _detonationTime = ammo.SelfDestructSeconds;
        }

        private void InitializeAimedFlight(Ray muzzle, Ray aim, float speed)
        {
            Vector3 cameraToMuzzle = muzzle.origin - aim.origin;
            if (TryHit(new Ray(aim.origin, cameraToMuzzle), cameraToMuzzle.magnitude, out RaycastHit obstruction))
            {
#if UNITY_EDITOR
                if (GrenadeShotDebug.Enabled)
                {
                    _debug = new GrenadeShotDebug(AmmoName, _ammo, muzzle, obstruction.point, true, new[] { muzzle.origin });
                }
#endif
                FinishFlight(obstruction.point, obstruction.normal);
                return;
            }

            bool hasTarget = TryHit(aim, MAXIMUM_AIM_DISTANCE, out RaycastHit aimedHit);
            Vector3 target = hasTarget ? aimedHit.point : aim.GetPoint(MAXIMUM_AIM_DISTANCE);
            Vector3 offset = target - muzzle.origin;
            if (Vector3.Dot(offset, muzzle.direction) <= 0f)
            {
                _clearanceTime = BARREL_CLEARANCE / speed;
                _aimTime = _clearanceTime;
            }
            else
            {
                float distance = offset.magnitude;
                _aimTime = Mathf.Max(MIN_FLIGHT_TIME, distance / speed);
                _clearanceTime = Mathf.Min(BARREL_CLEARANCE, distance * 0.25f) / speed;
                float timeAfterClearance = _aimTime - _clearanceTime;
                _steeringTime = Mathf.Min(MAX_STEERING_TIME, timeAfterClearance * 0.5f);
                float steeringDisplacement = _steeringTime * (timeAfterClearance - 0.5f * _steeringTime);
                Vector3 drop = _gravity * (0.5f * timeAfterClearance * timeAfterClearance);
                _aimAcceleration = (offset - _launchVelocity * _aimTime - drop) / steeringDisplacement;
                float acceleration = (_gravity + _aimAcceleration).magnitude;
                if (acceleration > 0f && float.IsFinite(acceleration))
                {
                    _steeringStep = Mathf.Min(MAX_SIMULATION_STEP, Mathf.Sqrt(2f * COLLISION_RADIUS / acceleration));
                }
            }

            _steeringEndTime = _clearanceTime + _steeringTime;

#if UNITY_EDITOR
            if (GrenadeShotDebug.Enabled)
            {
                const int PLAN_SAMPLES = 72;
                float duration = hasTarget && _aimTime > _clearanceTime ? Mathf.Min(_aimTime, _ammo.SelfDestructSeconds) : _ammo.SelfDestructSeconds;
                var points = new Vector3[PLAN_SAMPLES + 1];
                for (int i = 0; i <= PLAN_SAMPLES; i++)
                {
                    points[i] = GetPosition(duration * i / PLAN_SAMPLES);
                }

                _debug = new GrenadeShotDebug(AmmoName, _ammo, muzzle, target, hasTarget, points);
            }
#endif
        }

        private void FixedUpdate()
        {
            float end = Mathf.Min(_elapsed + Time.fixedDeltaTime, _detonationTime);
            if (_resting)
            {
                _elapsed = end;
#if UNITY_EDITOR
                _debug?.Advance(transform.position, transform.position, _elapsed, _travelled, 0f, false);
#endif
            }

            while (_elapsed < end)
            {
                float step = !_hasBounced && _elapsed >= _clearanceTime && _elapsed < _steeringEndTime ? _steeringStep : MAX_SIMULATION_STEP;
                float nextTime = Mathf.Min(end, _elapsed + step);
                if (!_hasBounced && _elapsed < _clearanceTime && nextTime > _clearanceTime)
                {
                    nextTime = _clearanceTime;
                }
                else if (!_hasBounced && _elapsed < _steeringEndTime && nextTime > _steeringEndTime)
                {
                    nextTime = _steeringEndTime;
                }
                else if (!_hasBounced && _elapsed < _aimTime && nextTime > _aimTime)
                {
                    nextTime = _aimTime;
                }

                if (!(nextTime > _elapsed))
                {
                    nextTime = end;
                }

                Vector3 position = transform.position;
                Vector3 nextPosition = GetPosition(nextTime);
                Vector3 segment = nextPosition - position;
                float distance = segment.magnitude;
                if (TryHit(new Ray(position, segment), distance, out RaycastHit hit, sweepProjectile: true))
                {
                    _travelled += hit.distance;
                    float hitTime = Mathf.Lerp(_elapsed, nextTime, hit.distance / distance);
#if UNITY_EDITOR
                    _debug?.Advance(position, hit.point, hitTime, _travelled, hit.distance, false);
#endif
                    if (_isThrown)
                    {
                        if (_ammo.ExplodeOnImpact)
                        {
                            _elapsed = hitTime;
                            if (_elapsed < _ammo.ArmingSeconds || _travelled < _ammo.ArmingDistance)
                            {
                                PlayBounceSound(hit, hitTime);
                            }

                            FinishFlight(hit.point, hit.normal);
                            return;
                        }

                        BounceThrow(hit, hitTime, nextTime);
                        if (_resting)
                        {
                            break;
                        }

                        continue;
                    }

                    if (TryBeginBounce(hit, hitTime))
                    {
                        end = Mathf.Min(end, _detonationTime);
                        continue;
                    }

                    FinishFlight(hit.point, hit.normal);
                    return;
                }

                _travelled += distance;
                _elapsed = nextTime;
#if UNITY_EDITOR
                _debug?.Advance(position, nextPosition, _elapsed, _travelled, distance, nextTime == end || nextTime == _clearanceTime);
#endif
                if (segment.sqrMagnitude > 0.000001f)
                {
                    transform.SetPositionAndRotation(nextPosition, Quaternion.FromToRotation(Vector3.forward, segment.normalized));
                }
                else
                {
                    transform.position = nextPosition;
                }
            }

            if (_elapsed >= _detonationTime)
            {
                Vector3 point = _resting ? _origin - _bounceNormal * (COLLISION_RADIUS + BOUNCE_SURFACE_OFFSET) : transform.position;
                FinishFlight(point, _hasBounced || _resting ? _bounceNormal : Vector3.up, _resting);
            }
        }

        private void BounceThrow(RaycastHit hit, float hitTime, float nextTime)
        {
            Vector3 velocity = _launchVelocity + _gravity * (hitTime - _bounceStartTime);
            if (Mathf.Abs(Vector3.Dot(velocity, hit.normal)) > 1f)
            {
                PlayBounceSound(hit, hitTime);
            }

            _launchVelocity = Vector3.Reflect(velocity, hit.normal) * THROW_RESTITUTION;
            _bounceNormal = hit.normal;
            _bounceStartTime = nextTime;
            _elapsed = nextTime;
            _origin = hit.point + hit.normal * (COLLISION_RADIUS + BOUNCE_SURFACE_OFFSET);
            transform.position = _origin;
            _resting = hit.normal.y > 0.5f && _launchVelocity.sqrMagnitude <= THROW_REST_SPEED_SQUARED;
#if UNITY_EDITOR
            _debug?.MarkBounce(hit.point);
#endif
        }

        private bool TryBeginBounce(RaycastHit hit, float hitTime)
        {
            float height = _ammo.ImpactBounceHeight;
            if (_hasBounced || _travelled < _ammo.ArmingDistance || hitTime >= _detonationTime || height <= 0f || !float.IsFinite(height))
            {
                return false;
            }

            float gravity = _gravity.magnitude;
            if (gravity <= 0f || !float.IsFinite(gravity))
            {
                return false;
            }

            float riseTime = Mathf.Sqrt(2f * (height / gravity));
            float bounceEnd = hitTime + riseTime;
            if (!(bounceEnd > hitTime) || !float.IsFinite(bounceEnd))
            {
                return false;
            }

            _hasBounced = true;
            PlayBounceSound(hit, hitTime);
            _bounceNormal = hit.normal;
            _bounceStartTime = hitTime;
            _elapsed = hitTime;
            _detonationTime = Mathf.Min(_detonationTime, bounceEnd);
            _origin = hit.point + _bounceNormal * (COLLISION_RADIUS + BOUNCE_SURFACE_OFFSET);
            _launchVelocity = -_gravity * riseTime;
            transform.position = _origin;
#if UNITY_EDITOR
            _debug?.MarkBounce(hit.point);
#endif
            return true;
        }

        private Vector3 GetPosition(float time)
        {
            if (_ammo.FliesStraight)
            {
                return _origin + _launchVelocity * time;
            }

            if (_hasBounced || _isThrown)
            {
                float riseTime = time - _bounceStartTime;
                return _origin + _launchVelocity * riseTime + _gravity * (0.5f * riseTime * riseTime);
            }

            float gravityTime = Mathf.Max(0f, time - _clearanceTime);
            float guidedTime = Mathf.Min(gravityTime, _steeringTime);
            return _origin + _launchVelocity * time + _gravity * (0.5f * gravityTime * gravityTime) + _aimAcceleration * (guidedTime * (gravityTime - 0.5f * guidedTime));
        }

        private bool TryHit(Ray ray, float distance, out RaycastHit closest, bool sweepProjectile = false)
        {
            closest = default;
            if (distance <= 0f)
            {
                return false;
            }

            int count = CollectHits(ray, distance, sweepProjectile);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                Collider collider = hit.collider;
                if (collider == null || hit.distance > distance)
                {
                    continue;
                }

                Transform target = collider.transform;
                if (target.IsChildOf(transform) || _owner != null && target.IsChildOf(_owner) || _weapon != null && target.IsChildOf(_weapon))
                {
                    continue;
                }

                if (sweepProjectile && hit.distance == 0f && !ResolveStartingOverlap(ray, collider, ref hit))
                {
                    continue;
                }

                closest = hit;
                distance = hit.distance;
                found = true;
            }

            if (found && sweepProjectile && closest.distance > 0f)
            {
                RefineSurfaceContact(ray.GetPoint(closest.distance), closest.collider, ref closest);
            }

            return found;
        }

        private int CollectHits(Ray ray, float distance, bool sweepProjectile)
        {
            while (true)
            {
                int count = sweepProjectile ? _physicsScene.SphereCast(ray.origin, COLLISION_RADIUS, ray.direction, _hits, distance, _hitMask, QueryTriggerInteraction.Ignore) : _physicsScene.Raycast(ray.origin, ray.direction, _hits, distance, _hitMask, QueryTriggerInteraction.Ignore);
                if (count < _hits.Length)
                {
                    return count;
                }

                Array.Resize(ref _hits, checked(_hits.Length * 2));
            }
        }

        private static bool ResolveStartingOverlap(Ray ray, Collider collider, ref RaycastHit hit)
        {
            if (collider is BoxCollider or SphereCollider or CapsuleCollider || collider is MeshCollider mesh && mesh.convex)
            {
                Vector3 point = collider.ClosestPoint(ray.origin);
                Vector3 normal = (ray.origin - point).normalized;
                if (normal.sqrMagnitude > 0f)
                {
                    if (Vector3.Dot(normal, ray.direction) > 0f)
                    {
                        return false;
                    }

                    hit.point = point;
                    hit.normal = normal;
                    RefineSurfaceContact(ray.origin, collider, ref hit);
                    return true;
                }
            }

            float retreat = Vector3.Distance(collider.bounds.center, ray.origin) + collider.bounds.extents.magnitude + SURFACE_PROBE_PADDING;
            if (collider.Raycast(new Ray(ray.GetPoint(-retreat), ray.direction), out RaycastHit surface, retreat + COLLISION_RADIUS))
            {
                hit.point = surface.point;
                hit.normal = surface.normal;
            }
            else
            {
                hit.point = ray.origin;
                hit.normal = -ray.direction;
            }

            return true;
        }

        private static void RefineSurfaceContact(Vector3 center, Collider collider, ref RaycastHit hit)
        {
            Vector3 toContact = hit.point - center;
            float distance = toContact.magnitude;
            if (distance > 0f && collider.Raycast(new Ray(center, toContact), out RaycastHit surface, distance + SURFACE_PROBE_PADDING))
            {
                hit.point = surface.point;
                hit.normal = surface.normal;
            }
        }

        private void FinishFlight(Vector3 point, Vector3 normal, bool surfaceHit = true)
        {
            enabled = false;
            Destroy(gameObject);
            bool armed = _travelled >= _ammo.ArmingDistance && _elapsed >= _ammo.ArmingSeconds;
#if UNITY_EDITOR
            _debug?.Finish(point, normal, armed, surfaceHit);
#endif
            if (!armed)
            {
                return;
            }

            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal) * _ammo.ExplosionPrefab.transform.rotation;
            GameObject explosion = Instantiate(_ammo.ExplosionPrefab, point, rotation);
            Destroy(explosion, EXPLOSION_LIFETIME);
            AudioClip transient = _ammo.SelectExplosionSound();
            AudioClip body = _ammo.SelectExplosionBodySound();
            PlayWorldSound("Grenade Explosion Audio", body != null ? body : transient, point, SOUND_MAX_DISTANCE, body != null ? transient : null);
            var damage = new GrenadeExplosionDamage(_ammo, point, surfaceHit ? normal : Vector3.zero, _hitMask, _weapon);
#if UNITY_EDITOR
            damage.DebugShot = _debug;
#endif
            damage.Apply();
        }

        private void PlayBounceSound(RaycastHit hit, float hitTime)
        {
            StalkerCollideSoundLibrary library = _ammo.BounceSounds;
            if (library == null || hitTime < _nextBounceSoundTime)
            {
                return;
            }

            _bounceResolver ??= new StalkerSurfaceResolver(_ammo.BounceSurfaces);
            StalkerSurfaceType surface = _bounceResolver.Resolve(hit, out _);
            IReadOnlyList<AudioClip> clips = library.Find(BOUNCE_MATERIAL, surface) ?? library.Find(BOUNCE_MATERIAL, StalkerSurfaceType.Default);
            if (clips == null || clips.Count == 0)
            {
                return;
            }

            _nextBounceSoundTime = hitTime + BOUNCE_SOUND_INTERVAL;
            PlayWorldSound("Grenade Bounce Audio", clips[UnityEngine.Random.Range(0, clips.Count)], hit.point, library.MaxDistance);
        }

        private void PlayWorldSound(string objectName, AudioClip clip, Vector3 point, float maxDistance, AudioClip layer = null)
        {
            if (clip == null)
            {
                return;
            }

            var emitter = new GameObject(objectName);
            emitter.transform.position = point;
            AudioSource source = emitter.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = maxDistance;
            source.outputAudioMixerGroup = _audioMixerGroup;
            source.clip = clip;
            source.Play();
            if (layer != null)
            {
                source.PlayOneShot(layer);
            }

            Destroy(emitter, Mathf.Max(clip.length, layer != null ? layer.length : 0f) + 0.1f);
        }

#if UNITY_EDITOR
        private void CreateStraightDebug(Ray muzzle, float speed)
        {
            float distance = speed * _detonationTime;
            bool hasSurface = TryHit(muzzle, distance, out RaycastHit hit, sweepProjectile: true);
            Vector3 target = hasSurface ? hit.point : muzzle.GetPoint(distance);
            _debug = new GrenadeShotDebug(AmmoName, _ammo, muzzle, target, hasSurface, new[] { muzzle.origin, target });
        }

        private void CreateThrowDebug(Ray release)
        {
            const int THROW_SAMPLES = 72;
            var points = new Vector3[THROW_SAMPLES + 1];
            for (int i = 0; i <= THROW_SAMPLES; i++)
            {
                points[i] = GetPosition(_detonationTime * i / THROW_SAMPLES);
            }

            _debug = new GrenadeShotDebug(AmmoName, _ammo, release, points[points.Length - 1], false, points, true);
        }

        private string AmmoName => gameObject.name.Replace("(Clone)", string.Empty);
#endif
    }
}
