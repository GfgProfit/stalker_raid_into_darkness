using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public sealed class XRayVegetationOccluderData : ScriptableObject
    {
        public const int VERSION = 1;
        public const string RESOURCE_FOLDER = "XRayVegetationOcclusion";

        [SerializeField] private int _version;
        [SerializeField] private string _signature;
        [SerializeField] private Entry[] _entries;

        public int Version => _version;
        public string Signature => _signature;
        public IReadOnlyList<Entry> Entries => _entries;

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

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private Mesh _mesh;
            [SerializeField] private Bounds _meshBounds;
            [SerializeField] private int _vertexCount;
            [SerializeField] private int _subMeshCount;
            [SerializeField] private Triangle[] _triangles;

            public Mesh Mesh => _mesh;
            public IReadOnlyList<Triangle> Triangles => _triangles;

            public Entry(Mesh mesh, Triangle[] triangles)
            {
                _mesh = mesh;
                _meshBounds = mesh.bounds;
                _vertexCount = mesh.vertexCount;
                _subMeshCount = mesh.subMeshCount;
                _triangles = triangles;
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

            public int SubMesh => _subMesh;
            public Vector3 A => _a;
            public Vector3 B => _b;
            public Vector3 C => _c;

            public Triangle(int subMesh, Vector3 a, Vector3 b, Vector3 c)
            {
                _subMesh = subMesh;
                _a = a;
                _b = b;
                _c = c;
            }
        }
    }
}
