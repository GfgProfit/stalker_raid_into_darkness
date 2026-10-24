using UnityEngine;

namespace XRay.Importer.Levels
{
    public sealed class XRayLevelLodSet : ScriptableObject
    {
        public const int FACES = 8;
        public const int VERTICES = 32;

        [SerializeField] private Material _material;
        [SerializeField] private Vector4[] _spheres;
        [SerializeField] private float[] _lodFactors;
        [SerializeField] private Vector3[] _positions;
        [SerializeField] private Vector2[] _uvs;
        [SerializeField] private float[] _hemi;
        [SerializeField] private float[] _sun;
        [SerializeField] private Vector3[] _normals;
        [SerializeField] private float _geometryLod = 0.5f;
        [SerializeField] private float _ssaLodA = 64.0f;
        [SerializeField] private float _ssaLodB = 48.0f;
        [SerializeField] private float _ssaDiscard = 3.5f;

        public Material Material => _material;
        public int Count => _spheres != null ? _spheres.Length : 0;
        public Vector4[] Spheres => _spheres;
        public float[] LodFactors => _lodFactors;
        public Vector3[] Positions => _positions;
        public Vector2[] Uvs => _uvs;
        public float[] Hemi => _hemi;
        public float[] Sun => _sun;
        public Vector3[] Normals => _normals;
        public float GeometryLod => _geometryLod;
        public float SsaLodA => _ssaLodA;
        public float SsaLodB => _ssaLodB;
        public float SsaDiscard => _ssaDiscard;

        public void Initialize(Material material, Vector4[] spheres, float[] lodFactors, Vector3[] positions, Vector2[] uvs, float[] hemi, float[] sun, Vector3[] normals, float geometryLod, float ssaLodA, float ssaLodB, float ssaDiscard)
        {
            _material = material;
            _spheres = spheres;
            _lodFactors = lodFactors;
            _positions = positions;
            _uvs = uvs;
            _hemi = hemi;
            _sun = sun;
            _normals = normals;
            _geometryLod = geometryLod;
            _ssaLodA = ssaLodA;
            _ssaLodB = ssaLodB;
            _ssaDiscard = ssaDiscard;
        }
    }
}
