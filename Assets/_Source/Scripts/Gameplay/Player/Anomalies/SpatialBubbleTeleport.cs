using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class SpatialBubbleTeleport : MonoBehaviour
    {
        private const int TELEPORT_EFFECT_ID = 2006;
        private const float TELEPORT_DELAY = 0.9f;
        private const float SOURCE_INSIDE_EPSILON = 0.001f;
        private const float DETECTION_MARGIN = 0.5f;

        [SerializeField] private Transform _destination;
        [SerializeField] private SphereCollider _activationVolume;
        [SerializeField] private AnomalyPostProcessAsset _teleportPostProcess;
        [SerializeField] private AudioSource _teleportAudio;

        private readonly Dictionary<Collider, StalkerPlayerController> _contacts = new Dictionary<Collider, StalkerPlayerController>();
        private readonly Dictionary<StalkerPlayerController, CharacterController> _players = new Dictionary<StalkerPlayerController, CharacterController>();
        private readonly List<Collider> _expiredContacts = new List<Collider>();
        private readonly List<StalkerPlayerController> _expiredPlayers = new List<StalkerPlayerController>();

        private Collider[] _overlapBuffer = new Collider[16];
        private StalkerPlayerController _activatedPlayer;
        private float _elapsed;
        private bool _activated;

        public Transform Destination
        {
            get => _destination;
            set => _destination = value;
        }
        public Collider ActivationVolume => _activationVolume;
        public bool IsActivated => _activated;
        public float DelayRemaining => _activated ? Mathf.Max(0f, TELEPORT_DELAY - _elapsed) : 0f;
        public float ActivationProgress => _activated ? Mathf.Clamp01(_elapsed / TELEPORT_DELAY) : 0f;

        private void Awake()
        {
            if (_activationVolume == null)
            {
                _activationVolume = GetComponent<SphereCollider>();
            }
        }

        private void OnDisable()
        {
            CancelActivation();
            _contacts.Clear();
            _players.Clear();
            _expiredContacts.Clear();
            _expiredPlayers.Clear();
            Array.Clear(_overlapBuffer, 0, _overlapBuffer.Length);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
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
            if (isActiveAndEnabled == false || other == null || other.transform.IsChildOf(transform))
            {
                return;
            }
            if (_contacts.TryGetValue(other, out StalkerPlayerController player) == false)
            {
                player = other.GetComponentInParent<StalkerPlayerController>();
                _contacts.Add(other, player);
            }
            if (player != null && player.isActiveAndEnabled && _players.ContainsKey(player) == false && player.TryGetComponent(out CharacterController body))
            {
                _players.Add(player, body);
            }
        }

        private void Tick(float deltaTime)
        {
            if (isActiveAndEnabled == false)
            {
                return;
            }
            RemoveExpiredContacts();
            if (_activated)
            {
                if (_activatedPlayer == null || _activatedPlayer.isActiveAndEnabled == false || _destination == null)
                {
                    CancelActivation();
                    return;
                }
                _elapsed += Mathf.Max(0f, deltaTime);
                if (_elapsed >= TELEPORT_DELAY)
                {
                    CompleteTransfer();
                }
                return;
            }
            if (_destination == null || _activationVolume == null || _activationVolume.enabled == false || _activationVolume.gameObject.activeInHierarchy == false)
            {
                return;
            }
            RecoverPlayers();
            foreach (KeyValuePair<StalkerPlayerController, CharacterController> candidate in _players)
            {
                if (OverlapsPlayer(candidate.Value))
                {
                    StalkerPlayerController player = candidate.Key;
                    _activatedPlayer = player;
                    _elapsed = 0f;
                    _activated = true;
                    StalkerAnomalyScreenEffects screen = player.GetComponent<StalkerAnomalyScreenEffects>();
                    if (screen != null && _teleportPostProcess != null)
                    {
                        screen.PlayOnce(TELEPORT_EFFECT_ID, _teleportPostProcess, 1f);
                    }
                    return;
                }
            }
        }

        private bool OverlapsPlayer(CharacterController body)
        {
            if (_activationVolume == null || _activationVolume.enabled == false || _activationVolume.gameObject.activeInHierarchy == false || body == null || body.enabled == false || body.gameObject.activeInHierarchy == false)
            {
                return false;
            }
            Vector3 sphereCenter = _activationVolume.transform.TransformPoint(_activationVolume.center);
            Vector3 scale = body.transform.lossyScale;
            float bodyRadius = RoundToSourceFloat(body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
            float halfSegment = Mathf.Max(0f, body.height * Mathf.Abs(scale.y) * 0.5f - bodyRadius);
            Vector3 bodyCenter = body.transform.TransformPoint(body.center);
            Vector3 axis = body.transform.up;
            Vector3 closest = bodyCenter + axis * Mathf.Clamp(Vector3.Dot(sphereCenter - bodyCenter, axis), -halfSegment, halfSegment);
            float radius = RoundToSourceFloat(WorldRadius() + bodyRadius);
            radius = RoundToSourceFloat(radius + SOURCE_INSIDE_EPSILON);
            return RoundToSourceFloat((closest - sphereCenter).sqrMagnitude) < RoundToSourceFloat(radius * radius);
        }

        private float WorldRadius()
        {
            Vector3 scale = _activationVolume.transform.lossyScale;
            return RoundToSourceFloat(_activationVolume.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
        }

        private void RecoverPlayers()
        {
            if (_players.Count > 0)
            {
                return;
            }
            PhysicsScene physicsScene = gameObject.scene.GetPhysicsScene();
            if (physicsScene.IsValid() == false)
            {
                return;
            }
            Vector3 center = _activationVolume.transform.TransformPoint(_activationVolume.center);
            int count;
            while (true)
            {
                count = physicsScene.OverlapSphere(center, WorldRadius() + DETECTION_MARGIN, _overlapBuffer, Physics.AllLayers, QueryTriggerInteraction.Collide);
                if (count < _overlapBuffer.Length)
                {
                    break;
                }
                Array.Resize(ref _overlapBuffer, _overlapBuffer.Length * 2);
            }
            for (int index = 0; index < count; index++)
            {
                RegisterContact(_overlapBuffer[index]);
            }
            Array.Clear(_overlapBuffer, 0, _overlapBuffer.Length);
        }

        private static float RoundToSourceFloat(float value)
        {
            return BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
        }

        private void RemoveExpiredContacts()
        {
            _expiredContacts.Clear();
            foreach (KeyValuePair<Collider, StalkerPlayerController> contact in _contacts)
            {
                if (contact.Key == null || contact.Key.enabled == false || contact.Key.gameObject.activeInHierarchy == false || (ReferenceEquals(contact.Value, null) == false && contact.Value == null))
                {
                    _expiredContacts.Add(contact.Key);
                }
            }
            foreach (Collider collider in _expiredContacts)
            {
                _contacts.Remove(collider);
            }
            _expiredPlayers.Clear();
            foreach (KeyValuePair<StalkerPlayerController, CharacterController> player in _players)
            {
                if (player.Key == null || player.Key.isActiveAndEnabled == false || player.Value == null || player.Value.gameObject.activeInHierarchy == false)
                {
                    _expiredPlayers.Add(player.Key);
                }
            }
            foreach (StalkerPlayerController player in _expiredPlayers)
            {
                _players.Remove(player);
            }
        }

        private void CompleteTransfer()
        {
            StalkerPlayerController player = _activatedPlayer;
            CancelActivation();
            if (player.Motor == null || player.Motor.Physics == null)
            {
                return;
            }
            float yaw = _destination.eulerAngles.y;
            player.Motor.Teleport(_destination.position);
            StalkerCameraController camera = player.CameraController;
            if (camera != null)
            {
                camera.SetLadder(null);
                camera.SetLook(yaw, 0f);
                camera.Apply(player.Motor.Stance != null ? player.Motor.Stance.CameraHeight : 0f);
                player.Motor.SetView(yaw, camera.Direction);
            }
            else
            {
                player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                player.Motor.SetView(yaw, player.transform.forward);
            }
            if (_teleportAudio != null)
            {
                _teleportAudio.PlayOneShot(_teleportAudio.clip);
            }
        }

        private void CancelActivation()
        {
            _activatedPlayer = null;
            _elapsed = 0f;
            _activated = false;
        }
    }
}
