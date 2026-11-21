using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace RaidIntoDarkness.Effects
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OverlappingAnomalyVisual), typeof(AnomalyParticleEmission))]
    public sealed class GazirovkaVfxPrototype : MonoBehaviour
    {
        private const string ASSET_NAME = "GazirovkaVfxPrototype";
        private const string IDLE_EVENT = "OnPlay";
        private const string DISCHARGE_EVENT = "GazirovkaDischarge";
        private const string INACTIVE_EVENT = "GazirovkaInactive";
        private const string EMISSION_SCALE_PROPERTY = "EmissionScale";
        private const string AMBIENT_EMISSION_PROPERTY = "AmbientEmission";
        private const string IDLE_COUNTERS_PROPERTY = "IdleCounters";
        private const string IDLE_COUNTER_OFFSET_PROPERTY = "IdleCounterOffset";
        private const int REQUIRED_SHADER_LEVEL = 45;
        private const float DISCHARGE_LIFETIME = 2f;
        private const float BOUNDS_EXTENT = 20f;
        private const int MAX_POOLED_DISCHARGES = 8;
        private const int IDLE_LAYER_COUNT = 8;
        private const int IDLE_COUNTERS_PAGE_SIZE = 128;

        private static readonly int EMISSION_SCALE = Shader.PropertyToID(EMISSION_SCALE_PROPERTY);
        private static readonly int AMBIENT_EMISSION = Shader.PropertyToID(AMBIENT_EMISSION_PROPERTY);
        private static readonly int IDLE_COUNTERS = Shader.PropertyToID(IDLE_COUNTERS_PROPERTY);
        private static readonly int IDLE_COUNTER_OFFSET = Shader.PropertyToID(IDLE_COUNTER_OFFSET_PROPERTY);
        private static readonly uint[] EMPTY_IDLE_COUNTERS = new uint[IDLE_LAYER_COUNT];
        private static readonly List<IdleCounterPage> IDLE_COUNTER_PAGES = new();

        private static VisualEffectAsset _asset;
        private static bool _modeInitialized;
        private static bool _isActive;

        private readonly List<Discharge> _discharges = new();
        private readonly Stack<Discharge> _spareDischarges = new();

        private OverlappingAnomalyVisual _visual;
        private AnomalyParticleEmission _emission;
        private ParticleSystem _idleSource;
        private VisualEffect _idleEffect;
        private Renderer _idleRenderer;
        private IdleCounterPage _idleCounterPage;
        private int _idleCounterSlot;
        private bool _bound;
        private bool _restoreEmission;
        private bool _restoreIdle;
        private bool _isDistant;
        private float _hiddenSince = -1f;
        private float _stayAwakeUntil;
        private float _appliedDensity = -1f;
        private Matrix4x4 _boundsMatrix;
        private Bounds _worldBounds;

        public static bool IsActive => _isActive;

        internal bool IsDischarging => _discharges.Count > 0;
        internal int SleepingIndex { get; set; } = -1;

        public static bool SetActive(bool active)
        {
            if (!Application.isPlaying) return false;
            if (_isActive == active)
            {
                _modeInitialized = true;
                return true;
            }
            if (active && !TryLoadAsset()) return false;

            GazirovkaVfxPrototype[] prototypes = FindObjectsByType<GazirovkaVfxPrototype>(FindObjectsInactive.Exclude);
            var eligible = new List<GazirovkaVfxPrototype>(prototypes.Length);
            foreach (GazirovkaVfxPrototype prototype in prototypes)
            {
                if (!prototype.isActiveAndEnabled || (active ? !prototype.ResolveSources() : !prototype._bound)) continue;
                if (prototype._visual.IsDischarging) return false;
                eligible.Add(prototype);
            }
            if (active)
            {
                if (eligible.Count == 0) return false;
                foreach (GazirovkaVfxPrototype prototype in eligible)
                {
                    if (prototype.TryPrepareIdleEffect()) continue;
                    foreach (GazirovkaVfxPrototype prepared in eligible) prepared.ReleaseIdleEffect();
                    return false;
                }
            }

            _modeInitialized = true;
            _isActive = active;
            foreach (GazirovkaVfxPrototype prototype in eligible) prototype.ApplyBackend(active);
            return true;
        }

        [ContextMenu("Prototype/Use VFX Graph in Play Mode")]
        private void UseVfxGraph()
        {
            if (!SetActive(true)) Debug.LogWarning("Gazirovka VFX requires Play Mode, the graph asset and idle effects.", this);
        }

        [ContextMenu("Prototype/Use Particle System in Play Mode")]
        private void UseParticleSystem()
        {
            if (!SetActive(false)) Debug.LogWarning("Switch Gazirovka to Particle System in Play Mode after active discharges finish.", this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMode()
        {
            foreach (IdleCounterPage page in IDLE_COUNTER_PAGES) page.Buffer.Dispose();
            IDLE_COUNTER_PAGES.Clear();
            _asset = null;
            _modeInitialized = false;
            _isActive = false;
        }

        private static bool TryLoadAsset()
        {
            if (_asset != null) return true;
            if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsShaderLevel < REQUIRED_SHADER_LEVEL) return false;
            VisualEffectAsset asset = Resources.Load<VisualEffectAsset>(ASSET_NAME);
            if (asset == null) return false;

            var properties = new List<VFXExposedProperty>();
            asset.GetExposedProperties(properties);
            if (!HasProperty(properties, EMISSION_SCALE_PROPERTY, typeof(float)) || !HasProperty(properties, AMBIENT_EMISSION_PROPERTY, typeof(float)) || !HasProperty(properties, IDLE_COUNTERS_PROPERTY, typeof(GraphicsBuffer)) || !HasProperty(properties, IDLE_COUNTER_OFFSET_PROPERTY, typeof(uint))) return false;

            var events = new List<string>();
            asset.GetEvents(events);
            if (!events.Contains(IDLE_EVENT) || !events.Contains(DISCHARGE_EVENT)) return false;
            _asset = asset;
            return true;
        }

        private static bool HasProperty(List<VFXExposedProperty> properties, string name, Type type)
        {
            foreach (VFXExposedProperty property in properties)
            {
                if (property.name == name && property.type == type) return true;
            }
            return false;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || (_modeInitialized && !_isActive) || !ResolveSources()) return;
            bool initialize = !_modeInitialized;
            if (initialize)
            {
                _modeInitialized = true;
                if (!TryLoadAsset()) return;
            }
            if (TryPrepareIdleEffect())
            {
                _isActive = true;
                ApplyBackend(true);
            }
        }

        private void OnDisable()
        {
            if (_bound) ApplyBackend(false);
        }

        private bool ResolveSources()
        {
            if (_visual == null) _visual = GetComponent<OverlappingAnomalyVisual>();
            if (_emission == null) _emission = GetComponent<AnomalyParticleEmission>();
            if (_visual != null) _idleSource = _visual.IdleParticleSystem;
            return _visual != null && _emission != null && _idleSource != null && _idleSource.transform != transform && _idleSource.transform.IsChildOf(transform);
        }

        private bool TryPrepareIdleEffect()
        {
            try
            {
                RentIdleCounters();
                if (!_idleCounterPage.Buffer.IsValid()) throw new InvalidOperationException("The idle counter buffer is unavailable.");
                _idleEffect = CreateEffect("Gazirovka VFX Idle");
                _idleRenderer = _idleEffect.GetComponent<Renderer>();
                if (_idleRenderer == null) throw new InvalidOperationException("The VFX renderer is unavailable.");
                return true;
            }
            catch (Exception exception)
            {
                ReleaseIdleEffect();
                Debug.LogWarning("Gazirovka kept Particle System: " + exception.Message, this);
                return false;
            }
        }

        private void ApplyBackend(bool active)
        {
            if (_bound == active) return;
            _bound = active;
            if (active)
            {
                _restoreEmission = _emission.enabled;
                _restoreIdle = _idleSource.gameObject.activeSelf;
                _emission.enabled = false;
                _idleSource.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                AnomalyVisualQuality.UnregisterPrototypeSources(_idleSource.GetComponentsInChildren<ParticleSystem>(true));
                _idleSource.gameObject.SetActive(false);
                AnomalyVisualQuality.AmbientVisibilityChanged += UpdateVisibility;
                _visual.SetParticlePrototype(this);
                return;
            }

            AnomalyVisualQuality.AmbientVisibilityChanged -= UpdateVisibility;
            SetIdle(false);
            StopDischarges();
            TrimPool(0);
            ReleaseIdleEffect();
            AnomalyVisualQuality.SetPrototypeWaiting(this, false);
            _hiddenSince = -1f;
            _stayAwakeUntil = 0f;
            _isDistant = false;
            _appliedDensity = -1f;
            if (_idleSource != null) _idleSource.gameObject.SetActive(_restoreIdle);
            if (_visual != null) _visual.SetParticlePrototype(null);
            if (_emission != null) _emission.enabled = _restoreEmission;
        }

        internal void SetIdle(bool visible)
        {
            if (_idleEffect == null || _idleEffect.gameObject.activeSelf == visible) return;
            if (visible)
            {
                UpdateDensity();
                _idleCounterPage.Buffer.SetData(EMPTY_IDLE_COUNTERS, 0, _idleCounterSlot * IDLE_LAYER_COUNT, IDLE_LAYER_COUNT);
                StartEffect(_idleEffect, IDLE_EVENT);
                UpdateVisibility();
            }
            else
            {
                _idleEffect.gameObject.SetActive(false);
                Resume(0f);
            }
        }

        internal bool StartDischarge(float delay)
        {
            TrimDischarges();
            bool atLimit = _discharges.Count >= AnomalyVisualQuality.DischargeLimit;
            Discharge discharge = TakeDischarge(atLimit);
            discharge.Elapsed = -Mathf.Max(0f, delay);
            discharge.RestartFrame = atLimit && delay <= 0f ? Time.frameCount + 1 : 0;
            SetDensity(discharge.Effect, AnomalyVisualQuality.Density);
            discharge.SetDistant(_isDistant || discharge.RestartFrame != 0);
            _discharges.Add(discharge);
            if (discharge.RestartFrame != 0) return true;
            if (delay <= 0f) StartEffect(discharge.Effect, DISCHARGE_EVENT);
            else if (discharge.Effect.gameObject.activeSelf) discharge.Effect.gameObject.SetActive(false);
            return true;
        }

        private Discharge TakeDischarge(bool atLimit)
        {
            if (atLimit)
            {
                Discharge oldest = _discharges[0];
                _discharges.RemoveAt(0);
                return oldest;
            }

            return _spareDischarges.Count > 0 ? _spareDischarges.Pop() : new Discharge(CreateEffect("Gazirovka VFX Discharge"));
        }

        internal void TickDischarges(float deltaTime)
        {
            TrimDischarges();
            for (int index = _discharges.Count - 1; index >= 0; index--)
            {
                Discharge discharge = _discharges[index];
                if (discharge.RestartFrame != 0)
                {
                    if (Time.frameCount < discharge.RestartFrame) continue;
                    discharge.RestartFrame = 0;
                    StartEffect(discharge.Effect, DISCHARGE_EVENT);
                    discharge.SetDistant(_isDistant);
                    _discharges[index] = discharge;
                    continue;
                }
                float previous = discharge.Elapsed;
                discharge.Elapsed += deltaTime;
                if (discharge.Elapsed >= DISCHARGE_LIFETIME)
                {
                    ReleaseDischarge(index);
                    continue;
                }
                if (previous < 0f && discharge.Elapsed >= 0f) StartEffect(discharge.Effect, DISCHARGE_EVENT);
                _discharges[index] = discharge;
            }
        }

        internal void StopDischarges()
        {
            for (int index = _discharges.Count - 1; index >= 0; index--) ReleaseDischarge(index);
        }

        private void TrimDischarges()
        {
            int limit = AnomalyVisualQuality.DischargeLimit;
            while (_discharges.Count > limit) ReleaseDischarge(0);
            TrimPool(Mathf.Min(MAX_POOLED_DISCHARGES, limit) - _discharges.Count);
        }

        private void ReleaseDischarge(int index)
        {
            Discharge discharge = _discharges[index];
            _discharges.RemoveAt(index);
            if (discharge.Effect == null) return;
            discharge.Effect.gameObject.SetActive(false);
            int retained = Mathf.Min(MAX_POOLED_DISCHARGES, AnomalyVisualQuality.DischargeLimit);
            if (_spareDischarges.Count + _discharges.Count < retained) _spareDischarges.Push(discharge);
            else Destroy(discharge.Effect.gameObject);
        }

        private void TrimPool(int capacity)
        {
            capacity = Mathf.Max(0, capacity);
            while (_spareDischarges.Count > capacity)
            {
                VisualEffect effect = _spareDischarges.Pop().Effect;
                if (effect != null) Destroy(effect.gameObject);
            }
        }

        private void UpdateVisibility()
        {
            if (_idleEffect == null) return;
            UpdateDensity();
            bool idleActive = _idleEffect.gameObject.activeSelf;
            if (!idleActive && _discharges.Count == 0) return;

            Bounds bounds = WorldBounds();
            SetDistant(AnomalyVisualQuality.IsPrototypeDistant(bounds, _isDistant));
            if (!idleActive)
            {
                AnomalyVisualQuality.SetPrototypeWaiting(this, _isDistant);
                return;
            }
            if (!_isDistant && AnomalyVisualQuality.IsPrototypeInView(bounds, 1 << gameObject.layer))
            {
                Resume(0f);
                return;
            }

            float now = Time.unscaledTime;
            if (_isDistant) AnomalyVisualQuality.SetPrototypeWaiting(this, true);
            if (now < _stayAwakeUntil || _idleEffect.pause) return;
            if (_hiddenSince < 0f) _hiddenSince = now;
            if (now - _hiddenSince < AnomalyVisualQuality.HIDDEN_DELAY) return;
            _idleEffect.pause = true;
            AnomalyVisualQuality.SetPrototypeWaiting(this, true);
        }

        internal void WakeForView(AnomalyVisualQuality.CameraView view, Vector3 position, bool isGame, float distance, float now)
        {
            if (_idleEffect == null || (!_idleEffect.gameObject.activeInHierarchy && _discharges.Count == 0))
            {
                AnomalyVisualQuality.SetPrototypeWaiting(this, false);
                return;
            }

            Bounds bounds = WorldBounds();
            if (_isDistant)
            {
                if (!isGame || bounds.SqrDistance(position) > distance * distance) return;
                SetDistant(false);
            }
            else if (!view.Includes(1 << gameObject.layer, bounds)) return;

            Resume(now + AnomalyVisualQuality.WAKE_GRACE);
        }

        private Bounds WorldBounds()
        {
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            if (_boundsMatrix.Equals(localToWorld)) return _worldBounds;
            Vector3 size = (Absolute(localToWorld.MultiplyVector(Vector3.right)) + Absolute(localToWorld.MultiplyVector(Vector3.up)) + Absolute(localToWorld.MultiplyVector(Vector3.forward))) * (2f * BOUNDS_EXTENT);
            _boundsMatrix = localToWorld;
            _worldBounds = new Bounds(localToWorld.GetPosition(), size);
            return _worldBounds;
        }

        private void SetDistant(bool distant)
        {
            if (_isDistant == distant) return;
            _isDistant = distant;
            if (_idleRenderer != null) _idleRenderer.forceRenderingOff = distant;
            if (_idleEffect != null && _idleEffect.HasFloat(AMBIENT_EMISSION)) _idleEffect.SetFloat(AMBIENT_EMISSION, distant ? 0f : 1f);
            foreach (Discharge discharge in _discharges) discharge.SetDistant(distant || discharge.RestartFrame != 0);
        }

        private void Resume(float stayAwakeUntil)
        {
            _hiddenSince = -1f;
            _stayAwakeUntil = stayAwakeUntil;
            if (_idleEffect != null && _idleEffect.pause) _idleEffect.pause = false;
            AnomalyVisualQuality.SetPrototypeWaiting(this, false);
        }

        private void UpdateDensity()
        {
            float density = AnomalyVisualQuality.Density;
            if (_appliedDensity == density) return;
            _appliedDensity = density;
            SetDensity(_idleEffect, density);
            foreach (Discharge discharge in _discharges) SetDensity(discharge.Effect, density);
        }

        private VisualEffect CreateEffect(string effectName)
        {
            GameObject instance = new GameObject(effectName);
            instance.SetActive(false);
            instance.layer = gameObject.layer;
            instance.transform.SetParent(transform, false);
            try
            {
                VisualEffect effect = instance.AddComponent<VisualEffect>();
                effect.initialEventName = INACTIVE_EVENT;
                effect.visualEffectAsset = _asset;
                effect.SetGraphicsBuffer(IDLE_COUNTERS, _idleCounterPage.Buffer);
                effect.SetUInt(IDLE_COUNTER_OFFSET, (uint)(_idleCounterSlot * IDLE_LAYER_COUNT));
                return effect;
            }
            catch
            {
                Destroy(instance);
                throw;
            }
        }

        private void ReleaseIdleEffect()
        {
            if (_idleEffect != null) Destroy(_idleEffect.gameObject);
            _idleEffect = null;
            _idleRenderer = null;
            if (_idleCounterPage != null) ReleaseIdleCounters();
        }

        private void RentIdleCounters()
        {
            foreach (IdleCounterPage page in IDLE_COUNTER_PAGES)
            {
                if (!page.HasSpace) continue;
                _idleCounterPage = page;
                _idleCounterSlot = page.Rent();
                return;
            }

            _idleCounterPage = new IdleCounterPage();
            IDLE_COUNTER_PAGES.Add(_idleCounterPage);
            _idleCounterSlot = _idleCounterPage.Rent();
        }

        private void ReleaseIdleCounters()
        {
            _idleCounterPage.Release(_idleCounterSlot);
            if (_idleCounterPage.IsEmpty)
            {
                IDLE_COUNTER_PAGES.Remove(_idleCounterPage);
                _idleCounterPage.Buffer.Dispose();
            }
            _idleCounterPage = null;
        }

        private static void StartEffect(VisualEffect effect, string eventName)
        {
            if (!effect.gameObject.activeSelf) effect.gameObject.SetActive(true);
            effect.pause = false;
            effect.Reinit();
            effect.SendEvent(eventName);
        }

        private static void SetDensity(VisualEffect effect, float density)
        {
            if (effect.HasFloat(EMISSION_SCALE)) effect.SetFloat(EMISSION_SCALE, density);
        }

        private static Vector3 Absolute(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private sealed class IdleCounterPage
        {
            private readonly Stack<int> _freeSlots = new();
            private int _nextSlot;

            public GraphicsBuffer Buffer { get; } = new(GraphicsBuffer.Target.Structured, IDLE_COUNTERS_PAGE_SIZE * IDLE_LAYER_COUNT, sizeof(uint));
            public bool HasSpace => _freeSlots.Count != 0 || _nextSlot < IDLE_COUNTERS_PAGE_SIZE;
            public bool IsEmpty => _freeSlots.Count == _nextSlot;

            public int Rent()
            {
                return _freeSlots.Count != 0 ? _freeSlots.Pop() : _nextSlot++;
            }

            public void Release(int slot)
            {
                _freeSlots.Push(slot);
            }
        }

        private struct Discharge
        {
            private readonly Renderer _renderer;

            public VisualEffect Effect { get; }
            public float Elapsed { get; set; }
            public int RestartFrame { get; set; }

            public Discharge(VisualEffect effect)
            {
                Effect = effect;
                _renderer = effect.GetComponent<Renderer>();
                Elapsed = 0f;
                RestartFrame = 0;
            }

            public void SetDistant(bool distant)
            {
                if (_renderer != null) _renderer.forceRenderingOff = distant;
            }
        }
    }
}
