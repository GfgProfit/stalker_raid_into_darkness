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
        private const int PRUNE_INTERVAL = 64;

        private static readonly Dictionary<ParticleSystem, ParticleSettings> _systems = new();
        private static readonly List<ParticleSystem> _destroyed = new();
        private static Camera[] _cameras = Array.Empty<Camera>();
        private static Vector3[] _eyePositions = Array.Empty<Vector3>();
        private static int _tier = -1;
        private static int _eyeCount;
        private static int _registeredSincePrune;
        private static float _nextVisibilityUpdate;

        internal static float Density => _tier == 0 ? 0.45f : _tier == 1 ? 0.7f : 1f;
        internal static int ChildLimit => _tier == 0 ? 16 : _tier == 1 ? 32 : _tier == 2 ? 64 : int.MaxValue;
        internal static int ContactLimit => _tier == 0 ? 4 : _tier == 1 ? 8 : _tier == 2 ? 16 : int.MaxValue;
        internal static int DischargeLimit => _tier == 0 ? 2 : _tier == 1 ? 4 : _tier == 2 ? 8 : int.MaxValue;
        private static float DrawDistance => _tier == 0 ? 80f : _tier == 1 ? 140f : 240f;

        public static void Apply(int? quality)
        {
            int tier = quality.HasValue ? Mathf.Clamp(quality.Value, 0, 2) : -1;
            if (_tier == tier) return;
            if (_tier < 0 && tier >= 0)
            {
                RenderPipelineManager.beginContextRendering += UpdateVisibility;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
            }
            else if (tier < 0)
            {
                RenderPipelineManager.beginContextRendering -= UpdateVisibility;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
            }

            _tier = tier;
            Prune();
            foreach (ParticleSettings state in _systems.Values)
            {
                state.ApplyDensity(Density);
                if (_tier < 0) state.RestoreVisibility();
            }
            _nextVisibilityUpdate = 0f;
            if (_tier < 0) _eyeCount = 0;
        }

        internal static void RegisterHierarchy(ParticleSystem root)
        {
            if (!Application.isPlaying || root == null) return;
            RegisterPlayableHierarchy(root.transform, root.GetComponentsInChildren<ParticleSystem>(true));
        }

        internal static void RegisterPlayableHierarchy(Transform root, ParticleSystem[] systems)
        {
            if (!Application.isPlaying || root == null) return;
            foreach (ParticleSystem system in systems)
            {
                Transform node = system.transform;
                while (node != root && node.gameObject.activeSelf)
                {
                    node = node.parent;
                }
                if (node == root) Register(system);
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

        internal static bool CanDraw(Vector3 position)
        {
            return IsNearAnyCamera(position, DrawDistance);
        }

        internal static bool CanEmitAmbient(ParticleSystem system)
        {
            return _tier < 0 || !_systems.TryGetValue(system, out ParticleSettings state) || !state.IsDistant;
        }

        private static void UpdateVisibility(ScriptableRenderContext context, List<Camera> cameras)
        {
            float now = Time.unscaledTime;
            if (now < _nextVisibilityUpdate) return;
            _nextVisibilityUpdate = now + VISIBILITY_INTERVAL;
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
                    if (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.VR)
                    {
                        _eyePositions[_eyeCount++] = camera.transform.position;
                    }
                    _cameras[index] = null;
                }
            }
            Prune();
            foreach (ParticleSettings state in _systems.Values)
            {
                state.UpdateVisibility();
            }
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
                _systems.Remove(system);
            }
            _destroyed.Clear();
            _registeredSincePrune = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Apply(null);
            _systems.Clear();
        }

        private sealed class ParticleSettings
        {
            private readonly ParticleSystem _system;
            private readonly ParticleSystemRenderer _renderer;
            private ParticleSystem.MinMaxCurve _timeRate;
            private ParticleSystem.MinMaxCurve _distanceRate;
            private ParticleSystem.Burst[] _bursts;
            private int _capacity;
            private bool _hasBaseline;
            private bool _ownsRenderFlag;

            public bool IsDistant { get; private set; }

            public ParticleSettings(ParticleSystem system)
            {
                _system = system;
                _renderer = system.GetComponent<ParticleSystemRenderer>();
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

            public void UpdateVisibility()
            {
                if (!_system.gameObject.activeInHierarchy) return;
                float distance = IsDistant ? DrawDistance : DrawDistance * DRAW_HYSTERESIS;
                Vector3 position = _system.transform.position;
                bool inRange = IsNearAnyCamera(position, distance);
                if (!inRange && _renderer != null && _renderer.enabled)
                {
                    Bounds bounds = _renderer.bounds;
                    inRange = IsNearAnyCamera(bounds.center, distance + bounds.extents.magnitude);
                }
                if (inRange)
                {
                    RestoreVisibility();
                    return;
                }
                IsDistant = true;
                if (_renderer != null && !_renderer.forceRenderingOff)
                {
                    _renderer.forceRenderingOff = true;
                    _ownsRenderFlag = true;
                }
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
