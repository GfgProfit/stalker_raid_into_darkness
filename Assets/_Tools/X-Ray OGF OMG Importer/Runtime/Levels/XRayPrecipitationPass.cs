using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    [Serializable]
    public sealed class XRayPrecipitationPass : CustomPass
    {
        private const int DROP_STRIDE = 64;

        private readonly Dictionary<Camera, View> _views = new Dictionary<Camera, View>();
        private readonly List<Camera> _removedViews = new List<Camera>();

        private GameObject _owner;
        private PrecipitationConfig _settings;
        private XRayPrecipitationGeometry _geometry;
        private Material _material;
        private ComputeShader _compute;
        private Shader _shader;
        private ComputeShader _sourceCompute;
        private int _simulationKernel;
        private int _initializationKernel;
        private Color _radiance;
        private Vector3 _direction;
        private float _speedFactor;
        private float _windVelocity;
        private float _density;
        private float _realTime;
        private float _drySeconds;
        private bool _visible;

        public int ViewCount => _views.Count;
        public int LastDropCount { get; private set; }
        public int RenderCount { get; private set; }
        public Camera LastCamera { get; private set; }
        public long EstimatedBytes => _views.Count * (long)(_settings != null ? _settings.DropBudget : 0) * DROP_STRIDE;

        public void Configure(GameObject owner, PrecipitationConfig settings, XRayPrecipitationGeometry geometry, Color radiance, Vector3 direction, float speedFactor, float windVelocity, float density, float realTime, float realDelta, bool visible)
        {
            if (_density <= 0.0f && density > 0.0f) ResetRain();
            _owner = owner;
            _settings = settings;
            _geometry = geometry;
            _radiance = radiance;
            _direction = direction;
            _speedFactor = speedFactor;
            _windVelocity = windVelocity;
            _density = density;
            _drySeconds = density > 0.0f ? 0.0f : _drySeconds + realDelta;
            _realTime = realTime;
            _visible = visible;
            name = "X-Ray Precipitation";
            targetColorBuffer = TargetBuffer.Camera;
            targetDepthBuffer = TargetBuffer.None;
            _removedViews.Clear();
            foreach (KeyValuePair<Camera, View> pair in _views)
            {
                if (pair.Value.Camera == null) _removedViews.Add(pair.Key);
                else pair.Value.PendingDelta += realDelta;
            }
            foreach (Camera key in _removedViews)
            {
                _views[key].Dispose();
                _views.Remove(key);
            }
            enabled = owner != null && settings != null;
        }

        public void ResetRain()
        {
            foreach (View view in _views.Values) view.Dispose();
            _views.Clear();
            LastCamera = null;
            LastDropCount = 0;
        }

        public int CapacityForCamera(Camera camera)
        {
            return camera != null && _views.TryGetValue(camera, out View view) ? view.Capacity : 0;
        }

        public bool TryReadDrops(Camera camera, XRayRainDropState[] target)
        {
            if (camera == null || target == null || !_views.TryGetValue(camera, out View view) || !view.Initialized || target.Length < view.Capacity) return false;
            view.Drops.GetData(target);
            return true;
        }

        public void Release()
        {
            ResetRain();
            if (_material != null) CoreUtils.Destroy(_material);
            if (_compute != null) CoreUtils.Destroy(_compute);
            _material = null;
            _compute = null;
            _shader = null;
            _sourceCompute = null;
            enabled = false;
        }

        protected override void Execute(CustomPassContext context)
        {
            Camera camera = context.hdCamera.camera;
            if (!CanRender(camera) || _settings == null || _geometry == null || _geometry.Heights == null || !_visible) return;
            if (_density <= 0.0f && (_views.Count == 0 || _drySeconds > XRayPrecipitationMath.SOURCE_SPLASH_SECONDS)) return;
            if (_material == null || _shader != _settings.RainShader || _compute == null || _sourceCompute != _settings.RainCompute)
            {
                Release();
                _material = CoreUtils.CreateEngineMaterial(_settings.RainShader);
                _compute = UnityEngine.Object.Instantiate(_settings.RainCompute);
                _compute.hideFlags = HideFlags.HideAndDontSave;
                _shader = _settings.RainShader;
                _sourceCompute = _settings.RainCompute;
                _initializationKernel = _compute.FindKernel("InitializeDrops");
                _simulationKernel = _compute.FindKernel("SimulateDrops");
                enabled = true;
            }
            if (!_views.TryGetValue(camera, out View view))
            {
                if (_views.Count >= _settings.MaxCameraViews) return;
                view = new View(camera, _settings.DropBudget);
                _views.Add(camera, view);
            }
            if (view.Capacity != _settings.DropBudget)
            {
                view.Dispose();
                view = new View(camera, _settings.DropBudget);
                _views[camera] = view;
            }
            LastDropCount = XRayPrecipitationMath.DropCount(_density, view.Capacity);
            float minimumFlightSeconds = XRayPrecipitationMath.SOURCE_MAX_DISTANCE / Mathf.Max(XRayPrecipitationMath.SOURCE_MAX_SPEED * _speedFactor, 0.01f);
            bool initialize = !view.Initialized || view.PendingDelta > Mathf.Max(XRayPrecipitationMath.SOURCE_SPLASH_SECONDS, minimumFlightSeconds);
            int kernel = initialize ? _initializationKernel : _simulationKernel;
            Vector3 center = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            context.cmd.SetComputeBufferParam(_compute, kernel, "_XRayRainDrops", view.Drops);
            context.cmd.SetComputeTextureParam(_compute, kernel, "_XRayRainHeights", _geometry.Heights);
            context.cmd.SetComputeVectorParam(_compute, "_XRayRainWorldMapping", _geometry.WorldMapping);
            context.cmd.SetComputeVectorParam(_compute, "_XRayRainSource", new Vector4(center.x, center.y, center.z, _settings.DrawRadius));
            context.cmd.SetComputeVectorParam(_compute, "_XRayRainForward", new Vector4(forward.x, 0.0f, forward.z, Mathf.Clamp01(_windVelocity * 0.001f)));
            context.cmd.SetComputeVectorParam(_compute, "_XRayRainMotion", new Vector4(_direction.x, _direction.y, _direction.z, _speedFactor));
            context.cmd.SetComputeIntParam(_compute, "_XRayRainDropCount", view.Capacity);
            context.cmd.SetComputeIntParam(_compute, "_XRayRainActiveCount", LastDropCount);
            context.cmd.SetComputeIntParam(_compute, "_XRayRainSplashBudget", _settings.SplashesEnabled ? _settings.MaxSplashes : 0);
            context.cmd.SetComputeFloatParam(_compute, "_XRayRainDelta", initialize ? 0.0f : view.PendingDelta);
            context.cmd.DispatchCompute(_compute, kernel, (view.Capacity + 63) / 64, 1, 1);
            view.Initialized = true;
            view.PendingDelta = 0.0f;
            MaterialPropertyBlock properties = context.propertyBlock;
            properties.Clear();
            properties.SetBuffer("_XRayRainDrops", view.Drops);
            properties.SetTexture("_XRayRainTexture", _settings.RainTexture);
            properties.SetTexture("_XRaySplashTexture", _settings.SplashTexture);
            properties.SetTexture("_XRayRainHeights", _geometry.Heights);
            properties.SetVector("_XRayRainWorldMapping", _geometry.WorldMapping);
            properties.SetVector("_XRayRainCamera", new Vector4(center.x, center.y, center.z, 0.0f));
            properties.SetVector("_XRayRainShape", new Vector4(_settings.DropLength, _settings.DropHalfWidth, _settings.SplashSize, _settings.RainOpacity));
            properties.SetVector("_XRayRainAppearance", new Vector4(_density, _realTime, _settings.DrawRadius, _settings.SplashesEnabled ? 1.0f : 0.0f));
            properties.SetVector("_XRayRainColor", new Vector4(_radiance.r, _radiance.g, _radiance.b, _radiance.a));
            if (LastDropCount > 0) context.cmd.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 6, LastDropCount, properties);
            int splashes = Mathf.Min(view.Capacity, _settings.MaxSplashes);
            if (_settings.SplashesEnabled && splashes > 0) context.cmd.DrawProcedural(Matrix4x4.identity, _material, 1, MeshTopology.Triangles, 6, splashes, properties);
            LastCamera = camera;
            RenderCount++;
        }

        protected override void Cleanup()
        {
            Release();
        }

        private bool CanRender(Camera camera)
        {
            if (!enabled || _owner == null || !_owner.activeInHierarchy || camera == null) return false;
            Scene scene = _owner.scene;
            if (!scene.IsValid() || !scene.isLoaded) return false;
            if (camera.cameraType == CameraType.SceneView) return SceneManager.GetActiveScene() == scene;
            if (camera.cameraType != CameraType.Game) return false;
            return (camera.scene.IsValid() ? camera.scene : camera.gameObject.scene) == scene;
        }

        private sealed class View : IDisposable
        {
            public Camera Camera { get; }
            public ComputeBuffer Drops { get; }
            public int Capacity { get; }
            public float PendingDelta { get; set; }
            public bool Initialized { get; set; }

            public View(Camera camera, int capacity)
            {
                Camera = camera;
                Capacity = capacity;
                Drops = new ComputeBuffer(capacity, DROP_STRIDE, ComputeBufferType.Structured);
            }

            public void Dispose()
            {
                Drops.Dispose();
            }
        }
    }
}
