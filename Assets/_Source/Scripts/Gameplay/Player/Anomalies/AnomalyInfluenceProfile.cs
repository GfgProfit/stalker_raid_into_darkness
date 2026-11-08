using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Player
{
    public enum AnomalyInfluenceKind
    {
        Springboard,
        Electric,
        Thermal
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

        public AnomalyInfluenceKind Kind => _kind;
        public AnomalyPostProcessAsset InherentPostProcess => _inherentPostProcess;
        public float RadiusMin => _radiusMin;
        public float RadiusMax => _radiusMax;
        public AnomalyPostProcessAsset ElectricMine => _electricMine;
        public AnomalyPostProcessAsset Electric => _electric;
        public XRayAnmAsset Earthquake => _earthquake;
        public bool ActorPainEnabled => _actorPainEnabled;
    }
}
