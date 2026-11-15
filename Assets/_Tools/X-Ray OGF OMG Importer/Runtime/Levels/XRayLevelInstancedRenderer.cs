using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    public sealed class XRayLevelInstancedRenderer : MonoBehaviour
    {
        public const string INSTANCE_DATA_PROPERTY = "_MonolithInstanceData";

        private const int MAX_MOTION_INSTANCES = XRayInstanceBatchSet.MAX_GROUP_INSTANCES;

        [SerializeField] private XRayInstanceBatchSet _batchSet;
        [SerializeField] private float _maxDistance;
        [SerializeField] private bool _castShadows = true;
        [SerializeField] private bool _lodImpostors;

        private readonly Plane[] _planes = new Plane[6];
        private readonly List<MaterialPropertyBlock> _properties = new List<MaterialPropertyBlock>();
        private readonly List<LodScratch> _lodScratch = new List<LodScratch>();
        private readonly List<InstanceHistory> _histories = new List<InstanceHistory>();
        private readonly Vector4[] _submissionData = new Vector4[MAX_MOTION_INSTANCES];
        private readonly Dictionary<Mesh, int> _meshSubmeshes = new Dictionary<Mesh, int>(new ReferenceComparer<Mesh>());
        private readonly Dictionary<Material, bool> _materialSupport = new Dictionary<Material, bool>(new ReferenceComparer<Material>());
        private readonly Dictionary<Material, bool> _windMaterials = new Dictionary<Material, bool>(new ReferenceComparer<Material>());
        private readonly Dictionary<Material, Material> _resolvedMaterials = new Dictionary<Material, Material>(new ReferenceComparer<Material>());
        private MaterialPropertyBlock _submissionBlock;
        private XRayLevelLodRenderer _lodRenderer;
        private int _propertyId;
        private XRayInstanceBatchSet _groupOwner;
        private int _groupRevision = -1;
        private int[] _groupByBatch;
        private GroupScratch[] _groups;
        private XRayInstanceBatchSet _propertiesOwner;
        private int _propertiesMatrixRevision = -1;
        private int _propertiesGroupingRevision = -1;
        private Bounds[] _sourceBounds;
        private byte[] _sourceVisibility;
        private Func<Material, Material> _materialResolver;
        private bool _orderIndependentResolver;

        public XRayInstanceBatchSet BatchSet
        {
            get => _batchSet;
            set
            {
                _batchSet = value;
                _properties.Clear();
                _lodScratch.Clear();
                _histories.Clear();
                _groups = null;
                _groupByBatch = null;
                _groupOwner = null;
                ReleaseCallbackCaches();
            }
        }

        public bool LodImpostors
        {
            get => _lodImpostors;
            set => _lodImpostors = value;
        }

        public Func<Material, Material> MaterialResolver
        {
            get => _materialResolver;
            set
            {
                _materialResolver = value;
                _orderIndependentResolver = false;
            }
        }
        public int LastVisibleBatches { get; private set; }
        public int LastVisibleInstances { get; private set; }
        public int LastShadowOnlyBatches { get; private set; }
        public int LastRejectedShadowBatches { get; private set; }
        public int LastSubmittedCalls { get; private set; }
        public int LastSubmittedInstances { get; private set; }
        public int LastImpostors { get; private set; }
        public int LastLodShadowOnlyInstances { get; private set; }
        public int LastLodDiscardedInstances { get; private set; }
        public long ReflectionSubmittedBatches { get; private set; }
        public long ReflectionSubmittedInstances { get; private set; }
        public int LodMeshCount => _lodRenderer != null ? _lodRenderer.MeshCount : 0;
        public XRayShadowInfluence ExperimentalShadowInfluence { get; set; }

        private void OnEnable()
        {
            _propertyId = Shader.PropertyToID(INSTANCE_DATA_PROPERTY);
            _submissionBlock = new MaterialPropertyBlock();
            _properties.Clear();
            _histories.Clear();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            _groupOwner = null;
            ReleaseCallbackCaches();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _properties.Clear();
            _lodScratch.Clear();
            _histories.Clear();
            _lodRenderer?.Dispose();
            _lodRenderer = null;
            _submissionBlock = null;
            _groups = null;
            _groupByBatch = null;
            _groupOwner = null;
            ReleaseCallbackCaches();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_batchSet == null || camera == null || camera.cameraType == CameraType.Preview)
            {
                return;
            }
            _meshSubmeshes.Clear();
            _materialSupport.Clear();
            _windMaterials.Clear();
            _resolvedMaterials.Clear();
            if (camera.cameraType == CameraType.Reflection)
            {
                RenderReflection(camera);
                return;
            }
            EnsureProperties();
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            ExperimentalShadowInfluence?.Prepare(camera);
            Vector3 eye = camera.transform.position;
            float maxDistanceSqr = _maxDistance > 0.0f ? _maxDistance * _maxDistance : float.MaxValue;
            int visibleBatches = 0;
            int visibleInstances = 0;
            int shadowOnlyBatches = 0;
            int rejectedShadowBatches = 0;
            int lodShadowOnly = 0;
            int lodDiscarded = 0;
            XRayLevelLodSet lods = _lodImpostors ? _batchSet.Lods : null;
            byte[] states = null;
            if (lods != null && lods.Count > 0)
            {
                _lodRenderer ??= new XRayLevelLodRenderer();
                _lodRenderer.Evaluate(camera, lods);
                states = _lodRenderer.States;
            }
            else if (_lodRenderer != null)
            {
                _lodRenderer.Dispose();
                _lodRenderer = null;
            }

            IReadOnlyList<XRayInstanceBatch> batches = _batchSet.Batches;
            XRayFloraWindState wind = XRayFloraWindBounds.ActiveState();
            LastSubmittedCalls = 0;
            LastSubmittedInstances = 0;
            bool grouped = PrepareGroups(camera, states);
            for (int i = 0; i < batches.Count; i++)
            {
                if (grouped && _groupByBatch[i] >= 0 && _groups[_groupByBatch[i]].Active)
                {
                    GroupScratch group = _groups[_groupByBatch[i]];
                    if (group.LeaderIndex != i || RenderGroup(camera, group, wind, ref visibleBatches, ref visibleInstances, ref shadowOnlyBatches)) continue;
                }
                XRayInstanceBatch batch = batches[i];
                InstanceHistory history = History(i, batch);
                if (MeshSubmeshes(batch.Mesh) < 0 || batch.Count == 0)
                {
                    continue;
                }

                Bounds bounds;
                bool visible;
                if (grouped) visible = SourceVisible(i, batch, wind, out bounds);
                else
                {
                    bounds = UsesWind(batch.Materials, wind) ? XRayFloraWindBounds.Expand(batch.Bounds, wind) : batch.Bounds;
                    visible = false;
                }
                if (bounds.SqrDistance(eye) > maxDistanceSqr)
                {
                    continue;
                }
                if (!grouped) visible = GeometryUtility.TestPlanesAABB(_planes, bounds);

                bool castShadows = _castShadows && batch.CastShadows;
                if (!visible && !castShadows)
                {
                    continue;
                }

                if (!visible && ExperimentalShadowInfluence != null && !ExperimentalShadowInfluence.Intersects(bounds))
                {
                    rejectedShadowBatches++;
                    continue;
                }

                if (visible)
                {
                    visibleBatches++;
                    visibleInstances += batch.Count;
                }
                else
                {
                    shadowOnlyBatches++;
                }

                if (visible && states != null && batch.HasLods && RenderLodBatch(camera, i, batch, history, bounds, wind, states, castShadows, ref lodShadowOnly, ref lodDiscarded))
                {
                    continue;
                }

                Submit(camera, batch, bounds, wind, _properties[i], history.Instances, batch.Data, batch.Count, castShadows ? visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off, history.Moving);
            }

            if (states != null)
            {
                _lodRenderer.Render(camera, lods, gameObject.layer);
            }

            LastImpostors = states != null ? _lodRenderer.LastImpostors : 0;
            LastLodShadowOnlyInstances = lodShadowOnly;
            LastLodDiscardedInstances = lodDiscarded;
            LastVisibleBatches = visibleBatches;
            LastVisibleInstances = visibleInstances;
            LastShadowOnlyBatches = shadowOnlyBatches;
            LastRejectedShadowBatches = rejectedShadowBatches;
        }

        private void RenderReflection(Camera camera)
        {
            EnsureProperties();
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            float distance = _maxDistance > 0.0f ? Mathf.Min(_maxDistance, camera.farClipPlane) : camera.farClipPlane;
            float distanceSquared = distance * distance;
            XRayFloraWindState wind = XRayFloraWindBounds.ActiveState();
            IReadOnlyList<XRayInstanceBatch> batches = _batchSet.Batches;
            for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                XRayInstanceBatch batch = batches[batchIndex];
                if (batch.Mesh == null || batch.Count == 0) continue;
                Bounds bounds = XRayFloraWindBounds.Uses(batch.Materials, wind) ? XRayFloraWindBounds.Expand(batch.Bounds, wind) : batch.Bounds;
                if (bounds.SqrDistance(camera.transform.position) > distanceSquared || !GeometryUtility.TestPlanesAABB(_planes, bounds)) continue;
                bool submitted = false;
                int submeshes = Mathf.Min(batch.Mesh.subMeshCount, batch.Materials.Length);
                for (int submesh = 0; submesh < submeshes; submesh++)
                {
                    Material source = batch.Materials[submesh];
                    Material material;
                material = source != null && MaterialResolver != null ? MaterialResolver(source) : source;
                    if (material == null) continue;
                    for (int start = 0; start < batch.Count; start += MAX_MOTION_INSTANCES)
                    {
                        int count = Mathf.Min(MAX_MOTION_INSTANCES, batch.Count - start);
                        MaterialPropertyBlock properties = _properties[batchIndex];
                        if (batch.Count > MAX_MOTION_INSTANCES)
                        {
                            for (int instance = 0; instance < count; instance++) _submissionData[instance] = batch.Data != null && start + instance < batch.Data.Length ? batch.Data[start + instance] : new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                            _submissionBlock.SetVectorArray(_propertyId, _submissionData);
                            properties = _submissionBlock;
                        }
                        var parameters = new RenderParams(material) { camera = camera, worldBounds = bounds, motionVectorMode = MotionVectorGenerationMode.ForceNoMotion, layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, matProps = properties };
                        Graphics.RenderMeshInstanced(parameters, batch.Mesh, submesh, batch.RenderMatrices, count, start);
                        submitted = true;
                    }
                }
                if (!submitted) continue;
                ReflectionSubmittedBatches++;
                ReflectionSubmittedInstances += batch.Count;
            }
        }

        private bool RenderLodBatch(Camera camera, int index, XRayInstanceBatch batch, InstanceHistory history, Bounds bounds, XRayFloraWindState wind, byte[] states, bool castShadows, ref int lodShadowOnly, ref int lodDiscarded)
        {
            int full = 0;
            for (int instance = 0; instance < batch.Count; instance++)
            {
                int lod = batch.LodIndices[instance];
                byte state = lod >= 0 && lod < states.Length ? states[lod] : XRayLevelLodRenderer.STATE_FULL;
                if (state <= XRayLevelLodRenderer.STATE_BOTH)
                {
                    full++;
                }
                else if (state == XRayLevelLodRenderer.STATE_DISCARD)
                {
                    lodDiscarded++;
                }
            }

            if (full == batch.Count)
            {
                return false;
            }

            lodShadowOnly += batch.Count - full;
            if (full == 0)
            {
                if (castShadows)
                {
                    Submit(camera, batch, bounds, wind, _properties[index], history.Instances, batch.Data, batch.Count, ShadowCastingMode.ShadowsOnly, history.Moving);
                }

                return true;
            }

            while (_lodScratch.Count <= index)
            {
                _lodScratch.Add(null);
            }

            LodScratch scratch = _lodScratch[index];
            if (scratch == null || scratch.Capacity != batch.Count)
            {
                scratch = new LodScratch(batch.Count);
                _lodScratch[index] = scratch;
            }

            int colorCount = 0;
            int shadowCount = 0;
            for (int instance = 0; instance < batch.Count; instance++)
            {
                int lod = batch.LodIndices[instance];
                byte state = lod >= 0 && lod < states.Length ? states[lod] : XRayLevelLodRenderer.STATE_FULL;
                Vector4 data = batch.Data != null && instance < batch.Data.Length ? batch.Data[instance] : new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                if (state > XRayLevelLodRenderer.STATE_BOTH)
                {
                    scratch.ShadowMatrices[shadowCount] = history.Instances[instance];
                    scratch.ShadowData[shadowCount++] = data;
                    continue;
                }

                scratch.ColorMatrices[colorCount] = history.Instances[instance];
                scratch.ColorData[colorCount++] = data;
            }

            scratch.ColorBlock.SetVectorArray(_propertyId, scratch.ColorData);
            Submit(camera, batch, bounds, wind, scratch.ColorBlock, scratch.ColorMatrices, scratch.ColorData, colorCount, castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off, history.Moving);
            if (castShadows)
            {
                scratch.ShadowBlock.SetVectorArray(_propertyId, scratch.ShadowData);
                Submit(camera, batch, bounds, wind, scratch.ShadowBlock, scratch.ShadowMatrices, scratch.ShadowData, shadowCount, ShadowCastingMode.ShadowsOnly, history.Moving);
            }

            return true;
        }

        private void Submit(Camera camera, XRayInstanceBatch batch, Bounds bounds, XRayFloraWindState wind, MaterialPropertyBlock block, InstanceData[] matrices, Vector4[] data, int count, ShadowCastingMode mode, bool moving, Material[] resolvedMaterials = null)
        {
            int submeshes = Mathf.Min(MeshSubmeshes(batch.Mesh), batch.Materials.Length);
            for (int submesh = 0; submesh < submeshes; submesh++)
            {
                Material source = batch.Materials[submesh];
                Material material;
                material = resolvedMaterials != null ? resolvedMaterials[submesh] : _orderIndependentResolver ? ResolveGroupingMaterial(source) : source != null && MaterialResolver != null ? MaterialResolver(source) : source;
                if (material == null)
                {
                    continue;
                }

                for (int start = 0; start < count; start += MAX_MOTION_INSTANCES)
                {
                    int length = Mathf.Min(MAX_MOTION_INSTANCES, count - start);
                    MaterialPropertyBlock properties = block;
                    if (count > MAX_MOTION_INSTANCES)
                    {
                        for (int i = 0; i < length; i++) _submissionData[i] = data != null && start + i < data.Length ? data[start + i] : new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                        _submissionBlock.SetVectorArray(_propertyId, _submissionData);
                        properties = _submissionBlock;
                    }
                    RenderParams parameters;
                    parameters = new RenderParams(material) { camera = camera, worldBounds = bounds, motionVectorMode = moving || UsesWind(material, wind) ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera, layer = gameObject.layer, shadowCastingMode = mode, receiveShadows = true, matProps = properties };
                    Graphics.RenderMeshInstanced(parameters, batch.Mesh, submesh, matrices, length, start);
                    LastSubmittedCalls++;
                    LastSubmittedInstances += length;
                }
            }
        }

        public void ResetMotionHistory()
        {
            _histories.Clear();
            _groupOwner = null;
            _groups = null;
            _groupByBatch = null;
            ReleaseCallbackCaches();
        }

        internal void SetOrderIndependentMaterialResolver(Func<Material, Material> resolver)
        {
            _materialResolver = resolver;
            _orderIndependentResolver = resolver != null;
        }

        private bool PrepareGroups(Camera camera, byte[] states)
        {
            if (MaterialResolver != null && !_orderIndependentResolver) return false;
            if (_groupOwner != null && (_groupOwner != _batchSet || _groupRevision != _batchSet.GroupingRevision))
            {
                _groupOwner = null;
                _groups = null;
                _groupByBatch = null;
            }
            if (_batchSet.GroupingVersion != XRayInstanceBatchSet.GROUPING_VERSION || !_batchSet.HasPreparedMatrices || _batchSet.SubmissionGroups == null || _batchSet.SubmissionGroups.Count == 0 || _maxDistance > 0f || _lodImpostors || states != null || ExperimentalShadowInfluence != null || camera.stereoEnabled || camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return false;
            if (_groupOwner != _batchSet || _groupRevision != _batchSet.GroupingRevision)
            {
                _groupOwner = _batchSet;
                _groupRevision = _batchSet.GroupingRevision;
                _groupByBatch = new int[_batchSet.Batches.Count];
                _sourceBounds = new Bounds[_batchSet.Batches.Count];
                _sourceVisibility = new byte[_batchSet.Batches.Count];
                Array.Fill(_groupByBatch, -1);
                _groups = new GroupScratch[_batchSet.SubmissionGroups.Count];
                for (int index = 0; index < _groups.Length; index++)
                {
                    var plan = _batchSet.SubmissionGroups[index];
                    _groups[index] = new GroupScratch(plan, _batchSet.Batches);
                    for (int member = 0; member < plan.BatchIndices.Count; member++) _groupByBatch[plan.BatchIndices[member]] = index;
                }
            }
            Array.Clear(_sourceVisibility, 0, _sourceVisibility.Length);
            foreach (GroupScratch group in _groups)
            {
                XRayInstanceBatch first = group.Sources[0];
                group.Active = group.Compatible && MeshSubmeshes(first.Mesh) >= 0;
                if (!group.Active) continue;
                for (int slot = 0; slot < first.Materials.Length; slot++)
                {
                    group.Resolved[slot] = ResolveGroupingMaterial(first.Materials[slot]);
                    if (!SupportsMaterial(group.Resolved[slot])) group.Active = false;
                }
                if (!group.Active) continue;
                for (int member = 0; member < group.Sources.Length; member++)
                {
                    XRayInstanceBatch batch = group.Sources[member];
                    if (batch.MatricesExposed || batch.Count != group.Counts[member])
                    {
                        group.Active = false;
                        break;
                    }
                    for (int material = 0; member > 0 && material < batch.Materials.Length; material++)
                    {
                        if (!ReferenceEquals(batch.Materials[material], first.Materials[material])) group.Active = false;
                    }
                    if (group.OrientationRevisions[member] != batch.MatrixRevision)
                    {
                        group.Orientations[member] = batch.GetMatrixOrientation();
                        group.OrientationRevisions[member] = batch.MatrixRevision;
                    }
                    if (group.Orientations[member] == 0 || group.Orientations[member] != group.Orientations[0]) group.Active = false;
                }
            }
            return true;
        }

        private bool RenderGroup(Camera camera, GroupScratch group, XRayFloraWindState wind, ref int visibleBatches, ref int visibleInstances, ref int shadowOnlyBatches)
        {
            XRayInstanceBatch first = group.Sources[0];
            bool usesWind = UsesWind(first.Materials, wind);
            Bounds bounds = usesWind ? XRayFloraWindBounds.Expand(group.Plan.Bounds, wind, group.MaximumSourceHeight) : group.Plan.Bounds;
            bool visible = false;
            for (int member = 0; member < group.Sources.Length; member++)
            {
                bool sourceVisible = SourceVisible(group.Plan.BatchIndices[member], group.Sources[member], wind, out _);
                if (member == 0) visible = sourceVisible;
                else if (sourceVisible != visible)
                {
                    group.Active = false;
                    return false;
                }
            }
            bool shadows = _castShadows && first.CastShadows;
            bool refresh = !group.Ready || group.Moving;
            group.Moving = false;
            int frame = Time.frameCount;
            int revision = _batchSet.MatrixRevision;
            for (int member = 0; member < group.Sources.Length; member++)
            {
                int index = group.Plan.BatchIndices[member];
                InstanceHistory history = group.Histories[member];
                if (history != null && !history.Moving && history.Revision == revision && history.BatchRevision == group.Sources[member].MatrixRevision) history.Frame = frame;
                else history = History(index, group.Sources[member]);
                group.Histories[member] = history;
                refresh |= group.Revisions[member] != group.Sources[member].MatrixRevision || history.Moving;
                group.Moving |= history.Moving;
            }
            if (refresh)
            {
                int offset = 0;
                var payload = !group.Ready ? new List<Vector4>() : null;
                for (int member = 0; member < group.Sources.Length; member++)
                {
                    int count = group.Sources[member].Count;
                    Array.Copy(group.Histories[member].Instances, 0, group.Instances, offset, count);
                    if (!group.Ready)
                    {
                        _properties[group.Plan.BatchIndices[member]].GetVectorArray(_propertyId, payload);
                        for (int item = 0; item < count; item++) group.Data[offset + item] = payload[item];
                    }
                    group.Revisions[member] = group.Sources[member].MatrixRevision;
                    offset += count;
                }
                if (!group.Ready) group.Properties.SetVectorArray(_propertyId, group.Data);
                group.Ready = true;
            }
            if (!visible && !shadows) return true;
            if (visible)
            {
                visibleBatches++;
                visibleInstances += group.Instances.Length;
            }
            else shadowOnlyBatches++;
            Submit(camera, first, bounds, wind, group.Properties, group.Instances, group.Data, group.Instances.Length, shadows ? visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off, group.Moving, group.Resolved);
            return true;
        }

        private bool SourceVisible(int index, XRayInstanceBatch batch, XRayFloraWindState wind, out Bounds bounds)
        {
            if (_sourceVisibility[index] == 0)
            {
                bounds = UsesWind(batch.Materials, wind) ? XRayFloraWindBounds.Expand(batch.Bounds, wind) : batch.Bounds;
                _sourceBounds[index] = bounds;
                _sourceVisibility[index] = GeometryUtility.TestPlanesAABB(_planes, bounds) ? (byte)2 : (byte)1;
            }
            bounds = _sourceBounds[index];
            return _sourceVisibility[index] == 2;
        }

        private int MeshSubmeshes(Mesh mesh)
        {
            if (ReferenceEquals(mesh, null)) return -1;
            if (MaterialResolver != null && !_orderIndependentResolver) return mesh != null ? mesh.subMeshCount : -1;
            if (_meshSubmeshes.TryGetValue(mesh, out int count)) return count;
            count = mesh != null ? mesh.subMeshCount : -1;
            _meshSubmeshes.Add(mesh, count);
            return count;
        }

        private bool SupportsMaterial(Material material)
        {
            if (ReferenceEquals(material, null)) return true;
            if (_materialSupport.TryGetValue(material, out bool supported)) return supported;
            supported = XRayInstanceBatchSet.SupportsSubmissionMaterial(material);
            _materialSupport.Add(material, supported);
            return supported;
        }

        private bool UsesWind(Material[] materials, XRayFloraWindState wind)
        {
            if (wind == null || materials == null) return false;
            foreach (Material material in materials)
            {
                if (UsesWind(material, wind)) return true;
            }
            return false;
        }

        private bool UsesWind(Material material, XRayFloraWindState wind)
        {
            if (wind == null || ReferenceEquals(material, null)) return false;
            if (MaterialResolver != null && !_orderIndependentResolver) return XRayFloraWindBounds.Uses(material, wind);
            if (_windMaterials.TryGetValue(material, out bool uses)) return uses;
            uses = XRayFloraWindBounds.Uses(material, wind);
            _windMaterials.Add(material, uses);
            return uses;
        }

        private void ReleaseCallbackCaches()
        {
            _sourceBounds = null;
            _sourceVisibility = null;
            _meshSubmeshes.Clear();
            _meshSubmeshes.TrimExcess();
            _materialSupport.Clear();
            _materialSupport.TrimExcess();
            _windMaterials.Clear();
            _windMaterials.TrimExcess();
            _resolvedMaterials.Clear();
            _resolvedMaterials.TrimExcess();
        }

        private Material ResolveGroupingMaterial(Material source)
        {
            if (!_orderIndependentResolver || source == null) return source;
            if (_resolvedMaterials.TryGetValue(source, out Material resolved)) return resolved;
            resolved = _materialResolver(source);
            _resolvedMaterials.Add(source, resolved);
            return resolved;
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public bool Equals(T first, T second)
            {
                return ReferenceEquals(first, second);
            }

            public int GetHashCode(T value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }

        private sealed class GroupScratch
        {
            public XRayInstanceBatchGroup Plan { get; }
            public int LeaderIndex { get; }
            public XRayInstanceBatch[] Sources { get; }
            public int[] Counts { get; }
            public int[] Revisions { get; }
            public int[] Orientations { get; }
            public int[] OrientationRevisions { get; }
            public InstanceHistory[] Histories { get; }
            public InstanceData[] Instances { get; }
            public Vector4[] Data { get; }
            public bool Compatible { get; }
            public Material[] Resolved { get; }
            public MaterialPropertyBlock Properties { get; } = new MaterialPropertyBlock();
            public float MaximumSourceHeight { get; }
            public bool Ready { get; set; }
            public bool Moving { get; set; }
            public bool Active { get; set; }

            public GroupScratch(XRayInstanceBatchGroup plan, IReadOnlyList<XRayInstanceBatch> batches)
            {
                Plan = plan;
                Sources = new XRayInstanceBatch[plan.BatchIndices.Count];
                Counts = new int[Sources.Length];
                Revisions = new int[Sources.Length];
                Orientations = new int[Sources.Length];
                OrientationRevisions = new int[Sources.Length];
                Histories = new InstanceHistory[Sources.Length];
                XRayInstanceBatch first = batches[plan.BatchIndices[0]];
                Compatible = first.Materials != null;
                Resolved = new Material[first.Materials != null ? first.Materials.Length : 0];
                int count = 0;
                LeaderIndex = int.MaxValue;
                for (int member = 0; member < Sources.Length; member++)
                {
                    LeaderIndex = Mathf.Min(LeaderIndex, plan.BatchIndices[member]);
                    XRayInstanceBatch batch = batches[plan.BatchIndices[member]];
                    Sources[member] = batch;
                    Counts[member] = batch.Count;
                    Orientations[member] = batch.GetMatrixOrientation();
                    OrientationRevisions[member] = batch.MatrixRevision;
                    Compatible &= ReferenceEquals(batch.Mesh, first.Mesh) && batch.CastShadows == first.CastShadows && batch.Materials != null && first.Materials != null && batch.Materials.Length == first.Materials.Length;
                    count += batch.Count;
                    MaximumSourceHeight = Mathf.Max(MaximumSourceHeight, batch.Bounds.size.y);
                }
                Instances = new InstanceData[count];
                Data = new Vector4[count];
            }
        }

        private InstanceHistory History(int index, XRayInstanceBatch batch)
        {
            while (_histories.Count <= index) _histories.Add(null);
            InstanceHistory history = _histories[index];
            int revision = _batchSet != null ? _batchSet.MatrixRevision : 0;
            if (history == null || !ReferenceEquals(history.Source, batch) || history.Instances.Length != batch.Count || history.Revision != revision)
            {
                history = new InstanceHistory(batch, revision);
                _histories[index] = history;
            }
            if (_batchSet != null && _batchSet.HasPreparedMatrices && !batch.MatricesExposed && history.BatchRevision == batch.MatrixRevision && !history.Moving)
            {
                history.Frame = Time.frameCount;
                history.Moving = false;
                return history;
            }
            bool advance = history.Frame != Time.frameCount;
            history.Moving = false;
            for (int i = 0; i < batch.Count; i++)
            {
                ref InstanceData instance = ref history.Instances[i];
                if (advance) instance.prevObjectToWorld = instance.objectToWorld;
                instance.objectToWorld = batch.RenderMatrices[i];
                history.Moving |= !Same(in instance.objectToWorld, in instance.prevObjectToWorld);
            }
            history.Frame = Time.frameCount;
            history.BatchRevision = batch.MatrixRevision;
            return history;
        }

        private static bool Same(in Matrix4x4 current, in Matrix4x4 previous)
        {
            return current.m00 == previous.m00 && current.m10 == previous.m10 && current.m20 == previous.m20 && current.m30 == previous.m30 && current.m01 == previous.m01 && current.m11 == previous.m11 && current.m21 == previous.m21 && current.m31 == previous.m31 && current.m02 == previous.m02 && current.m12 == previous.m12 && current.m22 == previous.m22 && current.m32 == previous.m32 && current.m03 == previous.m03 && current.m13 == previous.m13 && current.m23 == previous.m23 && current.m33 == previous.m33;
        }

        private struct InstanceData
        {
            public Matrix4x4 objectToWorld;
            public Matrix4x4 prevObjectToWorld;

            public InstanceData(Matrix4x4 current, Matrix4x4 previous)
            {
                objectToWorld = current;
                prevObjectToWorld = previous;
            }
        }

        private sealed class InstanceHistory
        {
            public InstanceHistory(XRayInstanceBatch source, int revision)
            {
                Source = source;
                Revision = revision;
                BatchRevision = source.MatrixRevision;
                Instances = new InstanceData[source.Count];
                for (int i = 0; i < source.Count; i++) Instances[i] = new InstanceData(source.GetMatrix(i), source.GetMatrix(i));
            }

            public XRayInstanceBatch Source { get; }
            public int Revision { get; }
            public InstanceData[] Instances { get; }
            public int Frame { get; set; } = -1;
            public int BatchRevision { get; set; }
            public bool Moving { get; set; }
        }

        private void EnsureProperties()
        {
            IReadOnlyList<XRayInstanceBatch> batches = _batchSet.Batches;
            if (_properties.Count == batches.Count && _propertiesOwner == _batchSet && _propertiesMatrixRevision == _batchSet.MatrixRevision && _propertiesGroupingRevision == _batchSet.GroupingRevision)
            {
                return;
            }

            _properties.Clear();
            _propertiesOwner = _batchSet;
            _propertiesMatrixRevision = _batchSet.MatrixRevision;
            _propertiesGroupingRevision = _batchSet.GroupingRevision;
            foreach (XRayInstanceBatch batch in batches)
            {
                var block = new MaterialPropertyBlock();
                if (batch.Data != null && batch.Data.Length > 0)
                {
                    block.SetVectorArray(_propertyId, batch.Data);
                }

                _properties.Add(block);
            }
        }

        private sealed class LodScratch
        {
            public LodScratch(int capacity)
            {
                Capacity = capacity;
                ColorMatrices = new InstanceData[capacity];
                ShadowMatrices = new InstanceData[capacity];
                ColorData = new Vector4[capacity];
                ShadowData = new Vector4[capacity];
            }

            public int Capacity { get; }
            public InstanceData[] ColorMatrices { get; }
            public InstanceData[] ShadowMatrices { get; }
            public Vector4[] ColorData { get; }
            public Vector4[] ShadowData { get; }
            public MaterialPropertyBlock ColorBlock { get; } = new MaterialPropertyBlock();
            public MaterialPropertyBlock ShadowBlock { get; } = new MaterialPropertyBlock();
        }
    }
}
