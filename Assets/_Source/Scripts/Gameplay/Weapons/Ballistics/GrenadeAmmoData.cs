using System;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [Serializable]
    public sealed class GrenadeAmmoData
    {
        [SerializeField] private GameObject _explosionPrefab;
        [SerializeField] private AudioClip _explosionSound;
        [SerializeField] private AudioClip _launchSound;
        [SerializeField, Min(0f)] private float _blastRadius = 5f;
        [SerializeField, Min(0f)] private float _blastDamage = 90f;
        [SerializeField, Min(0)] private int _fragmentCount = 80;
        [SerializeField, Min(0f)] private float _fragmentDamage = 14f;
        [SerializeField, Min(0.01f)] private float _selfDestructSeconds = 6f;
        [SerializeField, Min(0f)] private float _armingDistance = 2.5f;
        [SerializeField, Min(0f)] private float _impactBounceHeight;

        public GameObject ExplosionPrefab => _explosionPrefab;
        public AudioClip ExplosionSound => _explosionSound;
        public AudioClip LaunchSound => _launchSound;
        public float BlastRadius => Mathf.Max(0f, _blastRadius);
        public float BlastDamage => Mathf.Max(0f, _blastDamage);
        public int FragmentCount => Mathf.Max(0, _fragmentCount);
        public float FragmentDamage => Mathf.Max(0f, _fragmentDamage);
        public float SelfDestructSeconds => Mathf.Max(0.01f, _selfDestructSeconds);
        public float ArmingDistance => Mathf.Max(0f, _armingDistance);
        public float ImpactBounceHeight => Mathf.Max(0f, _impactBounceHeight);
        public bool IsConfigured => _explosionPrefab != null && _explosionSound != null;
    }
}
