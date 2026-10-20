using System;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    public enum XRayParticleActionType
    {
        Avoid = 0,
        Bounce = 1,
        CallActionList = 2,
        CopyVertexB = 3,
        Damping = 4,
        Explosion = 5,
        Follow = 6,
        Gravitate = 7,
        Gravity = 8,
        Jet = 9,
        KillOld = 10,
        MatchVelocity = 11,
        Move = 12,
        OrbitLine = 13,
        OrbitPoint = 14,
        RandomAccel = 15,
        RandomDisplace = 16,
        RandomVelocity = 17,
        Restore = 18,
        Sink = 19,
        SinkVelocity = 20,
        Source = 21,
        SpeedLimit = 22,
        TargetColor = 23,
        TargetSize = 24,
        TargetRotate = 25,
        TargetRotateD = 26,
        TargetVelocity = 27,
        TargetVelocityD = 28,
        Vortex = 29,
        Turbulence = 30,
        Scatter = 31
    }

    [Serializable]
    public sealed class XRayParticleAction
    {
        public const uint ALLOW_ROTATE = 1u << 1;
        public const uint SOURCE_SINGLE_SIZE = 1u << 29;
        public const uint SOURCE_SILENT = 1u << 30;

        [SerializeField] private XRayParticleActionType _type;
        [SerializeField] private uint _flags;
        [SerializeField] private XRayParticleDomain _position;
        [SerializeField] private XRayParticleDomain _velocity;
        [SerializeField] private XRayParticleDomain _rotation;
        [SerializeField] private XRayParticleDomain _size;
        [SerializeField] private XRayParticleDomain _color;
        [SerializeField] private Vector3 _vector;
        [SerializeField] private Vector3 _scale;
        [SerializeField] private float _alpha;
        [SerializeField] private float _rate;
        [SerializeField] private float _age;
        [SerializeField] private float _ageSigma;
        [SerializeField] private float _parentMotion;
        [SerializeField] private float _factor;
        [SerializeField] private float _timeFrom;
        [SerializeField] private float _timeTo;
        [SerializeField] private bool _killLessThan;

        public XRayParticleActionType Type => _type;
        public uint Flags => _flags;
        public bool AllowRotate => (_flags & ALLOW_ROTATE) != 0;
        public XRayParticleDomain Position => _position;
        public XRayParticleDomain Velocity => _velocity;
        public XRayParticleDomain Rotation => _rotation;
        public XRayParticleDomain Size => _size;
        public XRayParticleDomain Color => _color;
        public Vector3 Vector => _vector;
        public Vector3 Scale => _scale;
        public float Alpha => _alpha;
        public float Rate => _rate;
        public float Age => _age;
        public float AgeSigma => _ageSigma;
        public float ParentMotion => _parentMotion;
        public float Factor => _factor;
        public float TimeFrom => _timeFrom;
        public float TimeTo => _timeTo;
        public bool KillLessThan => _killLessThan;

        public static XRayParticleAction CreateSource(uint flags, XRayParticleDomain position, XRayParticleDomain velocity, XRayParticleDomain rotation, XRayParticleDomain size, XRayParticleDomain color, float alpha, float rate, float age, float ageSigma, Vector3 parentVelocity, float parentMotion)
        {
            return new XRayParticleAction { _type = XRayParticleActionType.Source, _flags = flags, _position = position, _velocity = velocity, _rotation = rotation, _size = size, _color = color, _alpha = alpha, _rate = rate, _age = age, _ageSigma = ageSigma, _vector = parentVelocity, _parentMotion = parentMotion };
        }

        public static XRayParticleAction CreateKillOld(uint flags, float ageLimit, bool killLessThan)
        {
            return new XRayParticleAction { _type = XRayParticleActionType.KillOld, _flags = flags, _age = ageLimit, _killLessThan = killLessThan };
        }

        public static XRayParticleAction CreateTargetColor(uint flags, Vector3 color, float alpha, float scale, float timeFrom, float timeTo)
        {
            return new XRayParticleAction { _type = XRayParticleActionType.TargetColor, _flags = flags, _vector = color, _alpha = alpha, _factor = scale, _timeFrom = timeFrom, _timeTo = timeTo };
        }

        public static XRayParticleAction CreateVector(XRayParticleActionType type, uint flags, Vector3 vector, Vector3 scale, float factor)
        {
            return new XRayParticleAction { _type = type, _flags = flags, _vector = vector, _scale = scale, _factor = factor };
        }

        public static XRayParticleAction CreateDomain(XRayParticleActionType type, uint flags, XRayParticleDomain domain)
        {
            return new XRayParticleAction { _type = type, _flags = flags, _position = domain };
        }

        public static XRayParticleAction CreateEmpty(XRayParticleActionType type, uint flags)
        {
            return new XRayParticleAction { _type = type, _flags = flags };
        }
    }
}
