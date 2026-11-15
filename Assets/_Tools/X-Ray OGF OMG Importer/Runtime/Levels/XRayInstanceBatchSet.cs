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

        [NonSerialized] private bool _matricesExposed;

        public XRayInstanceBatch(string name, Mesh mesh, Material[] materials, Bounds bounds, Matrix4x4[] matrices, Vector4[] data, bool castShadows, int[] lodIndices = null)
        {
            _lodIndices = lodIndices;
            _name = name;
            _mesh = mesh;
            _materials = materials;
            _bounds = bounds;
            _matrices = matrices;
            _matricesExposed = true;
            _data = data;
            _castShadows = castShadows;
        }

        public string Name => _name;
        public Mesh Mesh => _mesh;
        public Material[] Materials => _materials;
        public Bounds Bounds => _bounds;
        public Matrix4x4[] Matrices
        {
            get
            {
                _matricesExposed = true;
                return _matrices;
            }
        }

        internal Matrix4x4[] RenderMatrices => _matrices;
        internal bool MatricesExposed => _matricesExposed;
        public Vector4[] Data => _data;
        public bool CastShadows => _castShadows;
        public int[] LodIndices => _lodIndices;
        public bool HasLods => _lodIndices != null && _lodIndices.Length == Count;
        public int Count => _matrices != null ? _matrices.Length : 0;

        public Matrix4x4 GetMatrix(int index)
        {
            return _matrices[index];
        }

        internal void CaptureOwnedMatrices()
        {
            _matrices = _matrices != null ? (Matrix4x4[])_matrices.Clone() : null;
            _matricesExposed = false;
        }
    }

    public sealed class XRayInstanceBatchSet : ScriptableObject
    {
        public const int MAX_INSTANCES_PER_BATCH = 1023;
        public const int MATRIX_PREPARATION_VERSION = 1;

        [SerializeField] private string _levelName;
        [SerializeField] private float _cellSize;
        [SerializeField] private List<XRayInstanceBatch> _batches = new List<XRayInstanceBatch>();
        [SerializeField] private XRayLevelLodSet _lods;
        [SerializeField] private int _matrixPreparationVersion;
        [SerializeField] private string _matrixInputSignature;

        [NonSerialized] private int _matrixRevision;

        public string LevelName => _levelName;
        public float CellSize => _cellSize;
        public IReadOnlyList<XRayInstanceBatch> Batches => _batches;
        public XRayLevelLodSet Lods => _lods;
        public int MatrixPreparationVersion => _matrixPreparationVersion;
        public string MatrixInputSignature => _matrixInputSignature;
        internal int MatrixRevision => _matrixRevision;
        internal bool HasPreparedMatrices => _matrixPreparationVersion == MATRIX_PREPARATION_VERSION && !string.IsNullOrEmpty(_matrixInputSignature);

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
            _matrixPreparationVersion = 0;
            _matrixInputSignature = null;
            _matrixRevision++;
            _lods = lods;
            _levelName = levelName;
            _cellSize = cellSize;
            _batches = batches;
        }

        public void PrepareMatrices(string inputSignature)
        {
            if (string.IsNullOrEmpty(inputSignature))
            {
                throw new ArgumentException("A preparation input signature is required.", nameof(inputSignature));
            }
            _matrixPreparationVersion = 0;
            foreach (XRayInstanceBatch batch in _batches)
            {
                if (batch == null)
                {
                    throw new InvalidOperationException("A null instance batch cannot be prepared.");
                }
                batch.CaptureOwnedMatrices();
            }
            _matrixRevision++;
            _matrixInputSignature = inputSignature;
            _matrixPreparationVersion = MATRIX_PREPARATION_VERSION;
        }
    }
}
