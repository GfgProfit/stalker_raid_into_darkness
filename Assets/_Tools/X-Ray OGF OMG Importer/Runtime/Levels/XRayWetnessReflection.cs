using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public sealed class XRayWetnessReflection : IDisposable
    {
        private const float CAPTURE_MOVE_DISTANCE = 8.0f;
        private const float CAPTURE_RADIUS = 48.0f;

        private readonly GameObject _owner;

        private PrecipitationConfig _settings;
        private HDAdditionalReflectionData _probe;
        private Camera _camera;
        private float _elapsed;
        private uint _captureVersion;
        private int _resolution;
        private bool _pending;

        public HDAdditionalReflectionData Probe => _probe;
        public int CaptureRequests { get; private set; }
        public long EstimatedBytes => _probe != null && _probe.realtimeTexture != null ? (long)_resolution * _resolution * 6L * 8L * 4L / 3L : 0L;

        public XRayWetnessReflection(GameObject owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _owner = owner;
        }

        public void Tick(PrecipitationConfig settings, float realDelta)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!float.IsFinite(realDelta) || realDelta < 0.0f) throw new ArgumentOutOfRangeException(nameof(realDelta));
            if (!settings.NonCameraReflectionsEnabled || settings.ReflectionIntensity <= 0.0f || !settings.WetnessEnabled && !settings.PuddlesEnabled)
            {
                Dispose();
                return;
            }
            int resolution = settings.Quality == XRayPrecipitationQuality.Low ? 64 : 128;
            if (_probe != null && _resolution != resolution) Dispose();
            _settings = settings;
            _elapsed += realDelta;
            if (_probe == null) Create(resolution);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            if (_probe != null)
            {
                _probe.enabled = false;
                _probe.realtimeTextureRTH?.Release();
                _probe.realtimeDepthTextureRTH?.Release();
                CoreUtils.Destroy(_probe.gameObject);
            }
            _probe = null;
            _camera = null;
            _settings = null;
            _elapsed = 0.0f;
            _captureVersion = 0;
            _pending = false;
            _resolution = 0;
            CaptureRequests = 0;
        }

        private void Create(int resolution)
        {
            var root = new GameObject("X-Ray Wet Surface Reflection");
            root.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(root, _owner.scene);
            root.transform.SetParent(_owner.transform, false);
            root.transform.SetPositionAndRotation(_owner.transform.position, Quaternion.identity);
            root.layer = XRayWeatherFogRenderer.GetVolumeLayer(_owner);
            root.AddComponent<ReflectionProbe>();
            _probe = root.AddComponent<HDAdditionalReflectionData>();
            root.SetActive(false);
            _probe.mode = ProbeSettings.Mode.Realtime;
            _probe.realtimeMode = ProbeSettings.RealtimeMode.OnDemand;
            _probe.timeSlicing = true;
            _probe.influenceVolume.shape = InfluenceShape.Box;
            _probe.influenceVolume.boxSize = new Vector3(CAPTURE_RADIUS * 2.0f, 64.0f, CAPTURE_RADIUS * 2.0f);
            _probe.influenceVolume.boxBlendDistancePositive = Vector3.one * 12.0f;
            _probe.influenceVolume.boxBlendDistanceNegative = Vector3.one * 12.0f;
            _probe.settingsRaw.cubeResolution.useOverride = true;
            _probe.settingsRaw.cubeResolution.@override = (CubeReflectionResolution)resolution;
            _probe.settingsRaw.cameraSettings.frustum.nearClipPlaneRaw = 0.1f;
            _probe.settingsRaw.cameraSettings.frustum.farClipPlaneRaw = 160.0f;
            _probe.settingsRaw.cameraSettings.volumes.layerMask = 1 << root.layer;
            _probe.settingsRaw.cameraSettings.volumes.anchorOverride = root.transform;
            _probe.settingsRaw.cameraSettings.culling.useOcclusionCulling = false;
            _probe.importance = 0;
            _probe.multiplier = 1.0f;
            _probe.weight = 1.0f;
            _probe.fadeDistance = CAPTURE_RADIUS * 3.0f;
            _resolution = resolution;
            root.SetActive(true);
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_probe == null || _settings == null || camera == null) return;
            if (camera.cameraType != CameraType.Game && (Application.isPlaying || camera.cameraType != CameraType.SceneView)) return;
            if (_camera != null && _camera != camera && _camera.isActiveAndEnabled && (_camera.cameraType == CameraType.Game || camera.cameraType != CameraType.Game)) return;
            _camera = camera;
            RenderTexture texture = _probe.realtimeTexture;
            if (_pending && texture != null && texture.updateCount != _captureVersion) _pending = false;
            if (_pending || CaptureRequests > 0 && _elapsed < _settings.ReflectionRefreshSeconds) return;
            Vector3 position = camera.transform.position;
            if (CaptureRequests == 0 || (position - _probe.transform.position).sqrMagnitude >= CAPTURE_MOVE_DISTANCE * CAPTURE_MOVE_DISTANCE) _probe.transform.SetPositionAndRotation(position, Quaternion.identity);
            _captureVersion = texture != null ? texture.updateCount : 0;
            _probe.RequestRenderNextUpdate();
            CaptureRequests++;
            _elapsed = 0.0f;
            _pending = true;
        }
    }
}
