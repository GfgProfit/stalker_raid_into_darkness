using System;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [Serializable]
    public sealed class BallisticAmmoData
    {
        [SerializeField] [HideInInspector] private bool _allowRicochet;
        [SerializeField] [HideInInspector] private float _distanceMultiplier = 1f;
        [SerializeField] [HideInInspector] private bool _ammoTracerEnabled;
        [SerializeField] [HideInInspector] private Material _ammoTracerMaterial;
        [SerializeField] [ColorUsage(true, true)] private Color _ammoTracerColor = new Color(1f, 0.15f, 0.02f, 1f);
        [SerializeField] [HideInInspector] [Min(0f)] private float _ammoTracerWidthMeters = 0.006f;
        [SerializeField] [HideInInspector] [Min(0f)] private float _ammoTracerTrailDurationSeconds = 0.02f;
        [SerializeField] [Min(0f)] private float _ammoTracerIgnitionDistanceMeters;
        [SerializeField] [HideInInspector] [Min(0f)] private float _ammoTracerBurnDurationSeconds = 3f;
        [SerializeField] [Min(0f)] private float _ammoTracerEmissionIntensity = 10f;
        [SerializeField] [HideInInspector] private int _projectileCount = 1;

        public bool AllowRicochet => _allowRicochet;
        public float DistanceMultiplier => Mathf.Max(0f, _distanceMultiplier);
        public bool AmmoTracerEnabled => _ammoTracerEnabled;
        public Material AmmoTracerMaterial => _ammoTracerMaterial;
        public Color AmmoTracerColor => _ammoTracerColor;
        public float AmmoTracerWidthMeters => Mathf.Max(0f, _ammoTracerWidthMeters);
        public float AmmoTracerTrailDurationSeconds => Mathf.Max(0f, _ammoTracerTrailDurationSeconds);
        public float AmmoTracerIgnitionDistanceMeters => Mathf.Max(0f, _ammoTracerIgnitionDistanceMeters);
        public float AmmoTracerBurnDurationSeconds => Mathf.Max(0f, _ammoTracerBurnDurationSeconds);
        public float AmmoTracerEmissionIntensity => Mathf.Max(0f, _ammoTracerEmissionIntensity);
        public int ProjectileCount => Mathf.Max(1, _projectileCount);
        public bool IsConfigured => _projectileCount > 0 && _distanceMultiplier > 0f;
    }
}
