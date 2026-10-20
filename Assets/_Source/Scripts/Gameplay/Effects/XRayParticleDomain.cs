using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RaidIntoDarkness.Effects
{
    public enum XRayParticleDomainType
    {
        Point = 0,
        Line = 1,
        Triangle = 2,
        Plane = 3,
        Box = 4,
        Sphere = 5,
        Cylinder = 6,
        Cone = 7,
        Blob = 8,
        Disc = 9,
        Rectangle = 10
    }

    [Serializable]
    public struct XRayParticleDomain
    {
        [SerializeField] private XRayParticleDomainType _type;
        [SerializeField] private Vector3 _p1;
        [SerializeField] private Vector3 _p2;
        [SerializeField] private Vector3 _u;
        [SerializeField] private Vector3 _v;
        [SerializeField] private float _radius1;
        [SerializeField] private float _radius2;

        public XRayParticleDomain(XRayParticleDomainType type, Vector3 p1, Vector3 p2, Vector3 u, Vector3 v, float radius1, float radius2)
        {
            _type = type;
            _p1 = p1;
            _p2 = p2;
            _u = u;
            _v = v;
            _radius1 = radius1;
            _radius2 = radius2;
        }

        public XRayParticleDomainType Type => _type;
        public Vector3 P1 => _p1;
        public Vector3 P2 => _p2;
        public float Radius1 => _radius1;
        public float Radius2 => _radius2;

        public Vector3 Generate()
        {
            switch (_type)
            {
                case XRayParticleDomainType.Point:
                    return _p1;
                case XRayParticleDomainType.Line:
                    return _p1 + _p2 * Random.value;
                case XRayParticleDomainType.Box:
                    return new Vector3(_p1.x + (_p2.x - _p1.x) * Random.value, _p1.y + (_p2.y - _p1.y) * Random.value, _p1.z + (_p2.z - _p1.z) * Random.value);
                case XRayParticleDomainType.Triangle:
                    return GenerateTriangle();
                case XRayParticleDomainType.Rectangle:
                    return _p1 + _u * Random.value + _v * Random.value;
                case XRayParticleDomainType.Plane:
                    return _p1;
                case XRayParticleDomainType.Sphere:
                    return GenerateSphere();
                case XRayParticleDomainType.Cylinder:
                case XRayParticleDomainType.Cone:
                    return GenerateCone();
                case XRayParticleDomainType.Blob:
                    return new Vector3(_p1.x + Gaussian(_radius1), _p1.y + Gaussian(_radius1), _p1.z + Gaussian(_radius1));
                case XRayParticleDomainType.Disc:
                    return GenerateDisc();
                default:
                    return Vector3.zero;
            }
        }

        public XRayParticleDomain Transform(Matrix4x4 matrix)
        {
            XRayParticleDomain result = this;

            switch (_type)
            {
                case XRayParticleDomainType.Box:
                    Vector3 a = matrix.MultiplyPoint3x4(_p1);
                    Vector3 b = matrix.MultiplyPoint3x4(_p2);
                    result._p1 = Vector3.Min(a, b);
                    result._p2 = Vector3.Max(a, b);
                    break;
                case XRayParticleDomainType.Point:
                case XRayParticleDomainType.Sphere:
                case XRayParticleDomainType.Blob:
                    result._p1 = matrix.MultiplyPoint3x4(_p1);
                    break;
                case XRayParticleDomainType.Plane:
                    result._p1 = matrix.MultiplyPoint3x4(_p1);
                    result._p2 = matrix.MultiplyVector(_p2);
                    break;
                default:
                    result._p1 = matrix.MultiplyPoint3x4(_p1);
                    result._p2 = matrix.MultiplyVector(_p2);
                    result._u = matrix.MultiplyVector(_u);
                    result._v = matrix.MultiplyVector(_v);
                    break;
            }

            return result;
        }

        public XRayParticleDomain TransformDirection(Matrix4x4 matrix)
        {
            matrix.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            return Transform(matrix);
        }

        private Vector3 GenerateTriangle()
        {
            float r1 = Random.value;
            float r2 = Random.value;
            return r1 + r2 < 1f ? _p1 + _u * r1 + _v * r2 : _p1 + _u * (1f - r1) + _v * (1f - r2);
        }

        private Vector3 GenerateSphere()
        {
            Vector3 direction = new Vector3(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f);
            direction = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.zero;
            float radius = Mathf.Approximately(_radius1, _radius2) ? _radius1 : _radius2 + Random.value * (_radius1 - _radius2);
            return _p1 + direction * radius;
        }

        private Vector3 GenerateCone()
        {
            float distance = Random.value;
            float theta = Random.value * 2f * Mathf.PI;
            float radius = _radius2 + Random.value * (_radius1 - _radius2);
            float x = radius * Mathf.Cos(theta);
            float y = radius * Mathf.Sin(theta);

            if (_type == XRayParticleDomainType.Cone)
            {
                x *= distance;
                y *= distance;
            }

            return _p1 + _p2 * distance + _u * x + _v * y;
        }

        private Vector3 GenerateDisc()
        {
            float theta = Random.value * 2f * Mathf.PI;
            float radius = _radius2 + Random.value * (_radius1 - _radius2);
            return _p1 + _u * (radius * Mathf.Cos(theta)) + _v * (radius * Mathf.Sin(theta));
        }

        public static float Gaussian(float sigma)
        {
            if (sigma == 0f)
            {
                return 0f;
            }

            float x;
            float y;
            float r2;

            do
            {
                x = Random.value * 2f - 1f;
                y = Random.value * 2f - 1f;
                r2 = x * x + y * y;
            }
            while (r2 > 1f || r2 == 0f);

            return sigma * y * Mathf.Sqrt(-2f * Mathf.Log(r2) / r2);
        }
    }
}
