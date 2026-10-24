using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [Serializable]
    public sealed class XRayInstanceBatch
    {
        [SerializeField] private string _name;
        [SerializeField] private Mesh _mesh;
        [SerializeField] private Material[] _materials;
        [SerializeField] private Bounds _bounds;
        [SerializeField] private Matrix4x4[] _matrices;
        [SerializeField] private Vector4[] _data;
        [SerializeField] private bool _castShadows = true;
        [SerializeField] private int[] _lodIndices;

        public XRayInstanceBatch(string name, Mesh mesh, Material[] materials, Bounds bounds, Matrix4x4[] matrices, Vector4[] data, bool castShadows, int[] lodIndices = null)
        {
            _lodIndices = lodIndices;
            _name = name;
            _mesh = mesh;
            _materials = materials;
            _bounds = bounds;
            _matrices = matrices;
            _data = data;
            _castShadows = castShadows;
        }

        public string Name => _name;
        public Mesh Mesh => _mesh;
        public Material[] Materials => _materials;
        public Bounds Bounds => _bounds;
        public Matrix4x4[] Matrices => _matrices;
        public Vector4[] Data => _data;
        public bool CastShadows => _castShadows;
        public int[] LodIndices => _lodIndices;
        public bool HasLods => _lodIndices != null && _lodIndices.Length == Count;
        public int Count => _matrices != null ? _matrices.Length : 0;
    }

    public sealed class XRayInstanceBatchSet : ScriptableObject
    {
        public const int MAX_INSTANCES_PER_BATCH = 1023;

        [SerializeField] private string _levelName;
        [SerializeField] private float _cellSize;
        [SerializeField] private List<XRayInstanceBatch> _batches = new List<XRayInstanceBatch>();
        [SerializeField] private XRayLevelLodSet _lods;

        public string LevelName => _levelName;
        public float CellSize => _cellSize;
        public IReadOnlyList<XRayInstanceBatch> Batches => _batches;
        public XRayLevelLodSet Lods => _lods;

        public int InstanceCount
        {
            get
            {
                int count = 0;
                foreach (XRayInstanceBatch batch in _batches)
                {
                    count += batch.Count;
                }

                return count;
            }
        }

        public void Initialize(string levelName, float cellSize, List<XRayInstanceBatch> batches, XRayLevelLodSet lods = null)
        {
            _lods = lods;
            _levelName = levelName;
            _cellSize = cellSize;
            _batches = batches;
        }
    }
}
