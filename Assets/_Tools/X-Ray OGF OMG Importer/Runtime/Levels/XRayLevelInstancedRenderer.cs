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
        private readonly List<XRayInstanceNativeRenderer> _retiredNativeRenderers = new List<XRayInstanceNativeRenderer>();
        private readonly XRayInstanceNativeContext _nativeContext = new XRayInstanceNativeContext();
        private MaterialPropertyBlock _submissionBlock;
        private XRayLevelLodRenderer _lodRenderer;
        private bool[] _lodOwners;
        private bool[] _decorativeOwners;
        private DecorativePart[] _decorativeParts;
        private byte[] _objectDistanceStates;
        private Dictionary<Material, DecorativePart> _decorativeMaterials;
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
        private Func<Material, Material> _nativePreparedResolver;
        private bool _orderIndependentResolver;
        private GameConfig _configuration;
        private XRayInstanceNativeRenderer _nativeRenderer;
        private bool? _nativeSubmissionOverride;
        private bool? _lodImpostorsOverride;
        private Camera _nativeContextCamera;
        private bool _nativeContextActive;
        private bool _nativeContextAllowed;
        private bool _nativeContextChecked;
        private bool _nativePreparedCastShadows;
        private int _nativePreparedLayer;
        private string _nativeSubmissionFallbackReason;

        public XRayInstanceBatchSet BatchSet
        {
            get => _batchSet;
            set
            {
                ReleaseNativeRenderer();
                _batchSet = value;
                _properties.Clear();
                _lodScratch.Clear();
                _lodOwners = null;
                _histories.Clear();
                _groups = null;
                _groupByBatch = null;
                _groupOwner = null;
                ReleaseCallbackCaches();
            }
        }

        public bool LodImpostors
        {
            get => _lodImpostorsOverride ?? _lodImpostors;
            set => _lodImpostorsOverride = value;
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
        public GameConfig Configuration => _configuration;
        public bool? NativeSubmissionOverride
        {
            get => _nativeSubmissionOverride;
            set
            {
                _nativeSubmissionOverride = value;
                if (value == false && !_nativeContextActive) _nativeRenderer?.Disable();
            }
        }
        public bool NativeSubmissionActive => _nativeRenderer != null && _nativeRenderer.Active;
        public long NativeSubmissionMemoryBytes => _nativeRenderer != null ? _nativeRenderer.MemoryBytes : 0L;
        public int NativeCameraCommands => _nativeRenderer != null ? _nativeRenderer.LastCameraCommands : 0;
        public int NativeCameraInstances => _nativeRenderer != null ? _nativeRenderer.LastCameraInstances : 0;
        public int NativeCameraBatches => _nativeRenderer != null ? _nativeRenderer.LastCameraBatches : 0;
        public int NativeShadowCommands => _nativeRenderer != null ? _nativeRenderer.LastShadowCommands : 0;
        public int NativeShadowInstances => _nativeRenderer != null ? _nativeRenderer.LastShadowInstances : 0;
        public int NativeReflectionCommands => _nativeRenderer != null ? _nativeRenderer.ReflectionCommands : 0;
        public int NativeReflectionInstances => _nativeRenderer != null ? _nativeRenderer.ReflectionInstances : 0;
        public int NativeReflectionBatches => _nativeRenderer != null ? _nativeRenderer.ReflectionBatches : 0;
        public int NativeReflectionViews => _nativeRenderer != null ? _nativeRenderer.ReflectionViews : 0;
        public int NativeRegisteredReflectionViews => _nativeRenderer != null ? _nativeRenderer.RegisteredReflectionViews : 0;
        public int NativeCulledBatches => _nativeRenderer != null ? _nativeRenderer.LastCulledBatches : 0;
        public int NativeCulledInstances => _nativeRenderer != null ? _nativeRenderer.LastCulledInstances : 0;
        public int NativeShadowViews => _nativeRenderer != null ? _nativeRenderer.ShadowViews : 0;
        public int NativeCullingInvocations => _nativeRenderer != null ? _nativeRenderer.CullingInvocations : 0;
        public long NativeCullingOutputBytes => _nativeRenderer != null ? _nativeRenderer.LastCullingOutputBytes : 0L;
        public string NativeSubmissionFallbackReason => _nativeRenderer != null && _nativeRenderer.FallbackReason != null ? _nativeRenderer.FallbackReason : _nativeSubmissionFallbackReason;
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
            if (_configuration == null) _configuration = GameConfig.Load();
            _propertyId = Shader.PropertyToID(INSTANCE_DATA_PROPERTY);
            _submissionBlock = new MaterialPropertyBlock();
            _properties.Clear();
            _histories.Clear();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            RenderPipelineManager.endContextRendering += OnEndContextRendering;
            _groupOwner = null;
            ReleaseCallbackCaches();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            ReleaseNativeRenderer();
            if (!_nativeContextActive) RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            _properties.Clear();
            _lodScratch.Clear();
            _histories.Clear();
            _lodRenderer?.Dispose();
            _lodRenderer = null;
            _lodOwners = null;
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
                foreach (XRayInstanceNativeRenderer retired in _retiredNativeRenderers) retired.UnregisterReflection(camera);
                bool nativeEnabled = _nativeSubmissionOverride ?? (_configuration != null && _configuration.InstanceNativeSubmissionEnabled);
                if (nativeEnabled && _nativeContextActive && _nativeContextAllowed && _nativeRenderer != null && _nativeRenderer.Active && (MaterialResolver == null || _orderIndependentResolver) && ReferenceEquals(MaterialResolver, _nativePreparedResolver) && _castShadows == _nativePreparedCastShadows && gameObject.layer == _nativePreparedLayer && _maxDistance <= 0f && _nativeRenderer.TryRegisterReflection(camera)) return;
                _nativeRenderer?.UnregisterReflection(camera);
                RenderReflection(camera);
                return;
            }
            XRayFloraWindState wind = XRayFloraWindBounds.ActiveState();
            XRayVegetationOcclusion occlusion = XRayVegetationOcclusion.Begin(camera, transform.parent);
            bool authoredLod = LodImpostors;
            bool gameplayView = camera.cameraType == CameraType.Game && camera.targetTexture == null && !camera.orthographic && !camera.stereoEnabled;
            bool automaticLod = _lodImpostorsOverride == null && !authoredLod && (_configuration?.VegetationTreeImpostorsEnabled ?? true) && _maxDistance <= 0f && gameplayView;
            float objectDistance = Application.isPlaying && gameplayView ? XRayVideoOverrides.ObjectDrawDistance.GetValueOrDefault() : 0f;
            XRayLevelLodSet lods = _batchSet.Lods;
            bool hasLods = lods != null && lods.HasImpostorData;
            bool hasOwners = hasLods && (automaticLod || objectDistance > 0f) && PrepareAutomaticLodOwners(lods, objectDistance > 0f);
            bool[] decorativeOwners = hasOwners && objectDistance > 0f ? _decorativeOwners : null;
            byte[] states = null;
            if (hasLods && (authoredLod || automaticLod && hasOwners))
            {
                _lodRenderer ??= new XRayLevelLodRenderer();
                _lodRenderer.Evaluate(camera, lods, automaticLod ? _lodOwners : null, automaticLod ? occlusion : null, objectDistance, decorativeOwners);
                states = _lodRenderer.States;
            }
            else if (_lodRenderer != null)
            {
                _lodRenderer.Dispose();
                _lodRenderer = null;
            }
            if (states == null && decorativeOwners != null) states = PrepareObjectDistanceStates(lods, camera.transform.position, objectDistance);
            bool native = TryNativeSubmission(camera, wind, occlusion, states, out bool historyPrepared);
            if (!native)
            {
                EnsureProperties();
                GeometryUtility.CalculateFrustumPlanes(camera, _planes);
                ExperimentalShadowInfluence?.Prepare(camera);
            }
            Vector3 eye = native ? Vector3.zero : camera.transform.position;
            float maxDistanceSqr = _maxDistance > 0.0f ? _maxDistance * _maxDistance : float.MaxValue;
            int visibleBatches = 0;
            int visibleInstances = 0;
            int shadowOnlyBatches = 0;
            int rejectedShadowBatches = 0;
            int lodShadowOnly = 0;
            int lodDiscarded = 0;

            IReadOnlyList<XRayInstanceBatch> batches = _batchSet.Batches;
            LastSubmittedCalls = 0;
            LastSubmittedInstances = 0;
            bool grouped = !native && PrepareGroups(camera, states);
            for (int i = 0; !native && i < batches.Count; i++)
            {
                if (grouped && _groupByBatch[i] >= 0 && _groups[_groupByBatch[i]].Active)
                {
                    GroupScratch group = _groups[_groupByBatch[i]];
                    if (group.LeaderIndex != i || RenderGroup(camera, group, wind, occlusion, ref visibleBatches, ref visibleInstances, ref shadowOnlyBatches)) continue;
                }
                XRayInstanceBatch batch = batches[i];
                bool castShadows = _castShadows && batch.CastShadows;
                if (native && !castShadows) continue;
                InstanceHistory history = historyPrepared ? _histories[i] : History(i, batch);
                if (MeshSubmeshes(batch.Mesh) < 0 || batch.Count == 0)
                {
                    continue;
                }

                Bounds bounds;
                bool visible;
                if (grouped) visible = SourceVisible(i, batch, wind, occlusion, out bounds);
                else
                {
                    bounds = UsesWind(batch.Materials, wind) ? XRayFloraWindBounds.Expand(batch.Bounds, wind) : batch.Bounds;
                    visible = false;
                }
                if (!native && bounds.SqrDistance(eye) > maxDistanceSqr)
                {
                    continue;
                }
                if (!grouped) visible = IsColorVisible(batch, bounds, occlusion);

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

                Submit(camera, batch, bounds, wind, _properties[i], history.Instances, batch.Data, batch.Count, castShadows ? visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off, history.Moving, null, native);
            }

            if (_lodRenderer != null)
            {
                _lodRenderer.Render(camera, lods, gameObject.layer);
            }

            LastImpostors = _lodRenderer?.LastImpostors ?? 0;
            LastLodShadowOnlyInstances = lodShadowOnly;
            LastLodDiscardedInstances = lodDiscarded;
            LastVisibleBatches = visibleBatches;
            LastVisibleInstances = visibleInstances;
            LastShadowOnlyBatches = shadowOnlyBatches;
            LastRejectedShadowBatches = rejectedShadowBatches;
        }

        private bool PrepareAutomaticLodOwners(XRayLevelLodSet lods, bool classifyDecorative)
        {
            if (_lodOwners == null || _lodOwners.Length != lods.Count) _lodOwners = new bool[lods.Count];
            Array.Clear(_lodOwners, 0, _lodOwners.Length);
            if (classifyDecorative)
            {
                if (_decorativeParts == null || _decorativeParts.Length != lods.Count)
                {
                    _decorativeParts = new DecorativePart[lods.Count];
                    _decorativeOwners = new bool[lods.Count];
                }
                Array.Clear(_decorativeParts, 0, _decorativeParts.Length);
                _decorativeMaterials ??= new Dictionary<Material, DecorativePart>(new ReferenceComparer<Material>());
                _decorativeMaterials.Clear();
            }
            bool hasOwners = false;
            foreach (XRayInstanceBatch batch in _batchSet.Batches)
            {
                if (!batch.HasLods) continue;
                if (batch.MatricesExposed) return false;
                DecorativePart parts = classifyDecorative ? ClassifyDecorativeParts(batch.Materials) : DecorativePart.None;
                foreach (int lod in batch.LodIndices)
                {
                    if (lod < 0 || lod >= _lodOwners.Length) continue;
                    _lodOwners[lod] = true;
                    if (classifyDecorative) _decorativeParts[lod] |= parts;
                    hasOwners = true;
                }
            }
            if (classifyDecorative)
            {
                for (int lod = 0; lod < lods.Count; lod++)
                {
                    DecorativePart parts = _decorativeParts[lod];
                    _decorativeOwners[lod] = (parts & DecorativePart.Other) == 0 && ((parts & DecorativePart.Shrub) != 0 || (parts & (DecorativePart.Clutter | DecorativePart.Bark)) == DecorativePart.Clutter);
                }
            }
            return hasOwners;
        }

        private DecorativePart ClassifyDecorativeParts(Material[] materials)
        {
            if (materials == null || materials.Length == 0) return DecorativePart.Other;
            DecorativePart result = DecorativePart.None;
            foreach (Material material in materials)
            {
                if (material == null) return DecorativePart.Other;
                if (!_decorativeMaterials.TryGetValue(material, out DecorativePart part))
                {
                    string name = material.name;
                    if (name.StartsWith("trees_bushes", StringComparison.OrdinalIgnoreCase)) part = DecorativePart.Shrub;
                    else if (name.StartsWith("trees_bark_a_", StringComparison.OrdinalIgnoreCase)) part = DecorativePart.Bark;
                    else if (name.StartsWith("mtl_barrel", StringComparison.OrdinalIgnoreCase) || name.StartsWith("wood_collect", StringComparison.OrdinalIgnoreCase) || name.StartsWith("wood_wood_collect", StringComparison.OrdinalIgnoreCase) || name.StartsWith("wood_fire", StringComparison.OrdinalIgnoreCase) || name.StartsWith("wood_old", StringComparison.OrdinalIgnoreCase)) part = DecorativePart.Clutter;
                    else part = DecorativePart.Other;
                    _decorativeMaterials.Add(material, part);
                }
                result |= part;
            }
            return result;
        }

        private byte[] PrepareObjectDistanceStates(XRayLevelLodSet lods, Vector3 eye, float distance)
        {
            if (_objectDistanceStates == null || _objectDistanceStates.Length != lods.Count) _objectDistanceStates = new byte[lods.Count];
            bool anyHidden = false;
            for (int lod = 0; lod < lods.Count; lod++)
            {
                Vector4 sphere = lods.Spheres[lod];
                var center = new Vector3(sphere.x, sphere.y, sphere.z);
                bool hidden = _decorativeOwners[lod] && XRayLevelLodRenderer.BeyondObjectDistance(sphere.w, (center - eye).sqrMagnitude, distance);
                _objectDistanceStates[lod] = hidden ? XRayLevelLodRenderer.STATE_DISCARD : XRayLevelLodRenderer.STATE_FULL;
                anyHidden |= hidden;
            }
            return anyHidden ? _objectDistanceStates : null;
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

        private void Submit(Camera camera, XRayInstanceBatch batch, Bounds bounds, XRayFloraWindState wind, MaterialPropertyBlock block, InstanceData[] matrices, Vector4[] data, int count, ShadowCastingMode mode, bool moving, Material[] resolvedMaterials = null, bool preparedNativeMaterials = false)
        {
            int submeshes = Mathf.Min(MeshSubmeshes(batch.Mesh), batch.Materials.Length);
            for (int submesh = 0; submesh < submeshes; submesh++)
            {
                Material source = batch.Materials[submesh];
                Material material;
                if (preparedNativeMaterials)
                {
                    if (source == null) continue;
                    if (!_nativeRenderer.TryGetResolvedMaterial(source, out material)) throw new InvalidOperationException("A prepared native material is missing from the primary shadow submission cache.");
                }
                else material = resolvedMaterials != null ? resolvedMaterials[submesh] : _orderIndependentResolver ? ResolveGroupingMaterial(source) : source != null && MaterialResolver != null ? MaterialResolver(source) : source;
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
            if (!_nativeContextActive) _nativeRenderer?.Disable();
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

        public void ApplyConfiguration(GameConfig configuration)
        {
            if (!_nativeContextActive) _nativeRenderer?.Disable();
            _configuration = configuration;
            ReleaseGroups();
        }

        private bool TryNativeSubmission(Camera camera, XRayFloraWindState wind, XRayVegetationOcclusion occlusion, byte[] lodStates, out bool historyPrepared)
        {
            historyPrepared = false;
            bool enabled = _nativeSubmissionOverride ?? (_configuration != null && _configuration.InstanceNativeSubmissionEnabled);
            _nativeSubmissionFallbackReason = null;
            if (!enabled)
            {
                _nativeSubmissionFallbackReason = "Native submission is disabled.";
                return false;
            }
            if (!_nativeContextActive || !_nativeContextAllowed || camera != _nativeContextCamera)
            {
                _nativeSubmissionFallbackReason = "The render context does not contain a single supported camera.";
                return false;
            }
            if (camera.cameraType != CameraType.Game || camera.stereoEnabled || _maxDistance > 0f || LodImpostors || ExperimentalShadowInfluence != null || _configuration != null && _configuration.InstanceSubmissionGroupingEnabled || MaterialResolver != null && !_orderIndependentResolver || !_batchSet.HasPreparedMatrices)
            {
                _nativeSubmissionFallbackReason = "The renderer requires the original submission contract.";
                return false;
            }
            if (!_nativeContextChecked)
            {
                _nativeContextAllowed = _nativeContext.Supports(camera);
                _nativeContextChecked = true;
            }
            if (!_nativeContextAllowed)
            {
                _nativeSubmissionFallbackReason = "The render context requires the original submission path.";
                return false;
            }
            IReadOnlyList<XRayInstanceBatch> batches = _batchSet.Batches;
            bool moving = false;
            for (int index = 0; index < batches.Count; index++)
            {
                moving |= History(index, batches[index]).Moving;
            }
            historyPrepared = true;
            if (moving)
            {
                _nativeSubmissionFallbackReason = "Moving matrices require the original motion history path.";
                return false;
            }
            _nativeRenderer ??= new XRayInstanceNativeRenderer();
            Func<Material, Material> resolver = MaterialResolver;
            int layer = gameObject.layer;
            bool castShadows = _castShadows;
            bool prepared = _nativeRenderer.Prepare(_batchSet, camera, layer, castShadows, wind, resolver, occlusion, lodStates);
            if (prepared)
            {
                _nativePreparedResolver = resolver;
                _nativePreparedCastShadows = castShadows;
                _nativePreparedLayer = layer;
            }
            return prepared;
        }

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            _nativeRenderer?.BeginContext();
            _nativeContextActive = true;
            _nativeContextChecked = false;
            _nativePreparedResolver = null;
            _nativeContextCamera = cameras != null && cameras.Count == 1 ? cameras[0] : null;
            _nativeContextAllowed = _nativeContextCamera != null && _nativeContextCamera.cameraType == CameraType.Game && !_nativeContextCamera.stereoEnabled;
        }

        private void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            _nativeRenderer?.Disable();
            _nativeContextActive = false;
            _nativeContextAllowed = false;
            _nativeContextChecked = false;
            _nativePreparedResolver = null;
            _nativeContextCamera = null;
            foreach (XRayInstanceNativeRenderer renderer in _retiredNativeRenderers) renderer.Dispose();
            _retiredNativeRenderers.Clear();
            if (this == null || !isActiveAndEnabled) RenderPipelineManager.endContextRendering -= OnEndContextRendering;
        }

        private void ReleaseNativeRenderer()
        {
            if (_nativeRenderer == null) return;
            if (_nativeContextActive) _retiredNativeRenderers.Add(_nativeRenderer);
            else _nativeRenderer.Dispose();
            _nativeRenderer = null;
        }

        private void ReleaseGroups()
        {
            _groupOwner = null;
            _groups = null;
            _groupByBatch = null;
            _sourceBounds = null;
            _sourceVisibility = null;
        }

        private bool PrepareGroups(Camera camera, byte[] states)
        {
            if (_configuration == null || !_configuration.InstanceSubmissionGroupingEnabled)
            {
                if (_groups != null) ReleaseGroups();
                return false;
            }
            if (MaterialResolver != null && !_orderIndependentResolver) return false;
            if (_groupOwner != null && (_groupOwner != _batchSet || _groupRevision != _batchSet.GroupingRevision))
            {
                _groupOwner = null;
                _groups = null;
                _groupByBatch = null;
            }
            if (_batchSet.GroupingVersion != XRayInstanceBatchSet.GROUPING_VERSION || !_batchSet.HasPreparedMatrices || _batchSet.SubmissionGroups == null || _batchSet.SubmissionGroups.Count == 0 || _maxDistance > 0f || LodImpostors || states != null || ExperimentalShadowInfluence != null || camera.stereoEnabled || camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return false;
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

        private bool RenderGroup(Camera camera, GroupScratch group, XRayFloraWindState wind, XRayVegetationOcclusion occlusion, ref int visibleBatches, ref int visibleInstances, ref int shadowOnlyBatches)
        {
            XRayInstanceBatch first = group.Sources[0];
            bool usesWind = UsesWind(first.Materials, wind);
            Bounds bounds = usesWind ? XRayFloraWindBounds.Expand(group.Plan.Bounds, wind, group.MaximumSourceHeight) : group.Plan.Bounds;
            bool visible = false;
            for (int member = 0; member < group.Sources.Length; member++)
            {
                bool sourceVisible = SourceVisible(group.Plan.BatchIndices[member], group.Sources[member], wind, occlusion, out _);
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

        private bool SourceVisible(int index, XRayInstanceBatch batch, XRayFloraWindState wind, XRayVegetationOcclusion occlusion, out Bounds bounds)
        {
            if (_sourceVisibility[index] == 0)
            {
                bounds = UsesWind(batch.Materials, wind) ? XRayFloraWindBounds.Expand(batch.Bounds, wind) : batch.Bounds;
                _sourceBounds[index] = bounds;
                _sourceVisibility[index] = IsColorVisible(batch, bounds, occlusion) ? (byte)2 : (byte)1;
            }
            bounds = _sourceBounds[index];
            return _sourceVisibility[index] == 2;
        }

        private bool IsColorVisible(XRayInstanceBatch batch, Bounds bounds, XRayVegetationOcclusion occlusion)
        {
            if (!GeometryUtility.TestPlanesAABB(_planes, bounds)) return false;
            if (occlusion == null || batch.MatricesExposed || MaterialResolver != null && !_orderIndependentResolver || !XRayVegetationOcclusion.IsSmallFlora(bounds) || !XRayVegetationOcclusion.IsFlora(batch.Materials)) return true;
            return !occlusion.IsOccluded(bounds);
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
            _decorativeOwners = null;
            _decorativeParts = null;
            _objectDistanceStates = null;
            _decorativeMaterials = null;
            _meshSubmeshes.Clear();
            _meshSubmeshes.TrimExcess();
            _materialSupport.Clear();
            _materialSupport.TrimExcess();
            _windMaterials.Clear();
            _windMaterials.TrimExcess();
            _resolvedMaterials.Clear();
            _resolvedMaterials.TrimExcess();
        }

        [Flags]
        private enum DecorativePart : byte
        {
            None = 0,
            Shrub = 1,
            Bark = 2,
            Clutter = 4,
            Other = 8
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
