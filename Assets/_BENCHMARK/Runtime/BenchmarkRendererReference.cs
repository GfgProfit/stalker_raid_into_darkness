using System;
using XRay.Importer.Levels;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;

namespace Rvm.Benchmark
{
    [ExecuteAlways]
    public sealed class BenchmarkRendererReference : MonoBehaviour
    {
        private int _harnessFrame;

        public void Observe(int index, XRayInstanceBatch batch, int frame)
        {
            _harnessFrame = frame;
            History(index, batch);
        }

        public void CameraCallback(Camera camera, int frame)
        {
            _harnessFrame = frame;
            OnBeginCameraRendering(default, camera);
        }

        public Array Payload(int index) => _histories[index].Instances;
        public bool Moving(int index) => _histories[index].Moving;
        public int HistoryCount => _histories.Count;

        public const string INSTANCE_DATA_PROPERTY = "_MonolithInstanceData";

        private const int MAX_MOTION_INSTANCES = 511;

        private static readonly ProfilerMarker CAMERA_MARKER = new ProfilerMarker("RVM.XRay.InstancedRenderer");
        private static readonly ProfilerMarker HISTORY_MARKER = new ProfilerMarker("RVM.XRay.MatrixHistory");
        private static readonly ProfilerMarker SUBMISSION_MARKER = new ProfilerMarker("RVM.XRay.Submission");
        private static readonly ProfilerMarker HISTORY_LOOP_MARKER = new ProfilerMarker("RVM.XRay.HistoryLoop");
        private static readonly ProfilerMarker MATERIAL_MARKER = new ProfilerMarker("RVM.XRay.MaterialResolve");
        private static readonly ProfilerMarker DATA_MARKER = new ProfilerMarker("RVM.XRay.InstanceDataMPB");
        private static readonly ProfilerMarker PARAMS_MARKER = new ProfilerMarker("RVM.XRay.RenderParams");
        private static readonly ProfilerMarker DRAW_MARKER = new ProfilerMarker("RVM.XRay.UnitySubmit");
        private static readonly ProfilerMarker LOD_MARKER = new ProfilerMarker("RVM.XRay.Lod");
        private static readonly ProfilerMarker REFLECTION_MARKER = new ProfilerMarker("RVM.XRay.Reflection");

        public static bool DiagnosticMarkersEnabled { get; set; }

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
        private HashSet<XRayInstanceBatch> _diagnosticSubmitted;

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

        public Camera DiagnosticCamera { get; private set; }
        public int DiagnosticFrame { get; private set; } = -1;
        public int DiagnosticCpuSubmissions { get; private set; }
        public int DiagnosticSubmittedBatches => _diagnosticSubmitted?.Count ?? 0;
        public int DiagnosticSubmissionInstanceReferences { get; private set; }
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

        }

