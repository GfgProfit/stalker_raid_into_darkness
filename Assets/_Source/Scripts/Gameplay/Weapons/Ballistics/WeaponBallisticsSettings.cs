using System;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [Serializable]
    public sealed class WeaponBallisticsSettings
    {
        [SerializeField] [HideInInspector] private GameObject _bulletPrefab;
        [SerializeField] [HideInInspector] private LayerMask _ballisticHitMask = Physics.DefaultRaycastLayers;
        [SerializeField] [HideInInspector] private bool _drawBallisticDebug;

        public GameObject BulletPrefab => _bulletPrefab;
        public LayerMask BallisticHitMask => _ballisticHitMask;
        public float BallisticSimulationStepSeconds => 1f / 240f;
        public int BallisticMaxSimulationStepsPerFrame => 256;
        public float RicochetSurfaceOffsetMeters => 0.002f;
        public bool DrawBallisticDebug => _drawBallisticDebug;
        public float BallisticDebugDurationSeconds => 2f;
    }
}
