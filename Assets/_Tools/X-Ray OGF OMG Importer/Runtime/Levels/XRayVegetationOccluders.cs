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
        private const int MAX_HOM_RENDERERS_PER_VIEW = 4;
        private const int MAX_HOM_TRIANGLES_PER_VIEW = 160;
        private const int MAX_RENDERERS_INSPECTED_PER_VIEW = 768;
        private const float MAX_OCCLUDER_DISTANCE_SQUARED = 96f * 96f;
        private const string LEVEL_ROOT = "Assets/_Source/Levels/";

        private readonly Transform _geometry;
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private readonly List<MeshFilter> _filters = new List<MeshFilter>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly Dictionary<Mesh, XRayVegetationOccluderData.Entry> _entries = new Dictionary<Mesh, XRayVegetationOccluderData.Entry>();
        private readonly Dictionary<Material, int> _materialModes = new Dictionary<Material, int>();
        private readonly Plane[] _planes = new Plane[6];
        private readonly Candidate[] _candidates = new Candidate[MAX_RENDERERS_PER_VIEW];
        private readonly Candidate[] _homCandidates = new Candidate[MAX_HOM_RENDERERS_PER_VIEW];
        private readonly Occluder[] _occluders = new Occluder[MAX_RENDERERS_PER_VIEW];
        private readonly List<Material> _subMeshMaterials = new List<Material>();
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
            int homCandidateCount = 0;
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
                if (mesh == null || mesh.isReadable || !_entries.TryGetValue(mesh, out XRayVegetationOccluderData.Entry entry)) continue;
                Bounds bounds = renderer.bounds;
                if (bounds.SqrDistance(eye) > MAX_OCCLUDER_DISTANCE_SQUARED || !GeometryUtility.TestPlanesAABB(_planes, bounds)) continue;
                float size = bounds.extents.sqrMagnitude / Mathf.Max(1f, (bounds.center - eye).sqrMagnitude);
                if (!(size > 0f)) continue;
                var candidate = new Candidate(i, size);
                candidateCount = RankCandidate(_candidates, candidateCount, candidate);
                if (entry.HomTriangles is { Count: > 0 }) homCandidateCount = RankCandidate(_homCandidates, homCandidateCount, candidate);
            }

            PreferHomCandidates(candidateCount, homCandidateCount);
            int occluderCount = CollectOccluders(candidateCount);
            int homRemaining = RasterizeTriangles(query, occluderCount, MAX_HOM_TRIANGLES_PER_VIEW, true);
            RasterizeTriangles(query, occluderCount, MAX_TRIANGLES_PER_VIEW - MAX_HOM_TRIANGLES_PER_VIEW + homRemaining, false);
        }

        private static int RankCandidate(Candidate[] candidates, int count, Candidate candidate)
        {
            int index = Mathf.Min(count, candidates.Length - 1);
            if (count == candidates.Length && candidate.Size <= candidates[index].Size) return count;
            while (index > 0 && candidate.Size > candidates[index - 1].Size)
            {
                candidates[index] = candidates[index - 1];
                index--;
            }

            candidates[index] = candidate;
            return Mathf.Min(count + 1, candidates.Length);
        }

        private void PreferHomCandidates(int count, int homCount)
        {
            int replacement = count - 1;
            for (int i = 0; i < homCount; i++)
            {
                Candidate hom = _homCandidates[i];
                if (ContainsCandidate(_candidates, count, hom.Index)) continue;
                while (replacement >= 0 && ContainsCandidate(_homCandidates, homCount, _candidates[replacement].Index)) replacement--;
                if (replacement >= 0) _candidates[replacement--] = hom;
            }
        }

        private static bool ContainsCandidate(Candidate[] candidates, int count, int index)
        {
            for (int i = 0; i < count; i++)
            {
                if (candidates[i].Index == index) return true;
            }

            return false;
        }

        private int CollectOccluders(int candidateCount)
        {
            int count = 0;
            _subMeshMaterials.Clear();
            _materialModes.Clear();
            for (int i = 0; i < candidateCount; i++)
            {
                int index = _candidates[i].Index;
                MeshRenderer renderer = _renderers[index];
                Mesh mesh = _filters[index].sharedMesh;
                XRayVegetationOccluderData.Entry entry = _entries[mesh];
                if (!entry.Matches(mesh) || renderer.HasPropertyBlock() || renderer.GetComponentInParent<LODGroup>(true) != null) continue;
                Matrix4x4 transform = renderer.localToWorldMatrix;
                if (!(transform.determinant > 0f)) continue;
                renderer.GetSharedMaterials(_materials);
                int start = _subMeshMaterials.Count;
                int subMeshCount = Mathf.Min(mesh.subMeshCount, _materials.Count);
                if (subMeshCount == 0) continue;
                _subMeshMaterials.AddRange(_materials);
                _occluders[count++] = new Occluder(entry, transform, start, subMeshCount);
            }

            return count;
        }

        private int RasterizeTriangles(XRayVegetationOcclusion query, int count, int remaining, bool hom)
        {
            for (int i = 0; i < count && remaining > 0; i++)
            {
                Occluder occluder = _occluders[i];
                IReadOnlyList<XRayVegetationOccluderData.Triangle> triangles = hom ? occluder.Entry.HomTriangles : occluder.Entry.Triangles;
                if (triangles == null) continue;
                for (int t = 0; t < triangles.Count; t++)
                {
                    XRayVegetationOccluderData.Triangle triangle = triangles[t];
                    if ((uint)triangle.SubMesh >= (uint)occluder.SubMeshCount) continue;
                    Material material = _subMeshMaterials[occluder.SubMeshStart + triangle.SubMesh];
                    if (material == null) continue;
                    if (!_materialModes.TryGetValue(material, out int mode))
                    {
                        mode = XRayVegetationOccluderData.OccluderCullMode(material);
                        _materialModes.Add(material, mode);
                    }

                    if (mode < 0) continue;
                    Vector3 a = occluder.Transform.MultiplyPoint3x4(triangle.A);
                    Vector3 b = occluder.Transform.MultiplyPoint3x4(triangle.B);
                    Vector3 c = occluder.Transform.MultiplyPoint3x4(triangle.C);
                    bool doubleSided = mode == (int)CullMode.Off;
                    if (hom) query.AddHomTriangle(a, b, c, doubleSided && triangle.DoubleSided);
                    else query.AddTriangle(a, b, c, doubleSided);
                    if (--remaining == 0) break;
                }
            }

            return remaining;
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
            if (source == null || !source.IsReadableVersion || source.Entries == null) return;
            foreach (XRayVegetationOccluderData.Entry entry in source.Entries)
            {
                if (entry != null && entry.Mesh != null && (entry.Triangles is { Count: > 0 } || entry.HomTriangles is { Count: > 0 })) _entries[entry.Mesh] = entry;
            }
        }

        private readonly struct Candidate
        {
            public int Index { get; }
            public float Size { get; }

            public Candidate(int index, float size)
            {
                Index = index;
                Size = size;
            }
        }

        private readonly struct Occluder
        {
            public XRayVegetationOccluderData.Entry Entry { get; }
            public Matrix4x4 Transform { get; }
            public int SubMeshStart { get; }
            public int SubMeshCount { get; }

            public Occluder(XRayVegetationOccluderData.Entry entry, Matrix4x4 transform, int subMeshStart, int subMeshCount)
            {
                Entry = entry;
                Transform = transform;
                SubMeshStart = subMeshStart;
                SubMeshCount = subMeshCount;
            }
        }
    }
}
