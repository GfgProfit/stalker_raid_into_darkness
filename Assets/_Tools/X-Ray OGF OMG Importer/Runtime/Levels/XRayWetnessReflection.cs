using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public sealed class XRayWetnessReflection : IDisposable
    {
        private const int MAX_PROBES = 4;
        private const int CELL_STRIDE = 24;
        private const float GRID_SPACING = 32.0f;
        private const float CAPTURE_HEIGHT = 1.5f;
        private const float BOOTSTRAP_WEIGHT = 0.0001f;
        private const double CAPTURE_TIMEOUT_SECONDS = 10.0;
        private const float PLANAR_CAPTURE_DISTANCE = 80.0f;
        private const float PLANAR_GRID_SPACING = 4.0f;

        private readonly GameObject _owner;
        private readonly ProbeSlot[] _slots = new ProbeSlot[MAX_PROBES];

        private PrecipitationConfig _settings;
        private XRayWetnessField _wetness;
        private Camera _camera;
        private ProbeSlot _rendering;
        private ProbeSlot _reading;
        private PlanarReflectionProbe _planar;
        private Vector3 _planarAnchor;
        private double _time;
        private double _lastClock;
        private double _requestElapsed;
        private float _weight = 1.0f;
        private float _planarWeight = 1.0f;
        private double _planarNextHeightRead;
        private int _resolution;
        private int _generation;
        private int _planarGeneration;
        private int _planarResolution;
        private bool _planarHeightPending;

        public HDAdditionalReflectionData Probe => _slots[0]?.Probe;
        public int CaptureRequests { get; private set; }
        public int CaptureRestarts { get; private set; }
        public int ProbeCount { get; private set; }
        public PlanarReflectionProbe PlanarProbe => _planar;
        public Vector2 PlanarGroundHeights { get; private set; }
        public float PlanarGroundHeight { get; private set; }
        public bool PlanarGroundValid { get; private set; }
        public float PlanarWeight
        {
            get => _planarWeight;
            set
            {
                if (!float.IsFinite(value) || value < 0.0f || value > 1.0f) throw new ArgumentOutOfRangeException(nameof(value));
                _planarWeight = value;
                if (_planar != null) _planar.weight = _weight * _planarWeight;
            }
        }
        public float Weight
        {
            get => _weight;
            set
            {
                if (!float.IsFinite(value) || value < 0.0f || value > 1.0f) throw new ArgumentOutOfRangeException(nameof(value));
                _weight = value;
                foreach (ProbeSlot slot in _slots)
                {
                    if (slot?.Probe != null) slot.Probe.weight = _weight * (slot == _rendering ? Mathf.Max(BOOTSTRAP_WEIGHT, slot.Blend) : slot.Blend);
                }
                if (_planar != null) _planar.weight = _weight * _planarWeight;
            }
        }
        public long EstimatedBytes
        {
            get
            {
                long bytes = 0;
                foreach (ProbeSlot slot in _slots)
                {
                    if (slot?.Probe != null && slot.Probe.realtimeTexture != null) bytes += (long)_resolution * _resolution * 6L * 8L * 4L / 3L;
                }
                if (_planar != null)
                {
                    if (_planar.realtimeTexture != null) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(_planar.realtimeTexture);
                    if (_planar.realtimeDepthTextureRTH?.rt != null) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(_planar.realtimeDepthTextureRTH.rt);
                }
                return bytes;
            }
        }

        public XRayWetnessReflection(GameObject owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _owner = owner;
        }

        public HDAdditionalReflectionData GetProbe(int index)
        {
            if (index < 0 || index >= MAX_PROBES) throw new ArgumentOutOfRangeException(nameof(index));
            return _slots[index]?.Probe;
        }

        public void Tick(PrecipitationConfig settings, float realDelta, XRayWetnessField wetness = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!float.IsFinite(realDelta) || realDelta < 0.0f) throw new ArgumentOutOfRangeException(nameof(realDelta));
            if (!settings.NonCameraReflectionsEnabled || settings.ReflectionIntensity <= 0.0f || !settings.WetnessEnabled && !settings.PuddlesEnabled)
            {
                Dispose();
                return;
            }
            int resolution = settings.Quality == XRayPrecipitationQuality.Low ? 64 : 128;
            if (Probe != null && _resolution != resolution) Dispose();
            _settings = settings;
            if (wetness != null) _wetness = wetness;
            if (!settings.PlanarReflectionsEnabled || !settings.PuddlesEnabled || settings.PuddleStrength <= 0.0f || _planar != null && _planarResolution != settings.PlanarReflectionResolution) ReleasePlanar();
            AdvanceClock();
            if (Probe == null)
            {
                _resolution = resolution;
                _slots[0] = Create();
                RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            }
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            _generation++;
            ReleasePlanar();
            for (int i = 0; i < _slots.Length; i++)
            {
                ProbeSlot slot = _slots[i];
                ReleaseProbe(slot);
                _slots[i] = null;
            }
            _camera = null;
            _settings = null;
            _wetness = null;
            _rendering = null;
            _reading = null;
            _time = 0.0f;
            _lastClock = 0.0;
            _requestElapsed = 0.0f;
            _resolution = 0;
            _planarNextHeightRead = 0.0;
            PlanarGroundHeights = Vector2.zero;
            PlanarGroundHeight = 0.0f;
            PlanarGroundValid = false;
            CaptureRequests = 0;
            CaptureRestarts = 0;
            ProbeCount = 0;
        }

        private ProbeSlot Create()
        {
            var root = new GameObject("X-Ray Wet Surface Reflection " + ProbeCount);
            root.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(root, _owner.scene);
            root.transform.SetParent(_owner.transform, false);
            root.transform.SetPositionAndRotation(_owner.transform.position, Quaternion.identity);
            root.layer = XRayWeatherFogRenderer.GetVolumeLayer(_owner);
            root.AddComponent<ReflectionProbe>();
            HDAdditionalReflectionData probe = root.AddComponent<HDAdditionalReflectionData>();
            root.SetActive(false);
            probe.mode = ProbeSettings.Mode.Realtime;
            probe.realtimeMode = ProbeSettings.RealtimeMode.OnDemand;
            probe.timeSlicing = true;
            probe.influenceVolume.shape = InfluenceShape.Box;
            probe.influenceVolume.boxSize = Vector3.one * GRID_SPACING * 2.0f;
            probe.influenceVolume.boxBlendDistancePositive = Vector3.one * (GRID_SPACING * 0.5f);
            probe.influenceVolume.boxBlendDistanceNegative = Vector3.one * (GRID_SPACING * 0.5f);
            probe.settingsRaw.proxySettings.useInfluenceVolumeAsProxyVolume = true;
            probe.settingsRaw.cubeResolution.useOverride = true;
            probe.settingsRaw.cubeResolution.@override = (CubeReflectionResolution)_resolution;
            probe.settingsRaw.cameraSettings.frustum.nearClipPlaneRaw = 0.1f;
            probe.settingsRaw.cameraSettings.frustum.farClipPlaneRaw = 160.0f;
            probe.settingsRaw.cameraSettings.volumes.layerMask = 1 << root.layer;
            probe.settingsRaw.cameraSettings.volumes.anchorOverride = root.transform;
            probe.settingsRaw.cameraSettings.culling.useOcclusionCulling = false;
            probe.importance = 0;
            probe.multiplier = 1.0f;
            probe.weight = 0.0f;
            probe.fadeDistance = GRID_SPACING * 4.0f;
            root.SetActive(true);
            ProbeCount++;
            return new ProbeSlot { Probe = probe };
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (Probe == null || _settings == null || camera == null) return;
            if (camera.cameraType != CameraType.Game && (Application.isPlaying || camera.cameraType != CameraType.SceneView)) return;
            if (_camera != null && _camera != camera && _camera.isActiveAndEnabled && (_camera.cameraType == CameraType.Game || camera.cameraType != CameraType.Game)) return;
            _camera = camera;
            AdvanceClock();
            UpdatePlanar(camera);
            Vector2Int cell = GridCell(camera.transform.position);
            if (_rendering != null && (!Desired(_rendering.Cell, cell) || _time - _rendering.CaptureStarted >= CAPTURE_TIMEOUT_SECONDS)) RestartPendingCapture();
            if (_rendering != null)
            {
                RenderTexture texture = _rendering.Probe.realtimeTexture;
                if (texture != null && texture.updateCount != _rendering.CaptureVersion)
                {
                    _rendering.Captured = true;
                    _rendering.Blend = Mathf.Max(BOOTSTRAP_WEIGHT, _rendering.Blend);
                    _rendering.Probe.weight = _weight * _rendering.Blend;
                    _rendering.LastCapture = _time;
                    _rendering = null;
                }
            }
            EnsureLocalSlots(cell);
            if (_reading == null) BeginHeightReadback();
            if (_rendering != null || _weight <= 0.0f) return;
            ProbeSlot next = null;
            foreach (ProbeSlot slot in _slots)
            {
                if (slot == null || !slot.HeightReady || !Desired(slot.Cell, cell)) continue;
                if (slot.Captured && (_time - slot.LastCapture < _settings.ReflectionRefreshSeconds || _requestElapsed < _settings.ReflectionRefreshSeconds / MAX_PROBES)) continue;
                if (next == null || !slot.Captured && next.Captured || slot.Captured == next.Captured && slot.LastCapture < next.LastCapture) next = slot;
            }
            if (next == null) return;
            RenderTexture previous = next.Probe.realtimeTexture;
            next.CaptureVersion = previous != null ? previous.updateCount : 0;
            next.Probe.weight = _weight * Mathf.Max(BOOTSTRAP_WEIGHT, next.Blend);
            next.Probe.RequestRenderNextUpdate();
            next.CaptureStarted = _time;
            _rendering = next;
            CaptureRequests++;
            _requestElapsed = 0.0f;
        }

        private void EnsureLocalSlots(Vector2Int cell)
        {
            for (int z = 0; z < 2; z++)
            {
                for (int x = 0; x < 2; x++)
                {
                    var desired = cell + new Vector2Int(x, z);
                    bool exists = false;
                    foreach (ProbeSlot slot in _slots)
                    {
                        if (slot != null && slot.Assigned && slot.Cell == desired) exists = true;
                    }
                    if (exists) continue;
                    for (int i = 0; i < _slots.Length; i++)
                    {
                        ProbeSlot slot = _slots[i];
                        if (slot != null && (slot == _rendering || slot == _reading || slot.Assigned && Desired(slot.Cell, cell))) continue;
                        if (slot == null) _slots[i] = slot = Create();
                        slot.Cell = desired;
                        slot.Assigned = true;
                        slot.HeightReady = false;
                        slot.NextHeightAttempt = 0.0;
                        slot.Captured = false;
                        slot.Blend = 0.0f;
                        slot.Probe.weight = 0.0f;
                        break;
                    }
                }
            }
        }

        private void BeginHeightReadback()
        {
            if (_wetness?.Buffer == null || !SystemInfo.supportsAsyncGPUReadback) return;
            foreach (ProbeSlot slot in _slots)
            {
                if (slot == null || !slot.Assigned || slot.HeightReady || _time < slot.NextHeightAttempt) continue;
                slot.NextHeightAttempt = _time + 0.25;
                Vector4 mapping = _wetness.WorldMapping;
                float worldX = slot.Cell.x * GRID_SPACING;
                float worldZ = slot.Cell.y * GRID_SPACING;
                float u = (worldX - mapping.x) * mapping.z;
                float v = (worldZ - mapping.y) * mapping.w;
                if (u < 0.0f || u >= 1.0f || v < 0.0f || v >= 1.0f) continue;
                int x = Mathf.Min((int)(u * _wetness.Width), _wetness.Width - 1);
                int y = Mathf.Min((int)(v * _wetness.Height), _wetness.Height - 1);
                ReadHeight(slot, worldX, worldZ, (y * _wetness.Width + x) * CELL_STRIDE);
                return;
            }
        }

        private void ReadHeight(ProbeSlot slot, float worldX, float worldZ, int offset)
        {
            int generation = _generation;
            int slotGeneration = slot.Generation;
            _reading = slot;
            AsyncGPUReadback.Request(_wetness.Buffer, CELL_STRIDE, offset, request =>
            {
                if (generation != _generation || slotGeneration != slot.Generation || slot.Probe == null) return;
                _reading = null;
                if (request.hasError) return;
                Vector2 heights = request.GetData<XRayWetnessCell>()[0].Heights;
                if (!float.IsFinite(heights.x) || !float.IsFinite(heights.y) || heights.x < heights.y) return;
                slot.Probe.transform.SetPositionAndRotation(new Vector3(worldX, heights.y + CAPTURE_HEIGHT, worldZ), Quaternion.identity);
                slot.HeightReady = true;
            });
        }

        public static bool TrySelectPlanarGround(Vector2 heights, float viewerHeight, out float ground)
        {
            ground = 0.0f;
            if (!float.IsFinite(heights.x) || !float.IsFinite(heights.y) || !float.IsFinite(viewerHeight) || heights.x < heights.y) return false;
            float maximum = viewerHeight - 0.05f;
            if (heights.x < maximum)
            {
                ground = heights.x;
                return true;
            }
            if (heights.y < maximum)
            {
                ground = heights.y;
                return true;
            }
            return false;
        }

        private void UpdatePlanar(Camera camera)
        {
            if (!_settings.PlanarReflectionsEnabled || !_settings.PuddlesEnabled || _settings.PuddleStrength <= 0.0f || _wetness?.Buffer == null || !SystemInfo.supportsAsyncGPUReadback || _planarHeightPending || _weight <= 0.0f || _planarWeight <= 0.0f) return;
            Vector3 position = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            forward.y = 0.0f;
            if (forward.sqrMagnitude > 0.001f) position += forward.normalized * PLANAR_GRID_SPACING;
            position.x = Mathf.Round(position.x / PLANAR_GRID_SPACING) * PLANAR_GRID_SPACING;
            position.z = Mathf.Round(position.z / PLANAR_GRID_SPACING) * PLANAR_GRID_SPACING;
            float distance = new Vector2(position.x - _planarAnchor.x, position.z - _planarAnchor.z).sqrMagnitude;
            bool moved = _planar != null && distance >= PLANAR_GRID_SPACING * PLANAR_GRID_SPACING;
            if (_planar != null && distance > 256.0f)
            {
                ReleasePlanar();
                _planarNextHeightRead = 0.0;
            }
            if (_time < _planarNextHeightRead && (!moved || _time < _planarNextHeightRead - 0.75)) return;
            _planarNextHeightRead = _time + 1.0;
            Vector4 mapping = _wetness.WorldMapping;
            float u = (position.x - mapping.x) * mapping.z;
            float v = (position.z - mapping.y) * mapping.w;
            if (u < 0.0f || u >= 1.0f || v < 0.0f || v >= 1.0f)
            {
                ReleasePlanar();
                return;
            }
            int x = Mathf.Min((int)(u * _wetness.Width), _wetness.Width - 1);
            int y = Mathf.Min((int)(v * _wetness.Height), _wetness.Height - 1);
            ReadPlanarHeight(position, camera.transform.position.y, (y * _wetness.Width + x) * CELL_STRIDE);
        }

        private void ReadPlanarHeight(Vector3 position, float viewerHeight, int offset)
        {
            int generation = _generation;
            int planarGeneration = _planarGeneration;
            _planarHeightPending = true;
            AsyncGPUReadback.Request(_wetness.Buffer, CELL_STRIDE, offset, request =>
            {
                if (generation != _generation || planarGeneration != _planarGeneration || _settings == null) return;
                _planarHeightPending = false;
                if (request.hasError) return;
                XRayWetnessCell cell = request.GetData<XRayWetnessCell>()[0];
                Vector2 heights = cell.Heights;
                PlanarGroundHeights = heights;
                PlanarGroundValid = TrySelectPlanarGround(heights, viewerHeight, out float ground);
                PlanarGroundHeight = ground;
                if (!PlanarGroundValid)
                {
                    ReleasePlanar();
                    return;
                }
                float accumulation = ground == heights.x ? cell.Moisture.w : cell.Moisture.y;
                if (accumulation <= 0.025f)
                {
                    ReleasePlanar();
                    return;
                }
                position.y = ground + _settings.GetPuddleShape(_settings.ArtProfile).x * Mathf.Clamp01(accumulation * _settings.PuddleStrength) * 0.25f;
                _planarAnchor = position;
                if (_planar == null) CreatePlanar();
                _planar.transform.SetPositionAndRotation(position, Quaternion.identity);
                _planar.PrepareCulling();
            });
        }

        private void CreatePlanar()
        {
            var root = new GameObject("X-Ray Local Puddle Reflection");
            root.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(root, _owner.scene);
            root.transform.SetParent(_owner.transform, false);
            root.transform.SetPositionAndRotation(_planarAnchor, Quaternion.identity);
            root.layer = XRayWeatherFogRenderer.GetVolumeLayer(_owner);
            _planar = root.AddComponent<PlanarReflectionProbe>();
            root.SetActive(false);
            _planar.mode = ProbeSettings.Mode.Realtime;
            _planar.realtimeMode = ProbeSettings.RealtimeMode.EveryFrame;
            _planar.timeSlicing = false;
            _planar.influenceVolume.shape = InfluenceShape.Box;
            _planar.influenceVolume.boxSize = new Vector3(16.0f, 1.0f, 16.0f);
            _planar.influenceVolume.boxBlendDistancePositive = new Vector3(2.0f, 0.05f, 2.0f);
            _planar.influenceVolume.boxBlendDistanceNegative = new Vector3(2.0f, 0.05f, 2.0f);
            _planar.settingsRaw.proxySettings.useInfluenceVolumeAsProxyVolume = true;
            _planar.settingsRaw.proxySettings.mirrorPositionProxySpace = Vector3.zero;
            _planar.settingsRaw.proxySettings.mirrorRotationProxySpace = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            _planar.settingsRaw.resolutionScalable.useOverride = true;
            _planar.settingsRaw.resolutionScalable.@override = (PlanarReflectionAtlasResolution)_settings.PlanarReflectionResolution;
            _planar.settingsRaw.roughReflections = true;
            _planar.settingsRaw.frustum.fieldOfViewMode = ProbeSettings.Frustum.FOVMode.Viewer;
            _planar.settingsRaw.frustum.viewerScale = 1.0f;
            _planar.settingsRaw.cameraSettings.frustum.nearClipPlaneRaw = 0.05f;
            _planar.settingsRaw.cameraSettings.frustum.farClipPlaneRaw = PLANAR_CAPTURE_DISTANCE;
            _planar.settingsRaw.cameraSettings.volumes.layerMask = 1 << root.layer;
            _planar.settingsRaw.cameraSettings.volumes.anchorOverride = _camera != null ? _camera.transform : root.transform;
            _planar.settingsRaw.cameraSettings.culling.useOcclusionCulling = false;
            _planar.importance = 64;
            _planar.multiplier = 1.0f;
            _planar.weight = _weight * _planarWeight;
            _planar.fadeDistance = 48.0f;
            _planarResolution = _settings.PlanarReflectionResolution;
            root.SetActive(true);
        }

        private void ReleasePlanar()
        {
            _planarGeneration++;
            _planarHeightPending = false;
            if (_planar != null)
            {
                _planar.enabled = false;
                if (!Application.isEditor)
                {
                    _planar.gameObject.AddComponent<XRayWetnessReflectionRelease>().Initialize(_planar.realtimeTextureRTH, _planar.realtimeDepthTextureRTH);
                }
                CoreUtils.Destroy(_planar.gameObject);
            }
            _planar = null;
            _planarResolution = 0;
        }

        private void RestartPendingCapture()
        {
            ProbeSlot previous = _rendering;
            _rendering = null;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != previous) continue;
                ReleaseProbe(previous);
                ProbeCount--;
                _slots[i] = Create();
                CaptureRestarts++;
                return;
            }
        }

        private void ReleaseProbe(ProbeSlot slot)
        {
            if (slot == null) return;
            slot.Generation++;
            if (_reading == slot) _reading = null;
            if (slot.Probe == null) return;
            slot.Probe.enabled = false;
            if (!Application.isEditor)
            {
                slot.Probe.gameObject.AddComponent<XRayWetnessReflectionRelease>().Initialize(slot.Probe.realtimeTextureRTH, slot.Probe.realtimeDepthTextureRTH);
            }
            CoreUtils.Destroy(slot.Probe.gameObject);
            slot.Probe = null;
        }

        private void AdvanceClock()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float elapsed = _lastClock > 0.0 ? (float)Math.Max(0.0, now - _lastClock) : 0.0f;
            _lastClock = now;
            _time = now;
            _requestElapsed += elapsed;
            foreach (ProbeSlot slot in _slots)
            {
                if (slot?.Probe == null || !slot.Captured) continue;
                slot.Blend = Mathf.MoveTowards(slot.Blend, 1.0f, elapsed);
                slot.Probe.weight = _weight * slot.Blend;
            }
        }

        private static Vector2Int GridCell(Vector3 position)
        {
            return new Vector2Int(Mathf.FloorToInt(position.x / GRID_SPACING), Mathf.FloorToInt(position.z / GRID_SPACING));
        }

        private static bool Desired(Vector2Int candidate, Vector2Int cell)
        {
            return candidate.x >= cell.x && candidate.x <= cell.x + 1 && candidate.y >= cell.y && candidate.y <= cell.y + 1;
        }

        private sealed class ProbeSlot
        {
            public HDAdditionalReflectionData Probe { get; set; }
            public Vector2Int Cell { get; set; }
            public bool Assigned { get; set; }
            public bool HeightReady { get; set; }
            public bool Captured { get; set; }
            public float Blend { get; set; }
            public double LastCapture { get; set; }
            public double CaptureStarted { get; set; }
            public double NextHeightAttempt { get; set; }
            public uint CaptureVersion { get; set; }
            public int Generation { get; set; }
        }
    }
}
