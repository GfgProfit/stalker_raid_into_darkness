using System;
using RaidIntoDarkness.Player;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [Serializable]
    public sealed class GrenadeAmmoData
    {
        [SerializeField] private GameObject _explosionPrefab;
        [SerializeField] private AudioClip _explosionSound;
        [SerializeField] private AudioClip _launchSound;
        [SerializeField] private AudioClip[] _explosionVariants = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] _explosionBodyVariants = Array.Empty<AudioClip>();
        [SerializeField] private StalkerCollideSoundLibrary _bounceSounds;
        [SerializeField] private StalkerSurfaceAudioLibrary _bounceSurfaces;
        [SerializeField, Min(0f)] private float _blastRadius = 5f;
        [SerializeField, Min(0f)] private float _blastDamage = 90f;
        [SerializeField, Min(0)] private int _fragmentCount = 80;
        [SerializeField, Min(0f)] private float _fragmentDamage = 14f;
        [SerializeField, Min(0.01f)] private float _selfDestructSeconds = 6f;
        [SerializeField, Min(0f)] private float _armingDistance = 2.5f;
        [SerializeField, Min(0f)] private float _impactBounceHeight;
        [SerializeField, Min(0.01f)] private float _fragmentRange = 100f;
        [SerializeField, Min(0f)] private float _armingSeconds;
        [SerializeField] private bool _explodeOnImpact;

        public GameObject ExplosionPrefab => _explosionPrefab;
        public AudioClip ExplosionSound => _explosionSound;
        public AudioClip LaunchSound => _launchSound;
        public StalkerCollideSoundLibrary BounceSounds => _bounceSounds;
        public StalkerSurfaceAudioLibrary BounceSurfaces => _bounceSurfaces;
        public float BlastRadius => Mathf.Max(0f, _blastRadius);
        public float BlastDamage => Mathf.Max(0f, _blastDamage);
        public int FragmentCount => Mathf.Max(0, _fragmentCount);
        public float FragmentDamage => Mathf.Max(0f, _fragmentDamage);
        public float SelfDestructSeconds => Mathf.Max(0.01f, _selfDestructSeconds);
        public float ArmingDistance => Mathf.Max(0f, _armingDistance);
        public float ImpactBounceHeight => Mathf.Max(0f, _impactBounceHeight);
        public float FragmentRange => _fragmentRange > 0f ? _fragmentRange : 100f;
        public float ArmingSeconds => Mathf.Max(0f, _armingSeconds);
        public bool ExplodeOnImpact => _explodeOnImpact;
        public bool IsConfigured => _explosionPrefab != null && _explosionSound != null;

        public AudioClip SelectExplosionSound()
        {
            return _explosionVariants != null && _explosionVariants.Length > 0 ? _explosionVariants[UnityEngine.Random.Range(0, _explosionVariants.Length)] ?? _explosionSound : _explosionSound;
        }

        public AudioClip SelectExplosionBodySound()
        {
            return _explosionBodyVariants != null && _explosionBodyVariants.Length > 0 ? _explosionBodyVariants[UnityEngine.Random.Range(0, _explosionBodyVariants.Length)] : null;
        }
    }
}
