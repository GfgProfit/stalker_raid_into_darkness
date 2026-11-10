using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class AnomalyParticleDynamics : MonoBehaviour
    {
        public const float STEP = 0.033f;
        public const int MAX_STEPS = 3;

        private const float AGE_EPSILON = 0.00001f;

        [SerializeField] private ParticleSystem _system;
        [SerializeField] private XRayParticleDomain _initialVelocity;
        [SerializeField] private float _initialAge;
        [SerializeField] private bool _sourceAllowRotate = true;
        [SerializeField] private AnomalyParticleMotionAction[] _actions;

        private readonly Dictionary<uint, ParticleState> _states = new Dictionary<uint, ParticleState>();
        private readonly List<uint> _expiredSeeds = new List<uint>();

        private ParticleSystem.Particle[] _particles;
        private ParticleState[] _orderedStates;
        private AnomalyParticleMotionAction[] _worldActions;
        private float _accumulated;
        private float _elapsed;
        private int _sampleId;
        private bool _renderVelocity;

        public bool TryGetParticleVelocity(uint seed, out Vector3 velocity)
        {
            if (_states.TryGetValue(seed, out ParticleState state) && state.Alive)
            {
                velocity = state.Velocity;
                return true;
            }
            velocity = Vector3.zero;
            return false;
        }

        public void Configure(ParticleSystem system, XRayParticleDomain initialVelocity, float initialAge, AnomalyParticleMotionAction[] actions, bool sourceAllowRotate = true)
        {
            _system = system;
            _initialVelocity = initialVelocity;
            _initialAge = initialAge;
            _actions = actions ?? Array.Empty<AnomalyParticleMotionAction>();
            _sourceAllowRotate = sourceAllowRotate;
            ResetState();
            DisableNativeMotion();
        }

        public static int AdvanceClock(ref float accumulated, float deltaTime)
        {
            accumulated += Mathf.Max(0f, deltaTime);
            if (accumulated < STEP)
            {
                return 0;
            }
            int steps = Mathf.Min(MAX_STEPS, Mathf.FloorToInt(accumulated / STEP));
            accumulated %= STEP;
            return steps;
        }

        private void OnEnable()
        {
            if (_system == null)
            {
                _system = GetComponent<ParticleSystem>();
            }
            ResetState();
            DisableNativeMotion();
        }

        private void OnDisable()
        {
            ClearRenderVelocity();
            ResetState();
        }

        private void Update()
        {
            ClearRenderVelocity();
        }

        private void LateUpdate()
        {
            if (_system == null || _system.isPaused)
            {
                return;
            }
            int capacity = Mathf.Max(1, _system.main.maxParticles);
            if (_particles == null || _particles.Length < capacity)
            {
                _particles = new ParticleSystem.Particle[capacity];
                _orderedStates = new ParticleState[capacity];
            }
            int count = _system.GetParticles(_particles);
            if (count == 0)
            {
                _states.Clear();
                if (_system.isStopped)
                {
                    _accumulated = 0f;
                    _elapsed = 0f;
                    return;
                }
            }
            ParticleSystem.MainModule main = _system.main;
            float deltaTime = (main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) * main.simulationSpeed;
            int steps = AdvanceClock(ref _accumulated, deltaTime);
            Matrix4x4 pose = transform.localToWorldMatrix;
            int actionCount = _actions != null ? _actions.Length : 0;
            if (_worldActions == null || _worldActions.Length != actionCount)
            {
                _worldActions = new AnomalyParticleMotionAction[actionCount];
            }
            for (int index = 0; index < actionCount; index++)
            {
                _worldActions[index] = _actions[index].ToWorld(pose);
            }
            _sampleId++;
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                for (int index = 0; index < count; index++)
                {
                    ParticleSystem.Particle particle = _particles[index];
                    float observedAge = _initialAge + particle.startLifetime - particle.remainingLifetime;
                    if (_states.TryGetValue(particle.randomSeed, out ParticleState state) == false || observedAge + AGE_EPSILON < state.ObservedAge)
                    {
                        Vector3 velocity = AnomalyParticleDynamicsMath.GenerateVelocity(_initialVelocity, particle.randomSeed, pose, _sourceAllowRotate, out UnityEngine.Random.State birthRandomState);
                        state = new ParticleState(particle.position, velocity, birthRandomState);
                        state.BirthSteps = Mathf.Max(1, Mathf.CeilToInt((particle.startLifetime - particle.remainingLifetime) / STEP));
                        _states[particle.randomSeed] = state;
                    }
                    else
                    {
                        state.BirthSteps = MAX_STEPS;
                    }
                    state.ObservedAge = observedAge;
                    state.SeenAt = _sampleId;
                    _orderedStates[index] = state;
                }
                for (int step = 0; step < steps; step++)
                {
                    _elapsed += STEP;
                    for (int index = 0; index < count; index++)
                    {
                        ParticleState state = _orderedStates[index];
                        if (state.Alive == false || step < steps - state.BirthSteps)
                        {
                            continue;
                        }
                        UnityEngine.Random.state = state.RandomState;
                        Vector3 position = state.Position;
                        Vector3 velocity = state.Velocity;
                        for (int action = 0; action < actionCount; action++)
                        {
                            if (AnomalyParticleDynamicsMath.Apply(ref position, ref velocity, _worldActions[action], STEP, _elapsed) == false)
                            {
                                state.Alive = false;
                                break;
                            }
                        }
                        state.Position = position;
                        state.Velocity = velocity;
                        state.RandomState = UnityEngine.Random.state;
                    }
                }
            }
            finally
            {
                UnityEngine.Random.state = randomState;
            }
            for (int index = 0; index < count; index++)
            {
                ParticleSystem.Particle particle = _particles[index];
                ParticleState state = _orderedStates[index];
                particle.position = state.Position;
                particle.velocity = _renderVelocity ? state.Velocity : Vector3.zero;
                if (state.Alive == false)
                {
                    particle.remainingLifetime = 0f;
                }
                _particles[index] = particle;
            }
            _system.SetParticles(_particles, count);
            _expiredSeeds.Clear();
            foreach (KeyValuePair<uint, ParticleState> pair in _states)
            {
                if (pair.Value.SeenAt != _sampleId || pair.Value.Alive == false)
                {
                    _expiredSeeds.Add(pair.Key);
                }
            }
            foreach (uint seed in _expiredSeeds)
            {
                _states.Remove(seed);
            }
        }

        private void DisableNativeMotion()
        {
            if (_system == null)
            {
                return;
            }
            ParticleSystem.MainModule main = _system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            ParticleSystem.VelocityOverLifetimeModule velocity = _system.velocityOverLifetime;
            velocity.enabled = false;
            ParticleSystem.ForceOverLifetimeModule force = _system.forceOverLifetime;
            force.enabled = false;
            ParticleSystem.LimitVelocityOverLifetimeModule limit = _system.limitVelocityOverLifetime;
            limit.enabled = false;
            ParticleSystem.InheritVelocityModule inherit = _system.inheritVelocity;
            inherit.enabled = false;
            ParticleSystem.NoiseModule noise = _system.noise;
            noise.enabled = false;
            ParticleSystem.ExternalForcesModule external = _system.externalForces;
            external.enabled = false;
            main.gravityModifier = 0f;
            ParticleSystemRenderer renderer = _system.GetComponent<ParticleSystemRenderer>();
            _renderVelocity = renderer != null && renderer.renderMode == ParticleSystemRenderMode.Stretch;
        }

        private void ClearRenderVelocity()
        {
            if (_renderVelocity == false || _system == null || _system.particleCount == 0)
            {
                return;
            }
            int capacity = Mathf.Max(1, _system.main.maxParticles);
            if (_particles == null || _particles.Length < capacity)
            {
                _particles = new ParticleSystem.Particle[capacity];
                _orderedStates = new ParticleState[capacity];
            }
            int count = _system.GetParticles(_particles);
            for (int index = 0; index < count; index++)
            {
                ParticleSystem.Particle particle = _particles[index];
                particle.velocity = Vector3.zero;
                _particles[index] = particle;
            }
            _system.SetParticles(_particles, count);
        }

        private void ResetState()
        {
            _states.Clear();
            _expiredSeeds.Clear();
            _accumulated = 0f;
            _elapsed = 0f;
        }

        private sealed class ParticleState
        {
            public Vector3 Position { get; set; }
            public Vector3 Velocity { get; set; }
            public UnityEngine.Random.State RandomState { get; set; }
            public float ObservedAge { get; set; }
            public int SeenAt { get; set; }
            public int BirthSteps { get; set; }
            public bool Alive { get; set; }

            public ParticleState(Vector3 position, Vector3 velocity, UnityEngine.Random.State randomState)
            {
                Position = position;
                Velocity = velocity;
                RandomState = randomState;
                Alive = true;
            }
        }
    }
}
