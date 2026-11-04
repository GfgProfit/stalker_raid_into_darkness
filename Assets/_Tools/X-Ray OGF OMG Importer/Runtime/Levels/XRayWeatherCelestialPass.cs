using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    [Serializable]
    public sealed class XRayWeatherCelestialPass : CustomPass
    {
        private const float MIN_FORWARD_COSINE = 0.01f;
        private const float SOURCE_DEPTH = 0.75f;
        private const float OCCLUSION_ANGLE = 0.02f;
        private const float MIN_SHAFTS_INTENSITY = 0.001f;
        private const float EXPRESSIVE_SHAFTS_GAIN = 2.5f;
        private const float OPTICAL_HALF_EXTENT = 0.64f;
        private const float OPTICAL_EDGE_TAPER = 5.0f;
        private const int MASK_PASS = 0;
        private const int BLUR_PASS = 1;
        private const int SHAFTS_PASS = 2;
        private const int SPRITE_PASS = 3;
        private const int GEOMETRY_PASS = 4;
        private const int DISK_MODE = 0;
        private const int GHOST_MODE = 1;
        private const int GRADIENT_MODE = 2;
        private const int OPTICAL_MODE = 3;

        private static readonly int _sourceId = Shader.PropertyToID("_XRayCelestialSource");
        private static readonly int _occlusionId = Shader.PropertyToID("_XRayCelestialOcclusion");
        private static readonly int _viewportId = Shader.PropertyToID("_XRayCelestialViewport");
        private static readonly int _colorId = Shader.PropertyToID("_XRayCelestialColor");
        private static readonly int _inputId = Shader.PropertyToID("_XRayCelestialInput");
        private static readonly int _inputScaleId = Shader.PropertyToID("_XRayCelestialInputScale");
        private static readonly int _sceneColorId = Shader.PropertyToID("_XRayCelestialSceneColor");
        private static readonly int _sceneScaleId = Shader.PropertyToID("_XRayCelestialSceneScale");
        private static readonly int _blurSizeId = Shader.PropertyToID("_XRayCelestialBlurSize");
        private static readonly int _gainId = Shader.PropertyToID("_XRayCelestialGain");
        private static readonly int _spriteId = Shader.PropertyToID("_XRayCelestialSprite");
        private static readonly int _spriteRectId = Shader.PropertyToID("_XRayCelestialSpriteRect");
        private static readonly int _spriteModeId = Shader.PropertyToID("_XRayCelestialSpriteMode");
        private static readonly int _spriteFadeId = Shader.PropertyToID("_XRayCelestialSpriteFade");
        private static readonly int _spriteContributionId = Shader.PropertyToID("_XRayCelestialSpriteContribution");

        [SerializeField] private Shader _shader;

        private GameObject _sceneOwner;
        private Vector3 _direction;
        private Color _sourceColor;
        private float _shaftsIntensity;
        private bool _flareEnabled;
        private bool _expressiveDaylight;
        private XRayWeatherFlareDefinition _definition;
        private XRayWeatherFlareDefinition _expressiveDefinition;
        private float _flareFade;
        private float _flareIntensity;
        private float _untintedBrightness;
        private float _expressiveIntensity;
        private Material _material;
        private RTHandle _maskA;
        private RTHandle _maskB;
        private RTHandle _sceneColor;
        private RTHandle _geometrySample;
        private bool _geometryRequested;
        private bool _geometryPending;
        private int _geometryGeneration;

        public Camera LastCamera { get; private set; }
        public Vector2 LastSourceUv { get; private set; }
        public int RenderCount { get; private set; }
        public bool LastShaftsDrawn { get; private set; }
        public int LastFlareDraws { get; private set; }
        public float? GeometryVisibility { get; private set; }
        public string GeometryCamera { get; private set; }

        public void Configure(Shader shader)
        {
            if (_shader != shader && _material != null)
            {
                CoreUtils.Destroy(_material);
                _material = null;
            }
            _shader = shader;
            name = "X-Ray Weather Celestial";
            targetColorBuffer = TargetBuffer.Camera;
            targetDepthBuffer = TargetBuffer.None;
        }

        public void SetFrame(GameObject owner, Vector3 directionToSource, float shaftsIntensity, bool flareEnabled, Color encodedSourceColor, XRayWeatherFlareDefinition definition, float flareFade = 1.0f, bool expressiveDaylight = false, float flareIntensity = 1.0f, float untintedBrightness = 1.0f, float expressiveIntensity = 1.0f, XRayWeatherFlareDefinition expressiveDefinition = null)
        {
            _sceneOwner = owner;
            _direction = Finite(directionToSource) ? directionToSource.normalized : Vector3.zero;
            _sourceColor = new Color(Nonnegative(encodedSourceColor.r), Nonnegative(encodedSourceColor.g), Nonnegative(encodedSourceColor.b), 1.0f);
            _shaftsIntensity = Mathf.Clamp01(Nonnegative(shaftsIntensity));
            _flareEnabled = flareEnabled;
            _expressiveDaylight = expressiveDaylight;
            _definition = definition;
            _expressiveDefinition = expressiveDefinition;
            _flareFade = Mathf.Clamp01(Nonnegative(flareFade));
            _flareIntensity = Nonnegative(flareIntensity);
            _untintedBrightness = Mathf.Clamp01(Nonnegative(untintedBrightness));
            _expressiveIntensity = Nonnegative(expressiveIntensity);
            enabled = owner != null && _direction != Vector3.zero && _sourceColor.maxColorComponent > 0.0f && (_shaftsIntensity > MIN_SHAFTS_INTENSITY || _flareEnabled && _definition != null && _flareFade > 0.0f && _flareIntensity > 0.0f);
        }

        public bool RequestGeometrySample()
        {
            if (!enabled || !SystemInfo.supportsAsyncGPUReadback || _geometryRequested || _geometryPending) return false;
            _geometryRequested = true;
            return true;
        }

        public void ClearFrame()
        {
            enabled = false;
            _sceneOwner = null;
            _direction = Vector3.zero;
            _sourceColor = Color.black;
            _shaftsIntensity = 0.0f;
            _flareEnabled = false;
            _expressiveDaylight = false;
            _definition = null;
            _expressiveDefinition = null;
            _flareFade = 0.0f;
            _flareIntensity = 0.0f;
            LastCamera = null;
            LastSourceUv = default;
            LastShaftsDrawn = false;
            LastFlareDraws = 0;
            ClearGeometrySample();
        }

        protected override void Execute(CustomPassContext context)
        {
            Camera camera = context.hdCamera.camera;
            if (!CanRender(camera) || _shader == null || !_shader.isSupported || context.cameraColorBuffer == null || context.cameraColorBuffer.rt == null) return;
            LastCamera = camera;
            LastSourceUv = default;
            LastShaftsDrawn = false;
            LastFlareDraws = 0;
            RenderCount++;
            if (!TryProject(context.hdCamera, out Vector2 sourceUv, out Vector2 radiusScale, out Vector4 occlusion, out float forward)) return;
            LastSourceUv = sourceUv;
            Vector2Int viewport = Viewport(context.cameraColorBuffer);
            float aspect = 1.333f * viewport.y / viewport.x;
            float x = Mathf.Max(0.0f, Mathf.Max(-sourceUv.x, sourceUv.x - 1.0f));
            float y = Mathf.Max(0.0f, Mathf.Max(-sourceUv.y, sourceUv.y - 1.0f)) * aspect;
            bool shafts = _shaftsIntensity > MIN_SHAFTS_INTENSITY && x * x + y * y < 1.0f;
            bool flare = _flareEnabled && _definition != null && _flareFade > 0.0f && _flareIntensity > 0.0f;
            if (!shafts && !flare) return;
            if (_material == null) _material = CoreUtils.CreateEngineMaterial(_shader);
            MaterialPropertyBlock properties = context.propertyBlock;
            properties.Clear();
            properties.SetVector(_sourceId, new Vector4(sourceUv.x, sourceUv.y, forward, aspect));
            properties.SetVector(_viewportId, new Vector4(viewport.x, viewport.y, 1.0f / viewport.x, 1.0f / viewport.y));
            properties.SetVector(_occlusionId, occlusion);
            if (_geometryRequested) CaptureGeometry(context, camera);
            if (flare) DrawSprite(context, _definition.Source, DISK_MODE, sourceUv, radiusScale);
            if (shafts) DrawShafts(context);
            if (!flare) return;
            float expressive = _expressiveDaylight && _definition.Source.Texture != null && !string.IsNullOrEmpty(_definition.Name) && !_definition.Name.StartsWith("moon", StringComparison.OrdinalIgnoreCase) ? _expressiveIntensity : 0.0f;
            float regular = 1.0f - Mathf.Clamp01(expressive);
            if (expressive > 0.0f)
            {
                DrawOptics(context, sourceUv, expressive);
                XRayWeatherFlareDefinition styled = _expressiveDefinition ?? _definition;
                for (int index = 0; index < styled.Elements.Count; index++) DrawSprite(context, styled.Elements[index], GHOST_MODE, sourceUv, radiusScale, true, expressive);
            }
            if (regular > 0.0f)
            {
                for (int index = 0; index < _definition.Elements.Count; index++) DrawSprite(context, _definition.Elements[index], GHOST_MODE, sourceUv, radiusScale, false, regular);
            }
            DrawSprite(context, _definition.Gradient, GRADIENT_MODE, sourceUv, radiusScale);
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_material);
            _maskA?.Release();
            _maskB?.Release();
            _sceneColor?.Release();
            _geometrySample?.Release();
            _material = null;
            _maskA = null;
            _maskB = null;
            _sceneColor = null;
            _geometrySample = null;
            ClearGeometrySample();
        }

        private void CaptureGeometry(CustomPassContext context, Camera camera)
        {
            _geometryRequested = false;
            _geometryPending = true;
            if (_geometrySample == null) _geometrySample = RTHandles.Alloc(1, 1, colorFormat: GraphicsFormat.R32_SFloat, name: "X-Ray Flare Geometry Sample");
            int generation = _geometryGeneration;
            string cameraName = camera.name;
            CoreUtils.SetRenderTarget(context.cmd, _geometrySample);
            CoreUtils.DrawFullScreen(context.cmd, _material, context.propertyBlock, GEOMETRY_PASS);
            context.cmd.RequestAsyncReadback(_geometrySample.rt, request =>
            {
                if (generation != _geometryGeneration) return;
                _geometryPending = false;
                if (request.hasError) return;
                GeometryVisibility = request.GetData<float>()[0];
                GeometryCamera = cameraName;
            });
            CoreUtils.SetRenderTarget(context.cmd, context.cameraColorBuffer);
        }

        private void ClearGeometrySample()
        {
            _geometryGeneration++;
            _geometryRequested = false;
            _geometryPending = false;
            GeometryVisibility = null;
            GeometryCamera = null;
        }

        private bool CanRender(Camera camera)
        {
            if (!enabled || _sceneOwner == null || !_sceneOwner.activeInHierarchy || camera == null) return false;
            Scene scene = _sceneOwner.scene;
            if (!scene.IsValid() || !scene.isLoaded) return false;
            if (camera.cameraType == CameraType.SceneView) return SceneManager.GetActiveScene() == scene;
            if (camera.cameraType != CameraType.Game) return false;
            Scene cameraScene = camera.scene;
            return (cameraScene.IsValid() ? cameraScene : camera.gameObject.scene) == scene;
        }

        private bool TryProject(HDCamera camera, out Vector2 sourceUv, out Vector2 radiusScale, out Vector4 occlusion, out float forward)
        {
            sourceUv = default;
            radiusScale = default;
            occlusion = default;
            HDCamera.ViewConstants view = camera.mainViewConstants;
            Vector3 direction = view.viewMatrix.MultiplyVector(_direction);
            forward = -direction.z;
            if (!(forward > MIN_FORWARD_COSINE)) return false;
            float depth = camera.camera.farClipPlane * SOURCE_DEPTH;
            if (!float.IsFinite(depth) || depth <= 0.0f) return false;
            Vector3 atDepth = direction * (depth / forward);
            Matrix4x4 projection = view.nonJitteredProjMatrix;
            if (!TryProjectUv(projection, atDepth, out sourceUv)) return false;
            float scale = camera.camera.orthographic ? depth : 1.0f;
            radiusScale = new Vector2(Mathf.Abs(projection.m00), Mathf.Abs(projection.m11)) * (scale * 0.5f);
            Vector4 frameJitter = camera.taaJitter;
            Vector2 jitter = new Vector2(-frameJitter.z, -frameJitter.w);
            Vector2 spread = radiusScale * (OCCLUSION_ANGLE / forward);
            occlusion = new Vector4(jitter.x, jitter.y, spread.x, spread.y);
            return Finite(radiusScale) && Finite(spread) && Finite(jitter);
        }

        private static bool TryProjectUv(Matrix4x4 projection, Vector3 position, out Vector2 uv)
        {
            Vector4 clip = projection * new Vector4(position.x, position.y, position.z, 1.0f);
            uv = default;
            if (!(clip.w > 0.000001f) || !float.IsFinite(clip.w)) return false;
            float y = SystemInfo.graphicsUVStartsAtTop ? -clip.y : clip.y;
            uv = new Vector2(clip.x, y) * (0.5f / clip.w) + Vector2.one * 0.5f;
            return Finite(uv);
        }

        private void DrawShafts(CustomPassContext context)
        {
            if (_maskA == null) _maskA = RTHandles.Alloc(HalfSize, GraphicsFormat.R8_UNorm, TextureXR.slices, FilterMode.Bilinear, TextureWrapMode.Clamp, TextureXR.dimension, useDynamicScale: true, name: "X-Ray Shafts Mask A");
            if (_maskB == null) _maskB = RTHandles.Alloc(HalfSize, GraphicsFormat.R8_UNorm, TextureXR.slices, FilterMode.Bilinear, TextureWrapMode.Clamp, TextureXR.dimension, useDynamicScale: true, name: "X-Ray Shafts Mask B");
            if (_sceneColor == null) _sceneColor = RTHandles.Alloc(Vector2.one, GraphicsFormat.B10G11R11_UFloatPack32, TextureXR.slices, FilterMode.Point, TextureWrapMode.Clamp, TextureXR.dimension, useDynamicScale: true, name: "X-Ray Shafts Scene Color");
            CoreUtils.SetRenderTarget(context.cmd, _maskA);
            CoreUtils.DrawFullScreen(context.cmd, _material, context.propertyBlock, MASK_PASS);
            Blur(context, _maskA, _maskB, 1.0f);
            Blur(context, _maskB, _maskA, 0.7f);
            Blur(context, _maskA, _maskB, 0.3f);
            Blitter.BlitCameraTexture(context.cmd, context.cameraColorBuffer, _sceneColor);
            MaterialPropertyBlock properties = context.propertyBlock;
            BindInput(properties, _maskB);
            properties.SetTexture(_sceneColorId, _sceneColor.rt);
            properties.SetVector(_sceneScaleId, ScaleAndBounds(_sceneColor));
            properties.SetVector(_colorId, _sourceColor);
            float gain = _shaftsIntensity * (_shaftsIntensity < 1.0f ? 2.0f * _shaftsIntensity : 1.0f) * 10.0f;
            if (_expressiveDaylight) gain *= EXPRESSIVE_SHAFTS_GAIN;
            properties.SetFloat(_gainId, gain);
            CoreUtils.SetRenderTarget(context.cmd, context.cameraColorBuffer);
            CoreUtils.DrawFullScreen(context.cmd, _material, properties, SHAFTS_PASS);
            LastShaftsDrawn = true;
        }

        private void Blur(CustomPassContext context, RTHandle source, RTHandle target, float sampleSize)
        {
            BindInput(context.propertyBlock, source);
            context.propertyBlock.SetFloat(_blurSizeId, sampleSize);
            CoreUtils.SetRenderTarget(context.cmd, target);
            CoreUtils.DrawFullScreen(context.cmd, _material, context.propertyBlock, BLUR_PASS);
        }

        private void DrawOptics(CustomPassContext context, Vector2 sourceUv, float layerIntensity)
        {
            float outside = Mathf.Max(Mathf.Abs(sourceUv.x - 0.5f), Mathf.Abs(sourceUv.y - 0.5f)) - 0.5f;
            float fade = _flareFade * (1.0f - Mathf.Clamp01(outside * OPTICAL_EDGE_TAPER));
            if (!(fade > 0.0f)) return;
            MaterialPropertyBlock properties = context.propertyBlock;
            properties.SetVector(_spriteRectId, new Vector4(sourceUv.x, sourceUv.y, OPTICAL_HALF_EXTENT, OPTICAL_HALF_EXTENT));
            properties.SetInt(_spriteModeId, OPTICAL_MODE);
            properties.SetVector(_colorId, _sourceColor);
            properties.SetVector(_spriteFadeId, new Vector4(fade, 1.0f, 0.0f, 0.0f));
            properties.SetFloat(_spriteContributionId, _flareIntensity * layerIntensity);
            context.cmd.DrawProcedural(Matrix4x4.identity, _material, SPRITE_PASS, MeshTopology.Triangles, 6, 1, properties);
            LastFlareDraws++;
        }

        private void DrawSprite(CustomPassContext context, XRayWeatherFlareElement element, int mode, Vector2 sourceUv, Vector2 radiusScale, bool expressiveGhost = false, float layerIntensity = 1.0f)
        {
            if (element.Texture == null || !(element.Radius > 0.0f) || !float.IsFinite(element.Radius)) return;
            float opacity = mode == DISK_MODE ? 1.0f : Mathf.Clamp01(Nonnegative(element.Opacity));
            float contribution = _flareIntensity * layerIntensity * (element.IgnoreColor ? _untintedBrightness : 1.0f);
            if (!(contribution > 0.0f)) return;
            if (!(opacity > 0.0f) || mode == GHOST_MODE && !float.IsFinite(element.Position)) return;
            Vector2 center = mode == GHOST_MODE ? Vector2.LerpUnclamped(Vector2.one * 0.5f, sourceUv, element.Position) : sourceUv;
            Vector2 halfSize = radiusScale * element.Radius;
            if (!Finite(center) || !Finite(halfSize)) return;
            Vector2 bounds = mode == GHOST_MODE ? halfSize * 1.414214f : halfSize;
            if (mode != GRADIENT_MODE && (center.x + bounds.x < 0.0f || center.y + bounds.y < 0.0f || center.x - bounds.x > 1.0f || center.y - bounds.y > 1.0f)) return;
            MaterialPropertyBlock properties = context.propertyBlock;
            properties.SetTexture(_spriteId, element.Texture);
            properties.SetVector(_spriteRectId, new Vector4(center.x, center.y, halfSize.x, halfSize.y));
            properties.SetInt(_spriteModeId, mode);
            properties.SetVector(_colorId, element.IgnoreColor ? Color.white : _sourceColor);
            properties.SetVector(_spriteFadeId, new Vector4(_flareFade, opacity, expressiveGhost ? 1.0f : 0.0f, 0.0f));
            properties.SetFloat(_spriteContributionId, contribution);
            context.cmd.DrawProcedural(Matrix4x4.identity, _material, SPRITE_PASS, MeshTopology.Triangles, 6, 1, properties);
            LastFlareDraws++;
        }

        private static void BindInput(MaterialPropertyBlock properties, RTHandle source)
        {
            properties.SetTexture(_inputId, source.rt);
            properties.SetVector(_inputScaleId, ScaleAndBounds(source));
        }

        private static Vector2Int Viewport(RTHandle handle)
        {
            Vector2Int size = handle.useScaling ? handle.GetScaledSize(handle.rtHandleProperties.currentViewportSize) : new Vector2Int(handle.rt.width, handle.rt.height);
            return new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        }

        private static Vector2Int HalfSize(Vector2Int size)
        {
            return new Vector2Int(Mathf.Max(1, (size.x + 1) / 2), Mathf.Max(1, (size.y + 1) / 2));
        }

        private static Vector4 ScaleAndBounds(RTHandle handle)
        {
            Vector2Int size = Viewport(handle);
            float x = 1.0f / handle.rt.width;
            float y = 1.0f / handle.rt.height;
            return new Vector4(size.x * x, size.y * y, 0.5f * x, 0.5f * y);
        }

        private static float Nonnegative(float value)
        {
            return float.IsFinite(value) ? Mathf.Max(0.0f, value) : 0.0f;
        }

        private static bool Finite(Vector2 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y);
        }

        private static bool Finite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
