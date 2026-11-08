using System;
using System.Collections.Generic;
using RaidIntoDarkness.FirstPerson;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StalkerPlayerController))]
    public sealed class StalkerAnomalyResponse : MonoBehaviour
    {
        private const int ELECTRIC_MINE_ID = 98324;
        private const int ELECTRIC_ID = 98325;
        private const float CONTACT_EXPIRY = 0.2f;

        [SerializeField] private StalkerPlayerController _player;
        [SerializeField] private StalkerAnomalyAudio _audio;
        [SerializeField] private StalkerAnomalyScreenEffects _screen;

        private readonly Dictionary<AnomalyProximityZone, float> _zones = new Dictionary<AnomalyProximityZone, float>();
        private readonly List<AnomalyProximityZone> _expiredZones = new List<AnomalyProximityZone>();
        private readonly RaycastHit[] _rayHits = new RaycastHit[32];

        private Transform _spine;
        private CharacterController _bodyCollider;

        public float ElectricFieldFactor { get; private set; }

        public event Action<AnomalyInfluenceProfile> Impact;

        private bool IsAlive => _player != null && _player.isActiveAndEnabled && _player.Motor != null && (_player.Motor.Condition == null || _player.Motor.Condition.Health > 0f);

        public void RefreshZone(AnomalyProximityZone zone)
        {
            if (isActiveAndEnabled && zone != null)
            {
                _zones[zone] = Time.time + CONTACT_EXPIRY;
            }
        }

        public void RemoveZone(AnomalyProximityZone zone)
        {
            if (ReferenceEquals(zone, null) == false)
            {
                _zones.Remove(zone);
                if (_screen != null)
                {
                    _screen.RemoveImmediately(zone.GetEntityId().GetHashCode());
                }
            }
        }

        public void ApplyPulse(AnomalyInfluenceProfile profile)
        {
            if (isActiveAndEnabled == false || IsAlive == false || profile == null)
            {
                return;
            }
            bool shock = profile.Kind == AnomalyInfluenceKind.Springboard || profile.Kind == AnomalyInfluenceKind.Electric || profile.Kind == AnomalyInfluenceKind.Vortex;
            if (shock)
            {
                _player.Motor.ApplyAnomalyShock(profile.LaunchHeight <= 0f);
            }
            if (profile.LaunchHeight > 0f)
            {
                _player.Motor.LaunchFromAnomaly(profile.LaunchHeight, profile.LaunchAirAcceleration);
            }
            if (profile.ActorPainEnabled && _audio != null)
            {
                float height = _player.Motor.Stance != null ? _player.Motor.Stance.CameraHeight : 0f;
                _audio.PlayReaction(profile.Kind, _player.transform.position + Vector3.up * height);
            }
            if (profile.ImpactPostProcess != null && _screen != null)
            {
                _screen.PlayOnce(UnityEngine.Random.Range(655808, 655821), profile.ImpactPostProcess, 1f);
            }
            Impact?.Invoke(profile);
        }

        private void Awake()
        {
            if (_player == null)
            {
                _player = GetComponent<StalkerPlayerController>();
            }
            _bodyCollider = GetComponent<CharacterController>();
        }

        private void OnEnable()
        {
            if (_player != null && _player.Rig != null)
            {
                _player.Rig.OutfitApplied += OnOutfitApplied;
            }
            FindSpine();
        }

        private void OnDisable()
        {
            if (_player != null && _player.Rig != null)
            {
                _player.Rig.OutfitApplied -= OnOutfitApplied;
            }
            _zones.Clear();
            _expiredZones.Clear();
            ElectricFieldFactor = 0f;
            if (_player != null && _player.Motor != null)
            {
                _player.Motor.SetAnomalyControl(Vector3.zero);
            }
            if (_screen != null)
            {
                _screen.Clear();
            }
            if (_player != null && _player.CameraEffects != null)
            {
                _player.CameraEffects.StopAnomalyShake();
            }
        }

        private void LateUpdate()
        {
            _expiredZones.Clear();
            ElectricFieldFactor = 0f;
            Vector3 attraction = Vector3.zero;
            AnomalyInfluenceProfile electricProfile = null;
            foreach (KeyValuePair<AnomalyProximityZone, float> contact in _zones)
            {
                AnomalyProximityZone zone = contact.Key;
                if (IsAlive == false || zone == null || zone.IsAvailable == false || contact.Value < Time.time || zone.Profile == null)
                {
                    _expiredZones.Add(zone);
                    continue;
                }
                AnomalyInfluenceProfile profile = zone.Profile;
                if (profile.PullStrength > 0f && zone.IsPulling(_player))
                {
                    Vector3 bodyCenter = _bodyCollider != null ? _bodyCollider.bounds.center : _player.transform.position;
                    attraction += AnomalyAttraction.Evaluate(zone.Position - bodyCenter, zone.ZoneRadius, profile.ActorBlowoutRadius, profile.PullStrength);
                }
                UpdateInherent(zone, profile);
                float distanceSquared = (_player.transform.position - zone.Position).sqrMagnitude;
                float radiusSquared = zone.ProximityRadius * zone.ProximityRadius;
                if (distanceSquared >= radiusSquared)
                {
                    continue;
                }
                float proximity = 1f - distanceSquared / radiusSquared;
                if (profile.Kind == AnomalyInfluenceKind.Electric)
                {
                    ElectricFieldFactor += proximity * proximity * ObstacleReduction(zone, Mathf.Sqrt(distanceSquared));
                    electricProfile = profile;
                }
                else if ((profile.Kind == AnomalyInfluenceKind.Springboard || profile.Kind == AnomalyInfluenceKind.Vortex) && _player.CameraEffects != null)
                {
                    _player.CameraEffects.RefreshAnomalyShake(profile.Earthquake, 0.03f + proximity);
                }
            }
            foreach (AnomalyProximityZone zone in _expiredZones)
            {
                RemoveZone(zone);
            }
            ElectricFieldFactor = Mathf.Clamp01(ElectricFieldFactor);
            if (_player != null && _player.Motor != null)
            {
                _player.Motor.SetAnomalyControl(attraction);
            }
            if (electricProfile != null && _screen != null)
            {
                float factor = (ElectricFieldFactor + 0.05f) * 0.66f;
                _screen.Refresh(ELECTRIC_MINE_ID, electricProfile.ElectricMine, factor * 0.5f, CONTACT_EXPIRY);
                _screen.Refresh(ELECTRIC_ID, electricProfile.Electric, factor, CONTACT_EXPIRY);
            }
        }

        private void UpdateInherent(AnomalyProximityZone zone, AnomalyInfluenceProfile profile)
        {
            if (_screen == null || profile.InherentPostProcess == null)
            {
                return;
            }
            float distance = (_player.transform.position - Vector3.up * 0.9f - zone.Center).magnitude;
            float minimum = zone.ZoneRadius * profile.RadiusMin;
            float maximum = zone.ZoneRadius * profile.RadiusMax;
            if (distance >= maximum || maximum <= minimum)
            {
                _screen.RemoveImmediately(zone.GetEntityId().GetHashCode());
                return;
            }
            float factor = Mathf.Clamp((maximum - distance) / (maximum - minimum), 0.01f, 1f);
            _screen.Refresh(zone.GetEntityId().GetHashCode(), profile.InherentPostProcess, factor, CONTACT_EXPIRY);
        }

        private float ObstacleReduction(AnomalyProximityZone zone, float distance)
        {
            Vector3 actor = _spine != null ? _spine.position : _player.transform.position + Vector3.up * 0.5f;
            Vector3 anomaly = zone.Position;
            float forward = RayDistance(anomaly + Vector3.up * 0.3f, actor, distance, zone);
            float reverse = RayDistance(actor, anomaly, distance, zone);
            if (forward <= 0f)
            {
                return 1f;
            }
            float thickness = distance - forward - reverse;
            return Mathf.Clamp01(Mathf.Pow(Mathf.Max(0f, 1f - thickness / zone.ProximityRadius), 3.5f));
        }

        private float RayDistance(Vector3 origin, Vector3 target, float maximum, AnomalyProximityZone zone)
        {
            Vector3 direction = target - origin;
            if (direction.sqrMagnitude < 0.000001f || maximum <= 0f)
            {
                return 0f;
            }
            int mask = _player.Profile != null ? _player.Profile.Unity.CollisionMask.value : Physics.DefaultRaycastLayers;
            int count = Physics.RaycastNonAlloc(origin, direction.normalized, _rayHits, maximum, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = _rayHits[index];
                if (hit.collider != null && hit.collider.transform.IsChildOf(_player.transform) == false && hit.collider.transform.IsChildOf(zone.transform.parent) == false)
                {
                    nearest = Mathf.Min(nearest, hit.distance);
                }
            }
            return float.IsPositiveInfinity(nearest) ? 0f : nearest;
        }

        private void OnOutfitApplied(FirstPersonOutfitEntry entry, bool bodyChanged)
        {
            if (bodyChanged)
            {
                FindSpine();
            }
        }

        private void FindSpine()
        {
            _spine = null;
            Transform body = _player != null && _player.VisibleBody != null ? _player.VisibleBody.BodyRoot : null;
            if (body == null)
            {
                return;
            }
            foreach (Transform bone in body.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "bip01_spine")
                {
                    _spine = bone;
                    break;
                }
            }
        }
    }
}
