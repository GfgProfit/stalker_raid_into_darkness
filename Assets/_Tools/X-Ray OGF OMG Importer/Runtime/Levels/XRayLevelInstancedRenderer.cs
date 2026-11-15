using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    public sealed class XRayLevelInstancedRenderer : MonoBehaviour
    {
        public const string INSTANCE_DATA_PROPERTY = "_MonolithInstanceData";

        private const int MAX_MOTION_INSTANCES = 511;

        [SerializeField] private XRayInstanceBatchSet _batchSet;
        [SerializeField] private float _maxDistance;
        [SerializeField] private bool _castShadows = true;
        [SerializeField] private bool _lodImpostors;

        private readonly Plane[] _planes = new Plane[6];
        private readonly List<MaterialPropertyBlock> _properties = new List<MaterialPropertyBlock>();
        private readonly List<LodScratch> _lodScratch = new List<LodScratch>();
        private readonly List<InstanceHistory> _histories = new List<InstanceHistory>();
        private readonly Vector4[] _submissionData = new Vector4[MAX_MOTION_INSTANCES];
        private MaterialPropertyBlock _submissionBlock;
        private XRayLevelLodRenderer _lodRenderer;
        private int _propertyId;

        public XRayInstanceBatchSet BatchSet
        {
            get => _batchSet;
            set
            {
                _batchSet = value;
                _properties.Clear();
                _lodScratch.Clear();
                _histories.Clear();
            }
        }

        public bool LodImpostors
        {
            get => _lodImpostors;
            set => _lodImpostors = value;
        }

        public Func<Material, Material> MaterialResolver { get; set; }
        public int LastVisibleBatches { get; private set; }
        public int LastVisibleInstances { get; private set; }
        public int LastShadowOnlyBatches { get; private set; }
        public int LastRejectedShadowBatches { get; private set; }
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
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_batchSet == null || camera == null || camera.cameraType == CameraType.Preview)
            {
                return;
            }
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
            for (int i = 0; i < batches.Count; i++)
            {
                XRayInstanceBatch batch = batches[i];
                InstanceHistory history = History(i, batch);
                if (batch.Mesh == null || batch.Count == 0)
                {
                    continue;
                }

                Bounds bounds = XRayFloraWindBounds.Uses(batch.Materials, wind) ? XRayFloraWindBounds.Expand(batch.Bounds, wind) : batch.Bounds;
                if (bounds.SqrDistance(eye) > maxDistanceSqr)
                {
                    continue;
                }

                bool visible = GeometryUtility.TestPlanesAABB(_planes, bounds);
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

        private void Submit(Camera camera, XRayInstanceBatch batch, Bounds bounds, XRayFloraWindState wind, MaterialPropertyBlock block, InstanceData[] matrices, Vector4[] data, int count, ShadowCastingMode mode, bool moving)
        {
            int submeshes = Mathf.Min(batch.Mesh.subMeshCount, batch.Materials.Length);
            for (int submesh = 0; submesh < submeshes; submesh++)
            {
                Material source = batch.Materials[submesh];
                Material material;
                material = source != null && MaterialResolver != null ? MaterialResolver(source) : source;
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
                    parameters = new RenderParams(material) { camera = camera, worldBounds = bounds, motionVectorMode = moving || XRayFloraWindBounds.Uses(material, wind) ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera, layer = gameObject.layer, shadowCastingMode = mode, receiveShadows = true, matProps = properties };
                    Graphics.RenderMeshInstanced(parameters, batch.Mesh, submesh, matrices, length, start);
                }
            }
        }

        public void ResetMotionHistory()
        {
            _histories.Clear();
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
            if (_batchSet != null && _batchSet.HasPreparedMatrices && !batch.MatricesExposed)
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
                Instances = new InstanceData[source.Count];
                for (int i = 0; i < source.Count; i++) Instances[i] = new InstanceData(source.GetMatrix(i), source.GetMatrix(i));
            }

            public XRayInstanceBatch Source { get; }
            public int Revision { get; }
            public InstanceData[] Instances { get; }
            public int Frame { get; set; } = -1;
            public bool Moving { get; set; }
        }

        private void EnsureProperties()
        {
            IReadOnlyList<XRayInstanceBatch> batches = _batchSet.Batches;
            if (_properties.Count == batches.Count)
            {
                return;
            }

            _properties.Clear();
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
