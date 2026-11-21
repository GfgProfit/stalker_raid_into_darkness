using System;
using System.Collections.Generic;
using RaidIntoDarkness.Effects;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class AnomalyPlayerReaction : MonoBehaviour, IXRayAnomalyActivitySource
    {
        [SerializeField] private AnomalyVisual _visual;
        [SerializeField] private AnomalyInfluenceProfile _influenceProfile;
        [SerializeField, Min(0f)] private float _awakingTime;
        [SerializeField, Min(0f)] private float _blowoutTime;
        [SerializeField, Min(0f)] private float _accumulateTime;
        [SerializeField] private bool _ignoreSmallObjects;
        [SerializeField] private bool _actorOnly;
        [SerializeField] private bool _ignoreNonlivingObjects;
        [SerializeField, Min(0f)] private float _minimumPlayerSpeed;
        [SerializeField] private float _smallObjectIgnoreDelay = 0.5f;
        [SerializeField] private float _nonlivingIdleStopDelay = 0.1f;
        [SerializeField] private bool _repeatNonlivingIdleStop;
        [SerializeField] private bool _randomizeRadius;
        [SerializeField, Min(0)] private int _randomRadiusMin = 2;
        [SerializeField, Min(0)] private int _randomRadiusMax = 3;

        private readonly Dictionary<Collider, StalkerPlayerController> _contacts = new Dictionary<Collider, StalkerPlayerController>();
        private readonly List<Collider> _expiredContacts = new List<Collider>();
        private readonly HashSet<StalkerPlayerController> _affectedPlayers = new HashSet<StalkerPlayerController>();
        private readonly Dictionary<Collider, ProbeContact> _probeContacts = new Dictionary<Collider, ProbeContact>();
        private readonly HashSet<AnomalyProbe> _affectedProbes = new HashSet<AnomalyProbe>();

        private AnomalyCycle _cycle;
        private float _pulseAt;
        private bool _pulsePending;
        private bool _sourceActive = true;
        private XRayAnomalyActivationVolume _activationVolume;
        private Collider[] _localTriggerShapes;
        private ulong _dischargeSequence;
        private bool _confirmedPulse;
        private bool _presentingDischarge;

        public event Action<StalkerPlayerController, ulong> HitConfirmed;

        public AnomalyPhase Phase => _cycle != null ? _cycle.Phase : AnomalyPhase.Idle;
        public float CooldownRemaining => _cycle != null ? _cycle.GetCooldownRemaining(Time.time) : 0f;
        public bool IsReady => CanDetect && Phase == AnomalyPhase.Idle;
        public AnomalyInfluenceProfile InfluenceProfile => _influenceProfile;
        public bool IsInfluencing => CanDetect;
        public bool CanDeactivate => Phase == AnomalyPhase.Idle;

        public void CollectActivePlayers(List<StalkerPlayerController> players)
        {
            players.Clear();
            if (CanDetect == false)
            {
                return;
            }
            foreach (KeyValuePair<Collider, StalkerPlayerController> contact in _contacts)
            {
                if (IsContactActive(contact.Key, contact.Value) && players.Contains(contact.Value) == false && (_minimumPlayerSpeed <= 0f || contact.Value.Motor != null && contact.Value.Motor.ActualVelocity > _minimumPlayerSpeed))
                {
                    players.Add(contact.Value);
                }
            }
        }

        public void SetSourceActive(bool active)
        {
            if (_sourceActive == active)
            {
                return;
            }
            _sourceActive = active;
            if (_visual == null)
            {
                _visual = GetComponent<AnomalyVisual>();
            }
            if (_visual != null)
            {
                _visual.SetSourceActive(active);
            }
            if (active == false)
            {
                ResetCycle();
            }
        }

        public bool IsTouching(StalkerPlayerController player)
        {
            foreach (KeyValuePair<Collider, StalkerPlayerController> contact in _contacts)
            {
                if (contact.Value == player && IsContactActive(contact.Key, player))
                {
                    return true;
                }
            }
            return false;
        }

        private bool CanDetect => _sourceActive && isActiveAndEnabled && _visual != null && _visual.isActiveAndEnabled && _visual.IsVisible;

        private void Awake()
        {
            _activationVolume = GetComponent<XRayAnomalyActivationVolume>();
            if (_activationVolume != null)
            {
                foreach (Collider shape in _activationVolume.ActualShapes)
                {
                    AnomalyTriggerCollision.Configure(shape);
                }
            }
            else
            {
                _localTriggerShapes = GetComponents<Collider>();
                foreach (Collider shape in _localTriggerShapes)
                {
                    AnomalyTriggerCollision.Configure(shape);
                }
            }
            if (_visual == null)
            {
                _visual = GetComponent<AnomalyVisual>();
            }
            if (_randomizeRadius)
            {
                SphereCollider volume = GetComponent<SphereCollider>();
                if (volume != null)
                {
                    int minimum = Mathf.Max(0, _randomRadiusMin);
                    int maximum = Mathf.Max(minimum, _randomRadiusMax);
                    volume.radius = UnityEngine.Random.Range(minimum, maximum + 1);
                }
            }
        }

        private void OnEnable()
        {
            _cycle = new AnomalyCycle(_awakingTime, _blowoutTime, _accumulateTime);
            if (_visual != null)
            {
                _visual.DischargeStarted += OnDischargeStarted;
                _visual.Hidden += ResetCycle;
            }
        }

        private void OnDisable()
        {
            if (_visual != null)
            {
                _visual.DischargeStarted -= OnDischargeStarted;
                _visual.Hidden -= ResetCycle;
            }
            ResetCycle();
        }

        private void ResetCycle()
        {
            _pulsePending = false;
            _confirmedPulse = false;
            foreach (StalkerPlayerController player in _contacts.Values)
            {
                if (player != null && _visual != null)
                {
                    _visual.StopContact(player.transform);
                }
            }
            _contacts.Clear();
            foreach (ProbeContact contact in _probeContacts.Values)
            {
                if (contact.Probe != null && _visual != null)
                {
                    _visual.StopContact(contact.Probe.transform, true);
                }
            }
            _probeContacts.Clear();
            _expiredContacts.Clear();
            _affectedPlayers.Clear();
            _affectedProbes.Clear();
            if (_cycle != null)
            {
                _cycle.Reset();
            }
        }

        private void Update()
        {
            if (CanDetect == false)
            {
                ResetCycle();
                return;
            }

            RemoveExpiredContacts();
            AnomalyPhase previous = _cycle.Phase;
            if (_cycle.Tick(Time.time, HasActivePlayer() || HasActiveProbe()))
            {
                _dischargeSequence++;
                _confirmedPulse = true;
                SchedulePulse();
                _presentingDischarge = true;
                try
                {
                    _visual.PlayDischarge();
                }
                finally
                {
                    _presentingDischarge = false;
                }
            }
            if (_cycle.Phase != previous)
            {
                if (_cycle.Phase == AnomalyPhase.Awaking)
                {
                    _visual.PlayAwaking();
                }
                else if (_cycle.Phase == AnomalyPhase.Accumulate)
                {
                    _visual.PlayAccumulate();
                }
            }
            if (_pulsePending && Time.time >= _pulseAt)
            {
                ApplyPulse();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            RegisterContact(other);
        }

        private void OnTriggerStay(Collider other)
        {
            RegisterContact(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null || (!_probeContacts.ContainsKey(other) && !_contacts.ContainsKey(other)) || _activationVolume != null && _activationVolume.Overlaps(other))
            {
                return;
            }
            if (_probeContacts.TryGetValue(other, out ProbeContact probeContact))
            {
                _probeContacts.Remove(other);
                if (probeContact.Probe != null)
                {
                    _visual.StopContact(probeContact.Probe.transform, true);
                }
            }
            if (_contacts.TryGetValue(other, out StalkerPlayerController player))
            {
                _contacts.Remove(other);
                if (player != null && IsTouching(player) == false)
                {
                    _visual.StopContact(player.transform);
                }
            }
        }

        private void RegisterContact(Collider other)
        {
            if (CanDetect == false || other == null || other.enabled == false || other.gameObject.activeInHierarchy == false || _contacts.ContainsKey(other) || _probeContacts.ContainsKey(other))
            {
                return;
            }

            StalkerPlayerController player = other.GetComponentInParent<StalkerPlayerController>();
            if (player == null || player.isActiveAndEnabled == false)
            {
                RegisterProbe(other);
                return;
            }
            if (IsWithinSourceShapes(other) == false)
            {
                return;
            }

            bool entered = true;
            foreach (KeyValuePair<Collider, StalkerPlayerController> contact in _contacts)
            {
                if (contact.Value == player && IsContactActive(contact.Key, contact.Value))
                {
                    entered = false;
                    break;
                }
            }
            _contacts.Add(other, player);
            if (entered)
            {
                _visual.PlayEntrance(player.transform);
            }
        }

        private void RemoveExpiredContacts()
        {
            _expiredContacts.Clear();
            foreach (KeyValuePair<Collider, StalkerPlayerController> contact in _contacts)
            {
                if (IsContactActive(contact.Key, contact.Value) == false)
                {
                    _expiredContacts.Add(contact.Key);
                }
            }
            foreach (Collider collider in _expiredContacts)
            {
                StalkerPlayerController player = _contacts[collider];
                _contacts.Remove(collider);
                if (player != null && IsTouching(player) == false)
                {
                    _visual.StopContact(player.transform);
                }
            }
            _expiredContacts.Clear();
            foreach (KeyValuePair<Collider, ProbeContact> contact in _probeContacts)
            {
                if (IsProbeActive(contact.Key, contact.Value.Probe) == false)
                {
                    _expiredContacts.Add(contact.Key);
                }
                else if ((contact.Value.IdleStopped == false || _repeatNonlivingIdleStop) && _nonlivingIdleStopDelay >= 0f && Time.time - contact.Value.EnteredAt > _nonlivingIdleStopDelay)
                {
                    _visual.StopContact(contact.Value.Probe.transform, true);
                    contact.Value.IdleStopped = true;
                }
            }
            foreach (Collider collider in _expiredContacts)
            {
                ProbeContact contact = _probeContacts[collider];
                if (contact.Probe != null)
                {
                    _visual.StopContact(contact.Probe.transform, true);
                }
                _probeContacts.Remove(collider);
            }
        }

        private void RegisterProbe(Collider collider)
        {
            if (_actorOnly)
            {
                return;
            }
            AnomalyProbe probe = collider.GetComponentInParent<AnomalyProbe>();
            if (probe == null || probe.isActiveAndEnabled == false || IsWithinSourceShapes(collider) == false)
            {
                return;
            }
            _probeContacts.Add(collider, new ProbeContact(probe, Time.time));
            _visual.PlayEntrance(probe.transform, true);
        }

        private bool HasActiveProbe()
        {
            if (_actorOnly || _ignoreNonlivingObjects || _minimumPlayerSpeed > 0f)
            {
                return false;
            }
            foreach (KeyValuePair<Collider, ProbeContact> contact in _probeContacts)
            {
                if (IsProbeActive(contact.Key, contact.Value.Probe) && AnomalyProbe.CanActivate(Time.time - contact.Value.EnteredAt, _ignoreSmallObjects, _smallObjectIgnoreDelay))
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasActivePlayer()
        {
            foreach (KeyValuePair<Collider, StalkerPlayerController> contact in _contacts)
            {
                if (IsContactActive(contact.Key, contact.Value) && (_minimumPlayerSpeed <= 0f || contact.Value.Motor != null && contact.Value.Motor.ActualVelocity > _minimumPlayerSpeed))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsProbeActive(Collider collider, AnomalyProbe probe)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy && probe != null && probe.isActiveAndEnabled && IsWithinSourceShapes(collider);
        }

        private void OnDischargeStarted()
        {
            if (_presentingDischarge || _confirmedPulse && _pulsePending)
            {
                return;
            }
            _confirmedPulse = false;
            SchedulePulse();
        }

        private void SchedulePulse()
        {
            if (CanDetect == false)
            {
                return;
            }

            _cycle.RecordDischarge(Time.time);
            _pulseAt = Time.time + (_influenceProfile != null ? _influenceProfile.PulseDelay : 0f);
            _pulsePending = true;
            if (Time.time >= _pulseAt)
            {
                ApplyPulse();
            }
        }

        private void ApplyPulse()
        {
            _pulsePending = false;
            bool confirmed = _confirmedPulse;
            _confirmedPulse = false;
            _affectedPlayers.Clear();
            _affectedProbes.Clear();
            foreach (KeyValuePair<Collider, StalkerPlayerController> contact in _contacts)
            {
                if (IsContactActive(contact.Key, contact.Value) && _affectedPlayers.Add(contact.Value))
                {
                    if (_influenceProfile != null && _influenceProfile.Kind == AnomalyInfluenceKind.Vortex)
                    {
                        SphereCollider volume = GetComponent<SphereCollider>();
                        CharacterController body = contact.Value.GetComponent<CharacterController>();
                        Vector3 center = body != null ? body.bounds.center : contact.Value.transform.position;
                        Vector3 scale = transform.lossyScale;
                        float radius = volume.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) * _influenceProfile.ActorBlowoutRadius;
                        if ((center - transform.position).sqrMagnitude > radius * radius)
                        {
                            continue;
                        }
                    }
                    _visual.PlayHit(contact.Value.transform);
                    if (confirmed)
                    {
                        HitConfirmed?.Invoke(contact.Value, _dischargeSequence);
                    }
                    if (contact.Value.TryGetComponent(out StalkerAnomalyResponse response))
                    {
                        response.ApplyPulse(_influenceProfile);
                    }
                }
            }
            foreach (KeyValuePair<Collider, ProbeContact> contact in _probeContacts)
            {
                AnomalyProbe probe = contact.Value.Probe;
                if (IsProbeActive(contact.Key, probe) && AnomalyProbe.CanActivate(Time.time - contact.Value.EnteredAt, _ignoreSmallObjects || _ignoreNonlivingObjects || _actorOnly, _smallObjectIgnoreDelay) && _affectedProbes.Add(probe))
                {
                    _visual.PlayHit(probe.transform, true);
                }
            }
        }

        private bool IsContactActive(Collider collider, StalkerPlayerController player)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy && player != null && player.isActiveAndEnabled && IsWithinSourceShapes(collider);
        }

        private bool IsWithinSourceShapes(Collider collider)
        {
            if (_activationVolume != null)
            {
                return _activationVolume.Overlaps(collider);
            }
            if (_localTriggerShapes == null)
            {
                return true;
            }
            Bounds bounds = collider.bounds;
            bool hasTrigger = false;
            foreach (Collider shape in _localTriggerShapes)
            {
                if (shape == null || !shape.isTrigger)
                {
                    continue;
                }
                hasTrigger = true;
                if (shape.enabled && shape.bounds.Intersects(bounds))
                {
                    return true;
                }
            }
            return !hasTrigger;
        }

        private sealed class ProbeContact
        {
            public AnomalyProbe Probe { get; }
            public float EnteredAt { get; }
            public bool IdleStopped { get; set; }

            public ProbeContact(AnomalyProbe probe, float enteredAt)
            {
                Probe = probe;
                EnteredAt = enteredAt;
            }
        }
    }
}
