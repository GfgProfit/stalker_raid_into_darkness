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
        private readonly List<XRayLevelInstancedRenderer> _instancedRenderers = new List<XRayLevelInstancedRenderer>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<Draw> _draws = new List<Draw>();
        private readonly List<InstancedDraw> _instancedDraws = new List<InstancedDraw>();
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
        public int GeometryCount => _draws.Count + _instancedDraws.Count;
        public int InstancedGeometryCount => _instancedDraws.Count;
        public int InstancedInstanceCount { get; private set; }
        public long EstimatedBytes => (_heights != null ? (long)_heights.width * _heights.height * 16L : 0L) + (long)InstancedInstanceCount * 64L;

        public XRayPrecipitationGeometry(Scene scene)
        {
            _scene = scene;
        }

        public void Refresh(PrecipitationConfig config, float realDelta, bool force = false)
        {
            Refresh(config, realDelta, force, null);
        }

        internal void Refresh(PrecipitationConfig config, float realDelta, bool force, XRayPrecipitationQuality? videoQuality)
        {
            if (config == null || !_scene.IsValid() || !_scene.isLoaded) return;
            _refreshElapsed += realDelta;
            int atlasResolution = PrecipitationConfig.GetAtlasResolution(videoQuality ?? config.Quality);
            bool resized = _heights == null || _heights.width != atlasResolution || _shader != config.HeightShader;
            if (!force && !resized && _refreshElapsed < config.GeometryRefreshSeconds) return;
            _refreshElapsed = 0.0f;
            _roots.Clear();
            _scene.GetRootGameObjects(_roots);
            int signature = CaptureGeometrySignature();
            if (!force && !resized && signature == _geometrySignature) return;
            _geometrySignature = signature;
            Rebuild(config, atlasResolution);
        }

        public void Dispose()
        {
            if (_heights != null) CoreUtils.Destroy(_heights);
            if (_material != null) CoreUtils.Destroy(_material);
            _heights = null;
            _material = null;
            _draws.Clear();
            _instancedDraws.Clear();
            _renderers.Clear();
            _instancedRenderers.Clear();
            _roots.Clear();
            InstancedInstanceCount = 0;
        }

        private int CaptureGeometrySignature()
        {
            int signature = 17;
            unchecked
            {
                foreach (GameObject root in _roots)
                {
                    if (!root.activeInHierarchy) continue;
                    _renderers.Clear();
                    root.GetComponentsInChildren(false, _renderers);
                    foreach (MeshRenderer renderer in _renderers)
                    {
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                        signature = signature * 31 + renderer.GetEntityId().GetHashCode();
                        signature = signature * 31 + filter.sharedMesh.GetEntityId().GetHashCode();
                        signature = signature * 31 + renderer.localToWorldMatrix.GetHashCode();
                        _materials.Clear();
                        renderer.GetSharedMaterials(_materials);
                        foreach (Material material in _materials) signature = MaterialSignature(signature, material);
                    }
                    _instancedRenderers.Clear();
                    root.GetComponentsInChildren(false, _instancedRenderers);
                    foreach (XRayLevelInstancedRenderer renderer in _instancedRenderers)
                    {
                        if (renderer == null || !renderer.isActiveAndEnabled || renderer.BatchSet == null) continue;
                        signature = signature * 31 + renderer.GetEntityId().GetHashCode();
                        signature = signature * 31 + renderer.BatchSet.GetEntityId().GetHashCode();
                        signature = signature * 31 + renderer.transform.localToWorldMatrix.GetHashCode();
                        foreach (XRayInstanceBatch batch in renderer.BatchSet.Batches)
                        {
                            if (batch == null || batch.Mesh == null || batch.Count == 0 || batch.Materials == null) continue;
                            signature = signature * 31 + batch.Mesh.GetEntityId().GetHashCode();
                            signature = signature * 31 + batch.Bounds.GetHashCode();
                            signature = signature * 31 + batch.Count;
                            foreach (Material material in batch.Materials) signature = MaterialSignature(signature, ResolveMaterial(renderer, material));
                            foreach (Matrix4x4 matrix in batch.RenderMatrices) signature = signature * 31 + matrix.GetHashCode();
                        }
                    }
                }
            }
            return signature;
        }

        private static int MaterialSignature(int signature, Material material)
        {
            unchecked
            {
                signature = signature * 31 + (material != null ? material.GetEntityId().GetHashCode() : 0);
                if (material == null) return signature;
                Texture texture = ResolveTexture(material);
                signature = signature * 31 + (texture != null ? texture.GetEntityId().GetHashCode() : 0);
                signature = signature * 31 + material.renderQueue;
                signature = signature * 31 + (material.HasProperty("_AlphaCutoff") ? material.GetFloat("_AlphaCutoff").GetHashCode() : 0);
                signature = signature * 31 + (material.IsKeywordEnabled("_ALPHATEST_ON") ? 1 : 0);
                if (material.HasProperty("_BaseColorMap"))
                {
                    signature = signature * 31 + material.GetTextureScale("_BaseColorMap").GetHashCode();
                    signature = signature * 31 + material.GetTextureOffset("_BaseColorMap").GetHashCode();
                }
                return signature;
            }
        }

        private static Texture ResolveTexture(Material material)
        {
            Shader shader = material.shader;
            if (shader == null) return null;
            if (material.HasProperty("_BaseColorMap")) return material.GetTexture("_BaseColorMap");
            int count = shader.GetPropertyCount();
            for (int index = 0; index < count; index++)
            {
                if (shader.GetPropertyType(index) == ShaderPropertyType.Texture && (shader.GetPropertyFlags(index) & ShaderPropertyFlags.MainTexture) != 0)
                {
                    return material.GetTexture(shader.GetPropertyNameId(index));
                }
            }
            return material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        }

        private static Material ResolveMaterial(XRayLevelInstancedRenderer renderer, Material material)
        {
            return material != null && renderer.MaterialResolver != null ? renderer.MaterialResolver(material) : material;
        }

        private void Rebuild(PrecipitationConfig config, int atlasResolution)
        {
            if (config.HeightShader == null || !config.HeightShader.isSupported) throw new InvalidOperationException("The precipitation height shader is missing or unsupported.");
            if (_material == null || _shader != config.HeightShader)
            {
                if (_material != null) CoreUtils.Destroy(_material);
                _material = CoreUtils.CreateEngineMaterial(config.HeightShader);
                _material.enableInstancing = true;
                _shader = config.HeightShader;
            }
            if (_heights == null || _heights.width != atlasResolution)
            {
                var heights = new RenderTexture(atlasResolution, atlasResolution, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                heights.name = "XRay precipitation world heights";
                heights.hideFlags = HideFlags.HideAndDontSave;
                heights.filterMode = FilterMode.Point;
                heights.wrapMode = TextureWrapMode.Clamp;
                if (!heights.Create())
                {
                    CoreUtils.Destroy(heights);
                    throw new InvalidOperationException("Could not allocate the precipitation height atlas.");
                }
                if (_heights != null) CoreUtils.Destroy(_heights);
                _heights = heights;
            }
            _draws.Clear();
            _instancedDraws.Clear();
            InstancedInstanceCount = 0;
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (GameObject root in _roots)
            {
                if (!root.activeInHierarchy) continue;
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
                        if (!TryCreateProperties(_materials[submesh], out MaterialPropertyBlock properties)) continue;
                        _draws.Add(new Draw(mesh, renderer.localToWorldMatrix, submesh, properties));
                        Encapsulate(ref bounds, ref hasBounds, renderer.bounds);
                    }
                }
                _instancedRenderers.Clear();
                root.GetComponentsInChildren(false, _instancedRenderers);
                foreach (XRayLevelInstancedRenderer renderer in _instancedRenderers)
                {
                    if (renderer == null || !renderer.isActiveAndEnabled || renderer.BatchSet == null) continue;
                    foreach (XRayInstanceBatch batch in renderer.BatchSet.Batches)
                    {
                        if (batch == null || batch.Mesh == null || batch.Count == 0 || batch.Materials == null) continue;
                        for (int submesh = 0; submesh < Mathf.Min(batch.Mesh.subMeshCount, batch.Materials.Length); submesh++)
                        {
                            if (!TryCreateProperties(ResolveMaterial(renderer, batch.Materials[submesh]), out MaterialPropertyBlock properties)) continue;
                            for (int start = 0; start < batch.Count; start += XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH)
                            {
                                int count = Mathf.Min(XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH, batch.Count - start);
                                var matrices = new Matrix4x4[count];
                                Array.Copy(batch.RenderMatrices, start, matrices, 0, count);
                                _instancedDraws.Add(new InstancedDraw(batch.Mesh, submesh, properties, matrices));
                                InstancedInstanceCount += count;
                            }
                            Encapsulate(ref bounds, ref hasBounds, batch.Bounds);
                        }
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
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (Draw draw in _draws) commands.DrawMesh(draw.Mesh, draw.Matrix, _material, draw.Submesh, pass, draw.Properties);
                    foreach (InstancedDraw draw in _instancedDraws) commands.DrawMeshInstanced(draw.Mesh, draw.Submesh, _material, pass, draw.Matrices, draw.Matrices.Length, draw.Properties);
                }
                Graphics.ExecuteCommandBuffer(commands);
                CaptureCount++;
            }
            finally
            {
                CommandBufferPool.Release(commands);
            }
        }

        private static bool TryCreateProperties(Material source, out MaterialPropertyBlock properties)
        {
            properties = null;
            if (source == null || source.renderQueue >= (int)RenderQueue.Transparent || source.shader == null || source.shader.name.IndexOf("Sky", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (source.HasProperty("_SurfaceType") && source.GetFloat("_SurfaceType") > 0.5f) return false;
            properties = new MaterialPropertyBlock();
            Texture texture = ResolveTexture(source);
            properties.SetTexture(_textureId, texture != null ? texture : Texture2D.whiteTexture);
            Vector2 scale = source.HasProperty("_BaseColorMap") ? source.GetTextureScale("_BaseColorMap") : Vector2.one;
            Vector2 offset = source.HasProperty("_BaseColorMap") ? source.GetTextureOffset("_BaseColorMap") : Vector2.zero;
            properties.SetVector(_textureTransformId, new Vector4(scale.x, scale.y, offset.x, offset.y));
            bool clipped = source.IsKeywordEnabled("_ALPHATEST_ON") || source.HasProperty("_AlphaCutoffEnable") && source.GetFloat("_AlphaCutoffEnable") > 0.5f;
            properties.SetFloat(_cutoffId, clipped && source.HasProperty("_AlphaCutoff") ? source.GetFloat("_AlphaCutoff") : -1.0f);
            return true;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool hasBounds, Bounds addition)
        {
            if (hasBounds) bounds.Encapsulate(addition);
            else bounds = addition;
            hasBounds = true;
        }

        private sealed class Draw
        {
            public Mesh Mesh { get; }
            public Matrix4x4 Matrix { get; }
            public int Submesh { get; }
            public MaterialPropertyBlock Properties { get; }

            public Draw(Mesh mesh, Matrix4x4 matrix, int submesh, MaterialPropertyBlock properties)
            {
                Mesh = mesh;
                Matrix = matrix;
                Submesh = submesh;
                Properties = properties;
            }
        }

        private sealed class InstancedDraw
        {
            public Mesh Mesh { get; }
            public int Submesh { get; }
            public MaterialPropertyBlock Properties { get; }
            public Matrix4x4[] Matrices { get; }

            public InstancedDraw(Mesh mesh, int submesh, MaterialPropertyBlock properties, Matrix4x4[] matrices)
            {
                Mesh = mesh;
                Submesh = submesh;
                Properties = properties;
                Matrices = matrices;
            }
        }
    }
}
