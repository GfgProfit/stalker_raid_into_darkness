using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Levels
{
    public sealed class XRayInstanceNativeRenderer : IDisposable
    {
        private const uint PER_INSTANCE = 0x80000000u;
        private const int PACKED_MATRIX_BYTES = 48;
        private const int VECTOR_BYTES = 16;
        private const int MATRIX_START = 96;
        private const int MAX_REFLECTION_VIEWS = 64;
        private const int POINT_SHADOW_SPLITS = 6;

        private static readonly ProfilerMarker CULLING_MARKER = new ProfilerMarker("XRay.NativeInstanceCulling");
        private static readonly ProfilerMarker SHADOW_SELECT_MARKER = new ProfilerMarker("XRay.NativeShadowSelection");
        private static readonly ProfilerMarker SHADOW_OUTPUT_MARKER = new ProfilerMarker("XRay.NativeShadowOutput");
        private static readonly int WIND_ID = Shader.PropertyToID("_MonolithFloraWind");
        private static readonly int DETAIL_ID = Shader.PropertyToID("_MonolithLevelDetail");
        private static readonly Func<Camera> SHADOW_CAMERA = CreateShadowCameraAccessor(typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDShadowCullingCameraScope"));
        private static readonly BatchCullingViewType[] ENABLED_VIEWS = { BatchCullingViewType.Camera, BatchCullingViewType.Light };
        private static readonly BatchCullingViewType[] DISABLED_VIEWS = Array.Empty<BatchCullingViewType>();
        private static readonly IReadOnlyList<string> UNSUPPORTED_SHADER_KEYWORDS = Array.AsReadOnly(new[] { "_MONOLITH_LEVEL_DETAIL", "_MONOLITH_SKINNED_TBN", "_MONOLITH_LEVEL_LOD", "_SURFACE_TYPE_TRANSPARENT", "_VERTEX_DISPLACEMENT", "_PIXEL_DISPLACEMENT", "_DEPTHOFFSET_ON", "LOD_FADE_CROSSFADE", "LIGHTMAP_ON", "DYNAMICLIGHTMAP_ON" });

        private readonly Dictionary<Mesh, MeshState> _meshes = new Dictionary<Mesh, MeshState>();
        private readonly Dictionary<Material, MaterialState> _materials = new Dictionary<Material, MaterialState>();
        private readonly Dictionary<Material, Material> _resolved = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, bool> _reflectionWindMaterials = new Dictionary<Material, bool>();
        private readonly Dictionary<Mesh, BatchMeshID> _meshIds = new Dictionary<Mesh, BatchMeshID>();
        private readonly Dictionary<Material, BatchMaterialID> _materialIds = new Dictionary<Material, BatchMaterialID>();
        private readonly List<DrawSpec> _specifications = new List<DrawSpec>();
        private readonly List<ReflectionView> _reflectionViews = new List<ReflectionView>();
        private readonly XRayInstanceVisibilityIndex _index = new XRayInstanceVisibilityIndex();
        private readonly int[] _pointShadowStarts = new int[POINT_SHADOW_SPLITS];
        private readonly int[] _pointShadowCounts = new int[POINT_SHADOW_SPLITS];

        private BatchRendererGroup _group;
        private Shader _shader;
        private GraphicsBuffer _buffer;
        private XRayInstanceBatchSet _set;
        private XRayInstanceBatchSet _rejectedSet;
        private string _rejectedReason;
        private int _rejectedRevision;
        private int _revision;
        private Source[] _sources;
        private Draw[] _draws;
        private Bounds[] _instanceBounds;
        private Bounds[] _windBounds;
        private int[] _windBoundsStamps;
        private bool[] _sourceWind;
        private Bounds[] _sourceBounds;
        private Bounds[] _physicalSourceBounds;
        private ushort[] _sourceSplitMasks;
        private int[] _sourceShadowCounts;
        private ushort[] _instanceMasks;
        private int[] _sourceStamps;
        private int[] _selectedSources;
        private BatchDrawCommand[] _plannedCommands;
        private int[] _visibleIndices;
        private Vector4[] _payload;
        private byte[] _cameraLodStates;
        private XRayVegetationOcclusion _cameraOcclusion;
        private Plane[] _planes = new Plane[0];
        private XRayFloraWindState _wind;
        private XRayFloraWindBounds.SnapshotKey _windBoundsKey;
        private int _windBoundsStamp;
        private Bounds _globalBounds;
        private Bounds _preparedGlobalBounds;
        private Bounds _cachedGlobalBounds;
        private XRayFloraWindBounds.SnapshotKey _globalBoundsKey;
        private bool _globalBoundsDirty = true;
        private float _maximumHeight;
        private BatchID _batchId;
        private EntityId _cameraId;
        private int _layer;
        private uint _renderingLayerMask;
        private int _instanceCount;
        private int _dataOffset;
        private int _cullingStamp;
        private int _selectedCount;
        private bool _active;

        public static IReadOnlyList<string> UnsupportedShaderKeywords => UNSUPPORTED_SHADER_KEYWORDS;

        public bool Active => _active;
        public long MemoryBytes => (_buffer != null ? (long)_buffer.count * _buffer.stride : 0L) + (_instanceBounds != null ? _instanceBounds.LongLength * 24L : 0L) + (_windBounds != null ? _windBounds.LongLength * 24L : 0L) + (_windBoundsStamps != null ? _windBoundsStamps.LongLength * sizeof(int) : 0L) + (_sourceWind != null ? _sourceWind.LongLength : 0L) + (_sourceBounds != null ? _sourceBounds.LongLength * 24L : 0L) + (_physicalSourceBounds != null ? _physicalSourceBounds.LongLength * 24L : 0L) + (_sourceSplitMasks != null ? _sourceSplitMasks.LongLength * 2L : 0L) + (_sourceShadowCounts != null ? _sourceShadowCounts.LongLength * sizeof(int) : 0L) + (_payload != null ? _payload.LongLength * VECTOR_BYTES : 0L) + (_instanceMasks != null ? _instanceMasks.LongLength * 2L : 0L) + (_sources != null ? _sources.LongLength * 120L : 0L) + (_draws != null ? _draws.LongLength * 32L : 0L) + (_plannedCommands != null ? _plannedCommands.LongLength * UnsafeUtility.SizeOf<BatchDrawCommand>() : 0L) + (_visibleIndices != null ? _visibleIndices.LongLength * sizeof(int) : 0L) + _planes.LongLength * VECTOR_BYTES + _index.MemoryBytes + (long)_reflectionViews.Capacity * UnsafeUtility.SizeOf<ReflectionView>();
        public int LastCameraCommands { get; private set; }
        public int LastCameraInstances { get; private set; }
        public int LastCameraBatches { get; private set; }
        public int LastShadowCommands { get; private set; }
        public int LastShadowInstances { get; private set; }
        public int LastCulledBatches { get; private set; }
        public int LastCulledInstances { get; private set; }
        public int ShadowViews { get; private set; }
        public int ReflectionCommands { get; private set; }
        public int ReflectionInstances { get; private set; }
        public int ReflectionBatches { get; private set; }
        public int ReflectionViews { get; private set; }
        public int RegisteredReflectionViews => _reflectionViews.Count;
        public int CullingInvocations { get; private set; }
        public int VisitedNodes { get; private set; }
        public long LastCullingOutputBytes { get; private set; }
        public string FallbackReason { get; private set; }

        public bool Prepare(XRayInstanceBatchSet set, Camera camera, int layer, bool castShadows, XRayFloraWindState wind, Func<Material, Material> resolver = null)
        {
            return Prepare(set, camera, layer, castShadows, wind, resolver, null, null);
        }

        internal bool Prepare(XRayInstanceBatchSet set, Camera camera, int layer, bool castShadows, XRayFloraWindState wind, Func<Material, Material> resolver, XRayVegetationOcclusion occlusion, byte[] lodStates)
        {
            Disable();
            _reflectionViews.Clear();
            if (set == null || camera == null || !set.HasPreparedMatrices || set.Batches.Count == 0) return Unsupported("Prepared instance data and a camera are required.");
            if (SHADOW_CAMERA == null || !(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset)) return Unsupported("The active pipeline does not expose supported HDRP shadow camera ownership.");
            if (_rejectedSet == set && _rejectedRevision == set.MatrixRevision) return Unsupported(_rejectedReason);
            if (!SystemInfo.supportsInstancing || BatchRendererGroup.BufferTarget != BatchBufferTarget.RawBuffer) return Unsupported("The graphics device does not support raw-buffer batch rendering.");
            if (_shader == null)
            {
                _shader = Shader.Find("HDRP/MonolithLit");
            }
            if (HasUnsupportedGlobalShaderKeyword()) return Unsupported("A global keyword does not support the native instance shader contract.");
            _meshes.Clear();
            _materials.Clear();
            _resolved.Clear();
            _reflectionWindMaterials.Clear();
            _specifications.Clear();
            bool rebuild = _group == null || _set != set || _revision != set.MatrixRevision || _sources.Length != set.Batches.Count;
            int instanceCount = 0;
            for (int index = 0; index < set.Batches.Count; index++)
            {
                XRayInstanceBatch source = set.Batches[index];
                if (source == null || source.MatricesExposed || source.Mesh == null || source.Materials == null || source.Count == 0 || source.Data == null || source.Data.Length < source.Count || !Finite(source.Bounds)) return Unsupported("A source batch is mutable, missing or does not have a complete instance payload.");
                MeshState mesh;
                if (!_meshes.TryGetValue(source.Mesh, out mesh))
                {
                    mesh = new MeshState { Bounds = source.Mesh.bounds, Submeshes = source.Mesh.subMeshCount };
                    _meshes.Add(source.Mesh, mesh);
                }
                if (!Finite(mesh.Bounds) || mesh.Submeshes < 1 || mesh.Submeshes > ushort.MaxValue) return Unsupported("A mesh has unsupported bounds or submeshes.");
                if (!rebuild)
                {
                    Source previous = _sources[index];
                    rebuild |= !ReferenceEquals(previous.Batch, source) || previous.Mesh != source.Mesh || !previous.MeshBounds.Equals(mesh.Bounds) || previous.Submeshes != mesh.Submeshes || previous.MatrixRevision != source.MatrixRevision || !ReferenceEquals(previous.Matrices, source.RenderMatrices);
                }
                int submeshes = Mathf.Min(mesh.Submeshes, source.Materials.Length);
                for (int submesh = 0; submesh < submeshes; submesh++)
                {
                    Material sourceMaterial = source.Materials[submesh];
                    if (sourceMaterial == null) continue;
                    Material material;
                    if (!_resolved.TryGetValue(sourceMaterial, out material))
                    {
                        material = resolver != null ? resolver(sourceMaterial) : sourceMaterial;
                        _resolved.Add(sourceMaterial, material);
                    }
                    if (material == null) continue;
                    MaterialState state;
                    if (!_materials.TryGetValue(material, out state))
                    {
                        state = ReadMaterialState(material, wind, occlusion != null);
                        _materials.Add(material, state);
                    }
                    if (!state.Supported) return Unsupported("A material does not support the native instance shader contract.");
                    _specifications.Add(new DrawSpec { SourceIndex = index, Material = material, Submesh = submesh, Wind = state.Wind });
                }
                instanceCount = checked(instanceCount + source.Count);
            }
            if (_specifications.Count == 0) return Unsupported("No supported submeshes are available.");
            if (!rebuild)
            {
                rebuild = _draws.Length != _specifications.Count;
                for (int index = 0; !rebuild && index < _draws.Length; index++)
                {
                    DrawSpec specification = _specifications[index];
                    Draw previous = _draws[index];
                    rebuild = previous.SourceIndex != specification.SourceIndex || previous.Material != specification.Material || previous.Submesh != specification.Submesh;
                }
            }
            if (rebuild && !Build(set, instanceCount)) return false;
            for (int index = 0; index < _sources.Length; index++)
            {
                Source source = _sources[index];
                source.CastShadows = castShadows && source.Batch.CastShadows;
                source.Wind = false;
                source.Flora = occlusion != null && source.DrawCount > 0;
                bool reflectionWind = UsesReflectionWind(source.Batch.Materials, wind);
                _globalBoundsDirty |= source.ReflectionWind != reflectionWind;
                source.ReflectionWind = reflectionWind;
                source.ReflectionBounds = source.ReflectionWind ? XRayFloraWindBounds.Expand(source.Batch.Bounds, wind) : source.Batch.Bounds;
                _sources[index] = source;
            }
            for (int index = 0; index < _draws.Length; index++)
            {
                Draw draw = _draws[index];
                draw.Wind = _specifications[index].Wind;
                _draws[index] = draw;
                Source source = _sources[draw.SourceIndex];
                source.Wind |= draw.Wind;
                if (source.Flora) source.Flora = _materials[draw.Material].Flora;
                _sources[draw.SourceIndex] = source;
            }
            bool payloadChanged = false;
            for (int sourceIndex = 0; sourceIndex < _sources.Length; sourceIndex++)
            {
                Source source = _sources[sourceIndex];
                if (_sourceWind[sourceIndex] != source.Wind)
                {
                    Array.Clear(_windBoundsStamps, source.Start, source.Count);
                    _sourceWind[sourceIndex] = source.Wind;
                    _globalBoundsDirty = true;
                }
                Vector4[] data = source.Batch.Data;
                for (int index = 0; index < source.Count; index++)
                {
                    Vector4 value = data != null && index < data.Length ? data[index] : new Vector4(1f, 0f, 0f, 0f);
                    if (!Finite(value)) return Unsupported("An instance payload contains non-finite values.");
                    int destination = source.Start + index;
                    if (_payload[destination].Equals(value)) continue;
                    _payload[destination] = value;
                    payloadChanged = true;
                }
            }
            if (payloadChanged) _buffer.SetData(_payload, 0, _dataOffset / VECTOR_BYTES, _payload.Length);
            _index.Ensure(set, _sourceBounds, _revision);
            _wind = wind;
            _layer = layer;
            _renderingLayerMask = UnityEngine.RenderingLayerMask.defaultRenderingLayerMask;
            _cameraId = camera.GetEntityId();
            Bounds globalBounds = CurrentGlobalBounds();
            if (!Finite(globalBounds)) return Unsupported("The current wind-expanded global bounds are non-finite.");
            _group.SetGlobalBounds(globalBounds);
            _preparedGlobalBounds = globalBounds;
            ClearStatistics();
            FallbackReason = null;
            _cameraLodStates = lodStates;
            _cameraOcclusion = occlusion;
            _group.SetEnabledViewTypes(ENABLED_VIEWS);
            _active = true;
            return true;
        }

        public void Disable()
        {
            if (_active) _group?.SetEnabledViewTypes(DISABLED_VIEWS);
            _active = false;
            _cameraLodStates = null;
            _cameraOcclusion = null;
        }

        public bool TryGetResolvedMaterial(Material source, out Material material)
        {
            material = null;
            return _active && source != null && _resolved.TryGetValue(source, out material);
        }

        public bool TryRegisterReflection(Camera camera)
        {
            if (!_active || camera == null || camera.cameraType != CameraType.Reflection || camera.stereoEnabled) return false;
            EntityId id = camera.GetEntityId();
            int existing = -1;
            for (int index = 0; index < _reflectionViews.Count; index++)
            {
                if (_reflectionViews[index].Id == id) existing = index;
            }
            Vector3 position = camera.transform.position;
            float distance = camera.farClipPlane;
            float distanceSquared = distance * distance;
            if (!Finite(position) || !float.IsFinite(distanceSquared) || distance <= 0f || !ReflectionSnapshotCurrent())
            {
                if (existing >= 0) _reflectionViews.RemoveAt(existing);
                return false;
            }
            var view = new ReflectionView { Id = id, Position = position, DistanceSquared = distanceSquared };
            if (existing >= 0)
            {
                _reflectionViews[existing] = view;
                return true;
            }
            if (_reflectionViews.Count == MAX_REFLECTION_VIEWS) return false;
            if (_reflectionViews.Count == _reflectionViews.Capacity) _reflectionViews.Capacity = Mathf.Min(MAX_REFLECTION_VIEWS, Mathf.Max(6, _reflectionViews.Capacity * 2));
            _reflectionViews.Add(view);
            return true;
        }

        public void UnregisterReflection(Camera camera)
        {
            if (camera == null) return;
            EntityId id = camera.GetEntityId();
            for (int index = 0; index < _reflectionViews.Count; index++)
            {
                if (_reflectionViews[index].Id != id) continue;
                _reflectionViews.RemoveAt(index);
                return;
            }
        }

        private bool ReflectionSnapshotCurrent()
        {
            if (_set == null || !_set.HasPreparedMatrices || _set.MatrixRevision != _revision || _set.Batches.Count != _sources.Length || HasUnsupportedGlobalShaderKeyword()) return false;
            if (!CurrentGlobalBounds().Equals(_preparedGlobalBounds)) return false;
            foreach (KeyValuePair<Mesh, MeshState> entry in _meshes)
            {
                if (entry.Key == null || entry.Key.subMeshCount != entry.Value.Submeshes || !entry.Key.bounds.Equals(entry.Value.Bounds)) return false;
            }
            foreach (KeyValuePair<Material, MaterialState> entry in _materials)
            {
                if (entry.Key == null) return false;
                MaterialState current = ReadMaterialState(entry.Key, _wind);
                if (!current.Supported || current.Wind != entry.Value.Wind) return false;
            }
            foreach (KeyValuePair<Material, bool> entry in _reflectionWindMaterials)
            {
                if (entry.Key == null || XRayFloraWindBounds.Uses(entry.Key, _wind) != entry.Value) return false;
            }
            for (int sourceIndex = 0; sourceIndex < _sources.Length; sourceIndex++)
            {
                Source source = _sources[sourceIndex];
                XRayInstanceBatch batch = _set.Batches[sourceIndex];
                if (!ReferenceEquals(batch, source.Batch) || batch.MatricesExposed || batch.MatrixRevision != source.MatrixRevision || !ReferenceEquals(batch.RenderMatrices, source.Matrices) || batch.Count != source.Count || batch.Mesh != source.Mesh || batch.Materials == null || batch.Data == null || batch.Data.Length < source.Count) return false;
                for (int index = 0; index < source.Count; index++)
                {
                    if (!batch.Data[index].Equals(_payload[source.Start + index])) return false;
                }
                bool reflectionWind = false;
                for (int index = 0; index < batch.Materials.Length; index++)
                {
                    Material material = batch.Materials[index];
                    if (material == null) continue;
                    if (!_reflectionWindMaterials.TryGetValue(material, out reflectionWind)) return false;
                    if (reflectionWind) break;
                }
                if (reflectionWind != source.ReflectionWind) return false;
                Bounds bounds = reflectionWind ? XRayFloraWindBounds.Expand(batch.Bounds, _wind) : batch.Bounds;
                if (!bounds.Equals(source.ReflectionBounds)) return false;
                int drawIndex = source.DrawStart;
                int drawEnd = drawIndex + source.DrawCount;
                int submeshes = Mathf.Min(source.Submeshes, batch.Materials.Length);
                for (int submesh = 0; submesh < submeshes; submesh++)
                {
                    Material material = batch.Materials[submesh];
                    if (material == null) continue;
                    Material resolved;
                    if (!_resolved.TryGetValue(material, out resolved)) return false;
                    if (resolved == null) continue;
                    if (drawIndex >= drawEnd || _draws[drawIndex].Submesh != submesh || _draws[drawIndex].Material != resolved) return false;
                    drawIndex++;
                }
                if (drawIndex != drawEnd) return false;
            }
            return true;
        }

        private MaterialState ReadMaterialState(Material material, XRayFloraWindState wind, bool classifyFlora = false)
        {
            float materialWind = material.HasProperty(WIND_ID) ? material.GetFloat(WIND_ID) : 0f;
            bool supportedWind = materialWind <= 0.5f || wind != null && wind.Initialized && Mathf.Abs(materialWind - (float)wind.Profile) < 0.5f;
            bool supportedContract = !HasUnsupportedShaderKeyword(material) && (!material.HasProperty(DETAIL_ID) || material.GetFloat(DETAIL_ID) <= 0.5f);
            return new MaterialState { Supported = _shader != null && material.shader == _shader && material.renderQueue <= (int)RenderQueue.GeometryLast && XRayInstanceBatchSet.SupportsSubmissionMaterial(material) && supportedWind && supportedContract, Wind = XRayFloraWindBounds.Uses(material, wind), Flora = classifyFlora && XRayVegetationOcclusion.IsFlora(material) };
        }

        private bool UsesReflectionWind(Material[] materials, XRayFloraWindState wind)
        {
            for (int index = 0; index < materials.Length; index++)
            {
                Material material = materials[index];
                if (material == null) continue;
                bool uses;
                if (!_reflectionWindMaterials.TryGetValue(material, out uses))
                {
                    uses = XRayFloraWindBounds.Uses(material, wind);
                    _reflectionWindMaterials.Add(material, uses);
                }
                if (uses) return true;
            }
            return false;
        }

        private static bool HasUnsupportedShaderKeyword(Material material)
        {
            for (int index = 0; index < UNSUPPORTED_SHADER_KEYWORDS.Count; index++)
            {
                if (material.IsKeywordEnabled(UNSUPPORTED_SHADER_KEYWORDS[index])) return true;
            }
            return false;
        }

        private bool HasUnsupportedGlobalShaderKeyword()
        {
            if (_shader == null)
            {
                return false;
            }
            for (int index = 0; index < UNSUPPORTED_SHADER_KEYWORDS.Count; index++)
            {
                string keywordName = UNSUPPORTED_SHADER_KEYWORDS[index];
                if (!Shader.IsKeywordEnabled(keywordName))
                {
                    continue;
                }
                LocalKeyword keyword = _shader.keywordSpace.FindKeyword(keywordName);
                if (keyword.isValid && keyword.isOverridable)
                {
                    return true;
                }
            }
            return false;
        }

        internal void BeginContext()
        {
            Disable();
            _reflectionViews.Clear();
            ClearStatistics();
            FallbackReason = null;
        }

        public void Dispose()
        {
            ReleaseGraphics();
            _meshes.Clear();
            _materials.Clear();
            _resolved.Clear();
            _reflectionWindMaterials.Clear();
            _specifications.Clear();
            _meshes.TrimExcess();
            _materials.TrimExcess();
            _resolved.TrimExcess();
            _reflectionWindMaterials.TrimExcess();
            _meshIds.TrimExcess();
            _materialIds.TrimExcess();
            _specifications.TrimExcess();
            _reflectionViews.Clear();
            _reflectionViews.TrimExcess();
            _planes = Array.Empty<Plane>();
            _wind = null;
            _shader = null;
            _rejectedSet = null;
            _rejectedReason = null;
        }

        private void ClearStatistics()
        {
            LastCameraCommands = 0;
            LastCameraInstances = 0;
            LastCameraBatches = 0;
            LastShadowCommands = 0;
            LastShadowInstances = 0;
            LastCulledBatches = 0;
            LastCulledInstances = 0;
            ShadowViews = 0;
            ReflectionCommands = 0;
            ReflectionInstances = 0;
            ReflectionBatches = 0;
            ReflectionViews = 0;
            CullingInvocations = 0;
            VisitedNodes = 0;
            LastCullingOutputBytes = 0;
        }

        private void ReleaseGraphics()
        {
            Disable();
            _group?.Dispose();
            _group = null;
            _buffer?.Dispose();
            _buffer = null;
            _meshIds.Clear();
            _materialIds.Clear();
            _set = null;
            _sources = null;
            _draws = null;
            _instanceBounds = null;
            _windBounds = null;
            _windBoundsStamps = null;
            _sourceWind = null;
            _windBoundsStamp = 0;
            _windBoundsKey = default;
            _sourceBounds = null;
            _physicalSourceBounds = null;
            _globalBoundsDirty = true;
            _sourceSplitMasks = null;
            _sourceShadowCounts = null;
            _instanceMasks = null;
            _sourceStamps = null;
            _selectedSources = null;
            _plannedCommands = null;
            _visibleIndices = null;
            _payload = null;
            _instanceCount = 0;
            _index.Ensure(null);
        }

        private bool Unsupported(string reason)
        {
            _active = false;
            FallbackReason = reason;
            return false;
        }

        private bool RejectMatrices(XRayInstanceBatchSet set, string reason)
        {
            _rejectedSet = set;
            _rejectedRevision = set.MatrixRevision;
            _rejectedReason = reason;
            return Unsupported(reason);
        }

        private bool Build(XRayInstanceBatchSet set, int instanceCount)
        {
            ReleaseGraphics();
            var matrices = new PackedMatrix[instanceCount];
            var inverses = new PackedMatrix[instanceCount];
            _sources = new Source[set.Batches.Count];
            _sourceStamps = new int[set.Batches.Count];
            _selectedSources = new int[set.Batches.Count];
            _instanceBounds = new Bounds[instanceCount];
            _windBounds = new Bounds[instanceCount];
            _windBoundsStamps = new int[instanceCount];
            _sourceWind = new bool[set.Batches.Count];
            _sourceBounds = new Bounds[set.Batches.Count];
            _physicalSourceBounds = new Bounds[set.Batches.Count];
            _sourceSplitMasks = new ushort[set.Batches.Count];
            _sourceShadowCounts = new int[set.Batches.Count];
            _instanceMasks = new ushort[instanceCount];
            _payload = new Vector4[instanceCount];
            int start = 0;
            _maximumHeight = 0f;
            Vector3 globalMinimum = Vector3.zero;
            Vector3 globalMaximum = Vector3.zero;
            for (int sourceIndex = 0; sourceIndex < set.Batches.Count; sourceIndex++)
            {
                XRayInstanceBatch batch = set.Batches[sourceIndex];
                Bounds sourceBounds = batch.Bounds;
                Bounds physicalSourceBounds = batch.Bounds;
                Vector3 physicalMinimum = batch.Bounds.min;
                Vector3 physicalMaximum = batch.Bounds.max;
                MeshState mesh = _meshes[batch.Mesh];
                _sources[sourceIndex] = new Source { Batch = batch, Mesh = batch.Mesh, MeshBounds = mesh.Bounds, Submeshes = mesh.Submeshes, Start = start, Count = batch.Count, MatrixRevision = batch.MatrixRevision, Matrices = batch.RenderMatrices };
                for (int index = 0; index < batch.Count; index++)
                {
                    Matrix4x4 matrix = batch.GetMatrix(index);
                    float determinant = matrix.determinant;
                    if (!Finite(matrix) || !float.IsFinite(determinant) || determinant <= 0f || matrix.m30 != 0f || matrix.m31 != 0f || matrix.m32 != 0f || matrix.m33 != 1f) return RejectMatrices(set, "A static transform requires the original mirrored, non-affine or invalid-matrix submission path.");
                    Matrix4x4 inverse = matrix.inverse;
                    if (!Finite(inverse)) return RejectMatrices(set, "An instance inverse transform is non-finite.");
                    int destination = start + index;
                    matrices[destination] = new PackedMatrix(matrix);
                    inverses[destination] = new PackedMatrix(inverse);
                    _instanceBounds[destination] = TransformBounds(mesh.Bounds, matrix);
                    if (!Finite(_instanceBounds[destination])) return Unsupported("A transformed instance bound is non-finite.");
                    sourceBounds.Encapsulate(_instanceBounds[destination]);
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 local = mesh.Bounds.center + Vector3.Scale(mesh.Bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                        Vector3 point = matrix.MultiplyPoint3x4(local);
                        if (!Finite(point)) return Unsupported("A transformed mesh corner is non-finite.");
                        physicalMinimum = Vector3.Min(physicalMinimum, point);
                        physicalMaximum = Vector3.Max(physicalMaximum, point);
                    }
                    _payload[destination] = new Vector4(float.NaN, 0f, 0f, 0f);
                }
                if (!Finite(sourceBounds)) return Unsupported("A native source bound is non-finite.");
                if (!TryEnclosePhysicalBounds(ref physicalSourceBounds, physicalMinimum, physicalMaximum)) return Unsupported("A physical source bound cannot preserve its authored and transformed geometry coverage.");
                _sourceBounds[sourceIndex] = sourceBounds;
                _physicalSourceBounds[sourceIndex] = physicalSourceBounds;
                if (sourceIndex == 0)
                {
                    _globalBounds = physicalSourceBounds;
                    globalMinimum = physicalSourceBounds.min;
                    globalMaximum = physicalSourceBounds.max;
                }
                globalMinimum = Vector3.Min(globalMinimum, physicalSourceBounds.min);
                globalMaximum = Vector3.Max(globalMaximum, physicalSourceBounds.max);
                _maximumHeight = Mathf.Max(_maximumHeight, sourceBounds.size.y);
                start += batch.Count;
            }
            if (!TryEnclosePhysicalBounds(ref _globalBounds, globalMinimum, globalMaximum)) return Unsupported("A global physical bound cannot preserve every source bound.");
            _dataOffset = checked(MATRIX_START + instanceCount * PACKED_MATRIX_BYTES * 2);
            int bytes = checked(_dataOffset + instanceCount * VECTOR_BYTES);
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, bytes / sizeof(int), sizeof(int));
            _buffer.SetData(new Matrix4x4[1], 0, 0, 1);
            _buffer.SetData(matrices, 0, MATRIX_START / PACKED_MATRIX_BYTES, instanceCount);
            _buffer.SetData(inverses, 0, (MATRIX_START + instanceCount * PACKED_MATRIX_BYTES) / PACKED_MATRIX_BYTES, instanceCount);
            _group = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
            _group.SetEnabledViewTypes(DISABLED_VIEWS);
            var metadata = new NativeArray<MetadataValue>(6, Allocator.Temp);
            try
            {
                uint currentAddress = PER_INSTANCE | MATRIX_START;
                uint inverseAddress = PER_INSTANCE | (uint)(MATRIX_START + instanceCount * PACKED_MATRIX_BYTES);
                metadata[0] = new MetadataValue { NameID = Shader.PropertyToID("unity_ObjectToWorld"), Value = currentAddress };
                metadata[1] = new MetadataValue { NameID = Shader.PropertyToID("unity_WorldToObject"), Value = inverseAddress };
                metadata[2] = new MetadataValue { NameID = Shader.PropertyToID("unity_MatrixPreviousM"), Value = currentAddress };
                metadata[3] = new MetadataValue { NameID = Shader.PropertyToID("unity_MatrixPreviousMI"), Value = inverseAddress };
                metadata[4] = new MetadataValue { NameID = Shader.PropertyToID("_MonolithInstanceData"), Value = PER_INSTANCE | (uint)_dataOffset };
                metadata[5] = new MetadataValue { NameID = Shader.PropertyToID("_MonolithInstanceExtra"), Value = 0u };
                _batchId = _group.AddBatch(metadata, _buffer.bufferHandle);
            }
            finally
            {
                metadata.Dispose();
            }
            _draws = new Draw[_specifications.Count];
            _plannedCommands = new BatchDrawCommand[_draws.Length];
            _visibleIndices = new int[instanceCount];
            for (int index = 0; index < _specifications.Count; index++)
            {
                DrawSpec specification = _specifications[index];
                Mesh mesh = _sources[specification.SourceIndex].Mesh;
                BatchMeshID meshId;
                if (!_meshIds.TryGetValue(mesh, out meshId))
                {
                    meshId = _group.RegisterMesh(mesh);
                    _meshIds.Add(mesh, meshId);
                }
                BatchMaterialID materialId;
                if (!_materialIds.TryGetValue(specification.Material, out materialId))
                {
                    materialId = _group.RegisterMaterial(specification.Material);
                    _materialIds.Add(specification.Material, materialId);
                }
                _draws[index] = new Draw { SourceIndex = specification.SourceIndex, Material = specification.Material, Submesh = specification.Submesh, MeshId = meshId, MaterialId = materialId, Wind = specification.Wind };
                Source source = _sources[specification.SourceIndex];
                if (source.DrawCount == 0) source.DrawStart = index;
                source.DrawCount++;
                _sources[specification.SourceIndex] = source;
            }
            _set = set;
            _revision = set.MatrixRevision;
            _instanceCount = instanceCount;
            _rejectedSet = null;
            _rejectedReason = null;
            return true;
        }

        private unsafe JobHandle OnPerformCulling(BatchRendererGroup group, BatchCullingContext context, BatchCullingOutput output, IntPtr userContext)
        {
            using (CULLING_MARKER.Auto())
            {
                CullingInvocations++;
                *(BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr() = default;
                if (!_active) return default;
                if (context.viewType == BatchCullingViewType.Light) return CullShadows(context, output);
                if (context.viewType != BatchCullingViewType.Camera) return default;
                int reflectionIndex = -1;
                if (context.viewID.GetEntityId() != _cameraId)
                {
                    EntityId id = context.viewID.GetEntityId();
                    for (int index = 0; index < _reflectionViews.Count; index++)
                    {
                        if (_reflectionViews[index].Id != id) continue;
                        reflectionIndex = index;
                        break;
                    }
                    if (reflectionIndex < 0) return default;
                }
                bool reflection = reflectionIndex >= 0;
                if ((context.cullingLayerMask & (1u << _layer)) == 0u) return default;
                Array.Clear(_instanceMasks, 0, _instanceMasks.Length);
                if (_cullingStamp == int.MaxValue)
                {
                    Array.Clear(_sourceStamps, 0, _sourceStamps.Length);
                    _cullingStamp = 1;
                }
                else _cullingStamp++;
                _selectedCount = 0;
                Select(context.cullingPlanes, 0, context.cullingPlanes.Length, reflectionIndex);
                WriteCullingOutput(output, false, reflection, out int commands, out int submittedInstances, out int survivingInstances);
                if (reflection)
                {
                    ReflectionViews++;
                    ReflectionBatches += _selectedCount;
                    ReflectionCommands += commands;
                    ReflectionInstances += submittedInstances;
                }
                else
                {
                    LastCameraBatches = _selectedCount;
                    LastCulledBatches = _sources.Length - _selectedCount;
                    LastCulledInstances = _instanceCount - survivingInstances;
                    LastCameraCommands = commands;
                    LastCameraInstances = submittedInstances;
                }
                return default;
            }
        }

        private JobHandle CullShadows(BatchCullingContext context, BatchCullingOutput output)
        {
            Camera owner = SHADOW_CAMERA();
            if (owner == null || owner.GetEntityId() != _cameraId) return default;
            if ((context.cullingLayerMask & (1u << _layer)) == 0u) return default;
            ShadowViews++;
            using (SHADOW_SELECT_MARKER.Auto())
            {
                PrepareShadowSources(context);
            }
            int commands;
            int submittedInstances;
            using (SHADOW_OUTPUT_MARKER.Auto())
            {
                WriteCullingOutput(output, true, false, out commands, out submittedInstances, out _);
            }
            LastShadowCommands += commands;
            LastShadowInstances += submittedInstances;
            return default;
        }

        private unsafe void WriteCullingOutput(BatchCullingOutput output, bool shadows, bool reflection, out int commandCount, out int submittedInstances, out int survivingInstances)
        {
            commandCount = 0;
            submittedInstances = 0;
            survivingInstances = 0;
            int visibleCount = 0;
            int rangeCount = 0;
            for (int selected = 0; selected < _selectedCount; selected++)
            {
                int sourceIndex = _selectedSources[selected];
                Source source = _sources[sourceIndex];
                int first = visibleCount;
                int count = 0;
                int end = source.Start + source.Count;
                for (int index = source.Start; index < end; index++)
                {
                    if (_instanceMasks[index] != 0)
                    {
                        _visibleIndices[first + count++] = index;
                    }
                }
                survivingInstances += count;
                if (source.DrawCount == 0 || count == 0)
                {
                    continue;
                }
                visibleCount += count;
                submittedInstances = checked(submittedInstances + count * source.DrawCount);
                ushort splitMask = shadows ? _sourceSplitMasks[sourceIndex] : ushort.MaxValue;
                for (int drawIndex = source.DrawStart; drawIndex < source.DrawStart + source.DrawCount; drawIndex++)
                {
                    Draw draw = _draws[drawIndex];
                    BatchDrawCommandFlags flags = !shadows && !reflection && draw.Wind ? BatchDrawCommandFlags.HasMotion : BatchDrawCommandFlags.None;
                    if (commandCount > 0)
                    {
                        ref BatchDrawCommand previous = ref _plannedCommands[commandCount - 1];
                        if (previous.visibleOffset + previous.visibleCount == (uint)first && previous.meshID.value == draw.MeshId.value && previous.materialID.value == draw.MaterialId.value && previous.submeshIndex == draw.Submesh && previous.flags == flags && previous.splitVisibilityMask == splitMask)
                        {
                            previous.visibleCount += (uint)count;
                            continue;
                        }
                        if (previous.flags != flags)
                        {
                            rangeCount++;
                        }
                    }
                    else
                    {
                        rangeCount++;
                    }
                    _plannedCommands[commandCount++] = new BatchDrawCommand { batchID = _batchId, materialID = draw.MaterialId, meshID = draw.MeshId, submeshIndex = (ushort)draw.Submesh, visibleOffset = (uint)first, visibleCount = (uint)count, splitVisibilityMask = splitMask, flags = flags, sortingPosition = 0 };
                }
            }
            if (commandCount == 0)
            {
                return;
            }
            long commandBytes = (long)UnsafeUtility.SizeOf<BatchDrawCommand>() * commandCount;
            long indexBytes = (long)sizeof(int) * visibleCount;
            long rangeBytes = (long)UnsafeUtility.SizeOf<BatchDrawRange>() * rangeCount;
            var commands = (BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr();
            commands->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(commandBytes, UnsafeUtility.AlignOf<BatchDrawCommand>(), Allocator.TempJob);
            commands->visibleInstances = (int*)UnsafeUtility.Malloc(indexBytes, UnsafeUtility.AlignOf<int>(), Allocator.TempJob);
            commands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(rangeBytes, UnsafeUtility.AlignOf<BatchDrawRange>(), Allocator.TempJob);
            fixed (BatchDrawCommand* planned = _plannedCommands)
            {
                UnsafeUtility.MemCpy(commands->drawCommands, planned, commandBytes);
            }
            fixed (int* indices = _visibleIndices)
            {
                UnsafeUtility.MemCpy(commands->visibleInstances, indices, indexBytes);
            }
            var filter = new BatchFilterSettings { renderingLayerMask = _renderingLayerMask, layer = (byte)_layer, receiveShadows = true, shadowCastingMode = shadows ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off, staticShadowCaster = false, allDepthSorted = false, sceneCullingMask = ulong.MaxValue };
            int rangeStart = 0;
            int rangeIndex = 0;
            for (int index = 1; index <= commandCount; index++)
            {
                BatchDrawCommandFlags flags = _plannedCommands[rangeStart].flags;
                if (index < commandCount && _plannedCommands[index].flags == flags)
                {
                    continue;
                }
                filter.motionMode = reflection ? MotionVectorGenerationMode.ForceNoMotion : flags == BatchDrawCommandFlags.HasMotion ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera;
                commands->drawRanges[rangeIndex++] = new BatchDrawRange { drawCommandsBegin = (uint)rangeStart, drawCommandsCount = (uint)(index - rangeStart), drawCommandsType = BatchDrawCommandType.Direct, filterSettings = filter };
                rangeStart = index;
            }
            commands->drawCommandCount = commandCount;
            commands->visibleInstanceCount = visibleCount;
            commands->drawRangeCount = rangeCount;
            LastCullingOutputBytes += commandBytes + indexBytes + rangeBytes;
        }

        private void PrepareShadowSources(BatchCullingContext context)
        {
            _selectedCount = 0;
            Array.Clear(_sourceSplitMasks, 0, _sourceSplitMasks.Length);
            Array.Clear(_instanceMasks, 0, _instanceMasks.Length);
            bool valid = context.cullingSplits.IsCreated && context.cullingSplits.Length > 0 && context.cullingSplits.Length <= 16 && context.cullingPlanes.IsCreated && Finite(XRayFloraWindBounds.Expand(_globalBounds, _wind, _maximumHeight));
            for (int splitIndex = 0; valid && splitIndex < context.cullingSplits.Length; splitIndex++)
            {
                CullingSplit split = context.cullingSplits[splitIndex];
                valid = split.cullingPlaneOffset >= 0 && split.cullingPlaneCount > 0 && split.cullingPlaneOffset <= context.cullingPlanes.Length - split.cullingPlaneCount;
                for (int planeIndex = 0; valid && planeIndex < split.cullingPlaneCount; planeIndex++)
                {
                    Plane plane = context.cullingPlanes[split.cullingPlaneOffset + planeIndex];
                    float magnitude = plane.normal.sqrMagnitude;
                    valid = Finite(plane.normal) && float.IsFinite(plane.distance) && float.IsFinite(magnitude) && magnitude > 0f;
                }
            }
            bool useCombined = valid && context.cullingSplits.Length == POINT_SHADOW_SPLITS;
            if (useCombined)
            {
                SelectPointShadowSources(context);
            }
            else if (valid)
            {
                for (int splitIndex = 0; splitIndex < context.cullingSplits.Length; splitIndex++)
                {
                    CullingSplit split = context.cullingSplits[splitIndex];
                    if (_planes.Length < split.cullingPlaneCount) _planes = new Plane[Mathf.NextPowerOfTwo(split.cullingPlaneCount)];
                    for (int planeIndex = 0; planeIndex < split.cullingPlaneCount; planeIndex++) _planes[planeIndex] = context.cullingPlanes[split.cullingPlaneOffset + planeIndex];
                    _index.Select(_planes, split.cullingPlaneCount, _wind);
                    VisitedNodes += _index.VisitedNodes;
                    ushort mask = (ushort)(1 << splitIndex);
                    for (int candidate = 0; candidate < _index.SelectedCount; candidate++)
                    {
                        int sourceIndex = _index.SelectedSource(candidate);
                        Source source = _sources[sourceIndex];
                        if (!source.CastShadows) continue;
                        int count = _sourceSplitMasks[sourceIndex] != 0 ? _sourceShadowCounts[sourceIndex] : 0;
                        _sourceSplitMasks[sourceIndex] |= mask;
                        if (count == source.Count) continue;
                        if (source.Count == 1)
                        {
                            _instanceMasks[source.Start] = 1;
                            _sourceShadowCounts[sourceIndex] = 1;
                            continue;
                        }

                        for (int index = source.Start; index < source.Start + source.Count; index++)
                        {
                            if (_instanceMasks[index] != 0) continue;
                            Bounds bounds = _instanceBounds[index];
                            if (source.Wind) bounds = XRayFloraWindBounds.Expand(bounds, _wind, _sourceBounds[sourceIndex].size.y);
                            if (!Intersects(_planes, split.cullingPlaneCount, bounds)) continue;
                            _instanceMasks[index] = 1;
                            count++;
                        }
                        _sourceShadowCounts[sourceIndex] = count;
                    }
                }
            }
            for (int sourceIndex = 0; sourceIndex < _sources.Length; sourceIndex++)
            {
                Source source = _sources[sourceIndex];
                if (!source.CastShadows) continue;
                if (!valid)
                {
                    _sourceSplitMasks[sourceIndex] = ushort.MaxValue;
                    _sourceShadowCounts[sourceIndex] = source.Count;
                    for (int index = source.Start; index < source.Start + source.Count; index++) _instanceMasks[index] = 1;
                }
                if (_sourceSplitMasks[sourceIndex] == 0 || _sourceShadowCounts[sourceIndex] == 0) continue;
                _selectedSources[_selectedCount++] = sourceIndex;
            }
        }

        private void SelectPointShadowSources(BatchCullingContext context)
        {
            int length = context.cullingPlanes.Length;
            if (_planes.Length < length)
            {
                _planes = new Plane[Mathf.NextPowerOfTwo(length)];
            }
            NativeArray<Plane>.Copy(context.cullingPlanes, _planes, length);
            for (int splitIndex = 0; splitIndex < POINT_SHADOW_SPLITS; splitIndex++)
            {
                CullingSplit split = context.cullingSplits[splitIndex];
                _pointShadowStarts[splitIndex] = split.cullingPlaneOffset;
                _pointShadowCounts[splitIndex] = split.cullingPlaneCount;
            }

            _index.SelectShadowSplits(_planes, _pointShadowStarts, _pointShadowCounts, _wind, _sourceSplitMasks);
            VisitedNodes += _index.VisitedNodes;
            for (int candidate = 0; candidate < _index.SelectedCount; candidate++)
            {
                int sourceIndex = _index.SelectedSource(candidate);
                Source source = _sources[sourceIndex];
                if (!source.CastShadows)
                {
                    _sourceSplitMasks[sourceIndex] = 0;
                    continue;
                }
                if (source.Count == 1)
                {
                    _instanceMasks[source.Start] = 1;
                    _sourceShadowCounts[sourceIndex] = 1;
                    continue;
                }

                ushort mask = _sourceSplitMasks[sourceIndex];
                float height = source.Wind ? _sourceBounds[sourceIndex].size.y : 0f;
                int count = 0;
                int end = source.Start + source.Count;
                for (int index = source.Start; index < end; index++)
                {
                    Bounds bounds = _instanceBounds[index];
                    if (source.Wind)
                    {
                        bounds = XRayFloraWindBounds.Expand(bounds, _wind, height);
                    }
                    Vector3 center = bounds.center;
                    Vector3 extents = bounds.extents;
                    for (int split = 0; split < POINT_SHADOW_SPLITS; split++)
                    {
                        if ((mask & (1 << split)) == 0 || !_index.IntersectsPreparedShadowSplit(center, extents, _pointShadowStarts[split], _pointShadowCounts[split]))
                        {
                            continue;
                        }
                        _instanceMasks[index] = 1;
                        count++;
                        break;
                    }
                }
                _sourceShadowCounts[sourceIndex] = count;
            }
        }

        private Bounds CurrentGlobalBounds()
        {
            if (_wind == null) return _globalBounds;
            var key = new XRayFloraWindBounds.SnapshotKey(_wind);
            if (!_globalBoundsDirty && _globalBoundsKey.Equals(key)) return _cachedGlobalBounds;
            Bounds bounds = default;
            Vector3 minimum = Vector3.zero;
            Vector3 maximum = Vector3.zero;
            for (int sourceIndex = 0; sourceIndex < _sources.Length; sourceIndex++)
            {
                Source source = _sources[sourceIndex];
                Bounds sourceBounds = _physicalSourceBounds[sourceIndex];
                if (source.Wind || source.ReflectionWind) sourceBounds = XRayFloraWindBounds.Expand(sourceBounds, _wind, sourceBounds.size.y);
                if (sourceIndex == 0)
                {
                    bounds = sourceBounds;
                    minimum = sourceBounds.min;
                    maximum = sourceBounds.max;
                }
                minimum = Vector3.Min(minimum, sourceBounds.min);
                maximum = Vector3.Max(maximum, sourceBounds.max);
            }
            if (!TryEnclosePhysicalBounds(ref bounds, minimum, maximum)) return new Bounds(new Vector3(float.NaN, float.NaN, float.NaN), Vector3.zero);
            _globalBoundsKey = key;
            _cachedGlobalBounds = bounds;
            _globalBoundsDirty = false;
            return bounds;
        }

        private static bool TryEnclosePhysicalBounds(ref Bounds bounds, Vector3 minimum, Vector3 maximum)
        {
            if (!Finite(minimum) || !Finite(maximum)) return false;
            if (bounds.Contains(minimum) && bounds.Contains(maximum)) return Finite(bounds);
            bounds.SetMinMax(minimum, maximum);
            if (!Finite(bounds.center)) bounds.center = minimum * 0.5f + maximum * 0.5f;
            bounds.extents = Vector3.Max(bounds.center - minimum, maximum - bounds.center);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!Finite(bounds)) return false;
                Vector3 currentMinimum = bounds.min;
                Vector3 currentMaximum = bounds.max;
                bool growX = currentMinimum.x > minimum.x || currentMaximum.x < maximum.x;
                bool growY = currentMinimum.y > minimum.y || currentMaximum.y < maximum.y;
                bool growZ = currentMinimum.z > minimum.z || currentMaximum.z < maximum.z;
                if (!growX && !growY && !growZ) return true;
                Vector3 extents = bounds.extents;
                if (growX) extents.x = extents.x == 0f ? float.Epsilon : BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(extents.x) + 1);
                if (growY) extents.y = extents.y == 0f ? float.Epsilon : BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(extents.y) + 1);
                if (growZ) extents.z = extents.z == 0f ? float.Epsilon : BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(extents.z) + 1);
                bounds.extents = extents;
            }
            return Finite(bounds) && bounds.Contains(minimum) && bounds.Contains(maximum);
        }

        private static Func<Camera> CreateShadowCameraAccessor(Type scope)
        {
            if (scope == null) return null;
            PropertyInfo version = scope.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo current = scope.GetProperty("CurrentCamera", BindingFlags.Public | BindingFlags.Static);
            MethodInfo versionGetter = version?.GetGetMethod();
            MethodInfo currentGetter = current?.GetGetMethod();
            if (versionGetter == null || versionGetter.ReturnType != typeof(int) || versionGetter.GetParameters().Length != 0 || currentGetter == null || currentGetter.ReturnType != typeof(Camera) || currentGetter.GetParameters().Length != 0 || (int)version.GetValue(null) != 1) return null;
            return (Func<Camera>)Delegate.CreateDelegate(typeof(Func<Camera>), currentGetter);
        }

        private void Select(NativeArray<Plane> planes, int start, int count, int reflectionIndex)
        {
            var key = new XRayFloraWindBounds.SnapshotKey(_wind);
            if (_windBoundsStamp == 0 || !_windBoundsKey.Equals(key))
            {
                if (_windBoundsStamp == int.MaxValue)
                {
                    Array.Clear(_windBoundsStamps, 0, _windBoundsStamps.Length);
                    _windBoundsStamp = 1;
                }
                else _windBoundsStamp++;
                _windBoundsKey = key;
            }
            bool valid = start >= 0 && count >= 0 && start <= planes.Length - count;
            if (_planes.Length < count && valid) _planes = new Plane[Mathf.NextPowerOfTwo(count)];
            if (valid)
            {
                for (int index = 0; index < count; index++)
                {
                    Plane plane = planes[start + index];
                    _planes[index] = plane;
                    valid &= Finite(plane.normal) && float.IsFinite(plane.distance) && float.IsFinite(plane.normal.sqrMagnitude) && plane.normal.sqrMagnitude > 0f;
                }
                if (valid)
                {
                    _index.Select(_planes, count, _wind);
                    VisitedNodes += _index.VisitedNodes;
                }
            }
            int candidates = valid ? _index.SelectedCount : _sources.Length;
            for (int candidate = 0; candidate < candidates; candidate++)
            {
                int sourceIndex = valid ? _index.SelectedSource(candidate) : candidate;
                Source source = _sources[sourceIndex];
                if (reflectionIndex >= 0)
                {
                    ReflectionView view = _reflectionViews[reflectionIndex];
                    if (source.ReflectionBounds.SqrDistance(view.Position) > view.DistanceSquared) continue;
                }
                Bounds sourceBounds = _sourceBounds[sourceIndex];
                bool primary = reflectionIndex < 0;
                int[] lodIndices = primary && _cameraLodStates != null && source.Batch.HasLods ? source.Batch.LodIndices : null;
                for (int index = source.Start; index < source.Start + source.Count; index++)
                {
                    if (lodIndices != null)
                    {
                        int lod = lodIndices[index - source.Start];
                        if (lod >= 0 && lod < _cameraLodStates.Length && _cameraLodStates[lod] > XRayLevelLodRenderer.STATE_BOTH) continue;
                    }
                    Bounds bounds = _instanceBounds[index];
                    if (source.Wind)
                    {
                        if (_windBoundsStamps[index] != _windBoundsStamp)
                        {
                            _windBounds[index] = XRayFloraWindBounds.Expand(bounds, _wind, sourceBounds.size.y);
                            _windBoundsStamps[index] = _windBoundsStamp;
                        }
                        bounds = _windBounds[index];
                    }
                    if (valid && !Intersects(_planes, count, bounds)) continue;
                    if (primary && source.Flora && _cameraOcclusion != null && XRayVegetationOcclusion.IsSmallFlora(bounds) && _cameraOcclusion.IsOccluded(bounds)) continue;
                    _instanceMasks[index] = 1;
                    if (_sourceStamps[sourceIndex] == _cullingStamp) continue;
                    _sourceStamps[sourceIndex] = _cullingStamp;
                    _selectedSources[_selectedCount++] = sourceIndex;
                }
            }
        }

        private static bool Intersects(Plane[] planes, int count, Bounds bounds, int start = 0)
        {
            int end = start + count;
            for (int index = start; index < end; index++)
            {
                Plane plane = planes[index];
                Vector3 normal = plane.normal;
                if (!Finite(normal) || !float.IsFinite(plane.distance)) return true;
                float radius = Mathf.Abs(normal.x) * bounds.extents.x + Mathf.Abs(normal.y) * bounds.extents.y + Mathf.Abs(normal.z) * bounds.extents.z;
                float distance = Vector3.Dot(normal, bounds.center) + plane.distance + radius;
                if (!float.IsFinite(distance)) return true;
                if (distance < -0.00001f) return false;
            }
            return true;
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 x = matrix.MultiplyVector(new Vector3(bounds.extents.x, 0f, 0f));
            Vector3 y = matrix.MultiplyVector(new Vector3(0f, bounds.extents.y, 0f));
            Vector3 z = matrix.MultiplyVector(new Vector3(0f, 0f, bounds.extents.z));
            Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(bounds.center), extents * 2f);
            Vector3 magnitude = new Vector3(Mathf.Abs(result.center.x), Mathf.Abs(result.center.y), Mathf.Abs(result.center.z)) + result.extents;
            result.Expand(Vector3.Max(Vector3.one, magnitude) * 0.000002f);
            return result;
        }

        private static bool Finite(Bounds value)
        {
            return Finite(value.center) && Finite(value.extents) && value.extents.x >= 0f && value.extents.y >= 0f && value.extents.z >= 0f;
        }

        private static bool Finite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static bool Finite(Vector4 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);
        }

        private static bool Finite(Matrix4x4 value)
        {
            for (int index = 0; index < 16; index++) if (!float.IsFinite(value[index])) return false;
            return true;
        }

        private struct MeshState
        {
            public Bounds Bounds { get; set; }
            public int Submeshes { get; set; }
        }

        private struct MaterialState
        {
            public bool Supported { get; set; }
            public bool Wind { get; set; }
            public bool Flora { get; set; }
        }

        private struct Source
        {
            public XRayInstanceBatch Batch { get; set; }
            public Mesh Mesh { get; set; }
            public Bounds MeshBounds { get; set; }
            public Bounds ReflectionBounds { get; set; }
            public Matrix4x4[] Matrices { get; set; }
            public int Submeshes { get; set; }
            public int Start { get; set; }
            public int Count { get; set; }
            public int MatrixRevision { get; set; }
            public bool Wind { get; set; }
            public bool ReflectionWind { get; set; }
            public bool Flora { get; set; }
            public bool CastShadows { get; set; }
            public int DrawStart { get; set; }
            public int DrawCount { get; set; }
        }

        private struct ReflectionView
        {
            public EntityId Id { get; set; }
            public Vector3 Position { get; set; }
            public float DistanceSquared { get; set; }
        }

        private struct DrawSpec
        {
            public int SourceIndex { get; set; }
            public Material Material { get; set; }
            public int Submesh { get; set; }
            public bool Wind { get; set; }
        }

        private struct Draw
        {
            public int SourceIndex { get; set; }
            public Material Material { get; set; }
            public int Submesh { get; set; }
            public BatchMeshID MeshId { get; set; }
            public BatchMaterialID MaterialId { get; set; }
            public bool Wind { get; set; }
        }

        private struct PackedMatrix
        {
            private float _m00;
            private float _m10;
            private float _m20;
            private float _m01;
            private float _m11;
            private float _m21;
            private float _m02;
            private float _m12;
            private float _m22;
            private float _m03;
            private float _m13;
            private float _m23;

            public PackedMatrix(Matrix4x4 matrix)
            {
                _m00 = matrix.m00;
                _m10 = matrix.m10;
                _m20 = matrix.m20;
                _m01 = matrix.m01;
                _m11 = matrix.m11;
                _m21 = matrix.m21;
                _m02 = matrix.m02;
                _m12 = matrix.m12;
                _m22 = matrix.m22;
                _m03 = matrix.m03;
                _m13 = matrix.m13;
                _m23 = matrix.m23;
            }
        }
    }
}
