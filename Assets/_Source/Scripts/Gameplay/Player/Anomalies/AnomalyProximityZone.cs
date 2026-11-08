using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
    public sealed class AnomalyProximityZone : MonoBehaviour
    {
        [SerializeField] private AnomalyPlayerReaction _source;
        [SerializeField] private SphereCollider _sourceVolume;

        private readonly Dictionary<Collider, StalkerAnomalyResponse> _contacts = new Dictionary<Collider, StalkerAnomalyResponse>();
        private readonly List<Collider> _expiredContacts = new List<Collider>();
        private readonly HashSet<StalkerAnomalyResponse> _players = new HashSet<StalkerAnomalyResponse>();

        private SphereCollider _volume;

        public AnomalyInfluenceProfile Profile => _source != null ? _source.InfluenceProfile : null;
        public bool IsAvailable => isActiveAndEnabled && _source != null && _source.IsInfluencing && _sourceVolume != null && _sourceVolume.enabled;
        public Vector3 Position => _source != null ? _source.transform.position : transform.position;
        public Vector3 Center => _sourceVolume != null ? _sourceVolume.transform.TransformPoint(_sourceVolume.center) : Position;
        public float ZoneRadius => _sourceVolume != null ? _sourceVolume.radius * MaximumScale(_sourceVolume.transform.lossyScale) : 0f;
        public float ProximityRadius => (ZoneRadius + 1f) * 2f;

        private void Awake()
        {
            _volume = GetComponent<SphereCollider>();
            UpdateVolume();
        }

        private void Update()
        {
            UpdateVolume();
            _expiredContacts.Clear();
            _players.Clear();
            foreach (KeyValuePair<Collider, StalkerAnomalyResponse> contact in _contacts)
            {
                if (IsAvailable == false || contact.Key == null || contact.Key.enabled == false || contact.Key.gameObject.activeInHierarchy == false || contact.Value == null || contact.Value.isActiveAndEnabled == false)
                {
                    _expiredContacts.Add(contact.Key);
                }
                else if (_players.Add(contact.Value))
                {
                    contact.Value.RefreshZone(this);
                }
            }
            foreach (Collider contact in _expiredContacts)
            {
                _contacts.Remove(contact);
            }
        }

        private void OnDisable()
        {
            _players.Clear();
            foreach (StalkerAnomalyResponse player in _contacts.Values)
            {
                if (player != null && _players.Add(player))
                {
                    player.RemoveZone(this);
                }
            }
            _contacts.Clear();
            _players.Clear();
            _expiredContacts.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            Register(other);
        }

        private void OnTriggerStay(Collider other)
        {
            Register(other);
        }

        private void OnTriggerExit(Collider other)
        {
            _contacts.Remove(other);
        }

        private void Register(Collider other)
        {
            if (IsAvailable == false || other == null || _contacts.ContainsKey(other))
            {
                return;
            }
            StalkerPlayerController player = other.GetComponentInParent<StalkerPlayerController>();
            if (player != null && player.TryGetComponent(out StalkerAnomalyResponse response))
            {
                _contacts.Add(other, response);
                response.RefreshZone(this);
            }
        }

        private void UpdateVolume()
        {
            if (_volume != null)
            {
                _volume.center = Vector3.zero;
                _volume.radius = ProximityRadius / Mathf.Max(0.0001f, MaximumScale(transform.lossyScale));
            }
        }

        private static float MaximumScale(Vector3 scale)
        {
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }
    }
}
