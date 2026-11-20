using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    public sealed class XRayLevelDecalOrdering : MonoBehaviour
    {
        private const string DECAL_SHADER = "HDRP/MonolithDecal";

        private static readonly float[] _qualityDistance = { 0.0f, 60.0f, 120.0f, 240.0f };
        private static readonly int[] _qualityLimit = { 0, 32, 96, 256 };
        private static readonly List<XRayLevelDecalOrdering> _active = new();
        private static readonly List<MeshRenderer> _hidden = new();
        private static readonly List<DecalCandidate> _candidates = new();
        private static readonly List<(Vector3 Position, int Mask)> _views = new();
        private static int? _videoQuality;
        private static int _contextDepth;

        [SerializeField] private MeshRenderer[] _renderers;

        private int[] _indices;
        private int[] _originalPriorities;
        private float[] _distances;
        private readonly List<MeshRenderer> _qualityRenderers = new();

        public static void ApplyVideoQuality(int? quality)
        {
            if (quality.HasValue) quality = Mathf.Clamp(quality.Value, 0, _qualityLimit.Length - 1);
            if (_videoQuality == quality) return;
            if (!_videoQuality.HasValue)
            {
                RenderPipelineManager.beginContextRendering += BeginVideoContext;
                RenderPipelineManager.endContextRendering += EndVideoContext;
            }
            else if (!quality.HasValue)
            {
                RenderPipelineManager.beginContextRendering -= BeginVideoContext;
                RenderPipelineManager.endContextRendering -= EndVideoContext;
                RestoreVideoVisibility();
                _contextDepth = 0;
            }
            _videoQuality = quality;
        }

        public void Initialize(MeshRenderer[] renderers)
        {
            Restore();
            _renderers = renderers;
            Prepare();
        }

        public void Order(Camera camera)
        {
            if (camera == null || _renderers == null)
            {
                return;
            }

            if (_indices == null || _indices.Length != _renderers.Length)
            {
                Prepare();
            }

            Vector3 eye = camera.transform.position;
            for (int i = 0; i < _renderers.Length; i++)
            {
                MeshRenderer renderer = _renderers[i];
                _distances[i] = renderer != null && !renderer.forceRenderingOff ? (eye - renderer.bounds.center).sqrMagnitude : -1.0f;
                _indices[i] = i;
            }

            for (int i = 1; i < _indices.Length; i++)
            {
                int index = _indices[i];
                int j = i - 1;
                while (j >= 0 && _distances[_indices[j]] < _distances[index])
                {
                    _indices[j + 1] = _indices[j];
                    j--;
                }

                _indices[j + 1] = index;
            }

            for (int i = 0; i < _indices.Length; i++)
            {
                MeshRenderer renderer = _renderers[_indices[i]];
                if (renderer != null && !renderer.forceRenderingOff && renderer.rendererPriority != i)
                {
                    renderer.rendererPriority = i;
                }
            }
        }

        private void OnEnable()
        {
            Prepare();
            _active.Add(this);
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            _active.Remove(this);
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            Restore();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            Order(camera);
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!Application.isPlaying)
            {
                Restore();
            }
        }

        private void Prepare()
        {
            int count = _renderers?.Length ?? 0;
            _indices = new int[count];
            _originalPriorities = new int[count];
            _distances = new float[count];
            _qualityRenderers.Clear();
            for (int i = 0; i < count; i++)
            {
                MeshRenderer renderer = _renderers[i];
                if (renderer == null) continue;
                _originalPriorities[i] = renderer.rendererPriority;
                if (IsDecalOnly(renderer)) _qualityRenderers.Add(renderer);
            }
        }

        private void Restore()
        {
            if (_renderers == null || _originalPriorities == null)
            {
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].rendererPriority = _originalPriorities[i];
                }
            }
        }

        private static bool IsDecalOnly(MeshRenderer renderer)
        {
            if (renderer.shadowCastingMode != ShadowCastingMode.Off) return false;
            Material[] materials = renderer.sharedMaterials;
            foreach (Material material in materials)
            {
                if (material == null || material.shader == null || material.shader.name != DECAL_SHADER) return false;
            }
            return materials.Length > 0;
        }

        private static void BeginVideoContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_contextDepth++ != 0 || !Application.isPlaying || !_videoQuality.HasValue) return;
            foreach (Camera camera in cameras)
            {
                if (camera != null && camera.cameraType == CameraType.Game) _views.Add((camera.transform.position, camera.cullingMask));
            }
            if (_views.Count == 0) return;

            int tier = _videoQuality.Value;
            int limit = _qualityLimit[tier];
            float range = _qualityDistance[tier];
            float distanceLimit = range * range;
            foreach (XRayLevelDecalOrdering ordering in _active)
            {
                if (ordering == null) continue;
                foreach (MeshRenderer renderer in ordering._qualityRenderers)
                {
                    if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                    int layerMask = 1 << renderer.gameObject.layer;
                    float nearest = float.PositiveInfinity;
                    Bounds bounds = limit > 0 ? renderer.bounds : default;
                    foreach ((Vector3 position, int mask) in _views)
                    {
                        if ((mask & layerMask) == 0) continue;
                        nearest = limit > 0 ? Mathf.Min(nearest, bounds.SqrDistance(position)) : 0.0f;
                    }
                    if (float.IsPositiveInfinity(nearest)) continue;
                    if (limit == 0 || nearest > distanceLimit) Hide(renderer);
                    else _candidates.Add(new DecalCandidate(renderer, nearest, _candidates.Count));
                }
            }
            if (_candidates.Count > limit)
            {
                _candidates.Sort();
                for (int i = limit; i < _candidates.Count; i++)
                {
                    Hide(_candidates[i].Renderer);
                }
            }
            _candidates.Clear();
            _views.Clear();
        }

        private static void EndVideoContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_contextDepth > 0 && --_contextDepth == 0) RestoreVideoVisibility();
        }

        private static void Hide(MeshRenderer renderer)
        {
            renderer.forceRenderingOff = true;
            _hidden.Add(renderer);
        }

        private static void RestoreVideoVisibility()
        {
            foreach (MeshRenderer renderer in _hidden)
            {
                if (renderer != null) renderer.forceRenderingOff = false;
            }
            _hidden.Clear();
            _candidates.Clear();
            _views.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetVideoQuality()
        {
            ApplyVideoQuality(null);
        }

        private readonly struct DecalCandidate : IComparable<DecalCandidate>
        {
            private readonly float _distance;
            private readonly int _order;

            public MeshRenderer Renderer { get; }

            public DecalCandidate(MeshRenderer renderer, float distance, int order)
            {
                Renderer = renderer;
                _distance = distance;
                _order = order;
            }

            public int CompareTo(DecalCandidate other)
            {
                int byDistance = _distance.CompareTo(other._distance);
                return byDistance != 0 ? byDistance : _order.CompareTo(other._order);
            }
        }
    }
}
