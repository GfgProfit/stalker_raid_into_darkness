using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    public sealed class XRayLevelInstancedRenderer : MonoBehaviour
    {
        public const string INSTANCE_DATA_PROPERTY = "_MonolithInstanceData";

        [SerializeField] private XRayInstanceBatchSet _batchSet;
        [SerializeField] private float _maxDistance;
        [SerializeField] private bool _castShadows = true;
        [SerializeField] private bool _lodImpostors;

        private readonly Plane[] _planes = new Plane[6];
        private readonly List<MaterialPropertyBlock> _properties = new List<MaterialPropertyBlock>();
        private readonly List<LodScratch> _lodScratch = new List<LodScratch>();
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
            }
        }

        public bool LodImpostors
        {
            get => _lodImpostors;
            set => _lodImpostors = value;
        }

        public int LastVisibleBatches { get; private set; }
        public int LastVisibleInstances { get; private set; }
        public int LastShadowOnlyBatches { get; private set; }
        public int LastRejectedShadowBatches { get; private set; }
        public int LastImpostors { get; private set; }
        public int LastLodShadowOnlyInstances { get; private set; }
        public int LastLodDiscardedInstances { get; private set; }
        public int LodMeshCount => _lodRenderer != null ? _lodRenderer.MeshCount : 0;
        public XRayShadowInfluence ExperimentalShadowInfluence { get; set; }

        private void OnEnable()
        {
            _propertyId = Shader.PropertyToID(INSTANCE_DATA_PROPERTY);
            _properties.Clear();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _properties.Clear();
            _lodScratch.Clear();
            _lodRenderer?.Dispose();
            _lodRenderer = null;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_batchSet == null || camera == null || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
            {
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

                if (visible && states != null && batch.HasLods && RenderLodBatch(camera, i, batch, bounds, wind, states, castShadows, ref lodShadowOnly, ref lodDiscarded))
                {
                    continue;
                }

                int submeshes = Mathf.Min(batch.Mesh.subMeshCount, batch.Materials.Length);
                for (int submesh = 0; submesh < submeshes; submesh++)
                {
                    Material material = batch.Materials[submesh];
                    if (material == null)
                    {
                        continue;
                    }

                    var parameters = new RenderParams(material)
                    {
                        camera = camera,
                        worldBounds = bounds,
                        motionVectorMode = XRayFloraWindBounds.Uses(material, wind) ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera,
                        layer = gameObject.layer,
                        shadowCastingMode = castShadows ? visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off,
                        receiveShadows = true,
                        matProps = _properties[i]
                    };
                    Graphics.RenderMeshInstanced(parameters, batch.Mesh, submesh, batch.Matrices);
                }
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

        private bool RenderLodBatch(Camera camera, int index, XRayInstanceBatch batch, Bounds bounds, XRayFloraWindState wind, byte[] states, bool castShadows, ref int lodShadowOnly, ref int lodDiscarded)
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
                    Submit(camera, batch, bounds, wind, _properties[index], batch.Matrices, batch.Count, ShadowCastingMode.ShadowsOnly);
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
                    scratch.ShadowMatrices[shadowCount] = batch.Matrices[instance];
                    scratch.ShadowData[shadowCount++] = data;
                    continue;
                }

                scratch.ColorMatrices[colorCount] = batch.Matrices[instance];
                scratch.ColorData[colorCount++] = data;
            }

            scratch.ColorBlock.SetVectorArray(_propertyId, scratch.ColorData);
            Submit(camera, batch, bounds, wind, scratch.ColorBlock, scratch.ColorMatrices, colorCount, castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off);
            if (castShadows)
            {
                scratch.ShadowBlock.SetVectorArray(_propertyId, scratch.ShadowData);
                Submit(camera, batch, bounds, wind, scratch.ShadowBlock, scratch.ShadowMatrices, shadowCount, ShadowCastingMode.ShadowsOnly);
            }

            return true;
        }

        private void Submit(Camera camera, XRayInstanceBatch batch, Bounds bounds, XRayFloraWindState wind, MaterialPropertyBlock block, Matrix4x4[] matrices, int count, ShadowCastingMode mode)
        {
            int submeshes = Mathf.Min(batch.Mesh.subMeshCount, batch.Materials.Length);
            for (int submesh = 0; submesh < submeshes; submesh++)
            {
                Material material = batch.Materials[submesh];
                if (material == null)
                {
                    continue;
                }

                var parameters = new RenderParams(material) { camera = camera, worldBounds = bounds, motionVectorMode = XRayFloraWindBounds.Uses(material, wind) ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera, layer = gameObject.layer, shadowCastingMode = mode, receiveShadows = true, matProps = block };
                Graphics.RenderMeshInstanced(parameters, batch.Mesh, submesh, matrices, count);
            }
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
                ColorMatrices = new Matrix4x4[capacity];
                ShadowMatrices = new Matrix4x4[capacity];
                ColorData = new Vector4[capacity];
                ShadowData = new Vector4[capacity];
            }

            public int Capacity { get; }
            public Matrix4x4[] ColorMatrices { get; }
            public Matrix4x4[] ShadowMatrices { get; }
            public Vector4[] ColorData { get; }
            public Vector4[] ShadowData { get; }
            public MaterialPropertyBlock ColorBlock { get; } = new MaterialPropertyBlock();
            public MaterialPropertyBlock ShadowBlock { get; } = new MaterialPropertyBlock();
        }
    }
}
