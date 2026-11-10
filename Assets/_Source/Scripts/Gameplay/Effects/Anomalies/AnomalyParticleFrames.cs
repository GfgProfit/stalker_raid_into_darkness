using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class AnomalyParticleFrames : MonoBehaviour
    {
        private const float SOURCE_STEP = 0.033f;
        private const int MAX_SOURCE_STEPS = 3;

        private static readonly int FRAME_SIZE_ID = Shader.PropertyToID("_FrameSize");
        private static readonly int FRAME_COLUMNS_ID = Shader.PropertyToID("_FrameColumns");

        [SerializeField] private ParticleSystem _system;
        [SerializeField] private int _frameCount = 1;
        [SerializeField] private int _frameColumns = 1;
        [SerializeField] private Vector2 _frameSize = Vector2.one;
        [SerializeField] private float _framesPerSecond;
        [SerializeField] private bool _animated;
        [SerializeField] private bool _randomStart;
        [SerializeField] private bool _randomPlayback;

        private ParticleSystem.Particle[] _particles;
        private List<Vector4> _frames;
        private Dictionary<uint, PersistentFrame> _persistentFrames;
        private HashSet<uint> _seenPersistentSeeds;
        private List<uint> _deadPersistentSeeds;
        private ParticleSystemRenderer _renderer;
        private MaterialPropertyBlock _originalProperties;
        private MaterialPropertyBlock _properties;
        private bool _hasOriginalProperties;

        private struct PersistentFrame
        {
            public PersistentFrame(float frame, bool backward)
            {
                Frame = frame;
                Remainder = 0.0;
                Backward = backward;
            }

            public float Frame { get; set; }
            public double Remainder { get; set; }
            public bool Backward { get; }
        }

        public void Configure(ParticleSystem system, int frameCount, int frameColumns, Vector2 frameSize, float framesPerSecond, bool animated, bool randomStart, bool randomPlayback)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }
            if (frameCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            }
            if (frameColumns < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(frameColumns));
            }
            if (!IsPositiveFinite(frameSize.x) || !IsPositiveFinite(frameSize.y))
            {
                throw new ArgumentOutOfRangeException(nameof(frameSize));
            }
            if (float.IsNaN(framesPerSecond) || float.IsInfinity(framesPerSecond) || framesPerSecond < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
            }
            bool resetPersistent = _system != system || _frameCount != frameCount || _framesPerSecond != framesPerSecond || _animated != animated || _randomStart != randomStart || _randomPlayback != randomPlayback;
            if (_system != system)
            {
                RestoreAtlas();
                _renderer = null;
            }
            _system = system;
            _frameCount = frameCount;
            _frameColumns = frameColumns;
            _frameSize = frameSize;
            _framesPerSecond = framesPerSecond;
            _animated = animated;
            _randomStart = randomStart;
            _randomPlayback = randomPlayback;
            EnsureStorage();
            if (resetPersistent)
            {
                ResetPersistentFrames();
            }
            if (isActiveAndEnabled)
            {
                ApplyAtlas();
            }
            UpdateFrames(0f);
        }

        private void Awake()
        {
            ResolveSystem();
            EnsureStorage();
        }

        private void OnEnable()
        {
            ResolveSystem();
            EnsureStorage();
            ApplyAtlas();
        }

        private void OnDisable()
        {
            RestoreAtlas();
            ResetPersistentFrames();
        }

        private void LateUpdate()
        {
            if (_system != null)
            {
                UpdateFrames(_system.main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
            }
        }

        private void ResolveSystem()
        {
            if (_system == null)
            {
                _system = GetComponent<ParticleSystem>();
            }
        }

        private void UpdateFrames(float deltaTime)
        {
            if (_system == null)
            {
                return;
            }
            EnsureStorage();
            int count = _system.GetParticles(_particles);
            UpdatePersistentSeeds(count);
            double elapsed = _system.isPlaying && !float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime) ? Math.Max(0.0, deltaTime) * _system.main.simulationSpeed : 0.0;
            _frames.Clear();
            for (int index = 0; index < count; index++)
            {
                ParticleSystem.Particle particle = _particles[index];
                float frame;
                if (float.IsPositiveInfinity(particle.startLifetime))
                {
                    frame = PersistentFrameValue(particle.randomSeed, elapsed);
                }
                else
                {
                    float age = particle.startLifetime - particle.remainingLifetime;
                    frame = SpatialBubbleParticleFrames.EvaluateFrame(age, particle.randomSeed, _frameCount, _framesPerSecond, _animated, _randomStart, _randomPlayback);
                }
                _frames.Add(new Vector4(Mathf.Floor(frame), 0f, 0f, 0f));
            }
            _system.SetCustomParticleData(_frames, ParticleSystemCustomData.Custom1);
        }

        private float PersistentFrameValue(uint seed, double elapsed)
        {
            if (!_persistentFrames.TryGetValue(seed, out PersistentFrame state))
            {
                uint random = unchecked(seed * 747796405u + 2891336453u);
                random ^= random >> 16;
                float first = SpatialBubbleParticleFrames.EvaluateFrame(0f, seed, _frameCount, _framesPerSecond, _animated, _randomStart, _randomPlayback);
                state = new PersistentFrame(first, _randomPlayback && (random & 0x80000000u) != 0u);
            }
            else if (_animated && elapsed > 0.0)
            {
                double accumulated = state.Remainder + elapsed;
                double completeSteps = Math.Floor(accumulated / SOURCE_STEP);
                state.Remainder = accumulated - completeSteps * SOURCE_STEP;
                int steps = (int)Math.Min(MAX_SOURCE_STEPS, completeSteps);
                for (int index = 0; index < steps; index++)
                {
                    state.Frame = SpatialBubbleParticleFrames.AdvanceFrame(state.Frame, _framesPerSecond, _frameCount, state.Backward);
                }
            }
            _persistentFrames[seed] = state;
            return state.Frame;
        }

        private void UpdatePersistentSeeds(int count)
        {
            _seenPersistentSeeds.Clear();
            for (int index = 0; index < count; index++)
            {
                if (float.IsPositiveInfinity(_particles[index].startLifetime))
                {
                    _seenPersistentSeeds.Add(_particles[index].randomSeed);
                }
            }
            _deadPersistentSeeds.Clear();
            foreach (KeyValuePair<uint, PersistentFrame> item in _persistentFrames)
            {
                if (!_seenPersistentSeeds.Contains(item.Key))
                {
                    _deadPersistentSeeds.Add(item.Key);
                }
            }
            foreach (uint seed in _deadPersistentSeeds)
            {
                _persistentFrames.Remove(seed);
            }
        }

        private void ResetPersistentFrames()
        {
            _persistentFrames?.Clear();
            _seenPersistentSeeds?.Clear();
            _deadPersistentSeeds?.Clear();
        }

        private void ApplyAtlas()
        {
            if (_system == null)
            {
                return;
            }
            if (_renderer == null)
            {
                _renderer = _system.GetComponent<ParticleSystemRenderer>();
            }
            if (_renderer == null)
            {
                return;
            }
            if (_originalProperties == null)
            {
                _originalProperties = new MaterialPropertyBlock();
                _properties = new MaterialPropertyBlock();
            }
            if (!_hasOriginalProperties)
            {
                _renderer.GetPropertyBlock(_originalProperties);
                _hasOriginalProperties = true;
            }
            _renderer.GetPropertyBlock(_properties);
            _properties.SetVector(FRAME_SIZE_ID, new Vector4(_frameSize.x, _frameSize.y, 0f, 0f));
            _properties.SetFloat(FRAME_COLUMNS_ID, _frameColumns);
            _renderer.SetPropertyBlock(_properties);
        }

        private void RestoreAtlas()
        {
            if (_renderer != null && _hasOriginalProperties)
            {
                _renderer.SetPropertyBlock(_originalProperties.isEmpty ? null : _originalProperties);
            }
            _hasOriginalProperties = false;
        }

        private void EnsureStorage()
        {
            int maximum = _system != null ? Mathf.Max(1, _system.main.maxParticles) : 1;
            if (_particles != null && _particles.Length == maximum)
            {
                return;
            }
            _particles = new ParticleSystem.Particle[maximum];
            _frames = new List<Vector4>(maximum);
            _persistentFrames = new Dictionary<uint, PersistentFrame>(maximum);
            _seenPersistentSeeds = new HashSet<uint>(maximum);
            _deadPersistentSeeds = new List<uint>(maximum);
        }

        private static bool IsPositiveFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }
    }
}
