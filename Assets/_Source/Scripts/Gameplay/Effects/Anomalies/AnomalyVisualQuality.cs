using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Effects
{
    public static class AnomalyVisualQuality
    {
        private const float VISIBILITY_INTERVAL = 0.2f;
        private const float DRAW_HYSTERESIS = 1.1f;
        private const float BOUNDS_PADDING = 4f;
        private const int PRUNE_INTERVAL = 64;

        internal const float HIDDEN_DELAY = 0.35f;
        internal const float WAKE_GRACE = 0.4f;

        private static readonly Dictionary<ParticleSystem, ParticleSettings> _systems = new();
        private static readonly List<ParticleSystem> _destroyed = new();
        private static readonly List<ParticleSettings> _sleeping = new();
        private static readonly List<GazirovkaVfxPrototype> _sleepingPrototypes = new();
        private static readonly List<CameraView> _views = new();
        private static readonly CameraView _wakeView = new();
        private static readonly CameraView _gameWakeView = new();
        private static Camera[] _cameras = Array.Empty<Camera>();
        private static Vector3[] _eyePositions = Array.Empty<Vector3>();
        private static int _tier = -1;
        private static int _eyeCount;
        private static int _viewCount;
        private static int _registeredSincePrune;
        private static float _nextVisibilityUpdate;
        private static Camera _lastWakeCamera;

        internal static float Density => _tier == 0 ? 0.45f : _tier == 1 ? 0.7f : 1f;
        internal static int ChildLimit => _tier == 0 ? 16 : _tier == 1 ? 32 : _tier == 2 ? 64 : int.MaxValue;
        internal static int ContactLimit => _tier == 0 ? 4 : _tier == 1 ? 8 : _tier == 2 ? 16 : int.MaxValue;
        internal static int DischargeLimit => _tier == 0 ? 2 : _tier == 1 ? 4 : _tier == 2 ? 8 : int.MaxValue;
        private static float DrawDistance => _tier == 0 ? 80f : _tier == 1 ? 140f : 240f;

        internal static event Action AmbientVisibilityChanged;

        public static void Apply(int? quality)
        {
            int tier = quality.HasValue ? Mathf.Clamp(quality.Value, 0, 2) : -1;
            if (_tier == tier) return;
            if (_tier < 0 && tier >= 0)
            {
                RenderPipelineManager.beginContextRendering += UpdateVisibility;
                RenderPipelineManager.beginCameraRendering += WakeVisible;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
            }
            else if (tier < 0)
            {
                RenderPipelineManager.beginContextRendering -= UpdateVisibility;
                RenderPipelineManager.beginCameraRendering -= WakeVisible;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
            }

            _tier = tier;
            Prune();
            foreach (ParticleSettings state in _systems.Values)
            {
                state.ApplyDensity(Density);
                if (_tier < 0)
                {
                    state.RestoreVisibility();
                    state.Resume(0f);
                }
            }
            _nextVisibilityUpdate = 0f;
            _lastWakeCamera = null;
            if (_tier < 0) _eyeCount = 0;
            AmbientVisibilityChanged?.Invoke();
        }

        internal static void RegisterHierarchy(ParticleSystem root)
        {
            if (!Application.isPlaying || root == null) return;
            RegisterPlayableHierarchy(root.transform, root.GetComponentsInChildren<ParticleSystem>(true));
        }

        internal static void RegisterPlayableHierarchy(Transform root, ParticleSystem[] systems)
        {
            RegisterPlayableHierarchy(root, systems, null, false);
        }

        internal static void RegisterAmbientHierarchy(ParticleSystem root, AnomalyVisual owner, bool includeChildren)
        {
            if (!Application.isPlaying || root == null) return;
            RegisterPlayableHierarchy(root.transform, root.GetComponentsInChildren<ParticleSystem>(true), owner, includeChildren);
        }

        private static void RegisterPlayableHierarchy(Transform root, ParticleSystem[] systems, AnomalyVisual owner, bool includeChildren)
        {
            if (!Application.isPlaying || root == null) return;
            foreach (ParticleSystem system in systems)
            {
                Transform node = system.transform;
                while (node != root && node.gameObject.activeSelf)
                {
                    node = node.parent;
                }
                if (node != root) continue;
                Register(system);
                if (owner != null && (includeChildren || system.transform == root) && _systems.TryGetValue(system, out ParticleSettings state))
                {
                    state.SetAmbientOwner(owner);
                }
            }
        }

        internal static void Register(ParticleSystem system)
        {
            if (!Application.isPlaying || system == null || !system.gameObject.scene.IsValid() || _systems.ContainsKey(system)) return;
            var state = new ParticleSettings(system);
            _systems.Add(system, state);
            state.ApplyDensity(Density);
            if (++_registeredSincePrune >= PRUNE_INTERVAL) Prune();
        }

        internal static void UnregisterPrototypeSources(ParticleSystem[] systems)
        {
            foreach (ParticleSystem system in systems)
            {
                if (system == null || !_systems.Remove(system, out ParticleSettings state)) continue;
                state.ForgetSuspension();
                state.RestoreVisibility();
                state.ApplyDensity(1f);
            }
        }

        internal static bool IsPrototypeDistant(Bounds bounds, bool wasDistant)
        {
            if (_tier < 0 || _eyeCount == 0) return false;
            float distance = wasDistant ? DrawDistance : DrawDistance * DRAW_HYSTERESIS;
            float squared = distance * distance;
            for (int index = 0; index < _eyeCount; index++)
            {
                if (bounds.SqrDistance(_eyePositions[index]) <= squared) return false;
            }
            return true;
        }

        internal static void SetPrototypeWaiting(GazirovkaVfxPrototype prototype, bool waiting)
        {
            int index = prototype.SleepingIndex;
            if (waiting)
            {
                if (index >= 0) return;
                prototype.SleepingIndex = _sleepingPrototypes.Count;
                _sleepingPrototypes.Add(prototype);
                return;
            }
            if (index < 0) return;
            int last = _sleepingPrototypes.Count - 1;
            GazirovkaVfxPrototype moved = _sleepingPrototypes[last];
            _sleepingPrototypes[index] = moved;
            moved.SleepingIndex = index;
            _sleepingPrototypes.RemoveAt(last);
            prototype.SleepingIndex = -1;
        }

        internal static bool IsPrototypeInView(Bounds bounds, int layer)
        {
            if (_tier < 0 || _viewCount == 0 || _eyeCount == 0) return true;
            for (int index = 0; index < _viewCount; index++)
            {
                if (_views[index].Includes(layer, bounds)) return true;
            }
            return false;
        }

        internal static bool CanDraw(Vector3 position)
        {
            return IsNearAnyCamera(position, DrawDistance);
        }

        internal static bool CanEmitAmbient(ParticleSystem system)
        {
            return _tier < 0 || !_systems.TryGetValue(system, out ParticleSettings state) || !state.IsDistant;
        }

        internal static bool IsAmbientSuspended(ParticleSystem system)
        {
            return system != null && _sleeping.Count != 0 && _systems.TryGetValue(system, out ParticleSettings state) && state.SleepingIndex >= 0 && system.isPaused;
        }

        private static void UpdateVisibility(ScriptableRenderContext context, List<Camera> cameras)
        {
            float now = Time.unscaledTime;
            if (now < _nextVisibilityUpdate) return;
            _nextVisibilityUpdate = now + VISIBILITY_INTERVAL;
            _lastWakeCamera = null;
            _viewCount = 0;
            foreach (Camera camera in cameras)
            {
                if (!IsVisibilityCamera(camera)) continue;
                if (_viewCount == _views.Count) _views.Add(new CameraView());
                _views[_viewCount++].Capture(camera, camera.cullingMatrix);
            }
            int count = Camera.allCamerasCount;
            if (_cameras.Length < count)
            {
                _cameras = new Camera[count];
                _eyePositions = new Vector3[count];
            }
            _eyeCount = 0;
            if (count > 0)
            {
                int returned = Camera.GetAllCameras(_cameras);
                for (int index = 0; index < returned; index++)
                {
                    Camera camera = _cameras[index];
                    if (IsGameCamera(camera))
                    {
                        _eyePositions[_eyeCount++] = camera.transform.position;
                    }
                    _cameras[index] = null;
                }
            }
            Prune();
            foreach (ParticleSettings state in _systems.Values)
            {
                state.UpdateVisibility(now);
            }
            AmbientVisibilityChanged?.Invoke();
        }

        private static void WakeVisible(ScriptableRenderContext context, Camera camera)
        {
            if ((_sleeping.Count == 0 && _sleepingPrototypes.Count == 0) || !IsVisibilityCamera(camera)) return;
            bool isGame = IsGameCamera(camera);
            CameraView view = isGame ? _gameWakeView : _wakeView;
            Matrix4x4 matrix = camera.cullingMatrix;
            Vector3 position = camera.transform.position;
            if (isGame && camera == _lastWakeCamera && view.Matches(camera, matrix, position)) return;
            view.Capture(camera, matrix);
            if (isGame) _lastWakeCamera = camera;
            float now = Time.unscaledTime;
            for (int index = _sleeping.Count - 1; index >= 0; index--)
            {
                _sleeping[index].WakeForView(view, position, isGame, now);
            }
            for (int index = _sleepingPrototypes.Count - 1; index >= 0; index--)
            {
                _sleepingPrototypes[index].WakeForView(view, position, isGame, DrawDistance, now);
            }
        }

        private static bool IsGameCamera(Camera camera)
        {
            return camera != null && (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.VR);
        }

        private static bool IsVisibilityCamera(Camera camera)
        {
            return IsGameCamera(camera) || (camera != null && (camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.SceneView));
        }

        private static bool IsNearAnyCamera(Vector3 position, float distance)
        {
            if (_tier < 0 || _eyeCount == 0) return true;
            float squared = distance * distance;
            for (int index = 0; index < _eyeCount; index++)
            {
                if ((position - _eyePositions[index]).sqrMagnitude <= squared) return true;
            }
            return false;
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            Prune();
        }

        private static void Prune()
        {
            foreach (ParticleSystem system in _systems.Keys)
            {
                if (system == null) _destroyed.Add(system);
            }
            foreach (ParticleSystem system in _destroyed)
            {
                _systems[system].ForgetSuspension();
                _systems.Remove(system);
            }
            _destroyed.Clear();
            _registeredSincePrune = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            AmbientVisibilityChanged = null;
            Apply(null);
            _systems.Clear();
            _sleeping.Clear();
            foreach (GazirovkaVfxPrototype prototype in _sleepingPrototypes)
            {
                if (prototype != null) prototype.SleepingIndex = -1;
            }
            _sleepingPrototypes.Clear();
            _viewCount = 0;
            _lastWakeCamera = null;
        }

        internal sealed class CameraView
        {
            private readonly Plane[] _planes = new Plane[6];
            private Matrix4x4 _matrix;
            private Vector3 _position;
            private int _mask;
            private bool _stereo;
            private bool _valid;

            public void Capture(Camera camera, Matrix4x4 matrix)
            {
                _matrix = matrix;
                _position = camera.transform.position;
                _mask = camera.cullingMask;
                _stereo = camera.stereoEnabled;
                _valid = !_stereo;
                if (!_valid) return;
                GeometryUtility.CalculateFrustumPlanes(matrix, _planes);
                foreach (Plane plane in _planes)
                {
                    if (float.IsFinite(plane.distance) && float.IsFinite(plane.normal.sqrMagnitude) && plane.normal.sqrMagnitude > 0f) continue;
                    _valid = false;
                    break;
                }
            }

            public bool Matches(Camera camera, Matrix4x4 matrix, Vector3 position)
            {
                return _matrix.Equals(matrix) && _position.Equals(position) && _mask == camera.cullingMask && _stereo == camera.stereoEnabled;
            }

            public bool Includes(int layer, Bounds bounds)
            {
                return (_mask & layer) != 0 && (!_valid || GeometryUtility.TestPlanesAABB(_planes, bounds));
            }
        }

        private sealed class ParticleSettings
        {
            private readonly ParticleSystem _system;
            private readonly ParticleSystemRenderer _renderer;
            private AnomalyVisual _ambientOwner;
            private Bounds _sleepBounds;
            private Vector3 _sleepPosition;
            private float _sleepRadius;
            private float _hiddenSince = -1f;
            private float _stayAwakeUntil;
            private ParticleSystem.MinMaxCurve _timeRate;
            private ParticleSystem.MinMaxCurve _distanceRate;
            private ParticleSystem.Burst[] _bursts;
            private int _capacity;
            private bool _hasBaseline;
            private bool _ownsRenderFlag;

            public bool IsDistant { get; private set; }
            public int SleepingIndex { get; private set; } = -1;

            public ParticleSettings(ParticleSystem system)
            {
                _system = system;
                _renderer = system.GetComponent<ParticleSystemRenderer>();
            }

            public void SetAmbientOwner(AnomalyVisual owner)
            {
                if (_renderer == null || !_system.main.loop || _system.trigger.enabled || _system.collision.enabled || _system.lights.enabled || _system.subEmitters.enabled) return;
                _ambientOwner = owner;
            }

            public void ApplyDensity(float density)
            {
                if (!_hasBaseline && density == 1f) return;
                ParticleSystem.MainModule main = _system.main;
                ParticleSystem.EmissionModule emission = _system.emission;
                if (!_hasBaseline)
                {
                    _capacity = main.maxParticles;
                    _timeRate = emission.rateOverTime;
                    _distanceRate = emission.rateOverDistance;
                    _bursts = new ParticleSystem.Burst[emission.burstCount];
                    emission.GetBursts(_bursts);
                    _hasBaseline = true;
                }

                main.maxParticles = Mathf.Min(_capacity, Mathf.Max(1, Mathf.CeilToInt(_capacity * density)));
                emission.rateOverTime = Scale(_timeRate, density, 0f);
                emission.rateOverDistance = Scale(_distanceRate, density, 0f);
                for (int index = 0; index < _bursts.Length; index++)
                {
                    ParticleSystem.Burst burst = _bursts[index];
                    burst.count = Scale(burst.count, density, 1f);
                    emission.SetBurst(index, burst);
                }
                if (density == 1f)
                {
                    _hasBaseline = false;
                    _bursts = null;
                }
            }

            public void UpdateVisibility(float now)
            {
                if (!_system.gameObject.activeInHierarchy)
                {
                    ForgetSuspension();
                    return;
                }
                float distance = IsDistant ? DrawDistance : DrawDistance * DRAW_HYSTERESIS;
                Vector3 position = _system.transform.position;
                bool inRange = IsNearAnyCamera(position, distance);
                Bounds bounds = default;
                bool hasBounds = false;
                if (!inRange && _renderer != null && _renderer.enabled)
                {
                    bounds = _renderer.bounds;
                    hasBounds = true;
                    inRange = IsNearAnyCamera(bounds.center, distance + bounds.extents.magnitude);
                }
                if (inRange)
                {
                    RestoreVisibility();
                }
                else
                {
                    IsDistant = true;
                    if (_renderer != null && !_renderer.forceRenderingOff)
                    {
                        _renderer.forceRenderingOff = true;
                        _ownsRenderFlag = true;
                    }
                }
                if (_ambientOwner != null) UpdateSuspension(now, position, hasBounds ? bounds : _renderer.bounds);
            }

            private void UpdateSuspension(float now, Vector3 position, Bounds bounds)
            {
                if (!CanResume() || (SleepingIndex >= 0 && !_system.isPaused))
                {
                    ForgetSuspension();
                    return;
                }
                bool validBounds = float.IsFinite(bounds.center.sqrMagnitude) && float.IsFinite(bounds.extents.sqrMagnitude) && float.IsFinite(position.sqrMagnitude);
                _sleepPosition = position;
                _sleepRadius = bounds.extents.magnitude;
                _sleepBounds = bounds;
                _sleepBounds.Encapsulate(position);
                _sleepBounds.Expand(BOUNDS_PADDING);
                if (_eyeCount == 0 || !validBounds || (!IsDistant && !IsOutsideViews()))
                {
                    Resume(0f);
                    return;
                }
                if (SleepingIndex >= 0 || now < _stayAwakeUntil) return;
                if (_hiddenSince < 0f) _hiddenSince = now;
                if (now - _hiddenSince < HIDDEN_DELAY || !_system.isPlaying || _system.isPaused) return;
                _system.Pause(false);
                SleepingIndex = _sleeping.Count;
                _sleeping.Add(this);
            }

            private bool IsOutsideViews()
            {
                if (_viewCount == 0 || _renderer.shadowCastingMode != ShadowCastingMode.Off) return false;
                int layer = 1 << _system.gameObject.layer;
                for (int index = 0; index < _viewCount; index++)
                {
                    if (_views[index].Includes(layer, _sleepBounds)) return false;
                }
                return true;
            }

            public void WakeForView(CameraView view, Vector3 position, bool isGame, float now)
            {
                if (!CanResume() || !_system.isPaused)
                {
                    ForgetSuspension();
                    return;
                }
                if (IsDistant)
                {
                    if (!isGame) return;
                    float range = DrawDistance + _sleepRadius;
                    if ((position - _sleepPosition).sqrMagnitude > range * range && _sleepBounds.SqrDistance(position) > DrawDistance * DrawDistance) return;
                    RestoreVisibility();
                }
                else if (_renderer.shadowCastingMode == ShadowCastingMode.Off && !view.Includes(1 << _system.gameObject.layer, _sleepBounds))
                {
                    return;
                }
                Resume(now + WAKE_GRACE);
            }

            private bool CanResume()
            {
                return _ambientOwner != null && _ambientOwner.isActiveAndEnabled && _ambientOwner.IsVisible && _system.gameObject.activeInHierarchy;
            }

            public void Resume(float stayAwakeUntil)
            {
                if (SleepingIndex >= 0 && CanResume() && _system.isPaused) _system.Play(false);
                ForgetSuspension();
                _stayAwakeUntil = stayAwakeUntil;
            }

            public void ForgetSuspension()
            {
                _hiddenSince = -1f;
                if (SleepingIndex < 0) return;
                int lastIndex = _sleeping.Count - 1;
                ParticleSettings last = _sleeping[lastIndex];
                _sleeping[SleepingIndex] = last;
                last.SleepingIndex = SleepingIndex;
                _sleeping.RemoveAt(lastIndex);
                SleepingIndex = -1;
            }

            public void RestoreVisibility()
            {
                IsDistant = false;
                if (_ownsRenderFlag && _renderer != null) _renderer.forceRenderingOff = false;
                _ownsRenderFlag = false;
            }

            private static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve curve, float density, float minimum)
            {
                if (curve.mode == ParticleSystemCurveMode.Constant || curve.mode == ParticleSystemCurveMode.TwoConstants)
                {
                    curve.constantMin = Mathf.Min(curve.constantMin, Mathf.Max(minimum, curve.constantMin * density));
                    curve.constantMax = Mathf.Min(curve.constantMax, Mathf.Max(minimum, curve.constantMax * density));
                }
                else
                {
                    curve.curveMultiplier *= density;
                }
                return curve;
            }
        }
    }
}
