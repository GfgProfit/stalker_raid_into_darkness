using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class SpatialBubbleDestinationView : MonoBehaviour
    {
        public enum CaptureAntialiasing
        {
            None,
            Fxaa,
            Smaa
        }

        private const float VIEWER_RETRY_INTERVAL = 0.5f;
        private const float CAPTURE_PADDING_PIXELS = 64f;
        private const int RESOLUTION_STEP = 128;

        private static readonly int DESTINATION_TEXTURE = Shader.PropertyToID("_DestinationViewTex");
        private static readonly int DESTINATION_AVAILABLE = Shader.PropertyToID("_DestinationViewAvailable");
        private static readonly int DESTINATION_ASPECT = Shader.PropertyToID("_DestinationViewAspect");
        private static readonly int DESTINATION_PARALLAX = Shader.PropertyToID("_DestinationViewParallax");
        private static readonly int DESTINATION_WORLD_TO_CLIP = Shader.PropertyToID("_DestinationViewWorldToClip");
        private static readonly int DESTINATION_HEIGHT_OFFSET = Shader.PropertyToID("_DestinationViewHeightOffset");
        private static readonly FrameSettingsField[] DISABLED_POSTPROCESS =
        {
            FrameSettingsField.CustomPostProcess,
            FrameSettingsField.DepthOfField,
            FrameSettingsField.MotionBlur,
            FrameSettingsField.PaniniProjection,
            FrameSettingsField.Bloom,
            FrameSettingsField.LensFlareScreenSpace,
            FrameSettingsField.LensFlareDataDriven,
            FrameSettingsField.LensDistortion,
            FrameSettingsField.ChromaticAberration,
            FrameSettingsField.Vignette,
            FrameSettingsField.ColorGrading,
            FrameSettingsField.Tonemapping,
            FrameSettingsField.FilmGrain,
            FrameSettingsField.Dithering
        };

        [SerializeField] private SpatialBubbleTeleport _teleport;
        [SerializeField] private MeshRenderer _surface;
        [SerializeField] private Camera _viewerCamera;
        [SerializeField] private bool _fullParallax = true;
        [SerializeField, Range(128, 2048)] private int _resolution = 1024;
        [SerializeField] private bool _adaptiveResolution = true;
        [SerializeField, Range(128, 2048)] private int _minimumResolution = 256;
        [SerializeField] private CaptureAntialiasing _antialiasing = CaptureAntialiasing.Smaa;
        [SerializeField, Min(0.1f)] private float _fallbackEyeHeight = 1.7f;

        private readonly Plane[] _frustumPlanes = new Plane[6];
        private Camera _resolvedViewer;
        private StalkerPlayerController _viewerPlayer;
        private GameObject _cameraRoot;
        private Camera _captureCamera;
        private HDAdditionalCameraData _captureData;
        private RenderTexture _target;
        private MaterialPropertyBlock _propertyBlock;
        private MeshRenderer _boundSurface;
        private MeshRenderer _hiddenSurface;
        private Transform _lastDestination;
        private Matrix4x4 _sourceToDestination = Matrix4x4.identity;
        private Matrix4x4 _destinationViewWorldToClip = Matrix4x4.identity;
        private Rect _captureViewport = new Rect(0f, 0f, 1f, 1f);
        private CaptureAntialiasing _configuredAntialiasing;
        private float _nextViewerResolve;
        private bool _cameraConfigured;
        private bool _hasRendered;
        private bool _subscribed;
        private bool _hiddenForCapture;
        private bool _previousForceRenderingOff;
        private bool _lastFullParallax;
        private bool _captureFullParallax;
        private bool _antialiasingConfigured;

        private void Awake()
        {
            if (_teleport == null)
            {
                _teleport = GetComponent<SpatialBubbleTeleport>();
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || _subscribed)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
            _subscribed = true;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveViewer();
            Transform destination = _teleport != null ? _teleport.Destination : null;
            if (destination == null || _surface == null || !_surface.enabled || !_surface.gameObject.activeInHierarchy || _resolvedViewer == null || !_resolvedViewer.isActiveAndEnabled)
            {
                SuspendCapture();
                return;
            }

            GeometryUtility.CalculateFrustumPlanes(_resolvedViewer, _frustumPlanes);
            if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, _surface.bounds))
            {
                SuspendCapture();
                return;
            }

            EnsureCamera();
            ConfigureCamera();
            _captureViewport = _adaptiveResolution && _fullParallax ? CalculateCaptureViewport(_resolvedViewer, _surface.bounds) : new Rect(0f, 0f, 1f, 1f);
            Vector2Int previousSize = _target != null ? new Vector2Int(_target.width, _target.height) : Vector2Int.zero;
            Vector2Int size = _adaptiveResolution && _fullParallax ? CalculateAdaptiveCaptureSize(new Vector2(_resolvedViewer.pixelWidth, _resolvedViewer.pixelHeight), _captureViewport, _resolution, _minimumResolution, previousSize) : CalculateCaptureSize(_resolution, _resolvedViewer.aspect);
            EnsureTarget(size);
            if (_target == null || !_target.IsCreated())
            {
                SuspendCapture();
                return;
            }

            if (_lastDestination != destination || _lastFullParallax != _fullParallax)
            {
                _lastDestination = destination;
                _lastFullParallax = _fullParallax;
                _hasRendered = false;
            }

            UpdateCapturePose(destination);
            UpdateCaptureProjection();
            _captureCamera.targetTexture = _target;
            _captureCamera.enabled = true;
            BindSurface(_hasRendered);
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void ResolveViewer()
        {
            Camera viewer = _viewerCamera;
            if (viewer == null && _resolvedViewer != null)
            {
                viewer = _resolvedViewer;
            }

            if (viewer == null && Time.unscaledTime >= _nextViewerResolve)
            {
                viewer = Camera.main;
                _nextViewerResolve = Time.unscaledTime + VIEWER_RETRY_INTERVAL;
            }

            if (viewer == _resolvedViewer)
            {
                return;
            }

            _resolvedViewer = viewer;
            _viewerPlayer = viewer != null ? viewer.GetComponentInParent<StalkerPlayerController>() : null;
            _cameraConfigured = false;
            _hasRendered = false;
        }

        private void EnsureCamera()
        {
            if (_captureCamera != null)
            {
                return;
            }

            _cameraRoot = new GameObject("Spatial Bubble Destination Camera");
            _cameraRoot.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(_cameraRoot, gameObject.scene);
            _captureCamera = _cameraRoot.AddComponent<Camera>();
            _captureCamera.enabled = false;
            _captureData = _cameraRoot.AddComponent<HDAdditionalCameraData>();
            _cameraConfigured = false;
            _antialiasingConfigured = false;
        }

        private void ConfigureCamera()
        {
            if (_cameraConfigured)
            {
                ConfigureAntialiasing(_captureData);
                return;
            }

            _captureCamera.CopyFrom(_resolvedViewer);
            _captureCamera.enabled = false;
            _captureCamera.targetTexture = null;
            _captureCamera.rect = new Rect(0f, 0f, 1f, 1f);
            _captureCamera.allowHDR = false;
            _captureCamera.allowMSAA = false;
            _captureCamera.allowDynamicResolution = false;
            HDAdditionalCameraData data = _captureData;
            if (_resolvedViewer.TryGetComponent(out HDAdditionalCameraData viewerData))
            {
                viewerData.CopyTo(data);
            }

            data.volumeAnchorOverride = _captureCamera.transform;
            data.xrRendering = false;
            data.fullscreenPassthrough = false;
            data.allowDynamicResolution = false;
            data.clearDepth = true;
            data.dithering = false;
            data.flipYMode = HDAdditionalCameraData.FlipYMode.Automatic;
            data.customRenderingSettings = true;
            SetFrameSetting(data, FrameSettingsField.Postprocess, true);
            SetFrameSetting(data, FrameSettingsField.ExposureControl, true);
            foreach (FrameSettingsField field in DISABLED_POSTPROCESS)
            {
                SetFrameSetting(data, field, false);
            }

            _antialiasingConfigured = false;
            ConfigureAntialiasing(data);
            _cameraConfigured = true;
        }

        private void ConfigureAntialiasing(HDAdditionalCameraData data)
        {
            if (_antialiasingConfigured && _configuredAntialiasing == _antialiasing)
            {
                return;
            }

            data.antialiasing = _antialiasing == CaptureAntialiasing.None ? HDAdditionalCameraData.AntialiasingMode.None : _antialiasing == CaptureAntialiasing.Fxaa ? HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing : HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.High;
            SetFrameSetting(data, FrameSettingsField.Antialiasing, data.antialiasing != HDAdditionalCameraData.AntialiasingMode.None);
            _configuredAntialiasing = _antialiasing;
            _antialiasingConfigured = true;
            _hasRendered = false;
        }

        private void EnsureTarget(Vector2Int size)
        {
            if (_target != null && _target.width == size.x && _target.height == size.y)
            {
                return;
            }

            ReleaseTarget();
            _target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            _target.name = "Spatial Bubble Destination View";
            _target.hideFlags = HideFlags.HideAndDontSave;
            _target.filterMode = FilterMode.Bilinear;
            _target.wrapMode = TextureWrapMode.Clamp;
            _target.useMipMap = false;
            _target.autoGenerateMips = false;
            _target.Create();
            _hasRendered = false;
        }

        private void UpdateCapturePose(Transform destination)
        {
            _captureFullParallax = _fullParallax && _resolvedViewer != null;
            if (_captureFullParallax)
            {
                Quaternion rotation = Quaternion.Euler(0f, Mathf.DeltaAngle(transform.eulerAngles.y, destination.eulerAngles.y), 0f);
                Vector3 origin = destination.position + Vector3.up * ResolveDestinationHeightOffset();
                _sourceToDestination = Matrix4x4.TRS(origin, rotation, Vector3.one) * Matrix4x4.Translate(-transform.position);
                _captureCamera.transform.SetPositionAndRotation(_sourceToDestination.MultiplyPoint3x4(_resolvedViewer.transform.position), rotation * _resolvedViewer.transform.rotation);
                _captureCamera.ResetWorldToCameraMatrix();
                return;
            }

            _sourceToDestination = Matrix4x4.identity;
            float eyeHeight = Mathf.Max(0.1f, _fallbackEyeHeight);
            if (_viewerPlayer != null && _viewerPlayer.Motor != null && _viewerPlayer.Motor.Stance != null && _viewerPlayer.Motor.Stance.CameraHeight > 0f)
            {
                eyeHeight = _viewerPlayer.Motor.Stance.CameraHeight;
            }

            float yaw = destination.eulerAngles.y;
            if (_resolvedViewer != null)
            {
                yaw += Mathf.DeltaAngle(transform.eulerAngles.y, _resolvedViewer.transform.eulerAngles.y);
            }

            _captureCamera.transform.SetPositionAndRotation(destination.position + Vector3.up * eyeHeight, Quaternion.Euler(0f, yaw, 0f));
            _captureCamera.ResetWorldToCameraMatrix();
        }

        private float ResolveDestinationHeightOffset()
        {
            Material material = _surface != null ? _surface.sharedMaterial : null;
            if (material == null || !material.HasProperty(DESTINATION_HEIGHT_OFFSET))
            {
                return 0f;
            }

            float offset = material.GetFloat(DESTINATION_HEIGHT_OFFSET);
            return float.IsNaN(offset) || float.IsInfinity(offset) ? 0f : offset;
        }

        private void UpdateCaptureProjection()
        {
            _captureCamera.fieldOfView = _resolvedViewer.fieldOfView;
            _captureCamera.aspect = _resolvedViewer.aspect;
            _captureCamera.nearClipPlane = _resolvedViewer.nearClipPlane;
            _captureCamera.farClipPlane = _resolvedViewer.farClipPlane;
            _captureCamera.depth = _resolvedViewer.depth - 1f;
            _captureCamera.projectionMatrix = ApplyProjectionCrop(_resolvedViewer.projectionMatrix, _captureViewport);
            _captureCamera.ResetCullingMatrix();
        }

        private void UpdateDestinationProjection()
        {
            _destinationViewWorldToClip = _captureFullParallax ? GL.GetGPUProjectionMatrix(_captureCamera.projectionMatrix, true) * _captureCamera.worldToCameraMatrix * _sourceToDestination : Matrix4x4.identity;
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _captureCamera)
            {
                RestoreSurface();
                return;
            }
            if (_surface == null || _hiddenForCapture)
            {
                return;
            }

            _hiddenSurface = _surface;
            _previousForceRenderingOff = _surface.forceRenderingOff;
            _surface.forceRenderingOff = true;
            _hiddenForCapture = true;
        }

        private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _captureCamera)
            {
                return;
            }

            RestoreSurface();
            if (_captureCamera != null && _captureCamera.enabled && _target != null && _target.IsCreated())
            {
                UpdateDestinationProjection();
                _hasRendered = true;
                BindSurface(true);
            }
        }

        private void RestoreSurface()
        {
            if (_hiddenForCapture && _hiddenSurface != null)
            {
                _hiddenSurface.forceRenderingOff = _previousForceRenderingOff;
            }

            _hiddenSurface = null;
            _hiddenForCapture = false;
        }

        private void BindSurface(bool available)
        {
            if (_boundSurface != _surface)
            {
                ClearSurface();
                _boundSurface = _surface;
            }

            if (_boundSurface == null)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            _boundSurface.GetPropertyBlock(_propertyBlock);
            bool hasTarget = _target != null && _target.IsCreated();
            _propertyBlock.SetTexture(DESTINATION_TEXTURE, hasTarget ? _target : Texture2D.blackTexture);
            _propertyBlock.SetFloat(DESTINATION_AVAILABLE, available && hasTarget ? 1f : 0f);
            _propertyBlock.SetFloat(DESTINATION_ASPECT, _target != null ? (float)_target.width / _target.height : 1f);
            _propertyBlock.SetFloat(DESTINATION_PARALLAX, available && hasTarget && _captureFullParallax ? 1f : 0f);
            _propertyBlock.SetMatrix(DESTINATION_WORLD_TO_CLIP, available && hasTarget && _captureFullParallax ? _destinationViewWorldToClip : Matrix4x4.identity);
            _boundSurface.SetPropertyBlock(_propertyBlock);
        }

        private void ClearSurface()
        {
            if (_boundSurface != null)
            {
                _propertyBlock ??= new MaterialPropertyBlock();
                _boundSurface.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetTexture(DESTINATION_TEXTURE, Texture2D.blackTexture);
                _propertyBlock.SetFloat(DESTINATION_AVAILABLE, 0f);
                _propertyBlock.SetFloat(DESTINATION_ASPECT, 1f);
                _propertyBlock.SetFloat(DESTINATION_PARALLAX, 0f);
                _propertyBlock.SetMatrix(DESTINATION_WORLD_TO_CLIP, Matrix4x4.identity);
                _boundSurface.SetPropertyBlock(_propertyBlock);
            }

            _boundSurface = null;
        }

        private void SuspendCapture()
        {
            RestoreSurface();
            if (_captureCamera != null)
            {
                _captureCamera.enabled = false;
            }

            _hasRendered = false;
            BindSurface(false);
        }

        private void ReleaseTarget()
        {
            if (_captureCamera != null)
            {
                _captureCamera.targetTexture = null;
            }

            if (_target != null)
            {
                _target.Release();
                CoreUtils.Destroy(_target);
                _target = null;
            }
        }

        private void ReleaseResources()
        {
            RestoreSurface();
            if (_subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= EndCameraRendering;
                _subscribed = false;
            }

            ClearSurface();
            if (_captureCamera != null)
            {
                _captureCamera.enabled = false;
            }

            ReleaseTarget();
            CoreUtils.Destroy(_cameraRoot);
            _cameraRoot = null;
            _captureCamera = null;
            _captureData = null;
            _resolvedViewer = null;
            _viewerPlayer = null;
            _lastDestination = null;
            _cameraConfigured = false;
            _hasRendered = false;
            _lastFullParallax = false;
            _captureFullParallax = false;
            _antialiasingConfigured = false;
            _captureViewport = new Rect(0f, 0f, 1f, 1f);
            _sourceToDestination = Matrix4x4.identity;
            _destinationViewWorldToClip = Matrix4x4.identity;
        }

        private static Vector2Int CalculateCaptureSize(int resolution, float aspect)
        {
            int longestSide = Mathf.Clamp(resolution, 128, 2048);
            aspect = float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect <= 0f ? 1f : aspect;
            return aspect >= 1f ? new Vector2Int(longestSide, Mathf.Max(1, Mathf.RoundToInt(longestSide / aspect))) : new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(longestSide * aspect)), longestSide);
        }

        private static Rect CalculateCaptureViewport(Camera viewer, Bounds bounds)
        {
            if (bounds.Contains(viewer.transform.position))
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            Vector2 minimum = Vector2.one;
            Vector2 maximum = Vector2.zero;
            for (int index = 0; index < 8; index++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f));
                Vector3 projected = viewer.WorldToViewportPoint(corner);
                if (projected.z <= viewer.nearClipPlane)
                {
                    return new Rect(0f, 0f, 1f, 1f);
                }
                minimum = Vector2.Min(minimum, new Vector2(projected.x, projected.y));
                maximum = Vector2.Max(maximum, new Vector2(projected.x, projected.y));
            }
            float paddingX = CAPTURE_PADDING_PIXELS / Mathf.Max(1, viewer.pixelWidth);
            float paddingY = CAPTURE_PADDING_PIXELS / Mathf.Max(1, viewer.pixelHeight);
            return Rect.MinMaxRect(Mathf.Clamp01(minimum.x - paddingX), Mathf.Clamp01(minimum.y - paddingY), Mathf.Clamp01(maximum.x + paddingX), Mathf.Clamp01(maximum.y + paddingY));
        }

        private static Vector2Int CalculateAdaptiveCaptureSize(Vector2 screenSize, Rect viewport, int resolution, int minimumResolution, Vector2Int previousSize)
        {
            int maximum = Mathf.Clamp(resolution, 128, 2048);
            int minimum = Mathf.Clamp(minimumResolution, 128, maximum);
            float width = float.IsNaN(screenSize.x) || float.IsInfinity(screenSize.x) ? 1f : Mathf.Max(1f, screenSize.x);
            float height = float.IsNaN(screenSize.y) || float.IsInfinity(screenSize.y) ? 1f : Mathf.Max(1f, screenSize.y);
            int requestedWidth = Mathf.CeilToInt(width * Mathf.Clamp01(viewport.width));
            int requestedHeight = Mathf.CeilToInt(height * Mathf.Clamp01(viewport.height));
            return new Vector2Int(SelectAdaptiveSide(requestedWidth, minimum, maximum, previousSize.x), SelectAdaptiveSide(requestedHeight, minimum, maximum, previousSize.y));
        }

        private static int SelectAdaptiveSide(int requested, int minimum, int maximum, int previous)
        {
            requested = Mathf.Clamp(requested, minimum, maximum);
            if (previous >= minimum && previous <= maximum && requested <= previous * 1.15f && requested >= previous * 0.75f)
            {
                return previous;
            }
            return Mathf.Clamp(((requested + RESOLUTION_STEP - 1) / RESOLUTION_STEP) * RESOLUTION_STEP, minimum, maximum);
        }

        private static Matrix4x4 ApplyProjectionCrop(Matrix4x4 projection, Rect viewport)
        {
            float width = Mathf.Max(0.0001f, viewport.width);
            float height = Mathf.Max(0.0001f, viewport.height);
            Matrix4x4 crop = Matrix4x4.identity;
            crop.m00 = 1f / width;
            crop.m11 = 1f / height;
            crop.m03 = (1f - 2f * viewport.center.x) / width;
            crop.m13 = (1f - 2f * viewport.center.y) / height;
            return crop * projection;
        }

        private static void SetFrameSetting(HDAdditionalCameraData data, FrameSettingsField field, bool enabled)
        {
            data.renderingPathCustomFrameSettings.SetEnabled(field, enabled);
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
        }
    }
}
