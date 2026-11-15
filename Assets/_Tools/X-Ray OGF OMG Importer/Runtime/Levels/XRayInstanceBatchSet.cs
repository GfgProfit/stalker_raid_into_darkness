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
        [NonSerialized] private int _matrixRevision;

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
        internal int MatrixRevision => _matrixRevision;
        public Vector4[] Data => _data;
        public bool CastShadows => _castShadows;
        public int[] LodIndices => _lodIndices;
        public bool HasLods => _lodIndices != null && _lodIndices.Length == Count;
        public int Count => _matrices != null ? _matrices.Length : 0;

        public Matrix4x4 GetMatrix(int index)
        {
            return _matrices[index];
        }

        public int GetMatrixOrientation()
        {
            int result = 0;
            for (int index = 0; index < Count; index++)
            {
                float determinant = _matrices[index].determinant;
                if (!float.IsFinite(determinant) || determinant == 0f) return 0;
                int sign = determinant > 0f ? 1 : -1;
                if (index == 0) result = sign;
                else if (result != sign) return 0;
            }
            return result;
        }

        internal void CaptureOwnedMatrices()
        {
            _matrices = _matrices != null ? (Matrix4x4[])_matrices.Clone() : null;
            _matrixRevision++;
            _matricesExposed = false;
        }
    }

    [Serializable]
    public sealed class XRayInstanceBatchGroup
    {
        [SerializeField] private int[] _batchIndices;
        [SerializeField] private Bounds _bounds;

        public IReadOnlyList<int> BatchIndices => _batchIndices;
        public Bounds Bounds => _bounds;

        public XRayInstanceBatchGroup(int[] batchIndices, Bounds bounds)
        {
            _batchIndices = (int[])batchIndices.Clone();
            _bounds = bounds;
        }
    }

    public sealed class XRayInstanceBatchSet : ScriptableObject
    {
        public const int MAX_INSTANCES_PER_BATCH = 1023;
        public const int MATRIX_PREPARATION_VERSION = 1;
        public const int GROUPING_VERSION = 2;
        public const int MAX_GROUP_INSTANCES = 511;

        [SerializeField] private string _levelName;
        [SerializeField] private float _cellSize;
        [SerializeField] private List<XRayInstanceBatch> _batches = new List<XRayInstanceBatch>();
        [SerializeField] private XRayLevelLodSet _lods;
        [SerializeField] private int _matrixPreparationVersion;
        [SerializeField] private string _matrixInputSignature;
        [SerializeField] private int _groupingVersion;
        [SerializeField] private string _groupingSignature;
        [SerializeField] private List<XRayInstanceBatchGroup> _submissionGroups = new List<XRayInstanceBatchGroup>();

        [NonSerialized] private int _matrixRevision;
        [NonSerialized] private int _groupingRevision;
        [NonSerialized] private IReadOnlyList<XRayInstanceBatch> _batchView;

        public string LevelName => _levelName;
        public float CellSize => _cellSize;
        public IReadOnlyList<XRayInstanceBatch> Batches => _batchView ??= _batches.AsReadOnly();
        public XRayLevelLodSet Lods => _lods;
        public int MatrixPreparationVersion => _matrixPreparationVersion;
        public string MatrixInputSignature => _matrixInputSignature;
        public int GroupingVersion => _groupingVersion;
        public string GroupingSignature => _groupingSignature;
        public IReadOnlyList<XRayInstanceBatchGroup> SubmissionGroups => _submissionGroups;
        internal int MatrixRevision => _matrixRevision;
        internal int GroupingRevision => _groupingRevision;
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

        public static bool SupportsSubmissionMaterial(Material material)
        {
            if (material == null) return true;
            if (!material.enableInstancing || material.renderQueue >= 3000) return false;
            if (material.HasProperty("_SurfaceType")) return material.GetFloat("_SurfaceType") == 0f;
            string renderType = material.GetTag("RenderType", false, string.Empty);
            return renderType == "Opaque" || renderType == "TransparentCutout";
        }

        public void Initialize(string levelName, float cellSize, List<XRayInstanceBatch> batches, XRayLevelLodSet lods = null)
        {
            if (batches == null) throw new ArgumentNullException(nameof(batches));
            _matrixPreparationVersion = 0;
            _matrixInputSignature = null;
            _matrixRevision++;
            _groupingVersion = 0;
            _groupingSignature = null;
            _submissionGroups.Clear();
            _groupingRevision++;
            _lods = lods;
            _levelName = levelName;
            _cellSize = cellSize;
            _batches = new List<XRayInstanceBatch>(batches);
            _batchView = null;
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

        public void PrepareSubmissionGroups(List<XRayInstanceBatchGroup> groups, string signature)
        {
            if (groups == null || string.IsNullOrEmpty(signature)) throw new ArgumentException("Submission groups and their input signature are required.");
            var used = new HashSet<int>();
            foreach (var group in groups)
            {
                if (group == null || group.BatchIndices.Count < 2) throw new ArgumentException("A submission group needs at least two source batches.");
                int count = 0;
                foreach (int index in group.BatchIndices)
                {
                    if (index < 0 || index >= _batches.Count || !used.Add(index)) throw new ArgumentException("Invalid or repeated source batch index.");
                    count += _batches[index].Count;
                }
                if (count > MAX_GROUP_INSTANCES) throw new ArgumentException("Submission group exceeds its instance limit.");
            }
            _groupingVersion = 0;
            _submissionGroups = new List<XRayInstanceBatchGroup>(groups);
            _groupingSignature = signature;
            _groupingRevision++;
            _groupingVersion = GROUPING_VERSION;
        }

        public void InvalidateSubmissionGroups()
        {
            _groupingVersion = 0;
            _groupingRevision++;
        }
    }
}
