using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Player
{
    public enum AnomalyInfluenceKind
    {
        Springboard,
        Electric,
        Thermal,
        Vortex
    }

    public sealed class AnomalyInfluenceProfile : ScriptableObject
    {
        [SerializeField] private AnomalyInfluenceKind _kind;
        [SerializeField] private AnomalyPostProcessAsset _inherentPostProcess;
        [SerializeField] private float _radiusMin;
        [SerializeField] private float _radiusMax;
        [SerializeField] private AnomalyPostProcessAsset _electricMine;
        [SerializeField] private AnomalyPostProcessAsset _electric;
        [SerializeField] private XRayAnmAsset _earthquake;
        [SerializeField] private bool _actorPainEnabled = true;
        [SerializeField, Min(0f)] private float _launchHeight;
        [SerializeField, Min(0f)] private float _launchAirAcceleration;
        [SerializeField, Min(0f)] private float _pulseDelay;
        [SerializeField, Min(0f)] private float _pullStrength;
        [SerializeField, Range(0f, 1f)] private float _actorBlowoutRadius = 1f;
        [SerializeField] private AnomalyPostProcessAsset _impactPostProcess;
        [SerializeField] private XRayAnmAsset[] _impactAnimations;

        public AnomalyInfluenceKind Kind => _kind;
        public AnomalyPostProcessAsset InherentPostProcess => _inherentPostProcess;
        public float RadiusMin => _radiusMin;
        public float RadiusMax => _radiusMax;
        public AnomalyPostProcessAsset ElectricMine => _electricMine;
        public AnomalyPostProcessAsset Electric => _electric;
        public XRayAnmAsset Earthquake => _earthquake;
        public bool ActorPainEnabled => _actorPainEnabled;
        public float LaunchHeight => _launchHeight;
        public float LaunchAirAcceleration => _launchAirAcceleration;
        public float PulseDelay => _pulseDelay;
        public float PullStrength => _pullStrength;
        public float ActorBlowoutRadius => _actorBlowoutRadius;
        public AnomalyPostProcessAsset ImpactPostProcess => _impactPostProcess;
        public XRayAnmAsset[] ImpactAnimations => _impactAnimations;
    }
}
