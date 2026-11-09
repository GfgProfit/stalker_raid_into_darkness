using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class SpatialBubbleMeshReaction : MonoBehaviour
    {
        private const float VIEWER_RETRY_INTERVAL = 0.5f;
        private const float IMPACT_DAMPING = 3.2f;
        private const float PROXIMITY_FLOW = 0.5f;
        private const float CHARGE_FLOW = 1.2f;
        private const float IMPACT_FLOW = 0.25f;

        private static readonly int PROXIMITY = Shader.PropertyToID("_BubbleProximity");
        private static readonly int CHARGE = Shader.PropertyToID("_BubbleCharge");
        private static readonly int IMPACT = Shader.PropertyToID("_BubbleImpact");
        private static readonly int FLOW_OFFSET = Shader.PropertyToID("_BubbleFlowOffset");

        [SerializeField] private SpatialBubbleTeleport _teleport;
        [SerializeField] private MeshRenderer _surface;
        [SerializeField] private Camera _viewerCamera;
        [SerializeField, Min(0.01f)] private float _proximityDistance = 3f;
        [SerializeField, Min(0.01f)] private float _responseSpeed = 5f;
        [SerializeField, Min(0.001f)] private float _impactAttackSeconds = 0.035f;
        [SerializeField, Min(0.05f)] private float _impactRecoverySeconds = 1.15f;

        private readonly Dictionary<Collider, Rigidbody> _contacts = new Dictionary<Collider, Rigidbody>();
        private readonly Dictionary<Rigidbody, int> _bodyContacts = new Dictionary<Rigidbody, int>();
        private readonly List<Collider> _expiredContacts = new List<Collider>();

        private SphereCollider _volume;
        private Rigidbody _ownBody;
        private Camera _resolvedViewer;
        private MaterialPropertyBlock _propertyBlock;
        private MeshRenderer _boundSurface;
        private float _nextViewerResolve;
        private float _proximity;
        private float _charge;
        private float _impact;
        private float _impactElapsed;
        private float _impactStart;
        private float _flowOffset;
        private bool _impactActive;

        private void Awake()
        {
            if (_teleport == null)
            {
                _teleport = GetComponent<SpatialBubbleTeleport>();
            }
            _volume = _teleport != null ? _teleport.ActivationVolume as SphereCollider : null;
            if (_volume == null)
            {
                _volume = GetComponent<SphereCollider>();
            }
            _ownBody = GetComponent<Rigidbody>();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            ResolveViewer();
            Tick(Time.deltaTime);
        }

        private void OnDisable()
        {
            ClearSurface();
            _contacts.Clear();
            _bodyContacts.Clear();
            _expiredContacts.Clear();
            _resolvedViewer = null;
            _nextViewerResolve = 0f;
            _proximity = 0f;
            _charge = 0f;
            _impact = 0f;
            _impactElapsed = 0f;
            _impactStart = 0f;
            _flowOffset = 0f;
            _impactActive = false;
        }

        private void OnDestroy()
        {
            OnDisable();
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
            RemoveContact(other);
        }

        private void ResolveViewer()
        {
            if (_viewerCamera != null)
            {
                _resolvedViewer = _viewerCamera;
                return;
            }
            if (_resolvedViewer != null && _resolvedViewer.isActiveAndEnabled)
            {
                return;
            }
            if (Time.unscaledTime >= _nextViewerResolve)
            {
                _resolvedViewer = Camera.main;
                _nextViewerResolve = Time.unscaledTime + VIEWER_RETRY_INTERVAL;
            }
        }

        private void Tick(float deltaTime)
        {
            RemoveExpiredContacts();
            float delta = Mathf.Max(0f, deltaTime);
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.01f, _responseSpeed) * delta);
            float targetProximity = _resolvedViewer != null ? EvaluateProximity(_resolvedViewer.transform.position) : 0f;
            float targetCharge = _teleport != null && _teleport.isActiveAndEnabled ? _teleport.ActivationProgress : 0f;
            _proximity = Mathf.Lerp(_proximity, targetProximity, blend);
            _charge = Mathf.Lerp(_charge, targetCharge, blend);
            if (_impactActive)
            {
                _impactElapsed += delta;
                _impact = EvaluateImpact(_impactElapsed);
                _impactActive = _impactElapsed < Mathf.Max(0.001f, _impactAttackSeconds) + Mathf.Max(0.05f, _impactRecoverySeconds);
            }
            _flowOffset += delta * (_proximity * PROXIMITY_FLOW + _charge * CHARGE_FLOW + Mathf.Abs(_impact) * IMPACT_FLOW);
            BindSurface();
        }

        private float EvaluateProximity(Vector3 position)
        {
            if (_volume == null || !_volume.enabled || !_volume.gameObject.activeInHierarchy)
            {
                return 0f;
            }
            Vector3 scale = _volume.transform.lossyScale;
            float radius = _volume.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 center = _volume.transform.TransformPoint(_volume.center);
            float gap = Mathf.Max(0f, Vector3.Distance(position, center) - radius);
            return 1f - Mathf.Clamp01(gap / Mathf.Max(0.01f, _proximityDistance));
        }

        private float EvaluateImpact(float elapsed)
        {
            float attack = Mathf.Max(0.001f, _impactAttackSeconds);
            if (elapsed < attack)
            {
                return Mathf.Lerp(_impactStart, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / attack)));
            }
            float phase = Mathf.Clamp01((elapsed - attack) / Mathf.Max(0.05f, _impactRecoverySeconds));
            float tail = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, phase));
            return Mathf.Cos(phase * Mathf.PI * 2f) * Mathf.Exp(-IMPACT_DAMPING * phase) * tail;
        }

        private void RegisterContact(Collider other)
        {
            if (!isActiveAndEnabled || other == null || !other.enabled || !other.gameObject.activeInHierarchy || other.transform.IsChildOf(transform) || _contacts.ContainsKey(other))
            {
                return;
            }
            Rigidbody body = other.attachedRigidbody;
            if (body == null || body == _ownBody || body.transform.IsChildOf(transform) || other.GetComponentInParent<StalkerPlayerController>() != null)
            {
                return;
            }
            if (body.isKinematic)
            {
                AnomalyProbe probe = other.GetComponentInParent<AnomalyProbe>();
                if (probe == null || !probe.isActiveAndEnabled)
                {
                    return;
                }
            }
            _contacts.Add(other, body);
            _bodyContacts.TryGetValue(body, out int count);
            _bodyContacts[body] = count + 1;
            if (count == 0)
            {
                _impactElapsed = 0f;
                _impactStart = _impact;
                _impactActive = true;
            }
        }

        private void RemoveContact(Collider collider)
        {
            if (ReferenceEquals(collider, null) || !_contacts.TryGetValue(collider, out Rigidbody body))
            {
                return;
            }
            _contacts.Remove(collider);
            if (_bodyContacts.TryGetValue(body, out int count))
            {
                if (count > 1)
                {
                    _bodyContacts[body] = count - 1;
                }
                else
                {
                    _bodyContacts.Remove(body);
                }
            }
        }

        private void RemoveExpiredContacts()
        {
            _expiredContacts.Clear();
            foreach (KeyValuePair<Collider, Rigidbody> contact in _contacts)
            {
                if (contact.Key == null || !contact.Key.enabled || !contact.Key.gameObject.activeInHierarchy || contact.Value == null || !contact.Value.gameObject.activeInHierarchy)
                {
                    _expiredContacts.Add(contact.Key);
                }
            }
            foreach (Collider collider in _expiredContacts)
            {
                RemoveContact(collider);
            }
        }

        private void BindSurface()
        {
            if (_boundSurface != _surface)
            {
                ClearSurface();
                _boundSurface = _surface;
            }
            if (_boundSurface == null)
            {
                return;
            }
            _propertyBlock ??= new MaterialPropertyBlock();
            _boundSurface.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(PROXIMITY, _proximity);
            _propertyBlock.SetFloat(CHARGE, _charge);
            _propertyBlock.SetFloat(IMPACT, _impact);
            _propertyBlock.SetFloat(FLOW_OFFSET, _flowOffset);
            _boundSurface.SetPropertyBlock(_propertyBlock);
        }

        private void ClearSurface()
        {
            if (_boundSurface != null)
            {
                _propertyBlock ??= new MaterialPropertyBlock();
                _boundSurface.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetFloat(PROXIMITY, 0f);
                _propertyBlock.SetFloat(CHARGE, 0f);
                _propertyBlock.SetFloat(IMPACT, 0f);
                _propertyBlock.SetFloat(FLOW_OFFSET, 0f);
                _boundSurface.SetPropertyBlock(_propertyBlock);
            }
            _boundSurface = null;
        }
    }
}
