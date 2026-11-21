using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [Serializable]
    public sealed class AnomalyParticleChildLink
    {
        [SerializeField] private ParticleSystem _template;
        [SerializeField] private bool _followParent;
        [SerializeField] private bool _rewind;

        public ParticleSystem Template => _template;
        public bool FollowParent => _followParent;
        public bool Rewind => _rewind;

        public AnomalyParticleChildLink(ParticleSystem template, bool followParent, bool rewind = false)
        {
            _template = template;
            _followParent = followParent;
            _rewind = rewind;
        }
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class AnomalyParticleChildEmitters : MonoBehaviour
    {
        private const float AGE_EPSILON = 0.00001f;

        private sealed class ParentState
        {
            public Vector3 Position { get; set; }
            public Vector3 Velocity { get; set; }
            public int Frame { get; set; }
            public float Age { get; set; }
            public float StartLifetime { get; set; }
        }

        private sealed class ChildState
        {
            public ParticleSystem System { get; set; }
            public uint Seed { get; set; }
            public int Link { get; set; }
            public bool Following { get; set; }
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; }
            public Vector3 Offset { get; set; }
        }

        [SerializeField] private ParticleSystem _source;
        [SerializeField] private AnomalyParticleChildLink[] _links;

        private readonly Dictionary<uint, ParentState> _parents = new Dictionary<uint, ParentState>();
        private readonly List<uint> _expired = new List<uint>();
        private readonly List<ChildState> _children = new List<ChildState>();
        private readonly List<Stack<ParticleSystem>> _pools = new List<Stack<ParticleSystem>>();
        private ParticleSystem.Particle[] _particles = Array.Empty<ParticleSystem.Particle>();
        private Transform _poolRoot;
        private AnomalyVisual _visual;
        private AnomalyParticleDynamics _dynamics;
        private int _frame;

        public ParticleSystem Source => _source;
        public int ActiveChildCount => _children.Count;

        public void Configure(ParticleSystem source, AnomalyParticleChildLink[] links)
        {
            Clear();
            DisposePools();
            _source = source;
            _links = links;
            Initialize();
        }

        public void Clear()
        {
            foreach (ChildState child in _children)
            {
                Release(child);
            }
            _children.Clear();
            _parents.Clear();
        }

        private void OnEnable()
        {
            Initialize();
            _visual = GetComponentInParent<AnomalyVisual>();
            if (_visual != null)
            {
                _visual.Hidden += Clear;
            }
        }

        private void OnDisable()
        {
            if (_visual != null)
            {
                _visual.Hidden -= Clear;
            }
            _visual = null;
            Clear();
        }

        private void OnDestroy()
        {
            if (_poolRoot != null && _poolRoot.parent != transform)
            {
                DisposePools();
            }
        }

        private void Initialize()
        {
            if (_source == null)
            {
                _source = GetComponent<ParticleSystem>();
            }
            _dynamics = _source != null ? _source.GetComponent<AnomalyParticleDynamics>() : null;
            int count = _links != null ? _links.Length : 0;
            while (_pools.Count < count)
            {
                _pools.Add(new Stack<ParticleSystem>());
            }
        }

        private void LateUpdate()
        {
            if (_source == null || _links == null || _links.Length == 0)
            {
                return;
            }
            if (AnomalyVisualQuality.IsAmbientSuspended(_source))
            {
                UpdateChildren();
                return;
            }
            int maximum = Mathf.Max(1, _source.main.maxParticles);
            if (_particles.Length < maximum)
            {
                _particles = new ParticleSystem.Particle[maximum];
            }
            int count = _source.GetParticles(_particles);
            _frame++;
            for (int index = 0; index < count; index++)
            {
                ParticleSystem.Particle particle = _particles[index];
                Vector3 position = WorldPosition(particle.position);
                if (!_parents.TryGetValue(particle.randomSeed, out ParentState state) || IsNewBirth(particle, state))
                {
                    DetachChildren(particle.randomSeed);
                    state = new ParentState { Position = position, Velocity = ParentVelocity(particle, position, null) };
                    _parents[particle.randomSeed] = state;
                    for (int link = 0; link < _links.Length; link++)
                    {
                        Spawn(particle.randomSeed, link, state);
                    }
                }
                else
                {
                    state.Velocity = ParentVelocity(particle, position, state);
                    state.Position = position;
                }
                state.Frame = _frame;
                state.Age = particle.startLifetime - particle.remainingLifetime;
                state.StartLifetime = particle.startLifetime;
            }
            _expired.Clear();
            foreach (KeyValuePair<uint, ParentState> item in _parents)
            {
                if (item.Value.Frame != _frame)
                {
                    _expired.Add(item.Key);
                }
            }
            foreach (uint seed in _expired)
            {
                _parents.Remove(seed);
            }
            UpdateChildren();
        }

        private static bool IsNewBirth(ParticleSystem.Particle particle, ParentState state)
        {
            return particle.startLifetime != state.StartLifetime || particle.startLifetime - particle.remainingLifetime + AGE_EPSILON < state.Age;
        }

        private Vector3 ParentVelocity(ParticleSystem.Particle particle, Vector3 position, ParentState state)
        {
            if (_dynamics != null && _dynamics.TryGetParticleVelocity(particle.randomSeed, out Vector3 velocity))
            {
                return velocity;
            }
            if (state == null)
            {
                return WorldDirection(particle.totalVelocity);
            }
            return Time.deltaTime > 0f ? (position - state.Position) / Time.deltaTime : Vector3.zero;
        }

        private void DetachChildren(uint seed)
        {
            foreach (ChildState child in _children)
            {
                if (child.Seed == seed && child.Following)
                {
                    child.Following = false;
                    if (child.System != null)
                    {
                        child.System.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
            }
        }

        private void DisposePools()
        {
            _pools.Clear();
            if (_poolRoot == null)
            {
                return;
            }
            GameObject root = _poolRoot.gameObject;
            _poolRoot = null;
            if (Application.isPlaying)
            {
                Destroy(root);
            }
            else
            {
                DestroyImmediate(root);
            }
        }

        private void Spawn(uint seed, int linkIndex, ParentState parent)
        {
            if (_children.Count >= AnomalyVisualQuality.ChildLimit || !AnomalyVisualQuality.CanDraw(parent.Position)) return;
            AnomalyParticleChildLink link = _links[linkIndex];
            if (link == null || link.Template == null)
            {
                return;
            }
            ParticleSystem child = Acquire(linkIndex, link.Template);
            Quaternion rotation = _source.transform.rotation;
            Vector3 offset = rotation * link.Template.transform.localPosition;
            var state = new ChildState { System = child, Seed = seed, Link = linkIndex, Following = link.FollowParent, Position = parent.Position + offset, Rotation = rotation * link.Template.transform.localRotation, Offset = offset };
            child.transform.SetPositionAndRotation(state.Position, state.Rotation);
            var main = child.main;
            main.emitterVelocity = parent.Velocity;
            child.gameObject.SetActive(true);
            child.Play(true);
            _children.Add(state);
        }

        private ParticleSystem Acquire(int linkIndex, ParticleSystem template)
        {
            while (_pools[linkIndex].Count > 0)
            {
                ParticleSystem cached = _pools[linkIndex].Pop();
                if (cached != null)
                {
                    return cached;
                }
            }
            if (_poolRoot == null)
            {
                var root = new GameObject(name + " Child Emitters");
                _poolRoot = root.transform;
                Transform parent = _visual != null ? _visual.transform : transform;
                _poolRoot.SetParent(parent, false);
            }
            ParticleSystem child = Instantiate(template, _poolRoot);
            child.name = template.name + " Instance";
            child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (Application.isPlaying)
            {
                XRay.Importer.Levels.PunctualShadowDistance.RegisterHierarchy(child);
                AnomalyVisualQuality.RegisterHierarchy(child);
            }
            return child;
        }

        private void UpdateChildren()
        {
            for (int index = _children.Count - 1; index >= 0; index--)
            {
                ChildState child = _children[index];
                if (index >= AnomalyVisualQuality.ChildLimit)
                {
                    Release(child);
                    _children.RemoveAt(index);
                    continue;
                }
                if (child.System == null)
                {
                    _children.RemoveAt(index);
                    continue;
                }
                if (child.Following)
                {
                    if (_parents.TryGetValue(child.Seed, out ParentState parent))
                    {
                        child.Position = parent.Position + child.Offset;
                        var main = child.System.main;
                        main.emitterVelocity = parent.Velocity;
                    }
                    else
                    {
                        child.Following = false;
                        child.System.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
                child.System.transform.SetPositionAndRotation(child.Position, child.Rotation);
                if (child.System.IsAlive(true))
                {
                    continue;
                }
                if (child.Following && _links[child.Link].Rewind)
                {
                    child.System.Play(true);
                    continue;
                }
                Release(child);
                _children.RemoveAt(index);
            }
        }

        private void Release(ChildState child)
        {
            if (child.System == null)
            {
                return;
            }
            child.System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            child.System.gameObject.SetActive(false);
            if (child.Link < _pools.Count && _pools[child.Link].Count < AnomalyVisualQuality.ChildLimit)
            {
                _pools[child.Link].Push(child.System);
            }
            else
            {
                Destroy(child.System.gameObject);
            }
        }

        private Vector3 WorldPosition(Vector3 position)
        {
            var main = _source.main;
            if (main.simulationSpace == ParticleSystemSimulationSpace.World)
            {
                return position;
            }
            Transform space = main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null ? main.customSimulationSpace : _source.transform;
            return space.TransformPoint(position);
        }

        private Vector3 WorldDirection(Vector3 direction)
        {
            var main = _source.main;
            if (main.simulationSpace == ParticleSystemSimulationSpace.World)
            {
                return direction;
            }
            Transform space = main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null ? main.customSimulationSpace : _source.transform;
            return space.TransformVector(direction);
        }
    }
}
