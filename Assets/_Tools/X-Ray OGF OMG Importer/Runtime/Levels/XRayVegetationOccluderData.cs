using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    public sealed class XRayVegetationOccluderData : ScriptableObject
    {
        public const int VERSION = 2;
        public const string RESOURCE_FOLDER = "XRayVegetationOcclusion";

        private const int MESH_ONLY_VERSION = 1;
        private const string MESH_SHADER = "HDRP/MonolithLit";

        [SerializeField] private int _version;
        [SerializeField] private string _signature;
        [SerializeField] private Entry[] _entries;

        public int Version => _version;
        public string Signature => _signature;
        public IReadOnlyList<Entry> Entries => _entries;
        internal bool IsReadableVersion => _version == VERSION || _version == MESH_ONLY_VERSION;

        public void Initialize(string signature, Entry[] entries)
        {
            _version = VERSION;
            _signature = signature;
            _entries = entries;
        }

        public static string ResourceKey(string levelFolder)
        {
            int separator = levelFolder.LastIndexOf('/');
            string level = levelFolder.Substring(separator + 1);
            return RESOURCE_FOLDER + "/" + level + "/" + Hash128.Compute(levelFolder).ToString();
        }

        public static bool SupportsOccluderMaterial(Material material)
        {
            return OccluderCullMode(material) >= 0;
        }

        internal static int OccluderCullMode(Material material)
        {
            if (material == null || material.shader == null || material.shader.name != MESH_SHADER || material.renderQueue < (int)RenderQueue.Geometry || material.renderQueue > (int)RenderQueue.GeometryLast) return -1;
            if (material.GetFloat("_SurfaceType") != 0f || material.GetFloat("_ZWrite") < 0.5f || material.GetFloat("_AlphaCutoffEnable") > 0.5f || material.GetFloat("_DisplacementMode") != 0f || material.GetFloat("_DepthOffsetEnable") > 0.5f || material.GetFloat("_MonolithFloraWind") > 0f) return -1;
            if (material.IsKeywordEnabled("_ALPHATEST_ON") || material.IsKeywordEnabled("_MONOLITH_LEVEL_DETAIL") || material.IsKeywordEnabled("_MONOLITH_LEVEL_LOD") || material.IsKeywordEnabled("_MONOLITH_SKINNED_TBN") || material.IsKeywordEnabled("_VERTEX_DISPLACEMENT") || material.IsKeywordEnabled("_DEPTHOFFSET_ON")) return -1;
            if (!material.GetShaderPassEnabled("DepthForwardOnly") || !material.GetShaderPassEnabled("ForwardOnly")) return -1;
            int mode = (int)material.GetFloat("_CullMode");
            return (mode == (int)CullMode.Back || mode == (int)CullMode.Off) && mode == (int)material.GetFloat("_CullModeForward") ? mode : -1;
        }

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private Mesh _mesh;
            [SerializeField] private Bounds _meshBounds;
            [SerializeField] private int _vertexCount;
            [SerializeField] private int _subMeshCount;
            [SerializeField] private Triangle[] _triangles;
            [SerializeField] private Triangle[] _homTriangles;

            public Mesh Mesh => _mesh;
            public IReadOnlyList<Triangle> Triangles => _triangles;
            public IReadOnlyList<Triangle> HomTriangles => _homTriangles;

            public Entry(Mesh mesh, Triangle[] triangles, Triangle[] homTriangles = null)
            {
                _mesh = mesh;
                _meshBounds = mesh.bounds;
                _vertexCount = mesh.vertexCount;
                _subMeshCount = mesh.subMeshCount;
                _triangles = triangles;
                _homTriangles = homTriangles;
            }

            internal bool Matches(Mesh mesh)
            {
                return mesh != null && mesh == _mesh && mesh.vertexCount == _vertexCount && mesh.subMeshCount == _subMeshCount && mesh.bounds.Equals(_meshBounds);
            }
        }

        [Serializable]
        public struct Triangle
        {
            [SerializeField] private int _subMesh;
            [SerializeField] private Vector3 _a;
            [SerializeField] private Vector3 _b;
            [SerializeField] private Vector3 _c;
            [SerializeField] private bool _doubleSided;

            public int SubMesh => _subMesh;
            public Vector3 A => _a;
            public Vector3 B => _b;
            public Vector3 C => _c;
            public bool DoubleSided => _doubleSided;

            public Triangle(int subMesh, Vector3 a, Vector3 b, Vector3 c, bool doubleSided = false)
            {
                _subMesh = subMesh;
                _a = a;
                _b = b;
                _c = c;
                _doubleSided = doubleSided;
            }
        }
    }
}
