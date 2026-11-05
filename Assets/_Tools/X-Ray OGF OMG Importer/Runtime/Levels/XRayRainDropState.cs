using System.Runtime.InteropServices;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct XRayRainDropState
    {
        private readonly Vector3 _position;
        private readonly float _age;
        private readonly Vector3 _velocity;
        private readonly uint _seed;
        private readonly Vector3 _splashPosition;
        private readonly float _splashAge;
        private readonly float _lifetime;
        private readonly float _size;
        private readonly Vector2 _reserved;

        public Vector3 Position => _position;
        public float Age => _age;
        public Vector3 Velocity => _velocity;
        public uint Seed => _seed;
        public Vector3 SplashPosition => _splashPosition;
        public float SplashAge => _splashAge;
        public float Lifetime => _lifetime;
        public float Size => _size;
    }
}
