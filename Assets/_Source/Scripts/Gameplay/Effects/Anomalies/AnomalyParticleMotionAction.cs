using System;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [Serializable]
    public struct AnomalyParticleMotionAction
    {
        [SerializeField] private XRayParticleActionType _type;
        [SerializeField] private uint _flags;
        [SerializeField] private Vector3 _vector0;
        [SerializeField] private Vector3 _vector1;
        [SerializeField] private float _parameter0;
        [SerializeField] private float _parameter1;
        [SerializeField] private float _parameter2;
        [SerializeField] private int _octaves;
        [SerializeField] private XRayParticleDomain _domain;
        [SerializeField] private bool _dampingBoundsSquared;

        public XRayParticleActionType Type => _type;
        public Vector3 Vector0 => _vector0;
        public Vector3 Vector1 => _vector1;
        public float Parameter0 => _parameter0;
        public float Parameter1 => _parameter1;
        public float Parameter2 => _parameter2;
        public int Octaves => _octaves;
        public XRayParticleDomain Domain => _domain;
        public bool DampingBoundsSquared => _dampingBoundsSquared;

        public AnomalyParticleMotionAction(XRayParticleActionType type, uint flags, Vector3 vector0, Vector3 vector1, float parameter0, float parameter1, float parameter2, int octaves, XRayParticleDomain domain, bool dampingBoundsSquared)
        {
            _type = type;
            _flags = flags;
            _vector0 = vector0;
            _vector1 = vector1;
            _parameter0 = parameter0;
            _parameter1 = parameter1;
            _parameter2 = parameter2;
            _octaves = octaves;
            _domain = domain;
            _dampingBoundsSquared = dampingBoundsSquared;
        }

        public AnomalyParticleMotionAction ToWorld(Matrix4x4 parent)
        {
            AnomalyParticleMotionAction result = this;
            Matrix4x4 matrix = (_flags & XRayParticleAction.ALLOW_ROTATE) != 0 ? parent : Matrix4x4.Translate(parent.GetColumn(3));
            switch (_type)
            {
                case XRayParticleActionType.OrbitLine:
                case XRayParticleActionType.Vortex:
                    result._vector1 = matrix.MultiplyVector(_vector1);
                    result._vector0 = matrix.MultiplyPoint3x4(_vector0);
                    break;
                case XRayParticleActionType.OrbitPoint:
                case XRayParticleActionType.Scatter:
                    result._vector0 = matrix.MultiplyPoint3x4(_vector0);
                    break;
                case XRayParticleActionType.TargetVelocity:
                    result._vector0 = matrix.MultiplyVector(_vector0);
                    break;
                case XRayParticleActionType.RandomAccel:
                case XRayParticleActionType.RandomDisplace:
                case XRayParticleActionType.RandomVelocity:
                    result._domain = _domain.TransformDirection(matrix);
                    break;
                case XRayParticleActionType.Sink:
                    result._domain = _domain.Transform(matrix);
                    break;
            }
            return result;
        }
    }
}
