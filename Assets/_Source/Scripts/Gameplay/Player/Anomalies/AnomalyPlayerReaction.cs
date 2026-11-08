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

        private readonly Dictionary<Collider, StalkerPlayerController> _contacts = new Dictionary<Collider, StalkerPlayerController>();
        private readonly List<Collider> _expiredContacts = new List<Collider>();
        private readonly HashSet<StalkerPlayerController> _affectedPlayers = new HashSet<StalkerPlayerController>();

        private AnomalyCycle _cycle;

        public AnomalyPhase Phase => _cycle != null ? _cycle.Phase : AnomalyPhase.Idle;
        public float CooldownRemaining => _cycle != null ? _cycle.GetCooldownRemaining(Time.time) : 0f;
        public bool IsReady => CanDetect && Phase == AnomalyPhase.Idle;
        public AnomalyInfluenceProfile InfluenceProfile => _influenceProfile;
        public bool IsInfluencing => CanDetect;

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
            _contacts.Clear();
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
            if (_cycle.Tick(Time.time, _contacts.Count > 0))
            {
                _visual.PlayDischarge();
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
            _contacts.Remove(other);
        }

        private void RegisterContact(Collider other)
        {
            if (CanDetect == false || other == null || other.enabled == false || other.gameObject.activeInHierarchy == false || _contacts.ContainsKey(other))
            {
                return;
            }

            StalkerPlayerController player = other.GetComponentInParent<StalkerPlayerController>();
            if (player == null || player.isActiveAndEnabled == false)
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
                _contacts.Remove(collider);
            }
        }

        private void OnDischargeStarted()
        {
            if (CanDetect == false)
            {
                return;
            }

            _cycle.RecordDischarge(Time.time);
            _affectedPlayers.Clear();
            foreach (KeyValuePair<Collider, StalkerPlayerController> contact in _contacts)
            {
                if (IsContactActive(contact.Key, contact.Value) && _affectedPlayers.Add(contact.Value))
                {
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
    }
}
