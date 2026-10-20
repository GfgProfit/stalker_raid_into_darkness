using System.Collections.Generic;
using RaidIntoDarkness.Player;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WorldItemImpactSound : MonoBehaviour
    {
        public const string DEFAULT_MATERIAL = "objects\\clothes";

        [SerializeField] private StalkerCollideSoundLibrary _sounds;
        [SerializeField] private StalkerSurfaceAudioLibrary _surfaces;
        [SerializeField] private AudioSource _audio;
        [Tooltip("Game material of the world model bones (OGF S_IKDATA).")]
        [SerializeField] private string _material = DEFAULT_MATERIAL;
        [Tooltip("Physics shell mass: sum of the world model bone masses (OGF S_IKDATA); 0 uses the Rigidbody mass.")]
        [SerializeField] [Min(0f)] private float _mass;

        private StalkerSurfaceResolver _resolver;
        private Rigidbody _body;

        public string Material => _material;
        public float Mass => _mass > 0f ? _mass : Body.mass;
        public int PlayedCount { get; private set; }
        public AudioClip LastClip { get; private set; }
        public float LastVolume { get; private set; }
        public StalkerSurfaceType LastSurface { get; private set; }

        private Rigidbody Body
        {
            get
            {
                if (_body == null)
                {
                    _body = GetComponent<Rigidbody>();
                }

                return _body;
            }
        }

        public void Configure(StalkerCollideSoundLibrary sounds, StalkerSurfaceAudioLibrary surfaces, AudioSource audio, string material, float mass)
        {
            _sounds = sounds;
            _surfaces = surfaces;
            _audio = audio;
            _material = string.IsNullOrEmpty(material) ? DEFAULT_MATERIAL : material;
            _mass = Mathf.Max(0f, mass);
            _resolver = null;
        }

        public bool TryPlay(Collider other, Vector3 point, Vector3 normal, Vector3 velocity)
        {
            if (_sounds == null || _audio == null || other == null || (other.attachedRigidbody != null && other.attachedRigidbody.isKinematic == false))
            {
                return false;
            }

            float criterion = StalkerCollideSoundLibrary.GetCriterion(velocity, normal, Mass);
            if (criterion <= _sounds.VelocityCriterion)
            {
                return false;
            }

            Camera camera = Camera.main;
            if (camera != null && (camera.transform.position - point).sqrMagnitude > _sounds.MaxDistance * _sounds.MaxDistance)
            {
                return false;
            }

            if (_resolver == null)
            {
                _resolver = new StalkerSurfaceResolver(_surfaces);
            }

            StalkerSurfaceType surface = _resolver.Resolve(other, point, out _);
            IReadOnlyList<AudioClip> clips = _sounds.Find(_material, surface);
            if (clips == null || clips.Count == 0)
            {
                return false;
            }

            LastClip = clips[Random.Range(0, clips.Count)];
            LastVolume = Mathf.Clamp01(_sounds.GetVolume(criterion));
            LastSurface = surface;
            PlayedCount++;
            _audio.PlayOneShot(LastClip, LastVolume);
            return true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0)
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            TryPlay(collision.collider, contact.point, contact.normal, collision.relativeVelocity);
        }
    }
}