        private void OnDisable()
        {

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

            using var diagnosticScope = new DiagnosticScope(CAMERA_MARKER);
            if (DiagnosticMarkersEnabled)
            {
                _diagnosticSubmitted ??= new HashSet<XRayInstanceBatch>(_batchSet.Batches.Count);
                _diagnosticSubmitted.Clear();
                DiagnosticCpuSubmissions = 0;
                DiagnosticSubmissionInstanceReferences = 0;
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
            if (DiagnosticMarkersEnabled)
            {
                DiagnosticCamera = camera;
                DiagnosticFrame = _harnessFrame;
            }
        }

        private void RenderReflection(Camera camera)
        {
            using var diagnosticScope = new DiagnosticScope(REFLECTION_MARKER);
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
                using (new DiagnosticScope(MATERIAL_MARKER))
                {
                    material = source != null && MaterialResolver != null ? MaterialResolver(source) : source;
                }
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
                        RendererSubmissionTrace.Draw(parameters, batch.Mesh, submesh, batch.Matrices, count, start);
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
            using var diagnosticScope = new DiagnosticScope(LOD_MARKER);
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
            using var diagnosticScope = new DiagnosticScope(SUBMISSION_MARKER);
            int submeshes = Mathf.Min(batch.Mesh.subMeshCount, batch.Materials.Length);
            for (int submesh = 0; submesh < submeshes; submesh++)
            {
                Material source = batch.Materials[submesh];
                Material material;
                using (new DiagnosticScope(MATERIAL_MARKER))
                {
                    material = source != null && MaterialResolver != null ? MaterialResolver(source) : source;
                }
                if (material == null)
                {
                    continue;
                }

                for (int start = 0; start < count; start += MAX_MOTION_INSTANCES)
                {
                    int length = Mathf.Min(MAX_MOTION_INSTANCES, count - start);
                    MaterialPropertyBlock properties = block;
                    using (new DiagnosticScope(DATA_MARKER))
                    {
                        if (count > MAX_MOTION_INSTANCES)
                        {
                            for (int i = 0; i < length; i++) _submissionData[i] = data != null && start + i < data.Length ? data[start + i] : new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                            _submissionBlock.SetVectorArray(_propertyId, _submissionData);
                            properties = _submissionBlock;
                        }
                    }
                    RenderParams parameters;
                    using (new DiagnosticScope(PARAMS_MARKER))
                    {
                        parameters = new RenderParams(material) { camera = camera, worldBounds = bounds, motionVectorMode = moving || XRayFloraWindBounds.Uses(material, wind) ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera, layer = gameObject.layer, shadowCastingMode = mode, receiveShadows = true, matProps = properties };
                    }
                    using (new DiagnosticScope(DRAW_MARKER))
                    {
                        RendererSubmissionTrace.Draw(parameters, batch.Mesh, submesh, matrices, length, start);
                    }
                    if (DiagnosticMarkersEnabled)
                    {
                        DiagnosticCpuSubmissions++;
                        DiagnosticSubmissionInstanceReferences += length;
                        _diagnosticSubmitted.Add(batch);
                    }
                }
            }
        }

        public void ResetMotionHistory()
        {
            _histories.Clear();
        }

        private InstanceHistory History(int index, XRayInstanceBatch batch)
        {
            using var diagnosticScope = new DiagnosticScope(HISTORY_MARKER);
            while (_histories.Count <= index) _histories.Add(null);
            InstanceHistory history = _histories[index];
            if (history == null || !ReferenceEquals(history.Source, batch) || history.Instances.Length != batch.Count)
            {
                history = new InstanceHistory(batch);
                _histories[index] = history;
            }
            using var loopScope = new DiagnosticScope(HISTORY_LOOP_MARKER);
            bool advance = history.Frame != _harnessFrame;
            history.Moving = false;
            for (int i = 0; i < batch.Count; i++)
            {
                Matrix4x4 current = batch.Matrices[i];
                Matrix4x4 previous = advance ? history.Instances[i].objectToWorld : history.Instances[i].prevObjectToWorld;
                history.Instances[i] = new InstanceData(current, previous);
                history.Moving |= !current.Equals(previous);
            }
            history.Frame = _harnessFrame;
            return history;
        }

        private readonly struct DiagnosticScope : IDisposable
        {
            private readonly ProfilerMarker _marker;
            private readonly bool _enabled;

            public DiagnosticScope(ProfilerMarker marker)
            {
                _marker = marker;
                _enabled = DiagnosticMarkersEnabled;
                if (_enabled)
                {
                    _marker.Begin();
                }
            }

            public void Dispose()
            {
                if (_enabled)
                {
                    _marker.End();
                }
            }
        }

        private struct InstanceData
        {
            public readonly Matrix4x4 objectToWorld;
            public readonly Matrix4x4 prevObjectToWorld;

            public InstanceData(Matrix4x4 current, Matrix4x4 previous)
            {
                objectToWorld = current;
                prevObjectToWorld = previous;
            }
        }

        private sealed class InstanceHistory
        {
            public InstanceHistory(XRayInstanceBatch source)
            {
                Source = source;
                Instances = new InstanceData[source.Count];
                for (int i = 0; i < source.Count; i++) Instances[i] = new InstanceData(source.Matrices[i], source.Matrices[i]);
            }

            public XRayInstanceBatch Source { get; }
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
