using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [Serializable]
    public sealed class AnomalyParticleEmissionSource
    {
        [SerializeField] private ParticleSystem _system;
        [SerializeField, Min(0f)] private float _rate;
        [SerializeField, Min(0f)] private float _timeLimit;

        public ParticleSystem System => _system;
        public float Rate => _rate;
        public float TimeLimit => _timeLimit;

        public AnomalyParticleEmissionSource(ParticleSystem system, float rate, float timeLimit = 0f)
        {
            if (system == null || float.IsNaN(rate) || float.IsInfinity(rate) || rate < 0f || float.IsNaN(timeLimit) || float.IsInfinity(timeLimit) || timeLimit < 0f)
            {
                throw new ArgumentException("An anomaly particle source requires a system and finite nonnegative rate and time limit.");
            }

            _system = system;
            _rate = rate;
            _timeLimit = timeLimit;
        }
    }

    [DisallowMultipleComponent]
    public sealed class AnomalyParticleEmission : MonoBehaviour
    {
        public const float SOURCE_STEP = 0.033f;
        public const int MAX_STEPS = 3;

        private const float CLOCK_EPSILON = 0.00001f;

        [SerializeField] private List<AnomalyParticleEmissionSource> _sources = new List<AnomalyParticleEmissionSource>();

        private SourceState[] _states;
        private AnomalyVisual _visual;

        public IReadOnlyList<AnomalyParticleEmissionSource> Sources => _sources;

        private struct SourceState
        {
            public ParticleSystem System { get; set; }
            public float Accumulated { get; set; }
            public float Elapsed { get; set; }
            public float LastTime { get; set; }
            public bool Playing { get; set; }
        }

        public void Configure(AnomalyParticleEmissionSource[] sources)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            _sources = new List<AnomalyParticleEmissionSource>(sources);
            _states = new SourceState[_sources.Count];
        }

        public static int SampleCount(float rate, float randomValue)
        {
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate < 0f || float.IsNaN(randomValue) || randomValue < 0f || randomValue > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(rate));
            }

            float exact = rate * SOURCE_STEP;
            if (exact >= int.MaxValue)
            {
                return int.MaxValue;
            }

            int count = Mathf.FloorToInt(exact);
            return randomValue < exact - count ? count + 1 : count;
        }

        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (!isActiveAndEnabled || _sources == null)
            {
                return;
            }

            EnsureStates();
            for (int index = 0; index < _sources.Count; index++)
            {
                AnomalyParticleEmissionSource source = _sources[index];
                ParticleSystem system = source != null ? source.System : null;
                ref SourceState state = ref _states[index];
                if (system == null || !system.gameObject.activeInHierarchy)
                {
                    state = default;
                    continue;
                }

                if (system.isPaused)
                {
                    continue;
                }

                if (!system.isPlaying)
                {
                    state = default;
                    continue;
                }

                ParticleSystem.MainModule main = system.main;
                float scaledDelta = deltaTime * main.simulationSpeed;
                float currentTime = system.time;
                bool loopWrapped = main.loop && state.LastTime >= main.duration - scaledDelta - CLOCK_EPSILON && currentTime <= scaledDelta + CLOCK_EPSILON;
                if (state.System != system || !state.Playing || currentTime + CLOCK_EPSILON < state.LastTime && !loopWrapped)
                {
                    state = new SourceState { System = system, Playing = true };
                }

                state.LastTime = currentTime;
                state.Accumulated += scaledDelta;
                int steps = Mathf.Min(MAX_STEPS, Mathf.FloorToInt(state.Accumulated / SOURCE_STEP));
                if (steps == 0)
                {
                    continue;
                }

                state.Accumulated %= SOURCE_STEP;
                for (int step = 0; step < steps; step++)
                {
                    state.Elapsed += SOURCE_STEP;
                    if (source.TimeLimit > 0f && state.Elapsed > source.TimeLimit + CLOCK_EPSILON || !main.loop && (state.Elapsed > main.duration + CLOCK_EPSILON || currentTime >= main.duration))
                    {
                        continue;
                    }

                    int count = SampleCount(source.Rate, UnityEngine.Random.value);
                    int capacity = Mathf.Max(0, main.maxParticles - system.particleCount);
                    if (count > 0 && capacity > 0)
                    {
                        system.Emit(Mathf.Min(count, capacity));
                    }
                }
            }
        }

        public void ResetSources()
        {
            if (_states != null)
            {
                Array.Clear(_states, 0, _states.Length);
            }
        }

        private void OnEnable()
        {
            ResetSources();
            _visual = GetComponent<AnomalyVisual>();
            if (_visual != null)
            {
                _visual.Hidden += ResetSources;
            }
        }

        private void OnDisable()
        {
            if (_visual != null)
            {
                _visual.Hidden -= ResetSources;
            }

            _visual = null;
            ResetSources();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void EnsureStates()
        {
            if (_states == null || _states.Length != _sources.Count)
            {
                _states = new SourceState[_sources.Count];
            }
        }
    }
}
