using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public sealed class XRayPrecipitationGeometry : IDisposable
    {
        private static readonly int _mappingId = Shader.PropertyToID("_XRayRainWorldMapping");
        private static readonly int _textureId = Shader.PropertyToID("_XRayRainRoofTexture");
        private static readonly int _textureTransformId = Shader.PropertyToID("_XRayRainRoofTexture_ST");
        private static readonly int _cutoffId = Shader.PropertyToID("_XRayRainRoofCutoff");

        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<Draw> _draws = new List<Draw>();
        private readonly Scene _scene;

        private Material _material;
        private RenderTexture _heights;
        private Shader _shader;
        private int _geometrySignature;
        private float _refreshElapsed;

        public RenderTexture Heights => _heights;
        public Vector4 WorldMapping { get; private set; }
        public Bounds Bounds { get; private set; }
        public int CaptureCount { get; private set; }
        public int GeometryCount => _draws.Count;
        public long EstimatedBytes => _heights != null ? (long)_heights.width * _heights.height * 16L : 0L;

        public XRayPrecipitationGeometry(Scene scene)
        {
            _scene = scene;
        }

        public void Refresh(PrecipitationConfig config, float realDelta, bool force = false)
        {
            if (config == null || !_scene.IsValid() || !_scene.isLoaded) return;
            _refreshElapsed += realDelta;
            bool resized = _heights == null || _heights.width != config.AtlasResolution || _shader != config.HeightShader;
            if (!force && !resized && _refreshElapsed < config.GeometryRefreshSeconds) return;
            _refreshElapsed = 0.0f;
            _roots.Clear();
            _scene.GetRootGameObjects(_roots);
            int signature = CaptureGeometrySignature();
            bool changed = force || resized || signature != _geometrySignature;
            if (!changed)
            {
                foreach (Draw draw in _draws)
                {
                    if (draw.Renderer == null || !draw.Renderer.enabled || !draw.Renderer.gameObject.activeInHierarchy || draw.Matrix != draw.Renderer.localToWorldMatrix)
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (!changed) return;
            _geometrySignature = signature;
            Rebuild(config);
        }

        public void Dispose()
        {
            if (_heights != null) CoreUtils.Destroy(_heights);
            if (_material != null) CoreUtils.Destroy(_material);
            _heights = null;
            _material = null;
            _draws.Clear();
            _renderers.Clear();
            _roots.Clear();
        }

        private int CaptureGeometrySignature()
        {
            int signature = 17;
            foreach (GameObject root in _roots)
            {
                _renderers.Clear();
                root.GetComponentsInChildren(false, _renderers);
                foreach (MeshRenderer renderer in _renderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                    unchecked
                    {
                        signature = signature * 31 + renderer.GetEntityId().GetHashCode();
                        signature = signature * 31 + filter.sharedMesh.GetEntityId().GetHashCode();
                        _materials.Clear();
                        renderer.GetSharedMaterials(_materials);
                        foreach (Material material in _materials) signature = signature * 31 + (material != null ? material.GetEntityId().GetHashCode() : 0);
                    }
                }
            }
            return signature;
        }

        private void Rebuild(PrecipitationConfig config)
        {
            if (config.HeightShader == null || !config.HeightShader.isSupported) throw new InvalidOperationException("The precipitation height shader is missing or unsupported.");
            if (_material == null || _shader != config.HeightShader)
            {
                if (_material != null) CoreUtils.Destroy(_material);
                _material = CoreUtils.CreateEngineMaterial(config.HeightShader);
                _shader = config.HeightShader;
            }
            if (_heights == null || _heights.width != config.AtlasResolution)
            {
                if (_heights != null) CoreUtils.Destroy(_heights);
                _heights = new RenderTexture(config.AtlasResolution, config.AtlasResolution, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                _heights.name = "XRay precipitation world heights";
                _heights.hideFlags = HideFlags.HideAndDontSave;
                _heights.filterMode = FilterMode.Point;
                _heights.wrapMode = TextureWrapMode.Clamp;
                if (!_heights.Create()) throw new InvalidOperationException("Could not allocate the precipitation height atlas.");
            }
            _draws.Clear();
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (GameObject root in _roots)
            {
                _renderers.Clear();
                root.GetComponentsInChildren(false, _renderers);
                foreach (MeshRenderer renderer in _renderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                    _materials.Clear();
                    renderer.GetSharedMaterials(_materials);
                    Mesh mesh = filter.sharedMesh;
                    for (int submesh = 0; submesh < Mathf.Min(mesh.subMeshCount, _materials.Count); submesh++)
                    {
                        Material source = _materials[submesh];
                        if (source == null || source.renderQueue >= (int)RenderQueue.Transparent || source.shader == null || source.shader.name.IndexOf("Sky", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (source.HasProperty("_SurfaceType") && source.GetFloat("_SurfaceType") > 0.5f) continue;
                        var properties = new MaterialPropertyBlock();
                        Texture texture = source.HasProperty("_BaseColorMap") ? source.GetTexture("_BaseColorMap") : source.mainTexture;
                        properties.SetTexture(_textureId, texture != null ? texture : Texture2D.whiteTexture);
                        Vector2 scale = source.HasProperty("_BaseColorMap") ? source.GetTextureScale("_BaseColorMap") : Vector2.one;
                        Vector2 offset = source.HasProperty("_BaseColorMap") ? source.GetTextureOffset("_BaseColorMap") : Vector2.zero;
                        properties.SetVector(_textureTransformId, new Vector4(scale.x, scale.y, offset.x, offset.y));
                        bool clipped = source.IsKeywordEnabled("_ALPHATEST_ON") || source.HasProperty("_AlphaCutoffEnable") && source.GetFloat("_AlphaCutoffEnable") > 0.5f;
                        properties.SetFloat(_cutoffId, clipped && source.HasProperty("_AlphaCutoff") ? source.GetFloat("_AlphaCutoff") : -1.0f);
                        _draws.Add(new Draw(renderer, mesh, renderer.localToWorldMatrix, submesh, properties));
                        if (hasBounds) bounds.Encapsulate(renderer.bounds);
                        else bounds = renderer.bounds;
                        hasBounds = true;
                    }
                }
            }
            if (!hasBounds) bounds = new Bounds(Vector3.zero, new Vector3(100.0f, 100.0f, 100.0f));
            bounds.Expand(new Vector3(2.0f, 2.0f, 2.0f));
            Bounds = bounds;
            WorldMapping = new Vector4(bounds.min.x, bounds.min.z, 1.0f / Mathf.Max(bounds.size.x, 1.0f), 1.0f / Mathf.Max(bounds.size.z, 1.0f));
            _material.SetVector(_mappingId, WorldMapping);
            var commands = CommandBufferPool.Get("X-Ray precipitation world heights");
            try
            {
                commands.SetRenderTarget(_heights);
                commands.ClearRenderTarget(false, true, new Color(bounds.min.y - 1.0f, bounds.max.y + 1.0f, 0.0f, 0.0f));
                foreach (Draw draw in _draws) commands.DrawMesh(draw.Mesh, draw.Matrix, _material, draw.Submesh, 0, draw.Properties);
                foreach (Draw draw in _draws) commands.DrawMesh(draw.Mesh, draw.Matrix, _material, draw.Submesh, 1, draw.Properties);
                Graphics.ExecuteCommandBuffer(commands);
                CaptureCount++;
            }
            finally
            {
                CommandBufferPool.Release(commands);
            }
        }

        private sealed class Draw
        {
            public MeshRenderer Renderer { get; }
            public Mesh Mesh { get; }
            public Matrix4x4 Matrix { get; }
            public int Submesh { get; }
            public MaterialPropertyBlock Properties { get; }

            public Draw(MeshRenderer renderer, Mesh mesh, Matrix4x4 matrix, int submesh, MaterialPropertyBlock properties)
            {
                Renderer = renderer;
                Mesh = mesh;
                Matrix = matrix;
                Submesh = submesh;
                Properties = properties;
            }
        }
    }
}
