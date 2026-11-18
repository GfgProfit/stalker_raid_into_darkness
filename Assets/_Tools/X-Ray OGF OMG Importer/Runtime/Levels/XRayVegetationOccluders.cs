using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Levels
{
    internal sealed class XRayVegetationOccluders
    {
        private const int MAX_RENDERERS_PER_VIEW = 16;
        private const int MAX_TRIANGLES_PER_VIEW = 640;
        private const int MAX_RENDERERS_INSPECTED_PER_VIEW = 768;
        private const float MAX_OCCLUDER_DISTANCE_SQUARED = 96f * 96f;
        private const string LEVEL_ROOT = "Assets/_Source/Levels/";
        private const string MESH_SHADER = "HDRP/MonolithLit";

        private readonly Transform _geometry;
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private readonly List<MeshFilter> _filters = new List<MeshFilter>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly Dictionary<Mesh, XRayVegetationOccluderData.Entry> _entries = new Dictionary<Mesh, XRayVegetationOccluderData.Entry>();
        private readonly Dictionary<Material, int> _materialModes = new Dictionary<Material, int>();
        private readonly Plane[] _planes = new Plane[6];
        private readonly int[] _candidates = new int[MAX_RENDERERS_PER_VIEW];
        private readonly float[] _candidateSizes = new float[MAX_RENDERERS_PER_VIEW];
        private readonly bool[] _defaultLayerDistances = new bool[32];
        private ResourceRequest _request;
        private bool _initialized;
        private bool _loadedFallback;

        public XRayVegetationOccluders(Transform geometry)
        {
            _geometry = geometry;
        }

        internal void Rasterize(Camera camera, XRayVegetationOcclusion query)
        {
            if (_geometry == null || !Application.isPlaying || camera.cameraType != CameraType.Game || camera.stereoEnabled || GL.invertCulling || GraphicsSettings.currentRenderPipeline is not HDRenderPipelineAsset || !Prepare()) return;
            if (camera.TryGetComponent(out HDAdditionalCameraData cameraData) && (cameraData.hasCustomRender || cameraData.fullscreenPassthrough)) return;
            if (!HDCamera.GetOrCreate(camera).frameSettings.IsEnabled(FrameSettingsField.OpaqueObjects) || !camera.TryGetCullingParameters(false, out ScriptableCullingParameters parameters) || parameters.cullingPlaneCount != _planes.Length) return;

            for (int i = 0; i < _defaultLayerDistances.Length; i++)
            {
                float layerCullDistance = parameters.GetLayerCullingDistance(i);
                _defaultLayerDistances[i] = layerCullDistance == 0f || !camera.layerCullSpherical && layerCullDistance >= camera.farClipPlane;
            }

            for (int i = 0; i < _planes.Length; i++) _planes[i] = parameters.GetCullingPlane(i);
            Vector3 eye = camera.transform.position;
            int candidateCount = 0;
            int stride = (_renderers.Count + MAX_RENDERERS_INSPECTED_PER_VIEW - 1) / MAX_RENDERERS_INSPECTED_PER_VIEW;
            if (stride == 0) return;
            for (int i = Time.frameCount % stride; i < _renderers.Count; i += stride)
            {
                MeshRenderer renderer = _renderers[i];
                MeshFilter filter = _filters[i];
                if (renderer == null || filter == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff || renderer.isPartOfStaticBatch || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                int layer = renderer.gameObject.layer;
                if (!_defaultLayerDistances[layer] || (camera.cullingMask & (1 << layer)) == 0) continue;
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || mesh.isReadable || !_entries.ContainsKey(mesh)) continue;
                Bounds bounds = renderer.bounds;
                if (bounds.SqrDistance(eye) > MAX_OCCLUDER_DISTANCE_SQUARED || !GeometryUtility.TestPlanesAABB(_planes, bounds)) continue;
                float size = bounds.extents.sqrMagnitude / Mathf.Max(1f, (bounds.center - eye).sqrMagnitude);
                if (!(size > 0f)) continue;
                int j = Mathf.Min(candidateCount, MAX_RENDERERS_PER_VIEW - 1);
                if (candidateCount == MAX_RENDERERS_PER_VIEW && size <= _candidateSizes[j]) continue;
                while (j > 0 && size > _candidateSizes[j - 1])
                {
                    _candidates[j] = _candidates[j - 1];
                    _candidateSizes[j] = _candidateSizes[j - 1];
                    j--;
                }

                _candidates[j] = i;
                _candidateSizes[j] = size;
                candidateCount = Mathf.Min(candidateCount + 1, MAX_RENDERERS_PER_VIEW);
            }

            int remaining = MAX_TRIANGLES_PER_VIEW;
            _materialModes.Clear();
            for (int i = 0; i < candidateCount && remaining > 0; i++)
            {
                int index = _candidates[i];
                MeshRenderer renderer = _renderers[index];
                Mesh mesh = _filters[index].sharedMesh;
                XRayVegetationOccluderData.Entry entry = _entries[mesh];
                if (!entry.Matches(mesh) || renderer.HasPropertyBlock() || renderer.GetComponentInParent<LODGroup>(true) != null) continue;
                Matrix4x4 transform = renderer.localToWorldMatrix;
                if (!(transform.determinant > 0f)) continue;
                renderer.GetSharedMaterials(_materials);
                IReadOnlyList<XRayVegetationOccluderData.Triangle> triangles = entry.Triangles;
                for (int t = 0; t < triangles.Count; t++)
                {
                    XRayVegetationOccluderData.Triangle triangle = triangles[t];
                    if (triangle.SubMesh >= _materials.Count) continue;
                    Material material = _materials[triangle.SubMesh];
                    if (material == null) continue;
                    if (!_materialModes.TryGetValue(material, out int mode))
                    {
                        mode = OccluderCullMode(material);
                        _materialModes.Add(material, mode);
                    }

                    if (mode < 0) continue;
                    query.AddTriangle(transform.MultiplyPoint3x4(triangle.A), transform.MultiplyPoint3x4(triangle.B), transform.MultiplyPoint3x4(triangle.C), mode == (int)CullMode.Off);
                    if (--remaining == 0) break;
                }
            }
        }

        private bool Prepare()
        {
            if (!_initialized)
            {
                _initialized = true;
                _geometry.GetComponentsInChildren(true, _renderers);
                foreach (MeshRenderer renderer in _renderers)
                {
                    _filters.Add(renderer.GetComponent<MeshFilter>());
                }

                string path = _geometry.gameObject.scene.path;
                int separator = path.LastIndexOf("/Scenes/", StringComparison.Ordinal);
                if (separator > LEVEL_ROOT.Length && path.StartsWith(LEVEL_ROOT, StringComparison.Ordinal))
                {
                    _request = Resources.LoadAsync<XRayVegetationOccluderData>(XRayVegetationOccluderData.ResourceKey(path.Substring(0, separator)));
                }
            }

            if (_request != null)
            {
                if (!_request.isDone) return false;
                Add(_request.asset as XRayVegetationOccluderData);
                _request = null;
            }

            if (!_loadedFallback && !HasGeometryEntries())
            {
                _loadedFallback = true;
                Transform root = _geometry.parent;
                if (root == null) return false;
                string level = root.name;
                if (level.EndsWith("(Clone)", StringComparison.Ordinal)) level = level.Substring(0, level.Length - 7).TrimEnd();
                if (level.IndexOf('/') >= 0 || level.IndexOf('\\') >= 0) return false;
                foreach (XRayVegetationOccluderData source in Resources.LoadAll<XRayVegetationOccluderData>(XRayVegetationOccluderData.RESOURCE_FOLDER + "/" + level)) Add(source);
            }

            _loadedFallback = true;
            return _entries.Count > 0;
        }

        private bool HasGeometryEntries()
        {
            foreach (MeshFilter filter in _filters)
            {
                if (filter != null && filter.sharedMesh != null && _entries.ContainsKey(filter.sharedMesh)) return true;
            }

            return false;
        }

        private void Add(XRayVegetationOccluderData source)
        {
            if (source == null || source.Version != XRayVegetationOccluderData.VERSION || source.Entries == null) return;
            foreach (XRayVegetationOccluderData.Entry entry in source.Entries)
            {
                if (entry.Mesh != null && entry.Triangles != null) _entries[entry.Mesh] = entry;
            }
        }

        private static int OccluderCullMode(Material material)
        {
            if (material == null || material.shader == null || material.shader.name != MESH_SHADER || material.renderQueue < (int)RenderQueue.Geometry || material.renderQueue > (int)RenderQueue.GeometryLast) return -1;
            if (material.GetFloat("_SurfaceType") != 0f || material.GetFloat("_ZWrite") < 0.5f || material.GetFloat("_AlphaCutoffEnable") > 0.5f || material.GetFloat("_DisplacementMode") != 0f || material.GetFloat("_DepthOffsetEnable") > 0.5f || material.GetFloat("_MonolithFloraWind") > 0f) return -1;
            if (material.IsKeywordEnabled("_ALPHATEST_ON") || material.IsKeywordEnabled("_MONOLITH_LEVEL_DETAIL") || material.IsKeywordEnabled("_MONOLITH_LEVEL_LOD") || material.IsKeywordEnabled("_MONOLITH_SKINNED_TBN") || material.IsKeywordEnabled("_VERTEX_DISPLACEMENT") || material.IsKeywordEnabled("_DEPTHOFFSET_ON")) return -1;
            if (!material.GetShaderPassEnabled("DepthForwardOnly") || !material.GetShaderPassEnabled("ForwardOnly")) return -1;
            int mode = (int)material.GetFloat("_CullMode");
            return (mode == (int)CullMode.Back || mode == (int)CullMode.Off) && mode == (int)material.GetFloat("_CullModeForward") ? mode : -1;
        }
    }
}
