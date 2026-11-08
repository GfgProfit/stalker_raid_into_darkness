using System.Collections.Generic;
using RaidIntoDarkness.Effects;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
    public sealed class AnomalyPlayerReaction : MonoBehaviour
    {
        [SerializeField] private AnomalyVisual _visual;
        [SerializeField] private AnomalyInfluenceProfile _influenceProfile;
        [SerializeField, Min(0f)] private float _awakingTime;
        [SerializeField, Min(0f)] private float _blowoutTime;
        [SerializeField, Min(0f)] private float _accumulateTime;
        [SerializeField] private bool _ignoreSmallObjects;
        [SerializeField] private float _smallObjectIgnoreDelay = 0.5f;

        private readonly Dictionary<Collider, StalkerPlayerController> _contacts = new Dictionary<Collider, StalkerPlayerController>();
        private readonly List<Collider> _expiredContacts = new List<Collider>();
        private readonly HashSet<StalkerPlayerController> _affectedPlayers = new HashSet<StalkerPlayerController>();
        private readonly Dictionary<Collider, ProbeContact> _probeContacts = new Dictionary<Collider, ProbeContact>();

        private AnomalyCycle _cycle;
        private float _pulseAt;
        private bool _pulsePending;

        public AnomalyPhase Phase => _cycle != null ? _cycle.Phase : AnomalyPhase.Idle;
        public float CooldownRemaining => _cycle != null ? _cycle.GetCooldownRemaining(Time.time) : 0f;
        public bool IsReady => CanDetect && Phase == AnomalyPhase.Idle;
        public AnomalyInfluenceProfile InfluenceProfile => _influenceProfile;
        public bool IsInfluencing => CanDetect;

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

        private bool CanDetect => isActiveAndEnabled && _visual != null && _visual.isActiveAndEnabled && _visual.IsVisible;

        private void Awake()
        {
            if (_visual == null)
            {
                _visual = GetComponent<AnomalyVisual>();
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
                    _visual.StopContact(contact.Probe.transform);
                }
            }
            _probeContacts.Clear();
            _expiredContacts.Clear();
            _affectedPlayers.Clear();
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
            if (_cycle.Tick(Time.time, _contacts.Count > 0 || HasActiveProbe()))
            {
                _visual.PlayDischarge();
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
            if (_probeContacts.TryGetValue(other, out ProbeContact probeContact))
            {
                _probeContacts.Remove(other);
                if (probeContact.Probe != null)
                {
                    _visual.StopContact(probeContact.Probe.transform);
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
                else if (contact.Value.IdleStopped == false && Time.time - contact.Value.EnteredAt > 0.1f)
                {
                    _visual.StopContact(contact.Value.Probe.transform);
                    contact.Value.IdleStopped = true;
                }
            }
            foreach (Collider collider in _expiredContacts)
            {
                ProbeContact contact = _probeContacts[collider];
                if (contact.Probe != null)
                {
                    _visual.StopContact(contact.Probe.transform);
                }
                _probeContacts.Remove(collider);
            }
        }

        private void RegisterProbe(Collider collider)
        {
            if (_probeContacts.ContainsKey(collider))
            {
                return;
            }
            AnomalyProbe probe = collider.GetComponentInParent<AnomalyProbe>();
            if (probe == null || probe.isActiveAndEnabled == false)
            {
                return;
            }
            _probeContacts.Add(collider, new ProbeContact(probe, Time.time));
            _visual.PlayEntrance(probe.transform);
        }

        private bool HasActiveProbe()
        {
            foreach (ProbeContact contact in _probeContacts.Values)
            {
                if (AnomalyProbe.CanActivate(Time.time - contact.EnteredAt, _ignoreSmallObjects, _smallObjectIgnoreDelay))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsProbeActive(Collider collider, AnomalyProbe probe)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy && probe != null && probe.isActiveAndEnabled;
        }

        private void OnDischargeStarted()
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
            _affectedPlayers.Clear();
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
                    if (contact.Value.TryGetComponent(out StalkerAnomalyResponse response))
                    {
                        response.ApplyPulse(_influenceProfile);
                    }
                }
            }
        }

        private static bool IsContactActive(Collider collider, StalkerPlayerController player)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy && player != null && player.isActiveAndEnabled;
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
