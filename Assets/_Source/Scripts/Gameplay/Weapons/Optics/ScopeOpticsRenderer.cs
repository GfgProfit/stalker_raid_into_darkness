using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Weapons.Optics
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class ScopeOpticsRenderer : MonoBehaviour
    {
        private static readonly ProfilerMarker UPDATE_MARKER = new ProfilerMarker("ScopeOptics.Update");
        private static readonly int TRACER_VIEWER_EXPOSURE = Shader.PropertyToID("_ScopeTracerViewerExposure");
        private static readonly int TRACER_EXPOSURE_ENABLED = Shader.PropertyToID("_ScopeTracerExposureEnabled");
        private static readonly int SCOPE_AVAILABLE = Shader.PropertyToID("_ScopeAvailable");
        private static readonly int SCOPE_PRO_STYLE = Shader.PropertyToID("_ScopeProStyle");
        private static readonly int SCOPE_PRO_TEXTURE = Shader.PropertyToID("_RenderTexture");
        private static readonly int SCOPE_PRO_RIGHT = Shader.PropertyToID("_ScopeRightWS");
        private static readonly int SCOPE_PRO_UP = Shader.PropertyToID("_ScopeUpWS");
        private static readonly int SCOPE_CALIBRATED_APERTURE = Shader.PropertyToID("_ScopeCalibratedAperture");
        private static readonly int SCOPE_LENS_CENTER = Shader.PropertyToID("_ScopeLensCenterWS");
        private static readonly int SCOPE_LENS_HALF_SIZE = Shader.PropertyToID("_ScopeLensHalfSize");
        private static readonly int SCOPE_PRO_RETICLE_SCALE = Shader.PropertyToID("_ReticleRuntimeScale");
        private static readonly int SCOPE_PRO_RENDER_SIZE = Shader.PropertyToID("_RenderSize");
        private static readonly int LENS_ENVIRONMENT = Shader.PropertyToID("_LensEnvironment");
        private static readonly int LENS_USE_ENVIRONMENT = Shader.PropertyToID("_LensUseEnvironment");
        private static readonly int SCOPE_VISIBILITY = Shader.PropertyToID("_ScopeVisibility");
        private static readonly int RETICLE_SDF = Shader.PropertyToID("_ReticleSdf");
        private static readonly int RETICLE_SDF_AVAILABLE = Shader.PropertyToID("_ReticleSdfAvailable");

        [SerializeField] private ScopeOpticsConfig _configuration;
        [SerializeField] private Camera _scopeCamera;
        [SerializeField] private HDAdditionalCameraData _scopeCameraData;
        [SerializeField] private Transform _opticalAxis;
        [SerializeField] private Renderer _lensRenderer;
        [SerializeField, Min(0)] private int _materialIndex;
        [SerializeField] private Vector2 _lensHalfSizeMeters = new Vector2(0.014f, 0.014f);
        [SerializeField] private bool _followOpticalAxisScale;
        [SerializeField, Range(128, 2048)] private int _resolution = 768;
        [SerializeField, Min(0.0f)] private float _magnificationResponse = 12.0f;
        [SerializeField] private bool _disableVolumetrics;
        [SerializeField] private bool _disableShadows;

        private readonly List<Material> _materials = new List<Material>();

        private Camera _viewerCamera;
        private ScopeOpticsState _state;
        private Camera _controlledCamera;
        private HDAdditionalCameraData _controlledCameraData;
        private ScopeOpticsCameraState _cameraState;
        private Material _lensMaterial;
        private GameObject _excludedRoot;
        private ScopeCaptureExclusion _captureExclusion;
        private Renderer _boundRenderer;
        private int _boundMaterialIndex;
        private MotionVectorGenerationMode _originalLensMotionVectorMode;
        private MaterialPropertyBlock _propertyBlock;
        private MaterialPropertyBlock _originalPropertyBlock;
        private RenderTexture _target;
        private Vector3 _eyeLocal;
        private Vector3 _projectionEyeLocal;
        private Vector2 _effectiveLensHalfSizeMeters;
        private Quaternion _opticalRotation;
        private float _lensShear;
        private float _aimVisibility = 1f;
        private float _scopeProRenderSize = 1f;
        private bool _scopeUsesCalibratedAperture;
        private ScopeLensEnvironment _lensEnvironment;
        private Scene _environmentScene;
        private string _lastDiagnostic;
        private bool _subscribed;
        private bool _isRendering;
        private bool _tracerExposureBound;
        private float _previousTracerExposureEnabled;
        private Texture _previousTracerExposureTexture;
        private CustomPass _tracerExposureStartPass;
        private CustomPass _tracerExposureEndPass;

        public ScopeOpticsConfig Configuration => _configuration;
        public ScopeOpticsState State => _state;
        public bool IsRendering => _isRendering;
        public RenderTexture Target => _target;

        private void OnEnable()
        {
            if (!Application.isPlaying || _subscribed)
            {
                return;
            }

            _tracerExposureStartPass = new TracerExposurePass(this, false);
            _tracerExposureEndPass = new TracerExposurePass(this, true);
            CustomPassVolume.RegisterGlobalCustomPass(CustomPassInjectionPoint.BeforeTransparent, _tracerExposureStartPass);
            CustomPassVolume.RegisterGlobalCustomPass(CustomPassInjectionPoint.BeforePostProcess, _tracerExposureEndPass);
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

            using (UPDATE_MARKER.Auto())
            {
                if (_controlledCamera != _scopeCamera || _controlledCameraData != _scopeCameraData)
                {
                    ReleaseResources();
                    OnEnable();
                }

                if (_scopeCamera != null && _scopeCamera != _viewerCamera && _scopeCameraData != null && _scopeCameraData.gameObject == _scopeCamera.gameObject)
                {
                    TakeCameraControl();
                }

                if (!TryValidate(out string diagnostic))
                {
                    SuspendCapture();
                    ReportDiagnostic(diagnostic);
                    return;
                }

                _lastDiagnostic = null;
                EnsureState();
                TakeCameraControl();
                _state.Tick(Time.deltaTime, _magnificationResponse, _configuration);
                if (!_state.Active || !_viewerCamera.isActiveAndEnabled || !_lensRenderer.enabled || !_lensRenderer.gameObject.activeInHierarchy)
                {
                    SuspendCapture();
                    return;
                }

                if (!UpdateLensGeometry())
                {
                    SuspendCapture();
                    ReportDiagnostic("ScopeOpticsRenderer requires a finite non-mirrored Optical Axis transform with nonzero lens dimensions.");
                    return;
                }

                if (_eyeLocal.z >= -0.001f)
                {
                    SuspendCapture();
                    return;
                }

                if (!EnsureTarget())
                {
                    SuspendCapture();
                    ReportDiagnostic("ScopeOpticsRenderer could not create its linear HDR render target.");
                    return;
                }

                ConfigureCapture(_projectionEyeLocal);
                BindSurface(false);
                _controlledCamera.enabled = true;
            }
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        public void BindViewerCamera(Camera viewer, GameObject excludedRoot = null)
        {
            if (_viewerCamera == viewer && _excludedRoot == excludedRoot)
            {
                return;
            }

            SetActive(false);
            ReleaseResources();
            _viewerCamera = viewer;
            _excludedRoot = excludedRoot;
            if (Application.isPlaying && isActiveAndEnabled)
            {
                OnEnable();
            }
        }

        public void SetAimVisibility(float visibility)
        {
            _aimVisibility = ScopeOpticsMath.FiniteClamp(visibility, 0f, 1f, 0f);
            SetActive(_aimVisibility > 0f);
        }

        public void SetActive(bool active)
        {
            EnsureState();
            if (_state != null)
            {
                _state.SetActive(active);
            }

            if (!active)
            {
                SuspendCapture();
            }
        }

        public void SetMagnification(float magnification, bool immediate = false)
        {
            EnsureState();
            if (_state != null && _configuration != null)
            {
                _state.SetMagnification(magnification, _configuration, immediate);
            }
        }

        public void SetCorrections(float windageMilliradians, float elevationMilliradians)
        {
            EnsureState();
            if (_state != null && _configuration != null)
            {
                _state.SetCorrections(windageMilliradians, elevationMilliradians, _configuration);
            }
        }

        public bool TryValidate(out string diagnostic)
        {
            EnsureCaptureExclusion();
            diagnostic = null;
            if (_configuration == null) diagnostic = "ScopeOpticsRenderer requires Configuration.";
            else if (!_configuration.TryValidate(out diagnostic)) return false;
            else if (_viewerCamera == null) diagnostic = "ScopeOpticsRenderer requires Viewer Camera.";
            else if (_scopeCamera == null) diagnostic = "ScopeOpticsRenderer requires an explicitly assigned Scope Camera.";
            else if (_scopeCamera == _viewerCamera) diagnostic = "Scope Camera and Viewer Camera must be different cameras.";
            else if (_scopeCameraData == null || _scopeCameraData.gameObject != _scopeCamera.gameObject) diagnostic = "Scope Camera Data must be the existing HDAdditionalCameraData on Scope Camera.";
            else if (_opticalAxis == null) diagnostic = "ScopeOpticsRenderer requires Optical Axis at the ocular plane, with +Z toward the target.";
            else if (_opticalAxis.IsChildOf(_scopeCamera.transform) || _viewerCamera.transform.IsChildOf(_scopeCamera.transform)) diagnostic = "Scope Camera must not be an ancestor of Optical Axis or Viewer Camera.";
            else if (_lensRenderer == null) diagnostic = "ScopeOpticsRenderer requires Lens Renderer.";
            else if ((_scopeCamera.cullingMask & (1 << _lensRenderer.gameObject.layer)) != 0 && (_captureExclusion == null || !_captureExclusion.Contains(_lensRenderer))) diagnostic = "Scope Camera must exclude the lens through its Culling Mask or explicit HUD capture exclusion.";
            else if (!float.IsFinite(_lensHalfSizeMeters.x) || !float.IsFinite(_lensHalfSizeMeters.y) || _lensHalfSizeMeters.x < 0.0001f || _lensHalfSizeMeters.y < 0.0001f || _lensHalfSizeMeters.x > 10.0f || _lensHalfSizeMeters.y > 10.0f) diagnostic = "Lens Half Size Meters must contain finite half extents between 0.0001 and 10 meters; they are local to Optical Axis when Follow Optical Axis Scale is enabled.";
            else if (!float.IsFinite(_scopeCamera.nearClipPlane) || !float.IsFinite(_scopeCamera.farClipPlane) || _scopeCamera.nearClipPlane <= 0.0f || _scopeCamera.farClipPlane <= _scopeCamera.nearClipPlane) diagnostic = "Scope Camera requires finite positive Near/Far clip planes.";
            else
            {
                _lensRenderer.GetSharedMaterials(_materials);
                if (_materialIndex < 0 || _materialIndex >= _materials.Count || _materials[_materialIndex] == null || !_materials[_materialIndex].HasProperty(SCOPE_PRO_STYLE))
                {
                    diagnostic = "Lens Renderer Material Index must select a ScopeProLens optics material.";
                }
                else
                {
                    Material material = _materials[_materialIndex];
                    _lensMaterial = material;
                    _scopeProRenderSize = ScopeOpticsMath.FiniteClamp(material.GetFloat(SCOPE_PRO_RENDER_SIZE), 0.001f, 2f, 1f);
                    _scopeUsesCalibratedAperture = material.HasProperty(SCOPE_CALIBRATED_APERTURE) && material.GetFloat(SCOPE_CALIBRATED_APERTURE) > 0.5f;
                }
            }

            return diagnostic == null;
        }

        public void ReleaseResources()
        {
            RestoreTracerExposure();
            if (_tracerExposureStartPass != null)
            {
                CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.BeforeTransparent, _tracerExposureStartPass);
                _tracerExposureStartPass = null;
            }

            if (_tracerExposureEndPass != null)
            {
                CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.BeforePostProcess, _tracerExposureEndPass);
                _tracerExposureEndPass = null;
            }

            if (_subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= EndCameraRendering;
                _subscribed = false;
            }

            _captureExclusion?.Restore();
            _captureExclusion = null;
            RestoreSurface();
            if (_controlledCamera != null)
            {
                _controlledCamera.enabled = false;
                _controlledCamera.targetTexture = null;
            }

            ReleaseTarget();
            if (_cameraState != null)
            {
                _cameraState.Restore(_controlledCamera, _controlledCameraData);
            }

            _environmentScene = default;
            _lensEnvironment = null;
            _cameraState = null;
            _controlledCamera = null;
            _controlledCameraData = null;
            _isRendering = false;
        }

        private void EnsureState()
        {
            if (_configuration == null)
            {
                return;
            }

            if (_state == null)
            {
                _state = new ScopeOpticsState(_configuration);
            }

            _state.Sanitize(_configuration);
        }

        private void TakeCameraControl()
        {
            if (_cameraState != null)
            {
                return;
            }

            _controlledCamera = _scopeCamera;
            _controlledCameraData = _scopeCameraData;
            _cameraState = new ScopeOpticsCameraState(_controlledCamera, _controlledCameraData);
            _controlledCamera.enabled = false;
        }

        private bool UpdateLensGeometry()
        {
            _effectiveLensHalfSizeMeters = _lensHalfSizeMeters;
            _opticalRotation = _opticalAxis.rotation;
            _eyeLocal = Quaternion.Inverse(_opticalRotation) * (_viewerCamera.transform.position - _opticalAxis.position);
            _projectionEyeLocal = _eyeLocal;
            _lensShear = 0f;
            if (!_followOpticalAxisScale)
            {
                return true;
            }

            if (!ScopeLensGeometry.TryCreate(_opticalAxis.localToWorldMatrix, _viewerCamera.transform.position, _lensHalfSizeMeters, out ScopeLensGeometry geometry))
            {
                return false;
            }

            _effectiveLensHalfSizeMeters = geometry.HalfSizeMeters;
            _opticalRotation = geometry.Rotation;
            _eyeLocal = geometry.EyeLocal;
            _projectionEyeLocal = geometry.ProjectionEyeLocal;
            _lensShear = geometry.Shear;
            return true;
        }

        private bool EnsureTarget()
        {
            int longestSide = Mathf.Clamp(_resolution, 128, 2048);
            float aspect = _effectiveLensHalfSizeMeters.x / _effectiveLensHalfSizeMeters.y;
            int width = aspect >= 1.0f ? longestSide : Mathf.Max(1, Mathf.RoundToInt(longestSide * aspect));
            int height = aspect >= 1.0f ? Mathf.Max(1, Mathf.RoundToInt(longestSide / aspect)) : longestSide;
            if (_target != null && _target.width == width && _target.height == height && _target.IsCreated())
            {
                return true;
            }

            ReleaseTarget();
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            _target.name = "Scope Optics Linear HDR";
            _target.hideFlags = HideFlags.HideAndDontSave;
            _target.filterMode = FilterMode.Bilinear;
            _target.wrapMode = TextureWrapMode.Clamp;
            _target.useMipMap = false;
            _target.autoGenerateMips = false;
            _target.antiAliasing = 1;
            _target.useDynamicScale = false;
            return _target.Create();
        }

        private void ConfigureCapture(Vector3 eyeLocal)
        {
            _controlledCamera.enabled = false;
            _controlledCamera.targetTexture = _target;
            _controlledCamera.allowHDR = true;
            _controlledCamera.allowMSAA = false;
            _controlledCamera.allowDynamicResolution = false;
            _controlledCamera.orthographic = false;
            _controlledCamera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            _controlledCamera.aspect = _effectiveLensHalfSizeMeters.x / _effectiveLensHalfSizeMeters.y;
            _controlledCamera.depth = _viewerCamera.depth - 1.0f;
            Quaternion correction = Quaternion.Euler(-_state.ElevationMilliradians * 0.001f * Mathf.Rad2Deg, _state.WindageMilliradians * 0.001f * Mathf.Rad2Deg, 0.0f);
            Quaternion rotation = _opticalRotation * correction;
            _controlledCamera.transform.SetPositionAndRotation(_viewerCamera.transform.position, rotation);
            if (_followOpticalAxisScale)
            {
                _controlledCamera.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(_viewerCamera.transform.position, rotation, Vector3.one).inverse;
            }
            else
            {
                _controlledCamera.ResetWorldToCameraMatrix();
            }
            Vector3 projectionEye = new Vector3(0f, 0f, eyeLocal.z);
            Matrix4x4 projection = ScopeOpticsMath.BuildProjection(_configuration, _state.Magnification, _effectiveLensHalfSizeMeters, projectionEye, _controlledCamera.nearClipPlane, _controlledCamera.farClipPlane);
            if (_configuration.ProjectionMode != ScopeProjectionMode.CalibratedFieldOfView)
            {
                Vector2 projectionScale = _scopeUsesCalibratedAperture ? Vector2.one * _scopeProRenderSize : ScopeProProjection.GetCaptureProjectionScale(_effectiveLensHalfSizeMeters, -eyeLocal.z, _scopeProRenderSize);
                projection.m00 *= projectionScale.x;
                projection.m11 *= projectionScale.y;
            }

            projection.m01 = -_lensShear * projection.m00;
            _controlledCamera.fieldOfView = 2f * Mathf.Atan(1f / projection.m11) * Mathf.Rad2Deg;
            _controlledCamera.projectionMatrix = projection;
            _controlledCamera.ResetCullingMatrix();
            _controlledCameraData.customRenderingSettings = true;
            _controlledCameraData.volumeAnchorOverride = _viewerCamera.transform;
            _controlledCameraData.allowDynamicResolution = false;
            _controlledCameraData.xrRendering = false;
            _controlledCameraData.fullscreenPassthrough = false;
            _controlledCameraData.clearDepth = true;
            _controlledCameraData.dithering = false;
            _controlledCameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            _controlledCameraData.flipYMode = HDAdditionalCameraData.FlipYMode.Automatic;
            SetFrameSetting(FrameSettingsField.CustomPass, true);
            SetFrameSetting(FrameSettingsField.Postprocess, false);
            SetFrameSetting(FrameSettingsField.ExposureControl, false);
            SetFrameSetting(FrameSettingsField.Antialiasing, false);
            if (_disableVolumetrics)
            {
                SetFrameSetting(FrameSettingsField.Volumetrics, false);
            }
            else
            {
                RestoreFrameSetting(FrameSettingsField.Volumetrics);
            }

            if (_disableShadows)
            {
                SetFrameSetting(FrameSettingsField.ShadowMaps, false);
            }
            else
            {
                RestoreFrameSetting(FrameSettingsField.ShadowMaps);
            }
        }

        private void SetFrameSetting(FrameSettingsField field, bool enabled)
        {
            _controlledCameraData.renderingPathCustomFrameSettings.SetEnabled(field, enabled);
            _controlledCameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
        }

        private void RestoreFrameSetting(FrameSettingsField field)
        {
            _controlledCameraData.renderingPathCustomFrameSettings.SetEnabled(field, _cameraState.FrameSettings.IsEnabled(field));
            _controlledCameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = _cameraState.FrameSettingsMask.mask[(uint)field];
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _controlledCamera && camera != null && camera.enabled && _state != null && _state.Active)
            {
                EnsureCaptureExclusion();
                _captureExclusion?.Hide();
            }
            else
            {
                RestoreTracerExposure();
                _captureExclusion?.Restore();
            }
        }

        private void EnsureCaptureExclusion()
        {
            if (_captureExclusion == null && _excludedRoot != null)
            {
                _captureExclusion = new ScopeCaptureExclusion(_excludedRoot);
            }
        }

        private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            RestoreTracerExposure();
            _captureExclusion?.Restore();
            if (camera == _controlledCamera && camera != null && camera.enabled && _target != null && _target.IsCreated() && _state != null && _state.Active)
            {
                BindSurface(true);
            }
        }

        private void BindTracerExposure(CommandBuffer commands)
        {
            if (_tracerExposureBound || _viewerCamera == null || !(RenderPipelineManager.currentPipeline is HDRenderPipeline))
            {
                return;
            }

            HDCamera viewer = HDCamera.GetOrCreate(_viewerCamera);
            RTHandle exposure = viewer.frameSettings.IsEnabled(FrameSettingsField.ExposureControl) ? viewer.GetPreviousFrameRT((int)HDCameraFrameHistoryType.Exposure) : null;
            _previousTracerExposureEnabled = Shader.GetGlobalFloat(TRACER_EXPOSURE_ENABLED);
            _previousTracerExposureTexture = Shader.GetGlobalTexture(TRACER_VIEWER_EXPOSURE);
            commands.SetGlobalTexture(TRACER_VIEWER_EXPOSURE, exposure != null && exposure.rt != null ? exposure.rt : Texture2D.whiteTexture);
            commands.SetGlobalFloat(TRACER_EXPOSURE_ENABLED, 1f);
            _tracerExposureBound = true;
        }

        private void RestoreTracerExposure(CommandBuffer commands = null)
        {
            if (!_tracerExposureBound)
            {
                return;
            }

            Texture previous = _previousTracerExposureTexture != null ? _previousTracerExposureTexture : Texture2D.whiteTexture;
            if (commands != null)
            {
                commands.SetGlobalFloat(TRACER_EXPOSURE_ENABLED, _previousTracerExposureEnabled);
                commands.SetGlobalTexture(TRACER_VIEWER_EXPOSURE, previous);
            }
            else
            {
                Shader.SetGlobalFloat(TRACER_EXPOSURE_ENABLED, _previousTracerExposureEnabled);
                Shader.SetGlobalTexture(TRACER_VIEWER_EXPOSURE, previous);
            }
            _previousTracerExposureTexture = null;
            _tracerExposureBound = false;
        }

        public void UpdateLensEnvironment()
        {
            UpdateLensEnvironmentInScene(SceneManager.GetActiveScene());
        }

        private void UpdateLensEnvironmentInScene(Scene scene)
        {
            if (!EnsureSurfaceBinding())
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            _boundRenderer.GetPropertyBlock(_propertyBlock, _boundMaterialIndex);
            ApplyLensEnvironment(scene);
            _boundRenderer.SetPropertyBlock(_propertyBlock, _boundMaterialIndex);
        }

        private bool EnsureSurfaceBinding()
        {
            if (_lensRenderer == null || _materialIndex < 0)
            {
                return false;
            }

            if (_boundRenderer != _lensRenderer || _boundMaterialIndex != _materialIndex)
            {
                _lensRenderer.GetSharedMaterials(_materials);
                if (_materialIndex >= _materials.Count || _materials[_materialIndex] == null || !_materials[_materialIndex].HasProperty(SCOPE_PRO_STYLE))
                {
                    return false;
                }

                RestoreSurface();
                _boundRenderer = _lensRenderer;
                _boundMaterialIndex = _materialIndex;
                _originalLensMotionVectorMode = _boundRenderer.motionVectorGenerationMode;
                _boundRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                _originalPropertyBlock ??= new MaterialPropertyBlock();
                _boundRenderer.GetPropertyBlock(_originalPropertyBlock, _boundMaterialIndex);
            }

            return true;
        }

        private void ApplyLensEnvironment(Scene scene)
        {
            if (_environmentScene != scene)
            {
                _environmentScene = scene;
                _lensEnvironment = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (ScopeLensEnvironment candidate in root.GetComponentsInChildren<ScopeLensEnvironment>(true))
                    {
                        if (_lensEnvironment != null)
                        {
                            _lensEnvironment = null;
                            Debug.LogWarning("Only one ScopeLensEnvironment is supported in the active location scene. Falling back to the HDRP sky.", this);
                            _propertyBlock.SetFloat(LENS_USE_ENVIRONMENT, 0f);
                            return;
                        }

                        _lensEnvironment = candidate;
                    }
                }
            }

            Cubemap cubemap = _lensEnvironment != null && _lensEnvironment.isActiveAndEnabled && _lensEnvironment.UseBakedEnvironment ? _lensEnvironment.Cubemap : null;
            _propertyBlock.SetFloat(LENS_USE_ENVIRONMENT, cubemap != null ? 1f : 0f);
            if (cubemap != null)
            {
                _propertyBlock.SetTexture(LENS_ENVIRONMENT, cubemap);
            }
        }

        private void BindSurface(bool available)
        {
            if (_configuration == null || _state == null || _lensRenderer == null)
            {
                _isRendering = false;
                return;
            }

            if (!EnsureSurfaceBinding())
            {
                _isRendering = false;
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            _boundRenderer.GetPropertyBlock(_propertyBlock, _boundMaterialIndex);
            _propertyBlock.SetFloat(SCOPE_AVAILABLE, available ? 1.0f : 0.0f);
            _propertyBlock.SetFloat(SCOPE_VISIBILITY, _aimVisibility);
            ApplyLensEnvironment(SceneManager.GetActiveScene());
            _propertyBlock.SetFloat(RETICLE_SDF_AVAILABLE, _lensMaterial != null && _lensMaterial.HasProperty(RETICLE_SDF) && _lensMaterial.GetTexture(RETICLE_SDF) != null ? 1f : 0f);
            _propertyBlock.SetTexture(SCOPE_PRO_TEXTURE, _target != null ? _target : Texture2D.blackTexture);
            _propertyBlock.SetVector(SCOPE_PRO_RIGHT, _opticalRotation * Vector3.right);
            _propertyBlock.SetVector(SCOPE_PRO_UP, _opticalRotation * Vector3.up);
            if (_scopeUsesCalibratedAperture && _opticalAxis != null)
            {
                _propertyBlock.SetVector(SCOPE_LENS_CENTER, _opticalAxis.position);
                _propertyBlock.SetVector(SCOPE_LENS_HALF_SIZE, new Vector4(_effectiveLensHalfSizeMeters.x, _effectiveLensHalfSizeMeters.y, 0f, 0f));
            }

            float scale = ScopeOpticsMath.GetReticleScale(_configuration, _state.Magnification, _effectiveLensHalfSizeMeters.y, Mathf.Max(0.001f, -_eyeLocal.z));
            _propertyBlock.SetFloat(SCOPE_PRO_RETICLE_SCALE, scale);
            _boundRenderer.SetPropertyBlock(_propertyBlock, _boundMaterialIndex);
            _isRendering = available;
        }

        private void RestoreSurface()
        {
            if (_boundRenderer != null)
            {
                _boundRenderer.SetPropertyBlock(_originalPropertyBlock, _boundMaterialIndex);
                _boundRenderer.motionVectorGenerationMode = _originalLensMotionVectorMode;
            }

            _boundRenderer = null;
            _isRendering = false;
        }

        private void SuspendCapture()
        {
            RestoreTracerExposure();
            _captureExclusion?.Restore();
            if (_controlledCamera != null)
            {
                _controlledCamera.enabled = false;
            }

            if (_boundRenderer != null && _propertyBlock != null)
            {
                _boundRenderer.GetPropertyBlock(_propertyBlock, _boundMaterialIndex);
                _propertyBlock.SetFloat(SCOPE_AVAILABLE, 0.0f);
                _boundRenderer.SetPropertyBlock(_propertyBlock, _boundMaterialIndex);
            }

            _isRendering = false;
        }

        private void ReleaseTarget()
        {
            if (_target == null)
            {
                return;
            }

            if (_controlledCamera != null && _controlledCamera.targetTexture == _target)
            {
                _controlledCamera.targetTexture = null;
            }

            _target.Release();
            CoreUtils.Destroy(_target);
            _target = null;
        }

        private void ReportDiagnostic(string diagnostic)
        {
            if (_lastDiagnostic == diagnostic)
            {
                return;
            }

            _lastDiagnostic = diagnostic;
            Debug.LogWarning(diagnostic, this);
        }

        private sealed class TracerExposurePass : CustomPass
        {
            private readonly ScopeOpticsRenderer _owner;
            private readonly bool _restore;

            public TracerExposurePass(ScopeOpticsRenderer owner, bool restore)
            {
                _owner = owner;
                _restore = restore;
                name = restore ? "Scope Tracer Exposure Restore" : "Scope Tracer Exposure";
                targetColorBuffer = TargetBuffer.None;
                targetDepthBuffer = TargetBuffer.None;
            }

            protected override void Execute(CustomPassContext context)
            {
                if (_owner == null || context.hdCamera.camera != _owner._controlledCamera)
                {
                    return;
                }

                if (_restore)
                {
                    _owner.RestoreTracerExposure(context.cmd);
                }
                else if (_owner._state != null && _owner._state.Active)
                {
                    _owner.BindTracerExposure(context.cmd);
                }
            }
        }
    }
}
